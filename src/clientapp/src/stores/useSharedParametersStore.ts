/**
 * The shared parameter file being edited, the project's parameters beside it, and the selection the
 * report starts from. A store, not view state: the main window keeps both pages alive and the report
 * reads the selection made in the table.
 *
 * Edits are local until Save — the file is shared across projects (often a whole team), so changing
 * it is one deliberate step, not a write per keystroke.
 */
import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";
import {
  errorText,
  type BindResult,
  type ParameterRef,
  type ParameterRow,
  type ProjectParameter,
  type SharedParameter,
  type SharedParameterFileData,
  type SharedParameterGroup,
} from "@/view/SharedParameters/types";

const FILE_FILTER = "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*";

export const useSharedParametersStore = defineStore("sharedParameters", () => {
  const notifications = useNotificationStore();

  const file = ref<SharedParameterFileData | null>(null);
  const groups = ref<SharedParameterGroup[]>([]);
  const parameters = ref<SharedParameter[]>([]);
  const project = ref<ProjectParameter[]>([]);
  const dirty = ref(false);
  const loading = ref(false);
  const saving = ref(false);

  /** Keys of the selected rows (see ParameterRow.key). */
  const selection = ref<string[]>([]);

  const rows = computed<ParameterRow[]>(() => {
    const byGuid = new Map(project.value.filter((p) => p.guid).map((p) => [p.guid!.toLowerCase(), p]));
    const groupName = new Map(groups.value.map((g) => [g.id, g.name]));
    const seen = new Set<string>();

    const result: ParameterRow[] = parameters.value.map((p) => {
      const guid = p.guid.toLowerCase();
      seen.add(guid);
      return {
        key: `g:${guid}`,
        name: p.name,
        guid,
        file: p,
        project: byGuid.get(guid),
        groupName: groupName.get(p.groupId) ?? "",
        dataType: p.dataType,
        description: p.description,
      };
    });
    // The project's parameters the file does not have: shared ones from another file, and project
    // parameters, which no file has. Read-only rows — they can still go into the report.
    for (const p of project.value) {
      const guid = p.guid?.toLowerCase();
      if (guid && seen.has(guid)) continue;
      result.push({
        key: guid ? `g:${guid}` : `p:${p.id}`,
        name: p.name,
        guid,
        project: p,
        groupName: "",
        dataType: p.dataType,
        description: "",
      });
    }
    return result.sort((a, b) => a.name.localeCompare(b.name));
  });

  const selectedRows = computed(() => {
    const keys = new Set(selection.value);
    return rows.value.filter((r) => keys.has(r.key));
  });

  /** The selection as the report takes it — only what the project has bound can be counted. */
  const reportSelection = computed<ParameterRef[]>(() =>
    selectedRows.value
      .filter((r) => r.project?.bound)
      .map((r) => ({ guid: r.project!.guid, name: r.name })),
  );

  function applyFile(data: SharedParameterFileData) {
    file.value = data;
    groups.value = data.groups.map((g) => ({ ...g }));
    parameters.value = data.parameters.map((p) => ({ ...p }));
    dirty.value = false;
    if (data.error) notifications.error(`Could not read the shared parameter file: ${data.error}`);
  }

  async function loadProject() {
    try {
      const res = await invoke<{ parameters: ProjectParameter[] }>("GetProjectParameters");
      project.value = res?.parameters ?? [];
    } catch (e) {
      notifications.error(`Could not read the project's parameters: ${errorText(e)}`);
    }
  }

  async function load() {
    loading.value = true;
    try {
      const [data] = await Promise.all([
        invoke<SharedParameterFileData>("GetSharedParameterFile", {}),
        loadProject(),
      ]);
      applyFile(data);
    } catch (e) {
      notifications.error(`Could not read the shared parameter file: ${errorText(e)}`);
    } finally {
      loading.value = false;
    }
  }

  async function confirmDiscard(): Promise<boolean> {
    return !dirty.value || window.confirm("The file has unsaved changes. Discard them?");
  }

  /** Picks a file and makes it Revit's shared parameter file — or creates a new one. */
  async function openFile(create: boolean) {
    if (!(await confirmDiscard())) return;
    try {
      const picked = await invoke<{ path: string | null }>("BrowseForFile", {
        title: create ? "New shared parameter file" : "Open shared parameter file",
        filter: FILE_FILTER,
        save: create,
        fileName: create ? "SharedParameters.txt" : undefined,
      });
      if (!picked?.path) return;
      loading.value = true;
      applyFile(await invoke<SharedParameterFileData>("SetSharedParameterFile", { path: picked.path, create }));
    } catch (e) {
      notifications.error(errorText(e));
    } finally {
      loading.value = false;
    }
  }

  async function save(): Promise<boolean> {
    if (!file.value?.path) return false;
    saving.value = true;
    try {
      applyFile(
        await invoke<SharedParameterFileData>("SaveSharedParameterFile", {
          path: file.value.path,
          stamp: file.value.exists ? file.value.stamp : null,
          groups: groups.value,
          parameters: parameters.value,
        }),
      );
      notifications.success("Shared parameter file saved.");
      return true;
    } catch (e) {
      notifications.error(errorText(e));
      return false;
    } finally {
      saving.value = false;
    }
  }

  async function discard() {
    if (file.value) applyFile(file.value);
  }

  // ---- Edits (local until Save) ---------------------------------------------------------------

  function upsertParameter(p: SharedParameter) {
    const i = parameters.value.findIndex((x) => x.guid.toLowerCase() === p.guid.toLowerCase());
    if (i >= 0) parameters.value.splice(i, 1, { ...p });
    else parameters.value.push({ ...p });
    dirty.value = true;
  }

  function deleteParameters(guids: string[]) {
    const drop = new Set(guids.map((g) => g.toLowerCase()));
    parameters.value = parameters.value.filter((p) => !drop.has(p.guid.toLowerCase()));
    selection.value = selection.value.filter((k) => !drop.has(k.slice(2)));
    dirty.value = true;
  }

  function addGroup(name: string): SharedParameterGroup {
    const id = Math.max(0, ...groups.value.map((g) => g.id)) + 1;
    const group = { id, name: name.trim() };
    groups.value.push(group);
    dirty.value = true;
    return group;
  }

  function renameGroup(id: number, name: string) {
    const g = groups.value.find((x) => x.id === id);
    if (g) {
      g.name = name.trim();
      dirty.value = true;
    }
  }

  function deleteGroup(id: number) {
    groups.value = groups.value.filter((g) => g.id !== id);
    dirty.value = true;
  }

  // ---- Project --------------------------------------------------------------------------------

  async function bind(guids: string[], categoryIds: number[], isInstance: boolean, groupTypeId: string) {
    const res = await invoke<BindResult>("BindSharedParameters", { guids, categoryIds, isInstance, groupTypeId });
    await loadProject();
    return res;
  }

  return {
    file,
    groups,
    parameters,
    project,
    dirty,
    loading,
    saving,
    selection,
    rows,
    selectedRows,
    reportSelection,
    load,
    loadProject,
    openFile,
    save,
    discard,
    confirmDiscard,
    upsertParameter,
    deleteParameters,
    addGroup,
    renameGroup,
    deleteGroup,
    bind,
  };
});
