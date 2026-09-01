---
type: source
updated: 2026-09-02
status: draft
---

# Источник — документация репозитория AnalyseTool

Обзор документации, которую репозиторий уже несёт, и проектов, важных для AI-работы.
Всё ниже — указатели; ничего сюда не скопировано.

**Где:** рабочая копия на уровень выше — из этой страницы это `../../../`, из корня
вики `../`. Код целиком в `src/`, документы в корне репозитория и в `docs/`.
**Прочитан:** 2026-08-31, чтением перечисленных файлов.

Это живой источник, а не снимок: он меняется с каждым коммитом. Всё ниже — указатели,
которые надо открывать, а не запоминать. Раскладка репозитория — в
[`../../CLAUDE.md`](../../CLAUDE.md).

## Документы

| Путь | Что это |
| --- | --- |
| `CLAUDE.md` | инструкции для AI-ассистентов: контракт зависимостей, сборка, гардрейлы |
| `AGENTS.md` | то же содержание, для агентов, читающих `AGENTS.md` — но дрейфует от `CLAUDE.md`: на `dev` @ 45a2c09 ещё перечислял слайс `Families/` и старую раскладку деплоя Acme (`extensions\<year>\`); в рабочей копии 2026-09-02 поправлен, правка пока не закоммичена. Второй файл с тем же содержанием отстаёт первым |
| `ONBOARDING.md` | гайд автора расширений. Он же README пакета SDK на NuGet, он же зеркалится в GitHub Wiki |
| `src/LLM.md` | инструкции «вставь в ИИ» для написания расширений. Вшит ресурсом в `AnalyseTool.Core` и отдаётся дословно командой `CreateExtensionTemplate` (в папку нового расширения) и `GetAuthoringGuide` (`src/AnalyseTool.Core/Features/Extensions/GetAuthoringGuide.cs`, агенту через MCP). Рядом, в `src/AnalyseTool.Core/Features/Extensions/Templates/`, — csproj и `.gitignore`, а теперь и `readme.md.txt` с `workflow.yml.txt` |
| `docs/extension-platform-design.md` | дизайн платформы расширений |
| `CHANGELOG.md` | едет рядом с DLL плагина; окно Settings его показывает |

**GitHub Wiki уже существует и генерируется, а не пишется руками.**
`.github/workflows/wiki-sync.yml` копирует `ONBOARDING.md` в `wiki/Home.md`, а
`src/LLM.md` — в `wiki/Writing-extensions-with-AI.md`. Эта вики — отдельный артефакт,
путать нельзя: править страницы там бессмысленно, их перезапишет синхронизация.

## Проекты, важные для AI

`AnalyseTool.Sdk` — весь публичный контракт, и это пять файлов: `IRevitTask.cs`,
`IRevitContext.cs`, `RevitPayload.cs`, `RevitCommandAttribute.cs`, `IProgressAware.cs`.

`AnalyseTool.Mcp` (внешний stdio-exe): `Program.cs`, `RevitBridgeClient.cs`,
`BridgeException.cs`.

`AnalyseTool.Mcp.Bridge` (TCP-транспорт внутри Revit): `McpBridgeServer.cs`,
`McpServerController.cs`, `McpWire.cs`, `PayloadValidator.cs`, `NearestName.cs`,
плюс команды `Features/GetMcpStatus.cs` и `Features/SetMcpServer.cs`.

Слайс `Ai/` в `AnalyseTool.Tools` — это *другое* направление, плагин как AI-клиент:
`AiProviderRegistry.cs`, `AiClientFactory.cs`, `AiAnalysisService.cs`,
`OpenAiCompatibleChatClient.cs` и команды `OllamaAnalyse`, `OllamaEditParameters`,
`OllamaGetModels`, `OllamaSuggestName`, `OllamaSuggestNames`, `OllamaSuggestTemplate`,
`AiProviderCommands`.

Гардрейлы: `src/build/Check-Boundaries.ps1` (контракт зависимостей, инвариант
headless) и `src/build/Check-Schemas.ps1` (контракт схемы команд).

## Дыры

Три дыры прошлого прохода закрыты (2026-09-02): внутренности `CommandQueue` —
[`../entities/command-queue.md`](../entities/command-queue.md); блок конфигурации MCP-клиента —
[`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md); выгрузка ALC —
`src/AnalyseTool.Core/Common/Extensions/ExtensionLoadContext.cs` (`isCollectible: true`, чтобы
Reload мог выгрузить и заменить DLL) и `src/AnalyseTool.Core/Common/Extensions/ExtensionLoader.cs`
(`context.Unload()` при перезагрузке), при этом `AnalyseTool.Sdk` хоста расшаривается расширениям
ради идентичности типов.

## Связанное

- [`../overview.md`](../overview.md)
- [`../concepts/architecture-overview.md`](../concepts/architecture-overview.md)
