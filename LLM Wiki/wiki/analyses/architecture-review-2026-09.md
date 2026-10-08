---
type: analysis
updated: 2026-10-08
status: current
sources: [../sources/github-issues.md]
---

# Ревью архитектуры 2026-09-23 — зазор между C#-типом и всем остальным

Разбор [#141](https://github.com/Nikola1Davydov/AnalyzeTool/issues/141) и его шестнадцати
sub-issue ([#142](https://github.com/Nikola1Davydov/AnalyzeTool/issues/142)–[#158](https://github.com/Nikola1Davydov/AnalyzeTool/issues/158)):
путь `AnalyseTool.Sdk` → `CommandQueue` → `CommandDispatcher` → команды `Tools` → `clientapp`,
сравнённый с паттерном «plain handler» (класс с одним методом, вызывается напрямую).

## Вывод одной фразой

Концептуально платформа уже устроена как plain handler: **команда — тонкая оболочка, сервис —
функция от `Document`** (`SetDataToParameters` → `ParameterWriteService`). Разница в том, что у
plain handler контракт держит компилятор, а здесь — строковое имя, `object?`, атрибут и, на
фронте, рукописные копии. Почти всё, чего не хватает, живёт **в этом зазоре** — и это тот же класс,
что [#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98): схема и провод расходились, и
никто не сверял.

Что ревью явно оставляет как есть: `CommandQueue` как единая дверь всех транспортов, отсутствие
`Document` в `IRevitContext` (модель только через `RunInRevitAsync`), поиск команд рефлексией,
сервисы как функции от `Document` с тестами яруса 3.

## Что из этого касается агента

Большая часть пунктов — гигиена C# и фронта. Но четыре прямо меняют то, что видит или делает агент
через MCP, и ради них страница здесь.

**Отмена, которая врёт — [#142](https://github.com/Nikola1Davydov/AnalyzeTool/issues/142).**
`RevitTaskHub.EnqueueAsync` не принимает `CancellationToken` (`src/AnalyseTool.Core/Common/Dispatch/RevitTaskHub.cs`,
проверено 2026-10-08). Работа, поставленная, пока Revit не простаивает (диалог, режим
редактирования), выполнится **позже, уже после** ответа «Cancelled» — включая запись
`SetDataToParameters`. Для агента это хуже, чем отсутствие отмены: `CancelJob` и
`notifications/cancelled` ([`../concepts/long-running-calls.md`](../concepts/long-running-calls.md))
сообщают об остановке, а модель меняется. Раньше это было записано как «отмена доходит не везде»
([`../entities/command-queue.md`](../entities/command-queue.md)); ревью уточняет, что не дошедшей
может оказаться и ещё **не начатая** работа, и даёт план: токен в `RunInRevitAsync` (аддитивно в
Sdk) и пропуск отменённых элементов в `Execute`.

**Нет документа → `NullReferenceException` — [#143](https://github.com/Nikola1Davydov/AnalyzeTool/issues/143).**
В `AnalyseTool.Tools` 13 мест читают `app.ActiveUIDocument.Document` (подсчёт 2026-10-08; в issue —
15), null проверяет одно. Агент, позвавший команду при закрытом проекте, получает трассу вместо
«No active document». Предложено `IRevitContext.RunInDocumentAsync` — заодно подталкивает авторов
к форме «сервис = функция от `Document`».

**Ошибка, спрятанная в результате — [#146](https://github.com/Nikola1Davydov/AnalyzeTool/issues/146).**
Три стиля сразу: исключение, поле `{ error, didYouMean }`, и catch-all в `Ollama*`, который
возвращает сбой как успешный результат — для MCP это ответ без `isError`. Сегодня агента это не
задевает: все `Ollama*` помечены `HiddenFromMcp` ([`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md)).
Но стиль заразен — видимая команда, написанная по этому образцу (а агенты пишут команды, глядя на
существующие), спрячет свой провал, и агент прочитает его как ответ. Правило ревью: *сбой — исключение, ожидаемый доменный исход (не найдено,
подсказки) — поле результата*; в Sdk появляется `RevitCommandException(message, hint)`, и
транспорты отдают его без стектрейса. Это продолжение линии `[code] message` с подсказками из
[`mcp-surface-state.md`](mcp-surface-state.md).

**Гонка между чтением и записью — [#144](https://github.com/Nikola1Davydov/AnalyzeTool/issues/144).**
`RevitTaskHub` сериализует отдельные `RunInRevitAsync`, но не команду целиком. Команда
«прочитать → долго думать вне Revit → записать» может записать по устаревшим данным, если между
шагами прошла `Destructive`-команда из другого транспорта — например, второй параллельный вызов
того же агента. Предложен reader/writer lock в `CommandQueue`: `Destructive` берёт эксклюзивную.
Приоритет низкий, пока таких команд нет — но первая же «AI между чтением и записью»
([#79](https://github.com/Nikola1Davydov/AnalyzeTool/issues/79), [#126](https://github.com/Nikola1Davydov/AnalyzeTool/issues/126))
его поднимет. Связь с неатомарностью конвейера — [`../entities/command-queue.md`](../entities/command-queue.md).

## Контракт: один C#-тип как единственный источник правды

Главная мысль ревью — свести три описания команды (атрибут `InputType`/`OutputType`,
`ctx.Payload.As<T>()` в теле, рукописный TS на фронте) к одному:

| Шаг | Issue | Что даёт |
| --- | --- | --- |
| `RevitTask<TRequest, TResult>` в Sdk 1.3, аддитивно | [#145](https://github.com/Nikola1Davydov/AnalyzeTool/issues/145) | `InputType`/`OutputType` выводятся из generic-аргументов — схема перестаёт быть отдельным утверждением |
| `ctx.Progress` вместо setter-инъекции `IProgressAware` | [#147](https://github.com/Nikola1Davydov/AnalyzeTool/issues/147) | прогресс живёт в контексте вызова; `IProgressAware` → `[Obsolete]` |
| один сериализатор для провода и схемы | [#149](https://github.com/Nikola1Davydov/AnalyzeTool/issues/149) | корень #98: провод пишет Newtonsoft, схему строит STJ, который `[JsonProperty("id")]` не видит |
| Roslyn-анализатор в пакете Sdk вместо `Check-Schemas.ps1` | [#150](https://github.com/Nikola1Davydov/AnalyzeTool/issues/150) | правила схемы — ошибки в IDE, и **у авторов расширений тоже** |
| `PublicApiAnalyzers` до выпуска 1.3 | [#151](https://github.com/Nikola1Davydov/AnalyzeTool/issues/151) | SemVer Sdk держит сборка, а не дисциплина |
| TS-контракт генерируется из `InputSchemaJson`/`OutputSchemaJson` | [#157](https://github.com/Nikola1Davydov/AnalyzeTool/issues/157) | `invoke<K extends keyof CommandMap>`; CI сверяет «сгенерированное = закоммиченное» |

Для этой вики важна сцепка #145 + #157: та же схема, которую агент читает в `tools/list`, начинает
проверять и Vue. Сейчас агентская сторона защищена `SchemaContractTests`, а фронт — ничем
([`../concepts/command-schema-contract.md`](../concepts/command-schema-contract.md)). Как вводить
1.3, не сломав написанное агентами, — правила [`../concepts/contract-evolution.md`](../concepts/contract-evolution.md):
`IRevitTask` остаётся, новое — рядом.

## Фронт: та же проблема с другой стороны провода

Из комментариев к #141 (F1–F6) и sub-issue [#155](https://github.com/Nikola1Davydov/AnalyzeTool/issues/155),
[#156](https://github.com/Nikola1Davydov/AnalyzeTool/issues/156), [#158](https://github.com/Nikola1Davydov/AnalyzeTool/issues/158):
TypeScript не проверяется вовсе (нет `typescript`/`vue-tsc`, есть только `src/clientapp/jsconfig.json`),
`.ts` не линтуются, CI фронт не собирает. Реестр `Commands` в `src/clientapp/src/RevitBridge.ts`
шесть имён из 24 держит за командами Family Manager, ушедшими из платформы 2026-09-01, — и никто не
заметил: ровно то, что происходит с рукописным дублем. God-компоненты (`ExtensionsView.vue` —
1255 строк) — [#158](https://github.com/Nikola1Davydov/AnalyzeTool/issues/158).

## Остальное — по необходимости

[#148](https://github.com/Nikola1Davydov/AnalyzeTool/issues/148) убрать статическое состояние
(`CoreServices`, `RevitTaskHub.Current`; третий из списка, AiProviderRegistry, удалён 2026-10-08 вместе со встроенным ИИ) и `ActivatorUtilities` в диспетчере;
[#152](https://github.com/Nikola1Davydov/AnalyzeTool/issues/152) разбить god-классы (`RibbonHost` —
1027 строк, `McpBridgeServer` — 660, `Mcp/Program.cs` — 589; при переносе `RibbonHost` — FQN-строки
лаунчера); [#154](https://github.com/Nikola1Davydov/AnalyzeTool/issues/154) `Directory.Build.props`.

**[#153](https://github.com/Nikola1Davydov/AnalyzeTool/issues/153) по сути уже сделан, а issue открыт.**
Он предлагал «когда понадобится» цепочку behaviors вокруг диспетчера. Через два дня после ревью
(коммит e5d9fe5, 2026-09-25) `CommandQueue` стал исполнять через цепочку декораторов
`Logging → Gating → Tracking → Dispatching` (`src/AnalyseTool.Core/Common/Dispatch/CommandPipeline.cs`) —
триггером стало логирование исходов. Решать человеку: закрыть #153 как сделанный или сузить до
того, чего в цепочке ещё нет.

## Порядок, который предлагает ревью

1. Быстрые исправления: #142, #143, #156.
2. Фундамент фронта: #155.
3. Sdk 1.3: #145 + #146 + #147, перед выпуском — #151.
4. Сквозной контракт: #157.
5. Крупное по отдельности: #150, #149, #158.
6. Когда начнёт мешать: #144, #148, #152, #153, #154.

С точки зрения AI-поверхности порядок разумен с одной оговоркой: #142 — не «быстрое исправление
из списка», а единственный пункт, при котором платформа **сообщает агенту неправду** о состоянии
модели. Его место — первым.

## Связанное

- [`../entities/command-queue.md`](../entities/command-queue.md) — очередь, `RevitTaskHub`, цепочка декораторов
- [`../concepts/command-schema-contract.md`](../concepts/command-schema-contract.md) · [`../concepts/contract-evolution.md`](../concepts/contract-evolution.md)
- [`../concepts/long-running-calls.md`](../concepts/long-running-calls.md) — отмена снаружи, которую #142 доводит до потока Revit
- [`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md) — запись после «Cancelled»
- [`backlog-map.md`](backlog-map.md)
