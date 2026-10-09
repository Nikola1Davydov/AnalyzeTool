<script setup lang="ts">
/**
 * The folders that are scanned and the one saved commands go to — plumbing, collapsed.
 */
import { useExtensionManager } from "./useExtensionManager";

const {
  loading,
  paths,
  pathsBusy,
  userCatalogPath,
  openFolder,
  addPath,
  removePath,
  useForSavedCommands,
} = useExtensionManager();
</script>

<template>
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
      Own or company repositories for <b>Packages</b> go in
      <span class="font-mono break-all">{{ userCatalogPath }}</span> — same shape as the shipped
      list (<span class="font-mono">id, name, description, source, website</span>); an entry with
      an existing id replaces the shipped one.
    </p>
  </Panel>
</template>
