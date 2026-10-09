<script setup lang="ts">
/**
 * One block of the report. Rendered twice: once off-screen to be measured (charts as placeholders of
 * their fixed height — a canvas there would only cost time), once on its page.
 */
import { computed, defineAsyncComponent } from "vue";
import { percent, SECTION_LABELS, type Block, type ParameterReport } from "./report";

const Chart = defineAsyncComponent(() => import("primevue/chart"));

const props = defineProps<{
  block: Block;
  title: string;
  documentTitle: string;
  date: string;
  parameterCount: number;
  measure?: boolean;
}>();

const FILLED = "#16a34a";
const EMPTY = "#fda4af";
const VALUE = "#3b82f6";

function bindingText(p: ParameterReport) {
  return p.isInstance ? "Instance parameter" : "Type parameter (counted per instance)";
}

const chart = computed(() => {
  const b = props.block;
  if (b.kind !== "chart") return null;
  const p = b.param;

  if (b.section === "values") {
    const labels = p.values.map((v) => v.value);
    const data = p.values.map((v) => v.count);
    if (p.otherValuesCount > 0) {
      labels.push("Other");
      data.push(p.otherValuesCount);
    }
    return {
      data: { labels, datasets: [{ label: "Elements", data, backgroundColor: VALUE, borderRadius: 2 }] },
      stacked: false,
    };
  }

  const source = b.section === "filled" ? p.categories : p.levels;
  return {
    data: {
      labels: source.map((c) => c.name),
      datasets: [
        { label: "Filled", data: source.map((c) => c.filled), backgroundColor: FILLED },
        { label: "Empty", data: source.map((c) => c.total - c.filled), backgroundColor: EMPTY },
      ],
    },
    stacked: true,
  };
});

// Fixed for paper: no animation (a print taken mid-animation shows half bars), a higher pixel ratio
// so the canvas is not blurry printed, and the size from the block, not from the window.
const chartOptions = computed(() => ({
  indexAxis: "y",
  animation: false,
  responsive: true,
  maintainAspectRatio: false,
  devicePixelRatio: 3,
  plugins: {
    legend: { display: !!chart.value?.stacked, position: "bottom", labels: { boxWidth: 10, font: { size: 10 } } },
    tooltip: { enabled: false },
  },
  scales: {
    x: { stacked: chart.value?.stacked, beginAtZero: true, ticks: { precision: 0, font: { size: 9 } } },
    y: {
      stacked: chart.value?.stacked,
      ticks: {
        font: { size: 9 },
        // Long values would eat the plot area; the table carries them in full.
        callback(this: any, value: number) {
          const label = String(this.getLabelForValue(value) ?? "");
          return label.length > 34 ? label.slice(0, 33) + "…" : label;
        },
      },
    },
  },
}));

const sectionTitle = computed(() =>
  props.block.kind === "chart" || props.block.kind === "table" ? SECTION_LABELS[props.block.section].title : "",
);
</script>

<template>
  <!-- Title: first page only -->
  <div v-if="block.kind === 'title'" class="pb-2 border-b-2 border-surface-800">
    <div class="text-[22pt] font-bold leading-tight">{{ title || "Parameter report" }}</div>
    <div class="text-[10pt] text-surface-600 mt-1">
      {{ documentTitle }} · {{ date }} · {{ parameterCount }} {{ parameterCount === 1 ? "parameter" : "parameters" }}
    </div>
  </div>

  <!-- Summary: every parameter, one line -->
  <div v-else-if="block.kind === 'summary'">
    <div class="text-[11pt] font-semibold mb-1">Summary{{ block.continued ? " (continued)" : "" }}</div>
    <table class="report-table">
      <thead>
        <tr><th>Parameter</th><th>Binding</th><th class="num">Filled</th><th class="num">Total</th><th class="w-[45mm]">Filled %</th></tr>
      </thead>
      <tbody>
        <tr v-for="p in block.rows" :key="p.name + (p.guid ?? '')">
          <td>{{ p.name }}</td>
          <td>{{ p.error ? "—" : p.isInstance ? "Instance" : "Type" }}</td>
          <td class="num">{{ p.error ? "—" : p.filled }}</td>
          <td class="num">{{ p.error ? "—" : p.total }}</td>
          <td>
            <div v-if="!p.error" class="flex items-center gap-1">
              <div class="h-[2.5mm] grow bg-rose-200 rounded-sm overflow-hidden">
                <div class="h-full bg-green-600" :style="{ width: percent(p.filled, p.total) + '%' }" />
              </div>
              <span class="w-[9mm] text-right">{{ percent(p.filled, p.total) }}%</span>
            </div>
            <span v-else class="text-surface-500">{{ p.error }}</span>
          </td>
        </tr>
      </tbody>
    </table>
  </div>

  <!-- A parameter's heading -->
  <div v-else-if="block.kind === 'heading'" class="pt-1 border-t border-surface-300">
    <div class="flex items-baseline justify-between gap-3">
      <div class="text-[14pt] font-bold">{{ block.param.name }}</div>
      <div v-if="!block.param.error" class="text-[12pt] font-semibold" :class="percent(block.param.filled, block.param.total) === 100 ? 'text-green-700' : ''">
        {{ percent(block.param.filled, block.param.total) }}% filled
      </div>
    </div>
    <div class="text-[9pt] text-surface-600">
      <template v-if="block.param.error">{{ block.param.error }}</template>
      <template v-else-if="block.param.total === 0">No element in the bound categories carries this parameter.</template>
      <template v-else>
        {{ bindingText(block.param) }} · {{ block.param.filled }} of {{ block.param.total }} elements ·
        {{ block.param.distinctValues }} distinct {{ block.param.distinctValues === 1 ? "value" : "values" }}
      </template>
    </div>
  </div>

  <!-- Chart -->
  <div v-else-if="block.kind === 'chart'">
    <div class="text-[9pt] font-semibold text-surface-700 mb-1">{{ sectionTitle }}</div>
    <div :style="{ height: block.heightMm + 'mm' }">
      <Chart v-if="!measure && chart" type="bar" :data="chart.data" :options="chartOptions" class="h-full" />
    </div>
  </div>

  <!-- Table -->
  <div v-else-if="block.kind === 'table'">
    <div class="text-[9pt] font-semibold text-surface-700 mb-1">
      {{ sectionTitle }}{{ block.continued ? " (continued)" : "" }}
    </div>
    <table class="report-table">
      <thead v-if="block.section === 'values'">
        <tr><th>Value</th><th class="num">Elements</th><th class="num">% of filled</th></tr>
      </thead>
      <thead v-else>
        <tr>
          <th>{{ block.section === "filled" ? "Category" : "Level" }}</th>
          <th class="num">Filled</th><th class="num">Empty</th><th class="num">Total</th><th class="num">Filled %</th>
        </tr>
      </thead>
      <tbody v-if="block.section === 'values'">
        <tr v-for="(r, i) in block.rows" :key="i">
          <td class="break-all">{{ r.label }}</td><td class="num">{{ r.count }}</td><td class="num">{{ r.share }}%</td>
        </tr>
        <tr v-if="block.footer" class="text-surface-500">
          <td>{{ block.footer.label }}</td><td class="num">{{ block.footer.count }}</td><td class="num">{{ block.footer.share }}%</td>
        </tr>
      </tbody>
      <tbody v-else>
        <tr v-for="(r, i) in block.rows" :key="i">
          <td>{{ r.label }}</td>
          <td class="num">{{ r.filled }}</td>
          <td class="num">{{ (r.total ?? 0) - (r.filled ?? 0) }}</td>
          <td class="num">{{ r.total }}</td>
          <td class="num">{{ percent(r.filled ?? 0, r.total ?? 0) }}%</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.report-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 8.5pt;
}
.report-table th {
  text-align: left;
  font-weight: 600;
  border-bottom: 1px solid #334155;
  padding: 0.6mm 1.5mm;
}
.report-table td {
  border-bottom: 1px solid #e2e8f0;
  padding: 0.6mm 1.5mm;
  vertical-align: top;
}
.report-table .num {
  text-align: right;
  white-space: nowrap;
  width: 18mm;
}
</style>
