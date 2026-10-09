<script setup lang="ts">
/**
 * Packages — one list for what AnalyseTool installs: the packages it owns (extensions-dist) first,
 * then the catalog entries that are not here yet. Two blocks used to say the same thing twice ("this
 * is a package"); an entry is one row that changes its buttons when it gets installed.
 */
import { computed } from "vue";
import ToggleSwitch from "primevue/toggleswitch";
import ExtensionCell from "./ExtensionCell.vue";
import ExtensionStatus from "./ExtensionStatus.vue";
import { safeLink, type CatalogRow, type ExtensionRow } from "./types";
import { useExtensionManager } from "./useExtensionManager";

const {
  managedExtensions,
  availableCatalog,
  catalogError,
  installFromCatalog,
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

// Exactly one of the two is set: an installed package, or a catalog entry offered for install.
type PackageRow = { key: string; ext?: ExtensionRow; offer?: CatalogRow };

const rows = computed<PackageRow[]>(() => [
  ...managedExtensions.value.map((ext) => ({ key: `ext:${ext.id}`, ext })),
  ...availableCatalog.value.map((offer) => ({ key: `offer:${offer.id}`, offer })),
]);
</script>

<template>
  <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6">
    <h2 class="text-sm font-bold">
      Packages
      <span class="text-surface-500 font-normal">— installed and managed by AnalyseTool</span>
      <i
        v-if="checkingUpdates"
        class="pi pi-spin pi-spinner text-xs text-surface-400 ml-2"
        v-tooltip.top="'Checking for updates'"
      />
    </h2>
    <p class="text-xs text-surface-500 mb-3 max-w-2xl">
      Every catalog entry is a public repository. <b>Install</b> downloads the package from the
      publisher's own release — AnalyseTool is only the courier and does not host, review or endorse
      third-party extensions.
    </p>
    <div
      v-if="updateError"
      class="mb-3 rounded-lg border border-red-200 bg-red-50 p-2 text-xs text-red-700 flex items-start gap-2"
    >
      <i class="pi pi-exclamation-triangle mt-0.5" />
      <span class="grow whitespace-pre-wrap break-words">{{ updateError }}</span>
      <Button icon="pi pi-times" size="small" text severity="danger" @click="updateError = ''" />
    </div>
    <div
      v-if="catalogError"
      class="mb-3 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
    >
      <i class="pi pi-exclamation-triangle mr-1" />{{ catalogError }}
    </div>
    <DataTable :value="rows" :loading="loading" dataKey="key" class="text-sm">
      <Column header="Extension">
        <template #body="{ data: row }">
          <ExtensionCell v-if="row.ext" :row="row.ext" vendor />
          <div v-else-if="row.offer" class="flex items-start gap-3">
            <div
              class="w-8 h-8 rounded shrink-0 mt-0.5 bg-surface-100 flex items-center justify-center text-surface-400"
            >
              <i class="pi pi-cloud-download" />
            </div>
            <div class="min-w-0">
              <div class="font-semibold flex items-center gap-2 flex-wrap">
                {{ row.offer.name }}
                <Tag v-if="row.offer.userSupplied" value="local catalog" severity="secondary" />
                <Tag v-for="tag in row.offer.tags" :key="tag" :value="tag" severity="secondary" />
              </div>
              <div class="text-surface-500 text-xs">
                {{ row.offer.id }}<template v-if="row.offer.publisher"> · {{ row.offer.publisher }}</template
                ><template v-if="row.offer.license"> · {{ row.offer.license }}</template>
                <!-- The link is the part a person can act on without this window: it is where the
                     code, the README and the releases are. -->
                <a
                  v-if="safeLink(row.offer.website)"
                  :href="safeLink(row.offer.website)!"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="ml-1"
                  v-tooltip.top="row.offer.website"
                >
                  <i class="pi pi-external-link text-xs" />
                </a>
              </div>
              <div v-if="row.offer.description" class="text-surface-500 text-xs">
                {{ row.offer.description }}
              </div>
            </div>
          </div>
        </template>
      </Column>
      <Column header="Version">
        <template #body="{ data: row }">{{ row.ext?.version }}</template>
      </Column>
      <Column header="Status">
        <template #body="{ data: row }">
          <ExtensionStatus v-if="row.ext" :row="row.ext" />
          <span v-else class="text-xs text-surface-500">Not installed</span>
        </template>
      </Column>
      <Column header="Enabled" class="w-20">
        <template #body="{ data: row }">
          <ToggleSwitch
            v-if="row.ext"
            :modelValue="row.ext.enabled"
            :disabled="loading"
            @update:modelValue="setExtensionEnabled(row.ext, !row.ext.enabled)"
          />
        </template>
      </Column>
      <Column header="" class="w-40">
        <template #body="{ data: row }">
          <template v-if="row.ext">
            <Button
              v-if="updateChecks[row.ext.id]?.updateAvailable"
              icon="pi pi-arrow-circle-up"
              size="small"
              text
              severity="success"
              :loading="updatingId === row.ext.id"
              v-tooltip.left="`Update to ${updateChecks[row.ext.id]?.latest}`"
              @click="updateExtension(row.ext)"
            />
            <Button
              icon="pi pi-pencil"
              size="small"
              text
              severity="secondary"
              v-tooltip.left="'View manifest (installed packages are read-only)'"
              @click="openEdit(row.ext)"
            />
            <Button
              icon="pi pi-folder-open"
              size="small"
              text
              severity="secondary"
              v-tooltip.left="'Open in Explorer'"
              @click="openFolder(row.ext.directory)"
            />
            <Button
              icon="pi pi-trash"
              size="small"
              text
              severity="danger"
              v-tooltip.left="'Uninstall'"
              @click="askRemove(row.ext)"
            />
          </template>
          <template v-else-if="row.offer">
            <Button
              v-if="row.offer.source"
              label="Install"
              icon="pi pi-download"
              size="small"
              @click="installFromCatalog(row.offer)"
            />
            <span v-else class="text-xs text-surface-500">manual download</span>
          </template>
        </template>
      </Column>
      <template #empty>
        <div class="text-surface-500 p-4">
          Nothing installed and nothing in the catalog — <b>Install</b> a package from a file or a
          repository.
        </div>
      </template>
    </DataTable>
  </section>
</template>
