// The wire contract of the dock pane (source/AnalyseTool/Features/Pane/PaneCommands.cs).

export const paneCommands = {
  GetPaneButtons: "GetPaneButtons",
  RunButton: "RunButton",
  RefreshButtons: "RefreshButtons",
  /** Turning an extension on or off: the command of the extensions, named here for the pane. */
  SetFolderEnabled: "SetExtensionEnabled",
  GetPaneProfile: "GetPaneProfile",
  SavePaneProfile: "SavePaneProfile",
} as const;

export interface PaneButton {
  /** `<extension>/<name>`: the key of its statistics and of the profile. */
  key: string;
  name: string;
  tooltip: string | null;
  /** A Segoe MDL2 Assets character. */
  glyph: string | null;
  /** A data URL. */
  image: string | null;
  /** null: it opens a page. */
  command: string | null;
  /** The pulldown it sits in on the ribbon: a folder in the pane. */
  group: string | null;
  /** The ribbon tab it goes on, and its panel — its section of the folder. */
  tab?: string | null;
  panel?: string | null;
  /** plugin.json keeps it off the ribbon («ribbon»: false); «+» of the pane can still load it. */
  offRibbon?: boolean;
  /** The JSON Schema of the input of its command: the pane builds a form. */
  input?: unknown;
  /** Its page is a small interface, shown inside the pane; otherwise a page opens a window. */
  dockable?: boolean;
  hasPage?: boolean;
  /** Compiler errors in the files of this button. */
  problems?: string[] | null;
  /** The error of its last run, when that failed. */
  lastFailure?: string | null;
  lastFailureAt?: string | null;
}

export interface PaneFolder {
  id: string;
  name: string;
  builtIn: boolean;
  enabled: boolean;
  problems: string[];
  buttons: PaneButton[];
  /** One of the user's folders: «+ New button» adds to it, ✎ renames it and its buttons. */
  canAddButtons: boolean;
  description: string | null;
  /** The folder on disk; null for AnalyseTool's own. */
  directory: string | null;
  /** The ribbon tab of its buttons (its name for a folder of the user) and its default panel. */
  tab?: string | null;
  panel?: string | null;
  /** Its sections in order: the panels of its tab. */
  panels?: string[] | null;
  /** The tab is the one the package author chose, not the folder name. */
  tabFromPackage?: boolean;
  /** A DLL project — its author builds and edits it: the Revit years it has a build for. */
  dllBuilds?: string[] | null;
  /** «Older buttons»: the folders of plugin.json of the user, each a section with its own state. */
  parts?: PaneFolder[] | null;
}

export interface PaneOverview {
  userName: string;
  machineName: string;
  profileFile: string;
  folders: PaneFolder[];
}

export interface ButtonRunResult {
  ok: boolean;
  text: string | null;
  error: string | null;
  elapsedMilliseconds: number;
  openedPage: boolean;
  /** A small interface: the address the pane shows in a frame. */
  pageUrl?: string | null;
}

export interface PaneProfileResult {
  profile: unknown;
}
