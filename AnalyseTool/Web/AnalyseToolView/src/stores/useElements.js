import { defineStore } from "pinia";
import { ref, computed } from "vue";

export const useElements = defineStore("elements", () => {
  const items = ref([]); // весь список
  const filter = ref(""); // строка фильтра
  const selectedCategory = ref(null); // выбранная категория

  // новые фильтры по параметрам
  const parameterFilters = ref({
    isTypeParameter: null, // true / false / null (null = не фильтруем)
    originMax: null, // число (например, 3)
    originMin: null, // число (например, 1)
  });

  const filtered = computed(() => {
    // сначала оставляем только элементы выбранной категории
    let result = selectedCategory.value
      ? items.value.filter((x) => x.CategoryName === selectedCategory.value)
      : items.value;

    // потом применяем поиск только к этим элементам
    // if (filter.value) {
    //   result = result.filter((x) =>
    //     x.Name?.toLowerCase().includes(filter.value.toLowerCase())
    //   );
    // }

    return result;
  });

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
