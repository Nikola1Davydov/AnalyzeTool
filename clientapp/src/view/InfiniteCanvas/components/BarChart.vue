<script setup lang="ts">
import { computed } from "vue";
import Chart from "primevue/chart";
import type { ElementItem } from "@/stores/types";

const props = defineProps<{
  items: ElementItem[];
  selectedParameter?: string | null;
}>();

const chartRows = computed(() => {
  const buckets = new Map<string, number>();

  for (const element of props.items || []) {
    const parameters = (element.parameters || []) as any[];
    for (const parameter of parameters) {
      const name = String(parameter?.name ?? "");
      if (props.selectedParameter && name !== props.selectedParameter) continue;

      const raw = parameter?.value;
      const label = raw === undefined || raw === null || raw === "" ? "(empty)" : String(raw);
      buckets.set(label, (buckets.get(label) || 0) + 1);
    }
  }

  return Array.from(buckets.entries())
    .map(([label, value]) => ({ label, value }))
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
      backgroundColor: "#0ea5e9",
      hoverBackgroundColor: "#0284c7",
      maxBarThickness: 32,
    },
  ],
}));

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  animation: false,
  plugins: {
    legend: { display: false },
  },
  scales: {
    x: {
      ticks: {
        autoSkip: true,
        maxRotation: 0,
      },
      grid: {
        display: false,
      },
    },
    y: {
      beginAtZero: true,
      ticks: {
        precision: 0,
      },
      grid: {
        color: "#e2e8f0",
      },
    },
  },
};
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
