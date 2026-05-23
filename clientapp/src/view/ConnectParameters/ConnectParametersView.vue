<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, onMounted, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { Commands, invoke } from "@/RevitBridge";
import { ParameterOrgin, SetDataToParametersModes } from "@/stores/types";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import { useDocumentDataStore } from "@/stores/useDocumentDataStore";
import { useElementsStore } from "@/stores/useElementsStore";
import {
  getScopeTagSeverity,
  getStorageTypeTagSeverity,
  getValueTypeTagSeverity,
} from "@/view/ConnectParameters/tagSeverity";
import { parseSavedRules } from "@/view/ConnectParameters/savedRulesUtils";
import type { RuleBlock, SavedRule } from "@/view/ConnectParameters/ruleTypes";
import type { ElementItem, ParameterData, SetDataToParameters } from "@/stores/types";

const TopFiltersBar = defineAsyncComponent(() => import("@/components/TopFiltersBar.vue") as any);
const RuleBuilderBlock = defineAsyncComponent(() => import("./RuleBuilderBlock.vue") as any);
const SavedRulesDrawer = defineAsyncComponent(() => import("./SavedRulesDrawer.vue") as any);

type ApplyMode = (typeof SetDataToParametersModes)[keyof typeof SetDataToParametersModes];
type TargetKind = "Text" | "Number" | "Unknown";
type TargetNumberKind = "int" | "double" | "unknown";
const Status = {
  Changed: "Changed",
  Same: "Same",
  Error: "Error",
} as const;
type Status = (typeof Status)[keyof typeof Status];

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
  status: Status;
  errors: string[];
};

const categoriesStore = useCategoriesStore();
const documentDataStore = useDocumentDataStore();
const elementsStore = useElementsStore();

const { sortedCategories } = storeToRefs(categoriesStore);
const { documentName } = storeToRefs(documentDataStore);
const { items } = storeToRefs(elementsStore);

const selectedCategory = ref<string | null>(null);
const searchQuery = ref("");
const selectedFilters = ref<string[]>([]);
const targetParameterName = ref<string | null>(null);
const applyMode = ref<ApplyMode>(SetDataToParametersModes.Overwrite);

const loading = ref(false);
const applying = ref(false);
const sendInfo = ref("");
const draggedBlockIndex = ref<number | null>(null);
const editableRowMap = ref<Record<number, boolean>>({});
const isApplyingSavedRule = ref(false);
const previewDisplayMode = ref<"all" | "changed" | "errors">("all");

const RULES_STORAGE_KEY = "connect-parameters-rules-v1";
const savedRules = ref<SavedRule<ApplyMode>[]>([]);
const selectedSavedRuleId = ref<string | null>(null);
const ruleName = ref("");
const ruleGroupTag = ref("");
const isSavedRulesDrawerOpen = ref(false);

const blockIdCounter = ref(1);
const blocks = ref<RuleBlock[]>([{ id: 1, kind: "parameter", parameterName: null, literal: "" }]);

const filterOptions = ["Instance", "Type", "BuildIn", "Schared", "Project"];
const applyModes: { label: string; value: ApplyMode }[] = [
  { label: "Overwrite", value: SetDataToParametersModes.Overwrite },
  { label: "Only if target empty", value: SetDataToParametersModes.OnlyIfEmpty },
  { label: "Skip if equal", value: SetDataToParametersModes.SkipIfEqual },
];

const savedRulesSorted = computed(() =>
  [...savedRules.value].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)),
);

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
    if (param.orgin === ParameterOrgin.Shared) return originFilters.includes("Schared");
    if (param.orgin === ParameterOrgin.Project) return originFilters.includes("Project");
    if (param.orgin === ParameterOrgin.BuiltIn) return originFilters.includes("BuildIn");
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
        param.orgin === ParameterOrgin.Shared
          ? "Shared"
          : param.orgin === ParameterOrgin.Project
            ? "Project"
            : param.orgin === ParameterOrgin.BuiltIn
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

const elementsById = computed(() => {
  const map = new Map<number, ElementItem>();
  for (const element of items.value || []) {
    map.set(element.id, element);
  }
  return map;
});

function getLinkedTypeElement(element: ElementItem | null): ElementItem | null {
  if (!element || element.isElementType) return null;
  if (!element.elementTypeId) return null;
  return elementsById.value.get(element.elementTypeId) || null;
}

function getOwnParameterValue(element: ElementItem | null, parameterName: string | null): string {
  if (!element || !parameterName) return "";
  const found = (element.parameters || []).find((p) => p.name === parameterName);
  return found?.value == null ? "" : String(found.value);
}

function getRuleParameterValue(element: ElementItem | null, parameterName: string | null): string {
  const ownValue = getOwnParameterValue(element, parameterName);
  if (ownValue !== "") return ownValue;

  const linkedTypeElement = getLinkedTypeElement(element);
  return getOwnParameterValue(linkedTypeElement, parameterName);
}

function getParameterData(
  element: ElementItem | null,
  parameterName: string | null,
): ParameterData | null {
  if (!element || !parameterName) return null;
  return (element.parameters || []).find((p) => p.name === parameterName) || null;
}

function cloneBlocks(source: RuleBlock[]): RuleBlock[] {
  return source.map((b) => ({
    id: b.id,
    kind: b.kind,
    parameterName: b.parameterName,
    literal: b.literal,
  }));
}

function persistSavedRules() {
  try {
    localStorage.setItem(RULES_STORAGE_KEY, JSON.stringify(savedRules.value));
  } catch (err) {
    console.error("Failed to persist saved rules", err);
  }
}

function loadSavedRulesFromStorage() {
  try {
    const raw = localStorage.getItem(RULES_STORAGE_KEY);
    if (!raw) {
      savedRules.value = [];
      return;
    }

    const parsed = JSON.parse(raw);
    const modes = Object.values(SetDataToParametersModes) as ApplyMode[];
    savedRules.value = parseSavedRules<ApplyMode>(parsed, modes, "Unnamed rule");
  } catch (err) {
    console.error("Failed to load saved rules", err);
    savedRules.value = [];
  }
}

function buildCurrentRuleDraft(id: string, createdAt: string): SavedRule<ApplyMode> {
  const name = ruleName.value.trim() || `Rule ${savedRules.value.length + 1}`;
  const projectTag = documentName.value.trim() || null;
  return {
    id,
    name,
    projectTag,
    groupTag: ruleGroupTag.value.trim() || null,
    categoryName: selectedCategory.value,
    targetParameterName: targetParameterName.value,
    mode: applyMode.value,
    blocks: cloneBlocks(blocks.value),
    createdAt,
    updatedAt: new Date().toISOString(),
  };
}

function saveAsNewRule() {
  const id = crypto.randomUUID();
  const now = new Date().toISOString();
  const rule = buildCurrentRuleDraft(id, now);
  savedRules.value.unshift(rule);
  selectedSavedRuleId.value = rule.id;
  ruleName.value = rule.name;
  ruleGroupTag.value = rule.groupTag || "";
  persistSavedRules();
  sendInfo.value = `Rule saved: ${rule.name}`;
}

function updateSelectedRule() {
  if (!selectedSavedRuleId.value) return;

  const idx = savedRules.value.findIndex((x) => x.id === selectedSavedRuleId.value);
  if (idx < 0) return;

  const existing = savedRules.value[idx];
  const updated = buildCurrentRuleDraft(existing.id, existing.createdAt);
  savedRules.value.splice(idx, 1, updated);
  ruleName.value = updated.name;
  ruleGroupTag.value = updated.groupTag || "";
  persistSavedRules();
  sendInfo.value = `Rule updated: ${updated.name}`;
}

function loadRule(rule: SavedRule<ApplyMode>) {
  selectedSavedRuleId.value = rule.id;
  ruleName.value = rule.name;
  ruleGroupTag.value = rule.groupTag || "";

  isApplyingSavedRule.value = true;
  selectedCategory.value = rule.categoryName;
  targetParameterName.value = rule.targetParameterName;
  applyMode.value = rule.mode;

  const loadedBlocks = cloneBlocks(rule.blocks);
  blocks.value = loadedBlocks.length
    ? loadedBlocks
    : [{ id: 1, kind: "parameter", parameterName: null, literal: "" }];

  blockIdCounter.value = blocks.value.reduce((max, b) => Math.max(max, b.id), 1);

  nextTick(() => {
    isApplyingSavedRule.value = false;
  });

  sendInfo.value = `Rule loaded: ${rule.name}`;
}

function deleteRule(ruleId: string) {
  const target = savedRules.value.find((x) => x.id === ruleId);
  if (!target) return;

  const ok = window.confirm(`Delete rule '${target.name}'?`);
  if (!ok) return;

  savedRules.value = savedRules.value.filter((x) => x.id !== ruleId);
  if (selectedSavedRuleId.value === ruleId) {
    selectedSavedRuleId.value = null;
    ruleName.value = "";
    ruleGroupTag.value = "";
  }

  persistSavedRules();
}

function renameRule(ruleId: string) {
  const target = savedRules.value.find((x) => x.id === ruleId);
  if (!target) return;

  const next = window.prompt("New rule name", target.name);
  if (!next) return;

  target.name = next.trim() || target.name;
  target.updatedAt = new Date().toISOString();
  if (selectedSavedRuleId.value === target.id) {
    ruleName.value = target.name;
  }
  persistSavedRules();
}

function exportRulesJson() {
  const json = JSON.stringify(savedRules.value, null, 2);
  const blob = new Blob([json], { type: "application/json" });
  const url = URL.createObjectURL(blob);

  const link = document.createElement("a");
  link.href = url;
  link.download = "connect-parameter-rules.json";
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);

  URL.revokeObjectURL(url);
}

function importRulesJson() {
  const text = window.prompt("Paste JSON array of rules");
  if (!text) return;

  try {
    const parsed = JSON.parse(text);
    const modes = Object.values(SetDataToParametersModes) as ApplyMode[];
    const imported = parseSavedRules<ApplyMode>(parsed, modes, "Imported rule");
    if (imported.length === 0) return;

    const byId = new Map<string, SavedRule<ApplyMode>>();
    for (const item of savedRules.value) byId.set(item.id, item);
    for (const item of imported) byId.set(item.id, item);

    savedRules.value = Array.from(byId.values());
    persistSavedRules();
    sendInfo.value = `Rules imported: ${imported.length}`;
  } catch (err) {
    console.error("Failed to import rules JSON", err);
  }
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
    .map((el) => getOwnParameterValue(el, targetParameterName.value))
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

      const value = getRuleParameterValue(element, block.parameterName);
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

      const value = getRuleParameterValue(element, block.parameterName);
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
    textValue += getRuleParameterValue(element, block.parameterName);
  }

  return { value: textValue, errors };
}

const previewRows = computed<PreviewRow[]>(() => {
  if (!targetParameterName.value) return [];
  return filteredElements.value.map((element) => {
    const oldValue = getOwnParameterValue(element, targetParameterName.value);
    const built = buildNewValue(element);

    return {
      elementId: element.id,
      elementName: element.name,
      elementScope: element.isElementType ? "Type" : "Instance",
      oldValue,
      newValue: built.value,
      status: built.errors.length
        ? Status.Error
        : oldValue === built.value
          ? Status.Same
          : Status.Changed,
      errors: built.errors,
    };
  });
});

const editableRows = computed(() =>
  previewRows.value.filter((row) => row.status === Status.Changed && row.errors.length === 0),
);

const selectedEditableRows = computed(() =>
  editableRows.value.filter((row) => isRowSelectedForApply(row)),
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
const changedRows = computed(() =>
  previewRows.value.filter((row) => getDisplayStatus(row) === Status.Changed),
);
const errorRows = computed(() =>
  previewRows.value.filter((x) => x.status === Status.Error || x.errors.length > 0),
);
const visiblePreviewRows = computed(() => {
  if (previewDisplayMode.value === "changed") return changedRows.value;
  if (previewDisplayMode.value === "errors") return errorRows.value;
  return previewRows.value;
});

watch(
  previewRows,
  (rows) => {
    const nextMap: Record<number, boolean> = {};
    for (const row of rows) {
      const isEditable = row.status === Status.Changed && row.errors.length === 0;
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
  loadSavedRulesFromStorage();
});

function resetBuilderState() {
  targetParameterName.value = null;
  blocks.value = [{ id: 1, kind: "parameter", parameterName: null, literal: "" }];
  blockIdCounter.value = 1;
  sendInfo.value = "";
}

watch(selectedCategory, (next, prev) => {
  if (next === prev) return;
  if (isApplyingSavedRule.value) return;
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
    await elementsStore.loadByCategory(selectedCategory.value, true);
  } catch (err) {
    console.error("Failed to load elements", err);
  } finally {
    loading.value = false;
  }
}

function onUpdateCategory(value: string | null) {
  selectedCategory.value = value;
}

function onUpdateSearch(value: string) {
  searchQuery.value = value;
}

function onUpdateFilters(value: string[]) {
  selectedFilters.value = value;
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

function updateBlockParameterName(blockId: number, value: string | null) {
  const block = blocks.value.find((item) => item.id === blockId);
  if (!block) return;
  block.parameterName = value;
}

function updateBlockLiteral(blockId: number, value: string) {
  const block = blocks.value.find((item) => item.id === blockId);
  if (!block) return;
  block.literal = value;
}

function onBlockParameterNameUpdate(blockId: number, value: string | null) {
  updateBlockParameterName(blockId, value);
}

function onBlockLiteralUpdate(blockId: number, value: string) {
  updateBlockLiteral(blockId, value);
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
  return row.errors.length === 0;
}

function isRowSelectedForApply(row: PreviewRow): boolean {
  return (
    isRowEditable(row) &&
    row.status === Status.Changed &&
    editableRowMap.value[row.elementId] !== false
  );
}

function getDisplayStatus(row: PreviewRow): Status {
  if (row.status === Status.Changed && !isRowSelectedForApply(row)) {
    return Status.Same;
  }
  return row.status;
}

function getDisplayNewValue(row: PreviewRow): string {
  if (row.status === Status.Changed && !isRowSelectedForApply(row)) {
    return row.oldValue;
  }
  return row.newValue;
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
      const element = elementsById.value.get(row.elementId) || null;
      const targetParameter = getParameterData(element, targetParameterName.value);

      if (!targetParameter) return null;

      return {
        ...targetParameter,
        value: row.newValue,
      };
    })
    .filter(Boolean) as ParameterData[];

  const payload: SetDataToParameters = {
    items: parameterItems,
    mode: applyMode.value,
  };

  invoke(Commands.SetDataToParameters, payload).catch((err) => {
    console.error("Failed to send ApplyCombinedParameters", err);
  });

  applying.value = false;
  sendInfo.value = `ApplyCombinedParameters sent. Items: ${parameterItems.length}`;
}
</script>

<template>
  <div class="p-5 flex flex-col gap-4">
    <TopFiltersBar
      :categories="sortedCategories"
      :category="selectedCategory"
      :search="searchQuery"
      :filters="selectedFilters"
      :filterOptions="filterOptions"
      :loading="loading"
      updateButtonLabel="Update Data"
      categoryPlaceholder="Select category"
      searchPlaceholder="Search parameters"
      @update:category="onUpdateCategory"
      @update:search="onUpdateSearch"
      @update:filters="onUpdateFilters"
      @update-data="updateData"
    />
    <section class="bg-emphasis border border-surface rounded-lg p-4 flex flex-col gap-4">
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

            <Button
              icon="pi pi-folder-open"
              label="Rule Library"
              type="button"
              size="small"
              :badge="String(savedRulesSorted.length)"
              badgeSeverity="contrast"
              @click="isSavedRulesDrawerOpen = true"
            />
          </div>
        </div>

        <div class="text-xs text-color">{{ targetTypeInfo }}</div>
        <div v-if="targetOption" class="flex flex-wrap gap-1">
          <Tag
            :value="targetOption.valueTypeLabel"
            :severity="getValueTypeTagSeverity(targetOption.valueTypeLabel)"
          />
          <Tag
            :value="targetOption.scopeLabel"
            :severity="getScopeTagSeverity(targetOption.scopeLabel)"
          />
          <Tag :value="targetOption.originLabel" severity="secondary" />
          <Tag
            :value="targetOption.isReadOnly ? 'ReadOnly' : 'Writable'"
            :severity="targetOption.isReadOnly ? 'danger' : 'success'"
          />
        </div>
        <div v-if="shouldSumForTextTarget" class="text-xs status-success">
          Text target with numeric-only sources: values will be summed.
        </div>

        <div class="text-xs text-surface-500">
          Drag blocks to reorder. Empty parameter values are allowed and are not treated as errors.
        </div>

        <div class="flex flex-wrap items-stretch gap-2">
          <template v-for="(block, idx) in blocks" :key="block.id">
            <RuleBuilderBlock
              :block="block"
              :index="idx"
              :blocksCount="blocks.length"
              :targetKind="targetKind"
              :numericParameterNameOptions="numericParameterNameOptions"
              :parameterNameOptions="parameterNameOptions"
              :parameterOption="findOptionByName(block.parameterName)"
              :blockTitle="getBlockTitle(block)"
              @dragstart="onDragStart"
              @drop="onDrop"
              @remove="removeBlock"
              @updateParameterName="onBlockParameterNameUpdate(block.id, $event)"
              @updateLiteral="onBlockLiteralUpdate(block.id, $event)"
            />

            <div
              v-if="idx < blocks.length - 1"
              class="rule-block-separator self-center px-1 text-base font-semibold"
              aria-hidden="true"
            >
              +
            </div>
          </template>
        </div>
      </section>
    </section>

    <SavedRulesDrawer
      :visible="isSavedRulesDrawerOpen"
      :rules="savedRulesSorted"
      :selectedRuleId="selectedSavedRuleId"
      :ruleName="ruleName"
      :groupTag="ruleGroupTag"
      @update:visible="isSavedRulesDrawerOpen = $event"
      @update:ruleName="ruleName = $event"
      @update:groupTag="ruleGroupTag = $event"
      @load="loadRule"
      @rename="renameRule"
      @delete="deleteRule"
      @saveNew="saveAsNewRule"
      @updateSelected="updateSelectedRule"
      @exportJson="exportRulesJson"
      @importJson="importRulesJson"
    />

    <section class="card flex flex-col gap-3">
      <h3 class="text-base font-semibold">All Elements (Old vs New)</h3>

      <div class="text-sm status-error" v-for="(err, index) in configErrors" :key="`cfg-${index}`">
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
          <thead class="bg-emphasis">
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
            <tr
              v-for="row in visiblePreviewRows"
              :key="row.elementId"
              class="border-t border-surface-200"
            >
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
                <Tag :value="row.elementScope" :severity="getScopeTagSeverity(row.elementScope)" />
              </td>
              <td class="p-2">{{ row.oldValue || "(empty)" }}</td>
              <td
                class="p-2"
                :class="getDisplayStatus(row) === Status.Changed ? 'status-changed' : ''"
              >
                {{ getDisplayNewValue(row) || "(empty)" }}
              </td>
              <td class="p-2">
                <span
                  :class="
                    getDisplayStatus(row) === Status.Error
                      ? 'status-error'
                      : getDisplayStatus(row) === Status.Changed
                        ? 'status-changed-strong'
                        : 'status-neutral'
                  "
                >
                  {{ getDisplayStatus(row) }}
                </span>
                <div v-if="row.errors.length" class="text-xs status-error mt-1">
                  {{ row.errors.join("; ") }}
                </div>
              </td>
            </tr>
            <tr v-if="visiblePreviewRows.length === 0">
              <td colspan="7" class="p-3 text-surface-500">
                No rows to preview. Load category data and complete blocks.
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div
        class="preview-action-bar sticky bottom-0 z-10 flex flex-col gap-2 lg:flex-row lg:items-center lg:justify-between"
      >
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

      <div v-if="sendInfo" class="text-sm status-success">
        {{ sendInfo }}
      </div>
    </section>
  </div>
</template>

<style scoped>
.rule-block-separator {
  color: var(--p-surface-500, #9ca3af);
}

.status-error {
  color: var(--p-red-500, #ef4444);
}

.status-success {
  color: var(--p-emerald-500, #10b981);
}

.status-changed {
  color: var(--p-amber-500, #f59e0b);
}

.status-changed-strong {
  color: var(--p-amber-600, #d97706);
}

.status-neutral {
  color: var(--p-surface-600, #475569);
}
</style>
