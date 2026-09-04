---
type: analysis
updated: 2026-09-04
status: draft
sources: [../sources/github-issues.md, ../sources/analysetool-repo-docs.md]
---

# Модельный индекс на SQLite: план

План постройки теневого индекса ([`../entities/shadow-index.md`](../entities/shadow-index.md))
в форме, которую предложил владелец 2026-09-04: аддин один раз выгружает модель в SQLite
(элементы, типы, параметры, уровни, помещения, bbox — без тяжёлой геометрии), подписка на
`DocumentChanged` даёт списки added / modified / deleted и обновляет базу инкрементально,
агент через MCP-инструмент делает SQL-запросы вне потока Revit, а живой API остаётся для
того, что нужно «сейчас»: геометрия, выделение, запись.

Страница отвечает на три вопроса по порядку: **что из этого вики уже решила** (и где
предложение с ней расходится), **как это ложится на контракт зависимостей**, **в каком
порядке строить и как понять, что готово**. Код — только после согласования развилок из
раздела «Решить до кода».

## 1. Сверка с вики и бэклогом

### Совпадает — и уже решено, заново не обсуждаем

| Предложение | Где это уже записано | Что берём дословно |
| --- | --- | --- |
| слепок id → категория, тип, уровень, параметры; фон, чанки, инкремент | [#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80), [`../entities/shadow-index.md`](../entities/shadow-index.md) | «индекс — восстановимый кэш, истина — модель»; **никогда не в RVT** через Extensible Storage |
| обработчик `DocumentChanged` только пишет id и выходит | [#118](https://github.com/Nikola1Davydov/AnalyzeTool/issues/118), слои 0–3 | дебаунс, слияние, чтение чанками по 20–30 в паузу `Idling`, прерываемость: работа человека всегда выше |
| ключ — модель, а не путь к файлу | [#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85), закрыто тестом `src/AnalyseTool.RevitTests/DocumentIdentityTests.cs` | `CreationGUID` плюс `PathName`; для worksharing — GUID центральной модели |
| «без тяжёлой геометрии» | граница «хранимое против вычисляемого», [`checking-module.md`](checking-module.md) | bbox и точка расположения — хранимое и дешёвое; пересечения, зазоры, вложенность — вне объёма |
| «анализ — SQL, действия — живые инструменты по id» | [#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52) п. 18–20: `QueryFingerprint(sql)`, freshness-гейт | штамп свежести на каждом ответе; агент не строит «аналитику» на живых вызовах |
| индекс — ключевой камень этапа 2 | [`roadmap.md`](roadmap.md), «Этап 2 — фундамент», п. 3 | шесть направлений упираются в один компонент; первый UI — инвентарь |
| ответы несут языконезависимые ключи | [#113](https://github.com/Nikola1Davydov/AnalyzeTool/issues/113), [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md) | в схеме с первого дня: `built_in_category`, `built_in_parameter`, GUID общих параметров, `level_id` |

Отдельно: [#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85) сам называет момент,
когда SQLite становится оправданным — *«element cache: a 50k-element projection in JSON
filtered with LINQ is slow and memory-hungry»*. Это ровно наша постановка. Триггер достигнут не
по факту замера, а по определению задачи.

**Предусловия из дорожной карты выполнены.** [`roadmap.md`](roadmap.md) просил не начинать с
индекса, «потому что он самая интересная часть», пока не сделан этап 1: #97, #98, #100, #102,
#128, #113 и вопрос #85 про `CreationGUID` — все закрыты 2026-09-02
([`backlog-map.md`](backlog-map.md)). Два действия «не код» (приватный репозиторий, разметка
BIM-руководства) индекс не блокируют — они про платный модуль поверх него.

### Расходится — четыре развилки

**1. SQLite вместо LiteDB.** Вики трижды выбирала LiteDB
([#80](https://github.com/Nikola1Davydov/AnalyzeTool/issues/80),
[#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85),
[#56](https://github.com/Nikola1Davydov/AnalyzeTool/issues/56)) с одним аргументом: чисто
managed, нет нативной `e_sqlite3.dll` в общем процессе Revit, где другой аддин мог загрузить
свою версию, и нет пиннинга collectible ALC. Аргумент верный, но сегодня он перевешивается:

- **Агенту нужен SQL** — это вся суть предложения. У LiteDB свой диалект, которого модель не
  знает; каждый запрос агента — угадывание с ошибками. SQLite агент пишет с первой попытки.
- **Файл читается вне Revit чем угодно** — worker
  ([#131](https://github.com/Nikola1Davydov/AnalyzeTool/issues/131)), headless-ответчик
  ([#125](https://github.com/Nikola1Davydov/AnalyzeTool/issues/125)), DuckDB, Python, любой
  просмотрщик. LiteDB читает только .NET с той же библиотекой.
- **Риск нативной DLL снимается двумя способами**, и оба уже есть в репозитории:
  - `SQLitePCLRaw.bundle_winsqlite3` — биндинг к `winsqlite3.dll` из `System32`. Windows 10+
    возит её сама, Revit живёт только на Windows. Мы **не везём нативного файла вовсе**, и
    конфликт имён с чужой `e_sqlite3.dll` невозможен — модули разные.
  - запасной путь — `bundle_e_sqlite3` внутри изолированного ALC лаунчера:
    `src/AnalyseTool.Launcher/IsolatedAssemblyLoadContent.cs` уже переопределяет
    `LoadUnmanagedDll` через `AssemblyDependencyResolver`, то есть наша копия грузится по
    полному пути из папки аддина, а не по имени.
- **Ограничение, которое остаётся**: SQLite живёт **в платформе** (App/Core в ALC лаунчера) и
  никогда в расширении — `src/AnalyseTool.Core/Common/Extensions/ExtensionLoadContext.cs`
  нативные библиотеки не грузит и collectible. Отсюда следствие для SDK, см. развилку 4.

Решение: **SQLite**, LiteDB снимается. Что именно из двух бандлов — по итогам спайка (фаза 0).

**2. Parquet + DuckDB ([#52](https://github.com/Nikola1Davydov/AnalyzeTool/issues/52)) не
отменяется, а сужается.** Правило #80 «живое и мутируемое → база, неизменяемые слепки → Parquet»
остаётся с заменой LiteDB на SQLite. Но для первой версии слепок — это просто
`VACUUM INTO 'snapshot.db'`: атомарный один файл, та же схема, читается worker'ом без второго
формата. Parquet и DuckDB-WASM во фронте — когда появится дифф версий для отчёта
([#122](https://github.com/Nikola1Davydov/AnalyzeTool/issues/122)), не раньше.

**3. «Схему определяет свод правил» против «выгрузить параметры целиком».**
[`checking-module.md`](checking-module.md) записал: индексировать ровно те параметры, которые
называют правила, не тащить всё подряд. Но там же — два потребителя с разными требованиями:
инвентарю нужно *широко и мелко* (все значения, распределение — «уже почти правило»), своду —
*узко и глубоко*. Первый потребитель после агента — инвентарь, и он без полной выгрузки не
работает. Предложение: **все параметры, long-формат, нормализованные** (определение параметра
отдельно от значений); «что называют правила» становится приоритетом *инкрементального*
обновления, а не границей схемы. Гейт — замер в фазе 0: если полная выгрузка параметров
реальной модели (8 410 элементов из
[`../sources/field-test-2026-09-02.md`](../sources/field-test-2026-09-02.md)) не укладывается в
фоновую сборку чанками без заметной паузы — включается фильтр отобранных параметров, и это
настройка, а не другая архитектура.

**4. Где исполняется SQL.** Владелец сказал «через MCP-тул». Два места:

| | В exe, читая файл напрямую | В процессе, обычной командой `QueryModelIndex` |
| --- | --- | --- |
| поток Revit | не нужен | не нужен — команда не зовёт `RunInRevitAsync` |
| отвечает при занятом Revit | да | да: `CommandQueue` ничего не сериализует, сериализует только `RevitTaskHub` ([`../entities/command-queue.md`](../entities/command-queue.md)) |
| отвечает при закрытом Revit | да | нет |
| проходит через единственную дверь (гейт, лицензия, троттлинг) | **нет** — обход `CommandQueue` | да |
| доступно WebView и ленте | нет | да |
| вторая копия SQLite | в exe | нет |

Решение: **команда в процессе.** «Отвечать при закрытом Revit» — работа worker'а над слепком
([#131](https://github.com/Nikola1Davydov/AnalyzeTool/issues/131)), не exe. Единственная дверь
остаётся единственной.

## 2. Архитектура по контракту зависимостей

Ни одной новой стрелки в таблице `CLAUDE.md`. Новое — внутри существующих проектов, плюс одно
осознанное расширение SDK в фазе 4.

```
Revit ──DocumentChanged──► ChangeJournal (Core, кольцевой буфер, микросекунды)
                                  │ дебаунс + Idling
                                  ▼
                           ModelIndexer (Core) ──короткие слоты RevitTaskHub──► читает элементы чанками
                                  │
                                  ▼
                        %LOCALAPPDATA%\AnalyseTool\models\<model-key>\index.db   (SQLite, WAL)
                                  ▲                          ▲
     QueryModelIndex (Core, ReadOnly, без потока Revit) ─────┘         VACUUM INTO → папка проекта (#124) → worker (#131)
                 ▲
   webview2 · mcp · ribbon · agent — через CommandQueue, как всё
```

### Core — механизм

`src/AnalyseTool.Core/Common/Index/` (новая папка; Core уже ссылается на RevitAPI и уже
подписывается на события `UIApplication` в `DocumentTracker.cs`, так что дом правильный):

- `ChangeJournal` — обработчик `Application.DocumentChanged`: `GetAddedElementIds`,
  `GetModifiedElementIds`, `GetDeletedElementIds`, `GetTransactionNames`, ключ документа →
  кольцевой буфер, выход. Переполнение буфера — не потеря, а пометка `stale` с последующей
  сверкой (ниже).
- `ModelIdentity` — ключ модели: `CreationGUID` + `PathName`, для worksharing GUID центральной.
- `ModelIndexStore` — схема, миграции по `schema_version`, единственный писатель, WAL, пул
  читателей. `Microsoft.Data.Sqlite.Core` + выбранный бандл, версии в
  `src/Directory.Packages.props`.
- `ModelIndexer` — первичная сборка и применение дельт. Каждый заход на поток Revit —
  короткий слот через `RevitTaskHub` (цель: десятки миллисекунд, порция 100–200 элементов),
  между слотами — пауза, если `RevitAvailability` говорит «занят» или пришёл новый
  `DocumentChanged`. Прогресс — в `ActivityIndicator`, который уже умеет показывать долгую
  работу ([`../concepts/long-running-calls.md`](../concepts/long-running-calls.md)).
- `IndexScheduler` — дебаунс 1–2 с, ожидание `Idling` (единственный обработчик хоста в
  `DockPaneHost.OnIdling` штампует доступность; планировщику нужен свой крючок от него),
  явные моменты сброса: `DocumentSaved`, `DocumentSynchronizedWithCentral`, закрытие документа.
- `SqlGuard` — то, что делает произвольный SQL от модели безопасным: соединение
  `Mode=ReadOnly`, `PRAGMA query_only`, authorizer, который пропускает только чтение (никаких
  `ATTACH`, `PRAGMA` с записью, функций с побочным эффектом), ровно один statement, `LIMIT` по
  умолчанию 200 и потолок 2 000, таймаут через `sqlite3_interrupt` от `CancellationToken`.
  **Тестируется на ярусе 1 без Revit** — SQLite в памяти, и это самый дешёвый тест из всех,
  что у нас есть.

Извлечение значений параметров сегодня живёт в Tools
(`src/AnalyseTool.Tools/Shared/ParameterExtensions.cs`: `DescribeUnits`, чтение значения), а
Core на Tools ссылаться не может. Для первой версии — своя копия ~100 строк в Core с пометкой;
если дублирование начнёт расти — перенос в Sdk как контрактное решение, не ProjectReference.

### Команды — в Core, `Features/Index/`

| Команда | Что делает | Метаданные |
| --- | --- | --- |
| `QueryModelIndex { sql, args?, limit? }` | SQL к индексу активного документа; ответ `{ freshness, columns, rows, rowCount, truncated, elapsedMs, hint }` | `ReadOnly`, `InputType`, `OutputType`, видна MCP |
| `GetModelIndexSchema` | DDL таблиц и представлений, число строк, топ категорий, пять примеров запросов | `ReadOnly`; звать раз в сессию — ярус «на установку» из [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md) |
| `GetModelIndexStatus` | `absent · building(coverage) · ready · reconciling · stale`, `lastSyncUtc`, `pendingChanges`, размер | `ReadOnly`; в `Untracked` рядом с `GetQueueStatus`, чтобы не засорять лог |
| `RebuildModelIndex { scope? }` | полная пересборка тем же кодом, что первичная | `ReadOnly` по модели, но дорогая — описание говорит цену |

Core-команды освобождены от правила `OutputType` в `Check-Schemas.ps1`, но эти четыре его
объявляют: агент сверяет `structuredContent` со схемой, и урок #98 не хочется учить дважды.
Тест контракта схем (`src/AnalyseTool.Tests/SchemaContractTests.cs`) подхватит их сам.

### App — только проводка

Подписка `DocumentChanged` в `AnalyseToolBootstrap.Initialize` (там есть `UIApplication` и
валидный API-контекст), крючок `Idling` из `DockPaneHost`, событие `IndexChanged` в WebView по
существующему host-initiated каналу (`src/clientapp/src/RevitBridge.ts`), состояние индекса в
полосе док-панели рядом с занятостью Revit, тумблер «индекс» в Settings.

### Sdk — одно осознанное расширение, фаза 4

Инвентарь живёт в Tools, движок правил — в платном расширении
([#120](https://github.com/Nikola1Davydov/AnalyzeTool/issues/120)), и обоим нужен индекс из
C#. Расширение не может принести свой SQLite (см. развилку 1), значит платформа обязана дать
доступ через контракт: `IModelIndex` с `QueryAsync(sql, args, ct)` и `Status`, доступный из
`IRevitContext`. Это ровно механизм из #120 — «чего не хватит флагманскому платному модулю,
обязано попасть в SDK». Страницы Vue обходятся без этого: `AT.invoke("QueryModelIndex", …)`.
Аддитивно, minor SemVer, вместе с `ONBOARDING.md` и `src/LLM.md`.

### Mcp, Mcp.Bridge, Launcher — ничего

Команды попадают в `tools/list` сами; `list_changed` уже работает. Единственная работа на
стороне MCP — **описания** команд: это промпт-инжиниринг
([`../concepts/command-schema-contract.md`](../concepts/command-schema-contract.md)), и от него
зависит, пойдёт ли агент в SQL или по привычке в `ExecuteRevitCode`
([#105](https://github.com/Nikola1Davydov/AnalyzeTool/issues/105)).

## 3. Схема индекса, версия 1

Нормализованные таблицы для писателя, представления для агента. Значения чисел — в **экранных
единицах документа**, как везде после #113; текст — `AsValueString` для отображения. Первичный
ключ элемента — `UniqueId`: Revit переиспользует `ElementId` после удаления
([#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85)), а удалённые строки нам нужны.

```sql
meta            (key, value)                        -- schema_version, model_key, creation_guid, central_guid,
                                                    -- path, revit_version, language, display_units,
                                                    -- built_at, last_sync_utc, status
elements        (unique_id PK, element_id, is_type, category, built_in_category, category_type,
                 name, family_name, type_name, type_element_id, level_id, workset_id,
                 host_element_id, room_element_id, loc_x, loc_y, loc_z,
                 bbox_min_x, bbox_min_y, bbox_min_z, bbox_max_x, bbox_max_y, bbox_max_z,
                 version_guid, updated_at, deleted_at)
parameter_defs  (param_id PK, name, built_in_parameter, shared_guid, storage_type, spec, unit,
                 is_read_only, is_type_parameter)
parameter_values(element_id, param_id, value_text, value_num, value_id, PRIMARY KEY (element_id, param_id))
levels          (element_id PK, unique_id, name, elevation)
rooms           (element_id PK, unique_id, number, name, level_id, area, perimeter, is_placed)
worksets        (workset_id PK, name, kind, owner)
changes         (seq PK, utc, transaction_name, element_id, kind, applied)   -- журнал, applied=0 → ещё не в индексе

-- представления для агента и инвентаря
v_elements      -- элементы с именами уровня, ворксета, типа; deleted_at IS NULL
v_parameters    -- element_id, name, built_in_parameter, value_text, value_num, unit, is_type_parameter
v_distribution  -- param name × value_text → count   (строка инвентаря «BRANDSCHUTZ: EI30 214 · пусто 44»)
```

Что сознательно **не** в v1: геометрия сверх bbox и точки; помещение по `GetRoomAtPoint`
(берём только хранимое: `FamilyInstance.Room`, `FromRoom`/`ToRoom`); связанные модели; виды и
листы (дёшево, `ViewsSheetsService` есть — v1.1); FTS5 по именам семейств
([#85](https://github.com/Nikola1Davydov/AnalyzeTool/issues/85) P3 — другой продукт).

Схема — **wire-контракт** в дисциплине `McpWire`
([#131](https://github.com/Nikola1Davydov/AnalyzeTool/issues/131)): worker читает тот же файл,
поэтому DDL и версия лежат в одном C#-файле, который позже линкуется и в worker.

## 4. Инкремент: как дельта доезжает до базы

1. **Слой 0** — обработчик пишет `(id, kind, имя транзакции, utc)` в буфер. Ничего не читает.
   Undo и Reload Latest приходят тем же событием, отдельной ветки не нужно.
2. **Слияние** — элемент, изменённый пять раз, читается один раз; удалённый после добавления —
   не читается вовсе.
3. **Момент** — 1–2 с тишины, затем `Idling`. Новый `DocumentChanged` во время применения —
   текущая порция дописывается, остальное ждёт следующей паузы.
4. **Применение** — удалённые → `deleted_at` без чтения; добавленные и изменённые → короткие
   слоты `RevitTaskHub` по 100–200 id, полная строка плюс параметры; `doc.GetElement` вернул
   null → тоже tombstone.
5. **Сверка вместо доверия** — при открытии документа и при `stale`: один проход по
   `(ElementId, VersionGuid)` всех элементов без чтения параметров — дёшево, — и перечитываются
   только те, у кого `version_guid` разошёлся или кого нет в базе. Это и есть «переиндексация
   тем же кодом», только не с нуля: модель, правленную без плагина, догоняем за секунды.
6. **Первичная сборка** — то же самое при пустой базе: фон, чанки, прогресс в окне
   активности, документ не блокируется никогда; до `ready` агент получает `building` с
   покрытием, а не пустой ответ.
7. **Слепок** — на `DocumentSynchronizedWithCentral` / `DocumentSaved`: `VACUUM INTO` в
   `.cache\` папки проекта ([`../entities/project-folder.md`](../entities/project-folder.md)),
   если папка настроена.

Состояние — конечный автомат `absent → building → ready ⇄ reconciling`, плюс `stale` из
переполнения буфера или падения Revit (флаг «сессия закрыта чисто» в `meta`).

## 5. Поверхность для агента

`QueryModelIndex` — единственный новый глагол чтения. Его описание обязано сказать:
таблицы и представления есть в `GetModelIndexSchema`; ключи — `unique_id`, `element_id`
переиспользуется; `deleted_at IS NULL` в `v_elements` уже применён; числа в экранных единицах;
`LIMIT` по умолчанию; ответ несёт `freshness` — и что при `building`/`stale` агент
предупреждает человека, а не молчит ([#125](https://github.com/Nikola1Davydov/AnalyzeTool/issues/125):
«ответ без штампа — враньё по умолчанию»).

Живые команды остаются для того, что индекс не может: `SelectionInRevit`, `IsolationInRevit`
(ответ выделением стоит ноль токенов — [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md)),
`SetDataToParameters`, геометрия. После записи агент ничего не инвалидирует руками: запись
порождает `DocumentChanged`, индекс догоняет сам — паттерн #52 п. 19 без его ручного шага.
Описание `GetElements` получает одну фразу: для вопросов через всю модель — `QueryModelIndex`.

Метрика — **вызовов на задачу** ([#84](https://github.com/Nikola1Davydov/AnalyzeTool/issues/84)):
база 8 вызовов на размещение двух экземпляров; «найти все двери шире метра» сейчас — три-четыре
вызова с `parameterNames`, ожидание — один. Если число не сдвинулось, мы добавили таблиц и
рассказали себе историю.

## 6. Порядок постройки

Каждая фаза отгружается сама по себе и полезна без следующей.

| Фаза | Что | Готово, когда | Тесты |
| --- | --- | --- | --- |
| **0 · спайк** | `winsqlite3` в Revit 2025 через ALC лаунчера: `sqlite_version()`, JSON, WAL на `%LOCALAPPDATA%`; перезагрузка расширений не ломает соединения; полная выгрузка параметров реальной модели чанками — время и размер файла; решение по бандлу и по развилке 3 | цифры записаны в [`../entities/shadow-index.md`](../entities/shadow-index.md) | — (это замер) |
| **1 · хранилище и сборка** | `ModelIndexStore`, схема v1, `ModelIdentity`, первичная сборка чанками, `GetModelIndexStatus`, `RebuildModelIndex`; без UI | реальная модель индексируется в фоне, Revit отзывчив, статус доходит до `ready` | ярус 1: схема, миграции, идентичность (SQLite в памяти); ярус 3: `SeededModel` → четыре стены и уровень в базе |
| **2 · SQL для агента** | `QueryModelIndex`, `SqlGuard`, `GetModelIndexSchema`, описания; eval из пяти задач через настоящий клиент | «двери шире метра» — один вызов; `ATTACH`/`INSERT`/два statement'а — отказ с кодом | ярус 1: guard на все запрещённые формы, `LIMIT`, отмена; ярус 2: инструмент в `tools/list`, `structuredContent` по схеме |
| **3 · инкремент** | `ChangeJournal`, планировщик, применение дельт, tombstone'ы, сверка по `VersionGuid`, `stale` | правка стены видна в SQL через ~2 с без заметной паузы в Revit; удаление — `deleted_at`; модель, правленная без плагина, догнана при открытии | ярус 3: изменить/удалить/undo на `SeededModel` → строки; ярус 1: слияние журнала как чистая функция |
| **4 · потребители** | `IModelIndex` в Sdk; инвентарь на индексе (`ParameterFilledEmptyView` / `ParameterValueCheckView` сейчас сканируют модель через `GetDataByCategoryName`); `IndexChanged` в WebView; тумблер в Settings; `VACUUM INTO` в папку проекта | инвентарь открывается на настоящей модели за секунды, а не сканирует её (критерий из [`roadmap.md`](roadmap.md)) | ярус 1: контракт схемы для новых типов; ярус 2: команда видна |

Припарковано с условием: Parquet и дифф версий — когда есть отчёт с динамикой
([#122](https://github.com/Nikola1Davydov/AnalyzeTool/issues/122)); worker над слепком — когда
есть слепок ([#131](https://github.com/Nikola1Davydov/AnalyzeTool/issues/131)); FTS5 по
семействам — когда есть библиотека семейств в объёме.

Правило кода с самого начала — из [`roadmap.md`](roadmap.md): **ядро чистой функцией**.
Извлечение строки элемента — функция от `Element`, применение дельты — функция от журнала и
хранилища, guard — функция от строки SQL. Тесты яруса 1 покрывают всё, кроме самого Revit.

## Состояние на 2026-09-04

Фазы 1–3 написаны одним заходом по решению владельца («сделай, как считаешь нужным»), ветка
`feature/model-index`: `src/AnalyseTool.Core/Common/Index/` (`ModelIndexStore`, `ChangeJournal`,
`ModelIdentity`, `ElementRowReader`, `ModelIndexSession`, `ModelIndexHost`, `IndexQuery`),
команды в `src/AnalyseTool.Core/Features/Index/ModelIndexCommands.cs`, проводка одной строкой в
`AnalyseToolBootstrap`. Отличия от текста выше: слепка `VACUUM INTO` и Sdk `IModelIndex` пока нет
(фаза 4); журнал держится в памяти без таблицы `changes`; «огромная» пачка — больше 5 000 id или
четверти живых строк — идёт в сверку, малая и большая — одним путём применения порциями. Спайк
`ModelIndexSpike` оставлен до снятия цифр. Тесты: ярус 1 `ModelIndexTests` (свёртка журнала,
tombstone, замена, миграция версии схемы, guard запроса), ярус 3 `ModelIndexSessionTests`
(сборка, дельта через настоящий `DocumentChanged`, удаление, сверка мимо журнала, полная
пересборка). Собрано на Linux; в Revit ещё не запускалось — цифры и поведение на живой модели за
владельцем.

## 7. Риски — и что на каждый

| Риск | Ответ |
| --- | --- |
| нативная DLL в чужом процессе | `winsqlite3` из системы; запасной путь — своя копия по полному пути из ALC лаунчера; никогда в расширении |
| первичная сборка на 100k+ элементов | чанки, `Idling`, прогресс в окне активности, отмена при возвращении человека; агент видит `building` с покрытием |
| размер базы | long-формат нормализован; замер в фазе 0; фильтр параметров как настройка |
| произвольный SQL | только чтение по authorizer, один statement, `LIMIT`, `interrupt` по отмене — и это тест яруса 1 |
| переиспользование `ElementId` | ключ `unique_id`, tombstone'ы |
| модель правили без плагина | сверка по `VersionGuid` при открытии |
| worksharing: у каждого своя локальная копия | ключ — GUID центральной; индекс каждого — своя локальная база; общее — слепок в папке проекта |
| несколько открытых документов | база на модель, команда смотрит на активный документ (`DocumentTracker`); необязательный `documentKey` в запросе — v1.1 |
| локализация | имена и встроенные идентификаторы рядом, как в #113 |
| две копии чтения параметров (Core и Tools) | пометка в коде; перенос в Sdk, когда дублирование начнёт расти |

## Решить до кода

1. **SQLite подтверждён?** Вики трижды говорила LiteDB; здесь предложено снять. Да / нет.
2. **Все параметры или отобранные** — принять «все, с замером в фазе 0 и фильтром как
   настройкой», или сразу отобранные.
3. **SQL в процессе, не в exe** — принять, или есть сценарий «Revit закрыт», который нужен
   раньше worker'а.
4. **Расширение Sdk (`IModelIndex`)** — согласие в принципе; форма решается в фазе 4.
5. **Имя.** В вики — «теневой индекс», в коде предлагается `ModelIndex`
   (`QueryModelIndex`, `GetModelIndexStatus`): агенту «model index» понятнее, чем «shadow».

## Связанное

- [`../entities/shadow-index.md`](../entities/shadow-index.md) — история решений, которые этот план продолжает и в одном месте отменяет
- [`roadmap.md`](roadmap.md) · [`checking-module.md`](checking-module.md) · [`backlog-map.md`](backlog-map.md)
- [`../entities/command-queue.md`](../entities/command-queue.md) — почему SQL идёт через очередь
- [`../concepts/agent-legibility.md`](../concepts/agent-legibility.md) · [`../concepts/long-running-calls.md`](../concepts/long-running-calls.md)
- [`../entities/project-folder.md`](../entities/project-folder.md) — куда уходит слепок
