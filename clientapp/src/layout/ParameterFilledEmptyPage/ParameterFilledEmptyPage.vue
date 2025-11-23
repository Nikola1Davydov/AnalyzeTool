<script setup lang="ts">
import { ref, computed } from "vue";
import { storeToRefs } from "pinia";
import { useElementsStore } from "@/stores/useElementsStore";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import BodyTable from "./BodyTable.vue";
import Chart from "./Chart.vue";
import TopPanel from "./TopPanel.vue";
import Cart from "@/components/Cart.vue";

const categoriesStore = useCategoriesStore();
const elementsStore = useElementsStore();

const { sortedCategories, selectedCategory } = storeToRefs(categoriesStore);
const { filteredByCategory } = storeToRefs(elementsStore);

// Filter parameter labels
const filterParameter = ["Instance", "Type", "BuildIn", "Schared", "Project"];

// UI filters
const searchQuery = ref("");
const selectedFilters = ref<string[]>([]);

// фильтрация (общая для всех компонентов)
const filteredItems = computed(() => {
  let result = filteredByCategory.value;
  console.log("Filtered by category:", result);

  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase();
    result = result
      .map((x) => {
        // оставляем только совпавшие параметры
        const matchedParams = (x.parameters || []).filter((p) =>
          p?.name?.toLowerCase().includes(q)
        );

        // если совпадений нет → возвращаем null (чтобы потом выкинуть)
        if (matchedParams.length === 0) return null;

        // возвращаем новый объект, где только совпавшие параметры
        return {
          ...x,
          parameters: matchedParams,
        };
      })
      .filter(Boolean) as typeof result; // убираем null (элементы без совпадений)
  }

  function matchFilter(p: any, filters: string[]) {
    return filters.every((filter) => {
      if (filter === "Instance") return p.isTypeParameter === false;
      if (filter === "Type") return p.isTypeParameter === true;
      if (filter === "Schared") return p.orgin === 0;
      if (filter === "Project") return p.orgin === 1;
      if (filter === "BuildIn") return p.orgin === 2;
      return false;
    });
  }

  if (selectedFilters.value.length > 0) {
    if (selectedFilters.value.some((val) => filterParameter.includes(val))) {
      result = result
        .map((x) => {
          const matchedParams = (x.parameters || []).filter((p) =>
            matchFilter(p, selectedFilters.value)
          );

          if (matchedParams.length === 0) return null;

          return {
            ...x,
            parameters: matchedParams,
          };
        })
        .filter(Boolean) as typeof result;
    }
  }

  return result;
});

function handleUpdateData() {
  console.log("selectedCategory in store before send:", selectedCategory.value);

  if (!(window as any).chrome?.webview) {
    console.warn("WebView not available");
    return;
  }

  const message = {
    command: "updateDataParameterFilledEmptyPage",
    JsonData: selectedCategory.value,
  };

  // Send plain object, WebView2 will serialize it
  (window as any).chrome.webview.postMessage(message);
  console.log("selectedCategory in store before send:", message);
}
</script>

<template>
  <div class="p-5 gap-5">
    <TopPanel
      :categories="sortedCategories"
      v-model:category="selectedCategory"
      v-model:search="searchQuery"
      v-model:filters="selectedFilters"
      :filteredParamters="filterParameter"
      @update-data="handleUpdateData"
    />
    <Cart title="Parameters by Category">
      <Chart :items="filteredItems" />
    </Cart>
    <BodyTable :items="filteredItems" />
  </div>
</template>
