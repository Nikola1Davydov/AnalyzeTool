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

// --- Organization: what the policy does to this seat (read-only; the file is IT's or the coordinator's).
interface PolicyStatus {
  present: boolean;
  origin: "absent" | "loaded" | "invalid";
  path: string;
  problems: string[];
  organization?: { name?: string | null; contact?: string | null } | null;
  minimumVersion?: string | null;
  pluginVersion: string;
  policyUrl?: string | null;
  locked: string[];
  settings: {
    codeExecution: { enabled: boolean; origin: string; locked: boolean };
    extensionRoots: { fromPolicy: string[]; locked: boolean };
    allowedFeeds?: string[] | null;
    allowInstallFromRepository: boolean;
    mcpEnabled: { value?: boolean | null; locked: boolean };
  };
  catalog: { source?: string | null };
  required: {
    declared: { id: string; source?: string | null; pinned: boolean }[];
    lastRun?: string | null;
    outcomes: { id: string; state: string; version?: string | null; detail?: string | null }[];
  };
  backgroundApply: { running: boolean; lastCompleted?: string | null; error?: string | null };
  belowMinimumVersion?: boolean;
  update?: { downloadUrl?: string | null; pinned: boolean } | null;
  /** The organization layer: joined by the user, or set by the machine pointer. */
  membership?: {
    policyUrl: string;
    enforced: boolean;
    organization?: string | null;
    fetchedAt?: string | null;
    applied: boolean;
    signed: boolean;
    signingKeyFingerprint?: string | null;
    lastRefreshProblem?: string | null;
  } | null;
  layers?: { machine: boolean; organization: boolean };
  sources?: {
    declared?: { name: string; kind: string; sharepoint?: string | null; syncUrl?: string | null; resolved?: string | null }[] | null;
    unresolved: Record<string, string>;
    overrides: Record<string, string>;
  } | null;
  telemetry?: {
    enabled: boolean;
    sink?: string | null;
    identity: string;
    events: string[];
    pending: number;
    lastError?: string | null;
  } | null;
  logging?: string | null;
}
const policy = ref<PolicyStatus | null>(null);
const policyBusy = ref(false);
const selfUpdateBusy = ref(false);

/** Downloads the organization's installer, verifies its pin and schedules it for when Revit exits. */
async function startSelfUpdate() {
  selfUpdateBusy.value = true;
  try {
    const res = await invoke<{ scheduled: boolean; message: string }>("StartSelfUpdate", { consent: true });
    notifications.info(res?.message ?? "Update scheduled. Close Revit to finish it.");
  } catch (e) {
    notifications.error(`Could not schedule the update: ${errorText(e)}`);
  } finally {
    selfUpdateBusy.value = false;
  }
}

/** Not joined and no machine pointer: the seat may join an organization on its own. */
const canJoin = computed(() => !!policy.value && !policy.value.membership);

async function loadPolicy() {
  try {
    policy.value = await invoke<PolicyStatus>("GetPolicyStatus");
  } catch (e) {
    console.error("Failed to load the organization policy status", e);
  }
}

// --- Join / leave an organization (design §8: preview first, consent, then apply). ------------
interface PolicyPreview {
  reference: string;
  location?: string | null;
  how?: string | null;
  organization: { name?: string | null; contact?: string | null };
  signature: "nokey" | "verified" | string;
  signingKeyFingerprint?: string | null;
  locks: string[];
  codeExecution?: boolean | null;
  mcpEnabled?: boolean | null;
  extensionRoots: string[];
  catalogUrl?: string | null;
  allowedFeeds?: string[] | null;
  allowInstallFromRepository?: boolean | null;
  requiredExtensions: { id: string; source?: string | null; pinned: boolean }[];
  sources: string[];
  minimumVersion?: string | null;
  aiSection?: string | null;
  telemetrySection?: string | null;
  loggingSection?: string | null;
  alreadyJoined: boolean;
  machinePolicyPresent: boolean;
}
interface DiscoverTried {
  reference: string;
  how: string;
  problem?: string | null;
}
interface DiscoverResult {
  found: boolean;
  tried: DiscoverTried[];
  preview: PolicyPreview | null;
}

const joinInput = ref("");
const joinKey = ref("");
const joinKeyVisible = ref(false);
const joinBusy = ref(false);
const joinTried = ref<DiscoverTried[]>([]); // shown when nothing was found
const joinSearched = ref(false);

const preview = ref<PolicyPreview | null>(null);
const previewVisible = ref(false);
const previewKey = ref(""); // the signing key the preview was fetched with; travels into Join

// First-start banner: discovered in the background, dismissed for this window only.
const bannerPreview = ref<PolicyPreview | null>(null);
const bannerDismissed = ref(false);

async function discover(input: string, signingKey: string): Promise<DiscoverResult> {
  return await invoke<DiscoverResult>("DiscoverOrganizationPolicy", {
    input: input.trim() || undefined,
    signingKey: signingKey.trim() || undefined,
  });
}

async function findOrganization() {
  joinBusy.value = true;
  joinSearched.value = false;
  joinTried.value = [];
  try {
    const res = await discover(joinInput.value, joinKey.value);
    joinSearched.value = !res?.found; // the "tried" list is only interesting when nothing came of it
    joinTried.value = res?.tried ?? [];
    if (res?.found && res.preview) {
      openPreview(res.preview, joinKey.value);
    }
  } catch (e) {
    notifications.error(`Could not look for an organization policy: ${errorText(e)}`);
  } finally {
    joinBusy.value = false;
  }
}

function openPreview(p: PolicyPreview, signingKey: string) {
  preview.value = p;
  previewKey.value = signingKey;
  previewVisible.value = true;
}

async function joinOrganization() {
  const p = preview.value;
  if (!p) return;
  joinBusy.value = true;
  try {
    const res = await invoke<{ joined: boolean; organization?: string | null; signature: string }>(
      "JoinOrganization",
      { reference: p.reference, signingKey: previewKey.value.trim() || undefined, consent: true },
    );
    previewVisible.value = false;
    bannerPreview.value = null;
    await Promise.all([loadPolicy(), loadCodeExec(), loadMcp()]);
    notifications.success(
      `Joined ${res?.organization ?? p.organization?.name ?? "the organization"}. Required extensions install in the background.`,
    );
  } catch (e) {
    notifications.error(`Could not join: ${errorText(e)}`);
  } finally {
    joinBusy.value = false;
  }
}

/** Parse a raw policy section (JSON string) without ever throwing — the preview must not break on it. */
function parseSection(json: string | null | undefined): Record<string, any> | null {
  if (!json) return null;
  try {
    const v = JSON.parse(json);
    return v && typeof v === "object" ? v : null;
  } catch {
    return null;
  }
}

/** A sink value is a string or an object with a url/path; anything else is shown raw. */
function sinkText(sink: any): string {
  if (sink == null) return "";
  if (typeof sink === "string") return sink;
  if (typeof sink === "object") return sink.url ?? sink.path ?? sink.baseUrl ?? JSON.stringify(sink);
  return String(sink);
}

/** "Sends AI requests to": the provider endpoints, or the raw section when the shape is unknown. */
const previewAiTargets = computed<string[]>(() => {
  const raw = preview.value?.aiSection;
  const s = parseSection(raw);
  if (!s) return raw ? [raw] : [];
  const providers = Array.isArray(s.providers) ? s.providers : [];
  const targets = providers
    .map((p: any) => (p && typeof p === "object" ? p.baseUrl ?? p.url ?? p.name ?? p.type : null))
    .filter((t: unknown): t is string => typeof t === "string" && t.length > 0);
  return targets.length ? targets : [raw as string];
});

const previewTelemetryTarget = computed<string | null>(() => {
  const raw = preview.value?.telemetrySection;
  if (!raw) return null;
  const s = parseSection(raw);
  const sink = sinkText(s?.sink);
  const events = Array.isArray(s?.events) ? s!.events.join(", ") : "";
  return sink ? `${sink}${events ? ` (${events})` : ""}` : raw;
});

const previewLoggingTarget = computed<string | null>(() => {
  const raw = preview.value?.loggingSection;
  if (!raw) return null;
  const s = parseSection(raw);
  return sinkText(s?.sink) || raw;
});

// Leave: releases the locks, then offers — never performs — the removal of what the policy installed.
const leaveDialogVisible = ref(false);
const leaveBusy = ref(false);
const removableDialogVisible = ref(false);
const removable = ref<string[]>([]);
const removeSelected = ref<string[]>([]);
const removeBusy = ref(false);

async function leaveOrganization() {
  leaveBusy.value = true;
  try {
    const res = await invoke<{ left: boolean; organization?: string | null; removableExtensions?: string[]; reason?: string }>(
      "LeaveOrganization",
    );
    leaveDialogVisible.value = false;
    await Promise.all([loadPolicy(), loadCodeExec(), loadMcp()]);
    if (!res?.left) {
      notifications.info(res?.reason ?? "This seat is not joined to an organization.");
      return;
    }
    notifications.info(`Left ${res.organization ?? "the organization"}. Your own settings apply again.`);
    removable.value = res.removableExtensions ?? [];
    removeSelected.value = [...removable.value];
    if (removable.value.length) removableDialogVisible.value = true;
  } catch (e) {
    notifications.error(`Could not leave: ${errorText(e)}`);
  } finally {
    leaveBusy.value = false;
  }
}

async function removeSelectedExtensions() {
  removeBusy.value = true;
  const failed: string[] = [];
  for (const id of removeSelected.value) {
    try {
      await invoke("RemoveExtension", { id });
    } catch (e) {
      failed.push(`${id}: ${errorText(e)}`);
    }
  }
  removeBusy.value = false;
  removableDialogVisible.value = false;
  if (failed.length) notifications.error(`Could not uninstall: ${failed.join("; ")}`);
  else if (removeSelected.value.length) notifications.success(`Uninstalled ${removeSelected.value.length} extension(s).`);
}

// --- Sources the policy names but this computer cannot resolve (SharePoint libraries, §9). -----
const unresolvedSources = computed(() => {
  const p = policy.value;
  if (!p?.sources) return [];
  const declared = p.sources.declared ?? [];
  return Object.entries(p.sources.unresolved ?? {}).map(([name, reason]) => ({
    name,
    reason,
    syncUrl: declared.find((d) => d.name === name)?.syncUrl ?? null,
  }));
});
const sourceBusy = ref<string | null>(null);

async function pickSourceFolder(name: string) {
  sourceBusy.value = name;
  try {
    const picked = await invoke<{ path: string | null }>("BrowseForFolder");
    if (!picked?.path) return;
    await invoke("SetSourceLocation", { name, path: picked.path });
    await loadPolicy();
    notifications.success(`"${name}" now points to ${picked.path}. Required extensions refresh in the background.`);
  } catch (e) {
    notifications.error(`Could not set the folder for "${name}": ${errorText(e)}`);
  } finally {
    sourceBusy.value = null;
  }
}

/** odopen:// and other custom schemes hand off to the registered app; http(s) opens like any link. */
function openExternal(url: string) {
  if (/^https?:/i.test(url)) window.open(url, "_blank", "noopener");
  else window.location.href = url;
}

// --- Telemetry: what leaves this seat, on demand (§10 "Show recent events"). --------------------
const telemetryVisible = ref(false);
const telemetryEvents = ref<string[]>([]);
const telemetryError = ref<string | null>(null);
const telemetryBusy = ref(false);

async function showTelemetry() {
  telemetryVisible.value = true;
  telemetryBusy.value = true;
  telemetryError.value = null;
  try {
    const res = await invoke<{ enabled: boolean; events: string[] }>("GetTelemetryRecent");
    telemetryEvents.value = res?.events ?? [];
  } catch (e) {
    telemetryError.value = errorText(e);
  } finally {
    telemetryBusy.value = false;
  }
}

async function reloadPolicy() {
  policyBusy.value = true;
  try {
    await invoke("ReloadPolicy");
    await Promise.all([loadPolicy(), loadCodeExec(), loadMcp()]);
    notifications.info("Policy re-read. Catalog and required extensions refresh in the background.");
  } catch (e) {
    notifications.error(`Could not reload the policy: ${errorText(e)}`);
  } finally {
    policyBusy.value = false;
  }
}

/** One line per required extension for the panel: "id — state (version)". */
function outcomeSeverity(state: string): "success" | "info" | "warn" | "danger" | "secondary" {
  switch (state) {
    case "installed":
    case "updated":
      return "success";
    case "present":
      return "secondary";
    case "blocked":
    case "failed":
      return "danger";
    default:
      return "warn";
  }
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
const codeExecManaged = ref(false); // locked by the organization policy: read-only

async function loadCodeExec() {
  try {
    const res = await invoke<{ enabled: boolean; managed?: boolean }>("GetCodeExecutionStatus");
    codeExec.value = !!res?.enabled;
    codeExecManaged.value = !!res?.managed;
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
  managed?: boolean; // locked by the organization policy: read-only
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

onMounted(async () => {
  loadEnvironment();
  await loadPolicy();
  // First start on a managed network: look for a published policy quietly. Found = a banner, never a
  // dialog; the person decides when to read the preview.
  if (canJoin.value) {
    discover("", "")
      .then((res) => {
        if (res?.found && res.preview) bannerPreview.value = res.preview;
      })
      .catch((e) => console.error("Background policy discovery failed", e));
  }
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

    <!-- First-start banner: the network publishes a policy and this seat is not joined. Non-modal;
         the preview dialog is one click away and the dismissal lasts for this window only. -->
    <div
      v-if="bannerPreview && !bannerDismissed && canJoin"
      class="mb-4 rounded-xl border border-primary-300 bg-primary-50 px-4 py-3 flex items-center gap-3 flex-wrap"
    >
      <i class="pi pi-building text-primary-600" />
      <span class="text-sm flex-1">
        <b>{{ bannerPreview.organization?.name ?? "Your organization" }}</b> publishes AnalyseTool
        settings. Join?
      </span>
      <Button label="See what changes" size="small" @click="openPreview(bannerPreview, '')" />
      <Button icon="pi pi-times" size="small" text severity="secondary" v-tooltip.top="'Not now'" @click="bannerDismissed = true" />
    </div>

    <!-- Below the organization's minimum version: the policy may not apply fully until updated. -->
    <div
      v-if="policy?.belowMinimumVersion"
      class="mb-4 rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 flex items-center gap-3 flex-wrap text-sm text-amber-800"
    >
      <i class="pi pi-exclamation-triangle" />
      <span class="flex-1">
        Your organization requires version <b>{{ policy?.minimumVersion }}</b> (you have
        {{ policy?.pluginVersion }}).
      </span>
      <a
        v-if="policy?.update?.downloadUrl"
        :href="policy.update.downloadUrl"
        target="_blank"
        rel="noopener noreferrer"
        class="underline font-semibold inline-flex items-center gap-1"
      >
        <i class="pi pi-download text-xs" />Download
      </a>
      <!-- Per-user installs only, and only with a sha256 pin: the backend refuses everything else. -->
      <Button
        v-if="policy?.update?.downloadUrl && policy?.update?.pinned"
        label="Install when Revit closes"
        icon="pi pi-clock"
        size="small"
        severity="warn"
        :loading="selfUpdateBusy"
        @click="startSelfUpdate"
      />
    </div>

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
            :disabled="mcpBusy || !!mcp?.managed"
            class="shrink-0 mt-1"
            v-tooltip.left="mcp?.managed ? 'Managed by your organization' : 'Allow external assistants to connect'"
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
                <Tag
                  v-if="codeExecManaged"
                  value="managed by your organization"
                  severity="secondary"
                  icon="pi pi-lock"
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
              :disabled="codeExecBusy || codeExecManaged"
              class="shrink-0 mt-1"
              @update:modelValue="setCodeExec($event)"
            />
          </div>
        </div>
      </div>
    </section>

    <!-- 2. Organization ---------------------------------------------------------------------------
         Read-only: the policy file belongs to IT (machine layer) or the BIM coordinator. This panel is
         the one place a broken or surprising policy becomes visible without reading the log. -->
    <section
      v-if="policy && (policy.present || policy.origin === 'invalid' || canJoin)"
      class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-4"
    >
      <div class="flex items-start justify-between gap-3 mb-3">
        <div>
          <h2 class="text-base font-bold flex items-center gap-2">
            <i class="pi pi-building" />
            Organization
            <Tag
              v-if="policy.membership?.organization || policy.organization?.name"
              :value="`Managed by ${policy.membership?.organization ?? policy.organization?.name}`"
              severity="info"
            />
          </h2>
          <p v-if="policy.present" class="text-xs text-surface-500 mt-1">
            Settings below marked with a lock come from
            <span class="font-mono break-all">{{ policy.membership?.policyUrl ?? policy.path }}</span
            ><template v-if="policy.organization?.contact">
              — questions go to <b>{{ policy.organization.contact }}</b></template
            >.
          </p>
          <p v-else class="text-xs text-surface-500 mt-1">
            Not managed. Your own settings apply.
          </p>
        </div>
        <Button
          v-if="policy.present || policy.origin === 'invalid'"
          icon="pi pi-refresh"
          size="small"
          text
          severity="secondary"
          :loading="policyBusy"
          v-tooltip.left="'Re-read the policy file'"
          @click="reloadPolicy"
        />
      </div>

      <!-- Not joined: the compact join card. Empty input = automatic discovery (domain, OneDrive). -->
      <div v-if="canJoin" class="rounded-lg border border-surface-200 p-3 mb-3">
        <div class="font-semibold text-sm mb-1">Join your organization</div>
        <p class="text-xs text-surface-600 mb-2">
          Your BIM coordinator or IT may publish AnalyseTool settings — approved sources, required
          extensions, locks. Enter the policy URL, your company domain or a folder, or leave the field
          empty to look automatically. Nothing changes before you have seen a preview and confirmed.
        </p>
        <div class="flex items-center gap-2 flex-wrap">
          <InputText
            v-model="joinInput"
            placeholder="https://…/policy.json, company.com, a folder — or empty"
            class="flex-1 min-w-[16rem]"
            size="small"
            :disabled="joinBusy"
            @keyup.enter="findOrganization"
          />
          <Button label="Find" icon="pi pi-search" size="small" :loading="joinBusy" @click="findOrganization" />
          <Button
            :label="joinKeyVisible ? 'Hide signing key' : 'Signing key…'"
            size="small"
            text
            severity="secondary"
            @click="joinKeyVisible = !joinKeyVisible"
          />
        </div>
        <div v-if="joinKeyVisible" class="mt-2">
          <label class="block text-xs text-surface-500 mb-1">
            Signing key (optional, base64 — handed out by the organization; the policy must then be signed with it)
          </label>
          <InputText v-model="joinKey" class="w-full font-mono" size="small" :disabled="joinBusy" />
        </div>
        <div v-if="joinSearched" class="mt-2 text-xs">
          <div class="text-surface-600 mb-1">No organization policy found. Tried:</div>
          <div v-for="t in joinTried" :key="t.reference" class="text-surface-500 break-all">
            <span class="font-mono">{{ t.reference }}</span>
            <span class="text-surface-400"> ({{ t.how }})</span>
            <span v-if="t.problem"> — {{ t.problem }}</span>
          </div>
          <div v-if="!joinTried.length" class="text-surface-500">nothing — no domain or synced library on this computer</div>
        </div>
      </div>

      <!-- Joined: where the policy comes from, how fresh it is, and the way out. -->
      <div v-if="policy.membership" class="rounded-lg border border-surface-200 p-3 mb-3 text-xs">
        <div class="flex items-start justify-between gap-3 flex-wrap">
          <div class="flex flex-col gap-1">
            <div>
              Managed by <b>{{ policy.membership.organization ?? "your organization" }}</b>
              <Tag v-if="policy.membership.enforced" value="set by an administrator" severity="secondary" icon="pi pi-lock" class="ml-1" />
              <Tag v-if="!policy.membership.applied" value="not applied" severity="warn" class="ml-1" />
            </div>
            <div class="text-surface-500">
              policy: <span class="font-mono break-all">{{ policy.membership.policyUrl }}</span>
            </div>
            <div class="text-surface-500">
              <template v-if="policy.membership.fetchedAt">
                last fetched {{ new Date(policy.membership.fetchedAt).toLocaleString() }} ·
              </template>
              <template v-if="policy.membership.signed">
                signed · key <span class="font-mono">{{ policy.membership.signingKeyFingerprint }}</span>
              </template>
              <template v-else>not signed (trusted by location)</template>
            </div>
            <div v-if="policy.membership.lastRefreshProblem" class="text-amber-700">
              <i class="pi pi-exclamation-triangle mr-1" />{{ policy.membership.lastRefreshProblem }}
            </div>
          </div>
          <div class="shrink-0">
            <Button
              v-if="!policy.membership.enforced"
              label="Leave organization"
              icon="pi pi-sign-out"
              size="small"
              text
              severity="danger"
              @click="leaveDialogVisible = true"
            />
            <span v-else class="text-surface-500">
              This computer's organization is set by an administrator and cannot be changed here.
            </span>
          </div>
        </div>
      </div>

      <!-- Sources the policy names but this seat cannot find (a SharePoint library not synced yet). -->
      <div
        v-for="s in unresolvedSources"
        :key="s.name"
        class="mb-2 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
      >
        <div class="flex items-center gap-2 flex-wrap">
          <span class="flex-1">
            <i class="pi pi-question-circle mr-1" />Where is <b>{{ s.name }}</b> on this computer?
            <span class="text-amber-700">{{ s.reason }}</span>
          </span>
          <Button
            v-if="s.syncUrl"
            label="Connect the library"
            icon="pi pi-cloud-download"
            size="small"
            text
            v-tooltip.top="'Opens OneDrive to sync the library; the next start finds it automatically'"
            @click="openExternal(s.syncUrl)"
          />
          <Button
            label="Pick folder…"
            icon="pi pi-folder-open"
            size="small"
            severity="secondary"
            :loading="sourceBusy === s.name"
            @click="pickSourceFolder(s.name)"
          />
        </div>
      </div>

      <!-- Telemetry: never silent. One line saying where and what, and the exact events on demand. -->
      <div
        v-if="policy.telemetry?.enabled"
        class="mb-2 rounded-lg border border-surface-200 px-3 py-2 text-xs flex items-center gap-2 flex-wrap"
      >
        <i class="pi pi-send text-surface-500" />
        <span class="flex-1">
          Sends telemetry to <span class="font-mono break-all">{{ policy.telemetry.sink }}</span>
          ({{ policy.telemetry.events.join(", ") || "inventory" }}; identity: {{ policy.telemetry.identity }})
          <span v-if="policy.telemetry.pending" class="text-surface-500"> · {{ policy.telemetry.pending }} pending</span>
          <span v-if="policy.telemetry.lastError" class="text-amber-700"> · {{ policy.telemetry.lastError }}</span>
        </span>
        <Button label="Show recent events" size="small" text @click="showTelemetry" />
      </div>

      <div
        v-for="problem in policy.problems"
        :key="problem"
        class="mb-2 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
      >
        <i class="pi pi-exclamation-triangle mr-1" />{{ problem }}
      </div>
      <div
        v-if="policy.backgroundApply.error"
        class="mb-2 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-xs text-red-800"
      >
        <i class="pi pi-times-circle mr-1" />{{ policy.backgroundApply.error }}
      </div>

      <div v-if="policy.present" class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
        <div>
          <div class="text-surface-500 text-xs mb-1">Locked settings</div>
          <div v-if="policy.locked.length" class="flex flex-wrap gap-1">
            <Tag v-for="l in policy.locked" :key="l" :value="l" severity="secondary" icon="pi pi-lock" />
          </div>
          <div v-else class="text-surface-500">none — the policy only sets defaults</div>
        </div>
        <div>
          <div class="text-surface-500 text-xs mb-1">Extension sources</div>
          <div v-if="policy.settings.allowedFeeds" class="text-xs">
            approved: <span class="font-mono break-all">{{ policy.settings.allowedFeeds.join(", ") }}</span>
          </div>
          <div v-else class="text-xs text-surface-500">any source</div>
          <div v-if="!policy.settings.allowInstallFromRepository" class="text-xs">
            "Install from repository…" is switched off
          </div>
          <div v-if="policy.catalog.source" class="text-xs">
            catalog: <span class="font-mono break-all">{{ policy.catalog.source }}</span>
          </div>
          <div v-for="r in policy.settings.extensionRoots.fromPolicy" :key="r" class="text-xs font-mono break-all">
            folder: {{ r }}
          </div>
        </div>
        <div v-if="policy.required.declared.length" class="md:col-span-2">
          <div class="text-surface-500 text-xs mb-1">
            Required extensions
            <span v-if="policy.backgroundApply.running"> — checking…</span>
            <span v-else-if="policy.required.lastRun"> — last checked {{ new Date(policy.required.lastRun).toLocaleString() }}</span>
          </div>
          <div class="flex flex-col gap-1">
            <div
              v-for="req in policy.required.declared"
              :key="req.id"
              class="flex items-center gap-2 flex-wrap text-xs"
            >
              <span class="font-mono">{{ req.id }}</span>
              <template v-if="policy.required.outcomes.find((o) => o.id === req.id)">
                <Tag
                  :value="policy.required.outcomes.find((o) => o.id === req.id)!.state"
                  :severity="outcomeSeverity(policy.required.outcomes.find((o) => o.id === req.id)!.state)"
                />
                <span v-if="policy.required.outcomes.find((o) => o.id === req.id)!.version" class="text-surface-500">
                  {{ policy.required.outcomes.find((o) => o.id === req.id)!.version }}
                </span>
                <span
                  v-if="policy.required.outcomes.find((o) => o.id === req.id)!.detail"
                  class="text-surface-500 break-all"
                >
                  {{ policy.required.outcomes.find((o) => o.id === req.id)!.detail }}
                </span>
              </template>
              <Tag v-else value="pending" severity="secondary" />
              <Tag v-if="req.pinned" value="sha256" severity="secondary" v-tooltip.top="'Package hash pinned by the policy'" />
            </div>
          </div>
        </div>
        <div v-if="policy.minimumVersion" class="md:col-span-2 text-xs">
          Minimum plugin version required by the organization: <b>{{ policy.minimumVersion }}</b>
          (this seat: {{ policy.pluginVersion }})
        </div>
      </div>
    </section>

    <!-- 3. About ------------------------------------------------------------------------------->
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

    <!-- Join preview: every consequence named, then consent. Nothing is applied before "Join". -->
    <Dialog
      v-model:visible="previewVisible"
      modal
      header="Join organization"
      :style="{ width: 'min(40rem, 95vw)' }"
    >
      <div v-if="preview" class="text-sm flex flex-col gap-3 max-h-[65vh] overflow-y-auto pr-1">
        <div>
          <div class="font-semibold">{{ preview.organization?.name ?? "Unnamed organization" }}</div>
          <div v-if="preview.organization?.contact" class="text-xs text-surface-500">
            Contact: {{ preview.organization.contact }}
          </div>
          <div class="text-xs text-surface-500 break-all">
            Policy: <span class="font-mono">{{ preview.location ?? preview.reference }}</span>
            <span v-if="preview.how"> ({{ preview.how }})</span>
          </div>
          <div class="text-xs mt-1 flex items-center gap-2 flex-wrap">
            <Tag
              :value="preview.signature === 'verified' ? 'signature verified' : 'not signed / no key'"
              :severity="preview.signature === 'verified' ? 'success' : 'warn'"
              :icon="preview.signature === 'verified' ? 'pi pi-shield' : 'pi pi-exclamation-triangle'"
            />
            <span v-if="preview.signingKeyFingerprint" class="font-mono text-surface-500">
              key {{ preview.signingKeyFingerprint }}
            </span>
          </div>
          <div v-if="preview.alreadyJoined" class="text-xs text-amber-700 mt-1">
            This seat is already joined to an organization; joining replaces that membership.
          </div>
          <div v-if="preview.machinePolicyPresent" class="text-xs text-surface-500 mt-1">
            A machine policy set by IT is present; its locked values stay in force.
          </div>
        </div>

        <div class="text-xs text-surface-600">After joining, this policy will:</div>
        <ul class="text-xs flex flex-col gap-1.5 pl-1">
          <li>
            <b>Lock:</b>
            <template v-if="preview.locks?.length">
              <Tag v-for="l in preview.locks" :key="l" :value="l" severity="secondary" icon="pi pi-lock" class="ml-1" />
            </template>
            <span v-else class="text-surface-500">nothing — the policy only sets defaults</span>
          </li>
          <li v-if="preview.codeExecution != null">
            <b>C# code execution by external assistants:</b> {{ preview.codeExecution ? "on" : "off" }}
          </li>
          <li v-if="preview.mcpEnabled != null">
            <b>MCP server:</b> {{ preview.mcpEnabled ? "on" : "off" }}
          </li>
          <li v-if="preview.extensionRoots?.length">
            <b>Extension folders:</b>
            <div v-for="r in preview.extensionRoots" :key="r" class="font-mono break-all pl-3">{{ r }}</div>
          </li>
          <li v-if="preview.catalogUrl">
            <b>Catalog:</b> <span class="font-mono break-all">{{ preview.catalogUrl }}</span>
          </li>
          <li v-if="preview.allowedFeeds">
            <b>Approved sources:</b>
            <span v-if="preview.allowedFeeds.length" class="font-mono break-all">{{ preview.allowedFeeds.join(", ") }}</span>
            <span v-else class="text-surface-500">none — only the catalog and required extensions</span>
          </li>
          <li v-if="preview.allowInstallFromRepository === false">
            <b>"Install from repository…"</b> switched off
          </li>
          <li v-if="preview.requiredExtensions?.length">
            <b>Required extensions</b> (installed, kept up to date, cannot be removed):
            <div v-for="r in preview.requiredExtensions" :key="r.id" class="pl-3 flex items-center gap-2 flex-wrap">
              <span class="font-mono">{{ r.id }}</span>
              <span v-if="r.source" class="text-surface-500 break-all">{{ r.source }}</span>
              <Tag v-if="r.pinned" value="sha256" severity="secondary" v-tooltip.top="'Package hash pinned by the policy'" />
            </div>
          </li>
          <li v-if="preview.sources?.length">
            <b>Named sources:</b> <span class="font-mono">{{ preview.sources.join(", ") }}</span>
          </li>
          <li v-if="preview.minimumVersion">
            <b>Minimum plugin version:</b> {{ preview.minimumVersion }}
            <span v-if="policy?.pluginVersion" class="text-surface-500">(you have {{ policy.pluginVersion }})</span>
          </li>
          <li v-if="previewAiTargets.length">
            <b>Sends AI requests to:</b>
            <div v-for="t in previewAiTargets" :key="t" class="font-mono break-all pl-3">{{ t }}</div>
          </li>
          <li v-if="previewTelemetryTarget">
            <b>Sends telemetry to:</b> <span class="font-mono break-all">{{ previewTelemetryTarget }}</span>
          </li>
          <li v-if="previewLoggingTarget">
            <b>Writes logs to:</b> <span class="font-mono break-all">{{ previewLoggingTarget }}</span>
          </li>
        </ul>
        <p class="text-xs text-surface-500">
          You can leave at any time from this page; your own settings then apply again.
        </p>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" :disabled="joinBusy" @click="previewVisible = false" />
        <Button label="Join" icon="pi pi-check" :loading="joinBusy" @click="joinOrganization" />
      </template>
    </Dialog>

    <!-- Leave: confirm, then offer to uninstall what the policy brought in. -->
    <Dialog v-model:visible="leaveDialogVisible" modal header="Leave organization" class="w-[28rem]">
      <div class="text-sm flex flex-col gap-3">
        <p>
          Leave <b>{{ policy?.membership?.organization ?? "the organization" }}</b>? The locks are
          released and your own settings apply again. Extensions the policy installed stay until you
          remove them — you will be asked next.
        </p>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" :disabled="leaveBusy" @click="leaveDialogVisible = false" />
        <Button label="Leave" severity="danger" :loading="leaveBusy" @click="leaveOrganization" />
      </template>
    </Dialog>

    <Dialog v-model:visible="removableDialogVisible" modal header="Extensions the organization required" class="w-[28rem]">
      <div class="text-sm flex flex-col gap-3">
        <p>These were installed because the policy required them. Uninstall the ones you no longer need:</p>
        <div class="flex flex-col gap-2">
          <label v-for="id in removable" :key="id" class="flex items-center gap-2 text-sm">
            <Checkbox v-model="removeSelected" :value="id" :disabled="removeBusy" />
            <span class="font-mono">{{ id }}</span>
          </label>
        </div>
      </div>
      <template #footer>
        <Button label="Keep all" text severity="secondary" :disabled="removeBusy" @click="removableDialogVisible = false" />
        <Button
          :label="`Uninstall ${removeSelected.length}`"
          severity="danger"
          :disabled="!removeSelected.length"
          :loading="removeBusy"
          @click="removeSelectedExtensions"
        />
      </template>
    </Dialog>

    <!-- Telemetry: the last events exactly as they were sent. -->
    <Dialog v-model:visible="telemetryVisible" modal dismissableMask header="Recent telemetry events" :style="{ width: 'min(44rem, 95vw)' }">
      <div v-if="telemetryError" class="text-sm text-red-600">{{ telemetryError }}</div>
      <div v-else-if="telemetryBusy" class="text-surface-500 text-sm p-4 text-center">
        <i class="pi pi-spin pi-spinner mr-2" />Loading…
      </div>
      <div v-else-if="!telemetryEvents.length" class="text-surface-500 text-sm p-4 text-center">
        Nothing sent yet.
      </div>
      <pre
        v-else
        class="bg-surface-100 text-surface-700 text-xs rounded p-3 max-h-[60vh] overflow-auto whitespace-pre-wrap break-all font-mono"
        >{{ telemetryEvents.join("\n") }}</pre
      >
    </Dialog>

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
