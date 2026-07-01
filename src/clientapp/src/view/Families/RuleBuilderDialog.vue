<script setup lang="ts">
import { ref, watch, computed } from "vue";
import { FIELDS, OPERATORS } from "./familyRules";
import type { FilterRule, RuleCondition, RuleScope } from "./types";

const visible = defineModel<boolean>("visible", { required: true });
const props = defineProps<{ rule: FilterRule | null }>();
const emit = defineEmits<{ save: [rule: FilterRule] }>();

// Edit a deep clone so Cancel discards changes; only commit to the store on Save.
const draft = ref<FilterRule | null>(null);
watch(
  () => props.rule,
  (r) => {
    draft.value = r ? JSON.parse(JSON.stringify(r)) : null;
  },
  { immediate: true },
);

const matchOptions = [
  { label: "All (AND)", value: "all" },
  { label: "Any (OR)", value: "any" },
];
const scopeOptions = [
  { label: "Families", value: "families" },
  { label: "Family Types", value: "types" },
];

function fieldsFor(scope: RuleScope) {
  return FIELDS[scope];
}
function operatorsFor(scope: RuleScope, fieldKey: string) {
  const def = FIELDS[scope].find((f) => f.key === fieldKey);
  return OPERATORS[def?.type ?? "text"];
}
function fieldType(scope: RuleScope, fieldKey: string) {
  return FIELDS[scope].find((f) => f.key === fieldKey)?.type ?? "text";
}

// When the field changes, reset the operator to the first valid one for the new field type.
function onFieldChange(cond: RuleCondition) {
  if (!draft.value) return;
  cond.op = operatorsFor(draft.value.scope, cond.field)[0].value;
}
// Changing scope invalidates the conditions' fields — reset to one fresh condition.
function onScopeChange() {
  if (!draft.value) return;
  const first = FIELDS[draft.value.scope][0];
  draft.value.conditions = [{ field: first.key, op: OPERATORS[first.type][0].value, value: "" }];
}

function addCondition() {
  if (!draft.value) return;
  const first = FIELDS[draft.value.scope][0];
  draft.value.conditions.push({ field: first.key, op: OPERATORS[first.type][0].value, value: "" });
}
function removeCondition(i: number) {
  draft.value?.conditions.splice(i, 1);
}

const valuelessOps = new Set(["isEmpty", "isTrue", "isFalse"]);
const canSave = computed(() => !!draft.value?.name?.trim() && !!draft.value?.conditions.length);

function save() {
  if (!draft.value || !canSave.value) return;
  draft.value.name = draft.value.name.trim();
  emit("save", JSON.parse(JSON.stringify(draft.value)));
  visible.value = false;
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    dismissableMask
    header="Filter rule"
    :style="{ width: '40rem', maxWidth: '95vw' }"
  >
    <div v-if="draft" class="flex flex-col gap-4">
      <!-- Name + scope + pin -->
      <div class="flex gap-3 items-end flex-wrap">
        <div class="flex flex-col gap-1 grow">
          <label class="text-xs text-surface-500">Rule name</label>
          <InputText v-model="draft.name" placeholder="e.g. Heavy doors" class="w-full" />
        </div>
        <div class="flex flex-col gap-1 w-40">
          <label class="text-xs text-surface-500">Applies to</label>
          <Select
            v-model="draft.scope"
            :options="scopeOptions"
            optionLabel="label"
            optionValue="value"
            @change="onScopeChange"
          />
        </div>
      </div>

      <div class="flex items-center gap-3">
        <span class="text-sm text-surface-600">Match</span>
        <Select
          v-model="draft.match"
          :options="matchOptions"
          optionLabel="label"
          optionValue="value"
          class="w-40"
        />
        <span class="text-sm text-surface-600">of the conditions</span>
        <label class="flex items-center gap-2 ml-auto text-sm cursor-pointer">
          <Checkbox v-model="draft.pinned" binary />
          Pin as quick filter
        </label>
      </div>

      <!-- Conditions -->
      <div class="flex flex-col gap-2">
        <div
          v-for="(cond, i) in draft.conditions"
          :key="i"
          class="flex gap-2 items-center"
        >
          <Select
            v-model="cond.field"
            :options="fieldsFor(draft.scope)"
            optionLabel="label"
            optionValue="key"
            class="w-40"
            @change="onFieldChange(cond)"
          />
          <Select
            v-model="cond.op"
            :options="operatorsFor(draft.scope, cond.field)"
            optionLabel="label"
            optionValue="value"
            class="w-40"
          />
          <InputText
            v-if="!valuelessOps.has(cond.op)"
            v-model="cond.value"
            :type="fieldType(draft.scope, cond.field) === 'number' ? 'number' : 'text'"
            placeholder="value"
            class="grow"
          />
          <span v-else class="grow" />
          <Button
            icon="pi pi-times"
            text
            rounded
            severity="secondary"
            :disabled="draft.conditions.length <= 1"
            @click="removeCondition(i)"
          />
        </div>
        <Button
          icon="pi pi-plus"
          label="Add condition"
          text
          size="small"
          class="self-start"
          @click="addCondition"
        />
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" text severity="secondary" @click="visible = false" />
      <Button label="Save rule" icon="pi pi-check" :disabled="!canSave" @click="save" />
    </template>
  </Dialog>
</template>
