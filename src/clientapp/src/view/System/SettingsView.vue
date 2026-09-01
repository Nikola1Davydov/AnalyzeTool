<script setup lang="ts">
/**
 * Settings — the plugin itself, and nothing else.
 *
 * This page used to be one tab of four, sharing a window with the extension manager and a read-only
 * command reference. Of the thirty-odd controls in that window, four were settings; the rest was work
 * you do (install, update, create) or documentation you read. Those moved to the Extensions window,
 * and what is left fits on one screen.
 *
 * Two groups, named after the question a person is actually asking — "what may the AI do",
 * "what is this" — not after the subsystem behind them. Anything only an author
 * needs (port, token, client snippet, the command list) is one click down, never on the surface.
 */
import { ref, computed, onMounted } from "vue";
import { storeToRefs } from "pinia";
import ToggleSwitch from "primevue/toggleswitch";
import { invoke } from "@/RevitBridge";
import { useUpdateStore } from "@/stores/useUpdateStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import AiModelPicker from "@/components/AiModelPicker.vue";

const notifications = useNotificationStore();

/** Where "Report a bug" goes — the same issues page the ribbon button opens. */
const ISSUES_URL = "https://github.com/Nikola1Davydov/AnalyzeTool/issues";

/** Message from a rejected invoke, ready to show. */
function errorText(e: unknown): string {
  return String((e as Error)?.message ?? e);
}

// --- About: the host facts. Same command the extension manager uses; we only read its header. -----
interface EnvironmentData {
  hostRevit: string;
  hostSdkVersion: string;
  pluginVersion: string;
  extensionsRoot: string;
}
const env = ref<EnvironmentData | null>(null);

async function loadEnvironment() {
  try {
    env.value = await invoke<EnvironmentData>("GetInstalledExtensions");
  } catch (e) {
    console.error("Failed to load environment info", e);
  }
}

// Same update-check the main AnalyseTool window uses (CheckUpdate command), surfaced next to the version.
const { updateInfo } = storeToRefs(useUpdateStore());

// --- Changelog (CHANGELOG.md ships next to the plugin DLL; rendered as markdown on demand) --------
const changelogVisible = ref(false);
const changelogHtml = ref<string | null>(null);
const changelogError = ref<string | null>(null);

async function openChangelog() {
  changelogVisible.value = true;
  if (changelogHtml.value) return; // fetched once per window
  // Clear the previous failure: the template checks the error branch first, so a single failed
  // fetch used to keep showing its message for the rest of the window's life — even after a
  // later open succeeded and the content was sitting right there, unrendered.
  changelogError.value = null;
  try {
    const res = await invoke<{ markdown: string | null; error: string | null }>("GetChangelog");
    if (res?.markdown) {
      const { marked } = await import("marked"); // lazy — only when the dialog is opened
      changelogHtml.value = await marked.parse(res.markdown);
    } else {
      changelogError.value = res?.error ?? "Changelog not available.";
    }
  } catch (e) {
    changelogError.value = String((e as Error)?.message ?? e);
  }
}

function openFolder(path: string | undefined) {
  if (!path) return;
  invoke("OpenFolder", { path }).catch((e) => console.error(e));
}

// --- C# code execution: gates the ad-hoc ExecuteRevitCode command (the AI scratchpad). -----------
const codeExec = ref(false);
const codeExecBusy = ref(false);

async function loadCodeExec() {
  try {
    const res = await invoke<{ enabled: boolean }>("GetCodeExecutionStatus");
    codeExec.value = !!res?.enabled;
  } catch (e) {
    console.error("Failed to load code-execution status", e);
  }
}

async function setCodeExec(enabled: boolean) {
  const previous = codeExec.value;
  codeExec.value = enabled;
  codeExecBusy.value = true;
  try {
    const res = await invoke<{ enabled: boolean }>("SetCodeExecution", { enabled });
    codeExec.value = !!res?.enabled;
  } catch (e) {
    // Of every toggle in this window, this is the one that must never lie: it gates arbitrary C#
    // execution inside Revit. Restore the value we know the host still holds and say so out loud.
    codeExec.value = previous;
    notifications.error(`Could not change the C# code-execution setting: ${errorText(e)}`);
  } finally {
    codeExecBusy.value = false;
  }
}

// --- MCP server: exposes every command (built-in + extensions) to AI clients. --------------------
interface McpStatus {
  running: boolean;
  enabled: boolean;
  port: number;
  configuredPort: number;
  wsUrl: string;
  serverExePath: string;
  serverExeExists: boolean;
  token: string;
  lastError: string | null;
}

const mcp = ref<McpStatus | null>(null);
const mcpBusy = ref(false);
const port = ref("17890");

async function loadMcp() {
  try {
    const status = await invoke<McpStatus>("GetMcpStatus");
    mcp.value = status;
    port.value = String(status.configuredPort);
  } catch (e) {
    console.error("Failed to load MCP status", e);
  }
}

async function applyMcp(enabled: boolean) {
  mcpBusy.value = true;
  try {
    const status = await invoke<McpStatus>("SetMcpServer", {
      enabled,
      port: Number(port.value) || undefined,
    });
    mcp.value = status;
    port.value = String(status.configuredPort);
  } catch (e) {
    notifications.error(`Could not ${enabled ? "start" : "stop"} the MCP server: ${errorText(e)}`);
  } finally {
    mcpBusy.value = false;
  }
}

const clientConfig = computed(() => {
  if (!mcp.value) return "";
  return JSON.stringify(
    {
      mcpServers: {
        "analysetool-revit": {
          command: mcp.value.serverExePath,
          // --token is required: Revit rejects bridge calls that don't carry it, so a config copied
          // before this version has to be replaced with this one.
          args: ["--port", String(mcp.value.port), "--token", mcp.value.token],
        },
      },
    },
    null,
    2,
  );
});

const copied = ref(false);
async function copyConfig() {
  try {
    await navigator.clipboard.writeText(clientConfig.value);
    copied.value = true;
    setTimeout(() => (copied.value = false), 1500);
  } catch (e) {
    console.error("Clipboard write failed", e);
  }
}

// --- Commands: the full reference, for people writing extensions against AT.invoke. --------------
interface CommandRow {
  name: string;
  source: string; // "core" for built-ins, else the extension id
  description: string | null;
  readOnly: boolean;
  destructive: boolean;
  exposedToMcp: boolean;
  inputSchema: any;
}

const commands = ref<CommandRow[]>([]);
const commandSearch = ref("");

const filteredCommands = computed(() => {
  const q = commandSearch.value.trim().toLowerCase();
  if (!q) return commands.value;
  return commands.value.filter(
    (c) =>
      c.name.toLowerCase().includes(q) ||
      (c.source ?? "").toLowerCase().includes(q) ||
      (c.description ?? "").toLowerCase().includes(q),
  );
});

/** Summarize a command's JSON-schema payload as "field: type, …" for the table. */
function payloadSummary(schema: any): string {
  if (!schema || typeof schema !== "object") return "—";
  const props = schema.properties;
  if (props && typeof props === "object") {
    const keys = Object.keys(props);
    if (keys.length)
      return keys.map((k) => (props[k]?.type ? `${k}: ${props[k].type}` : k)).join(", ");
  }
  if (schema.additionalProperties) return "(free-form object)";
  return "—";
}

async function loadCommands() {
  try {
    const res = await invoke<{ commands: CommandRow[] }>("GetCommands");
    commands.value = res?.commands ?? [];
  } catch (e) {
    console.error("Failed to load commands", e);
  }
}

onMounted(() => {
  loadEnvironment();
  loadCodeExec();
  loadMcp();
  loadCommands();
  // No loadUpdateData() here: App.vue already runs it for every window, and the store has no
  // in-flight guard — calling it again just spent a second GitHub API request on a result we hold.
});
</script>

<template>
  <div class="p-6 max-w-3xl mx-auto">
    <h1 class="text-xl font-bold">Settings</h1>
    <p class="text-sm text-surface-500 mb-6">
      The plugin itself. Extensions live in their own window — the <b>Extensions</b> button on the
      ribbon.
    </p>

    <!-- 1. AI ---------------------------------------------------------------------------------
         Two different assistants live behind the one word, and confusing them was easy: the model
         picked here drives the BUILT-IN one (it works inside AnalyseTool's windows, on what the window
         shows it), while the MCP toggle and the C# switch govern an EXTERNAL one (Claude Desktop and
         the like, which bring their own model and call our commands from outside). So: two blocks, each
         saying which one it is about, and each switch sitting with the assistant it applies to. -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-4">
      <h2 class="text-base font-bold mb-1">Artificial intelligence</h2>
      <p class="text-xs text-surface-500 mb-4">
        There are two, and they are not the same thing. The <b>built-in assistant</b> works inside
        AnalyseTool's windows on the model you pick below. An <b>external assistant</b> is an AI you
        already use elsewhere, connected to Revit through AnalyseTool — it brings its own model.
      </p>

      <!-- Built-in -->
      <div class="rounded-lg border border-surface-200 p-3">
        <div class="flex items-center gap-2 mb-1">
          <i class="pi pi-sparkles text-primary-500" />
          <span class="font-semibold text-sm">Built-in assistant</span>
          <Tag value="inside AnalyseTool" severity="secondary" />
        </div>
        <p class="text-xs text-surface-600 mb-3">
          The AI buttons in the parameter windows: analyse a table, propose parameter edits, suggest
          family and type names. It sees only what the window hands it and never touches the model on
          its own — you review and apply. Runs on the model below: a local Ollama model, or a cloud
          provider you add. Shared across all AnalyseTool windows.
        </p>
        <AiModelPicker manage />
      </div>

      <!-- External -->
      <div class="rounded-lg border border-surface-200 p-3 mt-4">
        <div class="flex items-start justify-between gap-3">
          <div>
            <div class="flex items-center gap-2 mb-1">
              <i class="pi pi-link text-primary-500" />
              <span class="font-semibold text-sm">External assistant</span>
              <Tag value="via MCP" severity="secondary" />
              <Tag
                v-if="mcp"
                :value="mcp.running ? `connected · port ${mcp.port}` : 'off'"
                :severity="mcp.running ? 'success' : 'secondary'"
              />
            </div>
            <p class="text-xs text-surface-600">
              Claude Desktop, Cursor or any other client that speaks the Model Context Protocol. It
              works the other way round: it <b>calls AnalyseTool's commands</b> — built-in and from
              your extensions — to read and change the model, in your name, without a window. The
              model picked above does not apply; the client uses its own. Both switches in this block
              concern this assistant only.
            </p>
          </div>
          <ToggleSwitch
            :modelValue="!!mcp?.running"
            :disabled="mcpBusy"
            class="shrink-0 mt-1"
            v-tooltip.left="'Allow external assistants to connect'"
            @update:modelValue="applyMcp(!mcp?.running)"
          />
        </div>

        <div v-if="mcp && !mcp.serverExeExists" class="text-xs text-amber-600 mt-2">
          Server executable not found at <span class="break-all">{{ mcp.serverExePath }}</span> —
          rebuild the plugin so the MCP server ships alongside it.
        </div>
        <div v-if="mcp?.lastError" class="text-xs text-red-600 mt-2">
          Last error: {{ mcp.lastError }}
        </div>

        <!-- The port, the token and the client snippet are setup trivia: needed once, by one
             person, and previously the largest block on the page. -->
        <Panel toggleable collapsed class="mt-3 settings-subpanel">
          <template #header>
            <span class="text-sm">Connection details</span>
          </template>
          <div class="flex items-end gap-3 mb-3">
            <div>
              <label class="block text-xs text-surface-500 mb-1">Port</label>
              <InputText v-model="port" :disabled="mcp?.running || mcpBusy" class="w-32" />
            </div>
            <span class="text-xs text-surface-500 pb-2">
              Turn the switch off to change it.
            </span>
          </div>

          <div v-if="mcp">
            <div class="flex items-center justify-between mb-1">
              <span class="text-sm font-semibold">Claude Desktop config</span>
              <Button
                :label="copied ? 'Copied' : 'Copy'"
                :icon="copied ? 'pi pi-check' : 'pi pi-copy'"
                size="small"
                text
                @click="copyConfig"
              />
            </div>
            <pre
              class="bg-surface-100 text-surface-700 text-xs rounded p-3 overflow-auto whitespace-pre-wrap break-all"
              >{{ clientConfig }}</pre
            >
            <p class="text-xs text-surface-500 mt-1">
              The <code>--token</code> argument authorizes this client against Revit — without it
              every call is refused. If you configured the MCP server before this version, replace
              your old config with the snippet above. Keep it local, like any other machine
              credential.
            </p>
          </div>
        </Panel>

        <!-- The one genuinely dangerous switch in the plugin. It sits INSIDE the external block
             because that is the only assistant it applies to — the built-in one never runs code —
             and it gets its own frame so it can never be skimmed past as another preference. -->
        <div class="mt-3 rounded-lg border border-amber-300 bg-amber-50 p-3">
          <div class="flex items-start justify-between gap-3">
            <div>
              <div class="flex items-center gap-2">
                <i class="pi pi-exclamation-triangle text-amber-600" />
                <span class="font-semibold text-sm">Let the external assistant write and run C# in Revit</span>
                <Tag
                  :value="codeExec ? 'on' : 'off'"
                  :severity="codeExec ? 'warn' : 'secondary'"
                />
              </div>
              <p class="text-xs text-surface-600 mt-1">
                Beyond the ready-made commands: the <code>ExecuteRevitCode</code> tool lets the
                client compile and run arbitrary C# in-process, with full Revit API access to your
                models and machine. Off by default, and hidden from the client's tool list while off.
                Only turn it on for a client you trust.
              </p>
            </div>
            <ToggleSwitch
              :modelValue="codeExec"
              :disabled="codeExecBusy"
              class="shrink-0 mt-1"
              @update:modelValue="setCodeExec($event)"
            />
          </div>
        </div>
      </div>
    </section>

    <!-- 2. About ------------------------------------------------------------------------------->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-4">
      <h2 class="text-base font-bold mb-3">About</h2>
      <div class="grid grid-cols-2 md:grid-cols-3 gap-3 text-sm">
        <div>
          <div class="text-surface-500 text-xs">Revit</div>
          <div>{{ env?.hostRevit ?? "—" }}</div>
        </div>
        <div>
          <div class="text-surface-500 text-xs">SDK version</div>
          <div>{{ env?.hostSdkVersion ?? "—" }}</div>
        </div>
        <div>
          <div class="text-surface-500 text-xs">Plugin version</div>
          <div class="flex items-center gap-2 flex-wrap">
            <span>{{ env?.pluginVersion ?? "—" }}</span>
            <template v-if="updateInfo?.isUpdateAvailable">
              <span
                class="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full text-white"
                :style="{ background: 'var(--p-primary-color)' }"
              >
                <i class="pi pi-arrow-up text-[10px]" />
                v{{ updateInfo.latestVersion }}
              </span>
              <a
                v-if="updateInfo.releaseUrl"
                :href="updateInfo.releaseUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="text-primary-600 underline font-semibold text-xs"
              >
                Download
              </a>
            </template>
          </div>
        </div>
      </div>

      <div class="flex flex-wrap items-center gap-3 mt-4">
        <Button label="What's new" icon="pi pi-book" size="small" text @click="openChangelog" />
        <Button
          label="Extensions folder"
          icon="pi pi-folder-open"
          size="small"
          text
          severity="secondary"
          :disabled="!env?.extensionsRoot"
          v-tooltip.top="env?.extensionsRoot"
          @click="openFolder(env?.extensionsRoot)"
        />
        <a
          :href="ISSUES_URL"
          target="_blank"
          rel="noopener noreferrer"
          class="text-sm text-primary-600 underline inline-flex items-center gap-1"
        >
          <i class="pi pi-github text-xs" />Report a bug
        </a>
      </div>
    </section>

    <!-- 3. For developers: the full command reference. Kept, but one click down — it answers a
         question ("what can I call from AT.invoke") that no one asks while changing a setting. -->
    <Panel toggleable collapsed class="mb-6">
      <template #header>
        <span class="text-sm font-bold">For developers — command reference</span>
      </template>
      <div class="flex items-start justify-between mb-3 gap-3">
        <p class="text-xs text-surface-500">
          Everything callable from a web extension via <code>AT.invoke(name, payload)</code>. The
          <b>MCP</b> tag marks the ones an AI client can see. To RUN one, use the
          <b>Scripts</b> button on the ribbon.
        </p>
        <InputText v-model="commandSearch" placeholder="Search…" class="w-56 shrink-0" size="small" />
      </div>
      <DataTable
        :value="filteredCommands"
        dataKey="name"
        scrollable
        scrollHeight="24rem"
        class="text-sm"
      >
        <Column header="Command">
          <template #body="{ data: row }">
            <div class="font-mono">{{ row.name }}</div>
            <div class="text-xs text-surface-500">
              {{ row.source === "core" ? "built-in" : row.source }}
            </div>
          </template>
        </Column>
        <Column header="Description">
          <template #body="{ data: row }">
            <div>{{ row.description || "—" }}</div>
          </template>
        </Column>
        <Column header="Payload">
          <template #body="{ data: row }">
            <span class="font-mono text-xs break-all">{{ payloadSummary(row.inputSchema) }}</span>
          </template>
        </Column>
        <Column header="" class="whitespace-nowrap">
          <template #body="{ data: row }">
            <Tag v-if="row.readOnly" value="read-only" severity="info" class="mr-1" />
            <Tag v-if="row.destructive" value="destructive" severity="danger" class="mr-1" />
            <Tag v-if="row.exposedToMcp" value="MCP" severity="success" />
          </template>
        </Column>
        <template #empty>
          <div class="text-surface-500 p-3">No commands match.</div>
        </template>
      </DataTable>
    </Panel>

    <!-- Changelog (CHANGELOG.md shipped with the plugin, rendered as markdown) -->
    <Dialog
      v-model:visible="changelogVisible"
      modal
      dismissableMask
      header="What's new"
      :style="{ width: 'min(44rem, 95vw)' }"
    >
      <div v-if="changelogError" class="text-sm text-red-600">{{ changelogError }}</div>
      <div v-else-if="!changelogHtml" class="text-surface-500 text-sm p-4 text-center">
        <i class="pi pi-spin pi-spinner mr-2" />Loading…
      </div>
      <div v-else class="changelog-body max-h-[65vh] overflow-y-auto pr-2" v-html="changelogHtml" />
    </Dialog>
  </div>
</template>

<style scoped>
/* A nested panel inside a card should read as a fold, not as a second card. */
.settings-subpanel :deep(.p-panel-header) {
  background: transparent;
  padding: 0.5rem 0.75rem;
}
.settings-subpanel :deep(.p-panel-content) {
  padding: 0.75rem;
}

/* Minimal markdown styling for the changelog dialog (marked outputs plain h2/ul/li/p). */
.changelog-body :deep(h2) {
  font-size: 1rem;
  font-weight: 700;
  margin: 1rem 0 0.5rem;
  padding-bottom: 0.25rem;
  border-bottom: 1px solid var(--p-surface-200);
}
.changelog-body :deep(h2:first-child) {
  margin-top: 0;
}
.changelog-body :deep(h1) {
  display: none; /* the dialog header already says what this is */
}
.changelog-body :deep(ul) {
  list-style: disc;
  padding-left: 1.25rem;
  margin: 0.25rem 0 0.75rem;
}
.changelog-body :deep(ul ul) {
  list-style: circle;
  margin: 0.125rem 0;
}
.changelog-body :deep(li) {
  font-size: 0.875rem;
  margin: 0.125rem 0;
}
.changelog-body :deep(p) {
  font-size: 0.875rem;
  margin: 0.375rem 0;
}
.changelog-body :deep(code) {
  background: var(--p-surface-100);
  border-radius: 0.25rem;
  padding: 0 0.25rem;
  font-size: 0.8em;
}
</style>
