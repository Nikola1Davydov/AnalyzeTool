<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { useElementsStore } from "@/stores/useElementsStore";
import { useCategoriesStore } from "@/stores/useCategoriesStore";

const TopFiltersBar = defineAsyncComponent(() => import("@/components/TopFiltersBar.vue") as any);
const ValueChart = defineAsyncComponent(
  () => import("@/view/ParameterValueCheck/Chart.vue") as any,
);

const elementsStore = useElementsStore();
const categoriesStore = useCategoriesStore();

const { items } = storeToRefs(elementsStore);
const { sortedCategories } = storeToRefs(categoriesStore);

const selectedCategory = ref<string | null>(null);
const selectedFilters = ref<string[]>([]);
const searchQuery = ref("");
const selectedLevel = ref<string | null>(null);
const clickAction = ref("Selection");
const loading = ref(false);

const filterParameter = ["Instance", "Type", "BuildIn", "Schared", "Project"];

const levels = computed(() => {
  const set = new Set<string>();
  for (const el of items.value || []) {
    if (el?.level) {
      set.add(String(el.level));
      continue;
    }
    if (el?.parameters && Array.isArray(el.parameters)) {
      for (const p of el.parameters) {
        if (p?.level) set.add(String(p.level));
      }
    }
  }
  return Array.from(set).sort();
});

onMounted(() => {
  categoriesStore.loadCategories().catch((err) => {
    console.error("Failed to load categories", err);
  });
});

const filteredItems = computed(() => {
  if (!selectedLevel.value) return items.value;
  const level = selectedLevel.value;
  return (items.value || []).filter((el) => {
    const elLevel = el?.level ?? el?.parameters?.[0]?.level ?? null;
    return elLevel === level;
  });
});

const hasData = computed(() => (filteredItems.value?.length ?? 0) > 0);

async function updateData() {
  loading.value = true;
  try {
    if (selectedCategory.value) {
      await elementsStore.loadByCategory(selectedCategory.value, true);
    } else {
      elementsStore.clear();
    }
  } catch (err) {
    console.error("Failed to load elements", err);
  } finally {
    loading.value = false;
  }
}

function onUpdateCategory(value: string | null) {
  selectedCategory.value = value;
}

function onUpdateFilters(value: string[]) {
  selectedFilters.value = value;
}

function onUpdateSearch(value: string) {
  searchQuery.value = value;
}

function onUpdateLevel(value: string | null) {
  selectedLevel.value = value;
}

function onUpdateClickAction(value: string | null) {
  clickAction.value = value || "Selection";
}

watch(
  () => items.value,
  () => {
    // Stop spinner once store updates with new data
    if (loading.value) loading.value = false;
  },
);
</script>

<template>
  <div class="p-5 flex flex-col gap-4">
    <TopFiltersBar
      :categories="sortedCategories"
      :category="selectedCategory"
      :filters="selectedFilters"
      :filterOptions="filterParameter"
      :search="searchQuery"
      :levels="levels"
      :level="selectedLevel"
      :showLevel="true"
      :clickAction="clickAction"
      :showClickAction="true"
      :loading="loading"
      @update:category="onUpdateCategory"
      @update:filters="onUpdateFilters"
      @update:search="onUpdateSearch"
      @update:level="onUpdateLevel"
      @update:clickAction="onUpdateClickAction"
      @update-data="updateData"
    />

    <ValueChart
      :items="filteredItems"
      :filters="selectedFilters"
      :search="searchQuery"
      :clickAction="clickAction"
    />

    <div v-if="!hasData && !loading" class="text-sm text-surface-500">
      Select a category and click "Update Data" to see parameter values grouped by value.
    </div>
  </div>
</template>
