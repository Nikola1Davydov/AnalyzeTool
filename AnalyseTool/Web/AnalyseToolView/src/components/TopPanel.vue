<script setup>
import { computed, ref } from "vue";
import { storeToRefs } from "pinia";
import { useElements } from "@/stores/useElements";
const { filtered, filter, selectedCategory } = storeToRefs(useElements());

const categories = computed(() => {
  const list = Array.isArray(filtered.value) ? filtered.value : [];
  const set = new Set(list.map((e) => e?.CategoryName).filter(Boolean));
  return Array.from(set); // ["Walls","Floors", ...]
});

function testToSendData() {
  const paylod = { category: selectedCategory.value };
  window.chrome?.webview.postMessage(paylod);
}
</script>

<template>
  <div class="card flex flex-row items-center w-full gap-5">
    <Select
      class="flex-1"
      :options="categories"
      placeholder="Select category"
      v-model="selectedCategory"
    />
    <p>Выбрана: {{ selectedCategory }}</p>
    <IconField class="IconField flex-2">
      <InputIcon class="search-icon flex-none" />
      <InputText placeholder="Search" v-model="filter" class="flex-auto" />
    </IconField>
    <div class="flex flex-row gap-x-2">
      <Button class="flex-none" label="Settings" />
      <Button class="flex-none" label="Send" @click="testToSendData" />
    </div>
  </div>
</template>
