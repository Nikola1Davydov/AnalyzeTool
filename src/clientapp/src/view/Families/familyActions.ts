// Family Control actions: thin wrappers over the built-in commands with toast feedback. Read actions
// (select/isolate) resolve the target instance ids first (GetFamilyInstances, by family or by type) and
// feed them to the existing SelectionInRevit / IsolationInRevit commands. Write actions (rename / delete /
// purge / workset) hit the dedicated write commands and report ok/error.

import { invoke } from "@/RevitBridge";
import { useToast } from "primevue/usetoast";
import type { FamilyInstancesResult, FamilyRow } from "./types";

/** Which instances an action targets: by owning family and/or by specific types. */
export interface InstanceTarget {
  familyId?: number;
  familyIds?: number[];
  typeIds?: number[];
}

export function useFamilyActions() {
  const toast = useToast();

  function ok(summary: string, detail?: string) {
    toast.add({ severity: "success", summary, detail, life: 2500 });
  }
  function fail(summary: string, detail?: string) {
    toast.add({ severity: "error", summary, detail, life: 4000 });
  }

  async function resolveIds(target: InstanceTarget): Promise<number[]> {
    const res = await invoke<FamilyInstancesResult>("GetFamilyInstances", target);
    return (res?.instances ?? []).map((i) => i.id);
  }

  async function select(target: InstanceTarget) {
    try {
      const ids = await resolveIds(target);
      if (!ids.length) return fail("Nothing to select", "No placed instances.");
      await invoke("SelectionInRevit", { elementIds: ids });
      ok(`Selected ${ids.length} instance(s)`);
    } catch (e) {
      fail("Select failed", String((e as Error)?.message ?? e));
    }
  }

  async function isolate(target: InstanceTarget) {
    try {
      const ids = await resolveIds(target);
      if (!ids.length) return fail("Nothing to isolate", "No placed instances.");
      await invoke("IsolationInRevit", { elementIds: ids });
      ok(`Isolated ${ids.length} instance(s)`);
    } catch (e) {
      fail("Isolate failed", String((e as Error)?.message ?? e));
    }
  }

  async function renameFamily(id: number, newName: string): Promise<boolean> {
    try {
      const res = await invoke<{ ok: boolean; name?: string; error?: string }>("RenameFamily", {
        id,
        newName,
      });
      if (res?.ok) {
        ok("Renamed", res.name);
        return true;
      }
      fail("Rename failed", res?.error ?? "Unknown error");
    } catch (e) {
      fail("Rename failed", String((e as Error)?.message ?? e));
    }
    return false;
  }

  async function renameTypeOne(id: number, newName: string): Promise<{ ok: boolean; error?: string }> {
    try {
      const res = await invoke<{ ok: boolean; error?: string }>("RenameFamilyType", { id, newName });
      return { ok: !!res?.ok, error: res?.error };
    } catch (e) {
      return { ok: false, error: String((e as Error)?.message ?? e) };
    }
  }

  /** Renames one or more types (a grouped row can hold several FamilySymbols sharing a name). */
  async function renameTypes(ids: number[], newName: string): Promise<boolean> {
    const results = await Promise.all(ids.map((id) => renameTypeOne(id, newName)));
    const failed = results.filter((r) => !r.ok);
    if (!failed.length) {
      ok("Type renamed", `${ids.length} type(s) → ${newName}`);
      return true;
    }
    fail("Rename failed", failed[0].error ?? "Unknown error");
    return false;
  }

  /** Deletes families and/or types (the Purge path passes the precomputed unused ids). */
  async function deleteElements(familyIds: number[], typeIds: number[] = []): Promise<boolean> {
    try {
      const res = await invoke<{ ok: boolean; deleted: number; error?: string }>(
        "DeleteFamilyElements",
        { familyIds, typeIds },
      );
      if (res?.ok) {
        ok("Deleted", `${res.deleted} element(s) removed.`);
        return true;
      }
      fail("Delete failed", res?.error ?? "Unknown error");
    } catch (e) {
      fail("Delete failed", String((e as Error)?.message ?? e));
    }
    return false;
  }

  /** Purges families with zero instances (caller passes the inventory). */
  async function purgeUnused(families: FamilyRow[]): Promise<boolean> {
    const unused = families.filter((f) => f.instanceCount === 0).map((f) => f.id);
    if (!unused.length) {
      ok("Nothing to purge", "No unused families found.");
      return false;
    }
    return deleteElements(unused);
  }

  /** Moves instances to a workset, targeted by explicit element ids and/or by type ids. */
  async function setWorkset(target: InstanceTarget, worksetId: number): Promise<boolean> {
    try {
      const res = await invoke<{ ok: boolean; updated: number; error?: string }>(
        "SetInstancesWorkset",
        { ...target, worksetId },
      );
      if (res?.ok) {
        ok("Workset updated", `${res.updated} instance(s) moved.`);
        return true;
      }
      fail("Workset change failed", res?.error ?? "Unknown error");
    } catch (e) {
      fail("Workset change failed", String((e as Error)?.message ?? e));
    }
    return false;
  }

  return {
    select,
    isolate,
    renameFamily,
    renameTypes,
    deleteElements,
    purgeUnused,
    setWorkset,
  };
}
