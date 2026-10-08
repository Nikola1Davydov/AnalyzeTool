// Shapes of what the host's extension commands return, and the small pure helpers the extension
// manager's parts share.

/** Message from a rejected invoke, ready to show. */
export function errorText(e: unknown): string {
  return String((e as Error)?.message ?? e);
}

export interface ExtensionRow {
  id: string;
  name: string;
  version: string;
  description?: string | null;
  publisher?: string | null;
  website?: string | null;
  supportUrl?: string | null;
  updateFeed?: string | null;
  enabled: boolean;
  hasCommands: boolean;
  hasUi: boolean;
  compatible: boolean;
  binaryYears?: string[]; // Revit years this extension actually ships a build for
  zone: "managed" | "dev";
  kind: "dll" | "js"; // what it is made of, not what it does
  hostBuilt?: boolean; // a command saved over MCP: AnalyseTool builds it from src\ in this folder
  compileError?: string | null;
  directory: string;
  icon?: string | null; // data URI served by the backend
}

export interface ExtensionsData {
  hostRevit: string;
  hostSdkVersion: string;
  pluginVersion: string;
  extensionsRoot: string;
  extensions: ExtensionRow[];
}

export interface PathRow {
  path: string; // root — used for remove
  scanDir: string; // what's actually scanned (extensions live directly under the root)
  isDefault: boolean;
  zone: "managed" | "dev";
  valid: boolean;
  reason: string;
  extensionCount: number;
  isAuthoringRoot: boolean; // where saved commands go when no root is named
}

export interface CatalogRow {
  id: string;
  name: string;
  publisher?: string | null;
  description?: string | null;
  source?: string | null;
  website?: string | null;
  license?: string | null;
  tags: string[];
  userSupplied: boolean;
  installed: boolean;
  installedVersion?: string | null;
  zone?: "managed" | "dev" | null;
}

export interface UpdateCheckRow {
  id: string;
  installed: string;
  latest: string | null;
  updateAvailable: boolean;
  releaseUrl?: string | null;
  error?: string | null;
}

export type InstallOrigin =
  | { kind: "file"; path: string }
  | { kind: "source"; source: string; expectedId?: string | null; name?: string };

// Vendor links come from the extension's own plugin.json. Binding one straight into :href would
// let "javascript:…" run in THIS origin, where window.AT reaches every registered command — so a
// UI-only extension could grant itself C# execution. The host strips non-http(s) links too
// (GetInstalledExtensions.SafeLink); this is the render-time half of the same rule.
export function safeLink(url?: string | null): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch {
    return null; // not an absolute URL — nothing safe to link to
  }
}

// "Incompatible" is the wrong word for an extension that was simply never built — the two states
// need different fixes (build the project vs. ship a build for this Revit year), so they say so.
// A freshly generated C# template hits the first one and used to be flagged as broken.
export function buildState(row: ExtensionRow, hostRevit?: string): { label: string; tip: string } {
  const years = row.binaryYears ?? [];
  if (row.hostBuilt)
    return {
      label: "Not built",
      tip: "AnalyseTool builds this from its src\\ folder on Reload — the sources do not compile yet.",
    };
  if (years.length === 0)
    return {
      label: "Not built",
      tip: "No compiled assembly found. Build the project in the extension folder (dotnet build), then Reload.",
    };
  return {
    label: "Incompatible",
    tip: `No build for Revit ${hostRevit} — this extension ships ${years.join(", ")}.`,
  };
}

// What an extension is made of — no longer a tag of its own (it told a BIM user nothing), only the
// placeholder icon and its tooltip when the extension ships no icon.
export function kindInfo(row: ExtensionRow): { tip: string; icon: string } {
  if (row.hostBuilt)
    return {
      tip: "Saved command — AnalyseTool builds it from the sources in its src folder.",
      icon: "pi pi-bolt",
    };
  if (row.kind === "dll")
    return { tip: "Commands from a project you build (dotnet build).", icon: "pi pi-box" };
  return { tip: "A page (HTML/JS) without commands of its own.", icon: "pi pi-window-maximize" };
}
