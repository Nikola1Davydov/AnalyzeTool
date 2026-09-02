# AnalyseTool MCP — отчёт о тестировании (2026-09-02)

Источник: полевой тест из claude.ai, переслан владельцем в сессию 2026-09-02. Текст ниже — как получен.

## Окружение

- Revit 2025, немецкий UI, единицы мм, не workshared
- AnalyseTool plugin 1.5.0.0, host SDK 1.2.0.0
- Модель: SiklaTestBauteillistenVirtElements (id 588ca72a-5dbd-4aca-81c5-ce747c1a3701), 1 уровень, 6 стен (Basiswand : Wand 1), 12 Rohrsegmente, 4 Tragwerksverbindungen, 23 вида, 2 листа
- Клиент: claude.ai, тулы MCP загружаются через tool_search (deferred)

## Read-only команды — результат

| Команда | Статус | Замечания |
| --- | --- | --- |
| GetDocumentData | ✅ | |
| GetModelOverview | ✅ | |
| GetQueueStatus | ✅ | пустая очередь, waitingForUser: false |
| GetCategoriesInRevit | ✅ | 135 категорий, локализованы |
| GetCategoryParameters("Wände") | ⚠️ | 65 параметров, но нет поля id (см. баг B2) |
| GetElements | ❌ | падает во всех вариантах (см. баг B1) |
| GetViewsAndSheets | ⚠️ | в views попадают сами листы (3176, 17539) и псевдовиды «Projektansicht», «Systembrowser»; обещанный в описании hiddenElementCount в ответе отсутствует |
| GetWarningsInRevit | ✅ | [] |
| GetWorksets | ✅ | isWorkshared: false |
| GetLinksInRevit | ✅ | пусто |
| GetCadImports | ✅ | пусто |
| GetInstalledExtensions | ✅ | 10 расширений |
| GetExtensionDiagnostics | ✅ | 1 failing: company.copyroom — «No build for Revit 2025», есть только 2027. Диагностика внятная |
| GetTypeParameters([1972,1973]) | ⚠️ | работает, но: (a) описание обещает non-empty, приходят десятки ""; (b) дубликаты имён без различителя: Kategorie×2, у Fassade Abstand/Layout/Innentyp/Grenze 1 Typ/Grenze 2 Typ по 2 раза (H/V раскладка) — нужен parameterId или group |
| ExecuteRevitCode | ✅ | Roslyn компилирует, doc/uidoc/uiapp в скоупе, UnitUtils/BuiltInParameter/Transaction доступны без extra using; возврат анонимных объектов сериализуется корректно |

## Пишущие команды — результат

| Команда | Статус | Замечания |
| --- | --- | --- |
| SelectionInRevit([2791, 2814, 999999]) | ✅ | selected: 2, несуществующий id молча отброшен, error: null — стоит вернуть ignoredIds |
| IsolationInRevit([2791, 2814]) | ✅ | isolated: 2, warnings: [] |
| SetDataToParameters, Overwrite | ✅ | written: 1, skipped: 1 (несуществующий элемент) |
| SetDataToParameters, SkipIfEqual | ✅ | written: 0, skipped: 1 |
| SetDataToParameters, OnlyIfEmpty | ✅ | written: 0, skipped: 1 |
| SetDataToParameters с неверным id | ❌ | вся команда падает (см. баг B3) |

Модель после теста возвращена в исходное состояние (комментарий стены 2791 очищен сниппетом). Temporary isolate в {3D} не сброшен.

## Баги

**B1. GetElements — полный отказ.** Вызовы: builtInCategory: "OST_Walls", category: "Wände", с/без parameterNames, с limit: 5. Всегда голый Tool execution failed без тела — исключение вылетает до сериализации ответа. При этом тот же запрос через ExecuteRevitCode (FilteredElementCollector.OfCategory(OST_Walls).WhereElementIsNotElementType()) возвращает 6 стен без проблем → доступ к модели исправен, баг внутри команды. Гипотеза: резолв familyId/familyName для системных семейств (Basiswand — WallType, не FamilySymbol). Нужен лог хоста.

**B2. GetCategoryParameters не отдаёт id, хотя SetDataToParameters его требует.** Описание SetDataToParameters: «Parameter ids come from GetCategoryParameters». Фактически там только name/storageType/isReadOnly/isType. Workflow разрывается — id пришлось доставать через ExecuteRevitCode (p.Id.Value → -1010106 для ALL_MODEL_INSTANCE_COMMENTS). Нужно добавить id (BuiltInParameter int или ElementId для shared/project).

**B3. SetDataToParameters — один битый item убивает весь батч.** Передан id: -1001103 (это WALL_HEIGHT_TYPE, ElementId) со строковым значением → [command_failed] Parameter 'Abhängigkeit oben': cannot convert 'MCP-Test 2026-09-02' to ElementId. Сообщение информативное, но: (a) в батче из сотен элементов один ошибочный item валит всё; (b) неясно, откатилась ли транзакция целиком. Ожидаемо: per-item ошибка в skipped с причиной, либо явно задокументировать all-or-nothing.

## Мелкие замечания

- tool_search в claude.ai не находит тулы по точному имени (GetModelOverview, GetCategoriesInRevit), только по словам из description. Вероятно, клиентский индекс, но подстраховка — продублировать имя команды в начале description.
- ExecuteRevitCode возвращает heightMm: 3999.9999999999995 — при конвертации в display-единицы имеет смысл округлять (4 знака).
- GetCategoriesInRevit в описании ссылается на несуществующую команду GetDataByCategoryName (устаревшее имя GetElements?).

## Не протестировано

SaveAsCommand, ReloadExtensions, UpdateExtensionManifest, SaveExtensionUi, GetScriptSource, GetAuthoringGuide, все company_*/niko_* extension-команды.
