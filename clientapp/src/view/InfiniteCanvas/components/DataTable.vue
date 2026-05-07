<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { ParameterData, SetDataToParameters } from "@/stores/types";
import { SetDataToParametersModes } from "@/stores/types";
import type { ElementItem } from "@/stores/types";
import { Commands, sendRequest } from "@/RevitBridge";

const props = defineProps<{
  items: ElementItem[];
  selectedParameter?: string | null;
}>();

type EditMode = "read" | "manual" | "ai";
type RowDecision = "idle" | "accepted" | "rejected";
type RowState = {
  pendingValue: string;
  comment: string;
  reason: string;
  decision: RowDecision;
};

const MODE_OPTIONS = [
  { label: "Lesen", value: "read" },
  { label: "✎ Manuell", value: "manual" },
  { label: "✦ KI", value: "ai" },
];

const AI_CHIPS = [
  "Fehlende Werte ergänzen",
  "Grammatik prüfen",
  "Werte normalisieren",
  "Duplikate erkennen",
];

const mode = ref<EditMode>("read");
const aiPrompt = ref("");
const aiRunning = ref(false);
const applying = ref(false);
const rowState = ref<RowState[]>([]);
const applyError = ref("");

watch(
  () => props.items,
  (next) => {
    rowState.value = (next || []).map(() => ({
      pendingValue: "",
      comment: "",
      reason: "",
      decision: "idle",
    }));
  },
  { immediate: true },
);

const rows = computed(() =>
  (props.items || []).map((element, i) => {
    const selectedValue = (element.parameters || []).find(
      (p) => p?.name === props.selectedParameter,
    )?.value;
    return {
      index: i,
      id: element.id,
      name: element.name,
      level: element.level,
      category: element.categoryName,
      parameterValue:
        selectedValue === undefined || selectedValue === null || selectedValue === ""
          ? "(empty)"
          : String(selectedValue),
      state: rowState.value[i] ?? {
        pendingValue: "",
        comment: "",
        reason: "",
        decision: "idle" as RowDecision,
      },
    };
  }),
);

const totalCount = computed(() => rows.value.length);
const acceptedCount = computed(
  () => rowState.value.filter((s) => s.decision === "accepted").length,
);
const rejectedCount = computed(
  () => rowState.value.filter((s) => s.decision === "rejected").length,
);
const filledCount = computed(
  () => rowState.value.filter((s) => s.pendingValue.trim() !== "").length,
);
const canApply = computed(() =>
  mode.value !== "read" &&
  rowState.value.some((s) => s.decision !== "rejected" && s.pendingValue.trim() !== ""),
);

function getRowClass(data: (typeof rows.value)[number]) {
  if (data.state.decision === "rejected") return "row-rejected";
  if (data.state.pendingValue.trim() !== "") return "row-accepted";
  return "";
}

function setMode(m: EditMode) {
  mode.value = m;
  applyError.value = "";
}

function reject(i: number) {
  rowState.value[i].decision = "rejected";
}

function undo(i: number) {
  rowState.value[i].decision = "idle";
}

function onModeChange(v: string) {
  setMode(v as EditMode);
}

function onPendingInput(index: number, e: Event) {
  rowState.value[index].pendingValue = (e.target as HTMLInputElement).value;
}
// TODO: Replace mock with real AI backend call — requires new C# command "AnalyzeParameters"
async function runAI() {
  if (!aiPrompt.value.trim()) return;
  aiRunning.value = true;

  await new Promise((resolve) => setTimeout(resolve, 1500));

  rowState.value = rowState.value.map((s, i) => ({
    ...s,
    pendingValue:
      rows.value[i]?.parameterValue === "(empty)" ? "—" : (rows.value[i]?.parameterValue ?? ""),
    reason: `KI-Vorschlag basierend auf: „${aiPrompt.value}"`,
    decision: "accepted",
  }));

  aiRunning.value = false;
}

async function applyToRevit() {
  applyError.value = "";
  const paramItems: ParameterData[] = [];

  for (let i = 0; i < props.items.length; i++) {
    const s = rowState.value[i];
    if (s.decision === "rejected" || s.pendingValue.trim() === "") continue;
    const element = props.items[i];
    const paramMeta = (element.parameters || []).find((p) => p.name === props.selectedParameter);
    if (!paramMeta) continue;
    paramItems.push({ ...paramMeta, value: rowState.value[i].pendingValue });
  }

  if (paramItems.length === 0) return;

  const payload: SetDataToParameters = {
    items: paramItems,
    mode: SetDataToParametersModes.Overwrite,
  };

  applying.value = true;
  try {
    await sendRequest(Commands.SetDataToParameters, payload);
  } catch (err) {
    applyError.value = String(err);
  } finally {
    applying.value = false;
  }
}
</script>

<template>
  <div class="w-full h-full flex flex-col overflow-hidden">
    <!-- Controls bar -->
    <div
      class="flex items-center gap-1.5 px-2 py-1.5 border-b border-surface-200 shrink-0 flex-wrap bg-surface-50"
    >
      <SelectButton
        :options="MODE_OPTIONS"
        optionLabel="label"
        optionValue="value"
        :modelValue="mode"
        size="small"
        @update:modelValue="onModeChange"
      />

      <template v-if="mode === 'ai'">
        <InputText
          v-model="aiPrompt"
          size="small"
          placeholder="Prompt eingeben…"
          class="flex-1 min-w-24 !text-xs"
          @keydown.enter="runAI"
        />
        <Button
          size="small"
          :loading="aiRunning"
          disabled
          label="Analysieren"
          icon="pi pi-sparkles"
          title="KI-Analyse noch nicht verfügbar"
          @click="runAI"
        />
        <div class="flex gap-1 flex-wrap">
          <Button
            v-for="chip in AI_CHIPS"
            :key="chip"
            size="small"
            text
            :label="chip"
            class="!text-[0.68rem] !py-0.5 !px-2"
            @click="aiPrompt = chip"
          />
        </div>
      </template>

      <span
        v-else-if="mode === 'manual'"
        class="flex items-center gap-1.5 text-xs text-surface-400"
      >
        <span class="inline-block w-1.5 h-1.5 rounded-full bg-amber-400 shrink-0" />
        Werte manuell eingeben und bestätigen
      </span>
    </div>

    <!-- PrimeVue DataTable -->
    <DataTable
      :value="rows"
      size="small"
      scrollable
      scrollHeight="flex"
      :rowClass="getRowClass"
      class="flex-1 min-h-0 text-xs"
    >
      <!-- Static columns -->
      <Column field="id" header="ID" headerClass="!text-[0.65rem]">
        <template #body="{ data }">
          <span class="font-mono text-surface-400 text-[0.72rem]">{{ data.id }}</span>
        </template>
      </Column>

      <Column field="name" header="Name" headerClass="!text-[0.65rem]" />

      <Column field="level" header="Level" headerClass="!text-[0.65rem]">
        <template #body="{ data }">
          <span class="text-surface-400 text-[0.72rem]">{{ data.level }}</span>
        </template>
      </Column>

      <Column field="category" header="Category" headerClass="!text-[0.65rem]">
        <template #body="{ data }">
          <span class="text-surface-400 text-[0.72rem]">{{ data.category }}</span>
        </template>
      </Column>

      <Column
        field="parameterValue"
        :header="selectedParameter || 'Parameter'"
        headerClass="!text-[0.65rem]"
      />

      <!-- Edit columns (manual / ai mode only) -->
      <Column
        v-if="mode !== 'read'"
        :header="mode === 'ai' ? '✦ KI-Vorschlag' : 'Neuer Wert'"
        headerClass="col-new-header !text-[0.65rem]"
        class="col-new-cell"
      >
        <template #body="{ data }">
          <InputText
            size="small"
            fluid
            :value="data.state.pendingValue"
            :placeholder="mode === 'ai' ? '—' : 'Wert eingeben…'"
            :disabled="data.state.decision === 'rejected'"
            :class="[
              'cell-input !text-[0.7rem]',
              mode === 'manual' ? 'cell-input--manual' : '',
              data.state.pendingValue ? 'cell-input--filled' : '',
              mode === 'manual' && data.state.pendingValue ? 'cell-input--filled-manual' : '',
            ]"
            @input="onPendingInput(data.index, $event)"
          />
        </template>
      </Column>

      <Column
        v-if="mode === 'ai'"
        :header="'Begründung'"
        headerClass="col-reason-header !text-[0.65rem]"
        class="col-reason-cell"
      >
        <template #body="{ data }">
          <span v-if="data.state.reason" class="reason-text">{{ data.state.reason }}</span>
          <span v-else class="text-surface-300 text-[0.7rem]">—</span>
        </template>
      </Column>

      <Column
        v-if="mode === 'ai'"
        header=""
        headerClass="!text-[0.65rem] !text-center"
        class="!text-center"
        style="width: 2.5rem"
      >
        <template #body="{ data }">
          <Button
            v-if="data.state.decision !== 'rejected'"
            icon="pi pi-times"
            severity="danger"
            text
            rounded
            size="small"
            class="!w-6 !h-6"
            title="Ausschließen"
            @click="reject(data.index)"
          />
          <Button
            v-else
            icon="pi pi-undo"
            text
            rounded
            size="small"
            class="!w-6 !h-6"
            title="Wiederherstellen"
            @click="undo(data.index)"
          />
        </template>
      </Column>

      <!-- Footer -->
      <template #footer>
        <div class="flex items-center justify-between px-1">
          <span class="font-mono text-[0.65rem] text-surface-400">
            Gesamt: <b class="text-surface-700 font-semibold">{{ totalCount }}</b>
            <template v-if="mode === 'manual'">
              &nbsp;·&nbsp;<span class="text-emerald-400">✎ {{ filledCount }}</span>
            </template>
            <template v-else-if="mode === 'ai'">
              &nbsp;·&nbsp;<span class="text-emerald-400">✓ {{ acceptedCount }}</span>
              &nbsp;·&nbsp;<span class="text-red-400">✕ {{ rejectedCount }}</span>
            </template>
          </span>
          <div v-if="mode !== 'read'" class="flex items-center gap-2">
            <span v-if="applyError" class="text-[0.65rem] text-red-400">{{ applyError }}</span>
            <Button
              size="small"
              label="In Revit übernehmen"
              icon="pi pi-send"
              severity="success"
              outlined
              :disabled="!canApply || applying"
              :loading="applying"
              class="!text-[0.7rem]"
              @click="applyToRevit"
            />
          </div>
        </div>
      </template>
    </DataTable>
  </div>
</template>

<style scoped>
/* Custom column header colors */
:deep(.col-new-header) {
  background: rgba(124, 106, 255, 0.05) !important;
  color: #a394ff !important;
}

:deep(.col-new-cell) {
  background: rgba(124, 106, 255, 0.025);
}

:deep(.col-reason-header) {
  color: #f0a843 !important;
}

:deep(.col-reason-cell) {
  background: rgba(240, 168, 67, 0.02);
}

/* Row state backgrounds */
:deep(.row-accepted td) {
  background: rgba(45, 212, 160, 0.06) !important;
}

:deep(.row-rejected) {
  opacity: 0.38;
}

/* Cell inputs */
.cell-input {
  font-family: monospace !important;
}

.cell-input--manual {
  border-color: rgba(251, 191, 36, 0.3) !important;
}

.cell-input--manual:focus {
  border-color: rgb(251, 191, 36) !important;
}

.cell-input--filled {
  color: #a394ff !important;
  border-color: rgba(124, 106, 255, 0.35) !important;
}

.cell-input--filled-manual {
  color: rgb(251, 191, 36) !important;
  border-color: rgba(251, 191, 36, 0.5) !important;
}

/* AI reason text */
.reason-text {
  color: rgb(251, 191, 36);
  font-size: 0.7rem;
  display: block;
  max-width: 180px;
  opacity: 0.9;
}
</style>
