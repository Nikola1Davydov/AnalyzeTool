<script setup lang="ts">
import { ref, onMounted } from "vue";
import { invoke } from "@/RevitBridge";

interface ExtensionRow {
  id: string;
  displayName: string;
  version: string;
  targetRevit: string;
  sdkVersion: string;
  hasCommands: boolean;
  hasUi: boolean;
  compatible: boolean;
  directory: string;
}

interface ExtensionsData {
  hostRevit: string;
  extensionsRoot: string;
  extensions: ExtensionRow[];
}

const data = ref<ExtensionsData | null>(null);
const loading = ref(true);

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

onMounted(load);
</script>

<template>
  <div class="p-6">
    <div class="flex items-start justify-between mb-4 gap-4">
      <div>
        <h1 class="text-xl font-bold">Extensions</h1>
        <p class="text-sm text-surface-500 break-all">
          {{ data?.extensionsRoot }} · Revit {{ data?.hostRevit }}
        </p>
      </div>
      <div class="flex gap-2 shrink-0">
        <Button label="Reload" icon="pi pi-refresh" :loading="loading" @click="reload" />
        <Button
          label="Open folder"
          icon="pi pi-folder-open"
          severity="secondary"
          @click="openFolder"
        />
      </div>
    </div>

    <DataTable :value="data?.extensions ?? []" :loading="loading" dataKey="id" class="text-sm">
      <Column header="Extension">
        <template #body="{ data: row }">
          <div class="font-semibold">{{ row.displayName || row.id }}</div>
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
  </div>
</template>
