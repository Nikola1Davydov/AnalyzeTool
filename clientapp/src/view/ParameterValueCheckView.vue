<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import ValueChart from "@/view/ParameterValueCheck/Chart.vue";
import TopPanel from "@/view/ParameterValueCheck/TopPanel.vue";
import { useElementsStore } from "@/stores/useElementsStore";
import { useCategoriesStore } from "@/stores/useCategoriesStore";

const elementsStore = useElementsStore();
const categoriesStore = useCategoriesStore();

const { items } = storeToRefs(elementsStore);
const { sortedCategories } = storeToRefs(categoriesStore);

const selectedCategory = ref<string | null>(null);
const selectedFilters = ref<string[]>([]);
const searchQuery = ref("");
const clickAction = ref("Selection");
const loading = ref(false);

const filterParameter = ["Instance", "Type", "BuildIn", "Schared", "Project"];

onMounted(() => {
  categoriesStore.loadCategories().catch((err) => {
    console.error("Failed to load categories", err);
  });
});

const hasData = computed(() => (items.value?.length ?? 0) > 0);

async function updateData() {
  loading.value = true;
  try {
    if (selectedCategory.value) {
      await elementsStore.loadByCategory(selectedCategory.value);
    } else {
      elementsStore.clear();
    }
  } catch (err) {
    console.error("Failed to load elements", err);
  } finally {
    loading.value = false;
  }
}

watch(
  () => items.value,
  () => {
    // Stop spinner once store updates with new data
    if (loading.value) loading.value = false;
  }
);
</script>

<template>
  <div class="p-5 flex flex-col gap-4">
    <TopPanel
      :categories="sortedCategories"
      :category="selectedCategory"
      :filters="selectedFilters"
      :filterOptions="filterParameter"
      :search="searchQuery"
      :clickAction="clickAction"
      :loading="loading"
      @update:category="(val) => (selectedCategory = val)"
      @update:filters="(val) => (selectedFilters = val)"
      @update:search="(val) => (searchQuery = val)"
      @update:clickAction="(val) => (clickAction = val || 'Selection')"
      @update-data="updateData"
    />

    <ValueChart
      :items="items"
      :filters="selectedFilters"
      :search="searchQuery"
      :clickAction="clickAction"
    />

    <div v-if="!hasData && !loading" class="text-sm text-surface-500">
      Select a category and click "Update Data" to see parameter values grouped by value.
    </div>
  </div>
</template>
