/**
 * The report's saved parameter sets — read from and written to a file in the user's AnalyseTool folder
 * (GetParameterSets / SaveParameterSets), so they outlive the browser cache and carry across projects.
 * The host stores the whole list; every change here sends the whole list back.
 */
import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";
import { errorText, type ParameterRef } from "@/view/SharedParameters/types";

export interface ParameterSet {
  id: string;
  name: string;
  parameters: ParameterRef[];
  updated: string;
  /** The Revit project (document CreationGUID) the set belongs to; absent = offered in every project. */
  projectId?: string;
  projectName?: string;
}

/** Where a new set lives: one project, or all of them. */
export interface SetScope {
  projectId?: string;
  projectName?: string;
}

interface SetsResult {
  path: string;
  sets: ParameterSet[];
  error?: string;
}

/** A parameter's identity across projects: GUID for shared, name for project parameters. */
export function refKey(r: ParameterRef): string {
  return r.guid ? `g:${r.guid.toLowerCase()}` : `n:${r.name}`;
}

export const useParameterSetsStore = defineStore("parameterSets", () => {
  const notifications = useNotificationStore();

  const sets = ref<ParameterSet[]>([]);
  const loaded = ref(false);
  const busy = ref(false);
  /** Set when the file could not be read: saving would replace what is there, so it is blocked. */
  const loadError = ref<string | null>(null);

  const sorted = computed(() => [...sets.value].sort((a, b) => a.name.localeCompare(b.name)));

  async function load() {
    try {
      const res = await invoke<SetsResult>("GetParameterSets");
      sets.value = res?.sets ?? [];
      loadError.value = res?.error ?? null;
      if (res?.error) notifications.error(`Could not read the saved parameter sets (${res.path}): ${res.error}`);
    } catch (e) {
      notifications.error(`Could not read the saved parameter sets: ${errorText(e)}`);
    } finally {
      loaded.value = true;
    }
  }

  async function persist(next: ParameterSet[]): Promise<ParameterSet[] | null> {
    if (loadError.value) {
      notifications.error("The saved sets file could not be read, so it is not overwritten. Fix or delete it first.");
      return null;
    }
    busy.value = true;
    try {
      const res = await invoke<SetsResult>("SaveParameterSets", { sets: next });
      sets.value = res?.sets ?? next;
      return sets.value;
    } catch (e) {
      notifications.error(`Could not save the parameter sets: ${errorText(e)}`);
      return null;
    } finally {
      busy.value = false;
    }
  }

  const now = () => new Date().toISOString();

  /** Saves a new set and returns it (with the id the host gave it). */
  async function create(name: string, parameters: ParameterRef[], scope: SetScope = {}): Promise<ParameterSet | null> {
    // The new one is the set whose id the list did not have before.
    const before = new Set(sets.value.map((s) => s.id));
    const saved = await persist([...sets.value, { id: "", name: name.trim(), parameters, updated: now(), ...scope }]);
    return saved?.find((s) => !before.has(s.id)) ?? null;
  }

  async function update(id: string, changes: Partial<Pick<ParameterSet, "name" | "parameters" | "projectId" | "projectName">>) {
    return !!(await persist(sets.value.map((s) => (s.id === id ? { ...s, ...changes, updated: now() } : s))));
  }

  async function remove(id: string) {
    return !!(await persist(sets.value.filter((s) => s.id !== id)));
  }

  /** The sets offered in a project: its own, and the ones for every project. */
  function forProject(projectId: string | undefined) {
    return sorted.value.filter((s) => !s.projectId || s.projectId === projectId);
  }

  /** Names are unique among the sets one project sees — two projects may each have a "Handover". */
  function nameTaken(name: string, projectId: string | undefined, exceptId?: string) {
    const n = name.trim().toLowerCase();
    return forProject(projectId).some((s) => s.id !== exceptId && s.name.trim().toLowerCase() === n);
  }

  return { sets, sorted, loaded, busy, loadError, load, create, update, remove, forProject, nameTaken };
});
