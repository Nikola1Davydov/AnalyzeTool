<script setup lang="ts">
import { computed, ref, nextTick, onMounted, onBeforeUnmount } from "vue";
import Chart from "primevue/chart";
import { Commands, invoke } from "@/RevitBridge";
import { ParameterOrigin } from "@/stores/types";
import type { ElementItem, ParameterData } from "@/stores/types";
import { resolveInstanceActionElementIds, type RevitActionMatch } from "@/utils/revitActionTargets";

type ParamChart = {
  parameter: string;
  entries: { value: string; count: number; elementIds: number[] }[];
  data: any;
  options: any;
};

const props = defineProps<{
  items: ElementItem[];
  filters?: string[];
  search?: string;
  clickAction?: string;
  selectedParameter?: string | null;
}>();

const themeVersion = ref(0);
let themeObserver: MutationObserver | null = null;

function resolveCssVar(name: string, fallback: string): string {
  if (typeof window === "undefined") return fallback;
  const value = window.getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

const palette = computed(() => {
  void themeVersion.value;
  return [
    resolveCssVar("--p-blue-500", "#42A5F5"),
    resolveCssVar("--p-emerald-500", "#66BB6A"),
    resolveCssVar("--p-amber-500", "#FFA726"),
    resolveCssVar("--p-violet-500", "#AB47BC"),
    resolveCssVar("--p-cyan-500", "#26C6DA"),
    resolveCssVar("--p-red-500", "#EF5350"),
    resolveCssVar("--p-yellow-500", "#FFCA28"),
  ];
});

const activeFilters = computed(() => props.filters ?? []);
const activeSearch = computed(() => (props.search ?? "").trim().toLowerCase());
const hasItems = computed(() => Array.isArray(props.items) && props.items.length > 0);
const expanded = ref<string | null>(null);
const activeClickAction = computed(() =>
  props.clickAction && props.clickAction.toLowerCase() === "isolation" ? "Isolation" : "Selection",
);

function matchesFilters(param: ParameterData, filters: string[]): boolean {
  if (!filters || filters.length === 0) return true;

  return filters.every((filter) => {
    if (filter === "Instance") return param.isTypeParameter === false;
    if (filter === "Type") return param.isTypeParameter === true;
    if (filter === "Schared") return param.origin === ParameterOrigin.Shared;
    if (filter === "Project") return param.origin === ParameterOrigin.Project;
    if (filter === "BuildIn") return param.origin === ParameterOrigin.BuiltIn;
    return false;
  });
}

const parameterCharts = computed(() => {
  if (!props.items || !Array.isArray(props.items)) return [];

  const grouped = new Map<string, Map<string, RevitActionMatch[]>>();

  for (const element of props.items) {
    const parameters: ParameterData[] = (element as any).parameters ?? [];

    for (const param of parameters) {
      if (!matchesFilters(param, activeFilters.value)) continue;

      const paramName = param?.name ?? "Unknown";
      if (props.selectedParameter && paramName !== props.selectedParameter) continue;
      const rawValue = param?.value;
      const valueLabel =
        rawValue === undefined || rawValue === null || rawValue === ""
          ? "(empty)"
          : String(rawValue);

      if (!grouped.has(paramName)) grouped.set(paramName, new Map());
      const valueMap = grouped.get(paramName)!;

      if (!valueMap.has(valueLabel)) valueMap.set(valueLabel, []);
      valueMap.get(valueLabel)!.push({ element, parameter: param });
    }
  }

  return Array.from(grouped.entries())
    .map(([parameter, valueMap]) => {
      const entries = Array.from(valueMap.entries())
        .map(([value, matches]) => {
          const elementIds = resolveInstanceActionElementIds(props.items, matches);
          return {
            value,
            count: elementIds.length,
            elementIds,
          };
        })
        .filter((entry) => entry.count > 0)
        .sort((a, b) => b.count - a.count);

      // Apply search: match parameter name or value label
      const q = activeSearch.value;
      let filteredEntries = entries;
      if (q) {
        const paramMatch = parameter.toLowerCase().includes(q);
        filteredEntries = paramMatch
          ? entries
          : entries.filter((e) => e.value.toLowerCase().includes(q));
      }

      if (filteredEntries.length === 0) return null;

      const data = {
        labels: filteredEntries.map((e) => e.value),
        datasets: [
          {
            label: "Count",
            data: filteredEntries.map((e) => e.count),
            backgroundColor: filteredEntries.map(
              (_, idx) => palette.value[idx % palette.value.length],
            ),
          },
        ],
      };

      const options = {
        responsive: true,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (context) => `Count: ${context.formattedValue}`,
            },
          },
        },
        onClick: (evt, elements, chart) => {
          const points = chart?.getElementsAtEventForMode(
            evt,
            "nearest",
            { intersect: true },
            false,
          );
          if (!points?.length) return;
          const idx = points[0].index;
          const entry = filteredEntries[idx];
          if (!entry) return;
          const command =
            activeClickAction.value === "Isolation"
              ? Commands.IsolationInRevit
              : Commands.SelectionInRevit;
          invoke(command, { elementIds: entry.elementIds }).catch((err) => {
            console.error("Failed to send selection request", err);
          });
        },
      };

      return { parameter, entries: filteredEntries, data, options };
    })
    .filter((c): c is ParamChart => Boolean(c))
    .sort((a, b) => {
      const totalA = a.entries.reduce((sum, e) => sum + e.count, 0);
      const totalB = b.entries.reduce((sum, e) => sum + e.count, 0);
      if (totalA !== totalB) return totalB - totalA;
      return a.parameter.localeCompare(b.parameter);
    });
});

const visibleCharts = computed(() => {
  if (!expanded.value) return parameterCharts.value;
  return parameterCharts.value.filter((c) => c.parameter === expanded.value);
});

function toggleExpand(parameter: string) {
  expanded.value = expanded.value === parameter ? null : parameter;
  // Force chart.js to recompute dimensions after layout change
  nextTick(() => {
    window.dispatchEvent(new Event("resize"));
  });
}

onMounted(() => {
  if (typeof window === "undefined") return;
  themeObserver = new MutationObserver(() => {
    themeVersion.value += 1;
  });
  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ["class", "style", "data-theme"],
  });
});

onBeforeUnmount(() => {
  if (themeObserver) {
    themeObserver.disconnect();
    themeObserver = null;
  }
});
</script>

<template>
  <div class="flex flex-col gap-4">
    <div v-if="parameterCharts.length === 0 && hasItems" class="text-sm text-surface-500">
      No parameters match the selected filters.
    </div>

    <div
      class="grid gap-4"
      :class="expanded ? 'grid-cols-1' : 'grid-cols-1 md:grid-cols-2 xl:grid-cols-3'"
    >
      <Panel v-for="chart in visibleCharts" :key="chart.parameter" class="w-full">
        <template #header>
          <div class="flex justify-between items-center w-full gap-2">
            <span class="font-semibold truncate">{{ chart.parameter }}</span>
            <Button
              size="small"
              severity="secondary"
              :icon="
                expanded === chart.parameter ? 'pi pi-window-minimize' : 'pi pi-window-maximize'
              "
              :label="expanded === chart.parameter ? 'Collapse' : 'Expand'"
              @click.stop="toggleExpand(chart.parameter)"
            />
          </div>
        </template>
        <Chart
          type="bar"
          :data="chart.data"
          :options="chart.options"
          :key="chart.parameter + (expanded ? '-expanded' : '-normal')"
          class="w-full h-full"
        />
      </Panel>
    </div>
  </div>
</template>
