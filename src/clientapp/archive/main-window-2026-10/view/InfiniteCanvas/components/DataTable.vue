<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { ParameterData, SetDataToParameters } from "@/stores/types";
import { SetDataToParametersModes } from "@/stores/types";
import type { ElementItem } from "@/stores/types";
import { Commands, invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";
const emit = defineEmits<{ refresh: [] }>();
const notificationStore = useNotificationStore();

const props = defineProps<{
  items: ElementItem[];
  selectedParameter?: string | null;
}>();

type EditMode = "read" | "manual";
type RowState = { pendingValue: string };

const MODE_OPTIONS = [
  { label: "Read", value: "read" },
  { label: "✎ Manual", value: "manual" },
];

const mode = ref<EditMode>("read");
const rowState = ref<RowState[]>([]);

watch(
  () => props.items,
  (next, prev) => {
    // Preserve existing state by elementId so refresh does not reset edits
    const prevById = new Map<number, RowState>();
    (prev || []).forEach((element, i) => {
      if (rowState.value[i]) prevById.set(element.id, rowState.value[i]);
    });

    rowState.value = (next || []).map((element) => prevById.get(element.id) ?? { pendingValue: "" });
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
      state: rowState.value[i] ?? { pendingValue: "" },
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
const filledCount = computed(
  () => rowState.value.filter((s) => s.pendingValue.trim() !== "").length,
);
const canApply = computed(
  () =>
    mode.value !== "read" &&
    rowState.value.some((s, i) => !readOnlyIndices.value.has(i) && s.pendingValue.trim() !== ""),
);

function getRowClass(data: (typeof rows.value)[number]) {
  if (data.isReadOnly) return "row-readonly";
  if (data.state.pendingValue.trim() !== "") return "row-accepted";
  return "";
}

function onModeChange(v: string) {
  mode.value = v as EditMode;
}

function onPendingInput(index: number, e: Event) {
  rowState.value[index].pendingValue = (e.target as HTMLInputElement).value;
}

function applyToRevit() {
  const paramItems: ParameterData[] = [];

  for (let i = 0; i < props.items.length; i++) {
    const s = rowState.value[i];
    if (readOnlyIndices.value.has(i)) continue;
    if (s.pendingValue.trim() === "") continue;
    const element = props.items[i];
    const paramMeta = (element.parameters || []).find((p) => p.name === props.selectedParameter);
    if (!paramMeta) continue;
    paramItems.push({ ...paramMeta, value: s.pendingValue });
  }

  if (paramItems.length === 0) return;

  const payload: SetDataToParameters = {
    items: paramItems,
    mode: SetDataToParametersModes.Overwrite,
  };

  try {
    invoke(Commands.SetDataToParameters, payload).catch((err) =>
      notificationStore.error(String((err as Error)?.message ?? err)),
    );

    // Reset state for applied rows
    rowState.value = rowState.value.map((s, i) =>
      readOnlyIndices.value.has(i) || s.pendingValue.trim() === "" ? s : { pendingValue: "" },
    );

    setTimeout(() => emit("refresh"), 800);
  } catch (err) {
    notificationStore.error(String(err));
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

      <span v-if="mode === 'manual'" class="flex items-center gap-1.5 text-xs text-surface-400">
        <span class="inline-block w-1.5 h-1.5 rounded-full bg-amber-400 shrink-0" />
        Enter values manually and confirm
      </span>
    </div>

    <!-- PrimeVue DataTable -->
    <DataTable
      :value="rows"
      size="small"
      scrollable
      scrollHeight="flex"
      :virtualScrollerOptions="{ itemSize: 36 }"
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

      <Column
        field="parameterValue"
        sortable
        :header="selectedParameter || 'Parameter'"
        headerClass="!text-[0.65rem]"
      />

      <!-- Edit column (manual mode only) -->
      <Column
        v-if="mode !== 'read'"
        header="New Value"
        headerClass="col-new-header !text-[0.65rem]"
        class="col-new-cell"
      >
        <template #body="{ data }">
          <span
            v-if="data.isReadOnly"
            class="flex items-center gap-1 text-[0.68rem] text-surface-400 italic"
          >
            <i class="pi pi-lock text-[0.6rem]" />
            read-only
          </span>
          <InputText
            v-else
            size="small"
            fluid
            :value="data.state.pendingValue"
            placeholder="Enter value..."
            :class="[
              'cell-input cell-input--manual !text-[0.7rem]',
              data.state.pendingValue ? 'cell-input--filled-manual' : '',
            ]"
            @input="onPendingInput(data.index, $event)"
          />
        </template>
      </Column>

      <!-- Footer -->
      <template #footer>
        <div class="flex items-center justify-between px-1">
          <span class="font-mono text-[0.65rem] text-surface-400">
            Total: <b class="text-surface-700 font-semibold">{{ totalCount }}</b>
            <template v-if="mode === 'manual'">
              &nbsp;·&nbsp;<span class="text-emerald-400">✎ {{ filledCount }}</span>
            </template>
          </span>
          <Button
            v-if="mode !== 'read'"
            size="small"
            label="Apply to Revit"
            icon="pi pi-send"
            severity="success"
            outlined
            :disabled="!canApply"
            class="!text-[0.7rem]"
            @click="applyToRevit"
          />
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

/* Row state backgrounds */
:deep(.row-accepted td) {
  background: rgba(45, 212, 160, 0.06) !important;
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

.cell-input--filled-manual {
  color: rgb(251, 191, 36) !important;
  border-color: rgba(251, 191, 36, 0.5) !important;
}
</style>
