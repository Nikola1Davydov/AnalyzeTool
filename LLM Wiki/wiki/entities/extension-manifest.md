---
type: entity
updated: 2026-09-02
status: current
sources: [../sources/analysetool-repo-docs.md]
---

# Манифест расширения (`plugin.json`)

Файл рядом с файлами расширения, по которому хост решает, что грузить и какие кнопки
строить. Модель — `src/AnalyseTool.Core/Common/Extensions/ExtensionManifest.cs`; здесь
справочник по ней, а не копия гайда: истина для авторов остаётся в `src/LLM.md` и
`ONBOARDING.md`.

## Верхний уровень

| Поле | Что |
| --- | --- |
| `schema` | версия **формата**, не расширения; отсутствует = 1; `2` — когда используется `ui.buttons`. Хост читает старые схемы весь мажор, миграция — предложение ([#127](https://github.com/Nikola1Davydov/AnalyzeTool/issues/127)) |
| `id` | стабильный id, он же префикс команд (`acme.tools`) |
| `version` | версия расширения |
| `description`, `publisher`, `website`, `supportUrl`, `icon`, `updateFeed` | метаданные издателя для менеджера (манифест v2, аддитивно); `updateFeed` — HTTPS-URL с `{version, downloadUrl}` или `github:owner/repo` |
| `entryAssembly` | DLL с командами; ищется сначала в подпапке года (`2025\Ext.dll`), потом в корне |
| `ui` | JS-интерфейс и кнопки ленты |

## `ui.*`

`entryHtml` (по умолчанию `index.html`), `devUrl` (dev-сервер вместо собранных файлов — убрать
перед релизом), `dockable` (в общей док-панели, а не отдельным окном), `tab`, `panel`,
`button` — одна кнопка (схема 1), `buttons` — несколько (схема 2). `EffectiveButtons()` отдаёт
список для сборки: `buttons`, если он непуст, отсортированный по `order`, затем по порядку
объявления; иначе `button`; иначе ничего.

`HasUi` (`src/AnalyseTool.Core/Common/Extensions/ExtensionCatalog.cs`) =
`EffectiveButtons().Count > 0`: расширение без кнопок ленты не получает, но его команды всё
равно грузятся и видны MCP и JS. Как это правило однажды читало только единственное число —
в [`../concepts/contract-evolution.md`](../concepts/contract-evolution.md).

## Кнопка (`ExtensionButton`)

| Поле | Что |
| --- | --- |
| `name`, `tooltip` | подпись (она же заголовок окна) и подсказка |
| `icon` | PNG относительно папки — или `glyph:E8A9`, глиф Segoe MDL2, тот же источник, что у кнопок хоста |
| `command` | если задано, клик **вызывает команду** (`<id>.Foo`) вместо открытия страницы |
| `entryHtml`, `dockable`, `tab`, `panel` | переопределения для этой кнопки; `null` = унаследовать от `ui.*`. Две поверхности одного расширения открывают две разные страницы |
| `order` | порядок в панели; равные — в порядке объявления |
| `kind` | `push` (по умолчанию), `stacked` (маленькая, колонки по три), `pulldown` (список из `items`); незнакомое значение → `push`, чтобы манифест против более нового хоста дал рабочую ленту, а не пустую |
| `items` | пункты `pulldown`; каждый — кнопка без собственного размещения |

Как это раскладывается по ленте — [`ribbon-host.md`](ribbon-host.md).

## Кто и как пишет

Записывает всегда `ExtensionManifestWriter`
(`src/AnalyseTool.Core/Common/Extensions/ExtensionManifestWriter.cs`), и он **сливает**, а не
переписывает: читает файл в `JObject`, правит только поля из `ManifestEdit` и сохраняет обратно.
Поэтому C#-сторона и web-сторона могут собирать одно расширение по очереди, а поля, о которых
writer не знает (`entryAssembly`, `icon`, вторая кнопка), переживают запись. Правило в одном
месте: если у `ui` есть `entryHtml`, из кнопки убирается `command` — кнопка страницы открывает
страницу. Writer правит только `ui.button`; манифест с `ui.buttons` редактируют руками.

`ManifestEdit`: `ButtonName`, `Tooltip`, `Tab`, `Panel`, `CommandName`, `EntryHtml`, `Dockable`,
`Description`, `Publisher`, `Website`, `SupportUrl`, `UpdateFeed`, `Kind`, `Order`, `RemoveButton`.
`null` — оставить как есть; для метаданных пустая строка — удалить поле.

| Кто | Что пишет |
| --- | --- |
| `SaveAsCommand` | `command` и кнопку, если попросили |
| `SaveExtensionUi` | `entryHtml`, кнопку, `dockable` |
| `UpdateExtensionManifest` | инструмент **агента**: за переключателем C#, только для папок, которые создали эти команды (`ExtensionFolder.IsGeneratedFolder`) |
| `EditExtensionManifest` | форма Edit в окне Extensions (читает её `GetExtensionManifest`): только dev-зона — установленный пакет принадлежит издателю, следующее обновление перепишет правку; обе `HiddenFromMcp` |

`id` не редактирует никто: writer выставляет его сам, форма его только показывает.

## Шаблон: всегда страница плюс C#

Форма New (`src/clientapp/src/view/System/CreateExtensionForm.vue`) шлёт `kind: "Combo"`
константой — `plugin.json` + `index.html` + csproj + `Hello.cs`, лишнее автор удаляет.
`CreateExtensionTemplate` (`src/AnalyseTool.Core/Features/Extensions/CreateExtensionTemplate.cs`)
по-прежнему принимает `UiOnly`, `Csharp` и `Combo` для тех, кто зовёт её сам. Из
`src/AnalyseTool.Core/Features/Extensions/Templates/` рядом кладутся `readme.md.txt` и
`workflow.yml.txt`.

## Связанное

- [`ribbon-host.md`](ribbon-host.md) — что лента делает с кнопками
- [`../concepts/contract-evolution.md`](../concepts/contract-evolution.md) — поле `schema` и правила миграции
- [`../concepts/extension-distribution.md`](../concepts/extension-distribution.md) — `updateFeed` и каталог
- [`analysetool-mcp-server.md`](analysetool-mcp-server.md)
