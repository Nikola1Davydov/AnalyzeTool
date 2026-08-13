<script setup lang="ts">
// Version comes straight from the extension manifest, so there is no second place to bump.
// Update CHECKING is deliberately absent: the host's Extension Manager already polls this
// extension's "updateFeed" and offers the update there — duplicating it here would mean two
// different update paths for the same package.
import manifest from "../../plugin.json";

const version = manifest.version;
</script>

<template>
  <footer class="layout-footer sticky bottom-0 z-30">
    <div class="flex justify-between items-center p-3">
      <div
        v-if="version"
        class="flex items-center gap-2 text-sm primary px-2 rounded"
        :style="{ background: 'var(--p-primary-color)' }"
      >
        <span>v{{ version }}</span>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.layout-footer {
  border-top: 1px solid var(--p-surface-border);
}
</style>
