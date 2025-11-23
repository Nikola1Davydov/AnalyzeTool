<script setup lang="ts">
import { computed } from "vue";

const props = defineProps({
  categories: {
    type: Array,
    default: () => [],
  },
  category: {
    type: String,
    default: null,
  },
  levels: {
    type: Array,
    default: () => [],
  },
  level: {
    type: String,
    default: null,
  },
  search: {
    type: String,
    default: "",
  },
  filters: {
    type: Array,
    default: () => [],
  },
  filteredParamters: {
    type: Array,
    default: () => [],
  },
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
  <header class="card flex flex-row items-center w-full gap-5">
    <Select
      :options="categoryOptions"
      placeholder="Select category"
      :modelValue="category"
      @update:modelValue="(val) => emit('update:category', val)"
    />

    <Select
      :options="levelOptions"
      placeholder="Select Level"
      :modelValue="level"
      @update:modelValue="(val) => emit('update:level', val)"
    />

    <IconField class="IconField flex-1">
      <InputIcon class="search-icon" />
      <InputText
        placeholder="Search"
        :modelValue="search"
        @update:modelValue="(val) => emit('update:search', val)"
      />
    </IconField>

    <div class="flex flex-row gap-x-2">
      <SelectButton
        :modelValue="filters"
        @update:modelValue="(val) => emit('update:filters', val)"
        :options="filteredParamters"
        multiple
        aria-labelledby="multiple"
      />
      <Button class="flex-none" icon="pi pi-sync" label="Update Data" @click="onUpdateDataClick" />
    </div>
  </header>
</template>
