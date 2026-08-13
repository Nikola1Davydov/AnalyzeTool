<script setup lang="ts">
/**
 * The dockable command launcher.
 *
 * It exists because the ribbon does not scale: an extension gets at most one button, so a session that
 * generates ten commands would otherwise leave ten buttons behind. One button opens this instead, and
 * the list behind it grows for free.
 *
 * The form is BUILT FROM THE SCHEMA. Every command publishes its inputSchema (that is what InputType
 * generates), so a launcher does not need a hand-written page per command — it reads the schema and
 * renders the fields. Which is also why a generated command that declares no InputType shows up here
 * with no form: it is not that the launcher cannot show it, it is that the command never said what it
 * takes.
 */
import { computed, onMounted, ref } from "vue";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

type JsonSchema = {
  type?: string | string[];
  properties?: Record<string, JsonSchema>;
  description?: string;
  items?: JsonSchema;
};

type CommandInfo = {
  name: string;
  source: string;
  description?: string | null;
  readOnly: boolean;
  destructive: boolean;
  inputSchema?: JsonSchema | null;
};

const notificationStore = useNotificationStore();

const commands = ref<CommandInfo[]>([]);
const loading = ref(false);
const search = ref("");
const onlyExtensions = ref(true);
const selected = ref<CommandInfo | null>(null);
const args = ref<Record<string, unknown>>({});
const running = ref(false);
const result = ref<string | null>(null);
const failed = ref(false);

/** "core" is the host's own surface — dozens of commands nobody launches by hand. Extensions are what
 *  this window is for, so they lead; the toggle is there for the times that is wrong. */
const filtered = computed(() => {
  const needle = search.value.trim().toLowerCase();
  return commands.value
    .filter((c) => !onlyExtensions.value || c.source !== "core")
    .filter(
      (c) =>
        !needle ||
        c.name.toLowerCase().includes(needle) ||
        (c.description ?? "").toLowerCase().includes(needle),
    );
});

/** Only the top level: a nested object is rendered as raw JSON rather than pretended to be a form. */
const fields = computed(() => {
  const properties = selected.value?.inputSchema?.properties;
  if (!properties) return [];
  return Object.entries(properties).map(([name, schema]) => ({
    name,
    description: schema.description ?? "",
    kind: kindOf(schema),
  }));
});

function kindOf(schema: JsonSchema): "string" | "number" | "boolean" | "array" | "json" {
  const declared = Array.isArray(schema.type)
    ? schema.type.filter((t) => t !== "null")
    : [schema.type].filter(Boolean);
  const type = declared[0];
  if (type === "integer" || type === "number") return "number";
  if (type === "boolean") return "boolean";
  if (type === "array") return "array";
  if (type === "string") return "string";
  return "json";
}

async function load() {
  loading.value = true;
  try {
    const res = await invoke<{ commands: CommandInfo[] }>("GetCommands", null);
    commands.value = res?.commands ?? [];
  } catch (err) {
    notificationStore.error(String((err as Error)?.message ?? err));
  } finally {
    loading.value = false;
  }
}

function select(command: CommandInfo) {
  selected.value = command;
  result.value = null;
  failed.value = false;
  args.value = {};
  for (const field of fields.value) {
    args.value[field.name] = field.kind === "boolean" ? false : "";
  }
}

/** Empty fields are OMITTED, not sent as "". An optional filter left blank must not become a filter
 *  for the empty string, which would quietly return nothing and look like "no matches". */
function buildPayload(): Record<string, unknown> | null {
  const payload: Record<string, unknown> = {};
  for (const field of fields.value) {
    const raw = args.value[field.name];
    if (field.kind === "boolean") {
      if (raw) payload[field.name] = true;
      continue;
    }
    const text = String(raw ?? "").trim();
    if (!text) continue;

    if (field.kind === "number") payload[field.name] = Number(text);
    else if (field.kind === "array")
      payload[field.name] = text
        .split(/[\s,]+/)
        .filter(Boolean)
        .map((v) => (/^-?\d+$/.test(v) ? Number(v) : v));
    else if (field.kind === "json") {
      try {
        payload[field.name] = JSON.parse(text);
      } catch {
        notificationStore.error(`'${field.name}' is not valid JSON.`);
        return null;
      }
    } else payload[field.name] = text;
  }
  return payload;
}

async function run() {
  if (!selected.value) return;
  const payload = buildPayload();
  if (payload === null) return;

  running.value = true;
  result.value = null;
  failed.value = false;
  try {
    const answer = await invoke<unknown>(selected.value.name, Object.keys(payload).length ? payload : null);
    result.value = typeof answer === "string" ? answer : JSON.stringify(answer, null, 2);
  } catch (err) {
    failed.value = true;
    result.value = String((err as Error)?.message ?? err);
  } finally {
    running.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="flex flex-col h-full gap-2 p-2 text-sm">
    <div class="flex items-center gap-2">
      <InputText v-model="search" size="small" placeholder="Search commands…" class="flex-1 !text-xs" />
      <Button size="small" text icon="pi pi-refresh" :loading="loading" @click="load" />
    </div>

    <div class="flex items-center gap-2 text-xs">
      <Checkbox v-model="onlyExtensions" binary input-id="onlyExt" />
      <label for="onlyExt" class="cursor-pointer">Extensions only</label>
      <span class="ml-auto opacity-60">{{ filtered.length }}</span>
    </div>

    <div class="flex-1 min-h-0 overflow-auto border rounded" style="border-color: var(--p-content-border-color)">
      <div v-if="!filtered.length" class="p-3 text-xs opacity-60">
        {{ loading ? "Loading…" : "No commands match." }}
      </div>
      <button
        v-for="command in filtered"
        :key="command.name"
        class="w-full text-left px-2 py-1.5 border-b hover:bg-[var(--p-content-hover-background)]"
        :class="{ 'bg-[var(--p-highlight-background)]': selected?.name === command.name }"
        style="border-color: var(--p-content-border-color)"
        @click="select(command)"
      >
        <div class="flex items-center gap-1">
          <span class="font-medium truncate">{{ command.name }}</span>
          <i v-if="command.destructive" class="pi pi-exclamation-triangle text-[10px] text-orange-500" />
        </div>
        <div v-if="command.description" class="text-[11px] opacity-60 line-clamp-2">
          {{ command.description }}
        </div>
      </button>
    </div>

    <div v-if="selected" class="flex flex-col gap-2 border-t pt-2" style="border-color: var(--p-content-border-color)">
      <div class="font-medium text-xs">{{ selected.name }}</div>

      <div v-if="!fields.length" class="text-[11px] opacity-60">
        Takes no arguments — or declares no InputType, in which case the launcher cannot know what it takes.
      </div>

      <div v-for="field in fields" :key="field.name" class="flex flex-col gap-0.5">
        <label class="text-[11px] opacity-70" :title="field.description">{{ field.name }}</label>
        <Checkbox v-if="field.kind === 'boolean'" v-model="args[field.name]" binary />
        <!-- A plain textarea, not PrimeVue's: it is registered globally or not at all (see main.js),
             and adding one to every window for a single fallback field is not worth it. -->
        <textarea
          v-else-if="field.kind === 'json'"
          v-model="args[field.name]"
          rows="2"
          class="text-xs p-1.5 rounded border font-mono"
          style="
            background: var(--p-inputtext-background);
            color: var(--p-inputtext-color);
            border-color: var(--p-inputtext-border-color);
          "
          placeholder="JSON"
        ></textarea>
        <InputText
          v-else
          v-model="args[field.name]"
          size="small"
          class="!text-xs"
          :placeholder="field.kind === 'array' ? 'comma or space separated' : field.kind"
        />
      </div>

      <Button
        size="small"
        :label="selected.destructive ? 'Run (modifies the model)' : 'Run'"
        :severity="selected.destructive ? 'warning' : 'primary'"
        :loading="running"
        icon="pi pi-play"
        @click="run"
      />

      <pre
        v-if="result !== null"
        class="max-h-40 overflow-auto text-[11px] whitespace-pre-wrap p-2 rounded"
        :class="failed ? 'text-red-500' : ''"
        style="background: var(--p-content-hover-background)"
        >{{ result }}</pre
      >
    </div>
  </div>
</template>
