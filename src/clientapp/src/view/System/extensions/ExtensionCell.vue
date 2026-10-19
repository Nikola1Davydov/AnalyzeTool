<script setup lang="ts">
/** The "Extension" cell of a row: icon, name, id — and for an installed package its publisher and links. */
import { kindInfo, safeLink, type ExtensionRow } from "./types";

defineProps<{ row: ExtensionRow; vendor?: boolean }>();
</script>

<template>
  <div class="flex items-start gap-3">
    <img
      v-if="row.icon"
      :src="row.icon"
      class="w-8 h-8 rounded shrink-0 mt-0.5"
      alt=""
    />
    <div
      v-else
      class="w-8 h-8 rounded shrink-0 mt-0.5 bg-surface-100 flex items-center justify-center text-surface-400"
      v-tooltip.top="kindInfo(row).tip"
    >
      <i :class="kindInfo(row).icon" />
    </div>
    <div>
      <div class="font-semibold" :class="{ 'text-surface-400': !row.enabled }">
        {{ row.name || row.id }}
      </div>
      <div class="text-surface-500 text-xs">
        {{ row.id }}<template v-if="vendor && row.publisher"> · {{ row.publisher }}</template>
        <a
          v-if="vendor && safeLink(row.website)"
          :href="safeLink(row.website)!"
          target="_blank"
          rel="noopener noreferrer"
          class="ml-1"
          v-tooltip.top="'Website'"
        >
          <i class="pi pi-external-link text-xs" />
        </a>
        <a
          v-if="vendor && safeLink(row.supportUrl)"
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
