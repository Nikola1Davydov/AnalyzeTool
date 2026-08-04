<script setup lang="ts">
import { computed } from "vue";
import type { JsonSchema } from "./types";
import { itemFieldNames } from "./schema";

// A condition editor for the Filter node.
//
// A special case, and worth admitting as one. Filter is the format's own orchestrator node and its
// `where` is a list of objects, which the generic schema-driven form renders as a JSON text box —
// technically correct and unusable. The first agent to write a Filter unprompted put its conditions
// somewhere nothing read, and the first person to open one in the editor could not tell how to
// apply it. Both are the same missing thing: nowhere does anything say which FIELDS you may compare.
//
// So the field list comes from the upstream node's declared output — and specifically from the
// shape INSIDE its array, because a Filter compares rows, not the envelope around them.

const props = defineProps<{
  conditions: Condition[];
  /** Declared output of the node this Filter reads from, if it is wired to one. */
  upstreamSchema: JsonSchema | null;
  /** Which of the upstream's properties is bound into `items`, when a binding says so. */
  boundField?: string;
  match: string;
}>();

const emit = defineEmits<{
  (e: "update", value: Condition[]): void;
  (e: "update:match", value: string): void;
}>();

interface Condition {
  field?: string;
  op?: string;
  value?: unknown;
}

const OPS = [
  "equals",
  "notEquals",
  "contains",
  "exists",
  "notExists",
  "greaterThan",
  "lessThan",
] as const;

/** Operators that ignore `value` — offering a box for it would invite a meaningless entry. */
const VALUELESS = new Set(["exists", "notExists"]);

const fields = computed(() => itemFieldNames(props.upstreamSchema, props.boundField));

function add() {
  emit("update", [...props.conditions, { field: fields.value[0] ?? "", op: "equals", value: "" }]);
}

function remove(index: number) {
  emit(
    "update",
    props.conditions.filter((_, i) => i !== index),
  );
}

function patch(index: number, change: Partial<Condition>) {
  emit(
    "update",
    props.conditions.map((c, i) => (i === index ? { ...c, ...change } : c)),
  );
}

/** Typed as JSON when it parses, so `0` and `false` reach the command as a number and a boolean —
 *  the comparison is textual either way, but a quoted "0" in the file misleads whoever reads it. */
function setValue(index: number, raw: string) {
  let parsed: unknown = raw;
  try {
    parsed = JSON.parse(raw);
  } catch {
    /* plain text, which is a perfectly good value */
  }
  patch(index, { value: parsed });
}

function valueText(condition: Condition): string {
  if (condition.value === undefined || condition.value === null) return "";
  return typeof condition.value === "string" ? condition.value : JSON.stringify(condition.value);
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex items-center gap-2">
      <label class="text-xs font-medium">Conditions</label>
      <span class="grow" />
      <Select
        :model-value="match || 'all'"
        :options="['all', 'any']"
        size="small"
        class="w-24"
        @update:model-value="(v) => emit('update:match', String(v))"
      />
      <Button icon="pi pi-plus" text rounded size="small" aria-label="Add" @click="add" />
    </div>

    <p v-if="!conditions.length" class="text-xs opacity-60">
      No conditions means every item passes through. That is fine while you are still wiring things
      up, and refused before a node that changes the model.
    </p>

    <p v-else-if="!fields.length" class="text-xs opacity-60">
      The node this reads from declares no row shape, so field names cannot be offered — type them.
    </p>

    <div v-for="(condition, index) in conditions" :key="index" class="flex flex-col gap-1">
      <div class="flex gap-1">
        <!-- editable so a field the schema does not declare can still be typed -->
        <AutoComplete
          :model-value="condition.field"
          :suggestions="fields"
          dropdown
          size="small"
          class="w-full"
          placeholder="field"
          @complete="() => {}"
          @update:model-value="(v: any) => patch(index, { field: String(v ?? '') })"
        />
        <Button
          icon="pi pi-times"
          text
          rounded
          size="small"
          aria-label="Remove"
          @click="remove(index)"
        />
      </div>
      <div class="flex gap-1">
        <Select
          :model-value="condition.op || 'equals'"
          :options="[...OPS]"
          size="small"
          class="w-1/2"
          @update:model-value="(v) => patch(index, { op: String(v) })"
        />
        <InputText
          v-if="!VALUELESS.has(condition.op || 'equals')"
          :model-value="valueText(condition)"
          placeholder="value, e.g. 0"
          size="small"
          class="w-1/2"
          @update:model-value="(v) => setValue(index, String(v ?? ''))"
        />
      </div>
    </div>
  </div>
</template>
