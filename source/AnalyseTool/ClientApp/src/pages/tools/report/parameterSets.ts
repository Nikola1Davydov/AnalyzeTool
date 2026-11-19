import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { type ParameterRef, type ParameterSetDto, type ParameterSetsResult, toolCommands } from "../../../api/tools";
import { useNotify } from "../../../components/notify";
import { useInvoke } from "../../../revit/hooks";

// The report's saved parameter sets — a file in the user's AnalyseTool folder (GetParameterSets / SaveParameterSets),
// so they outlive the window and carry across projects. The host stores the whole list; every change sends it back.

export type SetScope = { projectId?: string; projectName?: string };

const key = ["parameterSets"] as const;

export function useParameterSets() {
  const invoke = useInvoke();
  const notify = useNotify();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const query = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => invoke<ParameterSetsResult>(toolCommands.GetParameterSets, null, { signal }),
  });
  const sets = query.data?.sets ?? [];
  const loadError = query.data?.error ?? null;

  async function persist(next: ParameterSetDto[]): Promise<ParameterSetDto[] | null> {
    if (loadError) {
      notify.error("The saved sets file could not be read, so it is not overwritten", loadError);
      return null;
    }
    setBusy(true);
    try {
      const result = await invoke<ParameterSetsResult>(toolCommands.SaveParameterSets, { sets: next });
      queryClient.setQueryData(key, result);
      return result.sets;
    } catch (error) {
      notify.failed(error, "Could not save the parameter sets");
      return null;
    } finally {
      setBusy(false);
    }
  }

  const now = () => new Date().toISOString();

  return {
    sets,
    busy,
    loadError,
    /** The sets offered in a project: its own, and the ones for every project. */
    forProject: (projectId: string | undefined) =>
      [...sets].filter((set) => !set.projectId || set.projectId === projectId).sort((a, b) => a.name.localeCompare(b.name)),
    nameTaken: (name: string, projectId: string | undefined, exceptId?: string) =>
      sets.some(
        (set) =>
          (!set.projectId || set.projectId === projectId) &&
          set.id !== exceptId &&
          set.name.trim().toLowerCase() === name.trim().toLowerCase(),
      ),
    async create(name: string, parameters: ParameterRef[], scope: SetScope) {
      const before = new Set(sets.map((set) => set.id));
      const saved = await persist([
        ...sets,
        {
          id: "",
          name: name.trim(),
          parameters: parameters.map((parameter) => ({ guid: parameter.guid ?? undefined, name: parameter.name ?? "" })),
          updated: now(),
          ...scope,
        },
      ]);
      return saved?.find((set) => !before.has(set.id)) ?? null;
    },
    async update(id: string, changes: Partial<Pick<ParameterSetDto, "name" | "parameters" | "projectId" | "projectName">>) {
      return !!(await persist(sets.map((set) => (set.id === id ? { ...set, ...changes, updated: now() } : set))));
    },
    async remove(id: string) {
      return !!(await persist(sets.filter((set) => set.id !== id)));
    },
  };
}
