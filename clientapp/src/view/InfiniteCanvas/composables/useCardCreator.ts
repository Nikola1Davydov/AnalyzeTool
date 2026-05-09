import { computed, reactive, ref, type Ref } from "vue";
import type { ElementItem } from "@/stores/types";

export type CardViewType = "chart" | "table";

type CreateCardConfig = {
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

type UseCardCreatorDeps = {
  showCreatePanel: Ref<boolean>;
  cards: Ref<CreateCardConfig[]>;
  nextCardId: Ref<number>;
  categorySnapshots: Ref<Record<string, ElementItem[]>>;
  loadCategorySnapshot: (category: string, force?: boolean) => Promise<ElementItem[]>;
  refreshCategoryForCards: (category: string, force?: boolean) => Promise<void>;
};

export function useCardCreator(deps: UseCardCreatorDeps) {
  const draftCategoryLoading = ref(false);
  const draftCategoryError = ref("");

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

    const categoryItems = deps.categorySnapshots.value[category] || [];
    const set = new Set<string>();
    for (const element of categoryItems) {
      for (const param of element.parameters || []) {
        if (param?.name) set.add(String(param.name));
      }
    }
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  });

  const canCreateCard = computed(() => !!draft.category && !!draft.parameter);

  async function onDraftCategoryChange(value: string | null) {
    draft.category = value;
    draft.parameter = null;
    draftCategoryError.value = "";
    if (!value) return;

    draftCategoryLoading.value = true;
    try {
      await deps.loadCategorySnapshot(value, true);
    } catch (err) {
      draftCategoryError.value = "Failed to load category parameters.";
      console.error("Failed to load category data for card draft", err);
    } finally {
      draftCategoryLoading.value = false;
    }
  }

  async function createCard() {
    if (!canCreateCard.value || !draft.category || !draft.parameter) return;

    const id = deps.nextCardId.value;
    deps.nextCardId.value += 1;

    deps.cards.value.push({
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

    deps.showCreatePanel.value = false;
    await deps.refreshCategoryForCards(draft.category, true);
  }

  return {
    draft,
    draftCategoryLoading,
    draftCategoryError,
    viewTypeOptions,
    availableParameters,
    canCreateCard,
    onDraftCategoryChange,
    createCard,
  };
}
