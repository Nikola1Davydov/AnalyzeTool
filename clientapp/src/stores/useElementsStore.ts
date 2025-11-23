import { defineStore } from "pinia";
import { ref, computed } from "vue";
import type { ElementItem } from "./types";
import { useCategoriesStore } from "./useCategoriesStore";
import { sendRequest } from "@/RevitBridge";

export const useElementsStore = defineStore("elements", () => {
  const items = ref<ElementItem[]>([]); // full list of elements

  const categoriesStore = useCategoriesStore();

  const count = computed(() => {
    return items.value.length;
  });

  async function loadByCategory(categoryName: string): Promise<void> {
    const payload = { categoryName };
    const result = await sendRequest("updateDataParameterFilledEmptyPage", payload);

    if (Array.isArray(result)) {
      items.value = result as ElementItem[];
    } else {
      items.value = [];
    }
  }

  function setItems(list: ElementItem[] = []): void {
    items.value = list ?? [];
  }

  function clear() {
    // Clear elements list
    items.value = [];
  }

  return {
    items,
    count,
    loadByCategory,
    setItems,
    clear,
  };
});
