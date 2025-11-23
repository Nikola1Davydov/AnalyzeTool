<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
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
const { items } = storeToRefs(elementsStore);

// Filter parameter labels
const filterParameter = ["Instance", "Type", "BuildIn", "Schared", "Project"];

// UI filters
const searchQuery = ref("");
const selectedFilters = ref<string[]>([]);
const selectedLevel = ref<string | null>(null);

// Compute unique levels from the loaded items (fall back to parameter.level if element level missing)
const levels = computed(() => {
  const set = new Set<string>();
  for (const el of items.value || []) {
    if (el?.level) {
      set.add(String(el.level));
      continue;
    }
    if (el?.parameters && Array.isArray(el.parameters)) {
      for (const p of el.parameters) {
        if (p?.level) set.add(String(p.level));
      }
    }
  }
  return Array.from(set).sort();
});

// фильтрация (общая для всех компонентов)
const filteredItems = computed(() => {
  let result = items.value;

  // По категории
  // По уровню (фильтрация на уровне элементов)
  if (selectedLevel.value) {
    result = result.filter((x) => {
      const elLevel = x?.level ?? (x?.parameters?.[0]?.level ?? null);
      return elLevel === selectedLevel.value;
    });
  }
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

  function matchFilter(p: any, filters: string[]): boolean {
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

onMounted(async () => {
  // Load categories once when the page is opened
  if (!categoriesStore.categories.length) {
    await categoriesStore.loadCategories();
  }
});
async function handleUpdateData() {
  if (!selectedCategory.value) {
    console.warn("No category selected");
    return;
  }

  await elementsStore.loadByCategory(selectedCategory.value);
}
</script>

<template>
  <div class="p-5 gap-5">
    <TopPanel
      :categories="sortedCategories"
      v-model:category="selectedCategory"
      v-model:level="selectedLevel"
      :levels="levels"
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
