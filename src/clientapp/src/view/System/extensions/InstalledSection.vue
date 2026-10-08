<script setup lang="ts">
/**
 * Installed packages — the ones the Extension Manager owns (extensions-dist).
 */
import ToggleSwitch from "primevue/toggleswitch";
import ExtensionCell from "./ExtensionCell.vue";
import ExtensionStatus from "./ExtensionStatus.vue";
import { useExtensionManager } from "./useExtensionManager";

const {
  managedExtensions,
  loading,
  openEdit,
  setExtensionEnabled,
  updateChecks,
  checkingUpdates,
  updatingId,
  updateError,
  updateExtension,
  askRemove,
  openFolder,
} = useExtensionManager();
</script>

<template>
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
          <ExtensionCell :row="row" vendor />
        </template>
      </Column>
      <Column field="version" header="Version" />
      <Column header="Status">
        <template #body="{ data: row }">
          <ExtensionStatus :row="row" />
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
</template>
