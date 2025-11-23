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

const emit = defineEmits(["update:category", "update:search", "update:filters", "update-data"]);

const categoryOptions = computed(() => props.categories as string[]);

function onUpdateDataClick() {
  // Emit event for parent to send message to Revit
  emit("update-data");
}
</script>

<template>
  <div class="card flex flex-row items-center w-full gap-5">
    <Select
      class="flex-1"
      :options="categoryOptions"
      placeholder="Select category"
      :modelValue="category"
      @update:modelValue="(val) => emit('update:category', val)"
    />

    <IconField class="IconField flex-2">
      <InputIcon class="search-icon flex-none" />
      <InputText
        placeholder="Search"
        class="flex-auto"
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
  </div>
</template>
