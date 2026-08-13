<script setup lang="ts">
/**
 * The dockable command launcher.
 *
 * It exists because the ribbon does not scale: an extension gets at most one button, so a session that
 * generates ten commands would otherwise leave ten buttons behind. One button opens this instead, and
 * the list behind it grows for free.
 *
 * TWO VIEWS, not one page. The list answers "what do I have"; a command's own page answers "run this
 * one". They were the same screen at first, with the form pinned under the list — which put the Run
 * button as far from the command you clicked as the layout allowed, and grew the further you scrolled.
 *
 * The form is BUILT FROM THE SCHEMA. Every command publishes its inputSchema (that is what InputType
 * generates), so a launcher does not need a hand-written page per command — it reads the schema and
 * renders the fields. Which is also why a generated command that declares no InputType opens with no
 * form: it is not that the launcher cannot show it, it is that the command never said what it takes.
 *
 * Each row also decides WHERE its command lives. The pin is the user's answer to "the shared list, or
 * a button of its own" — stored as an override of what the extension's manifest declares, so it works
 * the same for a command from an installed package (whose manifest must not be written) and for a
 * built-in one (which has no manifest at all).
 */
import { computed, ref, watch } from "vue";
import { useRoute } from "vue-router";
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
  /** Where the command came from: "core" | "script" | "dll" | "js". */
  kind: string;
  description?: string | null;
  readOnly: boolean;
  destructive: boolean;
  onRibbon: boolean;
  inputSchema?: JsonSchema | null;
};

const route = useRoute();
const notificationStore = useNotificationStore();

const commands = ref<CommandInfo[]>([]);
const loading = ref(false);
const search = ref("");
const running = ref(false);
const pinning = ref<string | null>(null);
const selected = ref<CommandInfo | null>(null);
const args = ref<Record<string, unknown>>({});
const result = ref<string | null>(null);
const failed = ref(false);

/**
 * Scripts lead, and by default they are all you see.
 *
 * A DLL extension is a built thing that ships its own page and its own ribbon button — listing its
 * commands here offers a second, worse way into something that already has a front door. "core" is the
 * host's own surface, dozens of commands nobody launches by hand. Neither is hidden for good: All is
 * one click away, and you cannot pin what the list will not show you.
 */
const scope = ref<"scripts" | "all" | "ribbon">("scripts");
const scopes = [
  { label: "Scripts", value: "scripts" },
  { label: "All", value: "all" },
  { label: "Ribbon", value: "ribbon" },
];

const filtered = computed(() => {
  const needle = search.value.trim().toLowerCase();
  return commands.value
    .filter((c) =>
      scope.value === "scripts" ? c.kind === "script" : scope.value === "ribbon" ? c.onRibbon : true,
    )
    .filter(
      (c) =>
        !needle ||
        c.name.toLowerCase().includes(needle) ||
        (c.description ?? "").toLowerCase().includes(needle),
    );
});

/** Only the top level: a nested object is rendered as raw JSON rather than pretended to be a form. */
function fieldsOf(command: CommandInfo | null) {
  const properties = command?.inputSchema?.properties;
  if (!properties) return [];
  return Object.entries(properties).map(([name, schema]) => ({
    name,
    description: schema.description ?? "",
    kind: kindOf(schema),
  }));
}

const fields = computed(() => fieldsOf(selected.value));

/** Whether a click can run this command outright, or has to ask for arguments first. */
function takesInput(command: CommandInfo): boolean {
  return Object.keys(command.inputSchema?.properties ?? {}).length > 0;
}

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

/** Opens a command's own page. Its form starts empty every time — a leftover value from the last run
 *  is worse than a blank field, because nothing on screen says it is left over. */
function open(command: CommandInfo) {
  selected.value = command;
  result.value = null;
  failed.value = false;
  args.value = {};
  for (const field of fieldsOf(command)) {
    args.value[field.name] = field.kind === "boolean" ? false : "";
  }
}

function back() {
  selected.value = null;
  result.value = null;
  failed.value = false;
}

/**
 * The row's ▶. It always opens the command's page, because that is where the answer goes — the same
 * shape as the ribbon, where a pinned command runs and reports into a dialog. What differs is whether
 * it runs on arrival: a command with nothing to fill in does, one with arguments waits for them.
 *
 * A DESTRUCTIVE command waits too, even with no arguments to give it. One click in a list should not
 * change the model; the page says what it does and puts a labelled button under it.
 */
function start(command: CommandInfo) {
  open(command);
  if (!takesInput(command) && !command.destructive) void run();
}

/** Moves a command between the ribbon and this list. The command itself never moves — it stays
 *  callable from here, from MCP and from JS either way; only the button appears or goes. */
async function togglePin(command: CommandInfo) {
  const next = !command.onRibbon;
  pinning.value = command.name;
  try {
    await invoke("SetCommandButton", { command: command.name, onRibbon: next });
    command.onRibbon = next;
  } catch (err) {
    notificationStore.error(String((err as Error)?.message ?? err));
  } finally {
    pinning.value = null;
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

    if (field.kind === "number") {
      // Reported, not passed on. Number("5O") is NaN, which JSON serializes to null — the host would
      // read that as "no value given" and, for something like limit, quietly return everything.
      const value = Number(text);
      if (!Number.isFinite(value)) {
        notificationStore.error(`'${field.name}' is not a number.`);
        return null;
      }
      payload[field.name] = value;
    } else if (field.kind === "array")
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

/** The initial load, started once and shared: the watcher below runs before mount, and both it and the
 *  first render need the list. The refresh button calls load() again on purpose. */
const ready = load();

/**
 * A pinned button for a command that TAKES arguments routes here with ?command=… rather than running it
 * blind — this is the page it was sent to find, so open on it.
 *
 * A WATCHER, not onMounted. The pane navigates by changing the hash fragment, which is a same-document
 * navigation: vue-router keeps this component mounted and reuses it, so onMounted fired for the first
 * pinned button and never again. The second button then showed the FIRST command's form, and Run
 * executed the wrong command.
 */
watch(
  () => route.query.command,
  async (raw) => {
    await ready;

    const wanted = String(raw ?? "").trim();
    if (!wanted) return; // arrived through the plain Scripts button; leave any selection alone

    const match = commands.value.find((c) => c.name.toLowerCase() === wanted.toLowerCase());
    if (!match) {
      notificationStore.error(`'${wanted}' is not registered.`);
      return;
    }
    if (match.kind !== "script") scope.value = "all"; // else the default scope would hide it on Back
    open(match);
  },
  { immediate: true },
);
</script>

<template>
  <div class="flex flex-col h-full gap-2 p-2 text-sm">
    <!-- LIST ------------------------------------------------------------------------------------->
    <template v-if="!selected">
      <div class="flex items-center gap-2">
        <InputText v-model="search" size="small" placeholder="Search commands…" class="flex-1 !text-xs" />
        <Button size="small" text icon="pi pi-refresh" :loading="loading" @click="load" />
      </div>

      <div class="flex items-center gap-2">
        <SelectButton
          v-model="scope"
          :options="scopes"
          optionLabel="label"
          optionValue="value"
          :allowEmpty="false"
          size="small"
          class="!text-xs"
        />
        <span class="ml-auto text-xs opacity-60">{{ filtered.length }}</span>
      </div>

      <div class="flex-1 min-h-0 overflow-auto border rounded" style="border-color: var(--p-content-border-color)">
        <div v-if="!filtered.length" class="p-3 text-xs opacity-60">
          {{ loading ? "Loading…" : "No commands match." }}
        </div>
        <!-- A row is a div, not a button: it holds two of its own, and a button inside a button is not
             valid. The LEFT BORDER is the tell — amber where a click opens a form, teal where it just
             runs — so the list reads before you have read a single name. -->
        <div
          v-for="command in filtered"
          :key="command.name"
          class="flex items-center gap-1 border-b border-l-2 hover:bg-[var(--p-content-hover-background)]"
          :class="takesInput(command) ? 'border-l-amber-400' : 'border-l-teal-400'"
          style="border-bottom-color: var(--p-content-border-color)"
        >
          <button class="flex-1 min-w-0 text-left px-2 py-1.5" @click="open(command)">
            <div class="flex items-center gap-1">
              <span class="font-medium truncate">{{ command.name }}</span>
              <i v-if="command.destructive" class="pi pi-exclamation-triangle text-[10px] text-orange-500" />
            </div>
            <div v-if="command.description" class="text-[11px] opacity-60 line-clamp-2">
              {{ command.description }}
            </div>
          </button>
          <Button
            size="small"
            text
            class="shrink-0"
            icon="pi pi-thumbtack"
            :severity="command.onRibbon ? 'primary' : 'secondary'"
            :loading="pinning === command.name"
            v-tooltip.left="command.onRibbon ? 'On the ribbon — click to remove' : 'Add a ribbon button'"
            @click="togglePin(command)"
          />
          <Button
            size="small"
            text
            class="shrink-0"
            icon="pi pi-play"
            :severity="command.destructive ? 'warning' : 'primary'"
            v-tooltip.left="
              takesInput(command) || command.destructive ? 'Open and fill in' : 'Run now'
            "
            @click="start(command)"
          />
        </div>
      </div>
    </template>

    <!-- ONE COMMAND ------------------------------------------------------------------------------>
    <template v-else>
      <div class="flex items-center gap-1">
        <Button size="small" text icon="pi pi-arrow-left" label="Scripts" class="!text-xs !px-1" @click="back" />
        <Button
          size="small"
          text
          class="ml-auto shrink-0"
          icon="pi pi-thumbtack"
          :severity="selected.onRibbon ? 'primary' : 'secondary'"
          :loading="pinning === selected.name"
          v-tooltip.left="selected.onRibbon ? 'On the ribbon — click to remove' : 'Add a ribbon button'"
          @click="togglePin(selected)"
        />
      </div>

      <div class="flex-1 min-h-0 overflow-auto flex flex-col gap-2">
        <div>
          <div class="font-medium break-all">{{ selected.name }}</div>
          <div v-if="selected.description" class="text-[11px] opacity-60 mt-0.5">
            {{ selected.description }}
          </div>
        </div>

        <div v-if="selected.destructive" class="text-[11px] text-orange-500 flex items-start gap-1">
          <i class="pi pi-exclamation-triangle text-[10px] mt-0.5" />
          <span>This command modifies the model.</span>
        </div>

        <div v-if="!fields.length" class="text-[11px] opacity-60">
          Takes no arguments — or declares no InputType, in which case the launcher cannot know what it
          takes.
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
          <div v-if="field.description" class="text-[10px] opacity-50">{{ field.description }}</div>
        </div>

        <pre
          v-if="result !== null"
          class="text-[11px] whitespace-pre-wrap p-2 rounded"
          :class="failed ? 'text-red-500' : ''"
          style="background: var(--p-content-hover-background)"
          >{{ result }}</pre
        >
      </div>

      <Button
        size="small"
        class="shrink-0"
        :label="selected.destructive ? 'Run (modifies the model)' : 'Run'"
        :severity="selected.destructive ? 'warning' : 'primary'"
        :loading="running"
        icon="pi pi-play"
        @click="run"
      />
    </template>
  </div>
</template>
