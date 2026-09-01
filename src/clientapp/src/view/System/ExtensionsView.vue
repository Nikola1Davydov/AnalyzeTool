<script setup lang="ts">
/**
 * The extension manager.
 *
 * It used to be two tabs inside "Settings", which put a page you VISIT TO WORK — install, update,
 * create, delete — behind a door labelled with something you configure once. Splitting it out is the
 * whole point of this window: preferences live in Settings, extensions live here.
 *
 * Two tabs, by the question being asked: "what do I have" (Installed) and "what else is there"
 * (Find extensions). Everything that is plumbing rather than an answer — which folders are scanned,
 * where generated scripts land — sits in a collapsed panel at the bottom of the first tab.
 */
import { ref, computed, onMounted, defineAsyncComponent } from "vue";
import ToggleSwitch from "primevue/toggleswitch";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

// Lazily loaded: the drawer carries the full extension scaffold (csproj, plugin.json and index.html
// templates) and is opened rarely, so it gets its own chunk instead of riding in the entry bundle.
const CreateExtensionTemplateDrawer = defineAsyncComponent(
  () => import("@/view/System/CreateExtensionTemplateDrawer.vue"),
);
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
  kind: "dll" | "script" | "js"; // what it is made of, not what it does
  legacyLayout?: boolean;
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
  isAuthoringRoot: boolean; // where generated scripts are saved when no root is named
}

const data = ref<ExtensionsData | null>(null);

// "Incompatible" is the wrong word for an extension that was simply never built — the two states
// need different fixes (build the project vs. ship a build for this Revit year), so they say so.
// A freshly generated C# template hits the first one and used to be flagged as broken.
function buildState(row: ExtensionRow): { label: string; tip: string } {
  const years = row.binaryYears ?? [];
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

// What an extension is MADE OF, as one tag. "C#" alone hid the difference that matters most in the
// dev list: a script is a .cs the host compiles at load (edit, Reload, done), a DLL is a project you
// build yourself — and the two fail in different ways.
function kindTag(row: ExtensionRow): { label: string; severity: string; tip: string; icon: string } {
  switch (row.kind) {
    case "script":
      return {
        label: "Script",
        severity: "info",
        tip: "C# script (.cs) compiled by AnalyseTool at load — edit the file and Reload.",
        icon: "pi pi-code",
      };
    case "dll":
      return {
        label: "DLL",
        severity: "contrast",
        tip: "Compiled project — build it (dotnet build) and Reload.",
        icon: "pi pi-box",
      };
    default:
      return {
        label: "Page",
        severity: "warn",
        tip: "HTML/JS page only, no C#.",
        icon: "pi pi-window-maximize",
      };
  }
}

// Two zones, two sections: installed packages (manager-owned) vs the user's own dev folders.
const managedExtensions = computed(() =>
  (data.value?.extensions ?? []).filter((e) => e.zone === "managed"),
);
const devExtensions = computed(() =>
  (data.value?.extensions ?? []).filter((e) => e.zone !== "managed"),
);
const loading = ref(true);

const paths = ref<PathRow[]>([]);
const pathsBusy = ref(false);
const templateDrawerVisible = ref(false);

function openTemplateDrawer() {
  templateDrawerVisible.value = true;
}

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
  askConsent(
    { kind: "source", source: row.source, expectedId: row.id, name: row.name },
    row.installed,
  );
}

// Uninstall from the catalog card: the same dialog and the same command as the extension
// list. The catalog knows an id, the remove flow wants the installed row — that lookup is the
// whole difference, and duplicating the flow for it would mean two ways to delete one thing.
function removeFromCatalog(row: CatalogRow) {
  const installed = data.value?.extensions.find(
    (e) => e.id.toLowerCase() === row.id.toLowerCase() && e.zone === "managed",
  );
  if (installed) askRemove(installed);
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

// ---- Update feeds: manual check (network), then per-row badge + Update action.
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
  checkingUpdates.value = true;
  try {
    const res = await invoke<{ results: UpdateCheckRow[] }>("CheckExtensionUpdates");
    const map: Record<string, UpdateCheckRow> = {};
    for (const r of res?.results ?? []) map[r.id] = r;
    updateChecks.value = map;
  } catch (e) {
    // The button stops spinning either way — without a message the user cannot tell "no updates"
    // from "the check never ran".
    notifications.error(`Update check failed: ${errorText(e)}`);
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
async function useForScripts(path: string) {
  pathsBusy.value = true;
  try {
    await invoke("SetAuthoringRoot", { path });
    await loadPaths();
  } catch (e) {
    console.error("Failed to set the scripts folder", e);
  } finally {
    pathsBusy.value = false;
  }
}

onMounted(() => {
  load();
  loadCatalog();
  loadPaths();
});
</script>

<template>
  <div class="p-6">
    <div class="flex items-start justify-between gap-4 mb-4 flex-wrap">
      <div>
        <h1 class="text-xl font-bold">Extensions</h1>
        <p class="text-sm text-surface-500">
          Everything that adds commands, buttons and pages to AnalyseTool.
        </p>
      </div>
      <!-- The manager lifecycle in one row, ordered by how often it is used. -->
      <div class="flex flex-wrap gap-2 justify-end">
        <Button
          label="Check updates"
          icon="pi pi-sync"
          severity="secondary"
          :loading="checkingUpdates"
          @click="checkUpdates"
        />
        <Button
          label="Install from file…"
          icon="pi pi-download"
          severity="secondary"
          @click="pickPackageAndAskConsent"
        />
        <Button
          label="New extension"
          icon="pi pi-plus"
          severity="contrast"
          @click="openTemplateDrawer"
        />
        <Button label="Reload" icon="pi pi-refresh" :loading="loading" @click="reload" />
      </div>
    </div>

    <!-- lazy: without it both panels mount and re-render on every refresh, including the catalog
         list sitting on a hidden tab. -->
    <Tabs value="installed" lazy>
      <TabList>
        <Tab value="installed">Installed</Tab>
        <Tab value="catalog">Find extensions</Tab>
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="installed">
          <!-- Installed: packages owned by the Extension Manager (extensions-dist). -->
          <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6 mt-4">
            <h2 class="text-sm font-bold mb-3">
              Installed
              <span class="text-surface-500 font-normal">— packages managed by AnalyseTool</span>
            </h2>
            <div
              v-if="updateError"
              class="mb-3 rounded-lg border border-red-200 bg-red-50 p-2 text-xs text-red-700 flex items-start gap-2"
            >
              <i class="pi pi-exclamation-triangle mt-0.5" />
              <span class="grow whitespace-pre-wrap break-words">{{ updateError }}</span>
              <Button
                icon="pi pi-times"
                size="small"
                text
                severity="danger"
                @click="updateError = ''"
              />
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
                    >
                      <i class="pi pi-box" />
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
              <Column header="Version">
                <template #body="{ data: row }">
                  <span>{{ row.version }}</span>
                  <Tag
                    v-if="updateChecks[row.id]?.updateAvailable"
                    :value="`→ ${updateChecks[row.id]?.latest}`"
                    severity="success"
                    class="ml-2"
                    v-tooltip.top="'Update available'"
                  />
                  <!-- Independent of the update tag: an update that FAILS leaves updateAvailable true,
                       so an v-else-if here would hide the very error the user needs to see. -->
                  <Tag
                    v-if="updateChecks[row.id]?.error"
                    value="error"
                    severity="danger"
                    class="ml-2"
                    v-tooltip.top="updateChecks[row.id]?.error"
                  />
                </template>
              </Column>
              <Column header="Type">
                <template #body="{ data: row }">
                  <Tag
                    :value="kindTag(row).label"
                    :severity="kindTag(row).severity"
                    class="mr-1"
                    v-tooltip.top="kindTag(row).tip"
                  />
                  <Tag v-if="row.hasUi && row.kind !== 'js'" value="UI" severity="warn" class="mr-1" />
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
                  Nothing installed yet — look under <b>Find extensions</b>, or use
                  <b>Install from file…</b>
                </div>
              </template>
            </DataTable>
          </section>

          <!-- Development: the user's own folders (default dev root + added paths). Reload-driven. -->
          <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
            <h2 class="text-sm font-bold mb-3">
              Your own
              <span class="text-surface-500 font-normal">— folders you edit, reloaded live</span>
            </h2>
            <DataTable :value="devExtensions" :loading="loading" dataKey="id" class="text-sm">
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
                      v-tooltip.top="kindTag(row).tip"
                    >
                      <i :class="kindTag(row).icon" />
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
              <Column header="Type">
                <template #body="{ data: row }">
                  <Tag
                    :value="kindTag(row).label"
                    :severity="kindTag(row).severity"
                    class="mr-1"
                    v-tooltip.top="kindTag(row).tip"
                  />
                  <Tag v-if="row.hasUi && row.kind !== 'js'" value="UI" severity="warn" class="mr-1" />
                  <Tag
                    v-if="row.legacyLayout"
                    value="Legacy layout"
                    severity="secondary"
                    class="mr-1"
                    v-tooltip.top="
                      'Old extensions\\<year>\\<id> layout — move the folder directly under the root'
                    "
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
                  None yet — press <b>New extension</b>, or drop a folder into the dev root.
                </div>
              </template>
            </DataTable>
          </section>

          <!-- Folders: plumbing, not an answer. Collapsed by default — most people never open it,
               and the ones who do are looking for exactly this. -->
          <Panel toggleable collapsed class="mb-6">
            <template #header>
              <span class="text-sm font-bold">Folders scanned — for developers</span>
            </template>
            <p class="text-xs text-surface-500 mb-3">
              Every extension found in these folders is loaded for this Revit version. The one tagged
              <span class="font-medium">scripts</span> is where commands generated over MCP are saved
              when no folder is named.
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
                  <Tag v-if="row.isAuthoringRoot" value="scripts" severity="info" class="ml-1" />
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
                      v-tooltip.left="'Save generated scripts here'"
                      @click="useForScripts(row.path)"
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
          </Panel>
        </TabPanel>

        <TabPanel value="catalog">
          <!-- The directory: which repositories to get extensions from. Links first — that half works
               offline and answers "where does this live" — with a one-click install where a
               publisher ships releases. -->
          <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6 mt-4">
            <div class="flex items-start justify-between mb-4 gap-3">
              <div>
                <h2 class="text-sm font-bold">Where extensions come from</h2>
                <p class="text-xs text-surface-500 max-w-2xl">
                  Every entry is a public repository. <b>Install</b> downloads the package from the
                  publisher's own release — AnalyseTool is only the courier and does not host, review
                  or endorse third-party extensions.
                </p>
              </div>
              <div class="flex gap-2 shrink-0">
                <Button
                  label="Install from repository…"
                  icon="pi pi-cloud-download"
                  size="small"
                  severity="secondary"
                  @click="askForSource"
                />
                <Button
                  icon="pi pi-refresh"
                  size="small"
                  text
                  severity="secondary"
                  :loading="catalogLoading"
                  v-tooltip.top="'Reload the catalog'"
                  @click="loadCatalog"
                />
              </div>
            </div>

            <div
              v-if="catalogError"
              class="mb-3 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
            >
              <i class="pi pi-exclamation-triangle mr-1" />{{ catalogError }}
            </div>

            <div class="flex flex-col gap-3">
              <div
                v-for="row in catalog"
                :key="row.id"
                class="border border-surface-200 rounded-lg p-3 flex items-start justify-between gap-4"
              >
                <div class="min-w-0">
                  <div class="flex items-center gap-2 flex-wrap">
                    <span class="font-medium">{{ row.name }}</span>
                    <Tag v-if="row.installed" value="installed" severity="success" />
                    <Tag v-if="row.userSupplied" value="local catalog" severity="secondary" />
                    <Tag v-for="tag in row.tags" :key="tag" :value="tag" severity="secondary" />
                  </div>
                  <div class="text-xs text-surface-500 mt-0.5">
                    <span v-if="row.publisher">{{ row.publisher }}</span>
                    <span v-if="row.license"> · {{ row.license }}</span>
                    <span v-if="row.installedVersion"> · installed {{ row.installedVersion }}</span>
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

                <div class="shrink-0 flex flex-col items-end gap-1">
                  <!-- A dev-zone hit is the author's own working copy of this id: installing the
                       package on top would leave two extensions claiming one id. -->
                  <Button
                    v-if="row.source && row.zone !== 'dev'"
                    :label="row.installed ? 'Reinstall' : 'Install'"
                    :icon="row.installed ? 'pi pi-replay' : 'pi pi-download'"
                    size="small"
                    :severity="row.installed ? 'secondary' : undefined"
                    @click="installFromCatalog(row)"
                  />
                  <span v-else-if="row.zone === 'dev'" class="text-xs text-surface-500">
                    open as a dev copy
                  </span>
                  <span v-else class="text-xs text-surface-500">manual download</span>
                  <!-- Installing and uninstalling belong to the same card: finding an extension
                       here and then hunting for it in another tab to remove it is one place too
                       many for one thing. -->
                  <Button
                    v-if="row.installed && row.zone === 'managed'"
                    label="Uninstall"
                    icon="pi pi-trash"
                    size="small"
                    text
                    severity="danger"
                    @click="removeFromCatalog(row)"
                  />
                </div>
              </div>

              <div v-if="!catalog.length && !catalogLoading" class="text-surface-500 text-sm p-4">
                The catalog is empty. Add entries in the file below, or use
                <b>Install from repository…</b>
              </div>
            </div>

            <p class="text-xs text-surface-500 mt-4">
              Own or company repositories go in
              <span class="font-mono break-all">{{ userCatalogPath }}</span> — same shape as the
              shipped list (<span class="font-mono">id, name, description, source, website</span>);
              an entry with an existing id replaces the shipped one.
            </p>
          </section>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Third-party install consent: the backend requires consent=true, logged host-side (#48).
         Outside the tab panels — the catalog and the extension list both open it, and a lazy
         TabPanel would unmount it under the user's hands. -->
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
        <p
          v-if="removeTarget?.zone === 'dev' && removeTarget?.kind === 'dll'"
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

    <CreateExtensionTemplateDrawer v-model:visible="templateDrawerVisible" @created="reload" />

    <EditExtensionDrawer
      v-model:visible="editDrawerVisible"
      :extensionId="editTargetId"
      @saved="afterEdit"
    />
  </div>
</template>
