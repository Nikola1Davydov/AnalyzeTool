import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { ElementItem } from "./types";
import { Commands, invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

export const useElementsStore = defineStore("elements", () => {
  const items = ref<ElementItem[]>([]); // full list of elements
  const lastLoadedCategory = ref<string | null>(null);
  const loading = ref(false);

  const count = computed(() => {
    return items.value.length;
  });

  async function loadByCategory(categoryName: string, force = false): Promise<void> {
    if (!force && lastLoadedCategory.value === categoryName) return;

    loading.value = true;
    lastLoadedCategory.value = categoryName;
    try {
      const list = await invoke<ElementItem[]>(Commands.GetDataByCategoryName, { categoryName });
      setItems(list);
    } catch (err) {
      console.error("Failed to load elements", err);
      useNotificationStore().error(`Failed to load elements: ${String((err as Error)?.message ?? err)}`);
      setItems([]);
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
