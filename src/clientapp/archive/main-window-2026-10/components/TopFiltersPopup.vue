<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "TopFiltersPopup",
});
</script>

<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    visible: boolean;
    draftFilters: string[];
    filterOptions: string[];
    showLevel?: boolean;
    levels?: string[];
    draftLevel?: string | null;
    showClickAction?: boolean;
    clickActionOptions?: string[];
    draftClickAction?: string | null;
  }>(),
  {
    showLevel: false,
    levels: () => [],
    draftLevel: null,
    showClickAction: false,
    clickActionOptions: () => ["Selection", "Isolation"],
    draftClickAction: null,
  },
);

const emit = defineEmits<{
  "update:draftFilters": [value: string[]];
  "update:draftLevel": [value: string | null];
  "update:draftClickAction": [value: string | null];
  apply: [];
  clear: [];
  cancel: [];
}>();

function onUpdateDraftFilters(value: string[] | null | undefined) {
  emit("update:draftFilters", value || []);
}

function onUpdateDraftLevel(value: string | null | undefined) {
  emit("update:draftLevel", value || null);
}

function onUpdateDraftClickAction(value: string | null | undefined) {
  emit("update:draftClickAction", value || null);
}
</script>

<template>
  <div
    v-if="props.visible"
    class="absolute bg-emphasis right-0 z-30 mt-2 w-[340px] max-w-[90vw] rounded-lg border shadow-xl p-3 flex flex-col gap-3"
  >
    <div class="text-sm font-semibold">Filter Settings</div>

    <div class="flex flex-col gap-1">
      <label class="text-xs">Parameter Types</label>
      <SelectButton
        :modelValue="props.draftFilters"
        @update:modelValue="onUpdateDraftFilters"
        :options="props.filterOptions"
        multiple
        aria-labelledby="parameter-filters"
      />
    </div>

    <div v-if="props.showLevel" class="flex flex-col gap-1">
      <label class="text-xs text-surface-600">Level</label>
      <Select
        :options="[null, ...props.levels]"
        placeholder="Select level"
        :modelValue="props.draftLevel"
        @update:modelValue="onUpdateDraftLevel"
      />
    </div>

    <div v-if="props.showClickAction" class="flex flex-col gap-1">
      <label class="text-xs text-surface-600">Chart click action</label>
      <Select
        :options="props.clickActionOptions"
        placeholder="Chart click action"
        :modelValue="props.draftClickAction"
        @update:modelValue="onUpdateDraftClickAction"
      />
    </div>

    <div class="flex justify-end gap-2 pt-1">
      <Button label="Clear" text @click="emit('clear')" />
      <Button label="Cancel" outlined @click="emit('cancel')" />
      <Button label="Apply" @click="emit('apply')" />
    </div>
  </div>
</template>
