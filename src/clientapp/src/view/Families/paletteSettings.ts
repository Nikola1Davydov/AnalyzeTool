// Persisted view settings for the dockable placement palette: which view (gallery/table), how families
// are grouped and sorted. Stored in localStorage (per-user, per-machine — same layer as the family rules
// and the preview cache) so the user's layout choice survives window/pane reopens and Revit restarts.

import { reactive, watch } from "vue";

export type PaletteView = "gallery" | "table";
export type PaletteGroupBy = "category" | "none";
export type PaletteSortBy = "name" | "typeCount" | "instanceCount";
export type SortDir = "asc" | "desc";

export interface PaletteSettings {
  view: PaletteView;
  groupBy: PaletteGroupBy;
  sortBy: PaletteSortBy;
  sortDir: SortDir;
}

const STORAGE_KEY = "analysetool.palette.v1";

// Defaults per the intended UX: gallery first, grouped by category, sorted by name ascending.
const defaults: PaletteSettings = {
  view: "gallery",
  groupBy: "category",
  sortBy: "name",
  sortDir: "asc",
};

function load(): PaletteSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? { ...defaults, ...(JSON.parse(raw) as Partial<PaletteSettings>) } : { ...defaults };
  } catch {
    return { ...defaults };
  }
}

// Single reactive object shared by any palette instance; persisted on every change.
const settings = reactive<PaletteSettings>(load());
watch(
  settings,
  () => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
    } catch {
      /* best-effort */
    }
  },
  { deep: true },
);

export function usePaletteSettings() {
  return settings;
}
