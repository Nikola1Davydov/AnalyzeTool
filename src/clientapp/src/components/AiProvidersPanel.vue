<script setup lang="ts">
import { ref, onMounted } from "vue";
import { storeToRefs } from "pinia";
import { useToast } from "primevue/usetoast";
import { useAiSettingsStore, type AiProviderInfo } from "@/stores/useAiSettingsStore";

// Settings-only management of AI providers (endpoints). The built-in Ollama is listed read-only;
// user-added providers speak the OpenAI-compatible protocol (OpenAI, OpenRouter, Groq, Mistral,
// LM Studio, vLLM…). API keys go straight to the host, are stored DPAPI-encrypted there and NEVER
// come back — an existing key shows only as a "key stored" badge.

const store = useAiSettingsStore();
const { providers, providersLoading } = storeToRefs(store);
const toast = useToast();

onMounted(() => store.loadProviders());

// ---- add / edit form -----------------------------------------------------------------------------
const formOpen = ref(false);
const editingId = ref<string | null>(null);
const fName = ref("");
const fBaseUrl = ref("");
const fApiKey = ref(""); // stays empty when editing = keep the stored key
const fTimeout = ref<number | null>(null);
const hadKey = ref(false);
const saving = ref(false);

function startAdd() {
  editingId.value = null;
  fName.value = "";
  fBaseUrl.value = "";
  fApiKey.value = "";
  fTimeout.value = null;
  hadKey.value = false;
  formOpen.value = true;
}
function startEdit(p: AiProviderInfo) {
  editingId.value = p.id;
  fName.value = p.displayName;
  fBaseUrl.value = p.baseUrl;
  fApiKey.value = "";
  fTimeout.value = p.timeoutSeconds;
  hadKey.value = p.hasKey;
  formOpen.value = true;
}

async function save() {
  if (!fName.value.trim() || !fBaseUrl.value.trim()) return;
  saving.value = true;
  try {
    const error = await store.saveProvider({
      id: editingId.value,
      displayName: fName.value,
      baseUrl: fBaseUrl.value,
      // Empty input while editing means "keep the stored key" (null); on a new provider it means no key.
      apiKey: editingId.value && !fApiKey.value ? null : fApiKey.value,
      timeoutSeconds: fTimeout.value,
    });
    if (error) {
      toast.add({ severity: "error", summary: "Save failed", detail: error, life: 4000 });
      return;
    }
    formOpen.value = false;
  } finally {
    saving.value = false;
  }
}

async function removeKey() {
  if (!editingId.value) return;
  const error = await store.saveProvider({
    id: editingId.value,
    displayName: fName.value,
    baseUrl: fBaseUrl.value,
    apiKey: "",
    timeoutSeconds: fTimeout.value,
  });
  if (!error) hadKey.value = false;
}

// ---- test connection -------------------------------------------------------------------------------
const testing = ref<string | null>(null);
async function test(p: AiProviderInfo) {
  testing.value = p.id;
  try {
    const res = await store.listProviderModels(p.id);
    if (res.running)
      toast.add({
        severity: "success",
        summary: `${p.displayName}: connected`,
        detail: `${res.models.length} model(s) listed.`,
        life: 3000,
      });
    else
      toast.add({
        severity: "error",
        summary: `${p.displayName}: unreachable`,
        detail: res.error ?? "No response.",
        life: 5000,
      });
  } finally {
    testing.value = null;
  }
}

async function remove(p: AiProviderInfo) {
  await store.deleteProvider(p.id);
  toast.add({ severity: "success", summary: "Provider removed", detail: p.displayName, life: 2500 });
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex items-center gap-2">
      <span class="text-sm font-medium">AI providers</span>
      <span v-if="providersLoading" class="text-xs text-surface-400"><i class="pi pi-spin pi-spinner" /></span>
      <span class="grow" />
      <Button icon="pi pi-plus" label="Add provider" size="small" text @click="startAdd" />
    </div>

    <!-- Provider list -->
    <div class="flex flex-col gap-1">
      <div
        v-for="p in providers"
        :key="p.id"
        class="flex items-center gap-2 px-2 py-1.5 rounded border border-surface-200"
      >
        <i :class="p.builtIn ? 'pi pi-home' : 'pi pi-cloud'" class="text-surface-400" />
        <div class="min-w-0 grow">
          <div class="text-sm font-medium truncate">{{ p.displayName }}</div>
          <div class="text-xs text-surface-400 truncate">{{ p.baseUrl }}</div>
        </div>
        <Tag v-if="p.hasKey" value="key stored" severity="secondary" />
        <Button
          :icon="testing === p.id ? 'pi pi-spin pi-spinner' : 'pi pi-bolt'"
          text
          rounded
          size="small"
          severity="secondary"
          :disabled="testing !== null"
          v-tooltip.top="'Test connection'"
          @click="test(p)"
        />
        <template v-if="!p.builtIn">
          <Button icon="pi pi-pencil" text rounded size="small" severity="secondary" @click="startEdit(p)" />
          <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click="remove(p)" />
        </template>
      </div>
    </div>

    <p class="text-xs text-surface-400">
      Any OpenAI-compatible endpoint works: OpenAI, OpenRouter, Groq, Mistral — or local LM Studio /
      vLLM. Keys are encrypted on this machine and never leave it.
    </p>

    <!-- Add / edit dialog -->
    <Dialog
      v-model:visible="formOpen"
      modal
      dismissableMask
      :header="editingId ? 'Edit provider' : 'Add provider'"
      :style="{ width: 'min(30rem, 95vw)' }"
    >
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Name</label>
          <InputText v-model="fName" placeholder="e.g. OpenRouter" class="w-full" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Base URL (including /v1)</label>
          <InputText v-model="fBaseUrl" placeholder="https://openrouter.ai/api/v1" class="w-full" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">API key</label>
          <InputText
            v-model="fApiKey"
            type="password"
            :placeholder="hadKey ? '(unchanged — a key is stored)' : 'sk-…'"
            class="w-full"
            autocomplete="off"
          />
          <button
            v-if="hadKey"
            type="button"
            class="self-start text-xs text-red-500 hover:underline"
            @click="removeKey"
          >
            Remove stored key
          </button>
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Timeout, seconds (default 120)</label>
          <InputText
            :modelValue="fTimeout == null ? '' : String(fTimeout)"
            placeholder="120"
            class="w-32"
            @update:modelValue="fTimeout = /^\d+$/.test(String($event)) ? Number($event) : null"
          />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="formOpen = false" />
        <Button
          :label="saving ? 'Saving…' : 'Save'"
          icon="pi pi-check"
          :disabled="saving || !fName.trim() || !fBaseUrl.trim()"
          @click="save"
        />
      </template>
    </Dialog>
  </div>
</template>
