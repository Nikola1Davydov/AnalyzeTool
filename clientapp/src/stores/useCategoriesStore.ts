import { defineStore } from "pinia";
import { ref, computed } from "vue";

export const useCategoriesStore = defineStore("categories", () => {
  const categories = ref<string[]>([]); // full list of category names
  const selectedCategory = ref<string | null>(null); // currently selected category

  const sortedCategories = computed(() => {
    // Sort categories alphabetically for nicer UI
    return [...categories.value].sort((a, b) => a.localeCompare(b));
  });

  const hasSelectedCategory = computed(() => {
    // Indicates if any category is selected
    return selectedCategory.value !== null;
  });

  function setCategories(arr: string[] = []) {
    // Replace full list of categories
    categories.value = arr ?? [];
  }

  function selectCategory(name: string | null) {
    // Set selected category (can be null to reset filter)
    selectedCategory.value = name;
  }

  function clear() {
    // Clear categories and reset selected category
    categories.value = [];
    selectedCategory.value = null;
  }

  return {
    categories,
    sortedCategories,
    selectedCategory,
    hasSelectedCategory,
    setCategories,
    selectCategory,
    clear,
  };
});
