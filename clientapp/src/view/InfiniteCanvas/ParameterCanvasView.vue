<script setup lang="ts">
import {
  computed,
  defineAsyncComponent,
  onBeforeUnmount,
  onMounted,
  reactive,
  ref,
  watch,
} from "vue";
import { storeToRefs } from "pinia";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import { useElementsStore } from "@/stores/useElementsStore";
import { useDocumentDataStore } from "@/stores/useDocumentDataStore";
import type { ElementItem } from "@/stores/types";

const InfiniteCanvas = defineAsyncComponent(() => import("./components/InfiniteCanvas.vue") as any);
const CanvasCard = defineAsyncComponent(() => import("./components/CanvasCard.vue") as any);
const ValueChart = defineAsyncComponent(() => import("./components/BarChart.vue") as any);
const BodyTable = defineAsyncComponent(() => import("./components/DataTable.vue") as any);

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

type PersistedCanvasCard = {
  id: number;
  category: string;
  parameter: string;
  viewType: CardViewType;
  x: number;
  y: number;
  width: number;
  height: number;
};

type PersistedViewportState = {
  zoom: number;
  panX: number;
  panY: number;
};

type PersistedChartActionState = {
  command: string;
};

type CanvasSelectionRect = {
  x: number;
  y: number;
  width: number;
  height: number;
};

type GeneratorDraftRow = {
  id: number;
  parameter: string;
  category: string | null;
  useAllCategories: boolean;
};

const CANVAS_STATE_STORAGE_KEY_PREFIX = "infinite-canvas-state-v1";

const categoriesStore = useCategoriesStore();
const elementsStore = useElementsStore();
const documentDataStore = useDocumentDataStore();
const { sortedCategories } = storeToRefs(categoriesStore);
const { items } = storeToRefs(elementsStore);
const { documentId, documentName } = storeToRefs(documentDataStore);

const cards = ref<CanvasCardConfig[]>([]);
const selectedCardIds = ref<number[]>([]);
const nextCardId = ref(1);
const showCreatePanel = ref(false);
const showGeneratorDrawer = ref(false);
const generatingFromDrawer = ref(false);
const generationInfo = ref("");
const refreshingAll = ref(false);
const categorySnapshots = ref<Record<string, ElementItem[]>>({});
const draftCategoryLoading = ref(false);
const draftCategoryError = ref("");
const generationRows = ref<GeneratorDraftRow[]>([
  {
    id: 1,
    parameter: "",
    category: null,
    useAllCategories: false,
  },
]);
const nextGenerationRowId = ref(2);
const chartActionOptions = [
  { label: "Select", value: "SelectionInRevit" },
  { label: "Isolate", value: "IsolationInRevit" },
];
const selectedChartActionCommand = ref("SelectionInRevit");

const draft = reactive<{
  category: string | null;
  parameter: string | null;
  viewType: CardViewType;
}>({
  category: null,
  parameter: null,
  viewType: "chart",
});

const viewTypeOptions: { label: string; value: CardViewType }[] = [
  { label: "Chart", value: "chart" },
  { label: "Table", value: "table" },
];

const availableParameters = computed(() => {
  const category = draft.category;
  if (!category) return [];

  const categoryItems = categorySnapshots.value[category] || [];
  const set = new Set<string>();
  for (const element of categoryItems) {
    for (const param of element.parameters || []) {
      if (param?.name) set.add(String(param.name));
    }
  }
  return Array.from(set).sort((a, b) => a.localeCompare(b));
});

const canCreateCard = computed(() => !!draft.category && !!draft.parameter);
const hasSelectedCards = computed(() => selectedCardIds.value.length > 0);
const selectedCardIdSet = computed(() => new Set(selectedCardIds.value));
const canGenerateFromDrawer = computed(() => {
  return generationRows.value.some((row) => {
    const hasParameter = row.parameter.trim().length > 0;
    if (!hasParameter) return false;
    if (row.useAllCategories) return sortedCategories.value.length > 0;
    return !!row.category;
  });
});

let persistTimer: number | null = null;
const activeStorageKey = ref("");
const suppressPersist = ref(false);
const viewportState = ref<PersistedViewportState>({
  zoom: 1,
  panX: 0,
  panY: 0,
});

const projectScope = computed(() => {
  const id = String(documentId.value || "").trim();
  if (id) return `id:${id}`;

  const name = String(documentName.value || "")
    .trim()
    .toLowerCase();
  if (name) return `name:${name}`;

  return "unknown-project";
});

function getCanvasStorageKey(): string {
  return `${CANVAS_STATE_STORAGE_KEY_PREFIX}::${projectScope.value}`;
}

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

function toPersistedCards(source: CanvasCardConfig[]): PersistedCanvasCard[] {
  return source.map((card) => ({
    id: card.id,
    category: card.category,
    parameter: card.parameter,
    viewType: card.viewType,
    x: Number(card.x) || 0,
    y: Number(card.y) || 0,
    width: Number(card.width) || 560,
    height: Number(card.height) || 420,
  }));
}

function applyPersistedCards(source: PersistedCanvasCard[]): CanvasCardConfig[] {
  return source
    .filter(
      (card) =>
        !!card &&
        typeof card.id === "number" &&
        typeof card.category === "string" &&
        typeof card.parameter === "string" &&
        (card.viewType === "chart" || card.viewType === "table"),
    )
    .map((card) => ({
      id: card.id,
      category: card.category,
      parameter: card.parameter,
      viewType: card.viewType,
      x: Number(card.x) || 0,
      y: Number(card.y) || 0,
      width: Math.max(320, Number(card.width) || (card.viewType === "table" ? 700 : 560)),
      height: Math.max(220, Number(card.height) || (card.viewType === "table" ? 520 : 420)),
      loading: false,
      error: null,
    }));
}

function persistCanvasState() {
  if (typeof localStorage === "undefined") return;
  if (suppressPersist.value) return;

  const storageKey = activeStorageKey.value || getCanvasStorageKey();

  const payload = {
    cards: toPersistedCards(cards.value),
    nextCardId: nextCardId.value,
    viewport: viewportState.value,
    chartAction: { command: selectedChartActionCommand.value },
  };

  try {
    localStorage.setItem(storageKey, JSON.stringify(payload));
  } catch (err) {
    console.error("Failed to persist canvas state", err);
  }
}

function queuePersistCanvasState() {
  if (persistTimer !== null) window.clearTimeout(persistTimer);
  persistTimer = window.setTimeout(() => {
    persistTimer = null;
    persistCanvasState();
  }, 120);
}

function restoreCanvasState() {
  if (typeof localStorage === "undefined") return;

  const storageKey = getCanvasStorageKey();
  activeStorageKey.value = storageKey;
  suppressPersist.value = true;
  selectedCardIds.value = [];

  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) {
      cards.value = [];
      nextCardId.value = 1;
      categorySnapshots.value = {};
      viewportState.value = { zoom: 1, panX: 0, panY: 0 };
      selectedChartActionCommand.value = "SelectionInRevit";
      return;
    }

    const parsed = JSON.parse(raw) as {
      cards?: PersistedCanvasCard[];
      nextCardId?: number;
      viewport?: PersistedViewportState;
      chartAction?: PersistedChartActionState;
    };

    const restoredCards = applyPersistedCards(parsed.cards || []);
    cards.value = restoredCards;

    const maxId = restoredCards.reduce((max, card) => Math.max(max, card.id), 0);
    const savedNext = Number(parsed.nextCardId) || 0;
    nextCardId.value = Math.max(maxId + 1, savedNext, 1);

    if (parsed.viewport && typeof parsed.viewport === "object") {
      const nextZoom = Number((parsed.viewport as any).zoom);
      const nextPanX = Number((parsed.viewport as any).panX);
      const nextPanY = Number((parsed.viewport as any).panY);

      viewportState.value = {
        zoom: Number.isFinite(nextZoom) ? Math.min(3, Math.max(0.2, nextZoom)) : 1,
        panX: Number.isFinite(nextPanX) ? nextPanX : 0,
        panY: Number.isFinite(nextPanY) ? nextPanY : 0,
      };
    }

    if (parsed.chartAction && typeof parsed.chartAction === "object") {
      const nextCommand = String((parsed.chartAction as any).command || "").trim();
      if (nextCommand) selectedChartActionCommand.value = nextCommand;
    }
  } catch (err) {
    console.error("Failed to restore canvas state", err);
    cards.value = [];
    nextCardId.value = 1;
    categorySnapshots.value = {};
    viewportState.value = { zoom: 1, panX: 0, panY: 0 };
    selectedChartActionCommand.value = "SelectionInRevit";
  } finally {
    suppressPersist.value = false;
  }
}

function warmCardsCategories() {
  const categoriesToWarm = Array.from(new Set(cards.value.map((card) => card.category)));
  for (const category of categoriesToWarm) {
    loadCategorySnapshot(category, true).catch((err) => {
      console.error("Failed to warm category snapshot", err);
    });
  }
}

function updateCardLayout(
  cardId: number,
  layout: { x: number; y: number; width: number; height: number },
) {
  const target = cards.value.find((card) => card.id === cardId);
  if (!target) return;

  target.x = layout.x;
  target.y = layout.y;
  target.width = layout.width;
  target.height = layout.height;
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

function addGenerationRow() {
  generationRows.value.push({
    id: nextGenerationRowId.value,
    parameter: "",
    category: null,
    useAllCategories: false,
  });
  nextGenerationRowId.value += 1;
}

function removeGenerationRow(rowId: number) {
  if (generationRows.value.length === 1) {
    generationRows.value[0] = {
      ...generationRows.value[0],
      parameter: "",
      category: null,
      useAllCategories: false,
    };
    return;
  }

  generationRows.value = generationRows.value.filter((row) => row.id !== rowId);
}

function onGeneratorRowAllCategoriesChange(row: GeneratorDraftRow, nextValue: boolean) {
  row.useAllCategories = nextValue;
  if (nextValue) row.category = null;
}

function buildGenerationPairs(): { parameter: string; category: string }[] {
  const pairs: { parameter: string; category: string }[] = [];

  for (const row of generationRows.value) {
    const parameter = row.parameter.trim();
    if (!parameter) continue;

    const categories = row.useAllCategories
      ? sortedCategories.value
      : row.category
        ? [row.category]
        : [];

    for (const category of categories) {
      if (!category) continue;
      pairs.push({ parameter, category });
    }
  }

  return pairs;
}

function hasParameterDataInCategory(category: string, parameter: string): boolean {
  const normalizedParameter = parameter.trim().toLowerCase();
  if (!normalizedParameter) return false;

  const snapshot = categorySnapshots.value[category] || [];
  return snapshot.some((element) =>
    (element.parameters || []).some(
      (param) =>
        String(param?.name || "")
          .trim()
          .toLowerCase() === normalizedParameter,
    ),
  );
}

function getPairKey(category: string, parameter: string): string {
  return `${String(category || "")
    .trim()
    .toLowerCase()}|${String(parameter || "")
    .trim()
    .toLowerCase()}`;
}

async function generateCardsFromDrawer() {
  generationInfo.value = "";
  const pairs = buildGenerationPairs();
  if (pairs.length === 0) return;

  generatingFromDrawer.value = true;
  try {
    const categoriesToLoad = Array.from(new Set(pairs.map((pair) => pair.category)));

    for (const category of categoriesToLoad) {
      await loadCategorySnapshot(category, true).catch((err) => {
        console.error("Failed to load category before bulk generation", err);
      });
    }

    const existingPairKeys = new Set(
      cards.value.map((card) => getPairKey(card.category, card.parameter)),
    );

    const acceptedPairKeys = new Set<string>();
    const validPairs = pairs.filter((pair) => {
      const key = getPairKey(pair.category, pair.parameter);
      if (existingPairKeys.has(key)) return false;
      if (acceptedPairKeys.has(key)) return false;
      if (!hasParameterDataInCategory(pair.category, pair.parameter)) return false;

      acceptedPairKeys.add(key);
      return true;
    });

    const skippedCount = pairs.length - validPairs.length;
    if (validPairs.length === 0) {
      generationInfo.value = "Nothing was generated: no data or pairs already exist.";
      return;
    }

    const startY = cards.value.length
      ? Math.max(...cards.value.map((card) => card.y + card.height)) + 28
      : 80;

    const newCards: CanvasCardConfig[] = [];
    const baseX = 90;
    const chartWidth = 560;
    const chartHeight = 420;
    const tableWidth = 700;
    const tableHeight = 520;
    const pairGapX = 24;
    const pairGapY = 28;
    const tableX = baseX + chartWidth + pairGapX;
    let cursorY = startY;

    for (const pair of validPairs) {
      const chartId = nextCardId.value;
      nextCardId.value += 1;
      newCards.push({
        id: chartId,
        category: pair.category,
        parameter: pair.parameter,
        viewType: "chart",
        x: baseX,
        y: cursorY,
        width: chartWidth,
        height: chartHeight,
        loading: true,
        error: null,
      });

      const tableId = nextCardId.value;
      nextCardId.value += 1;
      newCards.push({
        id: tableId,
        category: pair.category,
        parameter: pair.parameter,
        viewType: "table",
        x: tableX,
        y: cursorY,
        width: tableWidth,
        height: tableHeight,
        loading: true,
        error: null,
      });

      cursorY += Math.max(chartHeight, tableHeight) + pairGapY;
    }

    cards.value = [...cards.value, ...newCards];

    for (const category of categoriesToLoad) {
      await refreshCategoryForCards(category, true);
    }

    generationInfo.value =
      skippedCount > 0
        ? `Generated ${validPairs.length} row(s). Skipped ${skippedCount} row(s) (no data or already exists).`
        : `Generated ${validPairs.length} row(s).`;

    showGeneratorDrawer.value = false;
  } finally {
    generatingFromDrawer.value = false;
  }
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
  const loadPromise = elementsStore.loadByCategory(category).catch((err) => {
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

async function onDraftCategoryChange(value: string | null) {
  draft.category = value;
  draft.parameter = null;
  draftCategoryError.value = "";
  if (!value) return;

  draftCategoryLoading.value = true;
  try {
    await loadCategorySnapshot(value, true);
  } catch (err) {
    draftCategoryError.value = "Failed to load category parameters.";
    console.error("Failed to load category data for card draft", err);
  } finally {
    draftCategoryLoading.value = false;
  }
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

async function createCard() {
  if (!canCreateCard.value || !draft.category || !draft.parameter) return;

  const id = nextCardId.value;
  nextCardId.value += 1;

  cards.value.push({
    id,
    category: draft.category,
    parameter: draft.parameter,
    viewType: draft.viewType,
    x: 90 + (id % 4) * 80,
    y: 90 + (id % 3) * 60,
    width: draft.viewType === "table" ? 700 : 560,
    height: draft.viewType === "table" ? 520 : 420,
    loading: false,
    error: null,
  });

  showCreatePanel.value = false;
  await refreshCategoryForCards(draft.category, true);
}

function removeCard(cardId: number) {
  cards.value = cards.value.filter((x) => x.id !== cardId);
  selectedCardIds.value = selectedCardIds.value.filter((id) => id !== cardId);
}

function removeSelectedCards() {
  if (!selectedCardIds.value.length) return;

  const ids = new Set(selectedCardIds.value);
  cards.value = cards.value.filter((card) => !ids.has(card.id));
  selectedCardIds.value = [];
}

function removeAllCards() {
  const confirmed = window.confirm("Delete all generated cards?");
  if (!confirmed) return;

  cards.value = [];
  categorySnapshots.value = {};
  nextCardId.value = 1;
  selectedCardIds.value = [];
}

async function refreshCard(cardId: number) {
  const card = cards.value.find((x) => x.id === cardId);
  if (!card) return;
  await refreshCategoryForCards(card.category, true);
}

async function refreshAllCards() {
  // Explicitly request latest project context so project-scoped canvas can switch when needed.
  documentDataStore.loadDocumentData().catch((err) => {
    console.error("Failed to refresh document context", err);
  });

  const categories = Array.from(new Set(cards.value.map((c) => c.category)));
  if (categories.length === 0) return;

  refreshingAll.value = true;
  try {
    for (const category of categories) {
      await refreshCategoryForCards(category, true);
    }
  } finally {
    refreshingAll.value = false;
  }
}

function getCardItems(card: CanvasCardConfig): ElementItem[] {
  return categorySnapshots.value[card.category] || [];
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

onMounted(() => {
  restoreCanvasState();
  window.addEventListener("keydown", onWindowKeyDown);

  categoriesStore.loadCategories().catch((err) => {
    console.error("Failed to load categories", err);
  });

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

watch(
  () => cards.value,
  () => {
    queuePersistCanvasState();
  },
  { deep: true },
);

watch(nextCardId, () => {
  queuePersistCanvasState();
});

watch(
  () => viewportState.value,
  () => {
    queuePersistCanvasState();
  },
  { deep: true },
);

watch(selectedChartActionCommand, () => {
  queuePersistCanvasState();
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
      <div v-if="card.loading" class="card-state">Loading category data...</div>
      <div v-else-if="card.error" class="card-state text-red-600">{{ card.error }}</div>
      <div v-else-if="getCardItems(card).length === 0" class="card-state">
        No data for this category.
      </div>

      <ValueChart
        v-else-if="card.viewType === 'chart'"
        :items="getCardItems(card)"
        :selectedParameter="card.parameter"
        :actionCommand="selectedChartActionCommand"
      />

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
        <SelectButton
          class="chart-action-switch"
          :options="chartActionOptions"
          optionLabel="label"
          optionValue="value"
          :modelValue="selectedChartActionCommand"
          aria-label="Chart click action"
          @update:modelValue="(v) => (selectedChartActionCommand = v || 'SelectionInRevit')"
        />
        <button type="button" class="toolbar-btn" @click="showCreatePanel = !showCreatePanel">
          <i class="pi pi-plus" />
        </button>
        <button
          type="button"
          class="toolbar-btn toolbar-btn--accent"
          @click="showGeneratorDrawer = true"
        >
          <i class="pi pi-sparkles" />
        </button>
        <button
          type="button"
          class="toolbar-btn"
          :disabled="refreshingAll"
          @click="refreshAllCards"
        >
          <i class="pi pi-refresh" :class="refreshingAll ? 'pi-spin' : ''" />
        </button>
        <button
          type="button"
          class="toolbar-btn toolbar-btn--danger"
          title="Delete selected cards"
          :disabled="!hasSelectedCards"
          @click="removeSelectedCards"
        >
          <i class="pi pi-times" />
        </button>
        <button
          type="button"
          class="toolbar-btn toolbar-btn--danger"
          :disabled="cards.length === 0"
          @click="removeAllCards"
        >
          <i class="pi pi-trash" />
        </button>

        <div v-if="showCreatePanel" class="creator-panel">
          <div class="creator-title">Create Card</div>

          <div class="creator-grid">
            <div class="creator-field">
              <label>Category</label>
              <Select
                :options="sortedCategories"
                placeholder="Select category"
                :modelValue="draft.category"
                @update:modelValue="onDraftCategoryChange"
              />
            </div>

            <div class="creator-field">
              <label>Parameter</label>
              <Select
                :options="availableParameters"
                placeholder="Select parameter"
                :modelValue="draft.parameter"
                :disabled="!draft.category || draftCategoryLoading"
                @update:modelValue="(v) => (draft.parameter = v)"
              />
              <span v-if="draftCategoryLoading" class="creator-meta">Loading parameters...</span>
              <span v-else-if="draftCategoryError" class="creator-meta creator-meta--error">{{
                draftCategoryError
              }}</span>
            </div>

            <div class="creator-field">
              <label>View</label>
              <Select
                :options="viewTypeOptions"
                optionLabel="label"
                optionValue="value"
                :modelValue="draft.viewType"
                @update:modelValue="(v) => (draft.viewType = v || 'chart')"
              />
            </div>
          </div>

          <div class="creator-actions">
            <button type="button" class="secondary-btn" @click="showCreatePanel = false">
              Cancel
            </button>
            <button
              type="button"
              class="primary-btn"
              :disabled="!canCreateCard"
              @click="createCard"
            >
              Create
            </button>
          </div>
        </div>
      </div>
    </template>
  </InfiniteCanvas>

  <Drawer
    v-model:visible="showGeneratorDrawer"
    header="Bulk Generator"
    position="right"
    :modal="false"
    :dismissable="true"
    :style="{ width: 'min(48rem, 94vw)' }"
  >
    <div class="generator-panel">
      <div class="generator-table-head">
        <span>Parameter</span>
        <span>Category</span>
        <span>Action</span>
      </div>

      <div v-for="row in generationRows" :key="row.id" class="generator-row">
        <InputText
          :modelValue="row.parameter"
          placeholder="Parameter name"
          @update:modelValue="(v) => (row.parameter = String(v || ''))"
        />

        <div class="generator-category-cell">
          <Select
            :options="sortedCategories"
            placeholder="Category"
            :modelValue="row.category"
            :disabled="row.useAllCategories"
            @update:modelValue="(v) => (row.category = v)"
          />
          <label class="generator-all-toggle">
            <Checkbox
              binary
              :modelValue="row.useAllCategories"
              @update:modelValue="(v) => onGeneratorRowAllCategoriesChange(row, Boolean(v))"
            />
            <span>All categories</span>
          </label>
        </div>

        <button type="button" class="secondary-btn" @click="removeGenerationRow(row.id)">
          Remove
        </button>
      </div>

      <div class="generator-actions">
        <button type="button" class="secondary-btn" @click="addGenerationRow">+ Add row</button>
        <button
          type="button"
          class="primary-btn"
          :disabled="!canGenerateFromDrawer || generatingFromDrawer"
          @click="generateCardsFromDrawer"
        >
          {{ generatingFromDrawer ? "Generating..." : "Start generation" }}
        </button>
      </div>

      <div v-if="generationInfo" class="generator-info">{{ generationInfo }}</div>
    </div>
  </Drawer>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.chart-action-switch {
  min-width: 12.5rem;
}

.toolbar-btn {
  width: 2.2rem;
  height: 2.2rem;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.6rem;
  background: var(--p-surface-0, #ffffff);
  color: var(--p-surface-800, #1e293b);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.toolbar-btn:hover {
  background: var(--p-surface-100, #f1f5f9);
}

.toolbar-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.toolbar-btn--accent {
  border-color: var(--p-primary-500, #0284c7);
  color: var(--p-primary-500, #0284c7);
}

.toolbar-btn--accent:hover {
  background: var(--p-primary-100, #e0f2fe);
}

.toolbar-btn--danger {
  border-color: var(--p-red-500, #dc2626);
  color: var(--p-red-500, #dc2626);
}

.toolbar-btn--danger:hover {
  background: var(--p-red-100, #fee2e2);
}

.creator-panel {
  width: min(34rem, calc(100vw - 4rem));
  padding: 0.75rem;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.7rem;
  background: var(--p-surface-0, #ffffff);
  box-shadow: 0 16px 30px -26px rgba(15, 23, 42, 0.6);
}

.creator-title {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--p-surface-800, #1e293b);
  margin-bottom: 0.6rem;
}

.creator-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.55rem;
}

.creator-field {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.creator-field label {
  font-size: 0.7rem;
  color: var(--p-surface-600, #475569);
}

.creator-meta {
  font-size: 0.7rem;
  color: var(--p-surface-500, #64748b);
}

.creator-meta--error {
  color: #dc2626;
}

.creator-actions {
  margin-top: 0.7rem;
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
}

.primary-btn,
.secondary-btn {
  border-radius: 0.55rem;
  font-size: 0.76rem;
  font-weight: 600;
  padding: 0.35rem 0.7rem;
  cursor: pointer;
}

.secondary-btn {
  border: 1px solid var(--p-surface-300, #d1d5db);
  background: var(--p-surface-0, #ffffff);
  color: var(--p-surface-700, #334155);
}

.primary-btn {
  border: 1px solid var(--p-primary-500, #0284c7);
  background: var(--p-primary-500, #0284c7);
  color: var(--p-primary-contrast-color, #ffffff);
}

.primary-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.card-state {
  font-size: 0.82rem;
  color: var(--p-surface-600, #475569);
}

.generator-panel {
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
}

.generator-table-head,
.generator-row {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) minmax(0, 1.5fr) auto;
  gap: 0.55rem;
  align-items: start;
}

.generator-table-head {
  font-size: 0.72rem;
  color: var(--p-surface-600, #475569);
  font-weight: 700;
  text-transform: uppercase;
}

.generator-category-cell {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.generator-all-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.74rem;
  color: var(--p-surface-700, #334155);
}

.generator-actions {
  margin-top: 0.45rem;
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
}

.generator-info {
  font-size: 0.76rem;
  color: var(--p-surface-600, #475569);
  line-height: 1.35;
}
</style>
