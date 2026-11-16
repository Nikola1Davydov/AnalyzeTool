// The wire contract of the extension commands (source/AnalyseTool.Extensions/Features/*.cs), written by hand to match
// the C# records: camelCase names, enums as strings with their C# names, null written as null.

export const extensionCommands = {
  GetExtensions: "GetExtensions",
  ReloadExtensions: "ReloadExtensions",
  CreateExtension: "CreateExtension",
  CreateExtensionFolder: "CreateExtensionFolder",
  AddExtensionButton: "AddExtensionButton",
  UpdateExtensionFolder: "UpdateExtensionFolder",
  UpdateExtensionButton: "UpdateExtensionButton",
  SetExtensionPanel: "SetExtensionPanel",
  MoveExtensionButton: "MoveExtensionButton",
  SetExtensionEnabled: "SetExtensionEnabled",
  RemoveExtension: "RemoveExtension",
  BuildExtension: "BuildExtension",
  GetAuthoringGuide: "GetAuthoringGuide",
  ListExtensionFiles: "ListExtensionFiles",
  ReadExtensionFile: "ReadExtensionFile",
  WriteExtensionFile: "WriteExtensionFile",
  DeleteExtensionFile: "DeleteExtensionFile",
  GetExtensionVersions: "GetExtensionVersions",
  SaveExtensionVersion: "SaveExtensionVersion",
  GetExtensionDiff: "GetExtensionDiff",
  RestoreExtensionVersion: "RestoreExtensionVersion",
  PinExtensionVersion: "PinExtensionVersion",
  GetExtensionRoots: "GetExtensionRoots",
  AddExtensionRoot: "AddExtensionRoot",
  RemoveExtensionRoot: "RemoveExtensionRoot",
  SetAuthoringRoot: "SetAuthoringRoot",
  InstallExtensionPackage: "InstallExtensionPackage",
} as const;

/** `Dev` — the user's own; `Managed` — an installed package. */
export type ExtensionZone = "Managed" | "Dev";

/** `Source` — C# sources AnalyseTool compiles; `Dll` — a prebuilt DLL; `Page` — no commands. */
export type ExtensionKind = "Source" | "Dll" | "Page";

/** Command: a button that runs C#; CommandWithPage: a button that opens a page calling C#; Page: a page without C#. */
export type ExtensionTemplate = "Command" | "CommandWithPage" | "Page";

export interface CompileDiagnostic {
  /** Relative to the extension folder. */
  file: string;
  line: number;
  column: number;
  /** "error" | "warning" */
  severity: string;
  code: string;
  message: string;
}

export interface ExtensionSummary {
  id: string;
  name: string;
  description: string | null;
  version: string;
  publisher: string | null;
  directory: string;
  zone: ExtensionZone;
  kind: ExtensionKind;
  enabled: boolean;
  compatible: boolean;
  commands: string[];
  buttons: string[];
  errors: string[];
  diagnostics: CompileDiagnostic[];
  /** The pinned version that is loaded; null for the current files. */
  loadedVersion: string | null;
  /** The current sources do not compile; an earlier build is loaded. */
  usesOlderBuild: boolean;
}

export interface ExtensionList {
  revitVersion: string;
  extensions: ExtensionSummary[];
  /** Folder → why its plugin.json cannot be read. */
  brokenFolders: Record<string, string>;
}

export interface ReloadExtensionsRequest {
  id?: string | null;
}

export interface CreateExtensionFolderRequest {
  name: string;
  description?: string | null;
  id?: string | null;
  tab?: string | null;
  panel?: string | null;
  root?: string | null;
}

export interface AddExtensionButtonRequest {
  /** The folder (extension). */
  id: string;
  name: string;
  description?: string | null;
  template?: ExtensionTemplate;
  /** Its own ribbon tab and panel; the folder's when absent. */
  tab?: string | null;
  panel?: string | null;
}

export interface SetExtensionPanelRequest {
  id: string;
  /** The panel to rename; null adds a new one. */
  panel: string | null;
  name: string;
}

export interface MoveExtensionButtonRequest {
  id: string;
  button: string;
  panel: string;
  /** The button it goes before; the end of the panel when null. */
  before?: string | null;
  /** Another folder; the same one when null. */
  toId?: string | null;
}

export interface UpdateExtensionFolderRequest {
  id: string;
  name: string;
  description?: string | null;
  /** Empty: the default; absent: keep. */
  tab?: string | null;
  panel?: string | null;
}

export interface UpdateExtensionButtonRequest {
  id: string;
  /** The current text of the button. */
  button: string;
  name: string;
  tooltip?: string | null;
  /** Empty: the folder's; absent: keep. */
  tab?: string | null;
  panel?: string | null;
  /** Its page opens inside the pane; absent: keep. */
  dockable?: boolean | null;
}

export interface CreateExtensionRequest {
  name: string;
  description?: string | null;
  id?: string | null;
  template?: ExtensionTemplate;
  panel?: string | null;
  root?: string | null;
  /** The tab: an id («sheets.tab») or a name; a tab of that name is made when there is none. */
  tab?: string | null;
}

export interface ExtensionRequest {
  id: string;
}

export interface SetExtensionEnabledRequest {
  id: string;
  enabled: boolean;
}

export interface RemoveExtensionResult {
  movedTo: string;
}

export interface InstallExtensionPackageRequest {
  /** The full path of the .zip package. */
  path: string;
  overwrite?: boolean;
}

export interface PackageInstallResult {
  id: string;
  version: string;
  directory: string;
  /** An installed version was replaced. */
  replaced: boolean;
  /** Installed already and overwrite was not asked for: nothing changed. */
  alreadyInstalled: boolean;
  extension: ExtensionSummary | null;
}

export interface BuildExtensionResult {
  success: boolean;
  diagnostics: CompileDiagnostic[];
  extension: ExtensionSummary | null;
}

export interface AuthoringGuide {
  markdown: string;
  /** LLM.md next to the add-in, updated with it. */
  path: string;
}

// ---- Files ----------------------------------------------------------------------------------------

export interface ExtensionFile {
  /** Relative to the extension folder. */
  path: string;
  size: number;
  modified: string;
}

export interface ExtensionFileRequest {
  id: string;
  path: string;
}

export interface ExtensionFileContent {
  path: string;
  content: string;
}

export interface WriteExtensionFileRequest {
  id: string;
  path: string;
  content: string;
}

// ---- Versions -------------------------------------------------------------------------------------

export interface ExtensionVersion {
  id: string;
  shortId: string;
  message: string;
  author: string;
  time: string;
}

export interface ExtensionChange {
  file: string;
  /** "added" | "modified" | "deleted" | "renamed" */
  state: string;
}

export interface ExtensionVersionInfo {
  available: boolean;
  unavailableReason: string | null;
  hasHistory: boolean;
  changes: ExtensionChange[];
  latest: ExtensionVersion | null;
  pinned: ExtensionVersion | null;
  loadedVersion: string | null;
}

export interface ExtensionVersions {
  info: ExtensionVersionInfo;
  /** Newest first. */
  history: ExtensionVersion[];
}

export interface ExtensionVersionsRequest {
  id: string;
  file?: string | null;
  limit?: number;
}

export interface SaveExtensionVersionRequest {
  id: string;
  message?: string | null;
}

export interface SaveExtensionVersionResult {
  /** Null when nothing changed. */
  version: ExtensionVersion | null;
}

export interface ExtensionDiffRequest {
  id: string;
  from?: string | null;
  to?: string | null;
}

export interface ExtensionFileDiff {
  file: string;
  /** "added" | "modified" | "deleted" */
  state: string;
  before: string | null;
  after: string | null;
  binary: boolean;
}

export interface RestoreExtensionVersionRequest {
  id: string;
  version: string;
  file?: string | null;
}

export interface PinExtensionVersionRequest {
  id: string;
  version: string | null;
}

// ---- Folders --------------------------------------------------------------------------------------

export interface ExtensionRoot {
  path: string;
  zone: ExtensionZone;
  /** One of the two built-in roots, which cannot be removed. */
  isDefault: boolean;
}

export interface ExtensionRoots {
  roots: ExtensionRoot[];
  authoringRoot: string;
}

export interface ExtensionRootRequest {
  path: string;
}

// ---- Helpers --------------------------------------------------------------------------------------

export const kindLabels: Record<ExtensionKind, string> = {
  Source: "C# sources",
  Dll: "DLL",
  Page: "Page",
};

export const templateLabels: Record<ExtensionTemplate, { title: string; hint: string }> = {
  Command: { title: "Command", hint: "A button that runs C# in Revit." },
  CommandWithPage: { title: "Command with page", hint: "A button that opens an HTML page, which calls your C#." },
  Page: { title: "Page", hint: "A button that opens an HTML page, no C#." },
};

/** Does a version id (full or short) name this version? */
export function sameVersion(a: string | null | undefined, b: string | null | undefined) {
  if (!a || !b) return false;
  const x = a.toLowerCase();
  const y = b.toLowerCase();
  return x.startsWith(y) || y.startsWith(x);
}
