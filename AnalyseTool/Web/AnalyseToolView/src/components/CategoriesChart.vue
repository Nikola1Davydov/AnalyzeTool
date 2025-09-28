<script setup>
import Chart from "primevue/chart";
import { computed, ref } from "vue";
import { storeToRefs } from "pinia";
import { useElements } from "@/stores/useElements";
const { filtered } = storeToRefs(useElements());

const categories = computed(() => {
  const list = Array.isArray(filtered.value) ? filtered.value : [];
  const set = new Set(list.map((e) => e?.CategoryName).filter(Boolean));
  return Array.from(set); // ["Walls","Floors", ...]
});

const counts = computed(() => {
  return categories.value.map(
    (cat) => filtered.value.filter((e) => e?.CategoryName === cat).length
  );
});

const chartData = computed(() => ({
  labels: categories.value,
  datasets: [
    {
      label: "Elements count",
      data: counts.value,
      backgroundColor: "#42A5F5",
    },
  ],
}));

const chartOptions = {
  responsive: true,
  plugins: {
    legend: {
      position: "top",
    },
  },
  scales: {
    x: {
      title: {
        display: true,
        text: "Category",
      },
    },
    y: {
      beginAtZero: true,
      title: {
        display: true,
        text: "Count",
      },
    },
  },
};
</script>

<template>
  <div class="flex flex-col h-full w-full">
    <Chart
      class="flex flex-col"
      type="bar"
      :data="chartData"
      :options="chartOptions"
    />
  </div>
</template>
