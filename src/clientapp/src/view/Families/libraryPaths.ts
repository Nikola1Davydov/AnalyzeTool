// Persisted list of on-disk family library folders for the palette's Library mode. Stored in localStorage
// (per-user, per-machine — same layer as the palette settings and rules) so the configured folders survive
// pane/window reopens and Revit restarts.

import { reactive, watch } from "vue";

const STORAGE_KEY = "analysetool.libraryPaths.v1";

function load(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    const arr = raw ? (JSON.parse(raw) as unknown) : [];
    return Array.isArray(arr) ? arr.filter((p): p is string => typeof p === "string") : [];
  } catch {
    return [];
  }
}

const paths = reactive<string[]>(load());
watch(
  paths,
  () => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(paths));
    } catch {
      /* best-effort */
    }
  },
  { deep: true },
);

export function useLibraryPaths() {
  function add(path: string): boolean {
    const p = path.trim();
    if (!p || paths.some((x) => x.toLowerCase() === p.toLowerCase())) return false;
    paths.push(p);
    return true;
  }
  function remove(path: string) {
    const i = paths.indexOf(path);
    if (i >= 0) paths.splice(i, 1);
  }
  return { paths, add, remove };
}
