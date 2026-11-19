import { CommandError, registerMocks } from "revit-bridge";
import type { AppInfo, McpInfo, UpdateInfo } from "./api/app";
import type { ButtonStatistic, ButtonStatisticsResult } from "./api/statistics";
import type { ModelTestCase, ModelTestList, ModelTestResult, ModelTestRun, RunModelTestsRequest } from "./api/modelTests";
import type { ButtonRunResult, PaneFolder, PaneOverview } from "./api/pane";
import type {
  CompileDiagnostic,
  ExtensionFileDiff,
  ExtensionRoots,
  ExtensionSummary,
  ExtensionVersion,
  PackageInstallResult,
} from "./api/extensions";
import type {
  DataElement,
  ParameterReportDto,
  ParameterSetDto,
  ProjectParameterDto,
  SharedParameterDto,
  SharedParameterFileData,
} from "./api/tools";

// Answers of the add-in commands for `npm run dev` in a normal browser, without Revit. Inside Revit they are ignored.
// The extension mocks keep state, so saving a version, restoring or pinning changes what the pages show next.

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
const fail = (message: string, code = "bad_request") => {
  throw new CommandError({ code, message });
};
const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

// ---- Dock pane: the folders of buttons and the profile of the pane
const g = (code: number) => String.fromCodePoint(code);
const paneButton = (folder: string, name: string, glyph: string | null, command: string | null, tooltip: string | null = null, group: string | null = null) => ({
  key: `${folder}/${name}`,
  name,
  tooltip,
  glyph,
  image: null,
  command,
  group,
});
const paneFolders: PaneFolder[] = [
  {
    id: "analysetool",
    name: "AnalyseTool",
    builtIn: true,
    enabled: true,
    canAddButtons: false,
    description: null,
    directory: null,
    tab: "AnalyseTool",
    panel: "AnalyseTool",
    panels: ["AnalyseTool"],
    problems: [],
    buttons: [
      { ...paneButton("analysetool", "Parameters", g(0xe9f9), "OpenWindow", "The parameter report, the elements and the shared parameters."), offRibbon: true },
    ],
  },
  {
    id: "count-walls",
    name: "Count walls",
    builtIn: false,
    enabled: true,
    canAddButtons: true,
    description: "Counts walls.",
    tab: "Count walls",
    panel: "Count walls",
    panels: ["Count walls"],
    directory: "C:/Users/you/AppData/Local/AnalyseTool/extensions/count-walls",
    problems: [],
    buttons: [
      {
        ...paneButton("count-walls", "Count walls", g(0xe768), "count-walls.CountWalls", "Counts the walls of the active view by type."),
        problems: ["src/CountWalls.cs(14,9): The name 'grup' does not exist in the current context"],
      },
    ],
  },
  {
    id: "room-sheets",
    name: "Room sheets",
    builtIn: false,
    enabled: true,
    canAddButtons: true,
    description: "Rooms and their sheets.",
    tab: "Room sheets",
    panel: "Rooms",
    panels: ["Rooms", "Checks"],
    directory: "D:/Team/buttons/room-sheets",
    problems: [],
    buttons: [
      { ...paneButton("room-sheets", "Room sheets", g(0xe8a7), null, "A small interface in the pane: counts rooms or walls."), dockable: true, hasPage: true },
      {
        ...paneButton("room-sheets", "Renumber", g(0xe8ef), "room-sheets.Renumber", "Renumbers the sheets from a start number.", "Tools"),
        input: {
          type: "object",
          properties: {
            start: { type: "integer", default: 101, description: "The first number." },
            prefix: { type: ["string", "null"], description: "Put before every number, e.g. A-." },
            level: { enum: ["All", "Level 1", "Level 2"] },
            onlySelected: { type: "boolean", description: "Only the sheets selected in the project browser." },
          },
          required: ["start"],
        },
      },
      paneButton("room-sheets", "Purge empty", g(0xe74d), "room-sheets.PurgeEmpty", null, "Tools"),
      {
        ...paneButton("room-sheets", "Check numbering", g(0xe73e), "room-sheets.CheckNumbering"),
        panel: "Checks",
        lastFailure: "Open a document first: the command reads the sheets of the active document.",
        lastFailureAt: new Date(Date.now() - 3600_000).toISOString(),
      },
    ],
  },
  { id: "Acme.Sample", name: "Acme sample", builtIn: false, enabled: false, canAddButtons: false, description: null, directory: "C:/Users/you/AppData/Local/AnalyseTool/extensions-dist/Acme.Sample", tab: "Acme", panel: "Samples", panels: ["Samples"], dllBuilds: ["2025", "2026", "2027"], problems: [], buttons: [paneButton("Acme.Sample", "Hello", null, "Acme.Sample.Hello"), paneButton("Acme.Sample", "Doors", null, "Acme.Sample.Doors")] },
];

// Two tabs of the user: their buttons are folders of their own (<tab>.<button>)
paneFolders.splice(1, 0,
  { id: "sheets.tab", name: "Sheets", builtIn: false, enabled: true, canAddButtons: true, description: null, directory: "C:/Users/you/AppData/Local/AnalyseTool/extensions/Sheets.tab", tab: "Sheets", panel: "Numbering", panels: ["Numbering"], problems: [],
    buttons: [{ ...paneButton("sheets.renumber", "Renumber", g(0xe8ef), "sheets.renumber.Renumber"), panel: "Numbering", directory: "C:/Users/you/AppData/Local/AnalyseTool/extensions/Sheets.tab/Numbering.panel/Renumber.pushbutton" }] },
  { id: "rooms.tab", name: "Rooms", builtIn: false, enabled: true, canAddButtons: true, description: null, directory: "C:/Users/you/AppData/Local/AnalyseTool/extensions/Rooms.tab", tab: "Rooms", panel: "Rooms", panels: ["Rooms"], problems: [], buttons: [] },
);

// The user's folders of plugin.json (1.x) are sections of «Older buttons»
{
  const old = paneFolders.splice(paneFolders.findIndex((folder) => folder.id === "count-walls"), 1);
  const off = { ...old[0], id: "old-export", name: "Old export", enabled: false, directory: "C:/Users/you/AppData/Local/AnalyseTool/extensions/old-export", buttons: [paneButton("old-export", "Export IFC", g(0xe898), "old-export.Export")] };
  const parts = [...old, off];
  paneFolders.push({
    id: "older",
    name: "Older buttons",
    builtIn: false,
    enabled: true,
    canAddButtons: false,
    description: null,
    directory: null,
    problems: [],
    panels: parts.map((part) => part.name),
    buttons: parts.flatMap((part) => part.buttons.map((button) => ({ ...button, panel: part.name }))),
    parts,
  });
}
let paneProfile: unknown = { dock: ["count-walls/Count walls", "analysetool/Parameters"] };

// ---- Statistics: a few weeks of clicks
const statDays = (seed: number, scale: number) =>
  Array.from({ length: 30 }, (_, index) => {
    const value = Math.round(Math.max(0, Math.sin(index * 0.7 + seed) * scale + scale * 0.6 - (index % 7 > 4 ? scale : 0)));
    return value;
  });
const stat = (
  extension: string,
  extensionName: string,
  button: string,
  command: string | null,
  builtIn: boolean,
  seed: number,
  scale: number,
  extra: Partial<ButtonStatistic> = {},
): ButtonStatistic => {
  const last30Days = scale ? statDays(seed, scale) : Array<number>(30).fill(0);
  const clicks = last30Days.reduce((sum, value) => sum + value, 0) + Math.round(scale * 3);
  return {
    extension,
    extensionName,
    button,
    command,
    builtIn,
    onRibbon: true,
    clicks,
    failures: 0,
    firstClick: clicks ? ago(60 * 24 * 40) : null,
    lastClick: clicks ? ago(seed * 47) : null,
    lastError: null,
    lastErrorAt: null,
    averageMilliseconds: clicks && command ? 40 + seed * 37 : null,
    last30Days,
    ...extra,
  };
};
let statistics: ButtonStatistic[] = [
  stat("analysetool", "AnalyseTool", "Parameters", "OpenWindow", true, 1, 6),
  stat("analysetool", "AnalyseTool", "AnalyseTool", "OpenWindow", true, 2, 2),
  stat("analysetool", "AnalyseTool", "New button", "OpenWindow", true, 3, 1),
  stat("analysetool", "AnalyseTool", "Refresh", "RefreshButtons", true, 13, 0.4),
  stat("analysetool", "AnalyseTool", "Manage buttons", "OpenWindow", true, 4, 1.5),
  stat("analysetool", "AnalyseTool", "Statistics", "OpenWindow", true, 5, 0.6),
  stat("analysetool", "AnalyseTool", "Settings", "OpenWindow", true, 6, 0.3),
  stat("analysetool", "AnalyseTool", "About", "OpenWindow", true, 7, 0),
  stat("analysetool", "AnalyseTool", "Report a bug", "OpenUrl", true, 8, 0),
  stat("count-walls", "Count walls", "Count walls", "count-walls.CountWalls", false, 9, 8, {
    failures: 3,
    lastError: "Open a document first.",
    lastErrorAt: ago(300),
    averageMilliseconds: 182,
  }),
  stat("room-sheets", "Room sheets", "Room sheets", null, false, 10, 3),
  stat("Acme.Sample", "Acme sample", "Hello", "Acme.Sample.Hello", false, 11, 0.5),
  stat("old-tool", "old-tool", "Purge views", "old-tool.PurgeViews", false, 12, 0, { clicks: 14, onRibbon: false, lastClick: ago(60 * 24 * 50) }),
];

let logSequence = 0;
const levels = ["Debug", "Information", "Information", "Warning"] as const;

// ---- App ------------------------------------------------------------------------------------------------

const token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
const mcpUrl = "http://localhost:48075/mcp";
const update: UpdateInfo = {
  current: "2.0.1",
  latest: "2.1.0",
  url: "https://github.com/Nikola1Davydov/AnalyzeTool/releases/tag/2.1.0",
  available: true,
  checkedAt: new Date().toISOString(),
};
const mcp: McpInfo = {
  enabled: true,
  state: "listening",
  url: mcpUrl,
  claudeCode: `claude mcp add --transport http analysetool ${mcpUrl} --header "Authorization: Bearer ${token}"`,
  connectionFile: String.raw`C:\Users\you\AppData\Local\AnalyseTool\mcp.json`,
  name: "analysetool",
  token,
  claudePlugin: String.raw`C:\Users\you\AppData\Roaming\Autodesk\Revit\Addins\2026\AnalyseTool.Launcher\Plugin`,
};

const changelog = `# Changelog

## 2.0.0 — 2026-11-20

- **Rewrite** on the new template: React pages, one command queue for the pages and MCP.
- Extensions are C# sources AnalyseTool compiles itself; every folder keeps its own history.
- Save a version, compare, restore, or run an older version — from the extension page.

## 1.9.2 — 2026-10-19

- Fixed the report counting type parameters twice.
- The shared parameter file editor keeps unknown columns.

## 1.9.0 — 2026-09-01

- Family Manager moved out of the platform into an extension.
- See [the release notes](https://github.com/Nikola1Davydov/AnalyzeTool/releases).
`;

// ---- Extensions --------------------------------------------------------------------------------------

const root = String.raw`C:\Users\you\AppData\Local\AnalyseTool\extensions`;
const managedRoot = String.raw`C:\Users\you\AppData\Local\AnalyseTool\extensions-dist`;
let roots: ExtensionRoots = {
  roots: [
    { path: root, zone: "Dev", isDefault: true },
    { path: managedRoot, zone: "Managed", isDefault: true },
    { path: String.raw`\\server\bim\analysetool-buttons`, zone: "Dev", isDefault: false },
  ],
  authoringRoot: root,
};

type MockExtension = {
  summary: ExtensionSummary;
  /** The current files. */
  files: Record<string, string>;
  /** Saved versions, newest first, with their files. */
  versions: { version: ExtensionVersion; files: Record<string, string> }[];
};

let versionCounter = 0x3a1f00;
function newVersion(message: string, minutesAgo = 0): ExtensionVersion {
  versionCounter += 0x1b3d;
  const id = `${versionCounter.toString(16)}e1c4b2a9d8f7e6c5b4a3928170f6e5d4c3b`.slice(0, 40);
  return { id, shortId: id.slice(0, 7), message, author: "Nikolai Davydov", time: ago(minutesAgo) };
}

const countWallsV1 = `using AnalyseTool.Sdk;
using Autodesk.Revit.DB;

namespace CountWalls;

[RevitCommand(Description = "Counts the walls of the model.")]
public sealed class CountWalls : IRevitTask
{
    public async Task<object?> ExecuteAsync(IRevitContext context)
    {
        return await context.RunInRevitAsync(app =>
        {
            var document = app.ActiveUIDocument.Document;
            return new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .GetElementCount();
        });
    }
}
`;

const countWallsV2 = countWallsV1
  .replace("Counts the walls of the model.", "Counts the walls of the active view by type.")
  .replace(
    `            return new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .GetElementCount();`,
    `            return new FilteredElementCollector(document, document.ActiveView.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .GroupBy(wall => wall.WallType.Name)
                .ToDictionary(group => group.Key, group => group.Count());`,
  );

const countWallsWorking = countWallsV2.replace(
  "                .ToDictionary(group => group.Key, group => group.Count());",
  "                .OrderByDescending(group => group.Count())\n                .ToDictionary(group => group.Key, group => grup.Count());",
);

const pluginJson = (id: string, name: string, description: string) =>
  JSON.stringify({ id, version: "1.0.0", description, ui: { panel: "Extensions", buttons: [{ name }] } }, null, 2) + "\n";

const llm = "# Writing AnalyseTool extensions\n\nRead this before writing extension code.\n";
const agents = "# An AnalyseTool extension\n\nRead the authoring guide: LLM.md next to the add-in, or GetAuthoringGuide over MCP.\n";
const claude = "# Instructions\n\n@AGENTS.md\n";

function summary(partial: Partial<ExtensionSummary> & Pick<ExtensionSummary, "id" | "name">): ExtensionSummary {
  return {
    description: null,
    version: "1.0.0",
    publisher: null,
    directory: `${root}\\${partial.id}`,
    zone: "Dev",
    kind: "Source",
    enabled: true,
    compatible: true,
    commands: [],
    buttons: [partial.name],
    errors: [],
    diagnostics: [],
    loadedVersion: null,
    usesOlderBuild: false,
    ...partial,
  };
}

const countWallsDiagnostics: CompileDiagnostic[] = [
  {
    file: "src/CountWalls.cs",
    line: 19,
    column: 64,
    severity: "error",
    code: "CS0103",
    message: "The name 'grup' does not exist in the current context",
  },
  {
    file: "src/CountWalls.cs",
    line: 12,
    column: 22,
    severity: "warning",
    code: "CS1998",
    message: "This async method lacks 'await' operators and will run synchronously.",
  },
];

const v1 = newVersion("First version", 60 * 26);
const v2 = newVersion("Count by wall type, in the active view only", 60 * 3);

const extensions: MockExtension[] = [
  {
    summary: summary({
      id: "CountWalls",
      name: "Count walls",
      description: "Counts the walls of the active view by type.",
      commands: ["CountWalls.CountWalls"],
      diagnostics: countWallsDiagnostics,
      usesOlderBuild: true,
    }),
    files: {
      "plugin.json": pluginJson("CountWalls", "Count walls", "Counts the walls of the active view by type."),
      "AGENTS.md": agents,
      "CLAUDE.md": claude,
      "src/CountWalls.cs": countWallsWorking,
      "src/Notes.txt": "Ideas: group by level too.\n",
    },
    versions: [
      {
        version: v2,
        files: {
          "plugin.json": pluginJson("CountWalls", "Count walls", "Counts the walls of the active view by type."),
          "AGENTS.md": agents,
          "CLAUDE.md": claude,
          "src/CountWalls.cs": countWallsV2,
        },
      },
      {
        version: v1,
        files: {
          "plugin.json": pluginJson("CountWalls", "Count walls", "Counts the walls of the model."),
          "AGENTS.md": agents,
          "CLAUDE.md": claude,
          "src/CountWalls.cs": countWallsV1,
        },
      },
    ],
  },
  (() => {
    const files = {
      "plugin.json": pluginJson("RoomSheets", "Room sheets", "A page that lists rooms and creates sheets for them."),
      "index.html": "<!doctype html>\n<html>\n  <body>\n    <h1>Room sheets</h1>\n  </body>\n</html>\n",
      "src/RoomSheets.cs": "namespace RoomSheets;\n\npublic sealed class ListRooms : IRevitTask\n{\n}\n",
    };
    const version = newVersion("Page with the room list", 60 * 24 * 5);
    return {
      summary: summary({
        id: "RoomSheets",
        name: "Room sheets",
        description: "A page that lists rooms and creates sheets for them.",
        commands: ["RoomSheets.ListRooms", "RoomSheets.CreateSheets"],
        loadedVersion: version.id,
      }),
      files,
      versions: [{ version, files: { ...files } }],
    };
  })(),
  {
    summary: summary({
      id: "Acme.Sample",
      name: "Acme sample",
      description: "The sample extension of the SDK.",
      kind: "Dll",
      zone: "Managed",
      version: "2.1.0",
      publisher: "Acme",
      directory: `${managedRoot}\\Acme.Sample`,
      commands: ["Acme.Sample.Hello"],
    }),
    files: { "plugin.json": pluginJson("Acme.Sample", "Acme sample", "The sample extension of the SDK."), "2026/Acme.Sample.dll": "" },
    versions: [],
  },
  {
    summary: summary({
      id: "OldExport",
      name: "Old export",
      kind: "Dll",
      enabled: false,
      compatible: false,
      errors: ["Built for Revit 2024: AnalyseTool loads extensions for 2025 and later."],
      commands: [],
    }),
    files: { "plugin.json": pluginJson("OldExport", "Old export", "") },
    versions: [],
  },
];

function find(id: string) {
  const extension = extensions.find((item) => item.summary.id.toLowerCase() === String(id).toLowerCase());
  if (!extension) fail(`No extension '${id}'.`, "not_found");
  return extension!;
}

function changesOf(extension: MockExtension) {
  const saved = extension.versions[0]?.files ?? {};
  const changes = [];
  for (const [file, text] of Object.entries(extension.files)) {
    if (!(file in saved)) changes.push({ file, state: "added" });
    else if (saved[file] !== text) changes.push({ file, state: "modified" });
  }
  for (const file of Object.keys(saved)) if (!(file in extension.files)) changes.push({ file, state: "deleted" });
  return changes;
}

function diff(before: Record<string, string>, after: Record<string, string>): ExtensionFileDiff[] {
  const files = [...new Set([...Object.keys(before), ...Object.keys(after)])].sort();
  return files
    .filter((file) => before[file] !== after[file])
    .map((file) => ({
      file,
      state: !(file in before) ? "added" : !(file in after) ? "deleted" : "modified",
      before: before[file] ?? null,
      after: after[file] ?? null,
      binary: file.endsWith(".dll"),
    }));
}

function versionIndex(extension: MockExtension, id: string) {
  const index = extension.versions.findIndex((item) => item.version.id.startsWith(id) || id.startsWith(item.version.id));
  if (index < 0) fail(`No version '${id}'.`, "not_found");
  return index;
}

function save(extension: MockExtension, message: string) {
  const version = newVersion(message);
  extension.versions.unshift({ version, files: { ...extension.files } });
  return version;
}

function list() {
  return {
    revitVersion: "2026",
    extensions: extensions.map((item) => item.summary),
    brokenFolders: { [String.raw`\\server\bim\analysetool-buttons\Broken`]: "plugin.json: unexpected end of JSON at line 4." },
  };
}

// ---- Tools: shared parameters, report, elements ------------------------------------------------------

const guid = (n: number) => `${n.toString(16).padStart(8, "0")}-1111-4222-8333-${(n * 7919).toString(16).padStart(12, "0")}`;

let sharedFile: SharedParameterFileData = {
  path: String.raw`C:\BIM\SharedParameters.txt`,
  exists: true,
  isRevitCurrent: true,
  stamp: "2026-11-15T10:00:00Z",
  groups: [
    { id: 1, name: "Doors" },
    { id: 2, name: "General" },
    { id: 3, name: "Fire safety" },
  ],
  parameters: (
    [
      ["Fire rating", 3, "TEXT", "Required fire resistance"],
      ["Smoke tight", 3, "YESNO", ""],
      ["Door number", 1, "TEXT", "Number on the door schedule"],
      ["Clear width", 1, "LENGTH", "Clear opening width"],
      ["Classification", 2, "TEXT", "Uniclass code"],
      ["Phase note", 2, "MULTILINETEXT", ""],
      ["Cost group", 2, "INTEGER", "DIN 276 cost group"],
    ] as const
  ).map(
    ([name, groupId, dataType, description], index): SharedParameterDto => ({
      guid: guid(index + 1),
      name,
      dataType,
      dataCategory: "",
      groupId,
      visible: true,
      description,
      userModifiable: true,
      hideWhenNoValue: false,
    }),
  ),
};

let projectParameters: ProjectParameterDto[] = [
  { id: 101, guid: guid(1), name: "Fire rating", isShared: true, bound: true, isInstance: true, dataType: "TEXT", group: "Identity Data", categories: ["Doors", "Walls"] },
  { id: 102, guid: guid(3), name: "Door number", isShared: true, bound: true, isInstance: true, dataType: "TEXT", group: "Identity Data", categories: ["Doors"] },
  { id: 103, guid: guid(5), name: "Classification", isShared: true, bound: true, isInstance: false, dataType: "TEXT", group: "Identity Data", categories: ["Doors", "Walls", "Windows", "Floors"] },
  { id: 104, guid: guid(99), name: "Manufacturer code", isShared: true, bound: true, isInstance: false, dataType: "TEXT", group: "Other", categories: ["Windows"] },
  { id: 105, name: "Room finish", isShared: false, bound: true, isInstance: true, dataType: "TEXT", group: "Other", categories: ["Rooms"] },
  { id: 106, guid: guid(4), name: "Clear width", isShared: true, bound: false, isInstance: true, dataType: "LENGTH", group: "Dimensions", categories: [] },
];

let parameterSets: ParameterSetDto[] = [
  { id: "set-1", name: "Fire safety", parameters: [{ guid: guid(1), name: "Fire rating" }, { guid: guid(3), name: "Door number" }], updated: ago(600) },
  { id: "set-2", name: "Handover", parameters: [{ guid: guid(5), name: "Classification" }, { name: "Room finish" }], updated: ago(3000), projectId: "doc-guid-1", projectName: "Sample.rvt" },
];

const bindingCategories = [
  { id: -2000014, name: "Doors", type: "Model" },
  { id: -2000011, name: "Walls", type: "Model" },
  { id: -2000014 + 2, name: "Windows", type: "Model" },
  { id: -2000032, name: "Floors", type: "Model" },
  { id: -2000160, name: "Rooms", type: "Model" },
  { id: -2000280, name: "Title Blocks", type: "Annotation" },
];

function report(name: string, guidValue: string | undefined, isInstance: boolean, seed: number): ParameterReportDto {
  const categories = [
    { name: "Doors", total: 140, filled: Math.round(140 * (0.4 + (seed % 5) / 10)) },
    { name: "Walls", total: 210, filled: Math.round(210 * (0.2 + (seed % 3) / 5)) },
  ];
  const levelsReport = ["Level 0", "Level 1", "Level 2"].map((level, index) => ({ name: level, total: 100 + index * 25, filled: 60 + index * 10 + seed }));
  const total = categories.reduce((sum, item) => sum + item.total, 0);
  const filled = categories.reduce((sum, item) => sum + item.filled, 0);
  const values = ["EI 30", "EI 60", "EI 90", "REI 30", "T30-RS", "none"].map((value, index) => ({ value, count: Math.max(3, Math.round(filled / (index + 2) / 2)) }));
  return {
    name,
    guid: guidValue,
    isInstance,
    total,
    filled,
    categories,
    levels: levelsReport,
    values,
    otherValuesCount: 12,
    distinctValues: values.length + 4,
  };
}

const wallElements: DataElement[] = Array.from({ length: 40 }, (_, index) => {
  const id = 300000 + index;
  return {
    name: index % 3 ? "Basic Wall: Generic - 200mm" : "Basic Wall: Exterior - Brick",
    id,
    level: `Level ${index % 3}`,
    isElementType: false,
    elementTypeId: 4000 + (index % 3),
    categoryName: "Walls",
    parameters: [
      { name: "Fire rating", id: 101, level: "", elementId: id, isTypeParameter: false, origin: "Shared", value: index % 4 ? "EI 60" : "", storageType: "String", isReadOnly: false },
      { name: "Comments", id: -1010106, level: "", elementId: id, isTypeParameter: false, origin: "BuiltIn", value: index % 5 ? "" : "check", storageType: "String", isReadOnly: false },
      { name: "Length", id: -1004005, level: "", elementId: id, isTypeParameter: false, origin: "BuiltIn", value: (2.5 + index * 0.37).toFixed(2), storageType: "Double", isReadOnly: true },
      { name: "Classification", id: 103, level: "", elementId: 4000 + (index % 3), isTypeParameter: true, origin: "Shared", value: "Ss_25_10_30", storageType: "String", isReadOnly: false },
    ],
  };
});

// ---- The handlers ------------------------------------------------------------------------------------------


// ---- Model tests: a suite with rules, a check per element and a test of the model; accepting and repairing change the next run
const testSuites: ModelTestList = {
  suites: [
    {
      id: "bep-project-x.BepProjectX",
      name: "BEP Project X",
      extensionId: "bep-project-x",
      extensionName: "BEP Project X",
      directory: "C:\\Users\\you\\AppData\\Local\\AnalyseTool\\extensions\\bep-project-x",
      tests: [
        { id: "bep-project-x.BepProjectX/doors-fire-rating-not-empty", name: "Doors · Fire Rating · not empty", description: "Doors · Fire Rating · not empty", kind: "Rule", severity: "Error", traits: ["Discipline=ARC"], slow: false, skip: null, file: "src\\BepProjectX.cs", line: 12, fix: "Set to 'EI30'" },
        { id: "bep-project-x.BepProjectX/room-number-not-empty-unique", name: "Room · Number · not empty, unique", description: "Room (filtered) · Number · not empty, unique", kind: "Rule", severity: "Error", traits: ["Discipline=ARC"], slow: false, skip: null, file: "src\\BepProjectX.cs", line: 16, fix: null },
        { id: "bep-project-x.BepProjectX/swing-zone-is-free", name: "Swing zone is free", description: "Doors · code", kind: "Check", severity: "Warning", traits: ["Discipline=ARC"], slow: false, skip: null, file: "src\\BepProjectX.cs", line: 21, fix: null },
        { id: "bep-project-x.BepProjectX/no-unplaced-rooms", name: "No unplaced rooms", description: "Whole model · code", kind: "Model", severity: "Error", traits: ["Discipline=ARC"], slow: false, skip: null, file: "src\\BepProjectX.cs", line: 28, fix: null },
        { id: "bep-project-x.BepProjectX/ducts-do-not-hit-beams", name: "Ducts do not hit beams", description: "Duct Curves · code", kind: "Check", severity: "Error", traits: ["Discipline=MEP"], slow: true, skip: null, file: "src\\Clashes.cs", line: 9, fix: null },
      ],
    },
    {
      id: "sikla-test.SiklaTest",
      name: "Sikla Test",
      extensionId: "sikla-test",
      extensionName: "Sikla Test",
      directory: "C:\\Users\\you\\AppData\\Local\\AnalyseTool\\extensions\\sikla-test",
      tests: [
        { id: "sikla-test.SiklaTest/project-number-is-set", name: "Project number is set", description: "Whole model · code", kind: "Model", severity: "Error", traits: [], slow: false, skip: null, file: "src\\SiklaTest.cs", line: 14, fix: null },
        { id: "sikla-test.SiklaTest/sheet-numbers-are-unique", name: "Sheet numbers are unique", description: "Sheets · SHEET_NUMBER · not empty, unique", kind: "Rule", severity: "Error", traits: ["Discipline=Documentation"], slow: false, skip: null, file: "src\\SiklaTest.cs", line: 70, fix: null },
      ],
    },
  ],
};
const testAccepted = new Set<string>(Array.from({ length: 6 }, (_, index) => `door-${500 + index}||`));
const testFixed = new Set<string>();
let testTrend: ModelTestRun["trend"] = [9, 8, 8, 6, 5, 4, 4, 3].map((newCases, index) => ({ at: ago((8 - index) * 600), newCases, baselineCases: 6, failed: 2 }));
let lastTestRun: ModelTestRun | null = null;

function testCase(key: string, status: ModelTestCase["status"], message: string, elementId: number | null, label: string | null, extra: Partial<ModelTestCase> = {}): ModelTestCase {
  return { key, status, message, elementId, label, related: [], inBaseline: status === "failed" && testAccepted.has(key), point: null, box: null, ...extra };
}

function testResult(testId: string, cases: ModelTestCase[], checked: number): ModelTestResult {
  const failed = cases.filter((item) => item.status === "failed");
  const baseline = failed.filter((item) => item.inBaseline).length;
  const fresh = failed.length - baseline;
  const errors = cases.filter((item) => item.status === "error").length;
  const inconclusive = cases.filter((item) => item.status === "inconclusive").length;
  const status = fresh ? "failed" : errors ? "error" : inconclusive ? "inconclusive" : "passed";
  const order = (item: ModelTestCase) => (item.status === "failed" && !item.inBaseline ? 0 : item.status === "error" ? 1 : item.status === "inconclusive" ? 2 : 3);
  return { testId, status, checked, failed: failed.length, new: fresh, baseline, resolved: 0, inconclusive, errors, skip: null, cases: [...cases].sort((a, b) => order(a) - order(b)), truncated: false, milliseconds: 12 };
}

function runTestsMock(request: RunModelTestsRequest): ModelTestRun {
  const [doors, rooms, swing, unplaced, clashes] = testSuites.suites[0].tests;
  const all: ModelTestResult[] = [
    testResult(doors.id, [
      ...Array.from({ length: 6 }, (_, index) => testCase(`door-${500 + index}||`, "failed", "«Fire Rating» is empty", 500 + index, `Doors · T30_Single 900 · ${500 + index}`)),
      ...[412, 413, 414].filter((id) => !testFixed.has(`door-${id}||`)).map((id) => testCase(`door-${id}||`, "failed", "«Fire Rating» is empty", id, `Doors · T30_Single 900 · ${id}`)),
    ], 210),
    testResult(rooms.id, [
      testCase("room-7||unique", "failed", "«Number» '0.14' is used by 1 other element", 7, "Rooms · Flur · 0.14", { related: [8] }),
      testCase("room-8||unique", "failed", "«Number» '0.14' is used by 1 other element", 8, "Rooms · Lager · 0.14", { related: [7] }),
    ], 64),
    testResult(swing.id, [
      testCase("door-412||", "failed", "Swing zone blocked by Furniture · Schrank", 412, "Doors · T30_Single 900 · 412", { related: [9001], box: [10, 20, 0, 12.9, 22.9, 7] }),
      testCase("door-420||", "inconclusive", "The geometry of the door could not be read", 420, "Doors · Imported DWG door · 420"),
    ], 210),
    testResult(unplaced.id, [], 1),
    request.includeSlow || request.tests?.includes(clashes.id)
      ? testResult(clashes.id, [testCase("duct-1|beam-2|", "failed", "Clash 40 mm with Structural Framing · HEA 200", 3001, "Ducts · Rectangular · 3001", { related: [4002] })], 88)
      : { ...testResult(clashes.id, [], 0), status: "skipped", skip: "Slow: runs when slow tests are included or the test is run on its own." },
  ];
  const results = all.filter(
    (result) => (!request.tests?.length || request.tests.includes(result.testId)) && (!request.suite || result.testId.startsWith(request.suite)),
  );
  const full = !request.suite && !request.tests?.length && !request.elementIds?.length && !request.trait;
  const totals = {
    tests: results.length,
    passed: results.filter((r) => r.status === "passed").length,
    failed: results.filter((r) => r.status === "failed").length,
    errors: results.filter((r) => r.status === "error").length,
    inconclusive: results.filter((r) => r.status === "inconclusive").length,
    skipped: results.filter((r) => r.status === "skipped").length,
    newCases: results.reduce((sum, r) => sum + r.new, 0),
    baselineCases: results.reduce((sum, r) => sum + r.baseline, 0),
    inconclusiveCases: results.reduce((sum, r) => sum + r.inconclusive, 0),
  };
  const at = new Date().toISOString();
  if (full) testTrend = [...testTrend, { at, newCases: totals.newCases, baselineCases: totals.baselineCases, failed: totals.failed }].slice(-30);
  lastTestRun = { model: "Projekt X – ARC.rvt", modelKey: "mock-model", at, milliseconds: 840, trigger: request.trigger ?? "page", full, totals, results, trend: testTrend };
  return lastTestRun;
}

registerMocks({
  "Host.GetCommands": () => [
    { name: "CountElementsByCategory", description: "Counts the model elements by category.", requestType: null, responseType: "IReadOnlyList`1" },
    { name: "GetActiveDocument", description: "Returns the active document.", requestType: null, responseType: "DocumentSummary" },
    { name: "GetLogEntries", description: "Returns the log records after the given sequence number.", requestType: "GetLogEntriesRequest", responseType: "LogPage" },
    { name: "GetMcpStatus", description: "Returns the state of the MCP server.", requestType: null, responseType: "McpEndpointInfo" },
    { name: "Host.GetCommands", description: "Lists the registered commands.", requestType: null, responseType: "IReadOnlyList`1" },
  ],

  // A few new records on every poll, as a working add-in would write them
  GetLogEntries: (payload) => {
    const after = (payload as { afterSequence: number }).afterSequence;
    const entries = [];
    for (let count = Math.random() < 0.5 ? 1 : 0; count > 0; count--) {
      logSequence++;
      entries.push({
        sequence: logSequence,
        timestamp: new Date().toISOString(),
        level: levels[logSequence % levels.length],
        category: "AnalyseTool.Features.Sample.CountElementsByCategory",
        message: `Mock record ${logSequence}`,
        exception: logSequence % 7 === 0 ? "System.InvalidOperationException: mock\n   at Mock.Run()" : null,
      });
    }
    return { entries: entries.filter((entry) => entry.sequence > after), lastSequence: logSequence };
  },

  GetMcpStatus: () => ({
    state: "Listening",
    url: mcpUrl,
    name: "analysetool-2026",
    token,
    claudeCode: mcp.claudeCode,
    connectionFile: mcp.connectionFile,
    tools: [
      { name: "CountElementsByCategory", description: "Counts the model elements of the active document by category.", readOnly: true },
      { name: "GetActiveDocument", description: "Returns the key and title of the active document.", readOnly: true },
    ],
  }),

  GetActiveDocument: () => ({ key: "C:\\Models\\Sample.rvt", title: "Sample.rvt" }),

  CountElementsByCategory: async (_, { onProgress, signal }) => {
    for (let step = 1; step <= 10; step++) {
      await delay(150);
      if (signal?.aborted) throw new DOMException("The call was cancelled.", "AbortError");
      onProgress?.({ fraction: step / 10, message: `${step * 50} / 500` });
    }
    return [
      { category: "Walls", count: 210 },
      { category: "Doors", count: 140 },
      { category: "Windows", count: 150 },
    ];
  },

  // ---- App
  GetAppInfo: (): AppInfo => ({
    version: "2.0.0-alpha.3",
    revitVersion: "2026",
    changelog,
    profileFolder: String.raw`C:\Users\you\AppData\Local\AnalyseTool`,
    mcp: { ...mcp },
  }),
  SetMcpEnabled: async (payload) => {
    await delay(300);
    const { enabled } = payload as { enabled: boolean };
    mcp.enabled = enabled;
    mcp.state = enabled ? "listening" : "off";
    return { ...mcp };
  },
  GetUpdate: async () => {
    await delay(150);
    return { ...update };
  },
  SetScriptingEnabled: async (payload) => {
    await delay(200);
    mcp.scripting = (payload as { enabled: boolean }).enabled;
    return { ...mcp };
  },
  OpenPath: (payload) => {
    console.info("OpenPath", (payload as { path: string }).path);
    return null;
  },
  GetPaneButtons: async (): Promise<PaneOverview> => {
    await delay(120);
    return { userName: "nikolai", machineName: "WS-042", profileFile: String.raw`C:\Users\you\AppData\Local\AnalyseTool\pane-profile.json`, folders: structuredClone(paneFolders) };
  },
  RunButton: async (payload): Promise<ButtonRunResult> => {
    const key = (payload as { key: string }).key;
    await delay(600);
    if (key === "room-sheets/Room sheets") return { ok: true, text: null, error: null, elapsedMilliseconds: 0, openedPage: false, pageUrl: `${location.origin}/mock-ext.html` };
    if (key === "room-sheets/Renumber") return { ok: true, text: `Renumbered 12 sheets from ${JSON.stringify((payload as { input?: unknown }).input)}`, error: null, elapsedMilliseconds: 240, openedPage: false };
    if (key.startsWith("count-walls")) return { ok: true, text: "Level 1: 42 walls\nLevel 2: 38 walls", error: null, elapsedMilliseconds: 182, openedPage: false };
    if (key === "room-sheets/Purge empty") return { ok: false, text: null, error: "Open a document first.", elapsedMilliseconds: 12, openedPage: false };
    return { ok: true, text: null, error: null, elapsedMilliseconds: 0, openedPage: !key.startsWith("room-sheets/Renumber") };
  },
  CreateExtensionFolder: async (payload) => {
    await delay(300);
    const { name } = payload as { name: string };
    const id = name.trim().toLowerCase().replace(/[^a-z0-9._-]+/g, "-");
    if (paneFolders.some((folder) => folder.id === id)) fail(`An extension with the id '${id}' exists already.`, "exists");
    paneFolders.push({ id, name: name.trim(), builtIn: false, enabled: true, canAddButtons: true, description: null, directory: `C:/Users/you/AppData/Local/AnalyseTool/extensions/${id}`, tab: name.trim(), panel: name.trim(), panels: [name.trim()], problems: [], buttons: [] });
    return { id, name: name.trim() };
  },
  AddExtensionButton: async (payload) => {
    await delay(400);
    const { id, name, template, panel } = payload as { id: string; name: string; template?: string; panel?: string | null };
    const folder = paneFolders.find((candidate) => candidate.id === id) ?? fail(`No folder '${id}'.`, "not_found");
    folder.buttons.push({ ...paneButton(id, name.trim(), template === "Command" ? g(0xe768) : g(0xe8a7), template === "Command" ? `${id}.${name.replace(/\W/g, "")}` : null), panel: panel ?? folder.panel ?? folder.name });
    return { id, name: folder.name };
  },
  UpdateExtensionFolder: (payload) => {
    const { id, name, description } = payload as { id: string; name: string; description?: string | null };
    const folder = paneFolders.find((candidate) => candidate.id === id) ?? fail(`No folder '${id}'.`, "not_found");
    folder.name = name.trim();
    folder.description = description ?? null;
    return { id, name: folder.name };
  },
  UpdateExtensionButton: (payload) => {
    const { id, button, name, tooltip } = payload as { id: string; button: string; name: string; tooltip?: string | null };
    const folder = paneFolders.find((candidate) => candidate.id === id) ?? fail(`No folder '${id}'.`, "not_found");
    const found = folder.buttons.find((candidate) => candidate.name === button) ?? fail(`No button «${button}».`, "not_found");
    found.name = name.trim();
    found.key = `${id}/${found.name}`;
    found.tooltip = tooltip ?? null;
    return { id, name: folder.name };
  },
  SetExtensionPanel: (payload) => {
    const { id, panel, name } = payload as { id: string; panel: string | null; name: string };
    const folder = paneFolders.find((candidate) => candidate.id === id) ?? fail(`No folder '${id}'.`, "not_found");
    const panels = folder.panels ?? [];
    if (panel === null) folder.panels = [...panels, name];
    else {
      folder.panels = panels.map((item) => (item === panel ? name : item));
      for (const button of folder.buttons) if ((button.panel ?? folder.panel) === panel) button.panel = name;
    }
    return { id, name: folder.name };
  },
  MoveExtensionButton: (payload) => {
    const { id, button, panel, before, toId } = payload as { id: string; button: string; panel: string; before?: string | null; toId?: string | null };
    const from = paneFolders.find((candidate) => candidate.id === id) ?? fail(`No folder '${id}'.`, "not_found");
    const to = paneFolders.find((candidate) => candidate.id === (toId ?? id)) ?? fail(`No folder '${toId}'.`, "not_found");
    const moving = from.buttons.filter((candidate) => candidate.name === button || candidate.group === button);
    if (!moving.length) fail(`No button «${button}».`, "not_found");
    from.buttons = from.buttons.filter((candidate) => !moving.includes(candidate));
    for (const item of moving) {
      item.panel = panel;
      item.key = `${to.id}/${item.name}`;
    }
    const at = before ? to.buttons.findIndex((candidate) => candidate.name === before || candidate.group === before) : -1;
    to.buttons.splice(at < 0 ? to.buttons.length : at, 0, ...moving);
    if (!(to.panels ?? []).includes(panel)) to.panels = [...(to.panels ?? []), panel];
    return { id: to.id, name: to.name };
  },
  "room-sheets.Count": async (payload) => {
    await delay(300);
    const { category } = payload as { category: string };
    return { title: "Tower.rvt", count: category === "OST_Rooms" ? 64 : 210 };
  },
  GetRevitTheme: () => ({ theme: window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light" }),
  RefreshButtons: async () => {
    await delay(400);
    return null;
  },
  GetPaneProfile: () => ({ profile: paneProfile }),
  SavePaneProfile: (payload) => {
    paneProfile = (payload as { profile: unknown }).profile;
    return null;
  },
  GetButtonStatistics: async (): Promise<ButtonStatisticsResult> => {
    await delay(150);
    return { since: statistics.some((item) => item.clicks) ? ago(60 * 24 * 40) : null, totalClicks: statistics.reduce((sum, item) => sum + item.clicks, 0), buttons: [...statistics].sort((a, b) => b.clicks - a.clicks) };
  },
  ResetButtonStatistics: (payload) => {
    const extension = (payload as { extension?: string } | null)?.extension;
    let removed = 0;
    statistics = statistics
      .filter((item) => item.onRibbon || (extension && item.extension !== extension))
      .map((item) => {
        if (extension && item.extension !== extension) return item;
        if (item.clicks) removed++;
        return { ...item, clicks: 0, failures: 0, firstClick: null, lastClick: null, lastError: null, lastErrorAt: null, averageMilliseconds: null, last30Days: Array<number>(30).fill(0) };
      });
    return { removed };
  },
  OpenUrl: (payload) => {
    window.open((payload as { url: string }).url, "_blank");
    return null;
  },
  OpenWindow: (payload) => {
    window.open(`#${(payload as { route: string }).route}`, "_blank");
    return null;
  },
  BrowseFolder: () => {
    const path = window.prompt("Folder (mock of the folder picker)", String.raw`D:\Team\buttons`);
    return { path: path || null };
  },
  BrowseFile: (payload) => {
    const request = payload as { title?: string; initial?: string; filter?: string };
    const fallback = request.filter?.includes("zip") ? String.raw`C:\Downloads\Acme.Sample-2.2.0.zip` : String.raw`C:\BIM\SharedParameters.txt`;
    const path = window.prompt(request.title ?? "File (mock of the file dialog)", request.initial ?? fallback);
    return { path: path || null };
  },

  // ---- Extensions
  GetExtensions: async () => {
    await delay(150);
    return list();
  },
  ReloadExtensions: async () => {
    await delay(500);
    return list();
  },
  CreateExtension: async (payload) => {
    await delay(600);
    const request = payload as { name: string; description?: string; id?: string; template?: string; root?: string };
    const id = request.id || request.name.replace(/[^A-Za-z0-9]+(.)?/g, (_, c: string | undefined) => (c ? c.toUpperCase() : "")).replace(/^./, (c) => c.toUpperCase());
    if (extensions.some((item) => item.summary.id.toLowerCase() === id.toLowerCase())) fail(`An extension '${id}' exists.`, "invalid");
    const page = request.template !== "Command";
    const files: Record<string, string> = {
      "plugin.json": pluginJson(id, request.name, request.description ?? ""),
      "AGENTS.md": agents,
      "CLAUDE.md": claude,
      ...(request.template !== "Page" ? { [`src/${id}.cs`]: countWallsV1.replaceAll("CountWalls", id) } : {}),
      ...(page ? { "index.html": `<!doctype html>\n<h1>${request.name}</h1>\n` } : {}),
    };
    const extension: MockExtension = {
      summary: summary({
        id,
        name: request.name,
        description: request.description ?? null,
        directory: `${request.root ?? root}\\${id}`,
        kind: request.template === "Page" ? "Page" : "Source",
        commands: request.template === "Page" ? [] : [`${id}.${id}`],
      }),
      files,
      versions: [],
    };
    save(extension, "Created");
    extensions.push(extension);
    return extension.summary;
  },
  SetExtensionEnabled: (payload) => {
    const { id, enabled } = payload as { id: string; enabled: boolean };
    const extension = find(id);
    extension.summary = { ...extension.summary, enabled };
    return extension.summary;
  },
  RemoveExtension: (payload) => {
    const id = (payload as { id: string }).id;
    // A tab of the pane, or a button of one: gone from the pane
    const tab = paneFolders.findIndex((folder) => folder.id === id);
    const owner = paneFolders.find((folder) => folder.buttons.some((button) => button.key.startsWith(id + "/")));
    if (tab >= 0) paneFolders.splice(tab, 1);
    else if (owner) owner.buttons = owner.buttons.filter((button) => !button.key.startsWith(id + "/"));
    if (tab >= 0 || owner) return { movedTo: `C:/Users/you/AppData/Local/AnalyseTool/trash/${id}-20261116` };
    const extension = find(id);
    extensions.splice(extensions.indexOf(extension), 1);
    return { movedTo: String.raw`C:\Users\you\AppData\Local\AnalyseTool\trash\${extension.summary.id}-20261116` };
  },
  InstallExtensionPackage: async (payload): Promise<PackageInstallResult> => {
    await delay(500);
    const { overwrite } = payload as { path: string; overwrite?: boolean };
    const extension = find("Acme.Sample");
    if (!overwrite) {
      return { id: "Acme.Sample", version: "2.2.0", directory: extension.summary.directory, replaced: false, alreadyInstalled: true, extension: extension.summary };
    }
    extension.summary = { ...extension.summary, version: "2.2.0" };
    return { id: "Acme.Sample", version: "2.2.0", directory: extension.summary.directory, replaced: true, alreadyInstalled: false, extension: extension.summary };
  },
  BuildExtension: async (payload, { signal }) => {
    await delay(900);
    if (signal?.aborted) throw new DOMException("The call was cancelled.", "AbortError");
    const extension = find((payload as { id: string }).id);
    if (extension.summary.kind !== "Source") fail(`'${extension.summary.id}' is not built from sources.`, "invalid");
    const broken = Object.values(extension.files).some((text) => text.includes("grup."));
    extension.summary = {
      ...extension.summary,
      diagnostics: broken ? countWallsDiagnostics : [],
      usesOlderBuild: broken,
    };
    return { success: !broken, diagnostics: extension.summary.diagnostics, extension: extension.summary };
  },
  GetAuthoringGuide: () => ({ markdown: llm, path: "C:/Users/me/AppData/Roaming/Autodesk/Revit/Addins/2025/AnalyseTool.Launcher/LLM.md" }),
  ListExtensionFiles: (payload) => {
    const extension = find((payload as { id: string }).id);
    return Object.entries(extension.files).map(([path, text], index) => ({ path, size: text.length || 24576, modified: ago(index * 37 + 5) }));
  },
  ReadExtensionFile: (payload) => {
    const { id, path } = payload as { id: string; path: string };
    const text = find(id).files[path];
    if (text === undefined) fail(`No file '${path}'.`, "not_found");
    return { path, content: text };
  },
  WriteExtensionFile: (payload) => {
    const { id, path, content } = payload as { id: string; path: string; content: string };
    const extension = find(id);
    extension.files[path] = content;
    return extension.summary;
  },
  DeleteExtensionFile: (payload) => {
    const { id, path } = payload as { id: string; path: string };
    const extension = find(id);
    delete extension.files[path];
    return extension.summary;
  },
  GetExtensionVersions: async (payload) => {
    await delay(120);
    const extension = find((payload as { id: string }).id);
    if (!extension.versions.length) {
      return {
        info: { available: true, unavailableReason: null, hasHistory: false, changes: [], latest: null, pinned: null, loadedVersion: null },
        history: [],
      };
    }
    const pinned = extension.versions.find((item) => item.version.id === extension.summary.loadedVersion)?.version ?? null;
    return {
      info: {
        available: true,
        unavailableReason: null,
        hasHistory: extension.versions.length > 0,
        changes: changesOf(extension),
        latest: extension.versions[0]?.version ?? null,
        pinned,
        loadedVersion: extension.summary.loadedVersion,
      },
      history: extension.versions.map((item) => item.version),
    };
  },
  SaveExtensionVersion: async (payload) => {
    await delay(300);
    const { id, message } = payload as { id: string; message?: string | null };
    const extension = find(id);
    if (!changesOf(extension).length) return { version: null };
    return { version: save(extension, message || "Saved from AnalyseTool") };
  },
  GetExtensionDiff: async (payload) => {
    await delay(150);
    const { id, from, to } = payload as { id: string; from?: string | null; to?: string | null };
    const extension = find(id);
    if (!to) return diff(extension.versions[0]?.files ?? {}, extension.files);
    const index = versionIndex(extension, to);
    const before = from ? extension.versions[versionIndex(extension, from)].files : (extension.versions[index + 1]?.files ?? {});
    return diff(before, extension.versions[index].files);
  },
  RestoreExtensionVersion: async (payload) => {
    await delay(400);
    const { id, version, file } = payload as { id: string; version: string; file?: string | null };
    const extension = find(id);
    const source = extension.versions[versionIndex(extension, version)];
    if (changesOf(extension).length) save(extension, "Unsaved changes before restoring");
    if (file) {
      if (file in source.files) extension.files[file] = source.files[file];
      else delete extension.files[file];
    } else extension.files = { ...source.files };
    return { version: save(extension, file ? `Restored ${file} from ${source.version.shortId}` : `Restored ${source.version.shortId}`) };
  },
  PinExtensionVersion: async (payload) => {
    await delay(300);
    const { id, version } = payload as { id: string; version: string | null };
    const extension = find(id);
    const full = version ? extension.versions[versionIndex(extension, version)].version.id : null;
    extension.summary = { ...extension.summary, loadedVersion: full };
    return extension.summary;
  },
  GetExtensionRoots: () => roots,
  AddExtensionRoot: async (payload) => {
    await delay(300);
    roots = { ...roots, roots: [...roots.roots, { path: (payload as { path: string }).path, zone: "Dev", isDefault: false }] };
    return roots;
  },
  RemoveExtensionRoot: (payload) => {
    const path = (payload as { path: string }).path;
    roots = { roots: roots.roots.filter((item) => item.path !== path), authoringRoot: roots.authoringRoot === path ? root : roots.authoringRoot };
    return roots;
  },
  SetAuthoringRoot: (payload) => {
    roots = { ...roots, authoringRoot: (payload as { path: string }).path };
    return roots;
  },

  // ---- Shared parameters
  GetSharedParameterFile: async () => {
    await delay(200);
    return structuredClone(sharedFile);
  },
  SetSharedParameterFile: (payload) => {
    const { path, create } = payload as { path: string; create?: boolean };
    sharedFile = create ? { ...sharedFile, path, exists: true, groups: [], parameters: [] } : { ...sharedFile, path };
    return structuredClone(sharedFile);
  },
  SaveSharedParameterFile: async (payload) => {
    await delay(300);
    const request = payload as { groups: SharedParameterFileData["groups"]; parameters: SharedParameterDto[] };
    sharedFile = { ...sharedFile, groups: request.groups, parameters: request.parameters, stamp: new Date().toISOString() };
    return structuredClone(sharedFile);
  },
  GetProjectParameters: async () => {
    await delay(150);
    return { parameters: structuredClone(projectParameters) };
  },
  GetBindingOptions: () => ({
    categories: bindingCategories,
    groups: [
      { typeId: "autodesk.parameter.group:identityData-1.0.0", label: "Identity Data" },
      { typeId: "autodesk.parameter.group:data-1.0.0", label: "Data" },
      { typeId: "autodesk.parameter.group:other-1.0.0", label: "Other" },
    ],
    defaultGroup: "autodesk.parameter.group:data-1.0.0",
  }),
  BindSharedParameters: async (payload) => {
    await delay(400);
    const request = payload as { guids: string[]; categoryIds: number[]; isInstance: boolean };
    const names = request.categoryIds.map((id) => bindingCategories.find((item) => item.id === id)?.name ?? String(id));
    const added: string[] = [];
    const updated: string[] = [];
    for (const g of request.guids) {
      const parameter = sharedFile.parameters.find((item) => item.guid.toLowerCase() === g.toLowerCase());
      if (!parameter) continue;
      const existing = projectParameters.find((item) => item.guid?.toLowerCase() === g.toLowerCase());
      if (existing) {
        existing.bound = true;
        existing.categories = [...new Set([...existing.categories, ...names])];
        updated.push(parameter.name);
      } else {
        projectParameters = [
          ...projectParameters,
          { id: 200 + projectParameters.length, guid: parameter.guid, name: parameter.name, isShared: true, bound: true, isInstance: request.isInstance, dataType: parameter.dataType, group: "Data", categories: names },
        ];
        added.push(parameter.name);
      }
    }
    return { added, updated, problems: [] };
  },
  GetDocumentData: () => ({ name: "Sample.rvt", id: "doc-guid-1" }),
  GetParameterSets: () => ({ path: String.raw`C:\Users\you\AppData\Local\AnalyseTool\parameter-sets.json`, sets: parameterSets }),
  SaveParameterSets: (payload) => {
    const sets = ((payload as { sets: ParameterSetDto[] }).sets ?? []).map((set, index) => ({ ...set, id: set.id || `set-${Date.now()}-${index}` }));
    parameterSets = sets;
    return { path: String.raw`C:\Users\you\AppData\Local\AnalyseTool\parameter-sets.json`, sets };
  },
  GetParameterReport: async (payload) => {
    await delay(500);
    const request = payload as { parameters: { guid?: string; name?: string }[] };
    return {
      documentTitle: "Sample.rvt",
      parameters: request.parameters.map((ref, index) => {
        const known = projectParameters.find((item) => (ref.guid ? item.guid === ref.guid : item.name === ref.name));
        return report(ref.name ?? "?", ref.guid, known?.isInstance ?? true, index + 1);
      }),
    };
  },

  // ---- Commands of the mock extensions, for the dock pane
  "Acme.Sample.Hello": () => ({ message: "Hello from Acme" }),
  "CountWalls.CountWalls": async () => {
    await delay(400);
    return { "Generic - 200mm": 27, "Exterior - Brick": 13 };
  },
  "RoomSheets.ListRooms": () => [{ number: "101", name: "Office" }, { number: "102", name: "Meeting" }],
  "RoomSheets.CreateSheets": () => fail("Select rooms first.", "invalid"),

  // ---- Elements and actions
  GetCategoriesInRevit: () => ["Doors", "Floors", "Rooms", "Walls", "Windows"],
  GetDataByCategoryName: async (payload, { onProgress, signal }) => {
    for (let step = 1; step <= 5; step++) {
      await delay(120);
      if (signal?.aborted) throw new DOMException("The call was cancelled.", "AbortError");
      onProgress?.({ fraction: step / 5, message: `${step * 8} / 40 elements` });
    }
    const category = (payload as { categoryName: string }).categoryName;
    return wallElements.map((element) => ({ ...element, categoryName: category }));
  },
  SelectionInRevit: (payload) => ({ ok: true, selected: (payload as { elementIds: number[] }).elementIds.length, error: null }),
  IsolationInRevit: (payload) => ({ ok: true, isolated: (payload as { elementIds: number[] }).elementIds.length, error: null, warnings: [] }),
  SetDataToParameters: async (payload, { onProgress }) => {
    const items = (payload as { items: { elementId: number; id: number; value: string }[] }).items;
    for (let step = 1; step <= 4; step++) {
      await delay(150);
      onProgress?.({ fraction: step / 4, message: `${Math.round((items.length * step) / 4)} / ${items.length}` });
    }
    for (const item of items) {
      for (const element of wallElements) {
        const parameter = element.parameters.find((p) => p.id === item.id && (p.elementId === item.elementId || element.id === item.elementId));
        if (parameter) parameter.value = item.value;
      }
    }
    return { ok: true, written: items.length, skipped: 0, error: null, warnings: [] };
  },

  // ---- Model tests
  GetModelTests: () => testSuites,
  GetModelTestResults: () => lastTestRun,
  RunModelTests: async (payload) => {
    await delay(500);
    return runTestsMock((payload ?? {}) as RunModelTestsRequest);
  },
  FixModelTestCases: async (payload) => {
    const { keys } = payload as { testId: string; keys?: string[] | null };
    const chosen = keys?.length ? keys : ["door-412||", "door-413||", "door-414||"];
    chosen.forEach((key) => testFixed.add(key));
    await delay(200);
    return { testId: (payload as { testId: string }).testId, fixed: chosen.length, problems: [] };
  },
  AcceptModelTestFailures: (payload) => {
    const { testId, keys, remove } = payload as { testId: string; keys?: string[] | null; remove?: boolean };
    const result = lastTestRun?.results.find((r) => r.testId === testId);
    const chosen = keys?.length ? keys : (result?.cases.filter((c) => c.status === "failed").map((c) => c.key) ?? []);
    chosen.forEach((key) => (remove ? testAccepted.delete(key) : testAccepted.add(key)));
    return { testId, accepted: testAccepted.size, file: "baseline\\mock-model.json" };
  },
  CreateModelTestSuite: (payload) => fail(`Creating «${(payload as { name: string }).name}» needs Revit.`, "invalid"),
  ShowGeometry: () => ({ added: 1, missing: [], shown: 1 }),
  ShowElementsIn3D: (payload) => ({ ok: true, isolated: (payload as { elementIds: number[] }).elementIds.length, view: "{3D}", error: null }),
  ClearGeometry: () => null,
});
