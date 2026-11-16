import { useQuery } from "@tanstack/react-query";
import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { browseFile } from "../../../api/app";
import {
  type BindResult,
  type BindingOptionsResult,
  type ParameterRef,
  type ProjectParameterDto,
  type ProjectParametersResult,
  type SharedParameterDto,
  type SharedParameterFileData,
  type SharedParameterGroupDto,
  toolCommands,
} from "../../../api/tools";
import { useConfirm } from "../../../components/confirm";
import { errorText, useNotify } from "../../../components/notify";
import { useActiveDocument, useInvoke } from "../../../revit/hooks";

// The shared parameter file being edited, the project's parameters beside it, and the selection the report starts
// from. Lives above the tabs of the main window, so switching to the Report keeps the edits and the selection.
// Edits stay local until Save: the file is shared across projects (often a whole team), so changing it is one
// deliberate step, not a write per keystroke.

/** One parameter, wherever it lives — in the file, in the project, or both (matched by GUID). */
export interface ParameterRow {
  key: string;
  name: string;
  guid?: string;
  file?: SharedParameterDto;
  project?: ProjectParameterDto;
}

const FILE_FILTER = "Shared parameter file (*.txt)|*.txt|All files (*.*)|*.*";

/** A parameter's identity across projects: GUID for shared, name for project parameters. */
export function refKey(ref: ParameterRef): string {
  return ref.guid ? `g:${ref.guid.toLowerCase()}` : `n:${ref.name}`;
}

/** The selection key of a project parameter: `g:<guid>`, or `p:<id>` for a project parameter. */
export function projectKey(parameter: ProjectParameterDto) {
  return parameter.guid ? `g:${parameter.guid.toLowerCase()}` : `p:${parameter.id}`;
}

export const fileKey = (parameter: SharedParameterDto) => `g:${parameter.guid.toLowerCase()}`;

function useSharedParametersState() {
  const invoke = useInvoke();
  const document = useActiveDocument();
  const notify = useNotify();
  const confirm = useConfirm();

  const [file, setFile] = useState<SharedParameterFileData | null>(null);
  const [groups, setGroups] = useState<SharedParameterGroupDto[]>([]);
  const [parameters, setParameters] = useState<SharedParameterDto[]>([]);
  const [dirty, setDirty] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [selection, setSelection] = useState<string[]>([]);

  const projectQuery = useQuery({
    queryKey: [document.data?.key ?? null, toolCommands.GetProjectParameters],
    enabled: !!document.data,
    queryFn: ({ signal }) => invoke<ProjectParametersResult>(toolCommands.GetProjectParameters, null, { signal }),
  });
  const project = useMemo(() => projectQuery.data?.parameters ?? [], [projectQuery.data]);

  const applyFile = useCallback(
    (data: SharedParameterFileData) => {
      setFile(data);
      setGroups(data.groups.map((group) => ({ ...group })));
      setParameters(data.parameters.map((parameter) => ({ ...parameter })));
      setDirty(false);
      if (data.error) notify.error("Could not read the shared parameter file", data.error);
    },
    [notify],
  );

  const load = useCallback(async () => {
    setLoading(true);
    try {
      applyFile(await invoke<SharedParameterFileData>(toolCommands.GetSharedParameterFile, {}));
      void projectQuery.refetch();
    } catch (error) {
      notify.failed(error, "Could not read the shared parameter file");
    } finally {
      setLoading(false);
    }
  }, [applyFile, invoke, notify, projectQuery]);

  // The file once, when the window has a document to ask with
  const loadedOnce = useRef(false);
  useEffect(() => {
    if (document.isSuccess && !loadedOnce.current) {
      loadedOnce.current = true;
      void load();
    }
  }, [document.isSuccess, load]);

  const confirmDiscard = useCallback(
    async () =>
      !dirty ||
      confirm({
        title: "Discard the unsaved changes?",
        message: "The shared parameter file has changes that are not saved.",
        confirmLabel: "Discard",
        danger: true,
      }),
    [confirm, dirty],
  );

  /** Picks a file and makes it Revit's shared parameter file — or creates a new one. */
  async function openFile(create: boolean) {
    if (!(await confirmDiscard())) return;
    try {
      const path = await browseFile({
        title: create ? "New shared parameter file" : "Open shared parameter file",
        filter: FILE_FILTER,
        save: create,
        initial: create ? "SharedParameters.txt" : (file?.path ?? undefined),
      });
      if (!path) return;
      setLoading(true);
      applyFile(await invoke<SharedParameterFileData>(toolCommands.SetSharedParameterFile, { path, create }));
    } catch (error) {
      notify.failed(error);
    } finally {
      setLoading(false);
    }
  }

  async function save(): Promise<boolean> {
    if (!file?.path) return false;
    setSaving(true);
    try {
      applyFile(
        await invoke<SharedParameterFileData>(toolCommands.SaveSharedParameterFile, {
          path: file.path,
          stamp: file.exists ? file.stamp : null,
          groups,
          parameters,
        }),
      );
      notify.success("Shared parameter file saved");
      return true;
    } catch (error) {
      notify.failed(error, "Could not save the file");
      return false;
    } finally {
      setSaving(false);
    }
  }

  function discard() {
    if (file) applyFile(file);
  }

  // ---- Edits (local until Save) ---------------------------------------------------------------------

  function upsertParameter(parameter: SharedParameterDto) {
    setParameters((current) => {
      const index = current.findIndex((item) => item.guid.toLowerCase() === parameter.guid.toLowerCase());
      if (index < 0) return [...current, { ...parameter }];
      const next = [...current];
      next[index] = { ...parameter };
      return next;
    });
    setDirty(true);
  }

  function deleteParameters(guids: string[]) {
    const drop = new Set(guids.map((guid) => guid.toLowerCase()));
    setParameters((current) => current.filter((item) => !drop.has(item.guid.toLowerCase())));
    setSelection((current) => current.filter((key) => !drop.has(key.slice(2))));
    setDirty(true);
  }

  /** Moves parameters to another group — what dragging them onto a group does. */
  function moveParameters(guids: string[], groupId: number) {
    const move = new Set(guids.map((guid) => guid.toLowerCase()));
    setParameters((current) =>
      current.map((item) => (move.has(item.guid.toLowerCase()) && item.groupId !== groupId ? { ...item, groupId } : item)),
    );
    setDirty(true);
  }

  function addGroup(name: string): SharedParameterGroupDto {
    const group = { id: Math.max(0, ...groups.map((item) => item.id)) + 1, name: name.trim() };
    setGroups((current) => [...current, group]);
    setDirty(true);
    return group;
  }

  function renameGroup(id: number, name: string) {
    setGroups((current) => current.map((group) => (group.id === id ? { ...group, name: name.trim() } : group)));
    setDirty(true);
  }

  function deleteGroup(id: number) {
    setGroups((current) => current.filter((group) => group.id !== id));
    setDirty(true);
  }

  // ---- Project --------------------------------------------------------------------------------------

  const bindingOptions = useRef<BindingOptionsResult | null>(null);
  async function loadBindingOptions(): Promise<BindingOptionsResult | null> {
    if (bindingOptions.current) return bindingOptions.current;
    try {
      bindingOptions.current = await invoke<BindingOptionsResult>(toolCommands.GetBindingOptions);
    } catch (error) {
      notify.error("Could not read the categories", errorText(error));
    }
    return bindingOptions.current;
  }

  /** Binding reads the file from disk, so it needs the file Revit uses, saved. Null: good to go. */
  const bindBlocker = !file?.path
    ? "No shared parameter file."
    : !file.isRevitCurrent
      ? "Only parameters of Revit's current shared parameter file can be added."
      : dirty
        ? "Save the file first — Revit adds parameters from the file on disk."
        : null;

  async function bind(guids: string[], categoryIds: number[], isInstance: boolean, groupTypeId: string) {
    const result = await invoke<BindResult>(toolCommands.BindSharedParameters, { guids, categoryIds, isInstance, groupTypeId });
    await projectQuery.refetch();
    return result;
  }

  // ---- Rows and selection ---------------------------------------------------------------------------

  const rows = useMemo<ParameterRow[]>(() => {
    const byGuid = new Map(project.filter((item) => item.guid).map((item) => [item.guid!.toLowerCase(), item]));
    const seen = new Set<string>();
    const result: ParameterRow[] = parameters.map((item) => {
      const guid = item.guid.toLowerCase();
      seen.add(guid);
      return { key: `g:${guid}`, name: item.name, guid, file: item, project: byGuid.get(guid) };
    });
    for (const item of project) {
      const guid = item.guid?.toLowerCase();
      if (guid && seen.has(guid)) continue;
      result.push({ key: projectKey(item), name: item.name, guid, project: item });
    }
    return result.sort((a, b) => a.name.localeCompare(b.name));
  }, [parameters, project]);

  const selectedRows = useMemo(() => {
    const keys = new Set(selection);
    return rows.filter((row) => keys.has(row.key));
  }, [rows, selection]);

  /** The selection as the report takes it — only what the project has bound can be counted. */
  const reportSelection = useMemo<ParameterRef[]>(
    () => selectedRows.filter((row) => row.project?.bound).map((row) => ({ guid: row.project!.guid, name: row.name })),
    [selectedRows],
  );

  return {
    file,
    groups,
    parameters,
    project,
    projectLoading: projectQuery.isFetching,
    projectError: projectQuery.isError ? errorText(projectQuery.error) : null,
    dirty,
    loading,
    saving,
    selection,
    setSelection,
    rows,
    selectedRows,
    reportSelection,
    load,
    openFile,
    save,
    discard,
    confirmDiscard,
    upsertParameter,
    deleteParameters,
    moveParameters,
    addGroup,
    renameGroup,
    deleteGroup,
    loadBindingOptions,
    bindBlocker,
    bind,
  };
}

export type SharedParametersState = ReturnType<typeof useSharedParametersState>;

const SharedParametersContext = createContext<SharedParametersState | null>(null);

export function SharedParametersProvider({ children }: { children: ReactNode }) {
  const state = useSharedParametersState();
  return <SharedParametersContext.Provider value={state}>{children}</SharedParametersContext.Provider>;
}

export function useSharedParameters() {
  const state = useContext(SharedParametersContext);
  if (!state) throw new Error("useSharedParameters outside SharedParametersProvider");
  return state;
}

/** The data types a new parameter can have, as the file spells them. */
export const DATA_TYPES: { value: string; label: string }[] = [
  { value: "TEXT", label: "Text" },
  { value: "MULTILINETEXT", label: "Multiline text" },
  { value: "INTEGER", label: "Integer" },
  { value: "NUMBER", label: "Number" },
  { value: "LENGTH", label: "Length" },
  { value: "AREA", label: "Area" },
  { value: "VOLUME", label: "Volume" },
  { value: "ANGLE", label: "Angle" },
  { value: "SLOPE", label: "Slope" },
  { value: "CURRENCY", label: "Currency" },
  { value: "MASS_DENSITY", label: "Mass density" },
  { value: "YESNO", label: "Yes/No" },
  { value: "URL", label: "URL" },
  { value: "MATERIAL", label: "Material" },
  { value: "IMAGE", label: "Image" },
];

export function dataTypeLabel(value: string) {
  return DATA_TYPES.find((type) => type.value === value)?.label ?? value;
}
