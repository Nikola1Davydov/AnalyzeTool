// The wire contract of the dock pane (source/AnalyseTool/Features/Pane/PaneCommands.cs).

export const paneCommands = {
  GetPaneButtons: "GetPaneButtons",
  RunButton: "RunButton",
  RefreshButtons: "RefreshButtons",
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
}

export interface PaneFolder {
  id: string;
  name: string;
  builtIn: boolean;
  enabled: boolean;
  problems: string[];
  buttons: PaneButton[];
  /** One of the user's folders: «+ New button» adds to it. */
  canAddButtons: boolean;
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
}

export interface PaneProfileResult {
  profile: unknown;
}
