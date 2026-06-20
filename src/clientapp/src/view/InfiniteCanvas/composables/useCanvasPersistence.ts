import { computed, onBeforeUnmount, ref, watch, type Ref } from "vue";
import type { ElementItem } from "@/stores/types";

export type PersistedViewportState = {
  zoom: number;
  panX: number;
  panY: number;
};

type CanvasCardViewType = "chart" | "table";

type CanvasCardState = {
  id: number;
  category: string;
  parameter: string;
  viewType: CanvasCardViewType;
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
  viewType: CanvasCardViewType;
  x: number;
  y: number;
  width: number;
  height: number;
};

type PersistedChartActionState = {
  command: string;
};

type UseCanvasPersistenceDeps = {
  cards: Ref<CanvasCardState[]>;
  nextCardId: Ref<number>;
  selectedCardIds: Ref<number[]>;
  categorySnapshots: Ref<Record<string, ElementItem[]>>;
  selectedChartActionCommand: Ref<string>;
  documentId: Ref<unknown>;
  documentName: Ref<unknown>;
  storageKeyPrefix?: string;
};

export function useCanvasPersistence(deps: UseCanvasPersistenceDeps) {
  const persistTimer = ref<number | null>(null);
  const activeStorageKey = ref("");
  const suppressPersist = ref(false);
  const viewportState = ref<PersistedViewportState>({
    zoom: 1,
    panX: 0,
    panY: 0,
  });

  const projectScope = computed(() => {
    const id = String(deps.documentId.value || "").trim();
    if (id) return `id:${id}`;

    const name = String(deps.documentName.value || "")
      .trim()
      .toLowerCase();
    if (name) return `name:${name}`;

    return "unknown-project";
  });

  function getCanvasStorageKey(): string {
    return `${deps.storageKeyPrefix || "infinite-canvas-state-v1"}::${projectScope.value}`;
  }

  function toPersistedCards(source: CanvasCardState[]): PersistedCanvasCard[] {
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

  function applyPersistedCards(source: PersistedCanvasCard[]): CanvasCardState[] {
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
      cards: toPersistedCards(deps.cards.value),
      nextCardId: deps.nextCardId.value,
      viewport: viewportState.value,
      chartAction: { command: deps.selectedChartActionCommand.value },
    };

    try {
      localStorage.setItem(storageKey, JSON.stringify(payload));
    } catch (err) {
      console.error("Failed to persist canvas state", err);
    }
  }

  function queuePersistCanvasState() {
    if (persistTimer.value !== null) window.clearTimeout(persistTimer.value);
    persistTimer.value = window.setTimeout(() => {
      persistTimer.value = null;
      persistCanvasState();
    }, 120);
  }

  function restoreCanvasState() {
    if (typeof localStorage === "undefined") return;

    const storageKey = getCanvasStorageKey();
    activeStorageKey.value = storageKey;
    suppressPersist.value = true;
    deps.selectedCardIds.value = [];

    try {
      const raw = localStorage.getItem(storageKey);
      if (!raw) {
        deps.cards.value = [];
        deps.nextCardId.value = 1;
        deps.categorySnapshots.value = {};
        viewportState.value = { zoom: 1, panX: 0, panY: 0 };
        deps.selectedChartActionCommand.value = "SelectionInRevit";
        return;
      }

      const parsed = JSON.parse(raw) as {
        cards?: PersistedCanvasCard[];
        nextCardId?: number;
        viewport?: PersistedViewportState;
        chartAction?: PersistedChartActionState;
      };

      const restoredCards = applyPersistedCards(parsed.cards || []);
      deps.cards.value = restoredCards;

      const maxId = restoredCards.reduce((max, card) => Math.max(max, card.id), 0);
      const savedNext = Number(parsed.nextCardId) || 0;
      deps.nextCardId.value = Math.max(maxId + 1, savedNext, 1);

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
        if (nextCommand) deps.selectedChartActionCommand.value = nextCommand;
      }
    } catch (err) {
      console.error("Failed to restore canvas state", err);
      deps.cards.value = [];
      deps.nextCardId.value = 1;
      deps.categorySnapshots.value = {};
      viewportState.value = { zoom: 1, panX: 0, panY: 0 };
      deps.selectedChartActionCommand.value = "SelectionInRevit";
    } finally {
      suppressPersist.value = false;
    }
  }

  watch(
    () => deps.cards.value,
    () => {
      queuePersistCanvasState();
    },
    { deep: true },
  );

  watch(deps.nextCardId, () => {
    queuePersistCanvasState();
  });

  watch(
    () => viewportState.value,
    () => {
      queuePersistCanvasState();
    },
    { deep: true },
  );

  watch(deps.selectedChartActionCommand, () => {
    queuePersistCanvasState();
  });

  onBeforeUnmount(() => {
    if (persistTimer.value !== null) {
      window.clearTimeout(persistTimer.value);
      persistTimer.value = null;
    }
  });

  return {
    viewportState,
    projectScope,
    restoreCanvasState,
  };
}
