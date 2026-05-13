<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import { useElementsStore } from "@/stores/useElementsStore";
import { useDocumentDataStore } from "@/stores/useDocumentDataStore";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import { Commands, sendRequest } from "@/RevitBridge";
import type { ElementItem } from "@/stores/types";
import { useBulkGenerator } from "./composables/useBulkGenerator";
import { useCardCreator } from "./composables/useCardCreator";
import { useCardActions } from "./composables/useCardActions";
import {
  useCanvasPersistence,
  type PersistedViewportState,
} from "./composables/useCanvasPersistence";

const InfiniteCanvas = defineAsyncComponent(() => import("./components/InfiniteCanvas.vue") as any);
const CanvasCard = defineAsyncComponent(() => import("./components/CanvasCard.vue") as any);
const ValueChart = defineAsyncComponent(() => import("./components/BarChart.vue") as any);
const BodyTable = defineAsyncComponent(() => import("./components/DataTable.vue") as any);
const SettingsView = defineAsyncComponent(() => import("./components/SettingsView.vue") as any);
const ToolbarControls = defineAsyncComponent(
  () => import("./components/ToolbarControls.vue") as any,
);
const CreateCardPanel = defineAsyncComponent(
  () => import("./components/CreateCardPanel.vue") as any,
);
const BulkGeneratorDrawer = defineAsyncComponent(
  () => import("./components/BulkGeneratorDrawer.vue") as any,
);

type CardViewType = "chart" | "table";

type CanvasCardConfig = {
  id: number;
  category: string;
  parameter: string;
  viewType: CardViewType;
  x: number;
  y: number;
  width: number;
  height: number;
  loading: boolean;
  error: string | null;
};

type CanvasSelectionRect = {
  x: number;
  y: number;
  width: number;
  height: number;
};

const categoriesStore = useCategoriesStore();
const elementsStore = useElementsStore();
const documentDataStore = useDocumentDataStore();
const aiSettingsStore = useAiSettingsStore();
const notificationStore = useNotificationStore();
const { sortedCategories } = storeToRefs(categoriesStore);
const { items } = storeToRefs(elementsStore);
const { documentId, documentName } = storeToRefs(documentDataStore);

const cards = ref<CanvasCardConfig[]>([]);
const selectedCardIds = ref<number[]>([]);
const nextCardId = ref(1);
const showCreatePanel = ref(false);
const showAiSettings = ref(false);
const refreshingAll = ref(false);
const categorySnapshots = ref<Record<string, ElementItem[]>>({});
const chartActionOptions = [
  { label: "Select", value: "SelectionInRevit" },
  { label: "Isolate", value: "IsolationInRevit" },
];
const selectedChartActionCommand = ref("SelectionInRevit");
const hasSelectedCards = computed(() => selectedCardIds.value.length > 0);
const selectedCardIdSet = computed(() => new Set(selectedCardIds.value));
const {
  showGeneratorDrawer,
  generatingFromDrawer,
  generationInfo,
  generationRows,
  canGenerateFromDrawer,
  addGenerationRow,
  removeGenerationRow,
  updateGenerationRowParameter,
  updateGenerationRowCategory,
  updateGenerationRowAllCategories,
  generateCardsFromDrawer,
} = useBulkGenerator({
  sortedCategories,
  cards,
  nextCardId,
  categorySnapshots,
  loadCategorySnapshot,
  refreshCategoryForCards,
});
const {
  draft,
  draftCategoryLoading,
  draftCategoryError,
  viewTypeOptions,
  availableParameters,
  canCreateCard,
  onDraftCategoryChange,
  createCard,
} = useCardCreator({
  showCreatePanel,
  cards,
  nextCardId,
  categorySnapshots,
  loadCategorySnapshot,
  refreshCategoryForCards,
});
const {
  updateCardLayout,
  removeCard,
  removeSelectedCards,
  removeAllCards,
  refreshCard,
  refreshAllCards,
  getCardItems,
} = useCardActions({
  cards,
  selectedCardIds,
  nextCardId,
  refreshingAll,
  categorySnapshots,
  refreshCategoryForCards,
  loadDocumentData: () =>
    documentDataStore.loadDocumentData().catch((err) => {
      console.error("Failed to refresh document context", err);
    }),
});
const { viewportState, projectScope, restoreCanvasState } = useCanvasPersistence({
  cards,
  nextCardId,
  selectedCardIds,
  categorySnapshots,
  selectedChartActionCommand,
  documentId,
  documentName,
  storageKeyPrefix: "infinite-canvas-state-v1",
});

function cloneElements(source: ElementItem[]): ElementItem[] {
  function toStr(value: unknown): string {
    return value === null || value === undefined ? "" : String(value);
  }

  function toNum(value: unknown): number {
    const n = Number(value);
    return Number.isFinite(n) ? n : 0;
  }

  return source.map((element) => ({
    ...element,
    name: toStr((element as any).name ?? (element as any).Name),
    id: toNum((element as any).id ?? (element as any).Id),
    level: toStr((element as any).level ?? (element as any).Level),
    categoryName: toStr((element as any).categoryName ?? (element as any).CategoryName),
    isElementType: Boolean((element as any).isElementType ?? (element as any).IsElementType),
    elementTypeId: toNum((element as any).elementTypeId ?? (element as any).ElementTypeId),
    parameters: (((element as any).parameters ?? (element as any).Parameters ?? []) as any[]).map(
      (p) => ({
        ...p,
        name: toStr(p?.name ?? p?.Name),
        id: toNum(p?.id ?? p?.Id),
        value: toStr(p?.value ?? p?.Value),
        level: toStr(p?.level ?? p?.Level),
        elementId: toNum(p?.elementId ?? p?.ElementId),
        isTypeParameter: Boolean(p?.isTypeParameter ?? p?.IsTypeParameter),
        orgin: toStr(p?.orgin ?? p?.Orgin),
        storageType: toStr(p?.storageType ?? p?.StorageType),
        isReadOnly: Boolean(p?.isReadOnly ?? p?.IsReadOnly),
      }),
    ),
  }));
}

function hasExpectedCategory(list: ElementItem[], expectedCategory: string): boolean {
  if (!Array.isArray(list) || list.length === 0) return true;

  const known = list
    .map((el) => String((el as any).categoryName ?? "").trim())
    .filter((x) => x.length > 0);

  if (known.length === 0) return true;
  return known.every((x) => x === expectedCategory);
}

function warmCardsCategories() {
  const categoriesToWarm = Array.from(new Set(cards.value.map((card) => card.category)));
  for (const category of categoriesToWarm) {
    loadCategorySnapshot(category, true).catch((err) => {
      console.error("Failed to warm category snapshot", err);
    });
  }
}

function intersectsSelection(card: CanvasCardConfig, rect: CanvasSelectionRect): boolean {
  return !(
    card.x + card.width < rect.x ||
    rect.x + rect.width < card.x ||
    card.y + card.height < rect.y ||
    rect.y + rect.height < card.y
  );
}

function syncSelectedCards(rect: CanvasSelectionRect) {
  selectedCardIds.value = cards.value
    .filter((card) => intersectsSelection(card, rect))
    .map((card) => card.id);
}

function onCanvasSelectionChange(rect: CanvasSelectionRect) {
  syncSelectedCards(rect);
}

function onCanvasSelectionEnd(rect: CanvasSelectionRect | null) {
  if (!rect) {
    selectedCardIds.value = [];
    return;
  }

  syncSelectedCards(rect);
}

function onViewportChange(state: PersistedViewportState) {
  viewportState.value = state;
}

function waitForItemsUpdate(expectedCategory: string, timeoutMs = 3000): Promise<void> {
  return new Promise((resolve) => {
    let done = false;

    const stop = watch(
      () => items.value,
      (next) => {
        const list = cloneElements((next || []) as ElementItem[]);
        const isExpectedCategory = hasExpectedCategory(list, expectedCategory);
        if (!isExpectedCategory) return;

        if (done) return;
        done = true;
        stop();
        clearTimeout(timer);
        resolve();
      },
      { deep: false },
    );

    const timer = setTimeout(() => {
      if (done) return;
      done = true;
      stop();
      resolve();
    }, timeoutMs);
  });
}

async function loadCategorySnapshot(category: string, force = false): Promise<ElementItem[]> {
  const existing = categorySnapshots.value[category];
  if (!force && existing) return existing;

  const waitPromise = waitForItemsUpdate(category);
  const loadPromise = elementsStore.loadByCategory(category, true).catch((err) => {
    console.error("Failed to request category data", err);
  });
  await waitPromise;
  await Promise.race([loadPromise, Promise.resolve()]);

  const snapshot = cloneElements(items.value || []);
  if (!hasExpectedCategory(snapshot, category)) {
    if (existing) return existing;
    throw new Error("Received stale data for another category.");
  }

  categorySnapshots.value = {
    ...categorySnapshots.value,
    [category]: snapshot,
  };
  return snapshot;
}

async function refreshCategoryForCards(category: string, force = true) {
  const relatedIds = cards.value.filter((c) => c.category === category).map((c) => c.id);
  cards.value = cards.value.map((card) =>
    relatedIds.includes(card.id)
      ? {
          ...card,
          loading: true,
          error: null,
        }
      : card,
  );

  try {
    await loadCategorySnapshot(category, force);
    cards.value = cards.value.map((card) =>
      relatedIds.includes(card.id)
        ? {
            ...card,
            loading: false,
            error: null,
          }
        : card,
    );
  } catch (err) {
    cards.value = cards.value.map((card) =>
      relatedIds.includes(card.id)
        ? {
            ...card,
            loading: false,
            error: "Failed to refresh category data.",
          }
        : card,
    );
  }
}

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;

  const tagName = target.tagName;
  return (
    target.isContentEditable ||
    tagName === "INPUT" ||
    tagName === "TEXTAREA" ||
    tagName === "SELECT"
  );
}

function onWindowKeyDown(e: KeyboardEvent) {
  if (isEditableTarget(e.target)) return;

  if (e.key === "Escape") {
    if (!selectedCardIds.value.length) return;
    selectedCardIds.value = [];
    e.preventDefault();
    return;
  }

  if (e.key !== "Delete" && e.key !== "Backspace") return;
  if (!selectedCardIds.value.length) return;

  e.preventDefault();
  removeSelectedCards();
}

function requestOllamaModels() {
  aiSettingsStore.startLoadingModels();
  sendRequest(Commands.GetOllamaModels, null);
}

function checkAiAvailabilityOnCanvasEnter() {
  if (aiSettingsStore.modelSource !== "local") return;

  requestOllamaModels();

  const stop = watch(
    () => aiSettingsStore.modelsLoading,
    (isLoading) => {
      if (isLoading) return;
      stop();

      if (aiSettingsStore.availableModels.length === 0) {
        aiSettingsStore.setModel(null);
        notificationStore.warn(
          "Ollama is unavailable or no models are installed. AI features were disabled.",
        );
      }
    },
    { immediate: true },
  );
}

onMounted(() => {
  restoreCanvasState();
  window.addEventListener("keydown", onWindowKeyDown);

  categoriesStore.loadCategories().catch((err) => {
    console.error("Failed to load categories", err);
  });

  checkAiAvailabilityOnCanvasEnter();
  warmCardsCategories();
});

onBeforeUnmount(() => {
  window.removeEventListener("keydown", onWindowKeyDown);
});

watch(projectScope, (nextScope, prevScope) => {
  if (nextScope === prevScope) return;
  restoreCanvasState();
  warmCardsCategories();
});
</script>

<template>
  <InfiniteCanvas
    :initialZoom="viewportState.zoom"
    :initialPanX="viewportState.panX"
    :initialPanY="viewportState.panY"
    @viewportChange="onViewportChange"
    @selectionChange="onCanvasSelectionChange"
    @selectionEnd="onCanvasSelectionEnd"
  >
    <CanvasCard
      v-for="card in cards"
      :key="card.id"
      :title="`${card.category} • ${card.parameter}`"
      :initialX="card.x"
      :initialY="card.y"
      :initialWidth="card.width"
      :initialHeight="card.height"
      :selected="selectedCardIdSet.has(card.id)"
      :closable="true"
      :refreshable="true"
      :refreshing="card.loading"
      @close="removeCard(card.id)"
      @refresh="refreshCard(card.id)"
      @layoutChange="updateCardLayout(card.id, $event)"
    >
      <!-- Initial load: no data yet -->
      <div v-if="card.loading && getCardItems(card).length === 0" class="card-state">
        Loading category data...
      </div>
      <div v-else-if="card.error" class="card-state text-red-600">{{ card.error }}</div>
      <div v-else-if="!card.loading && getCardItems(card).length === 0" class="card-state">
        No data for this category.
      </div>

      <ValueChart
        v-else-if="card.viewType === 'chart'"
        :items="getCardItems(card)"
        :selectedParameter="card.parameter"
        :actionCommand="selectedChartActionCommand"
      />

      <!-- Table stays mounted during refresh — only props.items updates -->
      <BodyTable
        v-else-if="card.viewType === 'table'"
        :items="getCardItems(card)"
        :selectedParameter="card.parameter"
        @refresh="refreshCard(card.id)"
      />
      <div v-else class="card-state text-surface-400">Unbekannter Kartentyp.</div>
    </CanvasCard>

    <template #toolbar>
      <div class="toolbar">
        <ToolbarControls
          :chartActionOptions="chartActionOptions"
          :chartAction="selectedChartActionCommand"
          :refreshingAll="refreshingAll"
          :hasSelectedCards="hasSelectedCards"
          :hasCards="cards.length > 0"
          @update:chartAction="selectedChartActionCommand = $event"
          @toggleCreate="showCreatePanel = !showCreatePanel"
          @openGenerator="showGeneratorDrawer = true"
          @refreshAll="refreshAllCards"
          @removeSelected="removeSelectedCards"
          @removeAll="removeAllCards"
          @openSettings="showAiSettings = true"
        />

        <CreateCardPanel
          :visible="showCreatePanel"
          :sortedCategories="sortedCategories"
          :availableParameters="availableParameters"
          :draftCategory="draft.category"
          :draftParameter="draft.parameter"
          :draftViewType="draft.viewType"
          :viewTypeOptions="viewTypeOptions"
          :draftCategoryLoading="draftCategoryLoading"
          :draftCategoryError="draftCategoryError"
          :canCreateCard="canCreateCard"
          @update:category="onDraftCategoryChange"
          @update:parameter="draft.parameter = $event"
          @update:viewType="draft.viewType = $event || 'chart'"
          @close="showCreatePanel = false"
          @create="createCard"
        />
      </div>
    </template>
  </InfiniteCanvas>

  <BulkGeneratorDrawer
    :visible="showGeneratorDrawer"
    :generationRows="generationRows"
    :sortedCategories="sortedCategories"
    :canGenerateFromDrawer="canGenerateFromDrawer"
    :generatingFromDrawer="generatingFromDrawer"
    :generationInfo="generationInfo"
    @update:visible="showGeneratorDrawer = $event"
    @addRow="addGenerationRow"
    @removeRow="removeGenerationRow"
    @update:parameter="updateGenerationRowParameter($event.rowId, $event.value)"
    @update:category="updateGenerationRowCategory($event.rowId, $event.value)"
    @update:allCategories="updateGenerationRowAllCategories($event.rowId, $event.value)"
    @startGeneration="generateCardsFromDrawer"
  />

  <SettingsView v-model:visible="showAiSettings" />
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.card-state {
  font-size: 0.82rem;
  color: var(--p-surface-600, #475569);
}
</style>
