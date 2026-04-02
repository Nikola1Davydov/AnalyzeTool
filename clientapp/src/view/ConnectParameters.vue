<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { Commands, sendRequest } from "@/RevitBridge";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import { useElementsStore } from "@/stores/useElementsStore";
import type { ApplyCombinedParametersPayload, ElementItem, ParameterData } from "@/stores/types";

type ApplyMode = "Overwrite" | "OnlyIfEmpty" | "SkipIfEqual";
type TargetKind = "Text" | "Number" | "Unknown";
type TargetNumberKind = "int" | "double" | "unknown";

type RuleBlock = {
  id: number;
  kind: "parameter" | "text" | "number";
  parameterName: string | null;
  literal: string;
};

type ParameterOption = {
  key: string;
  name: string;
  scopeLabel: "Type" | "Instance";
  originLabel: "Shared" | "Project" | "BuildIn" | "Unknown";
  valueTypeLabel: "String" | "Number" | "Unknown";
  storageTypeLabel: string;
  isReadOnly: boolean;
};

type PreviewRow = {
  elementId: number;
  elementName: string;
  elementScope: "Type" | "Instance";
  oldValue: string;
  newValue: string;
  status: "Changed" | "Same" | "Error";
  errors: string[];
};

const categoriesStore = useCategoriesStore();
const elementsStore = useElementsStore();

const { sortedCategories } = storeToRefs(categoriesStore);
const { items } = storeToRefs(elementsStore);

const selectedCategory = ref<string | null>(null);
const searchQuery = ref("");
const selectedFilters = ref<string[]>([]);
const targetParameterName = ref<string | null>(null);
const applyMode = ref<ApplyMode>("Overwrite");

const loading = ref(false);
const applying = ref(false);
const sendInfo = ref("");
const draggedBlockIndex = ref<number | null>(null);
const editableRowMap = ref<Record<number, boolean>>({});

const blockIdCounter = ref(1);
const blocks = ref<RuleBlock[]>([{ id: 1, kind: "parameter", parameterName: null, literal: "" }]);

const filterOptions = ["Instance", "Type", "BuildIn", "Schared", "Project"];
const applyModes: { label: string; value: ApplyMode }[] = [
  { label: "Overwrite", value: "Overwrite" },
  { label: "Only if target empty", value: "OnlyIfEmpty" },
  { label: "Skip if equal", value: "SkipIfEqual" },
];

function normalizeStorageType(storageType: string | null | undefined): string {
  return (storageType || "").trim().toLowerCase();
}

function storageTypeToValueType(
  storageType: string | null | undefined,
): "String" | "Number" | "Unknown" {
  const normalized = normalizeStorageType(storageType);
  if (!normalized) return "Unknown";
  if (normalized.includes("string")) return "String";
  if (
    normalized.includes("double") ||
    normalized.includes("int") ||
    normalized.includes("integer")
  ) {
    return "Number";
  }
  return "Unknown";
}

function isStorageTypeNumeric(storageType: string | null | undefined): boolean {
  return storageTypeToValueType(storageType) === "Number";
}

const filteredElements = computed(() => {
  const search = searchQuery.value.trim().toLowerCase();
  const scopeFilters = selectedFilters.value.filter((f) => f === "Instance" || f === "Type");
  const originFilters = selectedFilters.value.filter(
    (f) => f === "BuildIn" || f === "Schared" || f === "Project",
  );

  function matchScope(param: ParameterData): boolean {
    if (scopeFilters.length === 0) return true;
    if (param.isTypeParameter) return scopeFilters.includes("Type");
    return scopeFilters.includes("Instance");
  }

  function matchOrigin(param: ParameterData): boolean {
    if (originFilters.length === 0) return true;
    if (param.orgin === 0) return originFilters.includes("Schared");
    if (param.orgin === 1) return originFilters.includes("Project");
    if (param.orgin === 2) return originFilters.includes("BuildIn");
    return false;
  }

  return (items.value || [])
    .map((element) => {
      const parameters = (element.parameters || []).filter((param) => {
        const nameMatch = !search || param?.name?.toLowerCase().includes(search);
        return nameMatch && matchScope(param) && matchOrigin(param);
      });

      if (!parameters.length) return null;
      return {
        ...element,
        parameters,
      };
    })
    .filter(Boolean) as ElementItem[];
});

const parameterOptions = computed(() => {
  const map = new Map<string, ParameterOption>();

  for (const element of filteredElements.value) {
    for (const param of element.parameters || []) {
      const scopeLabel: ParameterOption["scopeLabel"] = param.isTypeParameter ? "Type" : "Instance";
      const originLabel: ParameterOption["originLabel"] =
        param.orgin === 0
          ? "Shared"
          : param.orgin === 1
            ? "Project"
            : param.orgin === 2
              ? "BuildIn"
              : "Unknown";

      const storageTypeLabel = param.storageType || "Unknown";
      const valueTypeLabel: ParameterOption["valueTypeLabel"] =
        storageTypeToValueType(storageTypeLabel);

      const key = param.name;
      if (!map.has(key)) {
        map.set(key, {
          key,
          name: param.name,
          scopeLabel,
          originLabel,
          valueTypeLabel,
          storageTypeLabel,
          isReadOnly: Boolean(param.isReadOnly),
        });
      }
    }
  }

  return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
});

const parameterNameOptions = computed(() => parameterOptions.value.map((x) => x.name));
const numericParameterNameOptions = computed(() =>
  parameterOptions.value.filter((x) => x.valueTypeLabel === "Number").map((x) => x.name),
);

function getParameterValue(element: ElementItem | null, parameterName: string | null): string {
  if (!element || !parameterName) return "";
  const found = (element.parameters || []).find((p) => p.name === parameterName);
  return found?.value == null ? "" : String(found.value);
}

function getParameterData(
  element: ElementItem | null,
  parameterName: string | null,
): ParameterData | null {
  if (!element || !parameterName) return null;
  return (element.parameters || []).find((p) => p.name === parameterName) || null;
}

const targetOption = computed(() =>
  parameterOptions.value.find((option) => option.name === targetParameterName.value),
);

const targetKind = computed<TargetKind>(() => {
  if (!targetOption.value) return "Unknown";
  if (targetOption.value.valueTypeLabel === "Number") return "Number";
  if (targetOption.value.valueTypeLabel === "String") return "Text";
  return "Unknown";
});

const targetNumberKind = computed<TargetNumberKind>(() => {
  if (targetKind.value !== "Number" || !targetParameterName.value) return "unknown";
  const values = filteredElements.value
    .map((el) => getParameterValue(el, targetParameterName.value))
    .map((x) => x.trim())
    .filter((x) => x !== "");

  if (!values.length) return "unknown";
  const parsed = values.map((v) => Number(v.replace(",", "."))).filter((v) => Number.isFinite(v));
  if (!parsed.length) return "unknown";

  const isIntLike = parsed.every((v) => Number.isInteger(v));
  return isIntLike ? "int" : "double";
});

const targetTypeInfo = computed(() => {
  if (!targetParameterName.value) return "Select target parameter to detect type.";
  if (targetKind.value === "Text") {
    return "Target type: Text. You can combine parameter and text blocks.";
  }
  if (targetKind.value === "Number") {
    if (targetNumberKind.value === "int") {
      return "Target type: Number (int-like). Numeric blocks are summed and rounded to integer.";
    }
    if (targetNumberKind.value === "double") {
      return "Target type: Number (double-like). Numeric blocks are summed with decimals.";
    }
    return "Target type: Number. Numeric blocks are summed.";
  }
  return "Target type is unclear (mixed/empty samples).";
});

const validBlocks = computed(() =>
  blocks.value.filter((block) => {
    if (block.kind === "parameter") return !!block.parameterName;
    if (block.kind === "text") return block.literal.length > 0;
    if (block.kind === "number") return block.literal.trim().length > 0;
    return false;
  }),
);

function findOptionByName(name: string | null): ParameterOption | null {
  if (!name) return null;
  return parameterOptions.value.find((x) => x.name === name) || null;
}

function isOptionNumeric(name: string | null): boolean {
  const option = findOptionByName(name);
  if (!option) return false;
  return isStorageTypeNumeric(option.storageTypeLabel);
}

const shouldSumForTextTarget = computed(() => {
  if (targetKind.value !== "Text") return false;
  if (validBlocks.value.length === 0) return false;
  return validBlocks.value.every((block) => {
    if (block.kind === "text") return false;
    if (block.kind === "number") return true;
    return isOptionNumeric(block.parameterName);
  });
});

function parseNumeric(value: string): number | null {
  const normalized = value.trim().replace(",", ".");
  if (!normalized) return null;
  const n = Number(normalized);
  if (!Number.isFinite(n)) return null;
  return n;
}

function buildNewValue(element: ElementItem): { value: string; errors: string[] } {
  const errors: string[] = [];

  if (targetKind.value === "Number") {
    let sum = 0;
    for (const block of validBlocks.value) {
      if (block.kind === "text") {
        errors.push("Text block is not allowed for numeric target parameter.");
        continue;
      }

      if (block.kind === "number") {
        const n = parseNumeric(block.literal);
        if (n == null) {
          errors.push(`Numeric block is invalid: ${block.literal}`);
          continue;
        }
        sum += n;
        continue;
      }

      const value = getParameterValue(element, block.parameterName);
      if (value.trim() === "") continue;

      const n = parseNumeric(value);
      if (n == null) {
        errors.push(
          `Parameter '${block.parameterName}' has text value and cannot be used for numeric target.`,
        );
        continue;
      }
      sum += n;
    }

    if (errors.length) return { value: "", errors };
    if (targetNumberKind.value === "int") {
      return { value: String(Math.round(sum)), errors };
    }
    return { value: String(sum), errors };
  }

  if (shouldSumForTextTarget.value) {
    let sum = 0;
    for (const block of validBlocks.value) {
      if (block.kind === "number") {
        const n = parseNumeric(block.literal);
        if (n == null) {
          errors.push(`Numeric block is invalid: ${block.literal}`);
          continue;
        }
        sum += n;
        continue;
      }

      const value = getParameterValue(element, block.parameterName);
      if (value.trim() === "") continue;
      const n = parseNumeric(value);
      if (n == null) {
        errors.push(
          `Parameter '${block.parameterName}' has non-numeric value in numeric sum mode.`,
        );
        continue;
      }
      sum += n;
    }

    if (errors.length) return { value: "", errors };
    return { value: String(sum), errors };
  }

  let textValue = "";
  for (const block of validBlocks.value) {
    if (block.kind === "text") {
      textValue += block.literal;
      continue;
    }
    if (block.kind === "number") {
      textValue += block.literal;
      continue;
    }
    textValue += getParameterValue(element, block.parameterName);
  }

  return { value: textValue, errors };
}

const previewRows = computed<PreviewRow[]>(() => {
  if (!targetParameterName.value) return [];
  return filteredElements.value.map((element) => {
    const oldValue = getParameterValue(element, targetParameterName.value);
    const built = buildNewValue(element);

    return {
      elementId: element.id,
      elementName: element.name,
      elementScope: element.isElementType ? "Type" : "Instance",
      oldValue,
      newValue: built.value,
      status: built.errors.length ? "Error" : oldValue === built.value ? "Same" : "Changed",
      errors: built.errors,
    };
  });
});

const editableRows = computed(() =>
  previewRows.value.filter((row) => row.status === "Changed" && row.errors.length === 0),
);

const selectedEditableRows = computed(() =>
  editableRows.value.filter((row) => editableRowMap.value[row.elementId] !== false),
);

const configErrors = computed(() => {
  const errs: string[] = [];
  if (!selectedCategory.value) errs.push("Select category first.");
  if (!targetParameterName.value) errs.push("Select target parameter.");
  if (validBlocks.value.length === 0) errs.push("Add at least one non-empty block.");
  if (targetKind.value === "Number" && validBlocks.value.some((x) => x.kind === "text")) {
    errs.push("Text blocks are not allowed for numeric target parameter.");
  }
  return errs;
});

const rowErrors = computed(() =>
  previewRows.value.reduce((acc: string[], row) => acc.concat(row.errors), []),
);
const hasErrors = computed(() => configErrors.value.length > 0 || rowErrors.value.length > 0);
const changedRows = computed(() => previewRows.value.filter((x) => x.status === "Changed"));

watch(
  previewRows,
  (rows) => {
    const nextMap: Record<number, boolean> = {};
    for (const row of rows) {
      const isEditable = row.status === "Changed" && row.errors.length === 0;
      nextMap[row.elementId] = isEditable ? (editableRowMap.value[row.elementId] ?? true) : false;
    }
    editableRowMap.value = nextMap;
  },
  { immediate: true },
);

watch(
  () => items.value,
  () => {
    if (loading.value) loading.value = false;
  },
);

onMounted(() => {
  categoriesStore.loadCategories().catch((err) => {
    console.error("Failed to load categories", err);
  });
});

function resetBuilderState() {
  targetParameterName.value = null;
  blocks.value = [{ id: 1, kind: "parameter", parameterName: null, literal: "" }];
  blockIdCounter.value = 1;
  sendInfo.value = "";
}

watch(selectedCategory, (next, prev) => {
  if (next === prev) return;
  elementsStore.clear();
  resetBuilderState();
});

async function updateData() {
  sendInfo.value = "";
  loading.value = true;
  try {
    if (!selectedCategory.value) {
      elementsStore.clear();
      return;
    }
    await elementsStore.loadByCategory(selectedCategory.value);
  } catch (err) {
    console.error("Failed to load elements", err);
  } finally {
    loading.value = false;
  }
}

function addParameterBlock() {
  blockIdCounter.value += 1;
  blocks.value.push({
    id: blockIdCounter.value,
    kind: "parameter",
    parameterName: null,
    literal: "",
  });
}

function addTextBlock() {
  blockIdCounter.value += 1;
  blocks.value.push({ id: blockIdCounter.value, kind: "text", parameterName: null, literal: "" });
}

function addNumberBlock() {
  blockIdCounter.value += 1;
  blocks.value.push({ id: blockIdCounter.value, kind: "number", parameterName: null, literal: "" });
}

function removeBlock(id: number) {
  if (blocks.value.length === 1) return;
  blocks.value = blocks.value.filter((x) => x.id !== id);
}

function getOptionLabel(name: string): string {
  const option = parameterOptions.value.find((x) => x.name === name);
  if (!option) return name;
  return `${option.name} • ${option.storageTypeLabel} • ${option.scopeLabel} • ${option.originLabel}`;
}

function getBlockTitle(block: RuleBlock): string {
  if (block.kind === "parameter") {
    if (!block.parameterName) return "Parameter";
    return getOptionLabel(block.parameterName);
  }
  if (block.kind === "number") return `Number: ${block.literal || "(empty)"}`;
  return `Text: ${block.literal || "(empty)"}`;
}

function onDragStart(index: number) {
  draggedBlockIndex.value = index;
}

function onDrop(targetIndex: number) {
  if (draggedBlockIndex.value == null) return;
  const from = draggedBlockIndex.value;
  if (from === targetIndex) {
    draggedBlockIndex.value = null;
    return;
  }

  const next = [...blocks.value];
  const [moved] = next.splice(from, 1);
  next.splice(targetIndex, 0, moved);
  blocks.value = next;
  draggedBlockIndex.value = null;
}

function isRowEditable(row: PreviewRow): boolean {
  return row.status === "Changed" && row.errors.length === 0;
}

function setEditableSelection(mode: "all" | "none" | "type" | "instance" | "changed") {
  const nextMap = { ...editableRowMap.value };

  for (const row of previewRows.value) {
    if (!isRowEditable(row)) {
      nextMap[row.elementId] = false;
      continue;
    }

    if (mode === "all" || mode === "changed") {
      nextMap[row.elementId] = true;
      continue;
    }

    if (mode === "none") {
      nextMap[row.elementId] = false;
      continue;
    }

    if (mode === "type") {
      nextMap[row.elementId] = row.elementScope === "Type";
      continue;
    }

    if (mode === "instance") {
      nextMap[row.elementId] = row.elementScope === "Instance";
    }
  }

  editableRowMap.value = nextMap;
}

function sendApplyCombinedParameters() {
  if (hasErrors.value || !targetParameterName.value || !selectedCategory.value) return;
  applying.value = true;
  sendInfo.value = "";

  const parameterItems = selectedEditableRows.value
    .map((row) => {
      const element = filteredElements.value.find((item) => item.id === row.elementId) || null;
      const targetParameter = getParameterData(element, targetParameterName.value);

      if (!targetParameter) return null;

      return {
        ...targetParameter,
        value: row.newValue,
      };
    })
    .filter(Boolean) as ParameterData[];

  const payload = {
    categoryName: selectedCategory.value,
    targetParameterName: targetParameterName.value,
    mode: applyMode.value,
    rules: validBlocks.value.map((block, index) => ({
      kind: block.kind,
      value: block.kind === "parameter" ? String(block.parameterName) : block.literal,
      order: index,
    })),
    items: parameterItems,
  } as ApplyCombinedParametersPayload;

  sendRequest(Commands.ApplyCombinedParameters, payload).catch((err) => {
    console.error("Failed to send ApplyCombinedParameters", err);
  });

  applying.value = false;
  sendInfo.value = `ApplyCombinedParameters sent. Items: ${payload.items.length}`;
}
</script>

<template>
  <div class="p-5 flex flex-col gap-4">
    <header
      class="card flex flex-row items-start lg:items-center w-full gap-3 flex-wrap lg:flex-nowrap"
    >
      <Select
        class="min-w-[180px] flex-1 w-full"
        :options="sortedCategories"
        placeholder="Select category"
        :modelValue="selectedCategory"
        @update:modelValue="(val) => (selectedCategory = val)"
      />

      <IconField class="flex-1 min-w-[220px] w-full">
        <InputIcon class="pi pi-search" />
        <InputText
          placeholder="Search parameters"
          class="min-w-[40px] shrink w-full"
          :modelValue="searchQuery"
          @update:modelValue="(val) => (searchQuery = val || '')"
        />
      </IconField>

      <SelectButton
        class="flex-1 min-w-[220px]"
        :modelValue="selectedFilters"
        @update:modelValue="(val) => (selectedFilters = val)"
        :options="filterOptions"
        multiple
        aria-labelledby="parameter-filters"
      />

      <Button
        class="flex-none w-full lg:w-auto"
        icon="pi pi-sync"
        label="Update Data"
        :loading="loading"
        :disabled="loading"
        @click="updateData"
      />
    </header>
    <section class="bg-blue-50/50 border border-blue-200 rounded-lg p-4 flex flex-col gap-4">
      <section class="card flex flex-col gap-3">
        <div class="flex items-end gap-3 flex-wrap">
          <div class="flex flex-col gap-1 min-w-[280px] flex-1">
            <label class="text-sm font-medium">Target Parameter</label>
            <Select
              :options="parameterOptions"
              optionLabel="name"
              optionValue="name"
              :modelValue="targetParameterName"
              placeholder="Select target parameter"
              @update:modelValue="(val) => (targetParameterName = val)"
            />
          </div>

          <div class="flex flex-col gap-1 min-w-[220px]">
            <label class="text-sm font-medium">Apply Mode</label>
            <Select
              :options="applyModes"
              optionLabel="label"
              optionValue="value"
              :modelValue="applyMode"
              @update:modelValue="(val) => (applyMode = val)"
            />
          </div>

          <div class="flex gap-2 flex-wrap">
            <Button icon="pi pi-plus" label="Add Parameter" @click="addParameterBlock" />
            <Button
              icon="pi pi-plus"
              label="Add Text"
              :disabled="targetKind === 'Number'"
              @click="addTextBlock"
            />
            <Button icon="pi pi-plus" label="Add Number" @click="addNumberBlock" />
          </div>
        </div>

        <div class="text-xs text-surface-600">{{ targetTypeInfo }}</div>
        <div v-if="targetOption" class="flex flex-wrap gap-1">
          <Tag :value="targetOption.valueTypeLabel" severity="info" />
          <Tag :value="targetOption.storageTypeLabel" severity="contrast" />
          <Tag :value="targetOption.scopeLabel" severity="warn" />
          <Tag :value="targetOption.originLabel" severity="secondary" />
          <Tag
            :value="targetOption.isReadOnly ? 'ReadOnly' : 'Writable'"
            :severity="targetOption.isReadOnly ? 'danger' : 'success'"
          />
        </div>
        <div v-if="shouldSumForTextTarget" class="text-xs text-emerald-700">
          Text target with numeric-only sources: values will be summed.
        </div>

        <div class="text-xs text-surface-500">
          Drag blocks to reorder. Empty parameter values are allowed and are not treated as errors.
        </div>

        <div class="flex flex-wrap gap-2">
          <div
            v-for="(block, idx) in blocks"
            :key="block.id"
            draggable="true"
            @dragstart="onDragStart(idx)"
            @dragover.prevent
            @drop="onDrop(idx)"
            class="border border-surface-300 rounded-lg p-2 min-w-[220px] bg-surface-0 shadow-sm"
          >
            <div class="flex items-center justify-between gap-2 mb-2">
              <div class="text-xs font-medium text-surface-600">{{ block.kind.toUpperCase() }}</div>
              <Button
                icon="pi pi-times"
                text
                severity="danger"
                :disabled="blocks.length === 1"
                @click="removeBlock(block.id)"
              />
            </div>

            <Select
              v-if="block.kind === 'parameter'"
              :options="
                targetKind === 'Number' ? numericParameterNameOptions : parameterNameOptions
              "
              :modelValue="block.parameterName"
              placeholder="Select parameter"
              @update:modelValue="(val) => (block.parameterName = val)"
            />

            <InputText
              v-else
              :modelValue="block.literal"
              :placeholder="block.kind === 'number' ? 'Enter number' : 'Enter text (e.g. _)'"
              @update:modelValue="(val) => (block.literal = val || '')"
            />

            <div
              v-if="block.kind === 'parameter' && block.parameterName"
              class="mt-2 flex flex-wrap gap-1"
            >
              <Tag
                :value="findOptionByName(block.parameterName)?.valueTypeLabel || 'Unknown'"
                severity="info"
              />
              <Tag
                :value="findOptionByName(block.parameterName)?.storageTypeLabel || 'Unknown'"
                severity="contrast"
              />
              <Tag
                :value="findOptionByName(block.parameterName)?.scopeLabel || 'Unknown'"
                severity="warn"
              />
              <Tag
                :value="findOptionByName(block.parameterName)?.originLabel || 'Unknown'"
                severity="secondary"
              />
            </div>

            <div
              v-else
              class="text-[11px] text-surface-500 mt-2 truncate"
              :title="getBlockTitle(block)"
            >
              {{ getBlockTitle(block) }}
            </div>
          </div>
        </div>
      </section>
    </section>
    <section class="card flex flex-col gap-3">
      <h3 class="text-base font-semibold">All Elements (Old vs New)</h3>

      <div class="text-sm text-red-600" v-for="(err, index) in configErrors" :key="`cfg-${index}`">
        {{ err }}
      </div>

      <div class="flex flex-wrap items-center gap-2">
        <span class="text-sm text-surface-600">Quick select:</span>
        <Button size="small" label="All Editable" outlined @click="setEditableSelection('all')" />
        <Button size="small" label="Changed" outlined @click="setEditableSelection('changed')" />
        <Button size="small" label="Instance" outlined @click="setEditableSelection('instance')" />
        <Button size="small" label="Type" outlined @click="setEditableSelection('type')" />
        <Button size="small" label="None" outlined @click="setEditableSelection('none')" />
      </div>

      <div class="overflow-auto border border-surface-200 rounded-lg">
        <table class="w-full text-sm">
          <thead class="bg-surface-100">
            <tr>
              <th class="text-left p-2">Edit</th>
              <th class="text-left p-2">Element Id</th>
              <th class="text-left p-2">Element Name</th>
              <th class="text-left p-2">Element Type</th>
              <th class="text-left p-2">Old Value</th>
              <th class="text-left p-2">New Value</th>
              <th class="text-left p-2">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in previewRows" :key="row.elementId" class="border-t border-surface-200">
              <td class="p-2">
                <Checkbox
                  binary
                  :modelValue="editableRowMap[row.elementId] === true"
                  :disabled="!isRowEditable(row)"
                  @update:modelValue="(val) => (editableRowMap[row.elementId] = !!val)"
                />
              </td>
              <td class="p-2">{{ row.elementId }}</td>
              <td class="p-2">{{ row.elementName }}</td>
              <td class="p-2">
                <Tag
                  :value="row.elementScope"
                  :severity="row.elementScope === 'Type' ? 'warn' : 'info'"
                />
              </td>
              <td class="p-2">{{ row.oldValue || "(empty)" }}</td>
              <td class="p-2" :class="row.status === 'Changed' ? 'bg-emerald-50' : ''">
                {{ row.newValue || "(empty)" }}
              </td>
              <td class="p-2">
                <span
                  :class="
                    row.status === 'Error'
                      ? 'text-red-600'
                      : row.status === 'Changed'
                        ? 'text-emerald-700'
                        : 'text-surface-600'
                  "
                >
                  {{ row.status }}
                </span>
                <div v-if="row.errors.length" class="text-xs text-red-600 mt-1">
                  {{ row.errors.join("; ") }}
                </div>
              </td>
            </tr>
            <tr v-if="previewRows.length === 0">
              <td colspan="7" class="p-3 text-surface-500">
                No rows to preview. Load category data and complete blocks.
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="flex flex-col gap-2 lg:flex-row lg:items-center lg:justify-between">
        <div class="text-sm text-surface-600">
          Editable rows selected: {{ selectedEditableRows.length }} / {{ editableRows.length }}
        </div>
        <Button
          icon="pi pi-send"
          label="Apply to Revit"
          :loading="applying"
          :disabled="hasErrors || applying || selectedEditableRows.length === 0"
          @click="sendApplyCombinedParameters"
        />
      </div>

      <div v-if="sendInfo" class="text-sm text-emerald-700">
        {{ sendInfo }}
      </div>
    </section>
  </div>
</template>
