<script setup lang="ts">
/**
 * Edit an extension's plugin.json without opening the file.
 *
 * The manager could open the folder and delete it, but not change the one thing people change most:
 * what the button is called and where it sits. This is that. It edits what is safe to edit and
 * nothing else — the id is the extension's identity (folder, command namespace, enable-state key),
 * so it is shown and never editable; entryAssembly, devUrl and icon stay in the file.
 *
 * Installed packages open read-only: their manifest belongs to the publisher, and the next update
 * would put it back.
 */
import { computed, ref, watch } from "vue";
import ToggleSwitch from "primevue/toggleswitch";
import SelectButton from "primevue/selectbutton";
import { invoke } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";

interface ManifestButton {
  name: string;
  tooltip?: string | null;
  kind: "push" | "stacked" | "pulldown";
  order: number;
  tab?: string | null;
  panel?: string | null;
  dockable: boolean;
}

interface ManifestData {
  id: string;
  version: string;
  directory: string;
  editable: boolean;
  description?: string | null;
  publisher?: string | null;
  website?: string | null;
  supportUrl?: string | null;
  updateFeed?: string | null;
  hasUi: boolean;
  usesButtons: boolean;
  hasButton: boolean;
  button?: ManifestButton | null;
}

const props = defineProps<{ visible: boolean; extensionId: string | null }>();
const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "saved"): void;
}>();

const notifications = useNotificationStore();
const loading = ref(false);
const saving = ref(false);
const data = ref<ManifestData | null>(null);

const form = ref({
  description: "",
  publisher: "",
  website: "",
  supportUrl: "",
  updateFeed: "",
  name: "",
  tooltip: "",
  tab: "",
  panel: "",
  kind: "push" as ManifestButton["kind"],
  order: 0,
  dockable: false,
});

const kindOptions = computed(() => {
  const options = [
    { label: "Large", value: "push" },
    { label: "Small (stacked)", value: "stacked" },
  ];
  // Pulldown needs its items written by hand; it is offered only to keep an existing one as it is.
  if (data.value?.button?.kind === "pulldown") options.push({ label: "Pulldown", value: "pulldown" });
  return options;
});

async function load() {
  if (!props.extensionId) return;
  loading.value = true;
  data.value = null;
  try {
    const m = await invoke<ManifestData>("GetExtensionManifest", { id: props.extensionId });
    data.value = m;
    form.value = {
      description: m.description ?? "",
      publisher: m.publisher ?? "",
      website: m.website ?? "",
      supportUrl: m.supportUrl ?? "",
      updateFeed: m.updateFeed ?? "",
      name: m.button?.name ?? "",
      tooltip: m.button?.tooltip ?? "",
      tab: m.button?.tab ?? "",
      panel: m.button?.panel ?? "",
      kind: m.button?.kind ?? "push",
      order: m.button?.order ?? 0,
      dockable: m.button?.dockable ?? false,
    };
  } catch (e) {
    notifications.error(`Could not read the manifest: ${String((e as Error)?.message ?? e)}`);
    close();
  } finally {
    loading.value = false;
  }
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
  if (data.value?.hasButton && !form.value.name.trim()) return "The button needs a name.";
  if (!isHttpUrl(form.value.website)) return "The website must be an http(s) address.";
  if (!isHttpUrl(form.value.supportUrl)) return "The support link must be an http(s) address.";
  return "";
});

const canSave = computed(
  () => !!data.value?.editable && !validationError.value && !saving.value && !loading.value,
);

function close() {
  emit("update:visible", false);
}

async function save() {
  if (!data.value || !canSave.value) return;
  saving.value = true;
  try {
    // Every field is sent: an empty string means "remove", which is what clearing a field means
    // to the person doing it. Button fields go only where a single button exists — the host
    // ignores them otherwise, but there is no point sending what cannot apply.
    const payload: Record<string, unknown> = {
      id: data.value.id,
      description: form.value.description,
      publisher: form.value.publisher,
      website: form.value.website,
      supportUrl: form.value.supportUrl,
      updateFeed: form.value.updateFeed,
    };
    if (data.value.hasButton) {
      Object.assign(payload, {
        name: form.value.name,
        tooltip: form.value.tooltip,
        tab: form.value.tab,
        panel: form.value.panel,
        kind: form.value.kind,
        order: Number(form.value.order) || 0,
        dockable: form.value.dockable,
      });
    }
    await invoke("EditExtensionManifest", payload);
    notifications.success(`Saved ${data.value.id}`);
    emit("saved");
    close();
  } catch (e) {
    notifications.error(`Could not save: ${String((e as Error)?.message ?? e)}`);
  } finally {
    saving.value = false;
  }
}

watch(
  () => props.visible,
  (visible) => {
    if (visible) load();
  },
);
</script>

<template>
  <Drawer
    :visible="props.visible"
    position="right"
    header="Edit extension"
    class="!w-full md:!w-[36rem]"
    @update:visible="emit('update:visible', !!$event)"
  >
    <div v-if="loading" class="text-surface-500 text-sm p-4 text-center">
      <i class="pi pi-spin pi-spinner mr-2" />Loading…
    </div>

    <div v-else-if="data" class="flex flex-col gap-4">
      <!-- Identity: shown, never edited. -->
      <div class="rounded-lg bg-surface-100 p-3 text-sm">
        <div class="flex items-center justify-between gap-3">
          <span class="font-mono">{{ data.id }}</span>
          <Tag :value="data.version" severity="secondary" />
        </div>
        <p class="text-xs text-surface-500 mt-1">
          The id is the extension's identity — folder, command names, enabled state — and cannot be
          changed here. The version is bumped in plugin.json when you publish.
        </p>
      </div>

      <div
        v-if="!data.editable"
        class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-700"
      >
        This is an installed package. Its manifest belongs to the publisher, and the next update
        would overwrite any change — so it is shown read-only.
      </div>

      <!-- The button: the thing people actually come here to change. -->
      <div v-if="data.hasButton" class="rounded-lg border border-surface-200 p-3 flex flex-col gap-3">
        <div class="text-sm font-medium">Ribbon button</div>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-3">
          <div class="flex flex-col gap-1 sm:col-span-2">
            <label class="text-xs text-surface-500">Name</label>
            <InputText v-model="form.name" size="small" :disabled="!data.editable" />
            <small class="text-surface-500">Also the extension's display name and window title.</small>
          </div>
          <div class="flex flex-col gap-1 sm:col-span-2">
            <label class="text-xs text-surface-500">Tooltip</label>
            <InputText v-model="form.tooltip" size="small" :disabled="!data.editable" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-xs text-surface-500">Tab</label>
            <InputText v-model="form.tab" size="small" placeholder="AnalyseTool" :disabled="!data.editable" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-xs text-surface-500">Panel</label>
            <InputText v-model="form.panel" size="small" placeholder="Extensions" :disabled="!data.editable" />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-xs text-surface-500">Shape</label>
            <SelectButton
              v-model="form.kind"
              :options="kindOptions"
              optionLabel="label"
              optionValue="value"
              :allowEmpty="false"
              size="small"
              :disabled="!data.editable"
            />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-xs text-surface-500">Order in panel</label>
            <InputText
              v-model="form.order"
              size="small"
              type="number"
              min="0"
              class="w-24"
              :disabled="!data.editable"
            />
            <small class="text-surface-500">Lower comes first. 0 = no preference, after the numbered ones.</small>
          </div>
        </div>
        <div v-if="data.hasUi" class="flex items-center justify-between gap-3">
          <div>
            <div class="text-sm">Open in the dock pane</div>
            <div class="text-xs text-surface-500">Instead of a window of its own.</div>
          </div>
          <ToggleSwitch v-model="form.dockable" :disabled="!data.editable" />
        </div>
      </div>

      <div
        v-else-if="data.usesButtons"
        class="rounded-lg border border-surface-200 p-3 text-xs text-surface-600"
      >
        This extension declares several buttons (<span class="font-mono">ui.buttons</span>), each with
        its own placement. Edit those in <span class="font-mono">plugin.json</span>; the fields below
        still apply.
      </div>

      <!-- Vendor metadata -->
      <div class="grid grid-cols-1 gap-3">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Description</label>
          <InputText v-model="form.description" size="small" placeholder="What it does, in one line" :disabled="!data.editable" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Publisher</label>
          <InputText v-model="form.publisher" size="small" :disabled="!data.editable" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Website</label>
          <InputText v-model="form.website" size="small" placeholder="https://…" :disabled="!data.editable" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Support link</label>
          <InputText v-model="form.supportUrl" size="small" placeholder="https://…" :disabled="!data.editable" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Update feed</label>
          <InputText v-model="form.updateFeed" size="small" placeholder="github:owner/repo" :disabled="!data.editable" />
        </div>
      </div>

      <div
        v-if="validationError"
        class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-700"
      >
        {{ validationError }}
      </div>

      <p class="text-xs text-surface-500 break-all">
        {{ data.directory }}\plugin.json — other fields in the file are left as they are.
      </p>

      <div class="flex gap-2 justify-end">
        <Button label="Cancel" severity="secondary" text @click="close" />
        <Button label="Save" icon="pi pi-check" :loading="saving" :disabled="!canSave" @click="save" />
      </div>
    </div>
  </Drawer>
</template>
