<script>
import { defineComponent } from "vue";

export default defineComponent({
  name: "ParameterFilledEmptyChart",
});
</script>

<script setup>
import Chart from "primevue/chart";
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { Commands, sendRequest } from "@/RevitBridge";
import { resolveInstanceActionElementIds } from "@/utils/revitActionTargets";

const props = defineProps({
  items: { type: Array, default: () => [] },
});

const themeVersion = ref(0);
let themeObserver = null;

function resolveCssVar(name, fallback) {
  if (typeof window === "undefined") return fallback;
  const value = window.getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

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
  ...(void themeVersion.value, {}),
  labels: parameterStats.value.map((s) => s.parameter),
  datasets: [
    {
      label: "Filled",
      data: parameterStats.value.map((s) => s.filled),
      backgroundColor: resolveCssVar("--p-blue-500", "#42A5F5"),
      stack: "params",
    },
    {
      label: "Empty",
      data: parameterStats.value.map((s) => s.empty),
      backgroundColor: resolveCssVar("--p-red-500", "#EF5350"),
      stack: "params",
    },
  ],
}));

const handleChartClick = (evt, elements, chart) => {
  try {
    // Получаем ближайшие элементы под курсором
    const points = chart.getElementsAtEventForMode(evt, "nearest", { intersect: true }, false);
    if (!points || !points.length) return;

    const first = points[0];
    const dataIndex = first.index; // индекс по оси X
    const datasetIndex = first.datasetIndex; // 0 = Filled, 1 = Empty

    const paramName = parameterStats.value[dataIndex]?.parameter;
    console.log("Chart clicked point", { paramName, dataIndex, datasetIndex });

    if (!paramName) return;

    const matchedEntries = (props.items || []).flatMap((element) =>
      (element.parameters || [])
        .filter((parameter) => parameter.name === paramName)
        .map((parameter) => ({ element, parameter })),
    );

    // В зависимости от datasetIndex отбираем filled или empty элементы
    let matches = [];
    if (datasetIndex === 0) {
      matches = matchedEntries.filter((entry) => entry.parameter.value);
    } else if (datasetIndex === 1) {
      matches = matchedEntries.filter((entry) => !entry.parameter.value);
    } else {
      matches = matchedEntries;
    }

    const elementIds = resolveInstanceActionElementIds(props.items || [], matches);

    console.log(
      `Sending ${elementIds.length} elements for parameter "${paramName}" (dataset ${datasetIndex}):`,
      elementIds,
    );

    sendRequest(Commands.SelectionInRevit, {
      elementIds: elementIds,
    }).catch((err) => console.error("Error sending chart click:", err));
  } catch (e) {
    console.error("handleChartClick error", e);
  }
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
  <div>
    <Chart class="flex flex-col" type="bar" :data="chartData" :options="chartOptions" />
  </div>
</template>
