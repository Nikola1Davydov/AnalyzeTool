---
type: entity
updated: 2026-09-01
status: draft
sources: [../sources/analysetool-repo-docs.md, ../sources/github-issues.md]
---

# MCP-сервер AnalyseTool

MCP-сервер, позволяющий внешнему агенту управлять запущенной сессией Revit. Две
половины: `AnalyseTool.Mcp` (stdio-exe, запускается AI-клиентом, Revit не грузит
никогда) и `AnalyseTool.Mcp.Bridge` (TCP-слушатель внутри Revit, кладущий запросы в
`CoreServices.Queue`).

Инструменты — это команды AnalyseTool, выставленные через заявленные метаданные; см.
[`../concepts/command-schema-contract.md`](../concepts/command-schema-contract.md).

## Каталог инструментов

Снят с живой сессии 2026-08-31. Список генерируется из загруженных команд и растёт с
установленными расширениями — это снимок, а не контракт.

**Попадание в список не означает, что инструмент работает.** `GetElements` — первое, за
чем тянется агент, — падала на любом вводе всю сессию полевого теста
([#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98)). Прежде чем доверять
записям ниже, см.
[`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md).

### Чтение модели

`GetModelOverview` · `GetDocumentData` · `GetElements` · `GetCategoriesInRevit` ·
`GetCategoryParameters` · `GetTypeParameters` · `GetViewsAndSheets` · `GetWorksets` ·
`GetLinksInRevit` · `GetCadImports` · `GetWarningsInRevit`

### Семейства

`GetFamilies` · `GetFamilyTypes` · `GetFamilyTypeRows` · `GetFamilyInstances` ·
`GetInPlaceFamilies` · `GetLibraryFamilies` · `GetFamilyMesh` · `GetFamilyPreview`

### Запись в модель

`SetDataToParameters` · `SetInstancesWorkset` · `RenameFamily` · `RenameFamilyType` ·
`LoadLibraryFamilies` · `PlaceFamilyInstance` · `DeleteFamilyElements` ·
`PurgeFamilies` · `PurgeFamilyTypes` · `SaveAsCommand`

### Состояние вида и сессии

`SelectionInRevit` · `IsolationInRevit` · `GetQueueStatus`

### Скриптинг и расширения

`ExecuteRevitCode` · `GetScriptSource` · `GetAuthoringGuide` · `GetInstalledExtensions` ·
`GetExtensionDiagnostics` · `ReloadExtensions` · `SaveExtensionUi` ·
`UpdateExtensionManifest`

`GetAuthoringGuide` отдаёт `src/LLM.md` — тот же текст, что шаблон расширения кладёт в
новую папку. Агенту, которого просят написать расширение, стоит звать её первой, а не
угадывать контракт.

### Инструменты от расширений

Установленные расширения дают инструменты с именами
`<издатель>_<расширение>_<Команда>`. На снятой сессии присутствовали:

- `company_floorplangenerator_*` — `CreateFloorPlan`, `GetFloorPlanContext`, `PickArea`, `PickEntrance`
- `company_generatevirtualecomponentdata_*` — `GenerateFamilyTypes`, `GetFamilyContext`, `InspectSqliteDatabase`, `PickSqliteFile`, `ReadSqliteRows`
- `company_umgebung_*` — `GeocodeProjectAddress`, `GetProjectLocation`, `ImportSiteContext`, `ListContextBuildings`, `ListSources`, `PreviewSiteContext`, `SetProjectLocation`, `UpdateContextBuildings`
- `niko_family-cleaner_FamilieBereinigen`
- `niko_room-sheets_*` — `CreateRoomPlanSheets`, `PickRooms`
- `niko_wall-constraints_CopyWallConstraints`

Обратите внимание на форму: несколько расширений держат пару из команды `Pick*` и
команды, которая потребляет выбор. Это паттерн для работы, требующей человека в
контуре: агент просит выбрать в Revit и затем действует по выбранному.

## Что бридж делает для вызывающего, который угадывает

Два файла существуют только потому, что вызывающий — языковая модель:
`NearestName.cs` нечётко сопоставляет выдуманное имя команды, а `PayloadValidator.cs`
проверяет payload против заявленного `InputType` до диспатча. Заложено, что вызывающий
ошибётся и в имени, и в аргументах, а сообщение об ошибке должно быть таким, чтобы
модель могла по нему действовать.

Сейчас это допущение разбивается слоем выше: бридж схлопывает любой сбой до
`ex.Message` и ничего не логирует, поэтому ошибка, которую получает модель, — обычно
текст обёртки ([#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97)).

## Позиция по безопасности

Сверено с официальным чек-листом MCP в
[#112](https://github.com/Nikola1Davydov/AnalyzeTool/issues/112): токен сессии на каждом
запросе, серверная валидация payload'а, `HiddenFromMcp` плюс
`McpBridgeServer.IsAvailableToAi` как граница доступа, привязка к localhost без
исходящего трафика, и переключатель исполнения C#, который по построению доступен
только человеку — `SetCodeExecution` сам помечен `HiddenFromMcp`, так что ИИ не может
выдать себе право исполнять код. Единственный пробел — **ограничение частоты**: нет
ничего.

## Транспорт: порт, привязка, второй Revit

Проверено по коду 2026-08-31, блок «не проверено» снят.

- **Только loopback**: `new TcpListener(IPAddress.Loopback, port)`
  (`McpBridgeServer.cs:51`). Из сети бридж недостижим.
- **Порт по умолчанию 17890** (`McpWire.cs:28`), задаётся параметром `Start(int port)`;
  фактический читается обратно из `LocalEndpoint`, так что порт `0` означает выданный
  системой.
- **Второй Revit на том же порту не поднимется**: `listener.Start()` бросает («port in
  use, most likely»), состояние аккуратно откатывается, и исключение уходит наверх.
  Отката на другой порт нет — второй экземпляр остаётся без бриджа, если порт не сменить
  вручную.

И самое важное, сказанное прямо в комментарии на `McpBridgeServer.cs:164`:

> Loopback is not an authorization boundary: every process running as this user can open
> this port, and what is behind it drives Revit.

То есть привязка к localhost ничего не авторизует — любой процесс того же пользователя
может открыть этот порт, а за ним Revit. Авторизует **токен**: он доказывает, что
вызывающего настроил пользователь. Токен генерируется хостом и лежит в `mcp.json`,
доступном только пользователю; Settings отдаёт его в блоке конфигурации клиента вместе с
аргументом `--token`. Сравнение токена — постоянного времени
(`CryptographicOperations.FixedTimeEquals`), а пустой настроенный токен означает отказ, а
не открытую дверь.

## Блок конфигурации клиента

Проверено по коду 2026-09-01, дыра закрыта. Блок собирает **фронтенд**, не хост:
`clientConfig` в `src/clientapp/src/view/System/SettingsView.vue` из ответа `GetMcpStatus`
(`serverExePath`, `port`, `token`). Форма:

```json
{ "mcpServers": { "analysetool-revit": {
    "command": "<путь к AnalyseTool.Mcp.exe>",
    "args": ["--port", "<порт>", "--token", "<токен>"] } } }
```

Где это в интерфейсе — после разделения настроек 2026-09-01: **Settings → Artificial
intelligence → External assistant → Connection details** (свёрнуто; порт там же, менять
можно только при выключенном сервере). Сам сервер включается тумблером на блоке
«External assistant». Переключатель исполнения C# стоит **внутри того же блока** — по
построению он относится только к внешнему агенту, встроенный код не исполняет.

Блок намеренно назван «внешний ассистент», а не «MCP server»: в окне настроек рядом стоит
выбор модели для *другого* направления (плагин как AI-клиент, см.
[`ollama.md`](ollama.md)), и до разделения пользователь читал их как одну систему — ровно
путаница, про которую предупреждает [`../overview.md`](../overview.md).

## Связанное

- [`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md) — что сломано и в каком порядке чинить
- [`../concepts/architecture-overview.md`](../concepts/architecture-overview.md) · [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md)
- [`../concepts/long-running-calls.md`](../concepts/long-running-calls.md) · [`../overview.md`](../overview.md)
