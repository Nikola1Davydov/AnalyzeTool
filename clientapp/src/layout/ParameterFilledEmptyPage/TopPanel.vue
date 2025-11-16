<script setup>
import { computed } from "vue";

// props и v-model
const props = defineProps({
  allItems: Array,
  category: String,
  search: String,
  filters: Array,
  filteredParamters: Array,
});
const emit = defineEmits(["update:category", "update:search", "update:filters"]);
// категории из всех items
const categories = computed(() => {
  const set = new Set(props.allItems.map((e) => e?.CategoryName).filter(Boolean));
  return Array.from(set);
});

function testToSendData() {
  const message = {
    command: "updateDataParameterFilledEmptyPage",
    selectedCategory: props.category,
  };

  console.log("Send clicked", message);

  // Отправить сообщение обратно в Revit
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage(JSON.stringify(message));
  } else {
    console.warn("WebView not available");
  }
}
</script>

<template>
  <div class="card flex flex-row items-center w-full gap-5">
    <Select
      class="flex-1"
      :options="categories"
      placeholder="Select category"
      :modelValue="props.category"
      @update:modelValue="emit('update:category', $event)"
    />

    <IconField class="IconField flex-2">
      <InputIcon class="search-icon flex-none" />
      <InputText
        placeholder="Search"
        class="flex-auto"
        :modelValue="props.search"
        @update:modelValue="emit('update:search', $event)"
      />
    </IconField>

    <div class="flex flex-row gap-x-2">
      <SelectButton
        :modelValue="props.filters"
        @update:modelValue="emit('update:filters', $event)"
        :options="filteredParamters"
        multiple
        aria-labelledby="multiple"
      />
      <Button class="flex-none" icon="pi pi-sync" label="Update Data" @click="testToSendData" />
    </div>
  </div>
</template>
