import type { Ref } from "vue";
import type { ElementItem } from "@/stores/types";

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

type UseCardActionsDeps = {
  cards: Ref<CanvasCardConfig[]>;
  selectedCardIds: Ref<number[]>;
  nextCardId: Ref<number>;
  refreshingAll: Ref<boolean>;
  categorySnapshots: Ref<Record<string, ElementItem[]>>;
  refreshCategoryForCards: (category: string, force?: boolean) => Promise<void>;
  loadDocumentData: () => Promise<void> | void;
};

export function useCardActions(deps: UseCardActionsDeps) {
  function updateCardLayout(
    cardId: number,
    layout: { x: number; y: number; width: number; height: number },
  ) {
    const target = deps.cards.value.find((card) => card.id === cardId);
    if (!target) return;

    target.x = layout.x;
    target.y = layout.y;
    target.width = layout.width;
    target.height = layout.height;
  }

  function removeCard(cardId: number) {
    deps.cards.value = deps.cards.value.filter((x) => x.id !== cardId);
    deps.selectedCardIds.value = deps.selectedCardIds.value.filter((id) => id !== cardId);
  }

  function removeSelectedCards() {
    if (!deps.selectedCardIds.value.length) return;

    const ids = new Set(deps.selectedCardIds.value);
    deps.cards.value = deps.cards.value.filter((card) => !ids.has(card.id));
    deps.selectedCardIds.value = [];
  }

  function removeAllCards() {
    const confirmed = window.confirm("Delete all generated cards?");
    if (!confirmed) return;

    deps.cards.value = [];
    deps.categorySnapshots.value = {};
    deps.nextCardId.value = 1;
    deps.selectedCardIds.value = [];
  }

  async function refreshCard(cardId: number) {
    const card = deps.cards.value.find((x) => x.id === cardId);
    if (!card) return;
    await deps.refreshCategoryForCards(card.category, true);
  }

  async function refreshAllCards() {
    try {
      await deps.loadDocumentData();
    } catch (err) {
      console.error("Failed to refresh document context", err);
    }

    const categories = Array.from(new Set(deps.cards.value.map((c) => c.category)));
    if (categories.length === 0) return;

    deps.refreshingAll.value = true;
    try {
      for (const category of categories) {
        await deps.refreshCategoryForCards(category, true);
      }
    } finally {
      deps.refreshingAll.value = false;
    }
  }

  function getCardItems(card: CanvasCardConfig): ElementItem[] {
    return deps.categorySnapshots.value[card.category] || [];
  }

  return {
    updateCardLayout,
    removeCard,
    removeSelectedCards,
    removeAllCards,
    refreshCard,
    refreshAllCards,
    getCardItems,
  };
}
