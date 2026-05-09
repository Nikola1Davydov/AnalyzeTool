import { computed, ref, type Ref } from "vue";
import type { ElementItem } from "@/stores/types";

export type GeneratorDraftRow = {
  id: number;
  parameter: string;
  category: string | null;
  useAllCategories: boolean;
};

type BulkGeneratorCard = {
  id: number;
  category: string;
  parameter: string;
  viewType: "chart" | "table";
  x: number;
  y: number;
  width: number;
  height: number;
  loading: boolean;
  error: string | null;
};

type UseBulkGeneratorDeps = {
  sortedCategories: Ref<string[]>;
  cards: Ref<BulkGeneratorCard[]>;
  nextCardId: Ref<number>;
  categorySnapshots: Ref<Record<string, ElementItem[]>>;
  loadCategorySnapshot: (category: string, force?: boolean) => Promise<ElementItem[]>;
  refreshCategoryForCards: (category: string, force?: boolean) => Promise<void>;
};

export function useBulkGenerator(deps: UseBulkGeneratorDeps) {
  const showGeneratorDrawer = ref(false);
  const generatingFromDrawer = ref(false);
  const generationInfo = ref("");
  const generationRows = ref<GeneratorDraftRow[]>([
    {
      id: 1,
      parameter: "",
      category: null,
      useAllCategories: false,
    },
  ]);
  const nextGenerationRowId = ref(2);

  const canGenerateFromDrawer = computed(() => {
    return generationRows.value.some((row) => {
      const hasParameter = row.parameter.trim().length > 0;
      if (!hasParameter) return false;
      if (row.useAllCategories) return deps.sortedCategories.value.length > 0;
      return !!row.category;
    });
  });

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

  function updateGenerationRowParameter(rowId: number, value: string) {
    const row = generationRows.value.find((x) => x.id === rowId);
    if (!row) return;
    row.parameter = String(value || "");
  }

  function updateGenerationRowCategory(rowId: number, value: string | null) {
    const row = generationRows.value.find((x) => x.id === rowId);
    if (!row) return;
    row.category = value;
  }

  function updateGenerationRowAllCategories(rowId: number, value: boolean) {
    const row = generationRows.value.find((x) => x.id === rowId);
    if (!row) return;
    onGeneratorRowAllCategoriesChange(row, value);
  }

  function buildGenerationPairs(): { parameter: string; category: string }[] {
    const pairs: { parameter: string; category: string }[] = [];

    for (const row of generationRows.value) {
      const parameter = row.parameter.trim();
      if (!parameter) continue;

      const categories = row.useAllCategories
        ? deps.sortedCategories.value
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

    const snapshot = deps.categorySnapshots.value[category] || [];
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
        await deps.loadCategorySnapshot(category, true).catch((err) => {
          console.error("Failed to load category before bulk generation", err);
        });
      }

      const existingPairKeys = new Set(
        deps.cards.value.map((card) => getPairKey(card.category, card.parameter)),
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

      const startY = deps.cards.value.length
        ? Math.max(...deps.cards.value.map((card) => card.y + card.height)) + 28
        : 80;

      const newCards: BulkGeneratorCard[] = [];
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
        const chartId = deps.nextCardId.value;
        deps.nextCardId.value += 1;
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

        const tableId = deps.nextCardId.value;
        deps.nextCardId.value += 1;
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

      deps.cards.value = [...deps.cards.value, ...newCards];

      for (const category of categoriesToLoad) {
        await deps.refreshCategoryForCards(category, true);
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

  return {
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
  };
}
