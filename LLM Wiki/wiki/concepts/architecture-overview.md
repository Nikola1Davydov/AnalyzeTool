---
type: concept
updated: 2026-08-31
status: draft
sources: [../sources/analysetool-repo-docs.md]
---

# Как устроен путь от фразы до транзакции

Как предложение, набранное в AI-клиенте, превращается в транзакцию Revit.

```
AI-клиент (Claude Code, Claude Desktop, ...)
   |  stdio, протокол MCP
   v
AnalyseTool.Mcp.exe             отдельный процесс, Revit не грузит никогда
   |  TCP, контракт провода в McpWire.cs
   v
AnalyseTool.Mcp.Bridge          внутри Revit — транспорт, и только
   |  кладёт CommandRequest в очередь
   v
CommandQueue  (AnalyseTool.Core)    <- единственная дверь для ВСЕХ транспортов
   |
   v
CommandDispatcher -> IRevitTask.ExecuteAsync(ctx)
   |
   v
ctx.RunInRevitAsync(...)        <- единственное место, где можно трогать модель
```

Та же очередь обслуживает UI на WebView2. Транспорт никогда не обращается к
диспетчеру напрямую — именно поэтому добавить новый (SignalR, CLI, ...) стоит
ProjectReference плюс одна строка `InternalsVisibleTo`, без изменений в Core.

## Проекты, важные для AI-работы

| Проект | Роль |
| --- | --- |
| `AnalyseTool.Sdk` | публичный контракт: `IRevitTask`, `IRevitContext`, `RevitPayload`, `[RevitCommand]`, `IProgressAware` — пять файлов, это вся поверхность |
| `AnalyseTool.Core` | платформа: `CommandQueue`, `CommandDispatcher`, загрузчик расширений, Roslyn-скриптинг, `CoreServices`. Headless |
| `AnalyseTool.Tools` | встроенные команды вертикальными слайсами: `Actions/ Ai/ Elements/ Families/` |
| `AnalyseTool.Mcp.Bridge` | TCP-транспорт внутри Revit |
| `AnalyseTool.Mcp` | внешний stdio-exe с MCP |

Полный контракт зависимостей — в `CLAUDE.md` репозитория, проверяется
`src/build/Check-Boundaries.ps1`.

## Почему агент вообще может что-то обнаружить

Метаданные команды — `Description`, `InputType`, `OutputType` и `[Description]` на
каждом свойстве payload'а — это то, что слой MCP превращает в схему инструмента.
Метаданные не опциональны: без них `src/build/Check-Schemas.ps1` роняет CI. См.
[`command-schema-contract.md`](command-schema-contract.md).

## Связанное

- [`../entities/command-queue.md`](../entities/command-queue.md) — та же дверь изнутри: код, а не контракт
- [`command-schema-contract.md`](command-schema-contract.md)
- [`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md)
- [`../overview.md`](../overview.md)
