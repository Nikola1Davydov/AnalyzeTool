<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "CreateExtensionTemplateDrawer",
});
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

type TemplateKind = "UiOnly" | "Csharp" | "Combo";

interface ExtensionTemplateManifest {
  id: string;
  version: string;
  entryAssembly?: string;
  ui?: {
    entryHtml: string;
    tab: string;
    panel: string;
    button: {
      name: string;
      tooltip: string;
    };
  };
}

interface CreateExtensionTemplatePayload {
  folderName: string;
  kind: TemplateKind;
  pluginJson: ExtensionTemplateManifest;
  // Only sent for UI / Combo. Plain HTML/CSS/JS — authors can swap in any framework later.
  indexHtml?: string;
  // Empty (omitted) means the default root.
  targetRoot?: string;
}

interface TemplateFormState {
  kind: TemplateKind;
  id: string;
  name: string;
  tab: string;
  panel: string;
  tooltip: string;
  // Selected source root (path, not scan dir). Empty = default.
  targetRoot: string;
}

interface PathRow {
  path: string;
  scanDir: string;
  isDefault: boolean;
  valid: boolean;
  reason: string;
  extensionCount: number;
}

const props = defineProps<{
  visible: boolean;
  extensionsRoot?: string | null;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "created"): void;
}>();

const notificationStore = useNotificationStore();
const templateBusy = ref(false);
const templateForm = ref<TemplateFormState>(createDefaultTemplateForm());

const kindOptions: { value: TemplateKind; label: string; hint: string }[] = [
  { value: "UiOnly", label: "UI only", hint: "plugin.json + index.html" },
  { value: "Csharp", label: "C# commands", hint: "plugin.json + csproj + Hello.cs + README" },
  { value: "Combo", label: "C# + UI", hint: "both" },
];

const hasUi = computed(
  () => templateForm.value.kind === "UiOnly" || templateForm.value.kind === "Combo",
);
const hasCsharp = computed(
  () => templateForm.value.kind === "Csharp" || templateForm.value.kind === "Combo",
);

// Already-installed extensions (across all roots), used for conflict validation. Loaded on open.
const existingExtensions = ref<{ id: string; directory: string }[]>([]);

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

// Source roots populated from GetExtensionPaths. The "Target root" select is shown only when
// the user has added at least one extra root — otherwise everything goes to the default root.
const availableRoots = ref<PathRow[]>([]);
const rootOptions = computed(() =>
  availableRoots.value.map((r) => ({
    label: r.isDefault ? `${r.scanDir} (default)` : r.scanDir,
    value: r.path,
  })),
);
const selectedScanDir = computed(
  () =>
    availableRoots.value.find((r) => r.path === templateForm.value.targetRoot)?.scanDir ?? "",
);
const showAdvanced = ref(false);

async function loadRoots() {
  try {
    const res = await invoke<{ paths: PathRow[] }>("GetExtensionPaths");
    availableRoots.value = res?.paths ?? [];
    if (!templateForm.value.targetRoot) {
      const def = availableRoots.value.find((p) => p.isDefault);
      templateForm.value.targetRoot = def?.path ?? "";
    }
  } catch (e) {
    console.error("Failed to load extension paths", e);
  }
}

function closeDrawer() {
  emit("update:visible", false);
}

function slugifySegment(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function buildPluginId(value: string) {
  const segments = value
    .trim()
    .toLowerCase()
    .split(/[^a-z0-9]+/g)
    .filter(Boolean);

  if (segments.length === 0) return "company.sample.extension";
  return ["company", ...segments].join(".");
}

// "acme.sample.extension" → "Acme.Sample.Extension" — matches the AssemblyName generated on the host
// side, so the manifest's entryAssembly preview lines up with the actual built DLL.
function buildAssemblyName(id: string): string {
  return id
    .split(".")
    .filter(Boolean)
    .map((seg) => seg[0].toUpperCase() + seg.slice(1).toLowerCase())
    .join(".");
}

function createDefaultTemplateForm(): TemplateFormState {
  return {
    kind: "UiOnly",
    id: "company.sample.extension",
    name: "Sample Extension",
    tab: "AnalyseTool",
    panel: "Extensions",
    tooltip: "Open the Sample Extension page",
    targetRoot: "",
  };
}

function resetForm() {
  templateForm.value = createDefaultTemplateForm();
}

function syncFromName() {
  const name = templateForm.value.name.trim();
  if (!name) return;

  if (!templateForm.value.id.trim() || templateForm.value.id === "company.sample.extension") {
    templateForm.value.id = buildPluginId(name);
  }
  if (
    !templateForm.value.tooltip.trim() ||
    templateForm.value.tooltip === "Open the Sample Extension page"
  ) {
    templateForm.value.tooltip = `Open the ${name} page`;
  }
}

// Folder name is derived from Name — slug of the name, with a sensible fallback. Authors don't
// edit it directly: the form stays compact and the folder always matches what they typed.
const normalizedFolderName = computed(
  () => slugifySegment(templateForm.value.name) || "sample-extension",
);

const manifestPreview = computed<ExtensionTemplateManifest>(() => {
  const id = templateForm.value.id.trim();
  const m: ExtensionTemplateManifest = {
    id,
    // Templates always start at 1.0.0; authors bump it manually in plugin.json.
    version: "1.0.0",
  };
  if (hasCsharp.value && id) {
    m.entryAssembly = `${buildAssemblyName(id)}.dll`;
  }
  if (hasUi.value) {
    m.ui = {
      entryHtml: "index.html",
      tab: templateForm.value.tab.trim(),
      panel: templateForm.value.panel.trim(),
      button: {
        name: templateForm.value.name.trim(),
        tooltip: templateForm.value.tooltip.trim(),
      },
    };
  }
  return m;
});

const manifestPreviewText = computed(() => JSON.stringify(manifestPreview.value, null, 2));

// Minimal "hello world" page — one file, inline JS and CSS, no build step. Authors can swap in
// Vue/React/anything later; we deliberately don't pick a framework for them.
const indexHtmlPreview = computed(() => {
  const title = templateForm.value.name.trim() || "Sample Extension";
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
});

const filesList = computed(() => {
  const files = ["plugin.json"];
  if (hasUi.value) files.push("index.html");
  if (hasCsharp.value) {
    const assembly = buildAssemblyName(templateForm.value.id.trim() || "Sample");
    files.push(`${assembly}.csproj`, "Hello.cs", "README.md");
  }
  return files.join(", ");
});

const templateValidationError = computed(() => {
  if (!templateForm.value.name.trim()) return "Name is required.";

  const trimmedId = templateForm.value.id.trim();
  if (!trimmedId) return "Plugin id is required.";

  if (hasUi.value) {
    if (!templateForm.value.tab.trim()) return "Tab is required.";
    if (!templateForm.value.panel.trim()) return "Panel is required.";
    if (!templateForm.value.tooltip.trim()) return "Tooltip is required.";
  }

  // Conflict checks against already-installed extensions — catch collisions before we hit the host
  // (which would throw on a duplicate folder).
  const idConflict = existingExtensions.value.some(
    (e) => e.id.toLowerCase() === trimmedId.toLowerCase(),
  );
  if (idConflict) return `An extension with id "${trimmedId}" already exists.`;

  if (selectedScanDir.value) {
    const folder = normalizedFolderName.value;
    const expected = (selectedScanDir.value + "/" + folder).replace(/\\/g, "/").toLowerCase();
    const folderConflict = existingExtensions.value.some(
      (e) => e.directory.replace(/\\/g, "/").toLowerCase() === expected,
    );
    if (folderConflict)
      return `A folder named "${folder}" already exists in the target root.`;
  }

  return "";
});

const canCreateTemplate = computed(() => !templateValidationError.value && !templateBusy.value);

async function createTemplate() {
  if (templateValidationError.value) {
    notificationStore.warn(templateValidationError.value);
    return;
  }

  templateBusy.value = true;
  try {
    const payload: CreateExtensionTemplatePayload = {
      folderName: normalizedFolderName.value,
      kind: templateForm.value.kind,
      pluginJson: manifestPreview.value,
      targetRoot: templateForm.value.targetRoot.trim() || undefined,
    };
    if (hasUi.value) payload.indexHtml = indexHtmlPreview.value;

    await invoke("CreateExtensionTemplate", payload);
    notificationStore.success(`Template created in ${normalizedFolderName.value}.`);
    emit("created");
    closeDrawer();
  } catch (e) {
    const message = e instanceof Error ? e.message : "Failed to create template.";
    notificationStore.error(message);
    console.error("Failed to create extension template", e);
  } finally {
    templateBusy.value = false;
  }
}

watch(
  () => props.visible,
  (visible) => {
    if (visible) {
      resetForm();
      showAdvanced.value = false;
      loadRoots();
      loadExistingExtensions();
    }
  },
);
</script>

<template>
  <Drawer
    :visible="props.visible"
    position="right"
    header="Create extension template"
    class="!w-full md:!w-[44rem]"
    @update:visible="emit('update:visible', !!$event)"
  >
    <div class="flex flex-col gap-4">
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-4 flex flex-col gap-4">
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Type — fundamental choice; drives which files are scaffolded and what fields the form shows. -->
          <div class="md:col-span-2 flex flex-col gap-2">
            <label class="text-sm font-medium">Type</label>
            <div class="flex gap-2 flex-wrap">
              <Button
                v-for="o in kindOptions"
                :key="o.value"
                :label="o.label"
                :severity="templateForm.kind === o.value ? 'primary' : 'secondary'"
                :outlined="templateForm.kind !== o.value"
                size="small"
                @click="templateForm.kind = o.value"
              />
            </div>
            <small class="text-surface-500">
              {{ kindOptions.find((o) => o.value === templateForm.kind)?.hint }}
            </small>
          </div>

          <div class="flex flex-col gap-1 md:col-span-2">
            <label class="text-sm font-medium">Name</label>
            <InputText
              v-model="templateForm.name"
              placeholder="Sample Extension"
              @blur="syncFromName"
            />
            <small class="text-surface-500">
              Ribbon button label / window title (for UI). The folder name is derived from this.
            </small>
          </div>
          <div class="flex flex-col gap-1 md:col-span-2">
            <label class="text-sm font-medium">Plugin id</label>
            <InputText v-model="templateForm.id" placeholder="company.sample.extension" />
            <small v-if="hasCsharp" class="text-surface-500">
              Becomes the C# namespace and assembly name (e.g. <code>Company.Sample.Extension</code>).
            </small>
          </div>

          <!-- Target root: only meaningful when the user has more than one configured source. -->
          <div v-if="rootOptions.length > 1" class="flex flex-col gap-1 md:col-span-2">
            <label class="text-sm font-medium">Target root</label>
            <Select
              v-model="templateForm.targetRoot"
              :options="rootOptions"
              optionLabel="label"
              optionValue="value"
              placeholder="Select a source root"
            />
          </div>

          <!-- Advanced: ribbon placement + button tooltip. Only relevant when the template has a UI. -->
          <template v-if="hasUi">
            <div class="md:col-span-2">
              <Button
                :label="showAdvanced ? 'Hide advanced' : 'Show advanced'"
                :icon="showAdvanced ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"
                size="small"
                text
                @click="showAdvanced = !showAdvanced"
              />
            </div>
            <template v-if="showAdvanced">
              <div class="flex flex-col gap-1">
                <label class="text-sm font-medium">Tab</label>
                <InputText v-model="templateForm.tab" placeholder="AnalyseTool" />
              </div>
              <div class="flex flex-col gap-1">
                <label class="text-sm font-medium">Panel</label>
                <InputText v-model="templateForm.panel" placeholder="Extensions" />
              </div>
              <div class="flex flex-col gap-1 md:col-span-2">
                <label class="text-sm font-medium">Tooltip</label>
                <InputText
                  v-model="templateForm.tooltip"
                  placeholder="Open the Sample Extension page"
                />
              </div>
            </template>
          </template>
        </div>

        <div
          v-if="templateValidationError"
          class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-700"
        >
          {{ templateValidationError }}
        </div>

        <div class="text-sm text-surface-500 break-all">
          Root: {{ selectedScanDir || props.extensionsRoot || "Unknown" }}<br />
          Folder: {{ normalizedFolderName }}<br />
          Files: {{ filesList }}
        </div>

        <div class="flex gap-2 justify-end">
          <Button label="Cancel" severity="secondary" text @click="closeDrawer" />
          <Button
            label="Create template"
            icon="pi pi-check"
            :loading="templateBusy"
            :disabled="!canCreateTemplate"
            @click="createTemplate"
          />
        </div>
      </div>

      <div class="rounded-xl border border-surface-200 bg-surface-0 p-4">
        <div class="flex items-center justify-between mb-2">
          <h3 class="font-semibold">plugin.json preview</h3>
        </div>
        <pre
          class="bg-surface-100 text-surface-700 text-xs rounded p-3 overflow-auto whitespace-pre-wrap break-all"
          >{{ manifestPreviewText }}</pre
        >
      </div>

      <div v-if="hasUi" class="rounded-xl border border-surface-200 bg-surface-0 p-4">
        <h3 class="font-semibold mb-2">index.html preview</h3>
        <pre
          class="bg-surface-100 text-surface-700 text-xs rounded p-3 overflow-auto whitespace-pre-wrap break-all"
          >{{ indexHtmlPreview }}</pre
        >
      </div>

      <div v-if="hasCsharp" class="rounded-xl border border-surface-200 bg-surface-0 p-4 text-sm">
        <h3 class="font-semibold mb-2">C# build</h3>
        <p class="text-surface-600">
          After creation, open the folder, run <code>dotnet build -c Release</code>, then copy
          <code>bin/Release/net8.0-windows/&lt;assembly&gt;.dll</code> next to <code>plugin.json</code>
          and Reload. The full instructions are written to <code>README.md</code>.
        </p>
      </div>
    </div>
  </Drawer>
</template>
