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

interface ExtensionTemplateManifest {
  id: string;
  version: string;
  entryAssembly: string;
  ui: {
    entryHtml: string;
    devUrl: string;
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
  pluginJson: ExtensionTemplateManifest;
  indexHtml: string;
  mainTs: string;
}

interface TemplateFormState {
  folderName: string;
  id: string;
  name: string;
  version: string;
  devUrl: string;
  tab: string;
  panel: string;
  tooltip: string;
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

function createDefaultTemplateForm(): TemplateFormState {
  return {
    folderName: "sample-extension",
    id: "company.sample.extension",
    name: "Sample Extension",
    version: "1.0.0",
    devUrl: "",
    tab: "AnalyseTool",
    panel: "Extensions",
    tooltip: "Open the Sample Extension page",
  };
}

function resetForm() {
  templateForm.value = createDefaultTemplateForm();
}

function normalizeFolderName() {
  const normalized = slugifySegment(templateForm.value.folderName) || "sample-extension";
  templateForm.value.folderName = normalized;
}

function syncFromName() {
  const name = templateForm.value.name.trim();
  if (!name) return;

  if (
    !templateForm.value.folderName.trim() ||
    templateForm.value.folderName === "sample-extension"
  ) {
    templateForm.value.folderName = slugifySegment(name) || "sample-extension";
  }
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

const normalizedFolderName = computed(
  () => slugifySegment(templateForm.value.folderName) || "sample-extension",
);

const manifestPreview = computed<ExtensionTemplateManifest>(() => ({
  id: templateForm.value.id.trim(),
  version: templateForm.value.version.trim(),
  entryAssembly: "",
  ui: {
    entryHtml: `${normalizedFolderName.value}/index.html`,
    devUrl: templateForm.value.devUrl.trim(),
    tab: templateForm.value.tab.trim(),
    panel: templateForm.value.panel.trim(),
    button: {
      name: templateForm.value.name.trim(),
      tooltip: templateForm.value.tooltip.trim(),
    },
  },
}));

const manifestPreviewText = computed(() => JSON.stringify(manifestPreview.value, null, 2));

const indexHtmlPreview = computed(() => {
  const title = templateForm.value.name.trim() || "Sample Extension";
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>${title}</title>
  </head>
  <body>
    <main>
      <h1>${title}</h1>
      <p>This UI-only extension can call already registered AnalyseTool commands.</p>
      <button id="load-document" type="button">Load document data</button>
      <pre id="output">Click the button to load document data.</pre>
    </main>
    <script>
      async function invokeHostCommand(command, payload) {
        if (!window.AT || typeof window.AT.invoke !== "function") {
          throw new Error("Host bridge is not available.");
        }

        return window.AT.invoke(command, payload ?? null);
      }

      const output = document.getElementById("output");
      const loadButton = document.getElementById("load-document");

      if (!(output instanceof HTMLPreElement) || !(loadButton instanceof HTMLButtonElement)) {
        throw new Error("Template markup is missing required elements.");
      }

      loadButton.addEventListener("click", async () => {
        output.textContent = "Loading...";

        try {
          const documentData = await invokeHostCommand("GetDocumentData");
          output.textContent = JSON.stringify(
            {
              documentName: documentData.name,
              documentId: documentData.id,
            },
            null,
            2,
          );
        } catch (error) {
          output.textContent = error instanceof Error ? error.message : String(error);
        }
      });
    <\/script>
  </body>
</html>`;
});

const mainTsPreview = computed(
  () => `export {};

interface DocumentData {
  name: string;
  id: string;
}

type HostBridge = {
  invoke<T = unknown>(command: string, payload?: unknown): Promise<T>;
};

declare global {
  interface Window {
    AT?: HostBridge;
  }
}

const hostWindow = window as Window & { AT?: HostBridge };

async function invokeHostCommand<T = unknown>(command: string, payload?: unknown): Promise<T> {
  if (!hostWindow.AT?.invoke) {
    throw new Error("Host bridge is not available.");
  }

  return hostWindow.AT.invoke<T>(command, payload ?? null);
}

const output = document.getElementById("output");
const loadButton = document.getElementById("load-document");

if (!(output instanceof HTMLPreElement) || !(loadButton instanceof HTMLButtonElement)) {
  throw new Error("Template markup is missing required elements.");
}

loadButton.addEventListener("click", async () => {
  output.textContent = "Loading...";

  try {
    const documentData = await invokeHostCommand<DocumentData>("GetDocumentData");
    output.textContent = JSON.stringify(
      {
        documentName: documentData.name,
        documentId: documentData.id,
      },
      null,
      2,
    );
  } catch (error) {
    output.textContent = error instanceof Error ? error.message : String(error);
  }
});
`,
);

const templateValidationError = computed(() => {
  if (!normalizedFolderName.value) return "Folder name is required.";
  if (!templateForm.value.id.trim()) return "Plugin id is required.";
  if (!templateForm.value.name.trim()) return "Name is required.";
  if (!templateForm.value.version.trim()) return "Version is required.";
  if (!templateForm.value.tab.trim()) return "Tab is required.";
  if (!templateForm.value.panel.trim()) return "Panel is required.";
  if (!templateForm.value.tooltip.trim()) return "Tooltip is required.";
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
      pluginJson: manifestPreview.value,
      indexHtml: indexHtmlPreview.value,
      mainTs: mainTsPreview.value,
    };

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
    if (visible) resetForm();
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
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Folder name</label>
            <InputText
              v-model="templateForm.folderName"
              placeholder="sample-extension"
              @blur="normalizeFolderName"
            />
            <small class="text-surface-500">Creates the folder inside extensions root.</small>
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Name</label>
            <InputText
              v-model="templateForm.name"
              placeholder="Sample Extension"
              @blur="syncFromName"
            />
            <small class="text-surface-500">Ribbon button label and window title.</small>
          </div>
          <div class="flex flex-col gap-1 md:col-span-2">
            <label class="text-sm font-medium">Plugin id</label>
            <InputText v-model="templateForm.id" placeholder="company.sample.extension" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Version</label>
            <InputText v-model="templateForm.version" placeholder="1.0.0" />
          </div>
          <div class="flex flex-col gap-1 md:col-span-2">
            <label class="text-sm font-medium">Dev URL</label>
            <InputText v-model="templateForm.devUrl" placeholder="optional" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Tab</label>
            <InputText v-model="templateForm.tab" placeholder="AnalyseTool" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Panel</label>
            <InputText v-model="templateForm.panel" placeholder="Extensions" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Tooltip</label>
            <InputText
              v-model="templateForm.tooltip"
              placeholder="Open the Sample Extension page"
            />
          </div>
        </div>

        <div
          v-if="templateValidationError"
          class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-700"
        >
          {{ templateValidationError }}
        </div>

        <div class="text-sm text-surface-500 break-all">
          Root: {{ props.extensionsRoot || "Unknown" }}<br />
          Folder: {{ normalizedFolderName }}<br />
          Files: plugin.json, {{ normalizedFolderName }}/index.html,
          {{ normalizedFolderName }}/main.ts<br />
          Mode: UI-only extension, no separate DLL required
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

      <div class="rounded-xl border border-surface-200 bg-surface-0 p-4">
        <h3 class="font-semibold mb-2">index.html preview</h3>
        <pre
          class="bg-surface-100 text-surface-700 text-xs rounded p-3 overflow-auto whitespace-pre-wrap break-all"
          >{{ indexHtmlPreview }}</pre
        >
      </div>

      <div class="rounded-xl border border-surface-200 bg-surface-0 p-4">
        <h3 class="font-semibold mb-2">main.ts preview</h3>
        <pre
          class="bg-surface-100 text-surface-700 text-xs rounded p-3 overflow-auto whitespace-pre-wrap break-all"
          >{{ mainTsPreview }}</pre
        >
      </div>
    </div>
  </Drawer>
</template>
