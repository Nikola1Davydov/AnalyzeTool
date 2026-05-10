import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { ElementItem } from "./types";
import { Commands, sendRequest } from "@/RevitBridge";

export const useElementsStore = defineStore("elements", () => {
  const items = ref<ElementItem[]>([]); // full list of elements
  const lastLoadedCategory = ref<string | null>(null);
  const loading = ref(false);

  const count = computed(() => {
    return items.value.length;
  });

  // Fire-and-forget: sendRequest just posts to WebView2; the response
  // arrives asynchronously via App.vue's message listener → setItems().
  async function loadByCategory(categoryName: string, force = false): Promise<void> {
    if (!force && lastLoadedCategory.value === categoryName) return;

    loading.value = true;
    lastLoadedCategory.value = categoryName;

    try {
      sendRequest(Commands.GetDataByCategoryName, { categoryName });
    } catch (err) {
      loading.value = false;
      throw err;
    }
  }

  function setItems(list: ElementItem[] = []): void {
    items.value = list ?? [];
    loading.value = false;
  }

  function clear() {
    items.value = [];
    lastLoadedCategory.value = null;
    loading.value = false;
  }

  return {
    items,
    count,
    loading,
    lastLoadedCategory,
    loadByCategory,
    setItems,
    clear,
  };
});
