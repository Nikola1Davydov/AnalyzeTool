<script setup lang="ts">
/**
 * The A4 report: the chosen parameters laid out on printable pages — a summary, then per parameter the
 * analyses the user switched on, each as a chart, a table or both. What is on screen is what prints.
 *
 * Layout is measured, not guessed: every block is rendered once off-screen at the page's content width,
 * its height read, and the pages filled in order (report.ts → paginate).
 */
import { computed, nextTick, onActivated, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import MultiSelect from "primevue/multiselect";
import InputNumber from "primevue/inputnumber";
import ToggleSwitch from "primevue/toggleswitch";
import { invoke } from "@/RevitBridge";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import ReportBlock from "./ReportBlock.vue";
import {
  buildBlocks,
  defaultSettings,
  paginate,
  PAGE,
  PX_PER_MM,
  SECTION_LABELS,
  type ReportData,
  type ReportSettings,
  type SectionKind,
} from "./report";
import { errorText, type ParameterRef } from "../SharedParameters/types";

const store = useSharedParametersStore();
const { project, reportSelection } = storeToRefs(store);
const notifications = useNotificationStore();

// ---- Settings: remembered per viewer (a convenience — the report itself is rebuilt from the model) --
const SETTINGS_KEY = "at.report.settings";
function loadSettings(): ReportSettings {
  const base = defaultSettings();
  try {
    const saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) ?? "null");
    if (saved) return { ...base, ...saved, sections: { ...base.sections, ...saved.sections } };
  } catch {
    /* no storage, or an old shape: defaults */
  }
  return base;
}
const settings = ref<ReportSettings>(loadSettings());
watch(
  settings,
  (s) => {
    try {
      localStorage.setItem(SETTINGS_KEY, JSON.stringify(s));
    } catch {
      /* storage unavailable: settings last for this window */
    }
  },
  { deep: true },
);
const sectionKinds = Object.keys(SECTION_LABELS) as SectionKind[];

// ---- Which parameters: the bound ones of the project; the table's selection preselects them --------
const refKey = (r: ParameterRef) => (r.guid ? `g:${r.guid.toLowerCase()}` : `n:${r.name}`);
const options = computed(() =>
  project.value
    .filter((p) => p.bound)
    .map((p) => {
      const parameterRef: ParameterRef = { guid: p.guid, name: p.name };
      return { key: refKey(parameterRef), label: p.name, ref: parameterRef };
    }),
);
const chosen = ref<string[]>([]);
let takenSelection = "";

function takeSelectionFromTable() {
  const keys = reportSelection.value.map(refKey);
  const signature = keys.join("|");
  if (keys.length && signature !== takenSelection) chosen.value = keys;
  takenSelection = signature;
}

// ---- Data -----------------------------------------------------------------------------------------
const data = ref<ReportData | null>(null);
const loading = ref(false);
let requestSeq = 0;

async function loadReport() {
  const refs = chosen.value
    .map((k) => options.value.find((o) => o.key === k)?.ref)
    .filter((r): r is ParameterRef => !!r);
  if (!refs.length) {
    data.value = null;
    return;
  }
  const seq = ++requestSeq;
  loading.value = true;
  try {
    const res = await invoke<ReportData>("GetParameterReport", {
      parameters: refs,
      topValues: settings.value.topValues,
    });
    if (seq === requestSeq) data.value = res;
  } catch (e) {
    if (seq === requestSeq) notifications.error(`Could not build the report: ${errorText(e)}`);
  } finally {
    if (seq === requestSeq) loading.value = false;
  }
}

let reloadTimer: number | undefined;
watch([chosen, () => settings.value.topValues], () => {
  clearTimeout(reloadTimer);
  reloadTimer = window.setTimeout(loadReport, 300);
});

// ---- Blocks and pages -------------------------------------------------------------------------------
const blocks = computed(() => buildBlocks(data.value, settings.value));
const date = new Date().toLocaleDateString();
const measureBox = ref<HTMLElement | null>(null);
const pages = ref<number[][]>([]);

async function layout() {
  await nextTick();
  const box = measureBox.value;
  if (!box) return;
  const heights = Array.from(box.children).map((el) => (el as HTMLElement).offsetHeight);
  pages.value = paginate(blocks.value, heights);
}
watch(blocks, layout, { deep: false });
watch(() => settings.value.title, layout);

// ---- Preview zoom: pages at true size, scaled to the column's width on screen (never in print) ------
const preview = ref<HTMLElement | null>(null);
const previewWidth = ref(900);
const zoomMode = ref<"fit" | "100">("fit");
const pageWidthPx = PAGE.widthMm * PX_PER_MM;
const pageHeightPx = PAGE.heightMm * PX_PER_MM;
const scale = computed(() =>
  zoomMode.value === "100" ? 1 : Math.min(1, Math.max(0.3, (previewWidth.value - 48) / pageWidthPx)),
);
let observer: ResizeObserver | undefined;

onMounted(() => {
  if (!project.value.length) store.load();
  takeSelectionFromTable();
  observer = new ResizeObserver(([entry]) => (previewWidth.value = entry.contentRect.width));
  if (preview.value) observer.observe(preview.value);
});
onActivated(takeSelectionFromTable); // the main window keeps pages alive: coming back from the table
onBeforeUnmount(() => observer?.disconnect());

function print() {
  window.print();
}
</script>

<template>
  <div class="report-root flex h-[calc(100vh-7.5rem)]">
    <!-- Settings -->
    <aside class="no-print w-72 shrink-0 border-r border-surface-200 bg-surface-0 p-4 overflow-y-auto flex flex-col gap-5 text-sm">
      <div class="flex flex-col gap-1">
        <span class="font-semibold">Title</span>
        <InputText v-model="settings.title" size="small" />
      </div>

      <div class="flex flex-col gap-1">
        <span class="font-semibold">Parameters</span>
        <MultiSelect
          v-model="chosen"
          :options="options"
          optionLabel="label"
          optionValue="key"
          filter
          display="chip"
          placeholder="Pick parameters"
          :maxSelectedLabels="6"
          size="small"
          class="w-full"
        />
        <small class="text-surface-500">Parameters bound in the project. Selecting rows in the table picks them too.</small>
      </div>

      <div class="flex flex-col gap-3">
        <span class="font-semibold">Show</span>
        <label class="flex items-center gap-2">
          <ToggleSwitch v-model="settings.summary" />
          <span>Summary <span class="text-surface-500">— all parameters on one table</span></span>
        </label>

        <div v-for="kind in sectionKinds" :key="kind" class="rounded-lg border border-surface-200 p-2 flex flex-col gap-2">
          <label class="flex items-center gap-2">
            <ToggleSwitch v-model="settings.sections[kind].enabled" />
            <span class="font-medium">{{ SECTION_LABELS[kind].title }}</span>
          </label>
          <div class="text-xs text-surface-500 -mt-1 ml-12">{{ SECTION_LABELS[kind].hint }}</div>
          <div v-if="settings.sections[kind].enabled" class="flex gap-4 ml-12">
            <label class="flex items-center gap-1.5"><Checkbox v-model="settings.sections[kind].chart" binary />Chart</label>
            <label class="flex items-center gap-1.5"><Checkbox v-model="settings.sections[kind].table" binary />Table</label>
          </div>
          <div v-if="kind === 'values' && settings.sections.values.enabled" class="flex items-center gap-2 ml-12">
            <span class="text-xs">Top</span>
            <InputNumber v-model="settings.topValues" :min="3" :max="50" size="small" inputClass="w-14 text-center" />
            <span class="text-xs">values</span>
          </div>
        </div>
      </div>

      <div class="mt-auto flex flex-col gap-2">
        <Button label="Print" icon="pi pi-print" :disabled="!pages.length || loading" @click="print" />
        <Button label="Refresh" icon="pi pi-refresh" severity="secondary" size="small" :loading="loading" @click="loadReport" />
      </div>
    </aside>

    <!-- Pages: a toolbar row that stays put, the sheets scrolling under it -->
    <section class="report-column grow min-w-0 flex flex-col bg-surface-200">
    <div class="no-print shrink-0 px-4 py-2 flex justify-end items-center gap-2 text-xs text-surface-600 border-b border-surface-300">
      <span v-if="pages.length">{{ pages.length }} {{ pages.length === 1 ? "page" : "pages" }}</span>
      <SelectButton
        v-model="zoomMode"
        :options="[{ l: 'Fit', v: 'fit' }, { l: '100%', v: '100' }]"
        optionLabel="l"
        optionValue="v"
        :allowEmpty="false"
        size="small"
      />
    </div>
    <main ref="preview" class="report-preview grow overflow-auto p-6 relative">

      <div v-if="!chosen.length" class="no-print text-surface-500 text-center mt-24">
        Pick parameters on the left — or select rows in the Shared parameters table.
      </div>
      <div v-else-if="loading && !data" class="no-print text-surface-500 text-center mt-24">
        <i class="pi pi-spin pi-spinner mr-2" />Counting…
      </div>

      <div
        v-for="(page, pi) in pages"
        :key="pi"
        class="sheet-slot mx-auto mb-6"
        :style="{ width: pageWidthPx * scale + 'px', height: pageHeightPx * scale + 'px' }"
      >
        <div class="sheet" :style="{ transform: `scale(${scale})` }">
          <div class="sheet-header">
            <span>{{ settings.title }}</span><span>{{ data?.documentTitle }}</span>
          </div>
          <div class="sheet-body">
            <ReportBlock
              v-for="bi in page"
              :key="blocks[bi].key"
              :block="blocks[bi]"
              :title="settings.title"
              :documentTitle="data?.documentTitle ?? ''"
              :date="date"
              :parameterCount="data?.parameters.length ?? 0"
            />
          </div>
          <div class="sheet-footer">
            <span>{{ date }}</span><span>Page {{ pi + 1 }} / {{ pages.length }}</span>
          </div>
        </div>
      </div>

      <!-- Off-screen measuring copy, at the page's content width -->
      <div ref="measureBox" class="measure" aria-hidden="true">
        <ReportBlock
          v-for="b in blocks"
          :key="b.key"
          :block="b"
          :title="settings.title"
          :documentTitle="data?.documentTitle ?? ''"
          :date="date"
          :parameterCount="data?.parameters.length ?? 0"
          measure
        />
      </div>
    </main>
    </section>
  </div>
</template>

<style scoped>
.sheet {
  width: 210mm;
  height: 297mm;
  padding: 14mm;
  background: white;
  color: #0f172a;
  box-shadow: 0 2px 12px rgb(0 0 0 / 0.18);
  transform-origin: top left;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.sheet-header,
.sheet-footer {
  height: 7mm;
  display: flex;
  justify-content: space-between;
  font-size: 7.5pt;
  color: #64748b;
}
.sheet-header {
  align-items: flex-start;
}
.sheet-footer {
  align-items: flex-end;
}
.sheet-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 4mm;
  min-height: 0;
}
.measure {
  position: absolute;
  left: -10000px;
  top: 0;
  width: calc(210mm - 28mm);
  display: flex;
  flex-direction: column;
  visibility: hidden;
}

@media print {
  .report-root {
    height: auto !important;
    display: block !important;
  }
  .report-column,
  .report-preview {
    display: block !important;
    padding: 0 !important;
    overflow: visible !important;
    background: none !important;
  }
  .sheet-slot {
    width: auto !important;
    height: auto !important;
    margin: 0 !important;
  }
  .sheet {
    transform: none !important;
    box-shadow: none;
    break-after: page;
  }
  .sheet-slot:last-of-type .sheet {
    break-after: auto;
  }
  .measure {
    display: none;
  }
}
</style>
