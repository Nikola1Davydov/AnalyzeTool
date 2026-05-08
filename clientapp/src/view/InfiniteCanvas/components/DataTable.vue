<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from "vue";
import type { ParameterData, ParameterEdit, SetDataToParameters } from "@/stores/types";
import { SetDataToParametersModes } from "@/stores/types";
import type { ElementItem } from "@/stores/types";
import { Commands, sendRequest } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";
const emit = defineEmits<{ refresh: [] }>();
const notificationStore = useNotificationStore();
const aiSettingsStore = useAiSettingsStore();
const aiAvailable = computed(() => !!aiSettingsStore.selectedModel);

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
const aiRawRunning = ref(false);
const rawAiResponse = ref<string | null>(null);
const showRawPanel = ref(false);
const rowState = ref<RowState[]>([]);

watch(
  () => props.items,
  (next, prev) => {
    // Сохраняем существующее состояние по elementId, чтобы refresh не сбрасывал правки
    const prevById = new Map<number, RowState>();
    (prev || []).forEach((element, i) => {
      if (rowState.value[i]) prevById.set(element.id, rowState.value[i]);
    });

    rowState.value = (next || []).map((element) => {
      return (
        prevById.get(element.id) ?? {
          pendingValue: "",
          comment: "",
          reason: "",
          decision: "idle" as RowDecision,
        }
      );
    });
  },
  { immediate: true },
);

const rows = computed(() => {
  const result: {
    index: number;
    id: number;
    name: string;
    level: string;
    category: string;
    parameterValue: string;
    isReadOnly: boolean;
    state: RowState;
  }[] = [];

  (props.items || []).forEach((element, i) => {
    const param = (element.parameters || []).find((p) => p?.name === props.selectedParameter);
    if (!param) return;
    result.push({
      index: i,
      id: element.id,
      name: element.name,
      level: element.level,
      category: element.categoryName,
      parameterValue: param.value === "" ? "(empty)" : String(param.value),
      isReadOnly: param.isReadOnly,
      state: rowState.value[i] ?? {
        pendingValue: "",
        comment: "",
        reason: "",
        decision: "idle" as RowDecision,
      },
    });
  });

  return result;
});

// Set of original props.items indices that are read-only for the selected parameter
const readOnlyIndices = computed(() => {
  const set = new Set<number>();
  (props.items || []).forEach((element, i) => {
    const param = (element.parameters || []).find((p) => p?.name === props.selectedParameter);
    if (param?.isReadOnly) set.add(i);
  });
  return set;
});

const totalCount = computed(() => rows.value.length);
const hasEditableRows = computed(() => rows.value.some((r) => !r.isReadOnly));
const acceptedCount = computed(
  () => rowState.value.filter((s) => s.decision === "accepted").length,
);
const rejectedCount = computed(
  () => rowState.value.filter((s) => s.decision === "rejected").length,
);
const filledCount = computed(
  () => rowState.value.filter((s) => s.pendingValue.trim() !== "").length,
);
const canApply = computed(
  () =>
    mode.value !== "read" &&
    rowState.value.some(
      (s, i) =>
        !readOnlyIndices.value.has(i) && s.decision !== "rejected" && s.pendingValue.trim() !== "",
    ),
);

function getRowClass(data: (typeof rows.value)[number]) {
  if (data.isReadOnly) return "row-readonly";
  if (data.state.decision === "rejected") return "row-rejected";
  if (data.state.pendingValue.trim() !== "") return "row-accepted";
  return "";
}

function setMode(m: EditMode) {
  mode.value = m;
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
let aiMessageCleanup: (() => void) | null = null;

function runAI() {
  if (!aiPrompt.value.trim()) return;

  aiMessageCleanup?.();

  aiRunning.value = true;

  const paramItems = props.items
    .map((el) => el.parameters.find((p) => p.name === props.selectedParameter))
    .filter((p): p is ParameterData => p != null);

  function onAiResult(event: Event) {
    cleanup();

    const detail = (event as CustomEvent).detail as {
      edits: ParameterEdit[] | null;
      raw: string | null;
      error: string | null;
    };

    rawAiResponse.value = detail.raw ?? null;

    if (detail.error) {
      notificationStore.error(detail.error);
      return;
    }

    if (!detail.edits || !Array.isArray(detail.edits)) return;

    const byElementId = new Map(detail.edits.map((e) => [e.ElementId, e]));
    let appliedCount = 0;
    rowState.value = rowState.value.map((s, i) => {
      if (readOnlyIndices.value.has(i)) return s;
      const edit = byElementId.get(props.items[i]?.id);
      if (!edit) return s;
      appliedCount++;
      return {
        ...s,
        pendingValue: String(edit.NewValue ?? ""),
        reason: String(edit.Reason ?? ""),
        decision: "accepted",
      };
    });
    notificationStore.success(`KI fertig — ${appliedCount} Vorschläge bereit`);
  }

  function cleanup() {
    window.removeEventListener("revit:ai-edit", onAiResult);
    aiRunning.value = false;
    aiMessageCleanup = null;
  }

  aiMessageCleanup = cleanup;
  window.addEventListener("revit:ai-edit", onAiResult);
  sendRequest(Commands.ParametersEditWithAi, {
    items: paramItems,
    prompt: aiPrompt.value,
    model: aiSettingsStore.selectedModel!,
  });
}

let aiRawCleanup: (() => void) | null = null;

function runAIRaw() {
  if (!aiPrompt.value.trim()) return;

  aiRawCleanup?.();
  aiRawRunning.value = true;
  rawAiResponse.value = null;

  const paramItems = props.items
    .map((el) => el.parameters.find((p) => p.name === props.selectedParameter))
    .filter((p): p is ParameterData => p != null);

  function onRawResult(event: Event) {
    cleanupRaw();
    const detail = (event as CustomEvent).detail;
    if (detail?.error) {
      notificationStore.error(detail.error);
      return;
    }
    rawAiResponse.value = typeof detail === "string" ? detail : JSON.stringify(detail);
    showRawPanel.value = true;
    notificationStore.info("KI-Analyse abgeschlossen");
  }

  function cleanupRaw() {
    window.removeEventListener("revit:ai-raw", onRawResult);
    aiRawRunning.value = false;
    aiRawCleanup = null;
  }

  aiRawCleanup = cleanupRaw;
  window.addEventListener("revit:ai-raw", onRawResult);
  sendRequest(Commands.AnalyseWithAi, {
    items: paramItems,
    prompt: aiPrompt.value,
    model: aiSettingsStore.selectedModel!,
  });
}

onBeforeUnmount(() => {
  aiMessageCleanup?.();
  aiRawCleanup?.();
});

function applyToRevit() {
  const paramItems: ParameterData[] = [];

  for (let i = 0; i < props.items.length; i++) {
    const s = rowState.value[i];
    if (readOnlyIndices.value.has(i)) continue;
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

  try {
    sendRequest(Commands.SetDataToParameters, payload);

    // Сбрасываем состояние применённых строк
    rowState.value = rowState.value.map((s, i) => {
      if (readOnlyIndices.value.has(i)) return s;
      if (s.decision === "rejected" || s.pendingValue.trim() === "") return s;
      return { ...s, pendingValue: "", reason: "", decision: "idle" };
    });

    setTimeout(() => emit("refresh"), 800);
  } catch (err) {
    notificationStore.error(String(err));
  }
}
</script>

<template>
  <div class="w-full h-full flex flex-row overflow-hidden">
    <!-- Main area -->
    <div class="flex-1 flex flex-col overflow-hidden min-w-0">
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
            :loading="aiRawRunning"
            :disabled="!aiPrompt.trim() || aiRawRunning || aiRunning"
            label="Analysieren"
            icon="pi pi-sparkles"
            @click="runAIRaw"
          />
          <Button
            size="small"
            :loading="aiRunning"
            :disabled="!aiPrompt.trim() || aiRunning || aiRawRunning || !hasEditableRows"
            label="Bearbeiten"
            icon="pi pi-pen-to-square"
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

        <Column field="name" header="Name" headerClass="!text-[0.65rem]" sortable />

        <Column field="level" header="Level" headerClass="!text-[0.65rem]" sortable>
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
          sortable
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
            <span
              v-if="data.isReadOnly"
              class="flex items-center gap-1 text-[0.68rem] text-surface-400 italic"
            >
              <i class="pi pi-lock text-[0.6rem]" />
              schreibgeschützt
            </span>
            <InputText
              v-else
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
            <template v-if="!data.isReadOnly">
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
            <div class="flex items-center gap-2">
              <Button
                v-if="rawAiResponse"
                icon="pi pi-eye"
                severity="warn"
                text
                rounded
                size="small"
                class="!w-6 !h-6 shrink-0"
                title="KI-Antwort anzeigen"
                @click="showRawPanel = true"
              />

              <Button
                v-if="mode !== 'read'"
                size="small"
                label="In Revit übernehmen"
                icon="pi pi-send"
                severity="success"
                outlined
                :disabled="!canApply"
                class="!text-[0.7rem]"
                @click="applyToRevit"
              />
            </div>
          </div>
        </template>
      </DataTable>
    </div>
    <!-- end main area -->

    <!-- AI response side panel -->
    <Transition name="side-panel">
      <div
        v-if="showRawPanel"
        class="w-72 shrink-0 flex flex-col border-l border-surface-200 bg-surface-50"
      >
        <div
          class="flex items-center justify-between px-3 py-2 border-b border-surface-200 shrink-0"
        >
          <span class="text-[0.7rem] font-semibold text-amber-500 flex items-center gap-1">
            <i class="pi pi-sparkles text-[0.65rem]" />
            KI-Antwort
          </span>
          <Button
            icon="pi pi-times"
            text
            rounded
            size="small"
            class="!w-5 !h-5"
            @click="showRawPanel = false"
          />
        </div>
        <div class="flex-1 overflow-auto p-3">
          <pre
            class="text-[0.68rem] whitespace-pre-wrap break-words font-mono text-surface-600 leading-relaxed"
            >{{ rawAiResponse ?? (aiRawRunning ? "Lädt…" : "") }}</pre
          >
        </div>
      </div>
    </Transition>
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

:deep(.row-readonly td) {
  background: rgba(0, 0, 0, 0.015) !important;
  color: var(--p-surface-400) !important;
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

/* Side panel transition */
.side-panel-enter-active,
.side-panel-leave-active {
  transition:
    width 0.2s ease,
    opacity 0.2s ease;
  overflow: hidden;
}
.side-panel-enter-from,
.side-panel-leave-to {
  width: 0 !important;
  opacity: 0;
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
