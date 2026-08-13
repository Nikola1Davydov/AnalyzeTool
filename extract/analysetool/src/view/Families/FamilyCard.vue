<script setup lang="ts">
import FamilyThumb from "./FamilyThumb.vue";
import type { FamilyRow } from "./types";

defineProps<{ family: FamilyRow }>();
const emit = defineEmits<{ open: []; action: [name: string] }>();

// Quick-action buttons shown on hover (stopPropagation so they don't also open the detail dialog).
function run(name: string, e: Event) {
  e.stopPropagation();
  emit("action", name);
}
</script>

<template>
  <div
    class="group relative rounded-xl border border-surface-200 bg-surface-0 overflow-hidden hover:shadow-md transition-shadow cursor-pointer"
    @click="emit('open')"
  >
    <div class="aspect-square relative text-5xl">
      <FamilyThumb :family="family" />

      <!-- Hover action overlay (only families with placed instances can be selected/isolated). -->
      <div
        v-if="family.instanceCount > 0"
        class="absolute top-2 right-2 flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
      >
        <Button
          icon="pi pi-eye"
          size="small"
          severity="secondary"
          rounded
          v-tooltip.left="'Select instances'"
          @click="run('select', $event)"
        />
        <Button
          icon="pi pi-filter"
          size="small"
          severity="secondary"
          rounded
          v-tooltip.left="'Isolate in view'"
          @click="run('isolate', $event)"
        />
      </div>
    </div>
    <div class="p-3">
      <div class="font-semibold truncate" :title="family.name">{{ family.name }}</div>
      <div class="text-surface-500 text-xs truncate">{{ family.category || "—" }}</div>
      <div class="flex items-center gap-1 mt-2 flex-wrap">
        <Tag :value="`${family.typeCount} types`" severity="secondary" />
        <Tag
          :value="`${family.instanceCount} inst`"
          :severity="family.instanceCount === 0 ? 'warn' : 'info'"
        />
        <Tag v-if="family.isInPlace" value="in-place" severity="warn" />
      </div>
    </div>
  </div>
</template>
