<script setup lang="ts">
import { computed } from "vue";

const props = defineProps({
  categories: { type: Array, default: () => [] },
  category: { type: String, default: null },
  levels: { type: Array, default: () => [] },
  level: { type: String, default: null },
  search: { type: String, default: "" },
  filters: { type: Array, default: () => [] },
  filteredParamters: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
});

const emit = defineEmits([
  "update:category",
  "update:search",
  "update:filters",
  "update:level",
  "update-data",
]);

const categoryOptions = computed(() => props.categories as string[]);
const levelOptions = computed(() => {
  // Prepend "All Levels" (null value) to allow clearing the filter
  return [null, ...(props.levels as string[])];
});

function onUpdateDataClick() {
  // Emit event for parent to send message to Revit
  emit("update-data");
}
</script>

<template>
  <header
    class="card flex flex-row items-start lg:items-center w-full gap-3 flex-wrap lg:flex-nowrap"
  >
    <Select
      class="min-w-[160px] flex-1 w-full"
      :options="categoryOptions"
      placeholder="Select category"
      :modelValue="category"
      @update:modelValue="(val) => emit('update:category', val)"
    />

    <Select
      class="min-w-[140px] flex-1 w-full"
      :options="levelOptions"
      placeholder="Select Level"
      :modelValue="level"
      @update:modelValue="(val) => emit('update:level', val)"
    />

    <IconField class="flex-1 min-w-[200px] w-full">
      <InputIcon class="pi pi-search" />
      <InputText
        placeholder="Search"
        class="min-w-[40px] shrink w-full"
        :modelValue="search"
        @update:modelValue="(val) => emit('update:search', val)"
      />
    </IconField>

    <div class="flex flex-row flex-wrap gap-2 w-full lg:w-auto">
      <SelectButton
        class="flex-1 min-w-[180px] lg:flex-none"
        :modelValue="filters"
        @update:modelValue="(val) => emit('update:filters', val)"
        :options="filteredParamters"
        multiple
        aria-labelledby="multiple"
      />
      <Button
        class="flex-none w-full lg:w-auto"
        icon="pi pi-sync"
        :loading="loading"
        :disabled="!category || loading"
        label="Update Data"
        @click="onUpdateDataClick"
      />
    </div>
  </header>
</template>
