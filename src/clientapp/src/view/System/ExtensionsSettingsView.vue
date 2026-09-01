<script setup lang="ts">
import { ref, computed, onMounted, defineAsyncComponent } from "vue";
import { storeToRefs } from "pinia";
import ToggleSwitch from "primevue/toggleswitch";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
import { invoke } from "@/RevitBridge";
import { useUpdateStore } from "@/stores/useUpdateStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import AiModelPicker from "@/components/AiModelPicker.vue";

// Lazily loaded: the drawer carries the full extension scaffold (csproj, plugin.json and index.html
// templates) and is opened rarely, so it gets its own chunk instead of riding in the entry bundle.
const CreateExtensionTemplateDrawer = defineAsyncComponent(
  () => import("@/view/System/CreateExtensionTemplateDrawer.vue"),
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

interface CommandRow {
  name: string;
  source: string; // "core" for built-ins, else the extension id
  description: string | null;
  readOnly: boolean;
  destructive: boolean;
  exposedToMcp: boolean;
  inputSchema: any;
}

interface McpStatus {
  running: boolean;
  enabled: boolean;
  port: number;
  configuredPort: number;
  wsUrl: string;
  serverExePath: string;
  serverExeExists: boolean;
  token: string;
  lastError: string | null;
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

// Two zones, two sections: installed packages (manager-owned) vs the user's own dev folders.
const managedExtensions = computed(() =>
  (data.value?.extensions ?? []).filter((e) => e.zone === "managed"),
);
const devExtensions = computed(() =>
  (data.value?.extensions ?? []).filter((e) => e.zone !== "managed"),
);
const loading = ref(true);

// --- changelog (CHANGELOG.md ships next to the plugin DLL; rendered as markdown on demand) ---------
const changelogVisible = ref(false);
const changelogHtml = ref<string | null>(null);
const changelogError = ref<string | null>(null);
async function openChangelog() {
  changelogVisible.value = true;
  if (changelogHtml.value) return; // fetched once per window
  // Clear the previous failure: the template checks the error branch first, so a single failed
  // fetch used to keep showing its message for the rest of the window's life — even after a
  // later open succeeded and the content was sitting right there, unrendered.
  changelogError.value = null;
  try {
    const res = await invoke<{ markdown: string | null; error: string | null }>("GetChangelog");
    if (res?.markdown) {
      const { marked } = await import("marked"); // lazy — only when the dialog is opened
      changelogHtml.value = await marked.parse(res.markdown);
    } else {
      changelogError.value = res?.error ?? "Changelog not available.";
    }
  } catch (e) {
    changelogError.value = String((e as Error)?.message ?? e);
  }
}

// Same update-check the main AnalyseTool window uses (CheckUpdate command), surfaced next to the version.
const { updateInfo } = storeToRefs(useUpdateStore());

const paths = ref<PathRow[]>([]);
const pathsBusy = ref(false);

const commands = ref<CommandRow[]>([]);
const commandSearch = ref("");

const filteredCommands = computed(() => {
  const q = commandSearch.value.trim().toLowerCase();
  if (!q) return commands.value;
  return commands.value.filter(
    (c) =>
      c.name.toLowerCase().includes(q) ||
      (c.source ?? "").toLowerCase().includes(q) ||
      (c.description ?? "").toLowerCase().includes(q),
  );
});

// Summarize a command's JSON-schema payload as "field: type, …" for the table.
function payloadSummary(schema: any): string {
  if (!schema || typeof schema !== "object") return "—";
  const props = schema.properties;
  if (props && typeof props === "object") {
    const keys = Object.keys(props);
    if (keys.length)
      return keys.map((k) => (props[k]?.type ? `${k}: ${props[k].type}` : k)).join(", ");
  }
  if (schema.additionalProperties) return "(free-form object)";
  return "—";
}

const mcp = ref<McpStatus | null>(null);
const mcpBusy = ref(false);
const port = ref("17890");
const templateDrawerVisible = ref(false);

const codeExec = ref(false);
const codeExecBusy = ref(false);

function openTemplateDrawer() {
  templateDrawerVisible.value = true;
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
  // into it), the extension count changes, and extension commands appear/disappear.
  await Promise.all([load(), loadPaths(), loadCommands()]);
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
  await Promise.all([load(), loadCommands()]);
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
    await Promise.all([load(), loadPaths(), loadCommands(), loadCatalog()]);
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

async function loadCatalog() {
  catalogLoading.value = true;
  try {
    const res = await invoke<{ entries: CatalogRow[]; userCatalogPath: string }>(
      "GetExtensionCatalog",
    );
    catalog.value = res?.entries ?? [];
    userCatalogPath.value = res?.userCatalogPath ?? "";
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
    await Promise.all([load(), loadCommands()]);
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
    await Promise.all([load(), loadPaths(), loadCommands()]);
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

async function loadCommands() {
  try {
    const res = await invoke<{ commands: CommandRow[] }>("GetCommands");
    commands.value = res?.commands ?? [];
  } catch (e) {
    console.error("Failed to load commands", e);
  }
}

async function loadCodeExec() {
  try {
    const res = await invoke<{ enabled: boolean }>("GetCodeExecutionStatus");
    codeExec.value = !!res?.enabled;
  } catch (e) {
    console.error("Failed to load code-execution status", e);
  }
}

async function setCodeExec(enabled: boolean) {
  const previous = codeExec.value;
  codeExec.value = enabled;
  codeExecBusy.value = true;
  try {
    const res = await invoke<{ enabled: boolean }>("SetCodeExecution", { enabled });
    codeExec.value = !!res?.enabled;
  } catch (e) {
    // Of every toggle in this window, this is the one that must never lie: it gates arbitrary C#
    // execution inside Revit. Restore the value we know the host still holds and say so out loud.
    codeExec.value = previous;
    notifications.error(`Could not change the C# code-execution setting: ${errorText(e)}`);
  } finally {
    codeExecBusy.value = false;
  }
}

async function loadMcp() {
  try {
    const status = await invoke<McpStatus>("GetMcpStatus");
    mcp.value = status;
    port.value = String(status.configuredPort);
  } catch (e) {
    console.error("Failed to load MCP status", e);
  }
}

async function applyMcp(enabled: boolean) {
  mcpBusy.value = true;
  try {
    const status = await invoke<McpStatus>("SetMcpServer", {
      enabled,
      port: Number(port.value) || undefined,
    });
    mcp.value = status;
    port.value = String(status.configuredPort);
  } catch (e) {
    console.error("Failed to update MCP server", e);
  } finally {
    mcpBusy.value = false;
  }
}

const clientConfig = computed(() => {
  if (!mcp.value) return "";
  return JSON.stringify(
    {
      mcpServers: {
        "analysetool-revit": {
          command: mcp.value.serverExePath,
          // --token is required: Revit rejects bridge calls that don't carry it, so a config copied
          // before this version has to be replaced with this one.
          args: ["--port", String(mcp.value.port), "--token", mcp.value.token],
        },
      },
    },
    null,
    2,
  );
});

const copied = ref(false);
async function copyConfig() {
  try {
    await navigator.clipboard.writeText(clientConfig.value);
    copied.value = true;
    setTimeout(() => (copied.value = false), 1500);
  } catch (e) {
    console.error("Clipboard write failed", e);
  }
}

// ---- Host ribbon buttons (System tab): hide/show the three main buttons. UI-only —
// the commands behind them stay registered (MCP, AT.invoke, dock).
interface HostButtonRow {
  key: string;
  name: string;
  visible: boolean;
}
const hostButtons = ref<HostButtonRow[]>([]);
const hostButtonBusy = ref("");

async function loadHostButtons() {
  try {
    const res = await invoke<{ buttons: HostButtonRow[] }>("GetHostButtons");
    hostButtons.value = res?.buttons ?? [];
  } catch (e) {
    console.error("Failed to load host buttons", e);
  }
}

async function setHostButtonVisible(row: HostButtonRow, visible: boolean) {
  const previous = row.visible;
  row.visible = visible; // optimistic, restored below — see setExtensionEnabled for why
  hostButtonBusy.value = row.key;
  try {
    await invoke("SetHostButtonVisible", { key: row.key, visible });
  } catch (e) {
    row.visible = previous;
    hostButtonBusy.value = "";
    notifications.error(`Could not ${visible ? "show" : "hide"} "${row.name}": ${errorText(e)}`);
    return;
  }
  hostButtonBusy.value = "";
  await loadHostButtons();
}

onMounted(() => {
  load();
  loadCatalog();
  loadPaths();
  loadMcp();
  loadCodeExec();
  loadCommands();
  loadHostButtons();
  // No loadUpdateData() here: App.vue already runs it for every window, and the store has no
  // in-flight guard — calling it again just spent a second GitHub API request on a result we hold.
});
</script>

<template>
  <div class="p-6">
    <h1 class="text-xl font-bold mb-4">Settings</h1>

    <!-- lazy: without it every panel mounts and re-renders on every refresh, including the full
         commands table sitting on a hidden tab. -->
    <Tabs value="extensions" lazy>
      <TabList>
        <Tab value="extensions">Extensions</Tab>
        <Tab value="catalog">Catalog</Tab>
        <Tab value="commands">Commands</Tab>
        <Tab value="system">System</Tab>
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="extensions">
          <!-- Extension actions: the whole manager lifecycle in one row. -->
          <div class="flex flex-wrap gap-2 justify-end mb-4">
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
              label="New template"
              icon="pi pi-plus"
              severity="contrast"
              @click="openTemplateDrawer"
            />
            <Button label="Reload" icon="pi pi-refresh" :loading="loading" @click="reload" />
          </div>

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
        <p v-if="removeTarget?.zone === 'dev' && removeTarget?.kind === 'dll'" class="text-amber-600">
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

    <!-- Extension paths: the source roots scanned for the running Revit version (default + user-added). -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <div class="flex items-center justify-between mb-3 gap-3">
        <div>
          <h2 class="text-sm font-bold">Extension paths</h2>
          <p class="text-xs text-surface-500">
            The folder tagged <span class="font-medium">scripts</span> is where commands generated over
            MCP are saved when no folder is named.
          </p>
        </div>
        <div class="flex gap-2 shrink-0">
          <Button
            label="Add path"
            icon="pi pi-folder"
            size="small"
            severity="secondary"
            :loading="pathsBusy"
            @click="addPath"
          />
        </div>
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
    </section>

    <!-- Installed: packages owned by the Extension Manager (extensions-dist). -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
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
        <Button icon="pi pi-times" size="small" text severity="danger" @click="updateError = ''" />
      </div>
      <DataTable :value="managedExtensions" :loading="loading" dataKey="id" class="text-sm">
        <Column header="Extension">
          <template #body="{ data: row }">
            <div class="flex items-start gap-3">
              <img v-if="row.icon" :src="row.icon" class="w-8 h-8 rounded shrink-0 mt-0.5" alt="" />
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
            <Tag v-if="row.hasCommands" value="C#" severity="info" class="mr-1" />
            <Tag v-if="row.hasUi" value="UI" severity="warn" class="mr-1" />
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
            No installed packages yet — use "Install from file…" to add one.
          </div>
        </template>
      </DataTable>
    </section>

    <!-- Development: the user's own folders (default dev root + added paths). Reload-driven. -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <h2 class="text-sm font-bold mb-3">
        Development
        <span class="text-surface-500 font-normal">— your own extension folders, reloaded live</span>
      </h2>
      <DataTable :value="devExtensions" :loading="loading" dataKey="id" class="text-sm">
        <Column header="Extension">
          <template #body="{ data: row }">
            <div class="flex items-start gap-3">
              <img v-if="row.icon" :src="row.icon" class="w-8 h-8 rounded shrink-0 mt-0.5" alt="" />
              <div
                v-else
                class="w-8 h-8 rounded shrink-0 mt-0.5 bg-surface-100 flex items-center justify-center text-surface-400"
              >
                <i class="pi pi-wrench" />
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
            <Tag v-if="row.hasCommands" value="C#" severity="info" class="mr-1" />
            <Tag v-if="row.hasUi" value="UI" severity="warn" class="mr-1" />
            <Tag
              v-if="row.legacyLayout"
              value="Legacy layout"
              severity="secondary"
              class="mr-1"
              v-tooltip.top="'Old extensions\\<year>\\<id> layout — move the folder directly under the root'"
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
        <Column header="" class="w-24">
          <template #body="{ data: row }">
            <div class="flex justify-end gap-1">
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
            No dev extensions — create one with "New template" or drop a folder into the dev root.
          </div>
        </template>
      </DataTable>
    </section>

        </TabPanel>

        <TabPanel value="catalog">
          <!-- The directory: which repositories to get extensions from. Links first — that half works
               offline and answers "where does this live" — with a one-click install where a
               publisher ships releases. -->
          <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6 mt-6">
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

        <TabPanel value="commands">
    <!-- Commands: everything callable from a web extension via AT.invoke(name, payload). -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6 mt-6">
      <div class="flex items-start justify-between mb-3 gap-3">
        <div>
          <h2 class="text-sm font-bold">Commands</h2>
          <p class="text-xs text-surface-500">
            Callable from a web extension via <code>AT.invoke(name, payload)</code>.
          </p>
        </div>
        <InputText v-model="commandSearch" placeholder="Search…" class="w-56 shrink-0" />
      </div>
      <!-- Own tab now — take the remaining viewport height instead of a fixed cap; the column
           header stays sticky while the list scrolls. -->
      <DataTable
        :value="filteredCommands"
        dataKey="name"
        scrollable
        scrollHeight="calc(100vh - 21rem)"
        class="text-sm"
      >
        <Column header="Command">
          <template #body="{ data: row }">
            <div class="font-mono">{{ row.name }}</div>
            <div class="text-xs text-surface-500">
              {{ row.source === "core" ? "built-in" : row.source }}
            </div>
          </template>
        </Column>
        <Column header="Description">
          <template #body="{ data: row }">
            <div>{{ row.description || "—" }}</div>
          </template>
        </Column>
        <Column header="Payload">
          <template #body="{ data: row }">
            <span class="font-mono text-xs break-all">{{ payloadSummary(row.inputSchema) }}</span>
          </template>
        </Column>
        <Column header="" class="whitespace-nowrap">
          <template #body="{ data: row }">
            <Tag v-if="row.readOnly" value="read-only" severity="info" class="mr-1" />
            <Tag v-if="row.destructive" value="destructive" severity="danger" class="mr-1" />
            <Tag v-if="row.exposedToMcp" value="MCP" severity="success" />
          </template>
        </Column>
        <template #empty>
          <div class="text-surface-500 p-3">No commands match.</div>
        </template>
      </DataTable>
    </section>

        </TabPanel>

        <TabPanel value="system">
    <!-- Environment / About: what the host currently provides, so authors know what to build against. -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <h2 class="text-sm font-bold mb-3">Environment</h2>
      <div class="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm">
        <div>
          <div class="text-surface-500 text-xs">Revit</div>
          <div>{{ data?.hostRevit ?? "—" }}</div>
        </div>
        <div>
          <div class="text-surface-500 text-xs">SDK version</div>
          <div>{{ data?.hostSdkVersion ?? "—" }}</div>
        </div>
        <div>
          <div class="text-surface-500 text-xs">Plugin version</div>
          <div class="flex items-center gap-2 flex-wrap">
            <span>{{ data?.pluginVersion ?? "—" }}</span>
            <button
              type="button"
              class="text-primary-600 underline text-xs"
              v-tooltip.bottom="'Changelog'"
              @click="openChangelog"
            >
              What's new
            </button>
            <template v-if="updateInfo?.isUpdateAvailable">
              <span
                class="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full text-white"
                :style="{ background: 'var(--p-primary-color)' }"
              >
                <i class="pi pi-arrow-up text-[10px]" />
                v{{ updateInfo.latestVersion }}
              </span>
              <a
                v-if="updateInfo.releaseUrl"
                :href="updateInfo.releaseUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="text-primary-600 underline font-semibold text-xs"
              >
                Download
              </a>
            </template>
          </div>
        </div>
        <div class="col-span-2 md:col-span-1">
          <div class="text-surface-500 text-xs">Extensions folder</div>
          <div class="break-all">{{ data?.extensionsRoot ?? "—" }}</div>
        </div>
      </div>
    </section>

    <!-- Host ribbon buttons: hide unused main buttons. UI-only; commands stay callable. -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <h2 class="text-sm font-bold mb-1">Ribbon buttons</h2>
      <p class="text-xs text-surface-500 mb-3">
        Hide the main buttons you don't use. This is UI-only — their commands stay available to
        extensions, the dock and AI over MCP. Settings and Reload always stay visible.
      </p>
      <div class="flex flex-col gap-2 max-w-md">
        <div
          v-for="b in hostButtons"
          :key="b.key"
          class="flex items-center justify-between text-sm"
        >
          <span>{{ b.name }}</span>
          <ToggleSwitch
            :modelValue="b.visible"
            :disabled="hostButtonBusy === b.key"
            @update:modelValue="setHostButtonVisible(b, !b.visible)"
          />
        </div>
        <div v-if="!hostButtons.length" class="text-surface-500 text-xs">Not available.</div>
      </div>
    </section>

    <!-- AI model: the global model every window uses, saved cloud models, and Ollama status. -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <h2 class="text-sm font-bold mb-3">AI model</h2>
      <p class="text-xs text-surface-500 mb-3">
        Shared across all AnalyseTool windows. Changing it here applies everywhere.
      </p>
      <AiModelPicker manage />
    </section>

    <!-- C# code execution: gates the ad-hoc ExecuteRevitCode command (AI scratchpad). -->
    <section class="mt-8 border-t border-surface-200 pt-6">
      <div class="flex items-center gap-3 mb-1">
        <h2 class="text-lg font-bold">C# code execution</h2>
        <Tag
          :value="codeExec ? 'enabled' : 'disabled'"
          :severity="codeExec ? 'success' : 'secondary'"
        />
      </div>
      <p class="text-sm text-surface-500 mb-3">
        Lets the AI compile and run arbitrary C# inside Revit (the
        <code>ExecuteRevitCode</code> tool). The code runs in-process with full Revit API access — only
        enable this if you trust the AI client. Off by default; hidden from the MCP tool list while off.
      </p>
      <div class="flex items-center gap-2">
        <Checkbox
          :modelValue="codeExec"
          :binary="true"
          :disabled="codeExecBusy"
          inputId="codeExecToggle"
          @update:modelValue="setCodeExec($event)"
        />
        <label for="codeExecToggle" class="text-sm select-none cursor-pointer">
          Allow the AI to run C# code
        </label>
      </div>
    </section>

    <!-- MCP server: exposes every command (built-in + extensions) to AI clients over MCP. -->
    <section class="mt-8 border-t border-surface-200 pt-6">
      <div class="flex items-center gap-3 mb-1">
        <h2 class="text-lg font-bold">MCP server</h2>
        <Tag
          v-if="mcp"
          :value="mcp.running ? `running · port ${mcp.port}` : 'stopped'"
          :severity="mcp.running ? 'success' : 'secondary'"
        />
      </div>
      <p class="text-sm text-surface-500 mb-4">
        Lets an AI client (e.g. Claude Desktop) call every AnalyseTool command — built-in and from
        your extensions — over the Model Context Protocol.
      </p>

      <div class="flex items-end gap-3 mb-4">
        <div>
          <label class="block text-xs text-surface-500 mb-1">Port</label>
          <InputText v-model="port" :disabled="mcp?.running || mcpBusy" class="w-32" />
        </div>
        <Button
          v-if="!mcp?.running"
          label="Start"
          icon="pi pi-play"
          :loading="mcpBusy"
          @click="applyMcp(true)"
        />
        <Button
          v-else
          label="Stop"
          icon="pi pi-stop"
          severity="secondary"
          :loading="mcpBusy"
          @click="applyMcp(false)"
        />
      </div>

      <div v-if="mcp && !mcp.serverExeExists" class="text-sm text-amber-600 mb-3">
        Server executable not found at <span class="break-all">{{ mcp.serverExePath }}</span> —
        rebuild the plugin so the MCP server ships alongside it.
      </div>
      <div v-if="mcp?.lastError" class="text-sm text-red-600 mb-3">
        Last error: {{ mcp.lastError }}
      </div>

      <div v-if="mcp">
        <div class="flex items-center justify-between mb-1">
          <span class="text-sm font-semibold">Claude Desktop config</span>
          <Button
            :label="copied ? 'Copied' : 'Copy'"
            :icon="copied ? 'pi pi-check' : 'pi pi-copy'"
            size="small"
            text
            @click="copyConfig"
          />
        </div>
        <pre
          class="bg-surface-100 text-surface-700 text-xs rounded p-3 overflow-auto whitespace-pre-wrap break-all"
          >{{ clientConfig }}</pre
        >
        <p class="text-xs text-surface-500 mt-1">
          The <code>--token</code> argument authorizes this client against Revit — without it every
          call is refused. If you configured the MCP server before this version, replace your old
          config with the snippet above. Keep it local, like any other machine credential.
        </p>
      </div>
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

    <CreateExtensionTemplateDrawer
      v-model:visible="templateDrawerVisible"
      :extensionsRoot="data?.extensionsRoot"
      @created="reload"
    />

    <!-- Changelog (CHANGELOG.md shipped with the plugin, rendered as markdown) -->
    <Dialog
      v-model:visible="changelogVisible"
      modal
      dismissableMask
      header="What's new"
      :style="{ width: 'min(44rem, 95vw)' }"
    >
      <div v-if="changelogError" class="text-sm text-red-600">{{ changelogError }}</div>
      <div v-else-if="!changelogHtml" class="text-surface-500 text-sm p-4 text-center">
        <i class="pi pi-spin pi-spinner mr-2" />Loading…
      </div>
      <div v-else class="changelog-body max-h-[65vh] overflow-y-auto pr-2" v-html="changelogHtml" />
    </Dialog>
  </div>
</template>

<style scoped>
/* Minimal markdown styling for the changelog dialog (marked outputs plain h2/ul/li/p). */
.changelog-body :deep(h2) {
  font-size: 1rem;
  font-weight: 700;
  margin: 1rem 0 0.5rem;
  padding-bottom: 0.25rem;
  border-bottom: 1px solid var(--p-surface-200);
}
.changelog-body :deep(h2:first-child) {
  margin-top: 0;
}
.changelog-body :deep(h1) {
  display: none; /* the dialog header already says what this is */
}
.changelog-body :deep(ul) {
  list-style: disc;
  padding-left: 1.25rem;
  margin: 0.25rem 0 0.75rem;
}
.changelog-body :deep(ul ul) {
  list-style: circle;
  margin: 0.125rem 0;
}
.changelog-body :deep(li) {
  font-size: 0.875rem;
  margin: 0.125rem 0;
}
.changelog-body :deep(p) {
  font-size: 0.875rem;
  margin: 0.375rem 0;
}
.changelog-body :deep(code) {
  background: var(--p-surface-100);
  border-radius: 0.25rem;
  padding: 0 0.25rem;
  font-size: 0.8em;
}
</style>
