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

const mcp = ref<McpStatus | null>(null);
const mcpBusy = ref(false);
const port = ref("17890");
const templateDrawerVisible = ref(false);

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
  // Refresh both tables — after a reload a path can flip valid/invalid (e.g. a new extension was
  // dropped into it) and the extension count changes.
  await Promise.all([load(), loadPaths()]);
}

function openFolder() {
  invoke("OpenExtensionsFolder").catch((e) => console.error(e));
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
        <Button
          label="Open folder"
          icon="pi pi-folder-open"
          severity="secondary"
          @click="openFolder"
        />
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
        <Column header="" class="w-12">
          <template #body="{ data: row }">
            <Button
              v-if="!row.isDefault"
              icon="pi pi-trash"
              size="small"
              text
              severity="danger"
              :disabled="pathsBusy"
              @click="removePath(row.path)"
            />
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
      <template #empty>
        <div class="text-surface-500 p-4">No extensions installed.</div>
      </template>
    </DataTable>

    <CreateExtensionTemplateDrawer
      v-model:visible="templateDrawerVisible"
      :extensionsRoot="data?.extensionsRoot"
      @created="reload"
    />

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
