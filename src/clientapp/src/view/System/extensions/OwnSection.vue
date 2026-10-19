<script setup lang="ts">
/**
 * Your own extensions — the folders you edit (the default dev root and any added path).
 */
import ToggleSwitch from "primevue/toggleswitch";
import ExtensionCell from "./ExtensionCell.vue";
import ExtensionStatus from "./ExtensionStatus.vue";
import { useExtensionManager } from "./useExtensionManager";

const {
  devExtensions,
  devSearch,
  filteredDevExtensions,
  loading,
  openEdit,
  setExtensionEnabled,
  askRemove,
  openFolder,
} = useExtensionManager();
</script>

<template>
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
          <ExtensionCell :row="row" />
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
</template>
