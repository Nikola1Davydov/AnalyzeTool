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
      id: Number((element as any).id ?? 0),
      name: String((element as any).name ?? ""),
      level: String((element as any).level ?? ""),
      category: String((element as any).categoryName ?? ""),
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
const canApply = computed(() => acceptedCount.value > 0);

function rowHasPendingValue(state: RowState) {
  return state.pendingValue.trim() !== "" && state.decision === "idle";
}

function setMode(m: EditMode) {
  mode.value = m;
  applyError.value = "";
}

function accept(i: number) {
  rowState.value[i].decision = "accepted";
}

function reject(i: number) {
  rowState.value[i].decision = "rejected";
}

function undo(i: number) {
  rowState.value[i].decision = "idle";
}

// TODO: Replace mock with real AI backend call — requires new C# command "AnalyzeParameters"
async function runAI() {
  if (!aiPrompt.value.trim()) return;
  aiRunning.value = true;

  await new Promise((resolve) => setTimeout(resolve, 1500));

  rowState.value = rowState.value.map((s, i) => ({
    ...s,
    pendingValue: rows.value[i]?.parameterValue === "(empty)" ? "—" : (rows.value[i]?.parameterValue ?? ""),
    reason: `KI-Vorschlag basierend auf: „${aiPrompt.value}"`,
    decision: "idle",
  }));

  aiRunning.value = false;
}

async function applyToRevit() {
  applyError.value = "";
  const paramItems: ParameterData[] = [];

  for (let i = 0; i < props.items.length; i++) {
    if (rowState.value[i].decision !== "accepted") continue;
    const element = props.items[i];
    const paramMeta = (element.parameters || []).find(
      (p) => p.name === props.selectedParameter,
    );
    if (!paramMeta) continue;
    paramItems.push({ ...paramMeta, value: rowState.value[i].pendingValue });
  }

  if (paramItems.length === 0) return;

  const payload: SetDataToParameters = {
    items: paramItems,
    mode: SetDataToParametersModes.Overwrite,
  };

  try {
    await sendRequest(Commands.SetDataToParameters, payload);
  } catch (err) {
    applyError.value = String(err);
  }
}
</script>

<template>
  <div class="w-full h-full flex flex-col overflow-hidden">
    <!-- Controls bar -->
    <div class="flex items-center gap-1.5 px-2 py-1.5 border-b border-surface-200 shrink-0 flex-wrap bg-surface-50">
      <SelectButton
        :options="MODE_OPTIONS"
        optionLabel="label"
        optionValue="value"
        :modelValue="mode"
        size="small"
        @update:modelValue="(v) => setMode(v as EditMode)"
      />

      <!-- AI bar -->
      <template v-if="mode === 'ai'">
        <InputText
          v-model="aiPrompt"
          size="small"
          placeholder="Prompt eingeben…"
          class="flex-1 min-w-24 !text-[0.72rem]"
          @keydown.enter="runAI"
        />
        <Button
          size="small"
          :loading="aiRunning"
          :disabled="!aiPrompt.trim()"
          label="Analysieren"
          icon="pi pi-sparkles"
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

      <!-- Manual hint -->
      <span
        v-else-if="mode === 'manual'"
        class="flex items-center gap-1.5 text-[0.7rem] text-surface-400"
      >
        <span class="inline-block w-1.5 h-1.5 rounded-full bg-amber-400 shrink-0" />
        Werte manuell eingeben und bestätigen
      </span>
    </div>

    <!-- Scrollable table area -->
    <div class="flex-1 overflow-auto">
      <table class="w-full border-collapse">
        <thead>
          <tr>
            <th class="th-base">ID</th>
            <th class="th-base">Name</th>
            <th class="th-base">Level</th>
            <th class="th-base">Category</th>
            <th class="th-base">{{ selectedParameter || "Parameter" }}</th>
            <template v-if="mode !== 'read'">
              <th class="th-base th-new">
                {{ mode === "ai" ? "✦ KI-Vorschlag" : "Neuer Wert" }}
              </th>
              <th class="th-base th-reason">
                {{ mode === "ai" ? "Begründung" : "Kommentar" }}
              </th>
              <th class="th-base text-center">Aktion</th>
            </template>
          </tr>
        </thead>

        <tbody>
          <tr
            v-for="row in rows"
            :key="row.id"
            :class="{
              'row-accepted': row.state.decision === 'accepted',
              'row-rejected': row.state.decision === 'rejected',
            }"
          >
            <td class="td-base font-mono text-surface-400">{{ row.id }}</td>
            <td class="td-base">{{ row.name }}</td>
            <td class="td-base text-surface-400">{{ row.level }}</td>
            <td class="td-base text-surface-400">{{ row.category }}</td>
            <td class="td-base">{{ row.parameterValue }}</td>

            <template v-if="mode !== 'read'">
              <!-- New value cell -->
              <td class="td-base td-new">
                <span v-if="row.state.decision === 'accepted'" class="pill-accepted">
                  ✓ {{ row.state.pendingValue }}
                </span>
                <span
                  v-else-if="row.state.decision === 'rejected'"
                  class="line-through text-surface-400 font-mono text-[0.7rem]"
                >
                  {{ row.state.pendingValue }}
                </span>
                <InputText
                  v-else
                  size="small"
                  fluid
                  :value="row.state.pendingValue"
                  :placeholder="mode === 'ai' ? '—' : 'Wert eingeben…'"
                  :class="[
                    'cell-input !text-[0.7rem]',
                    mode === 'manual' ? 'cell-input--manual' : '',
                    row.state.pendingValue ? 'cell-input--filled' : '',
                    mode === 'manual' && row.state.pendingValue ? 'cell-input--filled-manual' : '',
                  ]"
                  @input="(e) => (rowState[row.index].pendingValue = (e.target as HTMLInputElement).value)"
                />
              </td>

              <!-- Reason / Comment cell -->
              <td class="td-base td-reason">
                <span
                  v-if="mode === 'ai' && row.state.reason"
                  class="reason-text text-[0.7rem]"
                >
                  {{ row.state.reason }}
                </span>
                <InputText
                  v-else-if="mode === 'manual'"
                  size="small"
                  fluid
                  :value="row.state.comment"
                  placeholder="Kommentar…"
                  class="cell-input cell-input--manual !text-[0.7rem]"
                  @input="(e) => (rowState[row.index].comment = (e.target as HTMLInputElement).value)"
                />
                <span v-else class="text-surface-300 text-[0.7rem]">—</span>
              </td>

              <!-- Actions cell -->
              <td class="td-base text-center whitespace-nowrap">
                <template v-if="row.state.decision === 'accepted'">
                  <Tag value="✓ OK" severity="success" class="!text-[0.65rem] !py-0" />
                  <Button
                    icon="pi pi-undo"
                    text
                    rounded
                    size="small"
                    class="!w-6 !h-6"
                    title="Rückgängig"
                    @click="undo(row.index)"
                  />
                </template>
                <template v-else-if="row.state.decision === 'rejected'">
                  <Tag value="Verworfen" severity="danger" class="!text-[0.65rem] !py-0" />
                  <Button
                    icon="pi pi-undo"
                    text
                    rounded
                    size="small"
                    class="!w-6 !h-6"
                    title="Rückgängig"
                    @click="undo(row.index)"
                  />
                </template>
                <template v-else-if="rowHasPendingValue(row.state)">
                  <Button
                    icon="pi pi-check"
                    severity="success"
                    text
                    rounded
                    size="small"
                    class="!w-6 !h-6"
                    title="Akzeptieren"
                    @click="accept(row.index)"
                  />
                  <Button
                    icon="pi pi-times"
                    severity="danger"
                    text
                    rounded
                    size="small"
                    class="!w-6 !h-6"
                    title="Ablehnen"
                    @click="reject(row.index)"
                  />
                </template>
                <span v-else class="text-surface-300 text-[0.7rem]">—</span>
              </td>
            </template>
          </tr>
        </tbody>

        <tfoot>
          <tr>
            <td
              :colspan="mode !== 'read' ? 5 : 5"
              class="sticky bottom-0 bg-surface-50 border-t border-surface-200 px-2 py-1.5 font-mono text-[0.65rem] text-surface-400"
            >
              Gesamt: <b class="text-surface-700 font-semibold">{{ totalCount }}</b>
              <template v-if="mode !== 'read'">
                &nbsp;·&nbsp;
                <span class="text-emerald-400">✓ {{ acceptedCount }}</span>
                &nbsp;·&nbsp;
                <span class="text-red-400">✕ {{ rejectedCount }}</span>
              </template>
            </td>
            <td
              v-if="mode !== 'read'"
              colspan="3"
              class="sticky bottom-0 bg-surface-50 border-t border-surface-200 px-2 py-1.5 text-right"
            >
              <span v-if="applyError" class="text-[0.65rem] text-red-400 mr-2">{{ applyError }}</span>
              <Button
                size="small"
                label="In Revit übernehmen"
                icon="pi pi-send"
                severity="success"
                outlined
                :disabled="!canApply"
                @click="applyToRevit"
                class="!text-[0.7rem]"
              />
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  </div>
</template>

<style scoped>
/* Reusable table cell base — Tailwind @apply keeps th/td DRY */
.th-base {
  @apply sticky top-0 z-10 bg-surface-50 text-surface-400 font-medium text-[0.65rem] uppercase tracking-wider px-2 py-1.5 text-left border-b border-surface-200 whitespace-nowrap;
}

.th-new {
  @apply bg-[rgba(124,106,255,0.05)] text-[#a394ff];
}

.th-reason {
  @apply text-amber-400;
}

.td-base {
  @apply border-b border-surface-100 px-2 py-1.5 text-[0.78rem] align-middle text-surface-700;
}

.td-new {
  @apply bg-[rgba(124,106,255,0.025)];
}

.td-reason {
  @apply bg-[rgba(240,168,67,0.02)];
}

/* Row state backgrounds */
.row-accepted .td-base {
  background: rgba(45, 212, 160, 0.06) !important;
}

.row-rejected {
  opacity: 0.38;
}

/* Cell inputs — slim overrides on PrimeVue InputText */
.cell-input {
  @apply !font-mono;
}

.cell-input--manual {
  @apply !border-amber-400/30 focus:!border-amber-400 focus:!ring-amber-400/20;
}

.cell-input--filled {
  @apply !text-[#a394ff] !border-[rgba(124,106,255,0.35)];
}

.cell-input--filled-manual {
  @apply !text-amber-400 !border-amber-400/50;
}

/* AI suggestion accepted pill */
.pill-accepted {
  @apply inline-flex items-center gap-1 bg-[rgba(124,106,255,0.12)] text-[#a394ff] px-2 py-0.5 rounded font-mono text-[0.7rem] font-medium;
}

/* AI reason text */
.reason-text {
  @apply text-amber-400 block max-w-[180px];
  opacity: 0.9;
}
</style>
