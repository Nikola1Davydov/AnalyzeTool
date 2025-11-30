<script setup lang="ts">
import { computed } from "vue";

const props = defineProps({
  categories: { type: Array, default: () => [] },
  category: { type: String, default: null },
  loading: { type: Boolean, default: false },
  filters: { type: Array, default: () => [] },
  filterOptions: { type: Array, default: () => [] },
  search: { type: String, default: "" },
  clickAction: { type: String, default: "Selection" },
  actionOptions: { type: Array, default: () => ["Selection", "Isolation"] },
});

const emit = defineEmits([
  "update:category",
  "update:filters",
  "update:search",
  "update:clickAction",
  "update-data",
]);

const categoryOptions = computed(() => props.categories as string[]);
const clickActionOptions = computed(() => props.actionOptions as string[]);

function onUpdateDataClick() {
  emit("update-data");
}
</script>

<template>
  <header
    class="card flex flex-row items-start lg:items-center w-full gap-3 flex-wrap lg:flex-nowrap"
  >
    <Select
      class="min-w-[200px] flex-1 w-full"
      :options="categoryOptions"
      placeholder="Select category"
      :modelValue="category"
      @update:modelValue="(val) => emit('update:category', val)"
    />

    <IconField class="flex-1 min-w-[200px] w-full">
      <InputIcon class="pi pi-search" />
      <InputText
        placeholder="Search parameters"
        class="min-w-[40px] shrink w-full"
        :modelValue="search"
        @update:modelValue="(val) => emit('update:search', val)"
      />
    </IconField>

    <Select
      class="min-w-[180px] flex-1 w-full"
      :options="clickActionOptions"
      placeholder="Chart click action"
      :modelValue="clickAction"
      @update:modelValue="(val) => emit('update:clickAction', val)"
    />

    <div class="flex flex-row flex-wrap gap-2 w-full lg:w-auto">
      <SelectButton
        class="flex-1 min-w-[200px] lg:flex-none"
        :modelValue="filters"
        @update:modelValue="(val) => emit('update:filters', val)"
        :options="filterOptions"
        multiple
        aria-labelledby="parameter-filter"
      />
      <Button
        class="flex-none w-full lg:w-auto"
        icon="pi pi-sync"
        label="Update Data"
        :loading="loading"
        :disabled="!category || loading"
        @click="onUpdateDataClick"
      />
    </div>
  </header>
</template>
