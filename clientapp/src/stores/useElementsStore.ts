import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { ElementItem } from "./types";
import { useCategoriesStore } from "./useCategoriesStore";

export const useElementsStore = defineStore("elements", () => {
  const items = ref<ElementItem[]>([]); // full list of elements

  const categoriesStore = useCategoriesStore();

  const count = computed(() => {
    // Total number of elements
    return items.value.length;
  });

  const filteredByCategory = computed(() => {
    // Filter elements by currently selected category from categoriesStore
    const category = categoriesStore.selectedCategory;
    console.log("Filtering by category:", category);
    if (!category) {
      // No category selected -> return all elements
      return items.value;
    }

    return items.value.filter((x) => x.categoryName === category);
  });

  function setItems(arr: ElementItem[] = []) {
    // Replace full list of elements
    console.log("setItems called with:", arr, "Array?", Array.isArray(arr));

    if (!Array.isArray(arr)) {
      console.warn("setItems expected an array, got:", typeof arr, arr);
      items.value = [];
      return;
    }

    items.value = arr as ElementItem[];
  }

  function clear() {
    // Clear elements list
    items.value = [];
  }

  return {
    items,
    count,
    filteredByCategory,
    setItems,
    clear,
  };
});
