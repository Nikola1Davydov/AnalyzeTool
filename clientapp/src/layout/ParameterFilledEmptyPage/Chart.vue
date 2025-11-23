<script setup type="ts">
import Chart from "primevue/chart";
import { computed } from "vue";

const props = defineProps({
  items: { type: Array, default: () => [] },
});

const parameterStats = computed(() => {
  if (!props.items || !Array.isArray(props.items)) return [];

  const allParams = props.items.flatMap((el) => el.parameters || []);

  // группируем по имени параметра
  const grouped = {};
  for (const p of allParams) {
    if (!grouped[p.name]) grouped[p.name] = [];
    grouped[p.name].push(p);
  }

  return Object.entries(grouped).map(([paramName, params]) => {
    const filled = params.filter((p) => p.value).length;
    const empty = params.filter((p) => !p.value).length;
    return { parameter: paramName, filled, empty };
  });
});

const chartData = computed(() => ({
  labels: parameterStats.value.map((s) => s.parameter),
  datasets: [
    {
      label: "Filled",
      data: parameterStats.value.map((s) => s.filled),
      backgroundColor: "#42A5F5",
      stack: "params",
    },
    {
      label: "Empty",
      data: parameterStats.value.map((s) => s.empty),
      backgroundColor: "#EF5350",
      stack: "params",
    },
  ],
}));

const chartOptions = {
  responsive: true,
  plugins: { legend: { position: "top" } },
  scales: {
    x: { stacked: true, title: { display: true, text: "Parameters" } },
    y: {
      stacked: true,
      beginAtZero: true,
      title: { display: true, text: "Count" },
    },
  },
};
</script>

<template>
  <div>
    <Chart class="flex flex-col" type="bar" :data="chartData" :options="chartOptions" />
  </div>
</template>
