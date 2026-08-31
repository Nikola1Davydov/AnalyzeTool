---
type: source
updated: 2026-08-31
status: current
---

# Источник — docs/pipeline-design.md

Дизайн-документ конвейеров: формат `.atpipe`, движок, AI-узлы, гейт перед редактором.
51 КБ. По плотности проверенных фактов — самый ценный источник в этой вики после
комментариев к issues, и единственный, где решения подкреплены **замерами в живом Revit**.

**Где:** ветка `claude/pipelines-plan-f8jrgf`, файл `docs/pipeline-design.md`.
В `dev` его нет.

```bash
git show claude/pipelines-plan-f8jrgf:docs/pipeline-design.md
```

**Прочитан:** 2026-08-31 целиком. В `raw/` не копировался: он версионируется рядом и
доступен командой выше.

## Что это

Результат сверки [#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70) с кодом.
Две вещи изменили форму в процессе, и обе несущие: две из пяти фаз оказались вообще не
конвейерами ([#88](https://github.com/Nikola1Davydov/AnalyzeTool/issues/88) и
[#89](https://github.com/Nikola1Davydov/AnalyzeTool/issues/89) — долг платформы,
окупающийся при нуле конвейеров), а нодовый редактор уехал за гейт.

Документ старше тела [#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70) и
новее его первых комментариев; где расходятся — прав документ.

## Чем он отличается от остальных источников

Здесь есть то, чего нет больше нигде: **выводы, проверенные прогоном, а не рассуждением.**

- Конвейер очистки прогнан 04.08.2026 через ~15 транзакций, собрал **~340 предупреждений**
  и **ни одного модального окна**. До этого прогона утверждение проверялось на одном
  предупреждении.
- Замерено в живом Revit: **per-transaction препроцессор срабатывает ПЕРВЫМ**, а
  `FailuresProcessing` — рутинное событие, стреляющее на каждом коммите.
- Прогнан живой конвейер «прочитать предупреждения → отфильтровать → изолировать»,
  сохранённый и запущенный по имени, без всякого редактора.

И раздел, который стоит прочитать целиком любому, кто пишет команды для агентов:
**«A schema that lied, and the silence that hid it»** — разбор четырёх дефектов, которые
сложились в конвейер, молча пропустивший 187 типов семейств в команду очистки.

## Что взято

Разнесено по страницам: [`../concepts/command-schema-contract.md`](../concepts/command-schema-contract.md)
(четыре правила из «схемы, которая лгала»),
[`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md)
(замеры, «Stop — не откат», одобрение как фильтр),
[`../concepts/deterministic-core.md`](../concepts/deterministic-core.md) (почему AI —
причина существования конвейеров, правила `AiTransform`),
[`../analyses/agent-hosting.md`](../analyses/agent-hosting.md) (почему модель не берут
через MCP), [`../entities/command-queue.md`](../entities/command-queue.md)
(запутанный заместитель в `RunPipeline`).

## Что не взято

Устройство редактора ([#91](https://github.com/Nikola1Davydov/AnalyzeTool/issues/91)):
порты, идентификаторы узлов, компоновка канвы. Это UI за гейтом, и в охват этой вики он
не попадает.

## Связанное

- [`github-issues.md`](github-issues.md) · [`../analyses/backlog-map.md`](../analyses/backlog-map.md)
