---
type: source
updated: 2026-09-02
status: current
---

# Источник — трекер задач на GitHub

Бэклог идей проекта и на сегодня самый плотный источник в этой вики. Большинство
issue — не тикеты, а разобранные дизайн-заметки: рассуждение, свидетельства из
реальных сессий, отвергнутые альтернативы и явные пометки «не проверено».

**Где:** [issues репозитория Nikola1Davydov/AnalyzeTool](https://github.com/Nikola1Davydov/AnalyzeTool/issues)
**Снимки:**
[`../../raw/github-issues-2026-09-02.md`](../../raw/github-issues-2026-09-02.md) — 94 issue, тела (актуальный) ·
[`../../raw/github-issues-2026-09-01.md`](../../raw/github-issues-2026-09-01.md) — 91 issue ·
[`../../raw/github-issues-2026-08-31.md`](../../raw/github-issues-2026-08-31.md) — 88 issue ·
[`../../raw/github-issue-comments-2026-09-02.md`](../../raw/github-issue-comments-2026-09-02.md) — 86 комментариев на 44 issue (актуальный) ·
[`../../raw/github-issue-comments-2026-09-01.md`](../../raw/github-issue-comments-2026-09-01.md) — 49 на 25 ·
[`../../raw/github-issue-comments-2026-08-31.md`](../../raw/github-issue-comments-2026-08-31.md) — 46
**Прочитан:** 2026-08-31 через публичный API (`/issues?state=all`), pull request'ы отфильтрованы

## Форма

На 2026-09-01 — 91 issue, 66 открытых. Разница с предыдущим снимком: заведены
[#127](https://github.com/Nikola1Davydov/AnalyzeTool/issues/127),
[#128](https://github.com/Nikola1Davydov/AnalyzeTool/issues/128) и
[#129](https://github.com/Nikola1Davydov/AnalyzeTool/issues/129) — все три произведены из этой
вики, а не приняты в неё.

Комментарии пересняты 2026-09-01: 49 против 46. Новых три, все от 2026-09-01 —
[#125](https://github.com/Nikola1Davydov/AnalyzeTool/issues/125) получил первый комментарий
(мультипроектность), и два ушли в
[#119](https://github.com/Nikola1Davydov/AnalyzeTool/issues/119) как ссылки на карту плана.

**Дыра снимка.** Те два комментария ссылаются на **#131** (worker) и **#132** (ACC / Autodesk
Platform Services) — issue, заведённые уже ПОСЛЕ съёмки тел за 2026-09-01. В `raw/` их нет, и
всё, что вики о них знает, — эти две ссылки. Следующий снимок тел их заберёт.
*Заполнена 2026-09-02: тела обоих в снимке, вплетены (см. ниже).*

**После снимка, тем же вечером (2026-09-01), из сессии, а не из снимка:** закрыты
[#127](https://github.com/Nikola1Davydov/AnalyzeTool/issues/127) (версия схемы манифеста — отгружена, e1ded76) и
[#129](https://github.com/Nikola1Davydov/AnalyzeTool/issues/129) (`GetFamilyPreview` исчез вместе со всем слайсом семейств, 63a1992);
в [#64](https://github.com/Nikola1Davydov/AnalyzeTool/issues/64), [#48](https://github.com/Nikola1Davydov/AnalyzeTool/issues/48) и [#76](https://github.com/Nikola1Davydov/AnalyzeTool/issues/76) отписан статус «что из
этого уже отгружено». Открытых стало **64**. В `raw/` этого нет — заберёт следующий снимок.

**2026-09-02, по итогам [сверки](../analyses/audit-2026-09-02.md):** закрыты [#48](https://github.com/Nikola1Davydov/AnalyzeTool/issues/48), [#64](https://github.com/Nikola1Davydov/AnalyzeTool/issues/64), [#89](https://github.com/Nikola1Davydov/AnalyzeTool/issues/89) (отгружены), [#45](https://github.com/Nikola1Davydov/AnalyzeTool/issues/45), [#114](https://github.com/Nikola1Davydov/AnalyzeTool/issues/114) (ушли с Family Manager в другой репозиторий), [#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52), [#53](https://github.com/Nikola1Davydov/AnalyzeTool/issues/53) (поглощены); статус и сужение отписаны в #83, #88, #106, #77, #84, #76, #128, #90–#92, #72, #74. Открытых **57**. Тоже не в `raw/`.

**Снимок 2026-09-02 (вечер):** 94 issue, **54 открытых**. Против снимка 2026-09-01 три новых —
[#131](https://github.com/Nikola1Davydov/AnalyzeTool/issues/131) (worker и движок правил без Revit API) и [#132](https://github.com/Nikola1Davydov/AnalyzeTool/issues/132) (ACC / APS: четыре шва сейчас), тела
которых закрыли дыру выше, и [#133](https://github.com/Nikola1Davydov/AnalyzeTool/issues/133) — план встроенного агента, произведён из этой вики
([`../analyses/built-in-agent-plan.md`](../analyses/built-in-agent-plan.md)). Пятнадцать закрытий, все
за этот день: семь по сверке (выше), #97, #98, #100, #101, #102, #127, #128, #129 — починены и
отгружены на `dev`. Комментарии: 86 на 44 issue против 49 на 25; все 37 новых — статусные записи из
рабочей сессии 2026-09-02 (обоснования закрытий, сужения, итоги живых проверок), ни одного
человеческого — вплетать из них нечего, вики их и породила.

**Позже тем же вечером, из сессии:** закрыт [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107) (гейт снят, строки таблицы разошлись по
своим issue), [#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111) сужен до трёх инструментов с массивом в корне и пяти пунктов; ещё позже закрыт
[#113](https://github.com/Nikola1Davydov/AnalyzeTool/issues/113) — третий срез отгружен, остаток ушёл в #84. Открытых **52**.
Не в `raw/` — заберёт следующий снимок.

Цифры ниже относятся к снимку от 2026-08-31: 63 открытых, 25 закрытых. Метки бедные — `enhancement` 40, `bug` 11, `help wanted` 1 —
поэтому метки как ось бесполезны. Полезная ось — кластер, и их шесть:

| Кластер | Issue | Что это |
| --- | --- | --- |
| Дефекты MCP | 97–105 | находки полевого теста релиз-кандидата 1.5 в живой сессии Revit |
| Протокол MCP | 83–85, 106–113 | эргономика, безопасность, спека `2026-07-28`, длинные вызовы, экономика контекста |
| Агент | 45, 56, 71, 77, 79, 80, 92, 115–118 | от подсказки-призрака до проактивного встроенного копилота |
| Модуль проверки | 119–126 | своды правил, граница платного, отчёт, папка проекта, headless-доступ |
| Платформа расширений | 48, 64, 72, 75, 76, 81, 87, 93, 95 | распространение, реестр, лицензирование, цепочка поставки |
| Не про AI | 13, 43, 52–54, 57, 62, 69, 74, 114 | IDS, палитра, спредшит, проверка орфографии, сайт, сборка |

Нумерация примерно хронологическая, и чем выше номер, тем больше бэклог из списков
фич превращается в аргументированный дизайн. Всё выше ~#95 написано после реального
полевого теста и читается соответственно.

## Почему это важно здесь

Три вещи есть только тут и больше нигде в репозитории:

1. **Наблюдаемое поведение агента.** [#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105) считает ~20 вызовов `ExecuteRevitCode` против
   горстки всего остального за одну сессию; [#84](https://github.com/Nikola1Davydov/AnalyzeTool/issues/84) фиксирует 8 вызовов, чтобы разместить
   2 экземпляра семейства. Это единственные существующие измерения AI-поверхности.
2. **Решения вместе с обоснованием**, включая отвергнутые варианты — ровно то, чего
   код не хранит.
3. **Честная неуверенность.** Несколько issue несут пометки ⚠️ о том, что не
   проверено против живого Revit или финальной спеки MCP. Эти пометки перенесены в
   вики, а не сглажены.

## Что взяли

Всё, что касается AI, — вплетено в [`../overview.md`](../overview.md), страницы концепций и три разбора.
Не-AI кластеры нанесены на карту, но не развёрнуты — см. [`../analyses/backlog-map.md`](../analyses/backlog-map.md).

## Что не взяли

- **Комментарии — выкачаны все 46.** Первый ingest их не брал вовсе, и это была самая
  дорогая ошибка: в комментариях лежит слой решений, которого нет в телах issue —
  разворот выбора хранилища, Microsoft Agent Framework, инвариант топологии графа,
  предупреждение про долговечность ошибок в few-shot памяти, незаписанный дефект «агент
  слеп». Всё это вплетено; журнал за 2026-08-31 перечисляет поштучно.

  Замечание на будущее: комментарии здесь — **не обсуждение, а ревизии плана**. Тело
  issue часто описывает более раннее состояние мысли, чем последний комментарий под ним,
  и расходится с ним по существу. Читать тело без комментариев — значит читать
  устаревшую версию.

  ```bash
  curl -s "https://api.github.com/repos/Nikola1Davydov/AnalyzeTool/issues/comments?per_page=100"
  ```

- **Закрытые issue**, кроме факта, что они закрыты.
- **Связанные дизайн-документы** — `docs/pipeline-design.md` прочитан отдельно, см.
  [`pipeline-design-doc.md`](pipeline-design-doc.md). Он новее первых комментариев к
  [#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70) и где расходятся — прав он.

## Как обновить

```bash
curl -s "https://api.github.com/repos/Nikola1Davydov/AnalyzeTool/issues?state=all&per_page=100&page=1"
```

Листать страницы до пустой, выбросить записи с `pull_request`, положить новый
датированный снимок рядом со старым — старый никогда не перезаписывать.

## Связанное

- [`../analyses/backlog-map.md`](../analyses/backlog-map.md) · [`../analyses/mcp-surface-state.md`](../analyses/mcp-surface-state.md) · [`../analyses/agent-hosting.md`](../analyses/agent-hosting.md)
