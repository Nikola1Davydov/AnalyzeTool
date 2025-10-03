<script setup>
import { ref, computed } from "vue";
import BodyTable from "./BodyTable.vue";
import Chart from "./Chart.vue";
import TopPanel from "./TopPanel.vue";
import Cart from "@/components/Cart.vue";
import { storeToRefs } from "pinia";
import { useElements } from "@/stores/useElements";
import { value } from "@primeuix/themes/aura/knob";

const { items } = storeToRefs(useElements());

const filterParameter = ["Instance", "Type", "BuildIn", "Schared", "Project"];

// состояние фильтров
const selectedCategory = ref(null);
const searchQuery = ref("");
const selectedFilters = ref([]);

// фильтрация (общая для всех компонентов)
const filteredItems = computed(() => {
  let result = items.value;

  if (selectedCategory.value) {
    result = result.filter((x) => x.CategoryName === selectedCategory.value);
  }

  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase();
    result = result
      .map((x) => {
        // оставляем только совпавшие параметры
        const matchedParams = (x.Parameters || []).filter((p) =>
          p?.Name?.toLowerCase().includes(q)
        );

        // если совпадений нет → возвращаем null (чтобы потом выкинуть)
        if (matchedParams.length === 0) return null;

        // возвращаем новый объект, где только совпавшие параметры
        return {
          ...x,
          Parameters: matchedParams,
        };
      })
      .filter(Boolean); // убираем null (элементы без совпадений)
  }

  function matchFilter(p, filters) {
    return filters.every((filter) => {
      if (filter === "Instance") return p.IsTypeParameter === false;
      if (filter === "Type") return p.IsTypeParameter === true;
      if (filter === "Schared") return p.Orgin === 0;
      if (filter === "Project") return p.Orgin === 1;
      if (filter === "BuildIn") return p.Orgin === 2;
      return false;
    });
  }

  if (selectedFilters.value.length > 0) {
    if (selectedFilters.value.some((val) => filterParameter.includes(val))) {
      result = result
        .map((x) => {
          const matchedParams = (x.Parameters || []).filter((p) =>
            matchFilter(p, selectedFilters.value)
          );

          if (matchedParams.length === 0) return null;

          return {
            ...x,
            Parameters: matchedParams,
          };
        })
        .filter(Boolean);
    }
  }

  return result;
});
</script>

<template>
  <div class="p-5 gap-5">
    <TopPanel
      v-model:category="selectedCategory"
      v-model:search="searchQuery"
      v-model:filters="selectedFilters"
      :allItems="items"
      :filteredParamters="filterParameter"
    />
    <Cart title="Parameters by Category">
      <Chart :items="filteredItems" />
    </Cart>
    <BodyTable :items="filteredItems" />
  </div>
</template>
