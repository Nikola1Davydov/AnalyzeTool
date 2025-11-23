<script setup lang="ts">
import Chart from "primevue/chart";
import { computed } from "vue";
import { sendRequest } from "@/RevitBridge";
import { RevitCommand } from "@/stores/types";

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

const handleChartClick = (event) => {
  // Получаем элементы Chart.js под курсором — они содержат datasetIndex и index
  const points = event.chart.getElementsAtEventForMode(
    event,
    "nearest",
    { intersect: true },
    false
  );
  if (!points || !points.length) return;

  const first = points[0];
  const dataIndex = first.index; // индекс по оси X
  const datasetIndex = first.datasetIndex; // 0 = Filled, 1 = Empty

  const paramName = parameterStats.value[dataIndex]?.parameter;
  console.log("Chart clicked point", { paramName, dataIndex, datasetIndex });

  if (!paramName) return;

  // Собираем все параметры и фильтруем по имени
  const allParams = props.items.flatMap((el) => el.parameters || []);
  const matchedParams = allParams.filter((p) => p.name === paramName);

  // В зависимости от datasetIndex отбираем filled или empty элементы
  let elementIds = [];
  if (datasetIndex === 0) {
    elementIds = matchedParams.filter((p) => p.value).map((p) => p.elementId);
  } else if (datasetIndex === 1) {
    elementIds = matchedParams.filter((p) => !p.value).map((p) => p.elementId);
  } else {
    // fallback: все элементы
    elementIds = matchedParams.map((p) => p.elementId);
  }

  console.log(
    `Sending ${elementIds.length} elements for parameter "${paramName}" (dataset ${datasetIndex}):`,
    elementIds
  );

  sendRequest(RevitCommand.Selection, { elementIds }).catch((err) =>
    console.error("Error sending chart click:", err)
  );
};

const chartOptions = {
  responsive: true,
  plugins: {
    legend: { position: "top" },
    tooltip: {
      mode: "index",
      intersect: false,
    },
  },
  scales: {
    x: { stacked: true, title: { display: true, text: "Parameters" } },
    y: {
      stacked: true,
      beginAtZero: true,
      title: { display: true, text: "Count" },
    },
  },
  onClick: handleChartClick,
};
</script>

<template>
  <div>
    <Chart class="flex flex-col" type="bar" :data="chartData" :options="chartOptions" />
  </div>
</template>
