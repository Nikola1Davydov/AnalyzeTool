# Журнал

Записи об операциях ingest / query / lint, только дозапись. Новые внизу.

Формат: `## ГГГГ-ММ-ДД — <операция>`, далее что изменилось и почему.

---

## 2026-08-31 — старт

База знаний заведена по паттерну LLM-вики.

- Вплетён [`sources/karpathy-llm-wiki-pattern.md`](sources/karpathy-llm-wiki-pattern.md) —
  из него взята структура этого хранилища.
- Вплетён [`sources/analysetool-repo-docs.md`](sources/analysetool-repo-docs.md) —
  обзор документации репозитория и AI-значимых проектов.
- Засеяны `overview.md`, `index.md`, `conventions.md`.
- Сущности: `analysetool-mcp-server`, `ollama`.
- Концепции: `architecture-overview`, `command-schema-contract`.
- `analyses/` намеренно пуст — сравнивать и решать пока нечего.

Открытые дыры отмечены на самих страницах через `status: stub` и
`> [!warning] не проверено`.

---

## 2026-08-31 — ingest: трекер задач на GitHub

Источник: [`sources/github-issues.md`](sources/github-issues.md); снимок в
[`../raw/github-issues-2026-08-31.md`](../raw/github-issues-2026-08-31.md) (88 issue,
63 открытых, только тела, ~292 КБ). Скачано через публичный API двумя страницами,
pull request'ы отфильтрованы.

Прочитано полностью: кластеры MCP ([#83](https://github.com/Nikola1Davydov/AnalyzeTool/issues/83)–[#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85),
[#88](https://github.com/Nikola1Davydov/AnalyzeTool/issues/88),
[#89](https://github.com/Nikola1Davydov/AnalyzeTool/issues/89),
[#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97)–[#113](https://github.com/Nikola1Davydov/AnalyzeTool/issues/113)),
кластер агента ([#45](https://github.com/Nikola1Davydov/AnalyzeTool/issues/45),
[#56](https://github.com/Nikola1Davydov/AnalyzeTool/issues/56),
[#71](https://github.com/Nikola1Davydov/AnalyzeTool/issues/71),
[#77](https://github.com/Nikola1Davydov/AnalyzeTool/issues/77),
[#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80),
[#115](https://github.com/Nikola1Davydov/AnalyzeTool/issues/115)–[#120](https://github.com/Nikola1Davydov/AnalyzeTool/issues/120))
и модуль проверки ([#121](https://github.com/Nikola1Davydov/AnalyzeTool/issues/121)–[#126](https://github.com/Nikola1Davydov/AnalyzeTool/issues/126)).
Просмотрено бегло: [#13](https://github.com/Nikola1Davydov/AnalyzeTool/issues/13),
[#43](https://github.com/Nikola1Davydov/AnalyzeTool/issues/43),
[#48](https://github.com/Nikola1Davydov/AnalyzeTool/issues/48),
[#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52)–[#54](https://github.com/Nikola1Davydov/AnalyzeTool/issues/54),
[#62](https://github.com/Nikola1Davydov/AnalyzeTool/issues/62),
[#64](https://github.com/Nikola1Davydov/AnalyzeTool/issues/64),
[#72](https://github.com/Nikola1Davydov/AnalyzeTool/issues/72),
[#76](https://github.com/Nikola1Davydov/AnalyzeTool/issues/76),
[#79](https://github.com/Nikola1Davydov/AnalyzeTool/issues/79),
[#87](https://github.com/Nikola1Davydov/AnalyzeTool/issues/87),
[#90](https://github.com/Nikola1Davydov/AnalyzeTool/issues/90)–[#92](https://github.com/Nikola1Davydov/AnalyzeTool/issues/92),
[#114](https://github.com/Nikola1Davydov/AnalyzeTool/issues/114).

Новые страницы:

- `concepts/deterministic-core.md` — инвариант, найденный независимо в шести планах
- `concepts/agent-legibility.md` — сцепление по ключам, экономика контекста, локализация
- `concepts/write-safety-and-approval.md` — сухой прогон, токен, гейт, граница ленты
- `concepts/long-running-calls.md` — прогресс, отмена, Tasks; четыре дефекта, одна причина
- `concepts/proactivity-budget.md` — внимание, поток Revit, деньги
- `entities/shadow-index.md` — непостроенный компонент, на который опираются пять планов
- `analyses/mcp-surface-state.md` — полевой тест 1.5, синтез и порядок работ
- `analyses/agent-hosting.md` — A/B/C, у кого инициатива, кто платит
- `analyses/backlog-map.md` — все 63 открытых issue по группам

Обновлены: `overview.md` (раздел о том, что меняет бэклог), `index.md` (полный каталог и
список дыр), `entities/analysetool-mcp-server.md` (каталог не обещает, что инструмент
работает; позиция по безопасности), `entities/ollama.md` (локальность как условие
допуска, три яруса задержки), `concepts/command-schema-contract.md` (свидетельства из
реальных сессий, два потребителя схемы).

`analyses/` больше не пуст.

Не сделано и стоит следующим: **комментарии** к issue не выкачаны — самая большая дыра
этого ingest; у модуля проверки нет собственной страницы разбора; `docs/pipeline-design.md`
(в ветке, цитируется четырьмя issue) не читался.

---

## 2026-08-31 — перевод вики на русский

Весь текст вики переведён на русский: `CLAUDE.md`, `conventions.md`, `index.md`,
`overview.md`, `log.md`, `raw/README.md` и все 15 страниц в `sources/`, `entities/`,
`concepts/`, `analyses/`.

Что осталось как было и почему:

- **Имена файлов** — латиницей, `kebab-case`. Это адреса, на которых держатся все
  относительные ссылки; правило записано в `conventions.md` и в схеме.
- **Снимок в `raw/`** не тронут: слой источников неизменяем, и там оригинальные тела
  issue, часть которых написана по-английски, часть по-русски.
- **Идентификаторы кода, имена команд, пути и названия проектов** не переводятся.

Терминология взята из самих issue, а не изобретена: теневой индекс, лента и карточки,
свод правил, порог прерывания, витрина, гейт.

---

## 2026-08-31 — схема: рабочая копия как источник для чтения

В [`../CLAUDE.md`](../CLAUDE.md) добавлен раздел «Рабочая копия рядом — её надо
читать»: раскладка репозитория с путями до всех проектов и правило, что утверждение о
коде проверяется открытием файла, а не доверием странице.

Три следствия записаны явно: проверять, а не помнить; отвечать шире, чем содержит вики
(прочитать код и вернуть ответ страницей); но код внутрь не копировать — цитировать
путём.

Уточнены заодно: операция lint и правило «проверять, прежде чем утверждать» теперь
называют `../src` прямо; [`sources/analysetool-repo-docs.md`](sources/analysetool-repo-docs.md)
помечен как живой источник, меняющийся с каждым коммитом, а не снимок;
[`overview.md`](overview.md) открывается сноской об этом для того, кто читает первым.

---

## 2026-08-31 — lint (первый с доступом к коду)

Сверено с рабочей копией на ветке `dev`. Проверялись пути и файлы, имена команд, типы и
члены, и отдельно — живы ли ещё дефекты, о которых пишет вики.

### Проверено и подтверждено

- **31 путь к артефактам кода** — все существуют, кроме двух, разобранных ниже.
- **41 имя команды** из каталога в `entities/analysetool-mcp-server.md` — все на месте
  (`ReloadExtensions` живёт как `ReloadExtensionsCommand`, имя инструмента верное).
- **SDK ровно пять файлов** — как утверждает `sources/analysetool-repo-docs.md`.
- **Номера строк `CommandQueue.cs`** — `:18` CancellationToken, `:21` Progress,
  `:26` Gate, `:116` проброс в `DispatchAsync`. Совпали точно.
- **`ModelContextProtocol` 1.3.0 в `src/Directory.Packages.props:42`** — версия и строка
  верны. Гейт-вопрос [#107](https://github.com/Nikola1Davydov/AnalyzeTool/issues/107)
  (поддерживает ли 1.3.0 спеку `2026-07-28`) офлайн не закрывается, остаётся открытым.
- **Ollama по умолчанию** — id `ollama`, «Ollama (local)», `http://localhost:11434`.
- **Маппинг `wiki-sync.yml`** — `ONBOARDING.md → wiki/Home.md`,
  `src/LLM.md → wiki/Writing-extensions-with-AI.md`.
- **`RevitAvailability.IsRevitBusy`, `CommandQueue.Untracked`** — существуют.
- **Дефект [#97](https://github.com/Nikola1Davydov/AnalyzeTool/issues/97) жив**, ровно
  на `McpBridgeServer.cs:276`. **Дефект [#112](https://github.com/Nikola1Davydov/AnalyzeTool/issues/112)
  жив** — троттлинга нет нигде. **Комментарий про array-rooted схему
  ([#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111)) на месте**,
  `Program.cs:240`.

### Устаревшие утверждения — исправлены

1. **`concepts/write-safety-and-approval.md` — статус
   [#88](https://github.com/Nikola1Davydov/AnalyzeTool/issues/88).** Страница повторяла
   пометку issue «не скомпилировано и не запущено». Неверно: код на `dev`,
   `CollectingFailuresPreprocessor` применяется в восьми местах, а
   `SwallowWarningsPreprocessor` **отсутствует в репозитории вовсе** — значит пункт
   «мигрировать слайс Families», числившийся в остатке, выполнен. Флаг `Destructive`
   стоит на всех девяти командах. Переписано, непроверенным оставлено только поведение
   в живом Revit.
2. **`analyses/mcp-surface-state.md` — P0 из
   [#83](https://github.com/Nikola1Davydov/AnalyzeTool/issues/83).** Страница числила
   весь P0 как отсутствующий. Половина про запросы уже в типах: `ElementQuery` несёт
   `ElementKind`, `BuiltInCategory`, `FamilyNameContains`, `TypeNameContains`;
   `ElementsResult` — `error` и `didYouMean`; `ElementSummary` — `FamilyId` и
   `FamilyName`. Не закрыто: размещение и геометрия элемента. Добавлена оговорка, что
   наличие полей не означает верного поведения — [#98](https://github.com/Nikola1Davydov/AnalyzeTool/issues/98)
   именно об этом.
3. **`entities/shadow-index.md`** — `ai-providers.json` подан как существующий файл. Это
   рантайм-файл в профиле, а не в репозитории; уточнено со ссылкой на
   `AiProviderRegistry.cs:49`.
4. **`concepts/long-running-calls.md`** — добавлены сверенные номера строк вместо общих
   формулировок.

Заодно подтверждено, что [#113](https://github.com/Nikola1Davydov/AnalyzeTool/issues/113)
не устарел: `categoryCounts` по-прежнему `Dictionary<string, int>` с локализованным
именем, `ElementSummary.Level` — по-прежнему строка, а `BuiltInCategory` есть только на
**входе** (`ElementQuery`), не в ответе.

### Не находки

- `docs/pipeline-design.md` отсутствует в `dev` — но вики и не утверждает обратного: он
  на ветке `claude/pipelines-plan-f8jrgf`, что подтверждено.
- Две страницы в `sources/` без файла в `raw/` — `karpathy-llm-wiki-pattern` (внешняя
  ссылка) и `analysetool-repo-docs` (рабочая копия). Оба случая описаны на самих
  страницах, это осознанное исключение.
- Битых относительных ссылок 0, страниц-сирот 0.

### Дыры — заведены, не закрыты

Термины, на которые ссылается много страниц, а своей страницы нет:

- **лента и карточки** — 9 страниц. Самый нагруженный термин без страницы; сюда сходятся
  гейт, одобрение, порог прерывания и обратная петля.
- **модуль проверки** (свод правил, папка проекта) — 7 страниц. Известная дыра, теперь с
  цифрой.
- **`CommandQueue`** — 5 страниц. Центральный механизм, описан только тонко внутри
  `concepts/architecture-overview.md`; внутренности так и не прочитаны.

---

## 2026-08-31 — закрыта дыра: лента и карточки

Заведена [`concepts/inbox-and-cards.md`](concepts/inbox-and-cards.md) — самая нагруженная
находка прошлого прохода lint (9 страниц ссылались, своей страницы не было).

Собрана из девяти issue, где лента упоминалась как деталь чужого плана:
[#79](https://github.com/Nikola1Davydov/AnalyzeTool/issues/79),
[#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80),
[#106](https://github.com/Nikola1Davydov/AnalyzeTool/issues/106),
[#115](https://github.com/Nikola1Davydov/AnalyzeTool/issues/115),
[#116](https://github.com/Nikola1Davydov/AnalyzeTool/issues/116),
[#118](https://github.com/Nikola1Davydov/AnalyzeTool/issues/118),
[#122](https://github.com/Nikola1Davydov/AnalyzeTool/issues/122),
[#123](https://github.com/Nikola1Davydov/AnalyzeTool/issues/123),
[#126](https://github.com/Nikola1Davydov/AnalyzeTool/issues/126).

Что дал сбор воедино — то, чего не видно в отдельных issue:

- **три источника карточек** (триггер агента, комментарий из чата через папку, находки
  проверки и коллизий) сходятся в одну очередь и попадают под один порог;
- **третья кнопка** — «записать правилом» рядом с «применить» и «отклонить»: это тот же
  паттерн активации, что у черновиков правил и блоков отчёта, и именно так свод растёт
  из реальных поправок;
- **исход обязан вернуться туда, откуда пришло намерение** — станция, которую легко
  потерять, а без неё канал перестают использовать.

Сверено с кодом при написании: `CommandRequest.Gate` объявлен
(`CommandQueue.cs:26`, вызовы `:84`/`:90`); канал «хост толкает в WebView» существует
(`clientapp/src/RevitBridge.ts:80`, host-initiated broadcasts без request id); а
сегодняшнее подтверждение в доке (`ScriptLauncherView.vue:513`, дублируется на `:449`)
— готовый антипример, ровно то «подтверждение без содержания», о котором пишет
[#106](https://github.com/Nikola1Davydov/AnalyzeTool/issues/106). Самой ленты нет.

Два вопроса оставлены открытыми пометками: где карточки живут между сессиями (JSON рядом
с `ai-providers.json` или `inbox\` в папке проекта — решение не принято) и стоит ли
копить принятые/отклонённые решения как few-shot примеры.

Связи проставлены из `write-safety-and-approval`, `proactivity-budget`,
`deterministic-core`, `analyses/agent-hosting`, `analyses/backlog-map` и `index.md`.
Остаются дыры: модуль проверки (7 страниц) и `CommandQueue` (5 страниц).

---

## 2026-08-31 — закрыта дыра: CommandQueue

Заведена [`entities/command-queue.md`](entities/command-queue.md) — вторая находка
прошлого прохода lint (5 страниц ссылались, файл никто не открывал).

Написана **по коду**, а не по issue: `Common/Dispatch/CommandQueue.cs` (135 строк),
`RevitTaskHub.cs` (102), `RevitAvailability.cs` (34), `CommandDispatcher.cs` (172).
Это первое применение правила «отвечать шире, чем содержит вики» из схемы.

Что дало чтение кода — то, чего нет ни в одном issue:

- **`CommandQueue` не очередь.** Собственный комментарий класса: запросы исполняются
  сразу и могут перекрываться, а сериализация живёт ниже, на внешнем событии
  `RevitTaskHub`. Воронка нужна ради *места*, куда потом лягут планирование, лимиты и
  политика. Это связывает [#112](https://github.com/Nikola1Davydov/AnalyzeTool/issues/112),
  [#120](https://github.com/Nikola1Davydov/AnalyzeTool/issues/120) и
  [#90](https://github.com/Nikola1Davydov/AnalyzeTool/issues/90) в одну точку.
- **`Execute` разбирает всю очередь целиком** — отсюда сразу и неатомарность прогона
  ([#90](https://github.com/Nikola1Davydov/AnalyzeTool/issues/90)), и «одно модальное
  окно замораживает платформу» ([#88](https://github.com/Nikola1Davydov/AnalyzeTool/issues/88)).
- **Два разных механизма определения занятости**, и различие между ними диагностическое:
  ждём и ничего не исполняется → Revit не может уйти в idle; ждём и что-то исполняется →
  просто длинная работа. Ровно это не удалось развести в
  [#104](https://github.com/Nikola1Davydov/AnalyzeTool/issues/104).
- **Класс уже один раз врал о занятости** — в `EnqueueAsync` оставлен комментарий о том,
  как порядок инкремента загонял `_pending` в −1 и `GetQueueStatus` сообщал «Revit занят»
  на простое. Стоит помнить, читая [#102](https://github.com/Nikola1Davydov/AnalyzeTool/issues/102)
  и [#104](https://github.com/Nikola1Davydov/AnalyzeTool/issues/104).
- **Три транспорта — весь список**: `ribbon` (`RibbonHost.cs:554`), `webview2`
  (`WebView2Transport.cs:86`), `mcp` (`McpBridgeServer.cs:250`). Никто не зовёт
  диспетчер напрямую.

### Попутная находка lint — исправлена

`concepts/command-schema-contract.md` утверждал, вслед за
[#89](https://github.com/Nikola1Davydov/AnalyzeTool/issues/89), что `BuildInputSchema`
обрезает схему на 4096 символах при регистрации, и это смертельно для рёбер конвейера.
**Устарело: вторая половина [#89](https://github.com/Nikola1Davydov/AnalyzeTool/issues/89)
сделана.** Метода `BuildInputSchema` в коде нет; есть общий `BuildSchema(Type?)`, схема
хранится целиком, а обрезка переехала к своему потребителю — `SchemaListing.Compact`
(`MaxChars = 4096`, откат в `FreeFormObject`). `CommandRegistration` несёт
`InputSchemaJson` и `OutputSchemaJson`. Не сделана первая половина — типы результата по
слайсам.

Связи: `concepts/architecture-overview.md` и `index.md`. Из дыр остаётся модуль проверки
(7 страниц).

---

## 2026-08-31 — закрыта дыра: модуль проверки + частичный ingest комментариев

Заведена [`analyses/checking-module.md`](analyses/checking-module.md) — последняя находка
прохода lint (7 страниц ссылались, своей страницы не было).

Перед написанием выкачаны **комментарии** к [#119](https://github.com/Nikola1Davydov/AnalyzeTool/issues/119)
(3) и [#53](https://github.com/Nikola1Davydov/AnalyzeTool/issues/53) (1) — первый заход в
дыру, которую прошлый ingest оставил целиком. Оправдалось: там лежат решения, которых нет
в телах issue.

### Что дали комментарии

- **Решение об объёме первой версии (28.08).** Геометрию не берём; коллизии остаются в
  [#79](https://github.com/Nikola1Davydov/AnalyzeTool/issues/79). Причём граница проходит
  не «параметры против геометрии», а **хранимое против вычисляемого** — и это шире:
  связи, которые Revit хранит (хост двери, привязка стены к уровню, замкнутость
  помещения), попадают в v1 даром. Ограничение совпадает с границей, за которой
  архитектура остаётся дешёвой: хранимое обслуживается индексом и не трогает поток Revit,
  значит проверку можно гонять на каждой синхронизации.
- **Разворот подхода к авторству (28.08).** Экран «напишите правило» отвергнут;
  распределение значений само по себе почти готовое правило. Побочно исчезает нужда в
  пробном прогоне: правило, собранное из наблюдённых значений, не бывает мёртвым.
  Отсюда же — один экран инвентарей с переключателем вместо вкладки на каждый вид
  данных, и правило как отдельная сущность с областью, автором и историей срабатываний.
- **Свод определяет схему теневого индекса** — «ключевые параметры» перестают быть
  догадкой. Перенесено в [`entities/shadow-index.md`](entities/shadow-index.md).
- **Список ловушек в данных Revit** — тип против экземпляра, три вида идентичности
  параметров, тип хранения, единицы, формульные, и «параметра нет» ≠ «параметр пуст».
- **Интеграционная картина из [#53](https://github.com/Nikola1Davydov/AnalyzeTool/issues/53)**
  — IDS/buildingSMART как главный мост, проверка IFC, BCF, ACC. Помечена как список
  возможностей, а не действующий план: комментарий старше решения об объёме, и его
  Supabase-часть конфликтует с [#124](https://github.com/Nikola1Davydov/AnalyzeTool/issues/124)
  и [#117](https://github.com/Nikola1Davydov/AnalyzeTool/issues/117).

### Связи

`index.md` (каталог и снятая дыра), `analyses/backlog-map.md` (раздел переписан со
ссылкой), `entities/shadow-index.md` (схема из свода), `sources/github-issues.md` (статус
комментариев уточнён, добавлена команда для добора).

Все три дыры прошлого lint закрыты. Осталась одна, и она же исходная: **комментарии к
~35 issue**, включая [#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80) и
[#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70) по четыре.

---

## 2026-08-31 — ingest: все комментарии к issues

Забраны **все 46 комментариев** одним проходом через репозиторный эндпоинт (одна страница
— это весь объём). Снимок:
[`../raw/github-issue-comments-2026-08-31.md`](../raw/github-issue-comments-2026-08-31.md),
24 issue, ~69 тыс. символов. Крупнейшие — [#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70)
(14 тыс.) и [#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80) (11 тыс.).

**Главный вывод о самом источнике:** комментарии здесь не обсуждение, а **ревизии плана**.
Тело issue часто описывает более раннее состояние мысли, чем последний комментарий под
ним, и расходится с ним по существу. Записано на странице источника как правило чтения.

### Что вплетено

- **Разворот решения о хранилище** → [`entities/shadow-index.md`](entities/shadow-index.md).
  В июле индекс отложили в пользу Fingerprints ([#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52),
  Parquet + DuckDB); в августе вернули и сдвинули вперёд, потому что инвентари данных —
  это буквально запросы к индексу, и он получает первый UI. Плюс два потребителя с разными
  требованиями и продуктовое назначение снапшотов.
- **Microsoft Agent Framework** → [`analyses/agent-hosting.md`](analyses/agent-hosting.md).
  GA 03.04.2026, закрывает большую часть «построить самим» в варианте B; оценка фазы 1
  падает с недель до дней. Границы обязательные: точка принуждения остаётся в
  `CommandQueue`, у MAF не должно быть пути к Revit в обход очереди.
- **Инвариант топологии графа** → [`concepts/deterministic-core.md`](concepts/deterministic-core.md).
  Ребро из AI-узла не может вести в Destructive-команду напрямую. «ИИ никогда не пишет в
  модель без человека» становится свойством графа, а не дисциплины — самая сильная форма
  принципа во всём бэклоге. Там же таксономия AI-узлов и Context-Based Analysis из
  слитого [#47](https://github.com/Nikola1Davydov/AnalyzeTool/issues/47).
- **Память из карточек делает ошибки долговечными** → [`concepts/inbox-and-cards.md`](concepts/inbox-and-cards.md).
  Закрывает вопрос, который эта страница оставляла открытым: один устало нажатый
  «применить» начинает учить агента и дальше подтверждается сам собой. Отклонения должны
  весить столько же, сколько одобрения.
- **Выделение как третий канал** → [`concepts/agent-legibility.md`](concepts/agent-legibility.md).
  Агент, отвечающий выделением, даёт обратную связь по **пониманию**, а не по результату.
- **Elicitation как официальный механизм для сухого прогона** → [`concepts/write-safety-and-approval.md`](concepts/write-safety-and-approval.md).
  `requestState` и есть `confirmToken`, и это один логический вызов вместо двух, которые
  пришлось бы коррелировать и протухать самим.
- **Контракт устоял под давлением третьего клиента** → [`concepts/command-schema-contract.md`](concepts/command-schema-contract.md).
  Пять предложенных флагов SDK отпали при проверке по коду, уцелел только `OutputType`.
- **Ограничение SqliteVec и таксономия узлов** → [`analyses/backlog-map.md`](analyses/backlog-map.md).
  SqliteVec нельзя тащить в процесс Revit: нативное расширение пинит collectible ALC.
- **`GetAuthoringGuide` как ресурс** → там же. Инструмент требует, чтобы агент додумался
  его позвать, а додумывается он уже после решения, на которое гайд должен был повлиять.

### Новый дефект, найденный и подтверждённый по коду

**Агент слеп.** `GetFamilyPreview` отдаёт PNG строкой `data:image/png;base64,…`
(`GetFamilyPreview.cs:61`), не помечен `HiddenFromMcp`, а `AnalyseTool.Mcp/Program.cs`
умеет только `TextContentBlock` (строки 133, 149). Агент получает килобайты base64 как
текст и увидеть их не может. Записано в
[`analyses/mcp-surface-state.md`](analyses/mcp-surface-state.md).

### Исправленные устаревания

- `GetModelOverview` числился отсутствующим — **отгружен** (подтверждено по коду).
- [#111](https://github.com/Nikola1Davydov/AnalyzeTool/issues/111) описывался шире, чем
  есть: `GetElements` уже завёрнут в `ElementsResult`; голым массивом отвечает только
  `GetCategoriesInRevit` (подтверждено по коду).
- [#89](https://github.com/Nikola1Davydov/AnalyzeTool/issues/89) числился наполовину
  невыполненным — `OutputType` встречается в 54 файлах и обеспечивается CI, значит типы
  результата по слайсам сделаны.
- К [#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105) добавлено, что
  тестировать негде: `AnalyseTool.Test` — один `UnitTest1.cs`, ссылающийся на
  `AnalyseTool.App`, то есть тянущий Revit (подтверждено по коду).

Исходная дыра ingest закрыта. Остались: тела закрытых issue и `docs/pipeline-design.md`
на ветке `claude/pipelines-plan-f8jrgf` — теперь крупнейший непрочитанный источник.

---

## 2026-08-31 — ingest: docs/pipeline-design.md с ветки

Прочитан целиком (51 КБ) через `git show claude/pipelines-plan-f8jrgf:docs/pipeline-design.md`.
Заведён источник [`sources/pipeline-design-doc.md`](sources/pipeline-design-doc.md). В
`raw/` не копировался: он версионируется рядом и достаётся одной командой.

Это единственный источник, где решения подкреплены **замерами в живом Revit**, а не
рассуждением.

### Что вплетено

- **«Схема, которая лгала»** → [`concepts/command-schema-contract.md`](concepts/command-schema-contract.md).
  Первый конвейер, написанный агентом без подсказки, пропустил все 187 типов семейств в
  очистку, потому что условия фильтра легли ключом не туда и были молча выброшены.
  Четыре дефекта, каждый — правило; самое едкое: `SavePipeline` переserialize'ил файл
  автора, и **улику уничтожил акт её сохранения**. Плюс `JToken` в объявлении свойства
  публикует схему, в которой скаляр невозможен, — агент физически не мог выразить условие
  против нашего же контракта.
- **Замеры вместо «не проверено»** → [`concepts/write-safety-and-approval.md`](concepts/write-safety-and-approval.md).
  Конвейер очистки: ~15 транзакций, ~340 предупреждений, **ни одного модального окна**.
  Препроцессор транзакции срабатывает первым; `FailuresProcessing` — рутинное событие на
  каждом коммите. Открытым остался ровно один вопрос — видит ли обработчик `count > 0` там,
  где препроцессора нет.
- **Одобрение — фильтр, а не барьер** → туда же. Прогон не встаёт: прошедшее предикат
  записывается, остальное паркуется карточкой. Плюс таблица силы условий автоприёма
  (самооценка уверенности модели — слабейшее и наименее надёжна на нетипичных случаях) и
  инвертированный список разрешений, из-за которого любая сторонняя команда строга по
  умолчанию.
- **AI — причина существования конвейеров, а не одна из функций** →
  [`concepts/deterministic-core.md`](concepts/deterministic-core.md). Детерминированный
  сценарий отстаивали и отказались: проверку, которую можно специфицировать, дешевле
  обслуживает команда с диалогом. Плюс три правила `AiTransform` — сопоставление по
  индексу, а не позиции; неотвеченная строка возвращается и говорит об этом; берутся только
  объявленные поля.
- **Почему модель для узла не берут через MCP** → [`analyses/agent-hosting.md`](analyses/agent-hosting.md).
  MCP инвертирован: узлу нужно *быть* вызывающим. Sampling в протоколе есть, но
  `RevitBridgeClient` — соединение на запрос без корреляции по id (подтверждено по коду),
  так что бридж ничего инициировать не может.
- **Запутанный заместитель** → [`entities/command-queue.md`](entities/command-queue.md).
  `SavePipeline` выставлен агенту, `RunPipeline` нет: узлы диспатчатся под его
  идентичностью, и агент дотянулся бы до команд, в которых ему отказано напрямую.
- **Правила как данные** → [`analyses/checking-module.md`](analyses/checking-module.md).
  Единственный детерминированный случай, переживший отказ, — правила, меняющиеся от бюро к
  бюро. Подтверждение модуля проверки с неожиданной стороны.

### Исправлены два устаревания, одно — моё собственное

1. **Про тесты.** В прошлом проходе я записал со слов комментария к
   [#70](https://github.com/Nikola1Davydov/AnalyzeTool/issues/70), что Revit-free тесты
   размещать негде. Неверно: в `dev` есть `AnalyseTool.Core.Tests` и
   `AnalyseTool.Tools.Tests`. Настоящая проблема хуже и подтверждена по коду — **CI не
   запускает тесты вообще**: `ci.yml` делегирует в NUKE-цель `Ci`, а та собирает три года
   Revit, проверяет пакет SDK и шаблон расширения; `dotnet test` не встречается нигде в
   `src/build/`. Гардрейл, который никто не запускает, не гардрейл.
2. **`onFailure`.** Записанное со слов комментария «continue / stopNode / stopPipeline,
   умолчание stopPipeline для Destructive» неверно. По документу значений два, умолчание
   `stop` для любого узла, `stopNode` намеренно не в v1, а отмена ловится раньше и
   `onFailure` не спрашивают — иначе узел с `continue` проглотил бы Stop пользователя.

Не взято намеренно: устройство редактора ([#91](https://github.com/Nikola1Davydov/AnalyzeTool/issues/91))
— порты, идентификаторы узлов, компоновка канвы. UI за гейтом, вне охвата вики.
