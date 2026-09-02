---
type: analysis
updated: 2026-09-01
status: current
sources: [../sources/github-issues.md]
---

# Карта открытого бэклога

53 открытых issue (снимок 2026-09-01, минус #100 и #128 (закрыты 2026-09-02), минус семь закрытых 2026-09-02 по [сверке](audit-2026-09-02.md), минус #97 и #98 — починены и подтверждены вживую в тот же день, минус [#127](https://github.com/Nikola1Davydov/AnalyzeTool/issues/127) и [#129](https://github.com/Nikola1Davydov/AnalyzeTool/issues/129), закрытые в тот же день после снимка), сгруппированных по тому, о чём они на самом деле, а не по меткам.
AI-кластеры развёрнуты в других страницах вики; остальные нанесены здесь, чтобы ничего
не потерялось молча.

## Развёрнуто в этой вики

| Кластер | Issue | Где |
| --- | --- | --- |
| Дефекты и протокол MCP | 83–85, 99, 101–113 (97, 98, 100, 128 закрыты 2026-09-02; 129 — команды больше нет) | [`mcp-surface-state.md`](mcp-surface-state.md), [`../concepts/long-running-calls.md`](../concepts/long-running-calls.md) |
| Агент и где он крутится | 80, 115–118 | [`agent-hosting.md`](agent-hosting.md), [`../concepts/proactivity-budget.md`](../concepts/proactivity-budget.md) |
| Безопасность и одобрение | 88, 106, 123, 126 | [`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md) |
| Лента, карточки, порог | 79, 80, 116, 118, 122, 126 | [`../concepts/inbox-and-cards.md`](../concepts/inbox-and-cards.md) |
| Инвариант всех AI-планов | 79, 92, 119, 122, 123, 126 | [`../concepts/deterministic-core.md`](../concepts/deterministic-core.md) |
| Хранилище под всем этим | 80, 85, 124, 125 | [`../entities/shadow-index.md`](../entities/shadow-index.md) |
| Экономика контекста | 83, 84, 113, 123 | [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md) |
| Модуль проверки | 119–126 | [`checking-module.md`](checking-module.md) |

## Модуль проверки

Коммерчески самая острая ветка, и самая свежая. Разобрана отдельно —
[`checking-module.md`](checking-module.md), включая решения из комментариев к
[#119](https://github.com/Nikola1Davydov/AnalyzeTool/issues/119), которых нет в телах
issue. Ниже — состав.

- **[#119](https://github.com/Nikola1Davydov/AnalyzeTool/issues/119)** — зонтичный
  план. Два продукта на общем фундаменте: *проверка* (правила → находки → отчёт, ИИ
  только при авторстве) и *ассистент*
  ([#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80),
  [#118](https://github.com/Nikola1Davydov/AnalyzeTool/issues/118)). Проверка
  отгружается **первой и независимо**: фазы 1–4 не требуют ни агента, ни MCP, ни
  единого вызова модели в рантайме. Рыночная мысль: категория зрелая (Solibri,
  Navisworks), её известное узкое место — составление наборов правил мучительно, а
  стандарт бюро *уже существует* в виде сорока страниц прозы в Word. Дистанцию между
  прозой и исполнимым сводом ИИ сокращает по-настоящему.
- **[#120](https://github.com/Nikola1Davydov/AnalyzeTool/issues/120)** — продуктовая
  линия: *смотреть бесплатно, обеспечивать соблюдение платно*. Теневой индекс и
  инвентари данных лежат в бесплатной части сознательно. Платный модуль отгружается как
  настоящее расширение в своём ALC — аргумент не защита, а поедание собственной еды:
  чего не хватит флагманскому платному модулю, обязано попасть в SDK, и это делает SDK
  по-настоящему пригодным для третьих лиц. Побочный эффект: подпись сборок
  ([#87](https://github.com/Nikola1Davydov/AnalyzeTool/issues/87)) перестаёт быть
  гигиеной и становится механизмом монетизации.
- **[#121](https://github.com/Nikola1Davydov/AnalyzeTool/issues/121)** — шаблон
  параметров как *декларативная половина свода*, а не второй артефакт. Отсутствие
  проверяемо только против декларации. Общие параметры опознаются по GUID, никогда по
  имени.
- **[#122](https://github.com/Nikola1Davydov/AnalyzeTool/issues/122)** — отчёт как
  документ: канва А4, блоки — **запросы, а не картинки**, печать прямо из WebView2.
  Дашборд для себя, а в стройке отдают документы.
- **[#123](https://github.com/Nikola1Davydov/AnalyzeTool/issues/123)** — MCP-поверхность
  модуля проверки и мост обратно к агенту.
- **[#124](https://github.com/Nikola1Davydov/AnalyzeTool/issues/124)** — папка проекта
  как интерфейс: обычная общая папка и есть бэкенд, значит для коллаборации не нужен наш
  сервер. Идентичность источника **по хешу, а не по пути**, чтобы цитата открывалась
  годами; дрейф источника становится находкой линта.
- **[#125](https://github.com/Nikola1Davydov/AnalyzeTool/issues/125)** —
  headless-доступ. Всё спроектированное читается без Revit, поэтому сервис над папкой
  может отвечать из Teams или чата. Коммерческая суть: вопрос «какое состояние проекта»
  задаёт не моделлер, а руководитель, у которого нет лицензии Revit.
  Комментарий 2026-09-01 добавил ярусы: **вики — на проект, шаблон — на бюро, харнес — на
  установку**, отсюда изоляция контекста между заказчиками и расписание посещений
  ([`agent-hosting.md`](agent-hosting.md)).
- **[#126](https://github.com/Nikola1Davydov/AnalyzeTool/issues/126)** — обратная петля:
  комментарий → карточка → применение → исход → кандидат в правило.

Заведены после снимка тел и известны вики **только по ссылкам** из комментариев к
[#119](https://github.com/Nikola1Davydov/AnalyzeTool/issues/119) (2026-09-01):

- **#131** — worker: консолидация трёх ролей (наблюдатель #124, индексатор #56, ответчик
  #125). Архитектурное требование, которое надо поймать до кода: **движок правил — библиотека
  без Revit API**, один движок и два исполнителя (плагин против живой модели, worker против
  снапшотов); форматы папки — wire-контракт в дисциплине `McpWire`.
- **#132** — ACC / Autodesk Platform Services. Интеграция отложена сознательно, но четыре шва
  закладываются сейчас: хранилище за интерфейсом (SMB · OneDrive · ACC Docs), AECDM как третья
  подложка `GetDataInventory`, «находка → внешняя система» как порт (BCF · ACC Issues · Teams),
  версионированные форматы папки. AEC Data Model API уже в GA — элементы и параметры облачных
  моделей без плагина и без Revit; мутации в бете.

Тел у них в `raw/` нет — заберёт следующий снимок.

## AI-фичи, не покрытые выше

- **[#56](https://github.com/Nikola1Davydov/AnalyzeTool/issues/56)** — AI-слой и RAG по
  проекту, шесть фаз: абстракция `IChatClient`, чат-UX, встроенный вывод, конвейер
  индексации, поиск с цитатами, интеграция с MCP. Учтите, что
  [#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85) утверждает: RAG
  оправдан только в двух местах — поиск по библиотеке семейств и тексты норм, — а всё
  остальное это детерминированный доступ, который извлечение сделало бы примерно втрое
  дороже и менее точным.
  В комментарии к [#56](https://github.com/Nikola1Davydov/AnalyzeTool/issues/56)
  (31.07.2026) — готовый слой хранения `CommunityToolkit/AI` (коннекторы
  `Microsoft.Extensions.VectorData`), из-за которого пункт «своя абстракция VectorStore»
  отменяется. И ограничение, которое надо зафиксировать до кода: **SqliteVec нельзя
  тащить в процесс Revit** — он тянет `SQLitePCLRaw.bundle_e_sqlite3` и нативное
  расширение `sqlite-vec`, а нативная библиотека не выгружается и пинит collectible ALC
  загрузчика расширений. Значит SqliteVec только во внешнем ingestion-воркере, PgVector
  (чисто managed, Npgsql) безопасен в процессе, InMemory безопасен везде. Отсюда развилка
  фазы 4: если локальный бесплатный тир — это sqlite-vec, то запрос из Revit должен идти
  через воркер по IPC/HTTP, либо локальный тир перестаёт быть sqlite-vec. Решать до
  начала фазы 3.
- **[#77](https://github.com/Nikola1Davydov/AnalyzeTool/issues/77)** — AI DX: замкнуть
  цикл авторства, чтобы агент писал, загружал, диагностировал, чинил и тестировал
  расширение без человека, пересказывающего ошибки. Частично отгружено —
  `GetExtensionDiagnostics`, `GetAuthoringGuide` и `ReloadExtensions` есть в живом
  списке инструментов, — а
  [#100](https://github.com/Nikola1Davydov/AnalyzeTool/issues/100) это оставшийся разрыв
  в цикле.
  Тонкое наблюдение из комментария к [#84](https://github.com/Nikola1Davydov/AnalyzeTool/issues/84)
  (16.08.2026): `GetAuthoringGuide` — **инструмент**, поэтому агент должен додуматься его
  позвать, а додумывается он только *после* решения писать расширение, то есть после
  момента, когда гайд повлиял бы на само решение. Ресурсом клиент прицепил бы его
  заранее и бесплатно. Мешает то же, что и всему остальному: у провода бриджа нет
  глагола для ресурсов. Плюс напряжение с правилом единственной копии — `LLM.md` вшит в
  Core, а exe про Core не знает, так что отдавать его при закрытом Revit значило бы везти
  вторую копию рядом с exe.
- **[#71](https://github.com/Nikola1Davydov/AnalyzeTool/issues/71)** — генеративный UI:
  команда рендера с **белым списком рендереров**, никогда не произвольный HTML от LLM.
  [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) отмечает, что MCP
  Apps теперь формальное расширение, где UI-шаблоны объявляются заранее — это заметно
  другая модель угроз, чем генерация HTML в момент вызова.
- **[#79](https://github.com/Nikola1Davydov/AnalyzeTool/issues/79)** — детекция коллизий
  с полуавтоматическим разрешением.
- **[#45](https://github.com/Nikola1Davydov/AnalyzeTool/issues/45)** — подсказка-призрак
  для имён. Маленькая, и единственное место, где порог прерывания соблюдён от рождения.
- **[#90](https://github.com/Nikola1Davydov/AnalyzeTool/issues/90)–[#92](https://github.com/Nikola1Davydov/AnalyzeTool/issues/92)**
  — конвейеры. [#90](https://github.com/Nikola1Davydov/AnalyzeTool/issues/90) даёт MCP
  то, чего у него нет: способ заморозить сработавшую цепочку и повторить её без LLM.
  [#91](https://github.com/Nikola1Davydov/AnalyzeTool/issues/91) и
  [#92](https://github.com/Nikola1Davydov/AnalyzeTool/issues/92) явно за гейтом, причём
  [#91](https://github.com/Nikola1Davydov/AnalyzeTool/issues/91) утверждает, что
  нодовый редактор может не понадобиться вообще.
  В комментариях к [#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70) —
  таксономия из пяти родов узлов (командные, оркестраторные, AI, взаимодействия,
  триггерные), инвариант топологии графа
  ([`../concepts/deterministic-core.md`](../concepts/deterministic-core.md)), уроки
  ComfyUI (кэш по хэшу **только для ReadOnly**-узлов — их узлы чистые функции, наши
  мутируют живую модель; workflow зашит в результат ради воспроизводимости) и `onFailure`
  на узел, который решает **движок** из исхода узла, а команда о нём не знает. По
  дизайн-документу значений два — `stop` и `continue`, — и три уточнения, каждое из
  которых более вольная формулировка однажды переврала: применяется **только когда команда
  бросает** (запись, положившая 488 из 500, не упала); умолчание `stop` для **любого** узла,
  а не только `Destructive` (умолчание, выведенное из каталога, заставило бы один и тот же
  файл вести себя по-разному на двух установках); и **отмена не отказ и всегда побеждает** —
  `OperationCanceledException` ловится первым, `onFailure` не спрашивают, иначе узел с
  `continue` проглотил бы Stop пользователя. `stopNode` намеренно не в v1: в линейном
  конвейере он неотличим от `stop`. Approval-узлы **приостанавливают прогон**, а не
  поднимают модальное окно: модальное заблокировало бы поток Revit, то есть повторило бы
  ровно тот сбой, ради устранения которого существует
  [#88](https://github.com/Nikola1Davydov/AnalyzeTool/issues/88).

## Платформа, не AI

~~[#127](https://github.com/Nikola1Davydov/AnalyzeTool/issues/127) версия схемы манифеста~~ — закрыт 2026-09-01, отгружен (e1ded76; разбор — [`../concepts/contract-evolution.md`](../concepts/contract-evolution.md)) ·
~~[#48](https://github.com/Nikola1Davydov/AnalyzeTool/issues/48) распространение сторонних~~ (закрыт 2026-09-02; остатки в #72 и #87) ·
~~[#64](https://github.com/Nikola1Davydov/AnalyzeTool/issues/64) менеджер расширений~~ (закрыт 2026-09-02, отгружен) ·
[#72](https://github.com/Nikola1Davydov/AnalyzeTool/issues/72) лицензирование ·
[#76](https://github.com/Nikola1Davydov/AnalyzeTool/issues/76) реестр (половина «Available» есть как каталог в плагине; открыт вопрос, нужен ли отдельный репозиторий) ·
[#81](https://github.com/Nikola1Davydov/AnalyzeTool/issues/81) публикация SDK ·
[#87](https://github.com/Nikola1Davydov/AnalyzeTool/issues/87) цепочка поставки ·
[#93](https://github.com/Nikola1Davydov/AnalyzeTool/issues/93) путь публикации ·
[#95](https://github.com/Nikola1Davydov/AnalyzeTool/issues/95) заморозка контракта лаунчера.

[#87](https://github.com/Nikola1Davydov/AnalyzeTool/issues/87) стоит прочитать даже с
AI-стороны: там прямо сказано, что collectible ALC — механизм выгрузки и идентичности
типов, **а не песочница**. Расширения работают в процессе с полным доверием, и
документация не должна позволять читать «изолированный контекст загрузки» как границу
безопасности.

## Вообще не про AI

[#13](https://github.com/Nikola1Davydov/AnalyzeTool/issues/13) IDS ·
[#14](https://github.com/Nikola1Davydov/AnalyzeTool/issues/14) вкладка материалов ·
[#43](https://github.com/Nikola1Davydov/AnalyzeTool/issues/43) палитра команд ·
[#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52) Fingerprints ·
[#53](https://github.com/Nikola1Davydov/AnalyzeTool/issues/53) требования к параметрам ·
[#54](https://github.com/Nikola1Davydov/AnalyzeTool/issues/54) проверка орфографии ·
[#57](https://github.com/Nikola1Davydov/AnalyzeTool/issues/57) спредшит-модуль ·
[#62](https://github.com/Nikola1Davydov/AnalyzeTool/issues/62) Image to Revit ·
[#69](https://github.com/Nikola1Davydov/AnalyzeTool/issues/69) миграция сборки ·
[#74](https://github.com/Nikola1Davydov/AnalyzeTool/issues/74) лендинг ·
[#114](https://github.com/Nikola1Davydov/AnalyzeTool/issues/114) экспорт семейств.

Две из них питают AI-работу косвенно:
[#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52) (данные модели вне Revit)
— это то, что [#125](https://github.com/Nikola1Davydov/AnalyzeTool/issues/125) называет
исполнением собственной идеи, а
[#53](https://github.com/Nikola1Davydov/AnalyzeTool/issues/53) — предок
[#121](https://github.com/Nikola1Davydov/AnalyzeTool/issues/121).

## Связанное

- [`../sources/github-issues.md`](../sources/github-issues.md) · [`mcp-surface-state.md`](mcp-surface-state.md) · [`agent-hosting.md`](agent-hosting.md)
