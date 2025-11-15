import { defineStore } from "pinia";
import { ref, computed } from "vue";

export const useElements = defineStore("elements", () => {
  const items = ref([]); // весь список
  const selectedCategory = ref(null); // выбранная категория

  const filtered = computed(() => {
    // сначала оставляем только элементы выбранной категории
    return selectedCategory.value
      ? items.value.filter((x) => x.CategoryName === selectedCategory.value)
      : items.value;
  });

  function setItems(arr = []) {
    items.value = arr;
  }

  function setCategory(cat) {
    selectedCategory.value = cat;
  }
  return {
    items,
    filtered,
    selectedCategory,
    setItems,
    setCategory,
  };
});
