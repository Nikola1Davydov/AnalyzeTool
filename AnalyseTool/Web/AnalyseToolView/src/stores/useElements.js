import { defineStore } from "pinia";
import { ref, computed } from "vue";

export const useElements = defineStore("elements", () => {
  const items = ref([]); // весь список
  const filter = ref(""); // строка фильтра
  const selectedCategory = ref(null); // выбранная категория

  const filtered = computed(() =>
    !filter.value
      ? items.value
      : items.value.filter((x) =>
          x.name?.toLowerCase().includes(filter.value.toLowerCase())
        )
  );

  function setItems(arr = []) {
    items.value = arr;
  }
  function setFilter(v = "") {
    filter.value = v;
  }
  function setCategory(cat) {
    selectedCategory.value = cat;
  }
  return {
    items,
    filtered,
    selectedCategory,
    filter,
    setItems,
    setFilter,
    setCategory,
  };
});
