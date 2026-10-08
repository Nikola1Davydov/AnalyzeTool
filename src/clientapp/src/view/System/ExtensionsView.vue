<script setup lang="ts">
/**
 * The extension manager.
 *
 * It used to be two tabs inside "Settings", which put a page you VISIT TO WORK — install, update,
 * create, delete — behind a door labelled with something you configure once. Splitting it out is the
 * whole point of this window: preferences live in Settings, extensions live here.
 *
 * One page, top to bottom by how often it is needed: what is installed, what is your own, what else
 * is available, and — collapsed — the plumbing (which folders are scanned, where saved commands
 * land). A row says nothing while all is well: its status column only speaks about a problem or an
 * update.
 */
import { ref, computed, onMounted, defineAsyncComponent } from "vue";
import ToggleSwitch from "primevue/toggleswitch";
import Menu from "primevue/menu";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

const EditExtensionDrawer = defineAsyncComponent(
  () => import("@/view/System/EditExtensionDrawer.vue"),
);

const notifications = useNotificationStore();

/** Message from a rejected invoke, ready to show. */
function errorText(e: unknown): string {
  return String((e as Error)?.message ?? e);
}

interface ExtensionRow {
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

interface ExtensionsData {
  hostRevit: string;
  hostSdkVersion: string;
  pluginVersion: string;
  extensionsRoot: string;
  extensions: ExtensionRow[];
}

// Vendor links come from the extension's own plugin.json. Binding one straight into :href would
// let "javascript:…" run in THIS origin, where window.AT reaches every registered command — so a
// UI-only extension could grant itself C# execution. The host strips non-http(s) links too
// (GetInstalledExtensions.SafeLink); this is the render-time half of the same rule.
function safeLink(url?: string | null): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch {
    return null; // not an absolute URL — nothing safe to link to
  }
}

interface PathRow {
  path: string; // root — used for remove
  scanDir: string; // what's actually scanned (extensions live directly under the root)
  isDefault: boolean;
  zone: "managed" | "dev";
  valid: boolean;
  reason: string;
  extensionCount: number;
  isAuthoringRoot: boolean; // where saved commands go when no root is named
}

const data = ref<ExtensionsData | null>(null);

// "Incompatible" is the wrong word for an extension that was simply never built — the two states
// need different fixes (build the project vs. ship a build for this Revit year), so they say so.
// A freshly generated C# template hits the first one and used to be flagged as broken.
function buildState(row: ExtensionRow): { label: string; tip: string } {
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
    tip: `No build for Revit ${data.value?.hostRevit} — this extension ships ${years.join(", ")}.`,
  };
}

// What an extension is made of — no longer a tag of its own (it told a BIM user nothing), only the
// placeholder icon and its tooltip when the extension ships no icon.
function kindInfo(row: ExtensionRow): { tip: string; icon: string } {
  if (row.hostBuilt)
    return {
      tip: "Saved command — AnalyseTool builds it from the sources in its src folder.",
      icon: "pi pi-bolt",
    };
  if (row.kind === "dll")
    return { tip: "Commands from a project you build (dotnet build).", icon: "pi pi-box" };
  return { tip: "A page (HTML/JS) without commands of its own.", icon: "pi pi-window-maximize" };
}

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
type InstallOrigin =
  | { kind: "file"; path: string }
  | { kind: "source"; source: string; expectedId?: string | null; name?: string };

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

// One "Install" button with its two sources, instead of a file button in the header and a repository
// button hidden on another tab.
const installMenu = ref<InstanceType<typeof Menu> | null>(null);
const installMenuItems = [
  { label: "From a file (.zip)…", icon: "pi pi-file", command: () => pickPackageAndAskConsent() },
  { label: "From a repository…", icon: "pi pi-github", command: () => askForSource() },
];
function toggleInstallMenu(event: Event) {
  installMenu.value?.toggle(event);
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
interface CatalogRow {
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
interface UpdateCheckRow {
  id: string;
  installed: string;
  latest: string | null;
  updateAvailable: boolean;
  releaseUrl?: string | null;
  error?: string | null;
}
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

onMounted(async () => {
  loadCatalog();
  loadPaths();
  await load();
  checkUpdates();
});
</script>

<template>
  <div class="p-6">
    <div class="flex items-start justify-between gap-4 mb-4 flex-wrap">
      <div>
        <h1 class="text-xl font-bold">Extensions</h1>
        <p class="text-sm text-surface-500">
          Everything that adds commands, buttons and pages to AnalyseTool. New ones are made with
          <b>New</b> on the ribbon.
        </p>
      </div>
      <div class="flex flex-wrap gap-2 justify-end">
        <Button
          label="Install"
          icon="pi pi-download"
          severity="secondary"
          aria-haspopup="true"
          @click="toggleInstallMenu"
        />
        <Menu ref="installMenu" :model="installMenuItems" popup />
        <Button label="Reload" icon="pi pi-refresh" :loading="loading" @click="reload" />
      </div>
    </div>

    <!-- Installed: packages owned by the Extension Manager (extensions-dist). -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <h2 class="text-sm font-bold mb-3">
        Installed
        <span class="text-surface-500 font-normal">— packages managed by AnalyseTool</span>
        <i
          v-if="checkingUpdates"
          class="pi pi-spin pi-spinner text-xs text-surface-400 ml-2"
          v-tooltip.top="'Checking for updates'"
        />
      </h2>
      <div
        v-if="updateError"
        class="mb-3 rounded-lg border border-red-200 bg-red-50 p-2 text-xs text-red-700 flex items-start gap-2"
      >
        <i class="pi pi-exclamation-triangle mt-0.5" />
        <span class="grow whitespace-pre-wrap break-words">{{ updateError }}</span>
        <Button icon="pi pi-times" size="small" text severity="danger" @click="updateError = ''" />
      </div>
      <DataTable :value="managedExtensions" :loading="loading" dataKey="id" class="text-sm">
        <Column header="Extension">
          <template #body="{ data: row }">
            <div class="flex items-start gap-3">
              <img
                v-if="row.icon"
                :src="row.icon"
                class="w-8 h-8 rounded shrink-0 mt-0.5"
                alt=""
              />
              <div
                v-else
                class="w-8 h-8 rounded shrink-0 mt-0.5 bg-surface-100 flex items-center justify-center text-surface-400"
                v-tooltip.top="kindInfo(row).tip"
              >
                <i :class="kindInfo(row).icon" />
              </div>
              <div>
                <div class="font-semibold" :class="{ 'text-surface-400': !row.enabled }">
                  {{ row.name || row.id }}
                </div>
                <div class="text-surface-500 text-xs">
                  {{ row.id }}<template v-if="row.publisher"> · {{ row.publisher }}</template>
                  <a
                    v-if="safeLink(row.website)"
                    :href="safeLink(row.website)!"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="ml-1"
                    v-tooltip.top="'Website'"
                  >
                    <i class="pi pi-external-link text-xs" />
                  </a>
                  <a
                    v-if="safeLink(row.supportUrl)"
                    :href="safeLink(row.supportUrl)!"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="ml-1"
                    v-tooltip.top="'Support'"
                  >
                    <i class="pi pi-question-circle text-xs" />
                  </a>
                </div>
                <div v-if="row.description" class="text-surface-500 text-xs">
                  {{ row.description }}
                </div>
              </div>
            </div>
          </template>
        </Column>
        <Column field="version" header="Version" />
        <Column header="Status">
          <template #body="{ data: row }">
            <!-- Silent while all is well: a tag here means there is something to do. -->
            <Tag
              v-if="updateChecks[row.id]?.updateAvailable"
              :value="`Update → ${updateChecks[row.id]?.latest}`"
              severity="success"
              class="mr-1"
              v-tooltip.top="'An update is available — the arrow button installs it'"
            />
            <!-- Independent of the update tag: an update that FAILS leaves updateAvailable true,
                 so an v-else-if here would hide the very error the user needs to see. -->
            <Tag
              v-if="updateChecks[row.id]?.error"
              value="Update failed"
              severity="danger"
              class="mr-1"
              v-tooltip.top="updateChecks[row.id]?.error"
            />
            <Tag
              v-if="!row.compatible"
              :value="buildState(row).label"
              severity="danger"
              v-tooltip.top="row.compileError || buildState(row).tip"
            />
            <Tag
              v-else-if="row.compileError"
              value="Error"
              severity="danger"
              v-tooltip.top="row.compileError"
            />
          </template>
        </Column>
        <Column header="Enabled" class="w-20">
          <template #body="{ data: row }">
            <ToggleSwitch
              :modelValue="row.enabled"
              :disabled="loading"
              @update:modelValue="setExtensionEnabled(row, !row.enabled)"
            />
          </template>
        </Column>
        <Column header="" class="w-40">
          <template #body="{ data: row }">
            <Button
              v-if="updateChecks[row.id]?.updateAvailable"
              icon="pi pi-arrow-circle-up"
              size="small"
              text
              severity="success"
              :loading="updatingId === row.id"
              v-tooltip.left="`Update to ${updateChecks[row.id]?.latest}`"
              @click="updateExtension(row)"
            />
            <Button
              icon="pi pi-pencil"
              size="small"
              text
              severity="secondary"
              v-tooltip.left="'View manifest (installed packages are read-only)'"
              @click="openEdit(row)"
            />
            <Button
              icon="pi pi-folder-open"
              size="small"
              text
              severity="secondary"
              v-tooltip.left="'Open in Explorer'"
              @click="openFolder(row.directory)"
            />
            <Button
              icon="pi pi-trash"
              size="small"
              text
              severity="danger"
              v-tooltip.left="'Uninstall'"
              @click="askRemove(row)"
            />
          </template>
        </Column>
        <template #empty>
          <div class="text-surface-500 p-4">
            Nothing installed yet — see <b>Available</b> below, or <b>Install</b> a package.
          </div>
        </template>
      </DataTable>
    </section>

    <!-- Your own: the user's folders (default dev root + added paths). Reload-driven. -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <div class="flex items-center justify-between gap-3 mb-3 flex-wrap">
        <h2 class="text-sm font-bold">
          Your own
          <span class="text-surface-500 font-normal">— folders you edit, reloaded live</span>
          <span v-if="devExtensions.length > 3" class="text-surface-400 font-normal ml-1">
            ({{ filteredDevExtensions.length }}/{{ devExtensions.length }})
          </span>
        </h2>
        <!-- Search, shown once there is enough to lose something in. -->
        <IconField v-if="devExtensions.length > 3">
          <InputIcon class="pi pi-search" />
          <InputText v-model="devSearch" placeholder="Search…" size="small" class="w-48" />
        </IconField>
      </div>
      <DataTable :value="filteredDevExtensions" :loading="loading" dataKey="id" class="text-sm">
        <Column header="Extension">
          <template #body="{ data: row }">
            <div class="flex items-start gap-3">
              <img
                v-if="row.icon"
                :src="row.icon"
                class="w-8 h-8 rounded shrink-0 mt-0.5"
                alt=""
              />
              <div
                v-else
                class="w-8 h-8 rounded shrink-0 mt-0.5 bg-surface-100 flex items-center justify-center text-surface-400"
                v-tooltip.top="kindInfo(row).tip"
              >
                <i :class="kindInfo(row).icon" />
              </div>
              <div>
                <div class="font-semibold" :class="{ 'text-surface-400': !row.enabled }">
                  {{ row.name || row.id }}
                </div>
                <div class="text-surface-500 text-xs">{{ row.id }}</div>
                <div v-if="row.description" class="text-surface-500 text-xs">
                  {{ row.description }}
                </div>
              </div>
            </div>
          </template>
        </Column>
        <Column field="version" header="Version" />
        <Column header="Status">
          <template #body="{ data: row }">
            <!-- Silent while all is well: a tag here means there is something to do. -->
            <Tag
              v-if="updateChecks[row.id]?.updateAvailable"
              :value="`Update → ${updateChecks[row.id]?.latest}`"
              severity="success"
              class="mr-1"
              v-tooltip.top="'An update is available — the arrow button installs it'"
            />
            <!-- Independent of the update tag: an update that FAILS leaves updateAvailable true,
                 so an v-else-if here would hide the very error the user needs to see. -->
            <Tag
              v-if="updateChecks[row.id]?.error"
              value="Update failed"
              severity="danger"
              class="mr-1"
              v-tooltip.top="updateChecks[row.id]?.error"
            />
            <Tag
              v-if="!row.compatible"
              :value="buildState(row).label"
              severity="danger"
              v-tooltip.top="row.compileError || buildState(row).tip"
            />
            <Tag
              v-else-if="row.compileError"
              value="Error"
              severity="danger"
              v-tooltip.top="row.compileError"
            />
          </template>
        </Column>
        <Column header="Enabled" class="w-20">
          <template #body="{ data: row }">
            <ToggleSwitch
              :modelValue="row.enabled"
              :disabled="loading"
              @update:modelValue="setExtensionEnabled(row, !row.enabled)"
            />
          </template>
        </Column>
        <Column header="" class="w-32">
          <template #body="{ data: row }">
            <div class="flex justify-end gap-1">
              <Button
                icon="pi pi-pencil"
                size="small"
                text
                severity="secondary"
                v-tooltip.left="'Edit name, button, description…'"
                @click="openEdit(row)"
              />
              <Button
                icon="pi pi-folder-open"
                size="small"
                text
                severity="secondary"
                v-tooltip.left="'Open in Explorer'"
                @click="openFolder(row.directory)"
              />
              <!-- Deleting your own folder used to mean going to Explorer and doing it by hand, which
                   is fine for one extension and a chore for the ten a session can generate. -->
              <Button
                icon="pi pi-trash"
                size="small"
                text
                severity="danger"
                v-tooltip.left="'Delete folder'"
                @click="askRemove(row)"
              />
            </div>
          </template>
        </Column>
        <template #empty>
          <div class="text-surface-500 p-4">
            <template v-if="devExtensions.length">
              Nothing matches.
              <button type="button" class="underline" @click="devSearch = ''">Clear the search</button>
            </template>
            <template v-else>
              None yet — press <b>New</b> on the ribbon, ask your AI to save a command, or drop a
              folder into the dev root.
            </template>
          </div>
        </template>
      </DataTable>
    </section>

    <!-- Available: catalog entries that are not here yet. Installed ones are rows above, with
         their own update and uninstall — the catalog is only the way in. -->
    <section
      v-if="availableCatalog.length || catalogError"
      class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6"
    >
      <h2 class="text-sm font-bold">Available</h2>
      <p class="text-xs text-surface-500 mb-3 max-w-2xl">
        Every entry is a public repository. <b>Install</b> downloads the package from the publisher's
        own release — AnalyseTool is only the courier and does not host, review or endorse
        third-party extensions.
      </p>
      <div
        v-if="catalogError"
        class="mb-3 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
      >
        <i class="pi pi-exclamation-triangle mr-1" />{{ catalogError }}
      </div>
      <div class="flex flex-col gap-3">
        <div
          v-for="row in availableCatalog"
          :key="row.id"
          class="border border-surface-200 rounded-lg p-3 flex items-start justify-between gap-4"
        >
          <div class="min-w-0">
            <div class="flex items-center gap-2 flex-wrap">
              <span class="font-medium">{{ row.name }}</span>
                  <Tag v-if="row.userSupplied" value="local catalog" severity="secondary" />
              <Tag v-for="tag in row.tags" :key="tag" :value="tag" severity="secondary" />
            </div>
            <div class="text-xs text-surface-500 mt-0.5">
              <span v-if="row.publisher">{{ row.publisher }}</span>
              <span v-if="row.license"> · {{ row.license }}</span>
                </div>
            <p v-if="row.description" class="text-xs text-surface-600 mt-1">
              {{ row.description }}
            </p>
            <!-- The link is the part a person can act on without this window: it is where the
                 code, the README and the releases are. -->
            <a
              v-if="safeLink(row.website)"
              :href="safeLink(row.website)!"
              target="_blank"
              rel="noopener noreferrer"
              class="text-xs font-mono break-all inline-flex items-center gap-1 mt-1"
            >
              <i class="pi pi-external-link text-[0.65rem]" />{{ row.website }}
            </a>
            <div v-else-if="row.source" class="text-xs font-mono text-surface-500 mt-1">
              {{ row.source }}
            </div>
          </div>

          <div class="shrink-0">
            <Button
              v-if="row.source"
              label="Install"
              icon="pi pi-download"
              size="small"
              @click="installFromCatalog(row)"
            />
            <span v-else class="text-xs text-surface-500">manual download</span>
          </div>
        </div>
      </div>
    </section>

    <!-- Folders: plumbing, not an answer. Collapsed by default — most people never open it,
         and the ones who do are looking for exactly this. -->
    <Panel toggleable collapsed class="mb-6">
      <template #header>
        <span class="text-sm font-bold">Folders scanned — for developers</span>
      </template>
      <p class="text-xs text-surface-500 mb-3">
        Every extension found in these folders is loaded for this Revit version. The one tagged
        <span class="font-medium">saved commands</span> is where commands your AI saves over MCP
        go when no folder is named.
      </p>
      <div class="flex justify-end mb-2">
        <Button
          label="Add folder"
          icon="pi pi-folder"
          size="small"
          severity="secondary"
          :loading="pathsBusy"
          @click="addPath"
        />
      </div>
      <DataTable :value="paths" dataKey="path" class="text-sm">
        <Column header="Path">
          <template #body="{ data: row }">
            <div class="break-all">{{ row.scanDir }}</div>
            <div v-if="!row.valid" class="text-xs text-amber-600">{{ row.reason }}</div>
          </template>
        </Column>
        <Column header="Status">
          <template #body="{ data: row }">
            <Tag
              :value="row.valid ? `${row.extensionCount} ext` : 'invalid'"
              :severity="row.valid ? 'success' : 'warn'"
            />
            <Tag v-if="row.isDefault" value="default" severity="secondary" class="ml-1" />
            <Tag v-if="row.isAuthoringRoot" value="saved commands" severity="info" class="ml-1" />
          </template>
        </Column>
        <Column header="" class="w-32">
          <template #body="{ data: row }">
            <div class="flex justify-end gap-1">
              <!-- Managed roots are not offered: the Extension Manager owns extensions-dist, and the
                   next update there would overwrite anything generated into it. -->
              <Button
                v-if="row.zone === 'dev' && !row.isAuthoringRoot"
                icon="pi pi-code"
                size="small"
                text
                severity="secondary"
                :disabled="pathsBusy"
                v-tooltip.left="'Save new commands here'"
                @click="useForSavedCommands(row.path)"
              />
              <Button
                icon="pi pi-folder-open"
                size="small"
                text
                severity="secondary"
                v-tooltip.left="'Open in Explorer'"
                @click="openFolder(row.scanDir)"
              />
              <Button
                v-if="!row.isDefault"
                icon="pi pi-trash"
                size="small"
                text
                severity="danger"
                :disabled="pathsBusy"
                @click="removePath(row.path)"
              />
            </div>
          </template>
        </Column>
        <template #empty>
          <div class="text-surface-500 p-3">No source paths.</div>
        </template>
      </DataTable>
      <p class="text-xs text-surface-500 mt-3">
        Own or company repositories for <b>Available</b> go in
        <span class="font-mono break-all">{{ userCatalogPath }}</span> — same shape as the shipped
        list (<span class="font-mono">id, name, description, source, website</span>); an entry with
        an existing id replaces the shipped one.
      </p>
    </Panel>

    <!-- Third-party install consent: the backend requires consent=true, logged host-side (#48). -->
    <Dialog
      v-model:visible="installDialogVisible"
      modal
      header="Install third-party extension"
      class="w-[34rem]"
      :closable="!installBusy"
      :closeOnEscape="!installBusy"
    >
      <div class="text-sm flex flex-col gap-3">
        <div class="break-all text-surface-500 font-mono text-xs">{{ installSubject }}</div>
        <p>
          This package contains <b>third-party code</b> that will run inside Revit with full access
          to your models and machine. Its <b>publisher is responsible</b> for what it does —
          AnalyseTool does not review, endorse or guarantee third-party extensions. Install only if
          you trust the source.
        </p>
        <p v-if="installOrigin?.kind === 'source'" class="text-xs text-surface-500">
          The package is downloaded from the publisher's own release, not from AnalyseTool.
        </p>
        <p v-if="installError" class="text-red-500">{{ installError }}</p>
      </div>
      <template #footer>
        <Button
          label="Cancel"
          text
          severity="secondary"
          :disabled="installBusy"
          @click="installDialogVisible = false"
        />
        <Button
          :label="installOverwrite ? 'Replace installed version' : 'I trust it — install'"
          :severity="installOverwrite ? 'danger' : undefined"
          :loading="installBusy"
          @click="confirmInstall"
        />
      </template>
    </Dialog>

    <!-- Delete confirmation, for both zones. -->
    <Dialog
      v-model:visible="removeDialogVisible"
      modal
      :header="removeTarget?.zone === 'dev' ? 'Delete extension' : 'Uninstall extension'"
      class="w-[28rem]"
    >
      <div class="text-sm flex flex-col gap-3">
        <p>
          Remove <b>{{ removeTarget?.name || removeTarget?.id }}</b> and delete its folder? This
          cannot be undone.
        </p>
        <!-- The path, for dev folders only. An installed package sits where the manager put it; one of
             your own could be anywhere, including a folder you share with your team. -->
        <p v-if="removeTarget?.zone === 'dev'" class="text-xs text-surface-500 break-all font-mono">
          {{ removeTarget?.directory }}
        </p>
        <!-- Two different losses, said differently. A saved command keeps its sources IN this folder
             (src), so they go with it; a project someone builds keeps them elsewhere. -->
        <p v-if="removeTarget?.zone === 'dev' && removeTarget?.hostBuilt" class="text-red-600">
          This is a saved command — its C# sources are in this folder and are deleted with it.
        </p>
        <p
          v-else-if="removeTarget?.zone === 'dev' && removeTarget?.kind === 'dll'"
          class="text-amber-600"
        >
          This is a compiled extension — its source project is somewhere else, but the built output
          here goes.
        </p>
        <p v-if="removeError" class="text-red-500">{{ removeError }}</p>
      </div>
      <template #footer>
        <Button
          label="Cancel"
          text
          severity="secondary"
          :disabled="removeBusy"
          @click="removeDialogVisible = false"
        />
        <Button
          :label="removeTarget?.zone === 'dev' ? 'Delete' : 'Uninstall'"
          severity="danger"
          :loading="removeBusy"
          @click="confirmRemove"
        />
      </template>
    </Dialog>

    <!-- Install from a repository the user names: anything not in the catalog. -->
    <Dialog
      v-model:visible="sourceDialogVisible"
      modal
      header="Install from a repository"
      class="w-[34rem]"
    >
      <div class="text-sm flex flex-col gap-3">
        <p class="text-surface-600">
          Paste the repository of the extension. What gets installed is the package attached to its
          latest release.
        </p>
        <InputText
          v-model="sourceInput"
          placeholder="https://github.com/owner/repo"
          class="w-full"
          autofocus
          @keyup.enter="proceedWithSource"
        />
        <p class="text-xs text-surface-500">
          Accepted: a GitHub repository URL, <span class="font-mono">owner/repo</span>,
          <span class="font-mono">github:owner/repo</span>, or an https URL returning
          <span class="font-mono">version</span> and
          <span class="font-mono">downloadUrl</span>.
        </p>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="sourceDialogVisible = false" />
        <Button label="Continue" :disabled="!sourceInput.trim()" @click="proceedWithSource" />
      </template>
    </Dialog>

    <EditExtensionDrawer
      v-model:visible="editDrawerVisible"
      :extensionId="editTargetId"
      @saved="afterEdit"
    />
  </div>
</template>
