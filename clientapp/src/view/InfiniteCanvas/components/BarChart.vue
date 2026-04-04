<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import Chart from "primevue/chart";
import { Commands, sendRequest } from "@/RevitBridge";
import type { ElementItem } from "@/stores/types";

const props = defineProps<{
  items: ElementItem[];
  selectedParameter?: string | null;
  actionCommand?: string;
}>();

const themeVersion = ref(0);
let themeObserver: MutationObserver | null = null;

function resolveCssVar(name: string, fallback: string): string {
  if (typeof window === "undefined") return fallback;
  const value = window.getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

const barColors = computed(() => {
  void themeVersion.value;
  return [
    resolveCssVar("--p-blue-500", "#3b82f6"),
    resolveCssVar("--p-emerald-500", "#10b981"),
    resolveCssVar("--p-amber-500", "#f59e0b"),
    resolveCssVar("--p-red-500", "#ef4444"),
    resolveCssVar("--p-violet-500", "#8b5cf6"),
    resolveCssVar("--p-cyan-500", "#06b6d4"),
    resolveCssVar("--p-lime-500", "#84cc16"),
    resolveCssVar("--p-orange-500", "#f97316"),
    resolveCssVar("--p-pink-500", "#ec4899"),
    resolveCssVar("--p-teal-500", "#14b8a6"),
  ];
});

const barHoverColors = computed(() => {
  void themeVersion.value;
  return [
    resolveCssVar("--p-blue-600", "#2563eb"),
    resolveCssVar("--p-emerald-600", "#059669"),
    resolveCssVar("--p-amber-600", "#d97706"),
    resolveCssVar("--p-red-600", "#dc2626"),
    resolveCssVar("--p-violet-600", "#7c3aed"),
    resolveCssVar("--p-cyan-600", "#0891b2"),
    resolveCssVar("--p-lime-600", "#65a30d"),
    resolveCssVar("--p-orange-600", "#ea580c"),
    resolveCssVar("--p-pink-600", "#db2777"),
    resolveCssVar("--p-teal-600", "#0d9488"),
  ];
});

const chartRows = computed(() => {
  const buckets = new Map<string, Set<number>>();

  for (const element of props.items || []) {
    const elementId = Number((element as any).id ?? 0);
    const parameters = (element.parameters || []) as any[];
    for (const parameter of parameters) {
      const name = String(parameter?.name ?? "");
      if (props.selectedParameter && name !== props.selectedParameter) continue;

      const raw = parameter?.value;
      const label = raw === undefined || raw === null || raw === "" ? "(empty)" : String(raw);
      if (!buckets.has(label)) buckets.set(label, new Set<number>());
      const fromParam = Number(parameter?.elementId ?? 0);
      buckets.get(label)!.add(fromParam || elementId);
    }
  }

  return Array.from(buckets.entries())
    .map(([label, ids]) => ({ label, value: ids.size, elementIds: Array.from(ids) }))
    .sort((a, b) => b.value - a.value)
    .slice(0, 24);
});

const chartData = computed(() => ({
  labels: chartRows.value.map((row) => row.label),
  datasets: [
    {
      label: "Count",
      data: chartRows.value.map((row) => row.value),
      borderRadius: 5,
      backgroundColor: chartRows.value.map(
        (_, idx) => barColors.value[idx % barColors.value.length],
      ),
      hoverBackgroundColor: chartRows.value.map(
        (_, idx) => barHoverColors.value[idx % barHoverColors.value.length],
      ),
      maxBarThickness: 32,
    },
  ],
}));

function runActionForIds(elementIds: number[]) {
  if (!elementIds.length) return;

  const command = props.actionCommand || Commands.SelectionInRevit;
  sendRequest(command as any, { elementIds } as any).catch((err) => {
    console.error("Failed to execute chart action", err);
  });
}

const chartOptions = computed(() => ({
  ...(void themeVersion.value, {}),
  responsive: true,
  maintainAspectRatio: false,
  animation: false,
  plugins: {
    legend: { display: false },
  },
  onClick: (evt: any, _elements: any, chart: any) => {
    const points = chart?.getElementsAtEventForMode(evt, "nearest", { intersect: true }, false);
    if (!points?.length) return;
    const idx = points[0].index;
    const row = chartRows.value[idx];
    if (!row) return;
    runActionForIds(row.elementIds || []);
  },
  scales: {
    x: {
      ticks: {
        autoSkip: true,
        maxRotation: 0,
        color: resolveCssVar("--p-surface-700", "#334155"),
      },
      grid: {
        display: false,
      },
    },
    y: {
      beginAtZero: true,
      ticks: {
        precision: 0,
        color: resolveCssVar("--p-surface-700", "#334155"),
      },
      grid: {
        color: resolveCssVar("--p-surface-300", "#e2e8f0"),
      },
    },
  },
}));

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
  <div class="chart-wrap">
    <div v-if="chartRows.length === 0" class="empty-state">No values for this parameter.</div>
    <Chart v-else type="bar" :data="chartData" :options="chartOptions" class="chart-canvas" />
  </div>
</template>

<style scoped>
.chart-wrap {
  width: 100%;
  height: 100%;
  min-height: 12rem;
}

.chart-canvas {
  width: 100%;
  height: 100%;
  cursor: pointer;
}

.empty-state {
  height: 100%;
  min-height: 12rem;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.82rem;
  color: var(--p-surface-500, #64748b);
}
</style>
