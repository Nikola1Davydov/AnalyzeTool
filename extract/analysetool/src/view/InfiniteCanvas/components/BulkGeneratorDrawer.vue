<script setup lang="ts">
type GeneratorDraftRow = {
  id: number;
  parameter: string;
  category: string | null;
  useAllCategories: boolean;
};

const props = defineProps<{
  visible: boolean;
  generationRows: GeneratorDraftRow[];
  sortedCategories: string[];
  canGenerateFromDrawer: boolean;
  generatingFromDrawer: boolean;
  generationInfo: string;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "addRow"): void;
  (e: "removeRow", rowId: number): void;
  (e: "update:parameter", payload: { rowId: number; value: string }): void;
  (e: "update:category", payload: { rowId: number; value: string | null }): void;
  (e: "update:allCategories", payload: { rowId: number; value: boolean }): void;
  (e: "startGeneration"): void;
}>();
</script>

<template>
  <Drawer
    :visible="props.visible"
    header="Bulk Generator"
    position="right"
    :modal="false"
    :dismissable="true"
    :style="{ width: 'min(48rem, 94vw)' }"
    @update:visible="emit('update:visible', $event)"
  >
    <div class="generator-panel">
      <div class="generator-table-head">
        <span>Parameter</span>
        <span>Category</span>
        <span>Action</span>
      </div>

      <div v-for="row in props.generationRows" :key="row.id" class="generator-row">
        <InputText
          :modelValue="row.parameter"
          placeholder="Parameter name"
          @update:modelValue="
            emit('update:parameter', { rowId: row.id, value: String($event || '') })
          "
        />

        <div class="generator-category-cell">
          <Select
            :options="props.sortedCategories"
            placeholder="Category"
            :modelValue="row.category"
            :disabled="row.useAllCategories"
            @update:modelValue="emit('update:category', { rowId: row.id, value: $event })"
          />
          <label class="generator-all-toggle">
            <Checkbox
              binary
              :modelValue="row.useAllCategories"
              @update:modelValue="
                emit('update:allCategories', { rowId: row.id, value: Boolean($event) })
              "
            />
            <span>All categories</span>
          </label>
        </div>

        <button type="button" class="generator-secondary-btn" @click="emit('removeRow', row.id)">
          Remove
        </button>
      </div>

      <div class="generator-actions">
        <button type="button" class="generator-secondary-btn" @click="emit('addRow')">
          + Add row
        </button>
        <button
          type="button"
          class="generator-primary-btn"
          :disabled="!props.canGenerateFromDrawer || props.generatingFromDrawer"
          @click="emit('startGeneration')"
        >
          {{ props.generatingFromDrawer ? "Generating..." : "Start generation" }}
        </button>
      </div>

      <div v-if="props.generationInfo" class="generator-info">{{ props.generationInfo }}</div>
    </div>
  </Drawer>
</template>

<style scoped>
.generator-primary-btn,
.generator-secondary-btn {
  border-radius: 0.55rem;
  font-size: 0.76rem;
  font-weight: 600;
  padding: 0.35rem 0.7rem;
  cursor: pointer;
}

.generator-secondary-btn {
  border: 1px solid var(--p-surface-300, #d1d5db);
  background: var(--p-surface-0, #ffffff);
  color: var(--p-surface-700, #334155);
}

.generator-primary-btn {
  border: 1px solid var(--p-primary-500, #0284c7);
  background: var(--p-primary-500, #0284c7);
  color: var(--p-primary-contrast-color, #ffffff);
}

.generator-primary-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.generator-panel {
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
}

.generator-table-head,
.generator-row {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) minmax(0, 1.5fr) auto;
  gap: 0.55rem;
  align-items: start;
}

.generator-table-head {
  font-size: 0.72rem;
  color: var(--p-surface-600, #475569);
  font-weight: 700;
  text-transform: uppercase;
}

.generator-category-cell {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.generator-all-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.74rem;
  color: var(--p-surface-700, #334155);
}

.generator-actions {
  margin-top: 0.45rem;
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
}

.generator-info {
  font-size: 0.76rem;
  color: var(--p-surface-600, #475569);
  line-height: 1.35;
}
</style>
