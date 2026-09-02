---
type: index
updated: 2026-09-02
---

# Индекс

Каталог всех страниц. Поддерживать при каждом ingest — страница-сирота это находка
линта. Если вы читаете, а не поддерживаете, начните с [`overview.md`](overview.md).

## Корень

| Страница | Что это |
| --- | --- |
| [`overview.md`](overview.md) | текущий синтез AI-поверхности AnalyseTool |
| [`conventions.md`](conventions.md) | как пишутся и связываются страницы |
| [`log.md`](log.md) | журнал операций ingest / query / lint, только дозапись |
| [`../CLAUDE.md`](../CLAUDE.md) | схема: слои, операции, правила |

## Источники

| Страница | Что это |
| --- | --- |
| [`sources/github-issues.md`](sources/github-issues.md) | бэклог идей — 91 issue, самый плотный источник здесь |
| [`sources/pipeline-design-doc.md`](sources/pipeline-design-doc.md) | дизайн конвейеров с ветки — единственный источник с замерами в живом Revit |
| [`sources/karpathy-llm-wiki-pattern.md`](sources/karpathy-llm-wiki-pattern.md) | паттерн, на котором построена вики, и как мы его адаптировали |
| [`sources/analysetool-repo-docs.md`](sources/analysetool-repo-docs.md) | обзор документации репозитория и AI-значимых проектов |
| [`sources/field-test-2026-09-02.md`](sources/field-test-2026-09-02.md) | второй полевой тест MCP — улика, которая нашла причину #98 |

## Сущности

| Страница | Что это |
| --- | --- |
| [`entities/analysetool-mcp-server.md`](entities/analysetool-mcp-server.md) | MCP-сервер, снимок каталога инструментов, позиция по безопасности |
| [`entities/command-queue.md`](entities/command-queue.md) | единственная дверь в платформу — и почему это не очередь |
| [`entities/shadow-index.md`](entities/shadow-index.md) | непостроенный компонент, на который опираются пять планов |
| [`entities/project-folder.md`](entities/project-folder.md) | папка как интерфейс, шина сообщений и хранилище свода |
| [`entities/general-folder.md`](entities/general-folder.md) | ярус бюро: общие источники без своей вики, и почему так |
| [`entities/ollama.md`](entities/ollama.md) | локальный вывод и почему это не просто дешёвый тариф |
| [`entities/ribbon-host.md`](entities/ribbon-host.md) | лента: панель Manage, три системных окна, стопки — дело панели |
| [`entities/extension-manifest.md`](entities/extension-manifest.md) | справочник `plugin.json`: схема 2, кнопки, кто и как его пишет |

## Концепции

| Страница | Что это |
| --- | --- |
| [`concepts/architecture-overview.md`](concepts/architecture-overview.md) | как фраза превращается в транзакцию Revit |
| [`concepts/deterministic-core.md`](concepts/deterministic-core.md) | инвариант всех AI-планов: решает код, модель объясняет |
| [`concepts/command-schema-contract.md`](concepts/command-schema-contract.md) | правила метаданных и описания как промпт-инжиниринг |
| [`concepts/contract-evolution.md`](concepts/contract-evolution.md) | как менять контракт, когда по нему пишет агент |
| [`concepts/agent-legibility.md`](concepts/agent-legibility.md) | сцепление по ключам и экономика контекста |
| [`concepts/write-safety-and-approval.md`](concepts/write-safety-and-approval.md) | что стоит между намерением и изменённой моделью |
| [`concepts/inbox-and-cards.md`](concepts/inbox-and-cards.md) | лента и карточки — единственная поверхность, где агент говорит |
| [`concepts/long-running-calls.md`](concepts/long-running-calls.md) | задача, под которую подогнан транспорт |
| [`concepts/proactivity-budget.md`](concepts/proactivity-budget.md) | внимание, поток Revit и деньги |
| [`concepts/extension-distribution.md`](concepts/extension-distribution.md) | каталог репозиториев: справочник, а не магазин |

## Разборы

| Страница | Что это |
| --- | --- |
| [`analyses/mcp-surface-state.md`](analyses/mcp-surface-state.md) | что нашёл полевой тест 1.5 и в каком порядке чинить |
| [`analyses/agent-hosting.md`](analyses/agent-hosting.md) | где крутится цикл агента, у кого инициатива, кто платит |
| [`analyses/roadmap.md`](analyses/roadmap.md) | куда двигаться дальше: три слоя, что припарковать, структура трекера |
| [`analyses/platform-as-runtime.md`](analyses/platform-as-runtime.md) | «всё — расширение»: где аналогия с NuGet точна, где ломается |
| [`analyses/checking-module.md`](analyses/checking-module.md) | модуль проверки: объём, авторство от данных, граница платного |
| [`analyses/licensing-and-monetization.md`](analyses/licensing-and-monetization.md) | как продавать модуль при открытом коде: что необратимо, что решить сейчас |
| [`analyses/backlog-map.md`](analyses/backlog-map.md) | все 53 открытых issue по группам |
| [`analyses/audit-2026-09-02.md`](analyses/audit-2026-09-02.md) | сверка issue и вики с кодом: семь выводов, что закрыть, что сузить, что править |

## Известные дыры

Перенесены сюда сознательно, чтобы проходу lint было с чего начать.

- **Закрытые issue** разобраны только по факту закрытия. Комментарии показали, что
  несколько из них закрыты как поглощённые
  ([#47](https://github.com/Nikola1Davydov/AnalyzeTool/issues/47) → [#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80),
  [#19](https://github.com/Nikola1Davydov/AnalyzeTool/issues/19), [#24](https://github.com/Nikola1Davydov/AnalyzeTool/issues/24),
  [#63](https://github.com/Nikola1Davydov/AnalyzeTool/issues/63), [#68](https://github.com/Nikola1Davydov/AnalyzeTool/issues/68))
  или как не планируемые ([#75](https://github.com/Nikola1Davydov/AnalyzeTool/issues/75)),
  причём с обоснованием. Тела закрытых issue не читались.
- **Устройство редактора конвейеров** ([#91](https://github.com/Nikola1Davydov/AnalyzeTool/issues/91))
  из дизайн-документа не взято: порты, идентификаторы узлов, компоновка канвы. Это UI за
  гейтом, и в охват вики он не попадает — но если гейт откроется, читать оттуда.
- **Ничего не измерено.** Ни сравнения провайдеров, ни набора eval'ов, ни базы по
  вызовам на задачу, кроме двух цифр из
  [#84](https://github.com/Nikola1Davydov/AnalyzeTool/issues/84) и
  [#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105).
Дыры пересчитаны проходом lint 2026-09-02, см. [`log.md`](log.md): транспорт MCP и блок
конфигурации клиента подтверждены по коду, гейт-вопрос про SDK MCP отвечен в
[`analyses/mcp-surface-state.md`](analyses/mcp-surface-state.md).
