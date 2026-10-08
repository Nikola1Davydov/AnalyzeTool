<script setup lang="ts">
/**
 * Catalog entries that are not installed yet — the way in; what is installed is a row above.
 */
import { safeLink } from "./types";
import { useExtensionManager } from "./useExtensionManager";

const {
  catalog,
  availableCatalog,
  catalogError,
  installFromCatalog,
} = useExtensionManager();
</script>

<template>
  <!-- Available: catalog entries that are not here yet. Installed ones are rows above, with
       their own update and uninstall — the catalog is only the way in. -->
  <section
    v-if="availableCatalog.length || catalogError"
    class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-6"
  >
    <h2 class="text-sm font-bold">Available</h2>
    <p class="text-xs text-surface-500 mb-3 max-w-2xl">
      Every entry is a public repository. <b>Install</b> downloads the package from the publisher's
      own release — AnalyseTool is only the courier and does not host, review or endorse
      third-party extensions.
    </p>
    <div
      v-if="catalogError"
      class="mb-3 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
    >
      <i class="pi pi-exclamation-triangle mr-1" />{{ catalogError }}
    </div>
    <div class="flex flex-col gap-3">
      <div
        v-for="row in availableCatalog"
        :key="row.id"
        class="border border-surface-200 rounded-lg p-3 flex items-start justify-between gap-4"
      >
        <div class="min-w-0">
          <div class="flex items-center gap-2 flex-wrap">
            <span class="font-medium">{{ row.name }}</span>
                <Tag v-if="row.userSupplied" value="local catalog" severity="secondary" />
            <Tag v-for="tag in row.tags" :key="tag" :value="tag" severity="secondary" />
          </div>
          <div class="text-xs text-surface-500 mt-0.5">
            <span v-if="row.publisher">{{ row.publisher }}</span>
            <span v-if="row.license"> · {{ row.license }}</span>
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

        <div class="shrink-0">
          <Button
            v-if="row.source"
            label="Install"
            icon="pi pi-download"
            size="small"
            @click="installFromCatalog(row)"
          />
          <span v-else class="text-xs text-surface-500">manual download</span>
        </div>
      </div>
    </div>
  </section>
</template>
