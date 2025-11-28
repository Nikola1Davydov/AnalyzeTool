<script setup lang="ts">
import { storeToRefs } from "pinia";
import { useUpdateStore } from "@/stores/useUpdateStore";

const updateStore = useUpdateStore();
const { updateInfo } = storeToRefs(updateStore);
</script>

<template>
  <footer class="layout-footer sticky bottom-0 z-30">
    <div class="flex justify-between items-center p-3">
      <!-- Version + update info -->
      <div
        v-if="updateInfo && updateInfo.currentVersion"
        class="flex items-center gap-2 text-sm primary px-2 rounded"
        :style="{ background: 'var(--p-primary-color)' }"
      >
        <!-- Current version -->
        <span>Current: v{{ updateInfo.currentVersion }}</span>

        <!-- New version + link (only if newer exists) -->
        <template v-if="updateInfo.isUpdateAvailable">
          <span>→ New: v{{ updateInfo.latestVersion }}</span>

          <a
            v-if="updateInfo.releaseUrl"
            :href="updateInfo.releaseUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="underline font-semibold"
          >
            Download
          </a>
        </template>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.layout-footer {
  border-top: 1px solid var(--p-surface-border);
}
</style>
