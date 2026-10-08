/**
 * The extension manager's state and actions — one instance per Extensions window, created by
 * ExtensionsView and handed to its parts through provide/inject, so a section, a table cell and a
 * dialog all act on the same lists without threading a dozen props through each other.
 */
import { computed, inject, provide, ref, type InjectionKey } from "vue";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";
import {
  errorText,
  type CatalogRow,
  type ExtensionRow,
  type ExtensionsData,
  type InstallOrigin,
  type PathRow,
  type UpdateCheckRow,
} from "./types";

export function createExtensionManager() {
  const notifications = useNotificationStore();

  const data = ref<ExtensionsData | null>(null);

  // Two zones, two sections: installed packages (manager-owned) vs the user's own dev folders.
  const managedExtensions = computed(() =>
    (data.value?.extensions ?? []).filter((e) => e.zone === "managed"),
  );
  const devExtensions = computed(() =>
    (data.value?.extensions ?? []).filter((e) => e.zone !== "managed"),
  );

  // ---- Finding your own: a session with an agent can leave a dozen folders behind, and by then the
  // list is a wall. Text matches name, id and description — local UI state, nothing asks the host.
  const devSearch = ref("");
  const filteredDevExtensions = computed(() => {
    const q = devSearch.value.trim().toLowerCase();
    return devExtensions.value.filter(
      (e) =>
        !q ||
        (e.name ?? "").toLowerCase().includes(q) ||
        e.id.toLowerCase().includes(q) ||
        (e.description ?? "").toLowerCase().includes(q),
    );
  });
  const loading = ref(true);

  const paths = ref<PathRow[]>([]);
  const pathsBusy = ref(false);

  // ---- Edit: the manifest, in a form. The rows could open the folder and delete it, but not change the
  // one thing people change most — what the button says and where it sits.
  const editDrawerVisible = ref(false);
  const editTargetId = ref<string | null>(null);

  function openEdit(row: ExtensionRow) {
    editTargetId.value = row.id;
    editDrawerVisible.value = true;
  }

  // Save already reloaded on the host; re-list so the row shows the new name. A function, not an
  // inline Promise.all in the template: Vue's template sandbox exposes only a whitelist of globals,
  // and Promise is not on it — the save went through and the handler then threw "reading 'all'".
  async function afterEdit() {
    await Promise.all([load(), loadPaths()]);
  }

  async function load() {
    loading.value = true;
    try {
      data.value = await invoke<ExtensionsData>("GetInstalledExtensions");
      // Update results were computed against the versions we are replacing right now. Keeping them
      // showed "update available → 2.0.0" next to a row that already reads 2.0.0 after a reinstall,
      // with an Update button that would re-download what is installed.
      updateChecks.value = {};
    } catch (e) {
      notifications.error(`Could not load the extension list: ${errorText(e)}`);
    } finally {
      loading.value = false;
    }
  }

  async function reload() {
    loading.value = true;
    try {
      await invoke("ReloadExtensions");
    } catch (e) {
      console.error("Reload failed", e);
    }
    // Refresh tables — after a reload a path can flip valid/invalid (e.g. a new extension was dropped
    // into it) and the extension count changes.
    await Promise.all([load(), loadPaths()]);
  }

  // The backend toggles extensions-state.json and reloads (commands + ribbon), so the
  // full refresh mirrors what just happened on the host side.
  //
  // The switch is updated optimistically and put back on failure. That is not cosmetic: PrimeVue's
  // ToggleSwitch holds its own internal value and only re-reads the prop when the prop CHANGES, so
  // leaving row.enabled untouched after a rejected call left the switch showing a state the host
  // never accepted — silently, since the failure only reached the console.
  async function setExtensionEnabled(row: ExtensionRow, enabled: boolean) {
    const previous = row.enabled;
    row.enabled = enabled;
    loading.value = true;
    try {
      await invoke("SetExtensionEnabled", { id: row.id, enabled });
    } catch (e) {
      row.enabled = previous;
      loading.value = false;
      notifications.error(
        `Could not ${enabled ? "enable" : "disable"} "${row.name || row.id}": ${errorText(e)}`,
      );
      return;
    }
    await load();
  }

  // ---- Install: from a picked zip, or straight from a publisher's release. Both routes go through
  // the same third-party disclaimer — the backend refuses without consent=true, so the dialog is not
  // just decoration.
  const installDialogVisible = ref(false);
  const installBusy = ref(false);
  const installError = ref("");
  const installOrigin = ref<InstallOrigin | null>(null);
  const installOverwrite = ref(false);

  /** What the disclaimer names as the thing about to be installed. */
  const installSubject = computed(() => {
    const o = installOrigin.value;
    if (!o) return "";
    return o.kind === "file" ? o.path : o.name ? `${o.name} — ${o.source}` : o.source;
  });

  function askConsent(origin: InstallOrigin, overwrite = false) {
    installError.value = "";
    installOverwrite.value = overwrite;
    installOrigin.value = origin;
    installDialogVisible.value = true;
  }

  async function pickPackageAndAskConsent() {
    try {
      const res = await invoke<{ path: string | null }>("BrowseForFile", {
        title: "Select an extension package",
        filter: "Extension package (*.zip)|*.zip",
      });
      if (!res?.path) return;
      askConsent({ kind: "file", path: res.path });
    } catch (e) {
      console.error("File picker failed", e);
    }
  }

  async function confirmInstall() {
    const origin = installOrigin.value;
    if (!origin) return;
    installBusy.value = true;
    installError.value = "";
    try {
      const res =
        origin.kind === "file"
          ? await invoke<{ installed?: boolean; alreadyInstalled?: boolean }>(
              "InstallExtensionFromFile",
              { path: origin.path, consent: true, overwrite: installOverwrite.value },
            )
          : await invoke<{ installed?: boolean; alreadyInstalled?: boolean }>(
              "InstallExtensionFromSource",
              {
                source: origin.source,
                expectedId: origin.expectedId ?? null,
                consent: true,
                overwrite: installOverwrite.value,
              },
            );
      // Structured signal from the backend (not error-prose matching): same id already
      // installed — keep the dialog open and arm the explicit replace flow.
      if (res?.alreadyInstalled) {
        installOverwrite.value = true;
        installError.value =
          "This extension is already installed. Install again to REPLACE it with this package.";
        return;
      }
      installDialogVisible.value = false;
      await Promise.all([load(), loadPaths(), loadCatalog()]);
    } catch (e) {
      installError.value = e instanceof Error ? e.message : String(e);
    } finally {
      installBusy.value = false;
    }
  }

  // ---- Catalog: the answer to "where do I get extensions". Names and repository links first —
  // that part works offline and is the whole point for a reader — with a one-click install on top
  // for the entries that publish releases. The list is the one shipped with the plugin plus the
  // user's own catalog.json; installs always download from the publisher, never from us.
  const catalog = ref<CatalogRow[]>([]);
  // What the "Available" block offers: entries not installed and not present as a dev copy. An
  // installed one is already a row above, with its own update and uninstall.
  const availableCatalog = computed(() => catalog.value.filter((row) => !row.installed));
  const userCatalogPath = ref("");
  const catalogLoading = ref(false);
  const catalogError = ref("");

  async function loadCatalog() {
    catalogLoading.value = true;
    try {
      const res = await invoke<{
        entries: CatalogRow[];
        userCatalogPath: string;
        error?: string | null;
      }>("GetExtensionCatalog");
      catalog.value = res?.entries ?? [];
      userCatalogPath.value = res?.userCatalogPath ?? "";
      // A file that failed to parse is a note above a working list, not a toast over an empty
      // page — the entries that did parse are still usable.
      catalogError.value = res?.error ?? "";
    } catch (e) {
      notifications.error(`Could not read the extension catalog: ${errorText(e)}`);
    } finally {
      catalogLoading.value = false;
    }
  }

  function installFromCatalog(row: CatalogRow) {
    if (!row.source) return;
    askConsent({ kind: "source", source: row.source, expectedId: row.id, name: row.name });
  }

  // ---- Install from a pasted repository: the same route, for anything not in the catalog.
  const sourceDialogVisible = ref(false);
  const sourceInput = ref("");

  function askForSource() {
    sourceInput.value = "";
    sourceDialogVisible.value = true;
  }

  function proceedWithSource() {
    const source = sourceInput.value.trim();
    if (!source) return;
    sourceDialogVisible.value = false;
    askConsent({ kind: "source", source });
  }

  // ---- Update feeds: checked once when the window opens (network, so only when an installed package
  // declares a feed), then a per-row status tag + Update action. No button: "is there an update?" is
  // a question the window answers by itself.
  const updateChecks = ref<Record<string, UpdateCheckRow>>({});
  const checkingUpdates = ref(false);
  const updatingId = ref("");

  async function checkUpdates() {
    if (!managedExtensions.value.some((e) => e.updateFeed)) return;
    checkingUpdates.value = true;
    try {
      const res = await invoke<{ results: UpdateCheckRow[] }>("CheckExtensionUpdates");
      const map: Record<string, UpdateCheckRow> = {};
      for (const r of res?.results ?? []) map[r.id] = r;
      updateChecks.value = map;
    } catch (e) {
      // Offline is normal for a check nobody asked for: no toast, the rows simply show no update.
      console.warn("Update check failed", e);
    } finally {
      checkingUpdates.value = false;
    }
  }

  // A failed update must SAY so. The per-row tag alone could never show it: the error is written
  // onto a row whose updateAvailable is still true, and the tag hangs off a v-else-if — so the
  // banner carries the message, in full, where a tooltip would truncate a .NET exception.
  const updateError = ref("");

  async function updateExtension(row: ExtensionRow) {
    updatingId.value = row.id;
    updateError.value = "";
    try {
      await invoke("UpdateExtension", { id: row.id });
      delete updateChecks.value[row.id];
      await load();
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      const prev = updateChecks.value[row.id];
      if (prev) updateChecks.value[row.id] = { ...prev, error: message };
      updateError.value = `${row.name || row.id}: ${message}`;
      console.error("Update failed", e);
    } finally {
      updatingId.value = "";
    }
  }

  // ---- Uninstall (managed zone only; dev folders belong to their author).
  const removeDialogVisible = ref(false);
  const removeBusy = ref(false);
  const removeError = ref("");
  const removeTarget = ref<ExtensionRow | null>(null);

  function askRemove(row: ExtensionRow) {
    removeTarget.value = row;
    removeError.value = "";
    removeDialogVisible.value = true;
  }

  // Two commands, one dialog. The manager owns extensions-dist and refuses dev folders on purpose, so
  // deleting one of your own goes through its own command — but the question being asked of the user is
  // the same one, and a second dialog would only be the first one reworded.
  async function confirmRemove() {
    const target = removeTarget.value;
    if (!target) return;
    removeBusy.value = true;
    removeError.value = "";
    try {
      await invoke(target.zone === "dev" ? "RemoveDevExtension" : "RemoveExtension", { id: target.id });
      removeDialogVisible.value = false;
      await Promise.all([load(), loadPaths(), loadCatalog()]);
    } catch (e) {
      removeError.value = e instanceof Error ? e.message : String(e);
    } finally {
      removeBusy.value = false;
    }
  }

  function openFolder(path: string | undefined) {
    if (!path) return;
    invoke("OpenFolder", { path }).catch((e) => console.error(e));
  }

  // --- Extension source paths ---------------------------------------------------------------
  async function loadPaths() {
    try {
      const res = await invoke<{ paths: PathRow[] }>("GetExtensionPaths");
      paths.value = res?.paths ?? [];
    } catch (e) {
      console.error("Failed to load extension paths", e);
    }
  }

  async function browseFolder(): Promise<string | null> {
    try {
      const res = await invoke<{ path: string | null }>("BrowseForFolder");
      return res?.path ?? null;
    } catch (e) {
      console.error("Folder picker failed", e);
      return null;
    }
  }

  // Adding/removing/creating a root changes what gets scanned, so re-list paths and Reload
  // (re-scans every root + refreshes the ribbon buttons) to apply it live.
  async function afterPathsChanged() {
    await loadPaths();
    await reload();
  }

  async function addPath() {
    const folder = await browseFolder();
    if (!folder) return;
    pathsBusy.value = true;
    try {
      await invoke("AddExtensionPath", { path: folder });
      await afterPathsChanged();
    } catch (e) {
      console.error("Failed to add path", e);
    } finally {
      pathsBusy.value = false;
    }
  }

  async function removePath(path: string) {
    pathsBusy.value = true;
    try {
      await invoke("RemoveExtensionPath", { path });
      await afterPathsChanged();
    } catch (e) {
      console.error("Failed to remove path", e);
    } finally {
      pathsBusy.value = false;
    }
  }

  // Where SaveAsCommand / SaveExtensionUi save when the caller names no folder — which is every call an
  // AI makes over MCP, since "save this as a command" names an id, not a path. Only re-lists: nothing
  // that is already loaded moves, so there is no reason to reload extensions.
  async function useForSavedCommands(path: string) {
    pathsBusy.value = true;
    try {
      await invoke("SetAuthoringRoot", { path });
      await loadPaths();
    } catch (e) {
      console.error("Failed to set the folder for saved commands", e);
    } finally {
      pathsBusy.value = false;
    }
  }

  /** First load: the lists, then — only once there is something installed — the update check. */
  async function init() {
    loadCatalog();
    loadPaths();
    await load();
    checkUpdates();
  }

  return {
    data,
    managedExtensions,
    devExtensions,
    devSearch,
    filteredDevExtensions,
    loading,
    paths,
    pathsBusy,
    editDrawerVisible,
    editTargetId,
    openEdit,
    afterEdit,
    load,
    reload,
    setExtensionEnabled,
    installDialogVisible,
    installBusy,
    installError,
    installOrigin,
    installOverwrite,
    installSubject,
    askConsent,
    pickPackageAndAskConsent,
    confirmInstall,
    catalog,
    availableCatalog,
    userCatalogPath,
    catalogLoading,
    catalogError,
    loadCatalog,
    installFromCatalog,
    sourceDialogVisible,
    sourceInput,
    askForSource,
    proceedWithSource,
    updateChecks,
    checkingUpdates,
    updatingId,
    checkUpdates,
    updateError,
    updateExtension,
    removeDialogVisible,
    removeBusy,
    removeError,
    removeTarget,
    askRemove,
    confirmRemove,
    openFolder,
    loadPaths,
    browseFolder,
    afterPathsChanged,
    addPath,
    removePath,
    useForSavedCommands,
    init,
  };
}

export type ExtensionManager = ReturnType<typeof createExtensionManager>;

const ExtensionManagerKey: InjectionKey<ExtensionManager> = Symbol("ExtensionManager");

/** Called once by ExtensionsView: creates the manager and makes it available to its parts. */
export function provideExtensionManager(): ExtensionManager {
  const manager = createExtensionManager();
  provide(ExtensionManagerKey, manager);
  return manager;
}

/** The manager of the Extensions window this component sits in. */
export function useExtensionManager(): ExtensionManager {
  const manager = inject(ExtensionManagerKey);
  if (!manager) throw new Error("useExtensionManager() outside the Extensions window");
  return manager;
}
