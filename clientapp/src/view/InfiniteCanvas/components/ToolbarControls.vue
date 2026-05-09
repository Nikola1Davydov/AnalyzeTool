<script setup lang="ts">
type ChartActionOption = {
  label: string;
  value: string;
};

const props = defineProps<{
  chartActionOptions: ChartActionOption[];
  chartAction: string;
  refreshingAll: boolean;
  hasSelectedCards: boolean;
  hasCards: boolean;
}>();

const emit = defineEmits<{
  (e: "update:chartAction", value: string): void;
  (e: "toggleCreate"): void;
  (e: "openGenerator"): void;
  (e: "refreshAll"): void;
  (e: "removeSelected"): void;
  (e: "removeAll"): void;
  (e: "openSettings"): void;
}>();

function onChartActionChange(value: string | null | undefined) {
  emit("update:chartAction", value || "SelectionInRevit");
}
</script>

<template>
  <div class="toolbar-controls">
    <SelectButton
      class="chart-action-switch"
      :options="props.chartActionOptions"
      optionLabel="label"
      optionValue="value"
      :modelValue="props.chartAction"
      aria-label="Chart click action"
      @update:modelValue="onChartActionChange"
    />
    <button type="button" class="toolbar-btn" @click="emit('toggleCreate')">
      <i class="pi pi-plus" />
    </button>
    <button type="button" class="toolbar-btn toolbar-btn--accent" @click="emit('openGenerator')">
      <i class="pi pi-sparkles" />
    </button>
    <button
      type="button"
      class="toolbar-btn"
      :disabled="props.refreshingAll"
      @click="emit('refreshAll')"
    >
      <i class="pi pi-refresh" :class="props.refreshingAll ? 'pi-spin' : ''" />
    </button>
    <button
      type="button"
      class="toolbar-btn toolbar-btn--danger"
      title="Delete selected cards"
      :disabled="!props.hasSelectedCards"
      @click="emit('removeSelected')"
    >
      <i class="pi pi-times" />
    </button>
    <button
      type="button"
      class="toolbar-btn toolbar-btn--danger"
      :disabled="!props.hasCards"
      @click="emit('removeAll')"
    >
      <i class="pi pi-trash" />
    </button>
    <button type="button" class="toolbar-btn" title="AI settings" @click="emit('openSettings')">
      <i class="pi pi-cog" />
    </button>
  </div>
</template>

<style scoped>
.toolbar-controls {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.chart-action-switch {
  min-width: 12.5rem;
}

.toolbar-btn {
  width: 2.2rem;
  height: 2.2rem;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.6rem;
  background: var(--p-surface-0, #ffffff);
  color: var(--p-surface-800, #1e293b);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.toolbar-btn:hover {
  background: var(--p-surface-100, #f1f5f9);
}

.toolbar-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.toolbar-btn--accent {
  border-color: var(--p-primary-500, #0284c7);
  color: var(--p-primary-500, #0284c7);
}

.toolbar-btn--accent:hover {
  background: var(--p-primary-100, #e0f2fe);
}

.toolbar-btn--danger {
  border-color: var(--p-red-500, #dc2626);
  color: var(--p-red-500, #dc2626);
}

.toolbar-btn--danger:hover {
  background: var(--p-red-100, #fee2e2);
}
</style>
