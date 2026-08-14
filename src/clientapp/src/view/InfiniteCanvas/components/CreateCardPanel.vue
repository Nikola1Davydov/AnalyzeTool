<script setup lang="ts">
type CardViewType = "chart" | "table";

type ViewTypeOption = {
  label: string;
  value: CardViewType;
};

const props = defineProps<{
  visible: boolean;
  sortedCategories: string[];
  availableParameters: string[];
  draftCategory: string | null;
  draftParameter: string | null;
  draftViewType: CardViewType;
  viewTypeOptions: ViewTypeOption[];
  draftCategoryLoading: boolean;
  draftCategoryError: string;
  canCreateCard: boolean;
}>();

const emit = defineEmits<{
  (e: "update:category", value: string | null): void;
  (e: "update:parameter", value: string | null): void;
  (e: "update:viewType", value: CardViewType): void;
  (e: "close"): void;
  (e: "create"): void;
}>();
</script>

<template>
  <div v-if="props.visible" class="creator-panel">
    <div class="creator-title">Create Card</div>

    <div class="creator-grid">
      <div class="creator-field">
        <label>Category</label>
        <Select
          :options="props.sortedCategories"
          placeholder="Select category"
          :modelValue="props.draftCategory"
          @update:modelValue="emit('update:category', $event)"
        />
      </div>

      <div class="creator-field">
        <label>Parameter</label>
        <Select
          :options="props.availableParameters"
          placeholder="Select parameter"
          :modelValue="props.draftParameter"
          :disabled="!props.draftCategory || props.draftCategoryLoading"
          @update:modelValue="emit('update:parameter', $event)"
        />
        <span v-if="props.draftCategoryLoading" class="creator-meta">Loading parameters...</span>
        <span v-else-if="props.draftCategoryError" class="creator-meta creator-meta--error">
          {{ props.draftCategoryError }}
        </span>
      </div>

      <div class="creator-field">
        <label>View</label>
        <Select
          :options="props.viewTypeOptions"
          optionLabel="label"
          optionValue="value"
          :modelValue="props.draftViewType"
          @update:modelValue="emit('update:viewType', $event || 'chart')"
        />
      </div>
    </div>

    <div class="creator-actions">
      <button type="button" class="creator-secondary-btn" @click="emit('close')">Cancel</button>
      <button
        type="button"
        class="creator-primary-btn"
        :disabled="!props.canCreateCard"
        @click="emit('create')"
      >
        Create
      </button>
    </div>
  </div>
</template>

<style scoped>
.creator-panel {
  width: min(34rem, calc(100vw - 4rem));
  padding: 0.75rem;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.7rem;
  background: var(--p-surface-0, #ffffff);
  box-shadow: 0 16px 30px -26px rgba(15, 23, 42, 0.6);
}

.creator-title {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--p-surface-800, #1e293b);
  margin-bottom: 0.6rem;
}

.creator-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.55rem;
}

.creator-field {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.creator-field label {
  font-size: 0.7rem;
  color: var(--p-surface-600, #475569);
}

.creator-meta {
  font-size: 0.7rem;
  color: var(--p-surface-500, #64748b);
}

.creator-meta--error {
  color: #dc2626;
}

.creator-actions {
  margin-top: 0.7rem;
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
}

.creator-primary-btn,
.creator-secondary-btn {
  border-radius: 0.55rem;
  font-size: 0.76rem;
  font-weight: 600;
  padding: 0.35rem 0.7rem;
  cursor: pointer;
}

.creator-secondary-btn {
  border: 1px solid var(--p-surface-300, #d1d5db);
  background: var(--p-surface-0, #ffffff);
  color: var(--p-surface-700, #334155);
}

.creator-primary-btn {
  border: 1px solid var(--p-primary-500, #0284c7);
  background: var(--p-primary-500, #0284c7);
  color: var(--p-primary-contrast-color, #ffffff);
}

.creator-primary-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
</style>
