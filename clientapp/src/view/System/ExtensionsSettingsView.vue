<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { invoke } from "@/RevitBridge";

interface ExtensionRow {
  id: string;
  name: string;
  version: string;
  targetRevit: string;
  hasCommands: boolean;
  hasUi: boolean;
  compatible: boolean;
  directory: string;
}

interface ExtensionsData {
  hostRevit: string;
  hostSdkVersion: string;
  pluginVersion: string;
  extensionsRoot: string;
  extensions: ExtensionRow[];
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
  await load();
}

function openFolder() {
  invoke("OpenExtensionsFolder").catch((e) => console.error(e));
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

    <DataTable :value="data?.extensions ?? []" :loading="loading" dataKey="id" class="text-sm">
      <Column header="Extension">
        <template #body="{ data: row }">
          <div class="font-semibold">{{ row.name || row.id }}</div>
          <div class="text-surface-500 text-xs">{{ row.id }}</div>
        </template>
      </Column>
      <Column field="version" header="Version" />
      <Column header="Target">
        <template #body="{ data: row }">
          <span class="mr-2">{{ row.targetRevit }}</span>
          <Tag
            :value="row.compatible ? 'ok' : 'mismatch'"
            :severity="row.compatible ? 'success' : 'danger'"
          />
        </template>
      </Column>
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
      :hostRevit="data?.hostRevit"
      :extensionsRoot="data?.extensionsRoot"
      @created="load"
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
