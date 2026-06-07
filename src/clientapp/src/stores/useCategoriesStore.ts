import { defineStore } from "pinia";
import { ref, computed } from "vue";
import { Commands, invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

export const useCategoriesStore = defineStore("categories", () => {
  const categories = ref<string[]>([]); // full list of category names
  const selectedCategory = ref<string | null>(null); // currently selected category
  const categoriesLoading = ref(false);
  const categoriesLoaded = ref(false);
  let categoriesRequest: Promise<void> | null = null;

  const sortedCategories = computed(() => {
    // Sort categories alphabetically for nicer UI
    return [...categories.value].sort((a, b) => a.localeCompare(b));
  });

  async function loadCategories(force = false): Promise<void> {
    if (!force && categoriesLoaded.value) return;
    if (categoriesRequest) return categoriesRequest;

    categoriesLoading.value = true;
    categoriesRequest = (async () => {
      try {
        // Ask Revit for categories
        const result = await invoke<unknown>(Commands.GetCategoriesInRevit, null);
        categories.value = Array.isArray(result)
          ? (result.filter((x) => typeof x === "string") as string[])
          : [];
        categoriesLoaded.value = true;
      } catch (err) {
        console.error("Failed to load categories", err);
        useNotificationStore().error(`Failed to load categories: ${String((err as Error)?.message ?? err)}`);
        categories.value = [];
      }
    })().finally(() => {
      categoriesLoading.value = false;
      categoriesRequest = null;
    });

    return categoriesRequest;
  }

  const hasSelectedCategory = computed(() => {
    // Indicates if any category is selected
    return selectedCategory.value !== null;
  });

  function setCategories(arr: string[] = []) {
    // Replace full list of categories
    categories.value = arr ?? [];
    categoriesLoaded.value = true;
    categoriesLoading.value = false;
  }

  function selectCategory(name: string | null) {
    // Set selected category (can be null to reset filter)
    selectedCategory.value = name;
  }

  function clear() {
    // Clear categories and reset selected category
    categories.value = [];
    selectedCategory.value = null;
    categoriesLoaded.value = false;
    categoriesLoading.value = false;
    categoriesRequest = null;
  }

  return {
    categories,
    categoriesLoading,
    categoriesLoaded,
    sortedCategories,
    selectedCategory,
    hasSelectedCategory,
    loadCategories,
    setCategories,
    selectCategory,
    clear,
  };
});
