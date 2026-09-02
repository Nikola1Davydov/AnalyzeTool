---
type: source
updated: 2026-09-02
status: current
---

# Источник — полевой тест MCP 2026-09-02

Второй полевой тест поверхности MCP, после теста релиз-кандидата 1.5 от 2026-08-13
([`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md)). Проведён из
claude.ai на Revit 2025 с немецким интерфейсом, плагин 1.5.0.0, SDK 1.2.0.0, модель с
шестью стенами. Пройдены 16 read-only команд и 6 пишущих вызовов.

**Где:** [`../../raw/field-test-2026-09-02.md`](../../raw/field-test-2026-09-02.md) — отчёт как получен.

## Что нашёл

Три бага и пять замечаний. Главное: **B1 — `GetElements` падает на любом вводе** — тот же
[#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98), что и три недели назад, теперь с решающей уликой: ошибка приходит голым
«Tool execution failed» *без тела*, тогда как другие ошибки клиент показывает с кодом. Это
провал на уровне протокола, не ответ сервера — и он привёл к настоящей причине (см. ниже).

- **B2** — `GetCategoryParameters` не отдаёт `id`, хотя описание `SetDataToParameters`
  обещает «ids come from GetCategoryParameters»; id доставали через `ExecuteRevitCode`.
- **B3** — один неконвертируемый item роняет весь батч `SetDataToParameters`, и неясно,
  откатилась ли транзакция.
- `GetViewsAndSheets` кладёт в `views` сами листы и псевдовиды «Projektansicht» /
  «Systembrowser»; обещанного `hiddenElementCount` в ответе нет.
- `GetTypeParameters` обещает non-empty, отдаёт пустые; дубликаты имён без различителя.
- `SelectionInRevit` молча отбрасывает несуществующий id; `GetCategoriesInRevit` ссылается
  на скрытую от ИИ `GetDataByCategoryName`; `tool_search` в claude.ai не ищет по имени
  инструмента, только по описанию.

## Что из этого стало кодом в тот же день

Всё, кроме округления в `ExecuteRevitCode` (это код пользователя) — см. `CHANGELOG.md`
[Unreleased] и разбор причины #98 в
[`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md). Все исправления
прогнаны вживую тем же днём на той же модели: `GetElements` отвечает, `id` в параметрах
категорий и типов на месте (дубликаты вроде «Kategorie» и «Abstand» H/V различимы по id),
виды без листов и псевдовидов, `ignoredIds: [999999]`, битый item в батче — в `problems`
с причиной при `written: 0`, #97 — тип и сообщение корневого исключения плюс строка в логе.
#97 и #98 закрыты.

## Что тест не покрыл

`SaveAsCommand`, `ReloadExtensions`, `UpdateExtensionManifest`, `SaveExtensionUi`,
`GetScriptSource`, `GetAuthoringGuide`, команды расширений. Цикл авторства
([#100](https://github.com/Nikola1Davydov/AnalyzeTool/issues/100)) остаётся непроверенным.

## Связанное

- [`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md) · [`github-issues.md`](github-issues.md)
- [`../entities/analysetool-mcp-server.md`](../entities/analysetool-mcp-server.md)
