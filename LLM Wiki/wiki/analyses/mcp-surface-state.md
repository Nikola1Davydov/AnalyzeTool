---
type: analysis
updated: 2026-09-02
status: current
sources: [../sources/github-issues.md]
---

# Состояние MCP-поверхности после полевого теста 1.5

Живую сессию Revit с настоящим немецким проектом прогнали против релиз-кандидата 1.5, и
она дала девять заведённых дефектов плюс одно наблюдение, объясняющее большинство из
них. Здесь синтез; сырые находки — [#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97)–[#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105).

## Находка, которая переосмысляет остальные

[#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105) — не дефект. С
включённым исполнением C# агент по умолчанию тянулся к `ExecuteRevitCode`: примерно
**двадцать** вызовов против горстки обращений ко всему остальному. Это рационально —
он делает всё, что делают специализированные команды чтения, плюс произвольную логику,
в одном вызове и без схемы, которую надо изучать.

Нерационально следствие. `GetElements` была **полностью сломана всю сессию**, и агент
её обошёл, вместо того чтобы на ней застрять. Обобщение:

> Специализированные команды — это те, что несут схемы, описания, валидацию и флаги
> безопасности. И это ровно те, которые агент пропускает, когда рядом лежит
> универсальный запасной люк. Самая тщательно спроектированная поверхность получает
> меньше всего проверки, а её поломка всплывает поздно.

Дешёвое противодействие — тест, который реально зовёт каждую MCP-команду. Структурное —
сделать специализированный путь *привлекательнее*, чем и занимается плотный инвентарь;
см. [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md).

## Второй полевой тест, 2026-09-02: причина #98 найдена — и она не в команде

[`../sources/field-test-2026-09-02.md`](../sources/field-test-2026-09-02.md) повторил падение
`GetElements` на всех вводах и добавил улику, которой не было в августе: ошибка приходит
голым «Tool execution failed» **без тела**, тогда как всякая другая ошибка у того же клиента
показывается с кодом (`[command_failed] …`). Голый текст — это провал на уровне протокола,
не ответ сервера. Значит exe получил от бриджа *успешный* ответ, и отвергнут он был дальше.

Дальше — валидация. `GetElements` объявляет `OutputType`, exe публикует его как
`outputSchema` и кладёт ответ в `structuredContent`; клиент по спеке обязан сверить одно с
другим. Схема из `AIJsonUtilities.CreateJsonSchema` помечает **все** свойства `required`,
включая `long?`/`string?`; хост же пишет их с `NullValueHandling.Ignore` — то есть при
`null` поля нет вовсе. Пробник на записи `ElementsResult` показал `required: [… "didYouMean"]`
при ответе без `didYouMean`. Клиент отверг ответ целиком — три недели «падает на любом вводе»
при команде, которая ни разу не падала (тот же запрос из WebView и через `ExecuteRevitCode`
работал: там никто не валидирует).

Починено 2026-09-02 в `CommandDispatcher.BuildSchema`: nullable-свойства исключаются из
`required` рекурсивно, для всех команд — та же ловушка ждала каждую команду с необязательным
полем. **Подтверждено вживую в тот же день**: через тот же клиент claude.ai на той же модели
`GetElements` вернул шесть стен; [#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98) закрыт.

Тем же днём закрыт по коду [#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97): бридж доходит до корневого исключения,
отдаёт его тип и сообщение, пишет всю цепочку с именем команды и payload в лог. Улика,
что это было нужно: утренний лог хоста содержал четыре строки «GetElements invoked via
mcp» и **ни одной** об ошибке. Проверено нарочно брошенным исключением: клиент получил
`[command_failed] InvalidOperationException: …`, в логе строка `[ERR] MCP: command
ExecuteRevitCode failed — …` с payload. [#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97) закрыт.

Урок для порядка ниже: #97 всё равно был прав как первый пункт — но причину #98 нашла не
диагностика, а *форма* ошибки у клиента. Голое «failed» без кода — само по себе сигнал: смотреть
на путь после бриджа.

## Дефекты и почему они в таком порядке

**[#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97) первым, всегда.** Одна
строка в `McpBridgeServer.cs` возвращает `ex.Message` и ничего не логирует. Поскольку
исключение маршалится с потока Revit, внешнее исключение регулярно оказывается обёрткой
— `AggregateException` даёт «One or more errors occurred.» — а предложение о том, что
на самом деле сломалось, живёт в `InnerException` и здесь выбрасывается. В Serilog тоже
ничего не попадает, поэтому после этой строки **информации не существует нигде**,
включая машину, где это случилось. Любой другой баг MCP недиагностируем, пока это не
починено: изоляция одного стоит сейчас целой сессии Revit.

**Проверено по коду 2026-08-31: дефект на месте**, ровно там, где указан —
`McpBridgeServer.cs:276`, `return Err(id, McpWire.Codes.CommandFailed, ex.Message);`,
без обхода `InnerException` и без записи в лог.

**[#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98) — `GetElements` падает
на любом вводе.** Команда, к которой агент тянется первой, чтобы посмотреть на модель,
не работает совсем. Падали все комбинации: `builtInCategory`, локализованная
`category`, с `limit` и без, с `parameterNames` и без. Исключено тем, что другие
команды в той же сессии работали: разрешение категорий, локаль, документ, поток Revit.
Заблокировано на [#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97) — «оно
падает» это буквально вся существующая информация.

**[#100](https://github.com/Nikola1Davydov/AnalyzeTool/issues/100) — цикл авторства
нельзя проверить.** *Сделано 2026-09-02 (штамп каталога на каждом ответе + фоновый опрос +
`list_changed`; см. [`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md)),
проверено вживую stdio-клиентом против задеплоенного exe: уведомление пришло раньше ответа на сам `ReloadExtensions`, новый инструмент — в следующем `tools/list`. Закрыт. Оговорка: реагирует ли клиент на уведомление — дело клиента; отложенный индекс инструментов Claude Code в той сессии не обновился.* После `SaveAsCommand` новая команда зарегистрировалась,
загрузилась и стала вызываемой с ленты и из WebView за секунды — лог это доказывает.
Устарел только список инструментов у AI-клиента: он получен до того, как команда
появилась, и ничто не велело его перезапросить. То есть «написать → сохранить →
запустить», цикл из гайда по авторству, ломается на последнем шаге. Чинится через
`notifications/tools/list_changed`;
[#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) предполагает, что
подсказки `ttlMs` / `cacheScope` из `2026-07-28` могут выйти ещё дешевле.

**[#101](https://github.com/Nikola1Davydov/AnalyzeTool/issues/101) — `using` в форме
голого тела — закрыт 2026-09-02.** Тело вклеивалось в метод, поэтому `using Autodesk.Revit.DB;`
разбирался как *оператор* `using`, и вызывающий получал двадцать ошибок компилятора, ни
одна из которых не называла причину. Сделано больше, чем просил issue: ведущие директивы
не отвергаются, а поднимаются над сгенерированным классом
(`RoslynScriptCompiler.LiftLeadingUsings`), каждая оставляет пустую строку, чтобы `#line`
продолжал указывать на строки автора. Тесты: пять на ярусе 1 (чистая функция и форма
обёртки), два на ярусе 3 (настоящий Roslyn против живого `RevitAPI`). Остаток из issue —
`Min`/`Max` как LINQ и язык диагностик — записан в гайд одной фразой, не чинился.

**[#102](https://github.com/Nikola1Davydov/AnalyzeTool/issues/102) — закрыт 2026-09-02**,
[#104](https://github.com/Nikola1Davydov/AnalyzeTool/issues/104),
[#99](https://github.com/Nikola1Davydov/AnalyzeTool/issues/99) — это задача долгих
вызовов с трёх сторон; см.
[`../concepts/long-running-calls.md`](../concepts/long-running-calls.md). Для #102 ответ
оказался на стороне страницы: хост принимает сообщения WebView на UI-потоке Revit, и пока
тот занят, опрос не доходит даже до `CommandQueue` — поэтому чинить нечего в хосте.
`RevitBridge.ts` теперь помнит время отправки каждого вызова (`oldestPendingAge`), а
`RevitBusyBar.vue` по своему таймеру показывает янтарное «Revit is busy … this window will
answer when it finishes» после трёх секунд без ответа.

**[#103](https://github.com/Nikola1Davydov/AnalyzeTool/issues/103)** —
`RemoveDevExtension` оставляет неудаляемые папки `.old` на путях под синхронизацией
OneDrive. Ничего не ломается; общая папка команды медленно заполняется мусором там, где
его видят коллеги. Коротко ретраить, потом подметать при следующем скане.

## Чего не хватает, а не сломано

Три чек-листа, у каждого свой внутренний порядок:

- **[#83](https://github.com/Nikola1Davydov/AnalyzeTool/issues/83)** — размещение и
  качество данных. P0 про корректность: программное размещение с явными `units` и
  `coordinateSystem`, геометрия элемента (кривая расположения стены и какая сторона
  наружу), разделение типов и экземпляров, `CategoryNotFound` с `didYouMean` вместо
  голого `[]`.
  **Проверено по коду 2026-08-31: половина P0 про запросы уже в типах.** `ElementQuery`
  (`Elements/Infrastructure/ElementSummary.cs`) несёт `ElementKind`, `BuiltInCategory`,
  `FamilyNameContains` и `TypeNameContains`; `ElementsResult` несёт `error` и
  `didYouMean`; `ElementSummary` — `FamilyId` и `FamilyName`. Не закрыто из P0:
  размещение и геометрия элемента. Наличие полей не означает, что поведение верно —
  [#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98) как раз о том, что
  команда падает целиком.
  P2 — настоящий скачок попадания: **вызовы уровня намерения** вроде
  `PlaceOnWall(wallId, typeId, Distribution.Evenly, count)`, потому что LLM плохи в
  вычислении координат и хороши в формулировке намерения.
- **[#84](https://github.com/Nikola1Davydov/AnalyzeTool/issues/84)** — эргономика
  сервера. `GetModelOverview` **отгружен** (комментарий от 14.08.2026, подтверждено по
  коду: `Elements/Features/GetModelOverview.cs`); остаётся рекомендовать его первым
  вызовом в описаниях. Ограждения на разрушительных командах выделены в
  [#106](https://github.com/Nikola1Davydov/AnalyzeTool/issues/106); описания инструментов как часть контракта данных; и набор
  eval'ов из реальных сессий с метриками попадания с первой попытки, **вызовов на
  задачу** и токенов на задачу.
- **[#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85)** — персистентность
  на документ: журнал операций в JSONL только на дозапись (сознательно не RAG и
  сознательно пока не SQLite), конвенции проекта с различением `inferred` и
  `userConfirmed`, и явный отказ хранить историю разговора.

## Дефект, которого не было в списке: агент слеп

Заведён [#129](https://github.com/Nikola1Davydov/AnalyzeTool/issues/129). **Закрыт 2026-09-01 без починки:** `GetFamilyPreview` исчез
вместе со всем слайсом семейств — Family Manager уехал в расширение (63a1992). Раздел ниже
остаётся как запись о принципе: любая будущая команда, отдающая картинку, либо шлёт блок
изображения MCP, либо помечается `HiddenFromMcp` с первого дня.

Найден в комментарии к [#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80)
(25.08.2026) и **подтверждён по коду 2026-08-31**.

`GetFamilyPreview` рендерит PNG-миниатюру и возвращает её строкой
`"data:image/png;base64," + Convert.ToBase64String(...)`
(`AnalyseTool.FamilyManager/extension/Features/GetFamilyPreview.cs`, после выноса). Команда **не помечена** `HiddenFromMcp`,
то есть выставлена агенту. А `AnalyseTool.Mcp/Program.cs` умеет собирать только
`TextContentBlock` (строки 133 и 149) — блока изображения там нет.

Итог: агент получает килобайты base64 как текст, потратив на них контекст, и **увидеть их
не может**. Хуже, чем отсутствие команды.

Смежная мысль из того же комментария, шире дефекта: в проекте нигде нет `ExportImage`.
Агент работает по JSON в геометрическом домене, тогда как план этажа, отданный зрячей
модели, содержит больше пригодного к действию, чем два десятка вызовов `GetElements`. Для
ревью вида и для размещения это меняет класс решаемых задач.

## Тесты есть на ветке — на `dev` их нет, и никто не запускает

Заведено [#128](https://github.com/Nikola1Davydov/AnalyzeTool/issues/128).

К [#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105) («тест, который реально
зовёт каждую MCP-команду»). Комментарий к
[#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70) от 01.08.2026 говорил, что
размещать такие тесты негде: `AnalyseTool.RevitTests` — один заглушку UnitTest1 (удалена 2026-09-02, проект пересоздан из revit-tunit), ссылающийся на
`AnalyseTool.App`, то есть тянущий Revit. **Это устарело.**

**Поправка 2026-09-02:** на `dev` проектов `AnalyseTool.Core.Tests` и `AnalyseTool.Tools.Tests`
нет — `git ls-files src/AnalyseTool.*.Tests` пуст, на диске только сиротские `bin/obj`. Они живут
на ветке `claude/pipelines-plan-f8jrgf` вместе с целью `RunTests` (b1ac574): Revit-free по
построению, ровно тот дом, которого не хватало, — но его ещё надо перенести.

Но настоящая проблема оказалась другой и хуже, и её называет
[`../sources/pipeline-design-doc.md`](../sources/pipeline-design-doc.md): **CI не запускает
тесты вообще.** Проверено: `.github/workflows/ci.yml` делегирует в NUKE-цель `Ci`, а та
зависит от `CompileCi`, `TestSdkPackage`, `TestExtensionTemplate` и `CheckCoreResources` —
сборка трёх лет Revit, проверка пакета SDK и шаблона расширения. Ни `dotnet test`, ни
`DotNetTest` не встречаются нигде в `src/build/`.

То есть правила привязок движка — то, на чём стоит всё остальное, — не проверялись всё это
время. Формулировка оттуда точная: **гардрейл, который никто не запускает, не гардрейл.**

## Вопрос протокола, который гейтил часть этого

*[#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) закрыт 2026-09-02 вечером: гейт снят, каждая строка его таблицы ушла в свой issue
(#110, #111, #106, #71) или уже сделана; sampling решён в #133 в пользу provider API.*

[#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) отслеживал, что
меняет спека MCP `2026-07-28`. Два пункта бэклога становятся устаревшими (sampling и
уведомления логирования — оба уже сделаны рекомендованным способом), один получает
более дешёвый ответ, и приходит Tasks — та возможность, которую три issue обходили без
имени. **Гейт-вопрос отвечен 2026-09-02 — дважды за день.** Утром: закреплённый
`ModelContextProtocol` 1.3.0 спеку `2026-07-28` не знал (ревизии в его
`ModelContextProtocol.Core.xml` — до 2025-11-25, ни `requestState`, ни `ttlMs`, ни
`cacheScope`), но уже нёс `tasks/get|cancel|list|result`, `McpTaskStatus.InputRequired`,
`ElicitRequestParams`, `ImageContentBlock`, `ToolListChangedNotification`. Вечером пакет
поднят до **2.2.0** (dc99dbc, `src/Directory.Packages.props:36`), и его xml
(`%USERPROFILE%\.nuget\packages\modelcontextprotocol.core.2.0\lib
et10.0\`) ссылается на
ревизии 2024-11-05, 2025-01-12, 2025-03-26, 2025-06-18, 2025-11-25 **и 2026-07-28**, а
`RequestState`, `ttlMs`, `CacheScope` в нём есть. Итог: **гейта больше нет.** На уровне
API разблокирована вся ветка — [#100](https://github.com/Nikola1Davydov/AnalyzeTool/issues/100) (list_changed, и дешёвый вариант через
`ttlMs`), [#108](https://github.com/Nikola1Davydov/AnalyzeTool/issues/108)–[#110](https://github.com/Nikola1Davydov/AnalyzeTool/issues/110) (прогресс, отмена, Tasks), elicitation для
[#106](https://github.com/Nikola1Davydov/AnalyzeTool/issues/106), [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) и [#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111). Живой запуск на 2.2.0 проверен 2026-09-02:
exe работал в сессиях весь день и под stdio-тестами яруса 2 (`src/AnalyseTool.Tests/Mcp/`).

Уточнение объёма к [#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111): его
собственный комментарий признаёт, что цитата в теле issue устарела. `GetElements` уже
завёрнут — `OutputType = typeof(ElementsResult)`, объект, — так что миграция, о которой
там говорится, для примера из issue уже произошла. **Проверено по коду 2026-08-31:**
голым массивом возвращает только `GetCategoriesInRevit` (`OutputType = typeof(List<string>)`).
**Поправка 2026-09-02, при сужении issue:** та проверка смотрела не все слайсы — открытых для MCP
команд с массивом в корне три: `GetCategoriesInRevit`, `GetCadImports` (`List<ImportInfo>`),
`GetWarningsInRevit` (`List<WarningInRevitModel>`); `GetDataByCategoryName` тоже список, но
`HiddenFromMcp`. SDK 2.2.0 разрешает не-объектный корень `Tool.OutputSchema` явно (его xml),
так что #111 теперь — удаление ограничения в `src/AnalyseTool.Mcp/Program.cs` и разворот двух
тестов яруса 2, без гейта.

Единственный пробел против официального чек-листа безопасности — ограничение частоты
([#112](https://github.com/Nikola1Davydov/AnalyzeTool/issues/112)); **проверено по коду
2026-08-31: в `AnalyseTool.Mcp.Bridge` и `AnalyseTool.Mcp` по-прежнему нет ни
`SemaphoreSlim`, ни какого-либо троттлинга.** Его реальный триггер назван честно: не атакующий, а **агент в цикле ретраев** — а
[#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97) вместе с
[#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98) это ровно та обстановка,
которая его порождает.

## Порядок, который отсюда следует

[#120](https://github.com/Nikola1Davydov/AnalyzeTool/issues/120) превращает всё это из
инженерной гигиены в блокеры выхода: если бесплатная часть — витрина, то пришедшего
сейчас встречают сломанный базовый инструмент чтения, неотличимые ошибки, протухший
список инструментов и замерзающий UI.

1. [#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97) — делает всё
   остальное диагностируемым
2. [#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98) — после этого
   пятиминутный баг вместо угадайки
3. [#100](https://github.com/Nikola1Davydov/AnalyzeTool/issues/100),
   [#102](https://github.com/Nikola1Davydov/AnalyzeTool/issues/102) — цикл авторства и UI
   (оба закрыты 2026-09-02)
4. ~~вопрос про SDK из [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107)~~ снят,
   затем прогресс ([#108](https://github.com/Nikola1Davydov/AnalyzeTool/issues/108)),
   отмена ([#109](https://github.com/Nikola1Davydov/AnalyzeTool/issues/109)), Tasks
   ([#110](https://github.com/Nikola1Davydov/AnalyzeTool/issues/110))
5. ~~поля читаемости ([#113](https://github.com/Nikola1Davydov/AnalyzeTool/issues/113))~~ — закрыт 2026-09-02:
   `builtInCategory`/`levelId` на элементе, `spec`/`unit` у параметров, `categories` в обзоре;
   ресурсная таблица категорий и метрика «вызовов на задачу» остались в #84

## Связанное

- [`../concepts/long-running-calls.md`](../concepts/long-running-calls.md) · [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md)
- [`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md) · [`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md)
