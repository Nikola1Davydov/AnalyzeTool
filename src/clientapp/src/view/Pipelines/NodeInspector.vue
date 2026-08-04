<script setup lang="ts">
import { computed } from "vue";
import Message from "primevue/message";
import type { CommandInfo, JsonSchema, PipelineNodeDoc } from "./types";

// Everything about ONE node. Parameter fields come from the command's declared input schema, so a
// command that declares its input gets a form for free and an extension author gets one without
// writing any UI — which is the whole reason #89 exists.

const props = defineProps<{
  node: PipelineNodeDoc;
  info: CommandInfo | null;
  /** Nodes listed BEFORE this one — the only ones a binding may read, since order is execution. */
  sources: PipelineNodeDoc[];
  index: number;
  count: number;
}>();

const emit = defineEmits<{
  (e: "rename", value: string): void;
  (e: "move", delta: number): void;
  (e: "remove"): void;
  (e: "changed"): void;
}>();

/** Declared payload properties, or an empty list when the command declares no input type. */
const fields = computed<{ key: string; schema: JsonSchema }[]>(() => {
  const properties = props.info?.inputSchema?.properties;
  if (!properties) return [];
  return Object.entries(properties).map(([key, schema]) => ({ key, schema }));
});

function typeOf(schema: JsonSchema): string {
  const t = Array.isArray(schema.type) ? schema.type.find((x) => x !== "null") : schema.type;
  return t ?? "any";
}

/** A field is either typed in (a literal) or wired to an earlier node — never both, since a binding
 *  wins over a literal of the same name and showing both would misrepresent what runs. */
function isBound(key: string): boolean {
  return !!props.node.bind?.[key];
}

function setBinding(key: string, value: string | null) {
  props.node.bind ??= {};
  if (!value) delete props.node.bind[key];
  else props.node.bind[key] = value;
  if (props.node.bind && !Object.keys(props.node.bind).length) delete props.node.bind;
  emit("changed");
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
      This command changes the model.
    </Message>
    <p v-if="info?.description" class="text-xs opacity-70">{{ info.description }}</p>

    <div class="flex flex-col gap-1">
      <label class="text-xs opacity-70">Node id</label>
      <InputText
        :model-value="node.id"
        size="small"
        @update:model-value="(v) => emit('rename', String(v ?? ''))"
      />
      <span class="text-xs opacity-50">Bindings from other nodes follow a rename.</span>
    </div>

    <div class="flex flex-col gap-1">
      <label class="text-xs opacity-70">On failure</label>
      <Select
        v-model="node.onFailure"
        :options="['Stop', 'Continue']"
        size="small"
        @update:model-value="emit('changed')"
      />
      <span class="text-xs opacity-50">
        Applies only when the command throws. Cancelling always ends the run.
      </span>
    </div>

    <div v-if="!fields.length" class="text-xs opacity-60">
      This command declares no input type, so there is nothing to fill in here. Anything it does take
      can still be sent — a pipeline written by hand may carry params we cannot vouch for.
    </div>

    <div v-for="field in fields" :key="field.key" class="flex flex-col gap-1">
      <div class="flex items-center gap-2">
        <label class="text-xs font-medium">{{ field.key }}</label>
        <span class="text-xs opacity-50">{{ typeOf(field.schema) }}</span>
        <span class="grow" />
        <Button
          :label="isBound(field.key) ? 'Value' : 'From node'"
          text
          size="small"
          :disabled="!sources.length && !isBound(field.key)"
          @click="setBinding(field.key, isBound(field.key) ? null : sources[0].id)"
        />
      </div>

      <div v-if="isBound(field.key)" class="flex gap-1">
        <Select
          :model-value="(node.bind?.[field.key] ?? '').split('.')[0]"
          :options="sources.map((s) => s.id)"
          size="small"
          class="w-1/2"
          @update:model-value="
            (v) =>
              setBinding(
                field.key,
                String(v) + (node.bind![field.key].includes('.') ? '.' + node.bind![field.key].split('.').slice(1).join('.') : ''),
              )
          "
        />
        <InputText
          :model-value="(node.bind?.[field.key] ?? '').split('.').slice(1).join('.')"
          placeholder="path, e.g. items[*].id"
          size="small"
          class="w-1/2"
          @update:model-value="
            (v) =>
              setBinding(
                field.key,
                (node.bind![field.key] ?? '').split('.')[0] + (v ? '.' + v : ''),
              )
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
  </div>
</template>
