<script setup lang="ts">
import { computed, ref } from "vue";
import Message from "primevue/message";
import FilterConditions from "./FilterConditions.vue";
import { bindablePaths, suggestBinding, typeLabel } from "./schema";
import type { CommandInfo, JsonSchema, NodeOutcome, PipelineNodeDoc } from "./types";

// Everything about ONE node. Parameter fields come from the command's declared input schema, so a
// command that declares its input gets a form for free and an extension author gets one without
// writing any UI — which is the whole reason #89 exists.
//
// It also has to answer the question the first version left open: what does this node HAND ON? A
// binding is written against the previous node's output, and an author who cannot see that shape is
// guessing. So the declared output is listed, and once a preview has run, the real values are shown
// beside it — a schema says there is a `rows` array, only data says whether it came back empty.

const props = defineProps<{
  node: PipelineNodeDoc;
  info: CommandInfo | null;
  /** Nodes listed BEFORE this one — the only ones a binding may read, since order is execution. */
  sources: PipelineNodeDoc[];
  index: number;
  count: number;
  /** Declared output of each node, by id, so a binding's target can be offered as a path list. */
  outputs: Record<string, JsonSchema | null>;
  /** What the last preview produced for this node, when one has run. */
  outcome: NodeOutcome | null;
  /** Renames the node, moving every binding that reads it. Returns the reason it refused, or null.
   *  A callback rather than an event because the answer has to come back on the same keystroke. */
  renamer: (next: string) => string | null;
}>();

const emit = defineEmits<{
  (e: "move", delta: number): void;
  (e: "remove"): void;
  (e: "changed"): void;
}>();

const isFilter = computed(() => props.node.command.toLowerCase() === "filter");

const renameError = ref<string | null>(null);

function rename(next: string) {
  renameError.value = props.renamer(next);
  if (!renameError.value) emit("changed");
}

/** Declared payload properties, or an empty list when the command declares no input type. */
const fields = computed<{ key: string; schema: JsonSchema }[]>(() => {
  const properties = props.info?.inputSchema?.properties;
  if (!properties) return [];
  return Object.entries(properties)
    // Filter's `where` gets its own editor below; a generic form renders it as a JSON box.
    .filter(([key]) => !(isFilter.value && (key === "where" || key === "match")))
    .map(([key, schema]) => ({ key, schema }));
});

const produces = computed(() => {
  const properties = props.info?.outputSchema?.properties;
  if (!properties) return [];
  return Object.entries(properties).map(([key, schema]) => ({ key, type: typeLabel(schema) }));
});

/** Paths a given source node offers FOR THIS FIELD — narrowed by what the field takes, so a list of
 *  rows is never offered a column of bare values to read instead. */
function pathsOf(nodeId: string, key?: string): string[] {
  return bindablePaths(
    props.outputs[nodeId] ?? null,
    key ? (props.info?.inputSchema?.properties?.[key] ?? null) : null,
  );
}

function isBound(key: string): boolean {
  return !!props.node.bind?.[key];
}

function bindingOf(key: string): { source: string; path: string } {
  const raw = props.node.bind?.[key] ?? "";
  const dot = raw.indexOf(".");
  return dot < 0 ? { source: raw, path: "" } : { source: raw.slice(0, dot), path: raw.slice(dot + 1) };
}

function setBinding(key: string, source: string | null, path?: string) {
  props.node.bind ??= {};
  if (!source) delete props.node.bind[key];
  else props.node.bind[key] = path ? `${source}.${path}` : source;
  if (props.node.bind && !Object.keys(props.node.bind).length) delete props.node.bind;
  emit("changed");
}

// A field is either typed in or wired to an earlier node — never both, since a binding wins over a
// literal of the same name and offering both would misrepresent what actually runs.
//
//  "Type a value"      — a literal that is the same on every run (a name, a number, a flag).
//  "Take from a node"  — filled at run time from what an earlier node returned.
function setMode(key: string, mode: string) {
  if (mode === "value") {
    setBinding(key, null);
    return;
  }
  if (isBound(key)) return;
  if (!props.sources.length) return;

  // The nearest earlier node whose shape fits, falling back to the immediately preceding one so the
  // switch always does something and the author can correct the path.
  const schema = props.info?.inputSchema?.properties?.[key] ?? null;
  for (let i = props.sources.length - 1; i >= 0; i--) {
    const candidate = props.sources[i];
    const path = suggestBinding(schema, props.outputs[candidate.id] ?? null, key);
    if (path) return setBinding(key, candidate.id, path);
  }

  const nearest = props.sources[props.sources.length - 1];
  setBinding(key, nearest.id, pathsOf(nearest.id, key)[0] ?? "");
}

function setParam(key: string, raw: string) {
  props.node.params ??= {};
  if (raw === "") {
    delete props.node.params[key];
  } else {
    // Typed as JSON when it parses, as text when it does not: "0" has to reach the command as a
    // number, and a schema that says "value: any" cannot tell us which the author meant.
    try {
      props.node.params[key] = JSON.parse(raw);
    } catch {
      props.node.params[key] = raw;
    }
  }
  if (props.node.params && !Object.keys(props.node.params).length) delete props.node.params;
  emit("changed");
}

function paramText(key: string): string {
  const value = props.node.params?.[key];
  if (value === undefined) return "";
  return typeof value === "string" ? value : JSON.stringify(value);
}

// --- Filter -------------------------------------------------------------------------------------

const conditions = computed(() => (props.node.params?.where as any[]) ?? []);

function setConditions(value: any[]) {
  props.node.params ??= {};
  if (value.length) props.node.params.where = value;
  else delete props.node.params.where;
  emit("changed");
}

function setMatch(value: string) {
  props.node.params ??= {};
  props.node.params.match = value;
  emit("changed");
}

/** The node whose rows this Filter is filtering, taken from the binding on `items`. */
const upstream = computed(() => bindingOf("items"));
const upstreamSchema = computed<JsonSchema | null>(() => props.outputs[upstream.value.source] ?? null);
const upstreamField = computed(() => upstream.value.path.split(/[.[]/)[0] || undefined);

const resultJson = computed(() =>
  props.outcome?.result === undefined ? "" : JSON.stringify(props.outcome.result, null, 2),
);
</script>

<template>
  <div class="flex h-full flex-col gap-3 overflow-auto p-3 text-sm">
    <div class="flex items-center gap-2">
      <span class="font-semibold">{{ node.command }}</span>
      <Tag :severity="info?.destructive ? 'danger' : 'secondary'" :value="`#${index + 1}`" />
      <span class="grow" />
      <Button
        icon="pi pi-arrow-up"
        text
        rounded
        size="small"
        :disabled="index === 0"
        aria-label="Earlier"
        @click="emit('move', -1)"
      />
      <Button
        icon="pi pi-arrow-down"
        text
        rounded
        size="small"
        :disabled="index === count - 1"
        aria-label="Later"
        @click="emit('move', 1)"
      />
      <Button
        icon="pi pi-trash"
        text
        rounded
        size="small"
        severity="danger"
        aria-label="Remove"
        @click="emit('remove')"
      />
    </div>

    <Message v-if="info?.destructive" severity="warn" size="small" variant="simple">
      This command changes the model, and previews stop before it.
    </Message>
    <p v-if="info?.description" class="text-xs opacity-70">{{ info.description }}</p>

    <!-- The id is what bindings reference and what the run receipt names. It is generated, and the
         generated one is a guess: two Filters come out as `filter` and `filter2`, which say nothing
         in the one place they are read most — a binding. So it can be corrected, and the rename
         carries every reference with it rather than leaving them pointing at a node that is gone. -->
    <div class="flex flex-col gap-1">
      <div class="flex items-center gap-2 text-xs">
        <label class="opacity-70">id</label>
        <span class="opacity-50">bindings that read this node follow the new name</span>
      </div>
      <InputText
        :model-value="node.id"
        size="small"
        class="font-mono"
        :invalid="!!renameError"
        @blur="(e: any) => rename(e.target.value)"
        @keyup.enter="(e: any) => (e.target as HTMLInputElement).blur()"
      />
      <Message v-if="renameError" severity="error" size="small" variant="simple">
        {{ renameError }}
      </Message>
    </div>

    <!-- What this node hands on. The single most useful thing when writing the NEXT node. -->
    <div v-if="produces.length" class="rounded border p-2">
      <div class="mb-1 text-xs font-medium">Produces</div>
      <div v-for="field in produces" :key="field.key" class="flex gap-2 text-xs">
        <span class="font-mono">{{ field.key }}</span>
        <span class="opacity-50">{{ field.type }}</span>
      </div>
    </div>

    <div class="flex flex-col gap-1">
      <label class="text-xs opacity-70">On failure</label>
      <Select
        v-model="node.onFailure"
        :options="['Stop', 'Continue']"
        size="small"
        @update:model-value="emit('changed')"
      />
    </div>

    <div v-if="!fields.length && !isFilter" class="text-xs opacity-60">
      This command declares no input type, so there is nothing to fill in here.
    </div>

    <div v-for="field in fields" :key="field.key" class="flex flex-col gap-1">
      <div class="flex items-center gap-2">
        <label class="text-xs font-medium">{{ field.key }}</label>
        <span class="text-xs opacity-50">{{ typeLabel(field.schema) }}</span>
        <span class="grow" />
        <!-- Both states visible, the current one selected. The old single button showed the ACTION
             and read as the STATE, which is not a distinction anyone should have to work out. -->
        <SelectButton
          :model-value="isBound(field.key) ? 'wire' : 'value'"
          :options="[
            { label: 'Type a value', value: 'value' },
            { label: 'Take from a node', value: 'wire' },
          ]"
          option-label="label"
          option-value="value"
          size="small"
          :allow-empty="false"
          :disabled="!sources.length && !isBound(field.key)"
          @update:model-value="(v) => setMode(field.key, String(v))"
        />
      </div>

      <div v-if="isBound(field.key)" class="flex gap-1">
        <!-- With one earlier node there is nothing to choose, so the picker is a control that only
             costs a glance. It appears the moment a second node makes it a real question. -->
        <span
          v-if="sources.length < 2"
          class="flex w-1/2 items-center rounded bg-surface-100 px-2 font-mono text-xs dark:bg-surface-800"
        >
          {{ bindingOf(field.key).source }}
        </span>
        <Select
          v-else
          :model-value="bindingOf(field.key).source"
          :options="sources.map((s) => s.id)"
          size="small"
          class="w-1/2"
          @update:model-value="(v) => setBinding(field.key, String(v), bindingOf(field.key).path)"
        />
        <AutoComplete
          :model-value="bindingOf(field.key).path"
          :suggestions="pathsOf(bindingOf(field.key).source, field.key)"
          dropdown
          size="small"
          class="w-1/2"
          placeholder="path"
          @complete="() => {}"
          @update:model-value="
            (v: any) => setBinding(field.key, bindingOf(field.key).source, String(v ?? ''))
          "
        />
      </div>

      <InputText
        v-else
        :model-value="paramText(field.key)"
        :placeholder="String(field.schema.description ?? '')"
        size="small"
        @update:model-value="(v) => setParam(field.key, String(v ?? ''))"
      />
    </div>

    <div v-if="isFilter" class="rounded border p-2 text-xs">
      <span v-if="upstream.source">
        Filtering the rows of
        <span class="font-mono">{{ upstream.source }}.{{ upstream.path || "(whole result)" }}</span>
        — one condition is applied to each row.
      </span>
      <span v-else class="text-amber-600">
        Not reading anything yet. Set <span class="font-mono">items</span> above to
        “Take from a node”, and the field names below will fill in from that node's rows.
      </span>
    </div>

    <FilterConditions
      v-if="isFilter"
      :conditions="conditions"
      :upstream-schema="upstreamSchema"
      :bound-field="upstreamField"
      :match="String(node.params?.match ?? 'all')"
      @update="setConditions"
      @update:match="setMatch"
    />

    <!-- Real data, once a preview has run. A schema says there is a `rows` array; only this says
         whether it came back with anything in it. -->
    <div v-if="outcome" class="flex min-h-0 flex-col gap-1">
      <div class="flex items-center gap-2">
        <span class="text-xs font-medium">Preview result</span>
        <Tag
          :severity="outcome.state === 'Completed' ? 'success' : 'danger'"
          :value="outcome.state"
        />
      </div>
      <Message v-if="outcome.error" severity="error" size="small" variant="simple">
        {{ outcome.error }}
      </Message>
      <pre
        v-else
        class="max-h-64 overflow-auto rounded bg-surface-100 p-2 text-xs dark:bg-surface-800"
        >{{ resultJson }}</pre
      >
    </div>
  </div>
</template>
