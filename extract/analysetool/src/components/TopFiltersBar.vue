<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "TopFiltersBar",
});
</script>

<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from "vue";

const TopFiltersPopup = defineAsyncComponent(
  () => import("@/components/TopFiltersPopup.vue") as any,
);

const props = withDefaults(
  defineProps<{
    categories: string[];
    category: string | null;
    search: string;
    filters: string[];
    filterOptions: string[];
    loading: boolean;
    levels?: string[];
    level?: string | null;
    showLevel?: boolean;
    clickAction?: string;
    clickActionOptions?: string[];
    showClickAction?: boolean;
    updateButtonLabel?: string;
    categoryPlaceholder?: string;
    searchPlaceholder?: string;
  }>(),
  {
    levels: () => [],
    level: null,
    showLevel: false,
    clickAction: "Selection",
    clickActionOptions: () => ["Selection", "Isolation"],
    showClickAction: false,
    updateButtonLabel: "Update Data",
    categoryPlaceholder: "Select category",
    searchPlaceholder: "Search parameters",
  },
);

const emit = defineEmits<{
  "update:category": [value: string | null];
  "update:search": [value: string];
  "update:filters": [value: string[]];
  "update:level": [value: string | null];
  "update:clickAction": [value: string | null];
  "update-data": [];
}>();

function onUpdateDataClick() {
  emit("update-data");
}

function onUpdateSearch(value: string | null | undefined) {
  emit("update:search", value || "");
}

function onUpdateFilters(value: string[] | null | undefined) {
  emit("update:filters", value || []);
}

function onUpdateLevel(value: string | null | undefined) {
  emit("update:level", value || null);
}

function onUpdateClickAction(value: string | null | undefined) {
  emit("update:clickAction", value || null);
}

function onUpdateDraftFilters(value: string[] | null | undefined) {
  draftFilters.value = value || [];
}

function onUpdateDraftLevel(value: string | null | undefined) {
  draftLevel.value = value || null;
}

function onUpdateDraftClickAction(value: string | null | undefined) {
  draftClickAction.value = value || null;
}

const showFiltersPopup = ref(false);
const draftFilters = ref<string[]>([]);
const draftLevel = ref<string | null>(null);
const draftClickAction = ref<string | null>(null);

const hasLevelApplied = computed(() => props.showLevel && !!props.level);
const hasClickActionApplied = computed(
  () => props.showClickAction && !!props.clickAction && props.clickAction !== "Selection",
);

const appliedCount = computed(() => {
  let count = props.filters.length;
  if (hasLevelApplied.value) count += 1;
  if (hasClickActionApplied.value) count += 1;
  return count;
});

watch(
  () => [props.filters, props.level, props.clickAction],
  () => {
    if (showFiltersPopup.value) return;
    draftFilters.value = [...props.filters];
    draftLevel.value = props.level ?? null;
    draftClickAction.value = props.clickAction ?? null;
  },
  { immediate: true, deep: true },
);

function openFiltersPopup() {
  draftFilters.value = [...props.filters];
  draftLevel.value = props.level ?? null;
  draftClickAction.value = props.clickAction ?? null;
  showFiltersPopup.value = true;
}

function closeFiltersPopup() {
  showFiltersPopup.value = false;
}

function applyFiltersFromPopup() {
  emit("update:filters", draftFilters.value);
  if (props.showLevel) emit("update:level", draftLevel.value);
  if (props.showClickAction) emit("update:clickAction", draftClickAction.value);
  showFiltersPopup.value = false;
}

function clearDraftFilters() {
  draftFilters.value = [];
  draftLevel.value = null;
  if (props.showClickAction) {
    draftClickAction.value = "Selection";
  }
}

function removeFilterChip(filterValue: string) {
  const next = props.filters.filter((x) => x !== filterValue);
  emit("update:filters", next);
}

function clearLevelChip() {
  emit("update:level", null);
}

function clearClickActionChip() {
  emit("update:clickAction", "Selection");
}
</script>

<template>
  <header class="card flex flex-col gap-3">
    <div class="flex flex-row items-start lg:items-center w-full gap-3 flex-wrap lg:flex-nowrap">
      <Select
        class="min-w-[180px] flex-1 w-full"
        :options="props.categories"
        :placeholder="props.categoryPlaceholder"
        :modelValue="props.category"
        @update:modelValue="(val) => emit('update:category', val)"
      />

      <IconField class="flex-1 min-w-[220px] w-full">
        <InputIcon class="pi pi-search" />
        <InputText
          :placeholder="props.searchPlaceholder"
          class="min-w-[40px] shrink w-full"
          :modelValue="props.search"
          @update:modelValue="onUpdateSearch"
        />
      </IconField>

      <div class="relative flex-none w-full lg:w-auto">
        <Button
          class="w-full lg:w-auto"
          icon="pi pi-filter"
          :label="appliedCount > 0 ? `Filters (${appliedCount})` : 'Filters'"
          outlined
          @click="openFiltersPopup"
        />

        <TopFiltersPopup
          :visible="showFiltersPopup"
          :draftFilters="draftFilters"
          :filterOptions="props.filterOptions"
          :showLevel="props.showLevel"
          :levels="props.levels"
          :draftLevel="draftLevel"
          :showClickAction="props.showClickAction"
          :clickActionOptions="props.clickActionOptions"
          :draftClickAction="draftClickAction"
          @update:draftFilters="onUpdateDraftFilters"
          @update:draftLevel="onUpdateDraftLevel"
          @update:draftClickAction="onUpdateDraftClickAction"
          @clear="clearDraftFilters"
          @cancel="closeFiltersPopup"
          @apply="applyFiltersFromPopup"
        />
      </div>

      <Button
        class="flex-none w-full lg:w-auto"
        icon="pi pi-sync"
        :label="props.updateButtonLabel"
        :loading="props.loading"
        :disabled="props.loading"
        @click="onUpdateDataClick"
      />
    </div>

    <div class="flex flex-wrap gap-2" v-if="appliedCount > 0">
      <div
        v-for="filter in props.filters"
        :key="`filter-${filter}`"
        class="inline-flex items-center gap-2 rounded-full border border-surface bg-surface px-3 py-1 text-xs"
      >
        <span>{{ filter }}</span>
        <button
          type="button"
          class="pi pi-times text-xs cursor-pointer"
          @click="removeFilterChip(filter)"
          aria-label="Remove filter"
        />
      </div>

      <div
        v-if="hasLevelApplied"
        class="inline-flex items-center gap-2 rounded-full border border-surface bg-surface px-3 py-1 text-xs"
      >
        <span>Level: {{ props.level }}</span>
        <button
          type="button"
          class="pi pi-times text-xs cursor-pointer"
          @click="clearLevelChip"
          aria-label="Remove level"
        />
      </div>

      <div
        v-if="hasClickActionApplied"
        class="inline-flex items-center gap-2 rounded-full border border-surface bg-surface px-3 py-1 text-xs"
      >
        <span>Action: {{ props.clickAction }}</span>
        <button
          type="button"
          class="pi pi-times text-xs cursor-pointer"
          @click="clearClickActionChip"
          aria-label="Remove click action"
        />
      </div>
    </div>
  </header>
</template>
