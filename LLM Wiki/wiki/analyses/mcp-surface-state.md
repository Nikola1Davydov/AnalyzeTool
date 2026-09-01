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
нельзя проверить.** После `SaveAsCommand` новая команда зарегистрировалась,
загрузилась и стала вызываемой с ленты и из WebView за секунды — лог это доказывает.
Устарел только список инструментов у AI-клиента: он получен до того, как команда
появилась, и ничто не велело его перезапросить. То есть «написать → сохранить →
запустить», цикл из гайда по авторству, ломается на последнем шаге. Чинится через
`notifications/tools/list_changed`;
[#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) предполагает, что
подсказки `ttlMs` / `cacheScope` из `2026-07-28` могут выйти ещё дешевле.

**[#101](https://github.com/Nikola1Davydov/AnalyzeTool/issues/101) — `using` в форме
голого тела.** Тело вклеивается в метод, поэтому `using Autodesk.Revit.DB;` разбирается
как *оператор* `using`. Вызывающий получает двадцать ошибок компилятора на языке
интерфейса Revit, ни одна из которых не называет причину. Ловить ведущие строки
`using` и отвечать одним предложением.

**[#102](https://github.com/Nikola1Davydov/AnalyzeTool/issues/102),
[#104](https://github.com/Nikola1Davydov/AnalyzeTool/issues/104),
[#99](https://github.com/Nikola1Davydov/AnalyzeTool/issues/99)** — это задача долгих
вызовов с трёх сторон; см.
[`../concepts/long-running-calls.md`](../concepts/long-running-calls.md).

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
размещать такие тесты негде: `AnalyseTool.Test` — один `UnitTest1.cs`, ссылающийся на
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

## Вопрос протокола, который гейтит часть этого

[#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) отслеживает, что
меняет спека MCP `2026-07-28`. Два пункта бэклога становятся устаревшими (sampling и
уведомления логирования — оба уже сделаны рекомендованным способом), один получает
более дешёвый ответ, и приходит Tasks — та возможность, которую три issue обходили без
имени. **Гейт-вопрос отвечен 2026-09-02.** `ModelContextProtocol` 1.3.0, закреплённый в
`src/Directory.Packages.props`, спеку `2026-07-28` **не** поддерживает: в
`ModelContextProtocol.Core.xml` пакета (`%USERPROFILE%\.nuget\packages\modelcontextprotocol.core\1.3.0\lib\net8.0\`)
нет ни `requestState`, ни `ttlMs`, ни `cacheScope`, а ревизии протокола, на которые он
ссылается, — 2024-11-05, 2025-03-26 и 2025-11-25, ни одной 2026 года. Но там уже есть
`tasks/get|cancel|list|result`, `McpTaskStatus.InputRequired`, `ElicitRequestParams`,
`ImageContentBlock` и `ToolListChangedNotification`. Значит на уровне API разблокированы
[#100](https://github.com/Nikola1Davydov/AnalyzeTool/issues/100) (list_changed),
[#108](https://github.com/Nikola1Davydov/AnalyzeTool/issues/108)–[#110](https://github.com/Nikola1Davydov/AnalyzeTool/issues/110)
(прогресс, отмена, Tasks) и форма elicitation для
[#106](https://github.com/Nikola1Davydov/AnalyzeTool/issues/106); ждут только те, кому буквально
нужна `2026-07-28` — [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107)
(ttl/cacheScope) и [#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111).

Уточнение объёма к [#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111): его
собственный комментарий признаёт, что цитата в теле issue устарела. `GetElements` уже
завёрнут — `OutputType = typeof(ElementsResult)`, объект, — так что миграция, о которой
там говорится, для примера из issue уже произошла. **Проверено по коду 2026-08-31:**
голым массивом возвращает только `GetCategoriesInRevit` (`OutputType = typeof(List<string>)`).
То есть за задачей стоит одна команда, а не несколько.

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
4. вопрос про SDK из [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107),
   затем прогресс ([#108](https://github.com/Nikola1Davydov/AnalyzeTool/issues/108)),
   отмена ([#109](https://github.com/Nikola1Davydov/AnalyzeTool/issues/109)), Tasks
   ([#110](https://github.com/Nikola1Davydov/AnalyzeTool/issues/110))
5. поля читаемости ([#113](https://github.com/Nikola1Davydov/AnalyzeTool/issues/113)) —
   работы по протоколу не требуют, и это блокирующая зависимость модуля проверки

## Связанное

- [`../concepts/long-running-calls.md`](../concepts/long-running-calls.md) · [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md)
- [`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md) · [`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md)
