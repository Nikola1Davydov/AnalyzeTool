---
type: entity
updated: 2026-09-02
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

Снят с живой сессии 2026-08-31, пересверен с кодом 2026-09-01. Список генерируется из
загруженных команд и растёт с установленными расширениями — это снимок, а не контракт.

**Попадание в список не означает, что инструмент работает.** `GetElements` — первое, за
чем тянется агент, — падала на любом вводе всю сессию полевого теста
([#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98)). Прежде чем доверять
записям ниже, см.
[`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md).

### Чтение модели

`GetModelOverview` · `GetDocumentData` · `GetElements` · `GetCategoriesInRevit` ·
`GetCategoryParameters` · `GetTypeParameters` · `GetViewsAndSheets` · `GetWorksets` ·
`GetLinksInRevit` · `GetCadImports` · `GetWarningsInRevit`

### Семейства — группы больше нет

2026-09-01 (63a1992) слайс `Tools/Families` удалён из платформы целиком: Family Manager
работает как расширение, и его 17 команд (`GetFamilies*`, `GetFamilyMesh`, `GetFamilyPreview`,
`RenameFamily*`, `LoadLibraryFamilies`, `PlaceFamilyInstance`, `DeleteFamilyElements`,
`PurgeFamilies`, `PurgeFamilyTypes`, `SetInstancesWorkset`) переехали с ним — в списке
инструментов они появляются только с установленным расширением, под его префиксом.
Зарегистрированных команд в платформе сегодня 64 (`[RevitCommand]` по `src`, сверка 2026-09-02),
из них 39 помечены `HiddenFromMcp` и агенту не видны — см. «Чего агент не видит» ниже.
`GetWorksets` и `GetTypeParameters` остались: они не про семейства и переехали в `Elements`.

Следствие для вики: всё, что выше и ниже сказано о размещении семейств и о `Destructive`
на командах Family Manager ([`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md),
[`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md)),
теперь относится к расширению, а не к платформе.

### Запись в модель

`SetDataToParameters` · `SaveAsCommand`

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

## Чего агент не видит

Правило в `src/AnalyseTool.Mcp.Bridge/McpBridgeServer.cs` одно, `IsAvailableToAi`:

`ExposeToMcp && (CodeExecutionSettings.Enabled || !IsCodeAuthoringTool(name))`

`ExposeToMcp` — это `!HiddenFromMcp` из атрибута (`CommandDispatcher.cs`). А переключатель C#
в Settings прячет **набор**, не один инструмент: `ExecuteRevitCode`, `SaveAsCommand`,
`GetScriptSource`, `SaveExtensionUi`, `UpdateExtensionManifest`, `ReloadExtensions` — читать
исходник скрипта значит отдать код с машины пользователя, а перезагрузка — шаг, которым
написанный код вступает в силу. Выключенный инструмент отвечает `NotAvailable` с подсказкой
попросить человека, а не «неизвестная команда».

Скрытые 39 (сверка 2026-09-02 по `HiddenFromMcp = true`):

| Группа | Команды |
| --- | --- |
| AI-провайдеры и Ollama (`Tools/Ai`) | `AiGetProviders`, `AiSaveProvider`, `AiDeleteProvider`, `AiGetModels`, `OllamaAnalyse`, `OllamaEditParameters`, `OllamaGetModels`, `OllamaSuggestName`, `OllamaSuggestNames`, `OllamaSuggestTemplate` |
| Управление расширениями (`Core/Features/Extensions`) | пути `GetExtensionPaths`, `AddExtensionPath`, `RemoveExtensionPath`, `SetAuthoringRoot`; обновления `CheckExtensionUpdates`, `UpdateExtension`; каталог `GetExtensionCatalog`; установка `InstallExtensionFromFile`, `InstallExtensionFromSource`; удаление `RemoveExtension`, `RemoveDevExtension`; `SetExtensionEnabled`; форма Edit `GetExtensionManifest`, `EditExtensionManifest`; `CreateExtensionTemplate`, `OpenFolder`, `SetCommandButton`, `GetCommands` |
| Исполнение кода (`Core/Features/Scripting`) | `GetCodeExecutionStatus`, `SetCodeExecution` — ИИ не может выдать себе право |
| Сам MCP (`Mcp.Bridge/Features`) | `GetMcpStatus`, `SetMcpServer` |
| Диалоги и кнопки хоста (`App/Features`) | `BrowseForFile`, `BrowseForFolder`, `PickFolder`, `GetChangelog`, `GetHostButtons`, `SetHostButtonVisible` |
| Тяжёлый UI-ответ (`Tools/Elements`) | `GetDataByCategoryName` — все параметры каждого элемента; агенту вместо неё `GetElements` / `GetCategoryParameters` |

Видимых остаётся 25, при выключенном переключателе C# — 19. `CheckUpdate`
(`src/AnalyseTool.App/Features/CheckUpdate.cs`: App, `ReadOnly`, не скрыт) по коду должен быть
среди них, но в каталоге, снятом с живой сессии выше, его нет — **проверить вживую**, попадает
ли он в `tools/list`.

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

## Исполняемый файл: самодостаточный

С 2026-09-02 `mcp\AnalyseTool.Mcp.exe` публикуется как один self-contained файл (win-x64,
single-file, сжатый, ~36 МБ) — `PluginAssets.targets` вызывает `Publish`, а не `Build`, и
передаёт `RuntimeIdentifier`/`SelfContained` параметрами, чтобы ProjectReference на exe (только
порядок сборки) не упирался в NETSDK1150. Причина: exe запускает клиент ИИ, не Revit, и
runtime Revit ему недоступен; framework-dependent net8.0 держался на том, что Revit 2025/2026
ставят .NET 8 сами, а Revit 2027 на net10 этого не гарантирует. Подсмотрено в шаблоне
`dotnet new mcpserver`; остальное из шаблона (атрибутные инструменты, логи через
`AddConsole`) нам не подходит, а `.mcp/server.json` + `PackageType=McpServer` — отдельное
решение о публикации в реестр MCP, рядом с #81/#93.

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
- [`extension-manifest.md`](extension-manifest.md) — манифест, из которого растут инструменты расширений · [`ribbon-host.md`](ribbon-host.md) — `ribbon`, третий источник той же очереди
