<script setup lang="ts">
/**
 * The create-extension form — one component, two homes: the "New" ribbon button's own window
 * (NewExtensionView) and a drawer inside the extension manager (CreateExtensionTemplateDrawer).
 *
 * It asks for what the manifest can hold and a person can know BEFORE the extension exists: the
 * identity, who made it, and the ribbon button that opens it. It shows no file previews — plugin.json
 * and index.html are written to disk, where an editor renders them better than a <pre> ever did, and
 * the previews were two thirds of the old drawer's height.
 *
 * ONE flavour, not three. The form used to ask "page, C# commands, or both?" — a choice a beginner
 * cannot make with any confidence, and a question an agent never needed asked: it has the authoring
 * guide and deletes or adds what the task turns out to need. So every template is page + C#, and
 * whoever knows better removes a file. The host still accepts the other kinds for callers that do.
 */
import { computed, onMounted, ref } from "vue";
import ToggleSwitch from "primevue/toggleswitch";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

type TemplateKind = "UiOnly" | "Csharp" | "Combo";

/** Mirrors ExtensionTemplateManifest on the host; nulls are omitted from the written plugin.json. */
interface ExtensionTemplateManifest {
  id: string;
  version: string;
  description?: string;
  publisher?: string;
  website?: string;
  supportUrl?: string;
  updateFeed?: string;
  entryAssembly?: string;
  ui?: {
    entryHtml: string;
    tab: string;
    panel: string;
    dockable?: boolean;
    button: { name: string; tooltip: string };
  };
}

interface CreateExtensionTemplatePayload {
  folderName: string;
  kind: TemplateKind;
  pluginJson: ExtensionTemplateManifest;
  indexHtml?: string; // UI / Combo only — plain HTML/CSS/JS, authors can swap in any framework later
  targetRoot?: string; // empty = the default dev root
}

interface PathRow {
  path: string;
  scanDir: string;
  isDefault: boolean;
  zone: "managed" | "dev";
  valid: boolean;
}

const emit = defineEmits<{
  (e: "created", directory: string): void;
  (e: "cancel"): void;
}>();

const notifications = useNotificationStore();
const busy = ref(false);
const showMore = ref(false);

/** Always page + C#: see the note at the top. */
const KIND: TemplateKind = "Combo";

const form = ref({
  name: "",
  id: "",
  description: "",
  publisher: "",
  tooltip: "",
  tab: "AnalyseTool",
  panel: "Extensions",
  dockable: false,
  website: "",
  supportUrl: "",
  updateFeed: "",
  targetRoot: "",
});

const hasUi = computed(() => KIND === "UiOnly" || KIND === "Combo");
const hasCsharp = computed(() => KIND === "Csharp" || KIND === "Combo");

// Already-installed extensions (across all roots), for conflict checks before the host is asked.
const existingExtensions = ref<{ id: string; directory: string }[]>([]);

// Only dev-zone roots are offered — the managed root belongs to the Extension Manager (installed
// packages). The picker appears only when the user has added at least one extra root.
const availableRoots = ref<PathRow[]>([]);
const devRoots = computed(() => availableRoots.value.filter((r) => r.zone !== "managed"));
const rootOptions = computed(() =>
  devRoots.value.map((r) => ({
    label: r.isDefault ? `${r.scanDir} (default)` : r.scanDir,
    value: r.path,
  })),
);
const selectedScanDir = computed(
  () => availableRoots.value.find((r) => r.path === form.value.targetRoot)?.scanDir ?? "",
);

async function loadRoots() {
  try {
    const res = await invoke<{ paths: PathRow[] }>("GetExtensionPaths");
    availableRoots.value = res?.paths ?? [];
    if (!form.value.targetRoot) {
      const def = devRoots.value.find((p) => p.isDefault) ?? devRoots.value[0];
      form.value.targetRoot = def?.path ?? "";
    }
  } catch (e) {
    console.error("Failed to load extension paths", e);
  }
}

async function loadExistingExtensions() {
  try {
    const res = await invoke<{ extensions: { id: string; directory: string }[] }>(
      "GetInstalledExtensions",
    );
    existingExtensions.value = res?.extensions ?? [];
  } catch (e) {
    console.error("Failed to load installed extensions", e);
  }
}

// ---- Derived values: the id, the tooltip and the folder follow the name until the user edits them.
function slug(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function idFromName(name: string) {
  const segments = name.trim().toLowerCase().split(/[^a-z0-9]+/g).filter(Boolean);
  return segments.length ? ["company", ...segments].join(".") : "";
}

// "acme.sample.extension" → "Acme.Sample.Extension" — matches the AssemblyName generated on the host
// side, so the manifest's entryAssembly lines up with the actual built DLL.
function assemblyName(id: string): string {
  return id
    .split(".")
    .filter(Boolean)
    .map((seg) => seg[0].toUpperCase() + seg.slice(1).toLowerCase())
    .join(".");
}

const idTouched = ref(false);
const tooltipTouched = ref(false);

const effectiveId = computed(() =>
  idTouched.value ? form.value.id.trim() : idFromName(form.value.name),
);
const effectiveTooltip = computed(() =>
  tooltipTouched.value
    ? form.value.tooltip.trim()
    : form.value.name.trim()
      ? `Open ${form.value.name.trim()}`
      : "",
);
const folderName = computed(() => slug(form.value.name));

const manifest = computed<ExtensionTemplateManifest>(() => {
  const id = effectiveId.value;
  const m: ExtensionTemplateManifest = { id, version: "1.0.0" };
  const opt = (v: string) => (v.trim() ? v.trim() : undefined);
  m.description = opt(form.value.description);
  m.publisher = opt(form.value.publisher);
  m.website = opt(form.value.website);
  m.supportUrl = opt(form.value.supportUrl);
  m.updateFeed = opt(form.value.updateFeed);
  if (hasCsharp.value && id) m.entryAssembly = `${assemblyName(id)}.dll`;
  if (hasUi.value) {
    m.ui = {
      entryHtml: "index.html",
      tab: form.value.tab.trim(),
      panel: form.value.panel.trim(),
      dockable: form.value.dockable || undefined,
      button: { name: form.value.name.trim(), tooltip: effectiveTooltip.value },
    };
  }
  return m;
});

// Minimal "hello world" page — one file, inline JS and CSS, no build step. Authors can swap in
// Vue/React/anything later; we deliberately don't pick a framework for them.
function indexHtml(title: string) {
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>${title}</title>
    <style>
      :root { color-scheme: light dark; }
      body { font-family: system-ui, -apple-system, sans-serif; padding: 1.5rem; max-width: 720px; }
      h1 { margin: 0 0 0.5rem; }
      p { color: #555; }
      button {
        padding: 0.5rem 1rem; font-size: 0.95rem; cursor: pointer;
        border-radius: 6px; border: 1px solid #ccc; background: #f8f8f8;
      }
      button:hover { background: #efefef; }
      pre {
        background: #f5f5f5; color: #222; padding: 1rem;
        border-radius: 6px; overflow: auto; margin-top: 1rem;
      }
    </style>
  </head>
  <body>
    <h1>${title}</h1>
    <p>This page can call any AnalyseTool command via <code>window.AT.invoke()</code>.</p>
    <button id="run" type="button">Load document</button>
    <pre id="out">Click the button.</pre>

    <script>
      const out = document.getElementById("out");
      document.getElementById("run").addEventListener("click", async () => {
        out.textContent = "Loading...";
        try {
          const data = await window.AT.invoke("GetDocumentData");
          out.textContent = JSON.stringify(data, null, 2);
        } catch (err) {
          out.textContent = (err && err.message) ? err.message : String(err);
        }
      });
    <\/script>
  </body>
</html>
`;
}

function isHttpUrl(value: string) {
  if (!value.trim()) return true; // optional
  try {
    const u = new URL(value.trim());
    return u.protocol === "http:" || u.protocol === "https:";
  } catch {
    return false;
  }
}

const validationError = computed(() => {
  if (!form.value.name.trim()) return "Give the extension a name.";
  const id = effectiveId.value;
  if (!id) return "The id is required.";
  if (!/^[a-z0-9]+(\.[a-z0-9-]+)+$/.test(id))
    return "The id should look like company.product (lowercase, dots between segments).";
  if (hasUi.value) {
    if (!form.value.tab.trim()) return "The ribbon tab is required.";
    if (!form.value.panel.trim()) return "The ribbon panel is required.";
  }
  if (!isHttpUrl(form.value.website)) return "The website must be an http(s) address.";
  if (!isHttpUrl(form.value.supportUrl)) return "The support link must be an http(s) address.";

  if (existingExtensions.value.some((e) => e.id.toLowerCase() === id.toLowerCase()))
    return `An extension with id "${id}" already exists.`;
  if (selectedScanDir.value) {
    const expected = `${selectedScanDir.value}/${folderName.value}`.replace(/\\/g, "/").toLowerCase();
    if (
      existingExtensions.value.some(
        (e) => e.directory.replace(/\\/g, "/").toLowerCase() === expected,
      )
    )
      return `A folder named "${folderName.value}" already exists there.`;
  }
  return "";
});

/** The error is shown only once the user has typed something — an empty form is not wrong yet. */
const started = computed(() => form.value.name.trim().length > 0 || idTouched.value);
const canCreate = computed(() => !validationError.value && !busy.value);

const filesList = computed(() => {
  const files = ["plugin.json"];
  if (hasUi.value) files.push("index.html");
  if (hasCsharp.value) files.push(`${assemblyName(effectiveId.value || "sample")}.csproj`, "Hello.cs");
  files.push("README.md");
  return files.join(", ");
});

async function create() {
  if (validationError.value) {
    notifications.warn(validationError.value);
    return;
  }
  busy.value = true;
  try {
    const payload: CreateExtensionTemplatePayload = {
      folderName: folderName.value,
      kind: KIND,
      pluginJson: manifest.value,
      targetRoot: form.value.targetRoot.trim() || undefined,
    };
    if (hasUi.value) payload.indexHtml = indexHtml(form.value.name.trim());
    const res = await invoke<{ directory: string }>("CreateExtensionTemplate", payload);
    emit("created", res?.directory ?? "");
  } catch (e) {
    notifications.error(e instanceof Error ? e.message : "Failed to create the extension.");
    console.error("Failed to create extension template", e);
  } finally {
    busy.value = false;
  }
}

onMounted(() => {
  loadRoots();
  loadExistingExtensions();
});
</script>

<template>
  <div class="flex flex-col gap-4">
    <!-- Identity -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-3">
      <div class="flex flex-col gap-1">
        <label class="text-sm font-medium">Name</label>
        <InputText size="small" v-model="form.name" placeholder="Room Sheets" autofocus />
        <small class="text-surface-500">The button label and window title.</small>
      </div>
      <div class="flex flex-col gap-1">
        <label class="text-sm font-medium">Id</label>
        <InputText
          :modelValue="idTouched ? form.id : effectiveId"
          placeholder="company.room-sheets"
          @update:modelValue="
            (v) => {
              idTouched = true;
              form.id = String(v ?? '');
            }
          "
        />
        <small class="text-surface-500">
          Unique, never changes.<template v-if="hasCsharp"> Also the C# namespace.</template>
        </small>
      </div>
      <div class="flex flex-col gap-1 sm:col-span-2">
        <label class="text-sm font-medium">Description</label>
        <InputText size="small" v-model="form.description" placeholder="What it does, in one line" />
      </div>
      <div class="flex flex-col gap-1 sm:col-span-2">
        <label class="text-sm font-medium">Publisher</label>
        <InputText size="small" v-model="form.publisher" placeholder="Your name or company" />
      </div>
    </div>

    <!-- The ribbon button — only for kinds that have a page to open. -->
    <div v-if="hasUi" class="rounded-lg border border-surface-200 p-3 flex flex-col gap-3">
      <div class="text-sm font-medium">Ribbon button</div>
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-3">
        <div class="flex flex-col gap-1 sm:col-span-2">
          <label class="text-xs text-surface-500">Tooltip</label>
          <InputText
            :modelValue="tooltipTouched ? form.tooltip : effectiveTooltip"
            placeholder="Open Room Sheets"
            @update:modelValue="
              (v) => {
                tooltipTouched = true;
                form.tooltip = String(v ?? '');
              }
            "
          />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Tab</label>
          <InputText size="small" v-model="form.tab" placeholder="AnalyseTool" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Panel</label>
          <InputText size="small" v-model="form.panel" placeholder="Extensions" />
        </div>
      </div>
      <div class="flex items-center justify-between gap-3">
        <div>
          <div class="text-sm">Open in the dock pane</div>
          <div class="text-xs text-surface-500">
            Instead of a window of its own — for palettes you keep beside the model.
          </div>
        </div>
        <ToggleSwitch v-model="form.dockable" />
      </div>
    </div>

    <!-- The rest of what the manifest holds: links and where the folder goes. Optional, folded. -->
    <div>
      <Button
        :label="showMore ? 'Fewer options' : 'More options'"
        :icon="showMore ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"
        size="small"
        text
        @click="showMore = !showMore"
      />
      <div v-if="showMore" class="grid grid-cols-1 gap-3 mt-2">
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Website</label>
          <InputText size="small" v-model="form.website" placeholder="https://…" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Support link</label>
          <InputText size="small" v-model="form.supportUrl" placeholder="https://… (issues, e-mail page)" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Update feed</label>
          <InputText size="small" v-model="form.updateFeed" placeholder="github:owner/repo" />
          <small class="text-surface-500">
            Lets users get updates from your releases. Fill in when you publish.
          </small>
        </div>
        <div v-if="rootOptions.length > 1" class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Create in</label>
          <Select
            size="small"
            v-model="form.targetRoot"
            :options="rootOptions"
            optionLabel="label"
            optionValue="value"
            placeholder="Select a folder"
          />
        </div>
      </div>
    </div>

    <div
      v-if="started && validationError"
      class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-700"
    >
      {{ validationError }}
    </div>

    <!-- Where it lands — the one thing worth previewing, because it is the thing you open next. -->
    <div class="text-xs text-surface-500 break-all">
      <span class="font-mono">{{ selectedScanDir || "…" }}\{{ folderName || "…" }}</span>
      <br />{{ filesList }}
    </div>

    <div class="flex gap-2 justify-end">
      <Button label="Cancel" severity="secondary" text @click="emit('cancel')" />
      <Button
        label="Create"
        icon="pi pi-check"
        :loading="busy"
        :disabled="!canCreate"
        @click="create"
      />
    </div>
  </div>
</template>
