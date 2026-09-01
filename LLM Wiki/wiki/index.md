---
type: index
updated: 2026-08-31
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
| [`sources/github-issues.md`](sources/github-issues.md) | бэклог идей — 88 issue, самый плотный источник здесь |
| [`sources/pipeline-design-doc.md`](sources/pipeline-design-doc.md) | дизайн конвейеров с ветки — единственный источник с замерами в живом Revit |
| [`sources/karpathy-llm-wiki-pattern.md`](sources/karpathy-llm-wiki-pattern.md) | паттерн, на котором построена вики, и как мы его адаптировали |
| [`sources/analysetool-repo-docs.md`](sources/analysetool-repo-docs.md) | обзор документации репозитория и AI-значимых проектов |

## Сущности

| Страница | Что это |
| --- | --- |
| [`entities/analysetool-mcp-server.md`](entities/analysetool-mcp-server.md) | MCP-сервер, снимок каталога инструментов, позиция по безопасности |
| [`entities/command-queue.md`](entities/command-queue.md) | единственная дверь в платформу — и почему это не очередь |
| [`entities/shadow-index.md`](entities/shadow-index.md) | непостроенный компонент, на который опираются пять планов |
| [`entities/project-folder.md`](entities/project-folder.md) | папка как интерфейс, шина сообщений и хранилище свода |
| [`entities/ollama.md`](entities/ollama.md) | локальный вывод и почему это не просто дешёвый тариф |

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

## Разборы

| Страница | Что это |
| --- | --- |
| [`analyses/mcp-surface-state.md`](analyses/mcp-surface-state.md) | что нашёл полевой тест 1.5 и в каком порядке чинить |
| [`analyses/agent-hosting.md`](analyses/agent-hosting.md) | где крутится цикл агента, у кого инициатива, кто платит |
| [`analyses/roadmap.md`](analyses/roadmap.md) | куда двигаться дальше: три слоя, что припарковать, структура трекера |
| [`analyses/checking-module.md`](analyses/checking-module.md) | модуль проверки: объём, авторство от данных, граница платного |
| [`analyses/licensing-and-monetization.md`](analyses/licensing-and-monetization.md) | как продавать модуль при открытом коде: что необратимо, что решить сейчас |
| [`analyses/backlog-map.md`](analyses/backlog-map.md) | все 63 открытых issue по группам |

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
- **Спека MCP `2026-07-28` не подтверждена** —
  [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) работал по пересказу
  release candidate, и его гейт-вопрос (поддерживает ли `ModelContextProtocol` 1.3.0)
  открыт.
- **Внутренности транспорта** — порт, привязка, поведение при двух Revit, блок
  конфигурации клиента — не подтверждены в
  [`entities/analysetool-mcp-server.md`](entities/analysetool-mcp-server.md).
- **Ничего не измерено.** Ни сравнения провайдеров, ни набора eval'ов, ни базы по
  вызовам на задачу, кроме двух цифр из
  [#84](https://github.com/Nikola1Davydov/AnalyzeTool/issues/84) и
  [#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105).
Дыры пересчитаны проходом lint 2026-08-31, см. [`log.md`](log.md).
