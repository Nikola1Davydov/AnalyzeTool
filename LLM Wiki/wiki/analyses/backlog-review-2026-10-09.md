---
type: analysis
updated: 2026-10-09
status: current
sources: [../sources/github-issues.md]
---

# Разбор бэклога после поворота продукта (2026-10-09)

Все 71 открытый issue (снимок `raw/github-issues-2026-10-09.md`) против того, чем продукт стал за два
дня: **без встроенного ИИ, фокус — MCP и создание кнопок** ([#164](https://github.com/Nikola1Davydov/AnalyzeTool/issues/164),
2026-10-08) и **главное окно — общие параметры и отчёт А4** (2026-10-09). Состав по темам —
[`backlog-map.md`](backlog-map.md); здесь — вердикты: что закрыть, что уже сделано, что делать дальше.
Статус в коде сверен с рабочей копией 2026-10-09, кроме отмеченного.

## Главный вывод

Трекер описывает продукт, которого больше нет, и отстаёт от кода в обе стороны:

- **Около трети открытых issue стоят на снятом фундаменте** — встроенный агент, локальная модель,
  проактивность внутри плагина. После #164 это не «отложено», а «не наш путь»: ИИ — внешний клиент.
- **Часть сделанного не закрыта.** Цепочка декораторов (#153), пункты окна Extensions из #164, первая
  половина #121/#122 — в коде, в трекере открыты.
- **Реальных дефектов мало, и они дешёвые** — пять штук, большинство за день.

Трекер перестал быть картой работы и стал архивом мыслей. Чистка (≈15 закрытий и 5 переписываний)
вернёт ему роль, и это дешевле любого пункта ниже.

## 1. Закрыть: фундамент снят 2026-10-08

| Issue | Почему |
| --- | --- |
| [#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80) встроенный проактивный копилот | цикл агента в процессе Revit — ровно то, что #164 убрал |
| [#115](https://github.com/Nikola1Davydov/AnalyzeTool/issues/115) внутренний агент как MCP-клиент | нет внутреннего агента |
| [#117](https://github.com/Nikola1Davydov/AnalyzeTool/issues/117) локальная модель как условие допуска | локальной модели в продукте нет ([`../entities/ollama.md`](../entities/ollama.md)) |
| [#118](https://github.com/Nikola1Davydov/AnalyzeTool/issues/118) делегация: триггер + sidecar | sidecar был мостом к встроенному агенту |
| [#133](https://github.com/Nikola1Davydov/AnalyzeTool/issues/133) встроенный чат по облачному ключу | #164 сам просит его закрыть; разбор — [`built-in-agent-plan.md`](built-in-agent-plan.md) |
| [#56](https://github.com/Nikola1Davydov/AnalyzeTool/issues/56) AI-слой + RAG | фазы 1–3 (`IChatClient`, чат, встроенный вывод) сняты; остаток (поиск по библиотеке семейств и нормам, [#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85)) — если нужен, новым узким issue |

[#116](https://github.com/Nikola1Davydov/AnalyzeTool/issues/116) (порог прерывания) — единственный из
кластера, который переживает поворот: правило «агент не имеет права на внимание» верно и для внешнего
клиента. Не закрывать — переписать как правило для MCP-уведомлений и окна активности
([`../concepts/proactivity-budget.md`](../concepts/proactivity-budget.md)).

Мысль в вики сохранена — закрытие issue её не теряет.

## 2. Сделано в коде, issue открыт

| Issue | Что есть |
| --- | --- |
| [#153](https://github.com/Nikola1Davydov/AnalyzeTool/issues/153) behaviors в `CommandQueue` | цепочка декораторов logging → gate → tracking → dispatch (`src/AnalyseTool.Core/Common/Dispatch/CommandPipeline.cs`) — закрыть |
| [#164](https://github.com/Nikola1Davydov/AnalyzeTool/issues/164) упрощение | всё, кроме живой проверки в Revit; пункты по окну Extensions сделаны — отметить и оставить один пункт |
| [#156](https://github.com/Nikola1Davydov/AnalyzeTool/issues/156) мёртвые команды Family Manager | ушли; **но** после архивации главного окна в `Commands` (`src/clientapp/src/RevitBridge.ts`) мёртвыми стали пять других — `SelectionInRevit`, `IsolationInRevit`, `GetCategoriesInRevit`, `GetDataByCategoryName`, `SetDataToParameters`. Переписать issue на них |
| [#121](https://github.com/Nikola1Davydov/AnalyzeTool/issues/121) / [#122](https://github.com/Nikola1Davydov/AnalyzeTool/issues/122) | первая половина — главное окно 2026-10-09 ([`checking-module.md`](checking-module.md)); отметить сделанное, остаток описан ниже |
| [#77](https://github.com/Nikola1Davydov/AnalyzeTool/issues/77) AI DX | цикл авторства замкнут (`GetExtensionDiagnostics`, `GetAuthoringGuide`, `list_changed`); то, что осталось, — это #165. Закрыть со ссылкой на #165 |

## 3. Дефекты — сделать первыми

| Issue | Состояние в коде | Цена |
| --- | --- | --- |
| [#143](https://github.com/Nikola1Davydov/AnalyzeTool/issues/143) нет документа → NRE | 17 мест `ActiveUIDocument.Document` в `Tools` без проверки; одна защита — в `GetModelOverview` | часы: одна обёртка в Sdk/Core, понятная ошибка вместо NRE |
| [#142](https://github.com/Nikola1Davydov/AnalyzeTool/issues/142) отмена не доходит до потока Revit | в `RevitTaskHub` / `RevitContext` токен не проверяется | день; без этого «Cancel» в окне активности — обещание |
| [#144](https://github.com/Nikola1Davydov/AnalyzeTool/issues/144) эксклюзивные `Destructive` | нет | день |
| [#103](https://github.com/Nikola1Davydov/AnalyzeTool/issues/103) `.old` на OneDrive | не проверялось | часы |
| [#104](https://github.com/Nikola1Davydov/AnalyzeTool/issues/104) зависание на 4 минуты | один случай, без следа | закрыть как «не воспроизводится»: логирующий декоратор теперь пишет исход и длительность каждого вызова, следующий случай будет виден |

И одно, что дефектом не помечено, но работает как он:
[#155](https://github.com/Nikola1Davydov/AnalyzeTool/issues/155) — во фронте нет конфигурации TypeScript (файла tsconfig), типы
не проверяет никто. Временная проверка `vue-tsc` 2026-10-09 нашла **три настоящие ошибки** в только что
написанном коде, а позже в отчёте нашлась ошибка рендера, которую проверка типов тоже бы не поймала.
Фронт растёт быстрее всего — это самый дешёвый гардрейл в списке.

## 4. Текущий фокус: MCP и кнопки

- **[#165](https://github.com/Nikola1Davydov/AnalyzeTool/issues/165) — решение, которое блокирует цикл
  авторства.** Три открытых вопроса из issue надо закрыть до кода; главный — нужна ли `BuildExtension`
  агентам с терминалом. Совет: да, но тонкая — один и тот же путь для всех клиентов дешевле, чем два
  описания цикла в `src/LLM.md`. Цена решения — .NET SDK у каждого пользователя; это меняет установщик и
  первое впечатление, поэтому решать вместе с [#93](https://github.com/Nikola1Davydov/AnalyzeTool/issues/93).
- **[#163](https://github.com/Nikola1Davydov/AnalyzeTool/issues/163) история версий** — после #165,
  потому что от формы команды зависит, что снимать
  ([`../concepts/write-safety-and-approval.md`](../concepts/write-safety-and-approval.md)).
- **[#137](https://github.com/Nikola1Davydov/AnalyzeTool/issues/137) официальный MCP-сервер Autodesk** —
  стратегически важнее, чем выглядит: если Autodesk отдаёт чтение модели бесплатно, ценность AnalyseTool
  смещается в то, чего у него нет, — свои кнопки, общие параметры, отчёт, расширения. Разведку сделать
  до того, как вкладываться в новые read-команды.
- **[#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105)** — `ExecuteRevitCode` вытесняет
  специализированные команды. Новые `GetProjectParameters` и `GetParameterReport` — проверка этой гипотезы:
  посмотреть в живой сессии, зовёт ли их агент или пишет C#.
- Безопасность MCP: [#88](https://github.com/Nikola1Davydov/AnalyzeTool/issues/88),
  [#106](https://github.com/Nikola1Davydov/AnalyzeTool/issues/106) (dry-run для `Destructive` — теперь их
  больше: `SaveSharedParameterFile`, `BindSharedParameters`),
  [#112](https://github.com/Nikola1Davydov/AnalyzeTool/issues/112) — после дефектов, до релиза.
- Зонтики [#83](https://github.com/Nikola1Davydov/AnalyzeTool/issues/83)–[#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85)
  — тела по 0,2k устарели, но **комментарии сузили каждый до конкретного остатка**: #83 — числа с единицей,
  создание системных элементов, курсорная пагинация; #84 — описания и эвалы инструментов, ресурсы и промпты
  (упираются в отсутствие глагола для ресурсов в `McpWire`); #85 — ключ документа `CreationGUID` + `PathName`.
  Не закрывать — переписать тела по последним комментариям.

## 5. Новое направление: общие параметры и отчёт

Главное окно сделало первый шаг модуля проверки ([`checking-module.md`](checking-module.md)) без самого
свода правил. Следующие шаги вытекают естественно и короче, чем выглядели в августе:

1. **Декларация** ([#121](https://github.com/Nikola1Davydov/AnalyzeTool/issues/121)): «эти параметры
   обязаны быть заполнены у этих категорий» — тогда отчёт показывает отсутствие, а не только заполненность.
   Наборы параметров отчёта уже хранятся и бывают на проект — это почти та же сущность.
2. **[#13](https://github.com/Nikola1Davydov/AnalyzeTool/issues/13) IDS сюда, а не в «не про AI».**
   Issue 2025 года описывает ровно две вкладки, которые теперь есть: таблицу параметров и «контроль
   значений». IDS — открытый стандарт требований к информации; как формат импорта/экспорта декларации из п. 1
   он даёт совместимость с Solibri и другими без своего формата.
3. **Остаток [#122](https://github.com/Nikola1Davydov/AnalyzeTool/issues/122)**: сохранять отчёт как
   шаблон (раскладка + разделы), экспорт в PDF без диалога печати, клик по диаграмме → выделение в Revit
   (у старого окна было, при архивации ушло).
4. [#57](https://github.com/Nikola1Davydov/AnalyzeTool/issues/57) (таблица поверх спецификаций) —
   родственник, но отдельный большой продукт; оставить в стороне, пока п. 1–3 не отгружены.

Платная граница ([#120](https://github.com/Nikola1Davydov/AnalyzeTool/issues/120)) проходит именно здесь:
смотреть (таблица, отчёт) — бесплатно, обеспечивать (декларация, проверка, история) — платно. Решать её
лучше до п. 1, а не после.

## 6. Отложить без изменений

Стратегические и большие — правильны, но не сейчас: модуль проверки целиком
([#119](https://github.com/Nikola1Davydov/AnalyzeTool/issues/119), [#123](https://github.com/Nikola1Davydov/AnalyzeTool/issues/123)–[#126](https://github.com/Nikola1Davydov/AnalyzeTool/issues/126),
[#131](https://github.com/Nikola1Davydov/AnalyzeTool/issues/131), [#132](https://github.com/Nikola1Davydov/AnalyzeTool/issues/132)),
конвейеры ([#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70), [#90](https://github.com/Nikola1Davydov/AnalyzeTool/issues/90)–[#92](https://github.com/Nikola1Davydov/AnalyzeTool/issues/92),
[#161](https://github.com/Nikola1Davydov/AnalyzeTool/issues/161)), распространение расширений
([#72](https://github.com/Nikola1Davydov/AnalyzeTool/issues/72), [#76](https://github.com/Nikola1Davydov/AnalyzeTool/issues/76),
[#81](https://github.com/Nikola1Davydov/AnalyzeTool/issues/81), [#87](https://github.com/Nikola1Davydov/AnalyzeTool/issues/87),
[#95](https://github.com/Nikola1Davydov/AnalyzeTool/issues/95)), подложки
[#136](https://github.com/Nikola1Davydov/AnalyzeTool/issues/136), коллизии
[#79](https://github.com/Nikola1Davydov/AnalyzeTool/issues/79), генеративный UI
[#71](https://github.com/Nikola1Davydov/AnalyzeTool/issues/71).

Ревью архитектуры ([#145](https://github.com/Nikola1Davydov/AnalyzeTool/issues/145)–[#152](https://github.com/Nikola1Davydov/AnalyzeTool/issues/152),
[#154](https://github.com/Nikola1Davydov/AnalyzeTool/issues/154), [#157](https://github.com/Nikola1Davydov/AnalyzeTool/issues/157),
[#158](https://github.com/Nikola1Davydov/AnalyzeTool/issues/158)) — не отдельным проектом, а по ходу
работы в соседнем коде. Исключение — **#157** (TS-контракт из C#-схем): 2026-10-09 типы новых команд
пришлось писать во фронте руками дважды; чем больше команд у окна, тем дороже это становится.
`RibbonHost` — 827 строк (#152), и каждая новая кнопка ленты его растит.

## 7. Мелкие заметки без тела — закрыть или перевести в идеи

[#14](https://github.com/Nikola1Davydov/AnalyzeTool/issues/14) вкладка материалов (0,1k) ·
[#62](https://github.com/Nikola1Davydov/AnalyzeTool/issues/62) Image to Revit (0,1k) ·
[#43](https://github.com/Nikola1Davydov/AnalyzeTool/issues/43) палитра команд (панель Scripts, ближайший
родственник, удалена 2026-10-08 — решить заново, нужна ли) ·
[#54](https://github.com/Nikola1Davydov/AnalyzeTool/issues/54) проверка орфографии ·
[#69](https://github.com/Nikola1Davydov/AnalyzeTool/issues/69) миграция с NUKE ·
[#74](https://github.com/Nikola1Davydov/AnalyzeTool/issues/74) лендинг. Ни одна не вредит, но вместе они
делают трекер шумным. Если на GitHub включить Discussions → Ideas, им там место.

## Предлагаемый порядок

1. **Чистка трекера** — закрыть разделы 1, 2 и 7 с комментарием-ссылкой на вики; переписать #116, #156,
   #83–#85. Полдня, и бэклог снова отражает продукт.
2. **Дефекты** #143, #142, #144 и конфигурация TypeScript + проверка типов в CI (#155).
3. **Решение по #165** вместе с установщиком (#93), затем #163.
4. **Декларация параметров + IDS** (#121 + #13) — следующая продуктовая ступень после окна 2026-10-09.
5. Разведка #137 — параллельно, это чтение, не код.

> [!warning] не проверено
> #103 и #104 в коде не воспроизводились; вердикт по ним — по тексту issue и по тому, что логирование
> с тех пор появилось. «Около трети» в главном выводе — оценка по разделам 1 и 7 (12 из 71) плюс
> зонтики и уже сделанное, не точный подсчёт.

## Связанное

- [`backlog-map.md`](backlog-map.md) · [`checking-module.md`](checking-module.md) · [`mcp-surface-state.md`](mcp-surface-state.md) · [`architecture-review-2026-09.md`](architecture-review-2026-09.md) · [`../sources/github-issues.md`](../sources/github-issues.md)
