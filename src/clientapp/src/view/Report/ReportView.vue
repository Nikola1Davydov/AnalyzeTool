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
import Menu from "primevue/menu";
import InputNumber from "primevue/inputnumber";
import ToggleSwitch from "primevue/toggleswitch";
import { invoke } from "@/RevitBridge";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import ReportBlock from "./ReportBlock.vue";
import ParameterPickerDialog from "./ParameterPickerDialog.vue";
import { refKey, useParameterSetsStore } from "@/stores/useParameterSetsStore";
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

// ---- Which parameters, in report order. Comes from the picker, a saved set, or the table's selection.
const sets = useParameterSetsStore();
const chosen = ref<ParameterRef[]>([]);
const activeSetId = ref<string | null>(null);
const pickerVisible = ref(false);

// What the report can count: parameters the project has bound. A set made in another project may
// name ones this project lacks — they stay in the list, greyed, and are skipped.
const bound = computed(() => new Set(project.value.filter((p) => p.bound).map((p) => refKey({ guid: p.guid, name: p.name }))));
const reportable = computed(() => chosen.value.filter((r) => bound.value.has(refKey(r))));
const missingCount = computed(() => chosen.value.length - reportable.value.length);

const activeSet = computed(() => sets.sets.find((s) => s.id === activeSetId.value) ?? null);
const sameList = (a: ParameterRef[], b: ParameterRef[]) => a.map(refKey).join("|") === b.map(refKey).join("|");
const setModified = computed(() => !!activeSet.value && !sameList(activeSet.value.parameters, chosen.value));

function applySet(id: string | null) {
  activeSetId.value = id;
  const set = sets.sets.find((s) => s.id === id);
  if (set) chosen.value = set.parameters.map((p) => ({ ...p }));
}

function removeChosen(index: number) {
  chosen.value = chosen.value.filter((_, i) => i !== index);
}

let takenSelection = "";
function takeSelectionFromTable() {
  const signature = reportSelection.value.map(refKey).join("|");
  if (reportSelection.value.length && signature !== takenSelection) {
    chosen.value = reportSelection.value.map((r) => ({ ...r }));
    activeSetId.value = null; // a fresh pick from the table, not the set that was open
  }
  takenSelection = signature;
}

// The last selection and set come back when the window reopens (a convenience; sets are the durable form).
const SELECTION_KEY = "at.report.selection";
try {
  const saved = JSON.parse(localStorage.getItem(SELECTION_KEY) ?? "null");
  if (Array.isArray(saved?.chosen)) chosen.value = saved.chosen;
  if (typeof saved?.activeSetId === "string") activeSetId.value = saved.activeSetId;
} catch {
  /* no storage: start empty */
}
watch([chosen, activeSetId], () => {
  try {
    localStorage.setItem(SELECTION_KEY, JSON.stringify({ chosen: chosen.value, activeSetId: activeSetId.value }));
  } catch {
    /* storage unavailable */
  }
});

// ---- Saving sets: a small name dialog for "save as" and "rename" -----------------------------------
const nameDialog = ref<{ mode: "new" | "rename"; name: string } | null>(null);
const nameError = computed(() => {
  const d = nameDialog.value;
  if (!d || !d.name.trim()) return "";
  return sets.nameTaken(d.name, d.mode === "rename" ? activeSetId.value ?? undefined : undefined) ? "A set with this name exists." : "";
});

function askName(mode: "new" | "rename") {
  nameDialog.value = { mode, name: mode === "rename" ? activeSet.value?.name ?? "" : "" };
}

async function confirmName() {
  const d = nameDialog.value;
  if (!d || !d.name.trim() || nameError.value) return;
  if (d.mode === "new") {
    const created = await sets.create(d.name, chosen.value);
    if (created) {
      activeSetId.value = created.id;
      notifications.success(`Saved set "${created.name}".`);
    }
  } else if (activeSetId.value) {
    await sets.update(activeSetId.value, { name: d.name.trim() });
  }
  nameDialog.value = null;
}

async function saveChanges() {
  if (activeSet.value && (await sets.update(activeSet.value.id, { parameters: chosen.value })))
    notifications.success(`Updated set "${activeSet.value.name}".`);
}

async function deleteSet() {
  const set = activeSet.value;
  if (!set || !window.confirm(`Delete the set "${set.name}"? The report keeps its current parameters.`)) return;
  if (await sets.remove(set.id)) activeSetId.value = null;
}

const setMenu = ref<InstanceType<typeof Menu> | null>(null);
const setMenuItems = computed(() => [
  { label: "Save as new set…", icon: "pi pi-plus", disabled: !chosen.value.length, command: () => askName("new") },
  { label: "Rename…", icon: "pi pi-pencil", disabled: !activeSet.value, command: () => askName("rename") },
  { label: "Delete set", icon: "pi pi-trash", disabled: !activeSet.value, command: deleteSet },
]);

// ---- Data -----------------------------------------------------------------------------------------
const data = ref<ReportData | null>(null);
const loading = ref(false);
let requestSeq = 0;

async function loadReport() {
  const refs = reportable.value;
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
// Re-counted when what can be counted changes — not when a greyed, unbound entry comes or goes.
watch([() => reportable.value.map(refKey).join("|"), () => settings.value.topValues], () => {
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
  if (!sets.loaded) sets.load();
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

      <div class="flex flex-col gap-2">
        <span class="font-semibold">Parameters</span>

        <!-- Saved sets -->
        <div class="flex items-center gap-1">
          <Select
            :modelValue="activeSetId"
            :options="sets.sorted"
            optionLabel="name"
            optionValue="id"
            placeholder="No saved set"
            emptyMessage="No saved sets yet"
            size="small"
            class="grow min-w-0"
            @update:modelValue="applySet"
          />
          <Button
            icon="pi pi-ellipsis-v"
            size="small"
            text
            severity="secondary"
            aria-haspopup="true"
            v-tooltip.top="'Save, rename, delete sets'"
            @click="setMenu?.toggle($event)"
          />
          <Menu ref="setMenu" :model="setMenuItems" popup />
        </div>
        <div v-if="setModified || (!activeSet && chosen.length)" class="flex items-center gap-2 text-xs">
          <template v-if="setModified">
            <span class="text-amber-700 grow">Changed since saved</span>
            <Button label="Save" size="small" text :loading="sets.busy" @click="saveChanges" />
            <Button label="Revert" size="small" text severity="secondary" @click="applySet(activeSetId)" />
          </template>
          <template v-else>
            <span class="text-surface-500 grow">Not saved as a set</span>
            <Button label="Save as set…" size="small" text @click="askName('new')" />
          </template>
        </div>

        <!-- The chosen ones, in report order -->
        <div class="rounded-lg border border-surface-200 max-h-56 overflow-y-auto">
          <div
            v-for="(r, i) in chosen"
            :key="refKey(r)"
            class="group flex items-center gap-2 pl-2 pr-1 py-1 border-b border-surface-100 last:border-b-0"
            :class="{ 'text-surface-400': !bound.has(refKey(r)) }"
            v-tooltip.right="bound.has(refKey(r)) ? undefined : 'Not bound in this project — skipped'"
          >
            <span class="w-4 text-right text-xs text-surface-400">{{ i + 1 }}</span>
            <span class="grow truncate">{{ r.name }}</span>
            <Button icon="pi pi-times" size="small" text severity="secondary" class="opacity-0 group-hover:opacity-100" @click="removeChosen(i)" />
          </div>
          <div v-if="!chosen.length" class="p-3 text-xs text-surface-500">
            None yet — choose them, open a saved set, or select rows in the Shared parameters table.
          </div>
        </div>
        <small v-if="missingCount" class="text-surface-500">
          {{ missingCount }} not bound in this project — skipped.
        </small>
        <Button label="Choose parameters…" icon="pi pi-list-check" size="small" severity="secondary" @click="pickerVisible = true" />
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

      <div v-if="!reportable.length" class="no-print text-surface-500 text-center mt-24">
        {{ chosen.length ? "None of the chosen parameters is bound in this project." : "Choose parameters on the left — or select rows in the Shared parameters table." }}
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

    <ParameterPickerDialog
      v-model:visible="pickerVisible"
      :modelValue="chosen"
      @apply="(refs) => (chosen = refs)"
    />

    <Dialog
      :visible="!!nameDialog"
      modal
      :header="nameDialog?.mode === 'rename' ? 'Rename set' : 'Save as new set'"
      :style="{ width: 'min(24rem, 95vw)' }"
      @update:visible="!$event && (nameDialog = null)"
    >
      <div v-if="nameDialog" class="flex flex-col gap-1 text-sm">
        <InputText v-model="nameDialog.name" placeholder="e.g. Fire safety" autofocus :invalid="!!nameError" @keydown.enter="confirmName" />
        <small v-if="nameError" class="text-red-600">{{ nameError }}</small>
        <small v-else-if="nameDialog.mode === 'new'" class="text-surface-500">{{ chosen.length }} parameters, in this order.</small>
      </div>
      <template #footer>
        <Button label="Cancel" severity="secondary" text @click="nameDialog = null" />
        <Button label="Save" icon="pi pi-check" :disabled="!nameDialog?.name.trim() || !!nameError" :loading="sets.busy" @click="confirmName" />
      </template>
    </Dialog>
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
