import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { ElementItem } from "./types";
import { Commands, sendRequest } from "@/RevitBridge";

export const useElementsStore = defineStore("elements", () => {
  const items = ref<ElementItem[]>([]); // full list of elements
  const lastLoadedCategory = ref<string | null>(null);
  const loading = ref(false);
  let pendingCategory: string | null = null;
  let pendingRequest: Promise<void> | null = null;

  const count = computed(() => {
    return items.value.length;
  });

  async function loadByCategory(categoryName: string, force = false): Promise<void> {
    if (!force && lastLoadedCategory.value === categoryName) return;
    if (pendingRequest && pendingCategory === categoryName) return pendingRequest;

    const payload = { categoryName };
    loading.value = true;
    pendingCategory = categoryName;
    pendingRequest = (async () => {
      const result = await sendRequest(Commands.GetDataByCategoryName, payload);

      if (Array.isArray(result)) {
        items.value = result as ElementItem[];
      } else {
        items.value = [];
      }

      lastLoadedCategory.value = categoryName;
    })().finally(() => {
      loading.value = false;
      pendingCategory = null;
      pendingRequest = null;
    });

    return pendingRequest;
  }

  function setItems(list: ElementItem[] = []): void {
    items.value = list ?? [];
  }

  function clear() {
    // Clear elements list
    items.value = [];
    lastLoadedCategory.value = null;
    loading.value = false;
    pendingCategory = null;
    pendingRequest = null;
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
