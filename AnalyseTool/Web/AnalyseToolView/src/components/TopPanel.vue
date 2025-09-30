<script setup>
import { computed, ref } from "vue";
import { storeToRefs } from "pinia";
import { useElements } from "@/stores/useElements";
const { filtered, filter, selectedCategory } = storeToRefs(useElements());
import SelectButton from "primevue/selectbutton";

const categories = computed(() => {
  const list = Array.isArray(filtered.value) ? filtered.value : [];
  const set = new Set(list.map((e) => e?.CategoryName).filter(Boolean));
  return Array.from(set); // ["Walls","Floors", ...]
});

function testToSendData() {
  const paylod = { category: selectedCategory.value };
  window.chrome?.webview.postMessage(paylod);
}

const filterParameter = [
  "Instance parameter",
  "Type",
  "BuildIn",
  "Schared",
  "Project",
];
</script>

<template>
  <div class="card flex flex-row items-center w-full gap-5">
    <Select
      class="flex-1"
      :options="categories"
      placeholder="Select category"
      v-model="selectedCategory"
    />
    <IconField class="IconField flex-2">
      <InputIcon class="search-icon flex-none" />
      <InputText placeholder="Search" v-model="filter" class="flex-auto" />
    </IconField>
    <div class="flex flex-row gap-x-2">
      <!-- <MultiSelect
        :options="filterParameter"
        placeholder="filter parameter by"
        class="flex-none"
        label="Settings"
      /> -->
      <SelectButton
        :options="filterParameter"
        multiple
        aria-labelledby="multiple"
      />
      <Button class="flex-none" label="Send" @click="testToSendData" />
    </div>
  </div>
</template>
