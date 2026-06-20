<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { invoke } from "@/RevitBridge";

interface ExtensionRow {
  id: string;
  name: string;
  version: string;
  hasCommands: boolean;
  hasUi: boolean;
  directory: string;
}

interface ExtensionsData {
  hostRevit: string;
  hostSdkVersion: string;
  pluginVersion: string;
  extensionsRoot: string;
  extensions: ExtensionRow[];
}

interface PathRow {
  path: string; // root — used for remove
  scanDir: string; // root + version — what's actually scanned (shown to the user)
  isDefault: boolean;
  valid: boolean;
  reason: string;
  extensionCount: number;
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
  lastError: string | null;
}

const data = ref<ExtensionsData | null>(null);
const loading = ref(true);

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
  } catch (e) {
    console.error("Failed to load extensions", e);
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

async function createStructure() {
  const base = await browseFolder();
  if (!base) return;
  pathsBusy.value = true;
  try {
    await invoke("CreateExtensionRoot", { basePath: base });
    await afterPathsChanged();
  } catch (e) {
    console.error("Failed to create structure", e);
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
  codeExecBusy.value = true;
  try {
    const res = await invoke<{ enabled: boolean }>("SetCodeExecution", { enabled });
    codeExec.value = !!res?.enabled;
  } catch (e) {
    console.error("Failed to update code-execution setting", e);
    await loadCodeExec(); // revert the checkbox to the real state
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
          args: ["--port", String(mcp.value.port)],
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

onMounted(() => {
  load();
  loadPaths();
  loadMcp();
  loadCodeExec();
  loadCommands();
});
</script>

<template>
  <div class="p-6">
    <div class="flex items-start justify-between mb-4 gap-4">
      <div>
        <h1 class="text-xl font-bold">Extensions</h1>
      </div>
      <div class="flex gap-2 shrink-0">
        <Button
          label="New template"
          icon="pi pi-plus"
          severity="contrast"
          @click="openTemplateDrawer"
        />
        <Button label="Reload" icon="pi pi-refresh" :loading="loading" @click="reload" />
      </div>
    </div>

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
          <div>{{ data?.pluginVersion ?? "—" }}</div>
        </div>
        <div class="col-span-2 md:col-span-1">
          <div class="text-surface-500 text-xs">Extensions folder</div>
          <div class="break-all">{{ data?.extensionsRoot ?? "—" }}</div>
        </div>
      </div>
    </section>

    <!-- Extension paths: the source roots scanned for the running Revit version (default + user-added). -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
      <div class="flex items-center justify-between mb-3 gap-3">
        <h2 class="text-sm font-bold">Extension paths</h2>
        <div class="flex gap-2 shrink-0">
          <Button
            label="Add path"
            icon="pi pi-folder"
            size="small"
            severity="secondary"
            :loading="pathsBusy"
            @click="addPath"
          />
          <Button
            label="Create structure"
            icon="pi pi-plus"
            size="small"
            severity="secondary"
            :loading="pathsBusy"
            @click="createStructure"
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

    <DataTable :value="data?.extensions ?? []" :loading="loading" dataKey="id" class="text-sm">
      <Column header="Extension">
        <template #body="{ data: row }">
          <div class="font-semibold">{{ row.name || row.id }}</div>
          <div class="text-surface-500 text-xs">{{ row.id }}</div>
        </template>
      </Column>
      <Column field="version" header="Version" />
      <Column header="Type">
        <template #body="{ data: row }">
          <Tag v-if="row.hasCommands" value="C#" severity="info" class="mr-1" />
          <Tag v-if="row.hasUi" value="UI" severity="warn" />
        </template>
      </Column>
      <Column header="" class="w-12">
        <template #body="{ data: row }">
          <Button
            icon="pi pi-folder-open"
            size="small"
            text
            severity="secondary"
            v-tooltip.left="'Open in Explorer'"
            @click="openFolder(row.directory)"
          />
        </template>
      </Column>
      <template #empty>
        <div class="text-surface-500 p-4">No extensions installed.</div>
      </template>
    </DataTable>

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
      <DataTable
        :value="filteredCommands"
        dataKey="name"
        scrollable
        scrollHeight="22rem"
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

    <CreateExtensionTemplateDrawer
      v-model:visible="templateDrawerVisible"
      :extensionsRoot="data?.extensionsRoot"
      @created="reload"
    />

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
      </div>
    </section>
  </div>
</template>
