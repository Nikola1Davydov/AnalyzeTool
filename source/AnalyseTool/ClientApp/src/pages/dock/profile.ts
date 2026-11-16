import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { bridge } from "revit-bridge";
import { appCommands, type RevitTheme } from "../../api/app";
import { type PaneFolder, type PaneProfileResult, paneCommands } from "../../api/pane";

// The profile of the pane: the size of the tiles, the theme, the dock (favourites), the order of the tiles, the last
// folder. Kept by the add-in in %LOCALAPPDATA%\AnalyseTool\pane-profile.json — the same in every Revit version.

/** «revit» follows the theme of Revit (2024+ has a dark one), «system» the theme of Windows. */
export type Theme = "revit" | "system" | "light" | "dark";
export type TileSize = "small" | "medium" | "large";

export type Profile = {
  version: 1;
  size: TileSize;
  theme: Theme;
  /** Animations and blur; off on computers without a graphics card (virtual machines, remote desktops). */
  effects: boolean;
  /** The favourite buttons in the dock: button keys (`extension/name`), in order. */
  dock: string[];
  /** Buttons loaded on the Revit ribbon although their plugin.json keeps them off («+» of the pane). */
  ribbon: string[];
  /** Folders whose tab is off the Revit ribbon («−» on a folder): they stay in the pane. */
  hiddenFolders: string[];
  /** Buttons not loaded on the Revit ribbon («−» on a button). */
  hiddenButtons: string[];
  /** The order of the tiles per folder: `btn:<key>` and `group:<name>`; `home` holds the order of the folders. */
  order: Record<string, string[]>;
  /** The folder the pane opens with. */
  lastFolder: string | null;
  /** The last values of the form of every button: key → values. */
  inputs: Record<string, Record<string, unknown>>;
  updatedAt: string;
};

export const defaultProfile: Profile = {
  version: 1,
  size: "medium",
  theme: "revit",
  effects: true,
  dock: [],
  ribbon: [],
  hiddenFolders: [],
  hiddenButtons: [],
  order: {},
  lastFolder: null,
  inputs: {},
  updatedAt: new Date(0).toISOString(),
};

/** A profile read from the file or an import, completed with the defaults of what it lacks. */
export function normalize(value: unknown): Profile {
  const source = (value && typeof value === "object" ? value : {}) as Partial<Profile>;
  return {
    ...defaultProfile,
    ...source,
    version: 1,
    size: (["small", "medium", "large"] as const).includes(source.size as TileSize) ? (source.size as TileSize) : defaultProfile.size,
    theme: (["revit", "system", "light", "dark"] as const).includes(source.theme as Theme) ? (source.theme as Theme) : defaultProfile.theme,
    effects: typeof source.effects === "boolean" ? source.effects : defaultProfile.effects,
    dock: Array.isArray(source.dock) ? source.dock.filter((item): item is string => typeof item === "string") : [],
    ribbon: Array.isArray(source.ribbon) ? source.ribbon.filter((item): item is string => typeof item === "string") : [],
    hiddenFolders: Array.isArray(source.hiddenFolders) ? source.hiddenFolders.filter((item): item is string => typeof item === "string") : [],
    hiddenButtons: Array.isArray(source.hiddenButtons) ? source.hiddenButtons.filter((item): item is string => typeof item === "string") : [],
    order: source.order && typeof source.order === "object" ? source.order : {},
    lastFolder: typeof source.lastFolder === "string" ? source.lastFolder : null,
    inputs: source.inputs && typeof source.inputs === "object" ? source.inputs : {},
  };
}

const profileQueryKey = ["paneProfile"] as const;

async function readProfile(): Promise<Profile> {
  const result = await bridge.invoke<PaneProfileResult>(paneCommands.GetPaneProfile, null);
  return normalize(result.profile);
}

/** The profile and a way to change it: `update(profile => ({ ...profile, size: "large" }))`. Saved at once. */
export function useProfile() {
  const client = useQueryClient();
  const query = useQuery({ queryKey: profileQueryKey, queryFn: readProfile, staleTime: Infinity });
  const { mutate: save } = useMutation({ mutationFn: (profile: Profile) => bridge.invoke(paneCommands.SavePaneProfile, { profile }) });

  const update = useCallback(
    (change: (profile: Profile) => Profile) => {
      const next = { ...change(client.getQueryData<Profile>(profileQueryKey) ?? defaultProfile), updatedAt: new Date().toISOString() };
      client.setQueryData(profileQueryKey, next);
      save(next);
    },
    [client, save],
  );

  return { profile: query.data ?? defaultProfile, loaded: query.isSuccess, update };
}

const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/** The folder id of a button key: everything before the first `/` (ids have none). */
export function folderOf(key: string) {
  const slash = key.indexOf("/");
  return slash < 0 ? key : key.slice(0, slash);
}

/**
 * The folder of the pane a button key belongs to: the folder of that id, or the tab whose buttons have ids
 * `<tab>.<button>` (a tab folder `sheets.tab` holds the keys `sheets.renumber/…`).
 */
export function ownerOf<T extends { id: string; parts?: T[] | null; userTab?: T | null }>(folders: T[], key: string): T | undefined {
  const id = folderOf(key).toLowerCase();
  // The folders of «Older buttons» and the user's tab inside AnalyseTool are found as themselves: a button acts on its own folder
  const all = folders.flatMap((folder) => [...(folder.parts ?? []), ...(folder.userTab ? [folder.userTab] : []), folder]);
  return (
    all.find((folder) => folder.id.toLowerCase() === id) ??
    all.find((folder) => folder.id.toLowerCase().endsWith(".tab") && id.startsWith(folder.id.toLowerCase().slice(0, -".tab".length) + "."))
  );
}

/** Finds a button by its key. */
export function findButton(folders: PaneFolder[], key: string) {
  const folder = ownerOf(folders, key);
  const button = folder?.buttons.find((candidate) => same(candidate.key, key));
  return folder && button ? { folder, button } : null;
}

/**
 * The profile without what is gone: buttons removed from a folder leave the dock and the order. A folder that is not in the
 * list is kept as it is — an extension may sit on a network folder that is offline now; the user removes it in the profile.
 */
export function pruneProfile(profile: Profile, folders: PaneFolder[]): Profile {
  const byId = new Map(folders.map((folder) => [folder.id.toLowerCase(), folder]));
  const buttonExists = (key: string) => {
    const folder = ownerOf(folders, key);
    return !folder || folder.buttons.some((button) => same(button.key, key));
  };
  const order: Record<string, string[]> = {};
  for (const [id, keys] of Object.entries(profile.order)) {
    const folder = byId.get(id.toLowerCase());
    if (id === "home" || !folder) {
      order[id] = keys;
      continue;
    }
    const valid = new Set([
      ...folder.buttons.map((button) => `btn:${button.key}`),
      ...folder.buttons.flatMap((button) => (button.group ? [`group:${button.group}`] : [])),
    ]);
    const kept = keys.filter((key) => valid.has(key));
    if (kept.length > 0) order[id] = kept;
  }
  return {
    ...profile,
    dock: profile.dock.filter(buttonExists),
    ribbon: profile.ribbon.filter(buttonExists),
    hiddenButtons: profile.hiddenButtons.filter(buttonExists),
    order,
    lastFolder: profile.lastFolder && (byId.has(profile.lastFolder.toLowerCase()) || folders.length === 0) ? profile.lastFolder : null,
  };
}

/** The dock keys whose folder is not in the list: removed, renamed or offline. */
export function missingFolders(profile: Profile, folders: PaneFolder[]): string[] {
  return [...profile.dock, ...profile.ribbon].filter((key) => !ownerOf(folders, key));
}

/** The profile without the entries of folders not in the list. */
export function withoutMissingFolders(profile: Profile, folders: PaneFolder[]): Profile {
  const ids = new Set(folders.map((folder) => folder.id.toLowerCase()));
  return {
    ...profile,
    dock: profile.dock.filter((key) => ownerOf(folders, key)),
    ribbon: profile.ribbon.filter((key) => ownerOf(folders, key)),
    order: Object.fromEntries(Object.entries(profile.order).filter(([id]) => id === "home" || ids.has(id.toLowerCase()))),
  };
}

/** The colours of the pane: light, dark, or the system's — «revit» is resolved before (useEffectiveTheme). */
export function applyTheme(theme: "system" | "light" | "dark") {
  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme === "system" ? "light dark" : theme;
}

/**
 * The theme to show: the profile's, with «revit» resolved by asking Revit — WebView2 only knows the theme of Windows. Asked
 * again every few seconds, so switching the theme in Revit reaches the open pane and windows.
 */
export function useEffectiveTheme(): "system" | "light" | "dark" {
  const { profile } = useProfile();
  const revit = useQuery({
    queryKey: ["revitTheme"],
    queryFn: ({ signal }) => bridge.invoke<RevitTheme>(appCommands.GetRevitTheme, null, { signal }),
    enabled: profile.theme === "revit",
    refetchInterval: 4000,
    staleTime: 2000,
  });
  if (profile.theme !== "revit") return profile.theme;
  return revit.data?.theme ?? "system";
}

/** The profile as a file to keep or to move to another computer. */
export function exportProfile(profile: Profile, userName: string) {
  const blob = new Blob([JSON.stringify(profile, null, 2)], { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `analysetool-pane-profile-${userName}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(link.href), 1000);
}
