import { bridge } from "revit-bridge";

// The add-in's own commands: versions, MCP, and the window/Explorer helpers the pages need.

export const appCommands = {
  GetAppInfo: "GetAppInfo",
  SetMcpEnabled: "SetMcpEnabled",
  OpenPath: "OpenPath",
  BrowseFolder: "BrowseFolder",
  BrowseFile: "BrowseFile",
  OpenWindow: "OpenWindow",
  OpenUrl: "OpenUrl",
  GetRevitTheme: "GetRevitTheme",
} as const;

export type McpState = "off" | "listening" | "portBusy";

export interface McpInfo {
  enabled: boolean;
  state: McpState;
  url: string;
  /** The `claude mcp add …` command line; null when there is nothing to connect to. */
  claudeCode: string | null;
  connectionFile: string;
  /** The server name for a client configuration. */
  name?: string | null;
  /** The bearer token while it listens. */
  token?: string | null;
}

export interface RevitTheme {
  theme: "light" | "dark";
}

export interface AppInfo {
  version: string;
  revitVersion: string;
  /** CHANGELOG.md. */
  changelog: string;
  profileFolder: string;
  mcp: McpInfo;
}

export interface SetMcpEnabledRequest {
  enabled: boolean;
}

export interface BrowseFolderRequest {
  title?: string;
  initial?: string;
}

export interface BrowseFileRequest {
  title?: string;
  /** e.g. "Shared parameters|*.txt". */
  filter?: string;
  save?: boolean;
  initial?: string;
}

export interface BrowseResult {
  path: string | null;
}

/** Opens a folder in Explorer, or a file with its program. */
export function openPath(path: string) {
  return bridge.invoke<null>(appCommands.OpenPath, { path });
}

/** Opens another AnalyseTool window on a route, e.g. `/extensions`. */
export function openWindow(route: string) {
  return bridge.invoke<null>(appCommands.OpenWindow, { route });
}

/** The folder picker; `null` when the user cancelled. */
export async function browseFolder(request: BrowseFolderRequest = {}) {
  return (await bridge.invoke<BrowseResult>(appCommands.BrowseFolder, request)).path;
}

/** The open/save file dialog; `null` when the user cancelled. */
export async function browseFile(request: BrowseFileRequest = {}) {
  return (await bridge.invoke<BrowseResult>(appCommands.BrowseFile, request)).path;
}

/** A path inside a folder, with the folder's separator. */
export function joinPath(folder: string, relative: string) {
  const separator = folder.includes("\\") ? "\\" : "/";
  const tail = relative.replace(/[\\/]/g, separator);
  return folder.endsWith(separator) ? folder + tail : folder + separator + tail;
}
