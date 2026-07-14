<script setup lang="ts">
import { ref, computed, watch, onMounted } from "vue";
import { storeToRefs } from "pinia";
import { OLLAMA_PROVIDER, useAiSettingsStore, type AiModelSource } from "@/stores/useAiSettingsStore";
import AiProvidersPanel from "@/components/AiProvidersPanel.vue";

// Shared AI-model picker used everywhere.
//  - manage=true  (Settings window): picking sets the GLOBAL model directly; lets you add / rename /
//    delete saved cloud models and shows the Ollama status.
//  - manage=false (every other window): the model is global by default; picking a different one asks
//    "make this the global model?" — Yes persists it globally, No uses it only in this window.
const props = withDefaults(defineProps<{ manage?: boolean }>(), { manage: false });

const store = useAiSettingsStore();
const {
  selectedModel,
  modelSource,
  selectedProvider,
  globalModel,
  globalSource,
  globalProvider,
  cloudModels,
  availableModels,
  modelsLoading,
  ollamaRunning,
  providers,
  isOverridden,
} = storeToRefs(store);

// Which provider/list is shown — local UI state, only commits when a model is chosen.
const providerTab = ref<string>(selectedProvider.value);
watch(selectedProvider, (p) => (providerTab.value = p));
const sourceTab = ref<AiModelSource>(modelSource.value);
watch(modelSource, (s) => (sourceTab.value = s));

const sourceOptions = [
  { label: "Local", value: "local" },
  { label: "Cloud", value: "cloud" },
];

onMounted(() => {
  store.loadModels();
  store.loadProviders();
});

// --- custom (OpenAI-compatible) provider: probe its model list --------------------------------
const customModels = ref<string[]>([]);
const customLoading = ref(false);
const customError = ref<string | null>(null);
const customRunning = ref(false);
const manualModel = ref("");

async function probeCustom() {
  if (providerTab.value === OLLAMA_PROVIDER) return;
  customLoading.value = true;
  customError.value = null;
  try {
    const res = await store.listProviderModels(providerTab.value);
    customRunning.value = res.running;
    customModels.value = res.models;
    customError.value = res.error;
  } finally {
    customLoading.value = false;
  }
}
watch(providerTab, (p) => {
  if (p !== OLLAMA_PROVIDER) probeCustom();
});
const providerTabInfo = computed(() => providers.value.find((p) => p.id === providerTab.value) ?? null);

// --- choosing a model -------------------------------------------------------------------------
const confirmVisible = ref(false);
const pending = ref<{ model: string; source: AiModelSource; provider: string } | null>(null);

function chooseModel(model: string | null, source: AiModelSource, provider: string = providerTab.value) {
  const m = String(model || "").trim();
  if (!m) return;
  if (props.manage) {
    store.setGlobal(m, source, provider);
    return;
  }
  // Scoped window: same as global → just drop any override; different → ask to make it global.
  if (m === globalModel.value && source === globalSource.value && provider === globalProvider.value) {
    store.clearOverride();
    return;
  }
  pending.value = { model: m, source, provider };
  confirmVisible.value = true;
}
function makeGlobal() {
  if (pending.value) store.setGlobal(pending.value.model, pending.value.source, pending.value.provider);
  confirmVisible.value = false;
}
function useHereOnly() {
  if (pending.value) store.selectLocal(pending.value.model, pending.value.source, pending.value.provider);
  confirmVisible.value = false;
}

// --- cloud model management (manage mode) ------------------------------------------------------
const newCloud = ref("");
const editing = ref<string | null>(null);
const editValue = ref("");

function addCloud() {
  const n = newCloud.value.trim();
  if (!n) return;
  store.addCloudModel(n);
  newCloud.value = "";
}
function startEdit(name: string) {
  editing.value = name;
  editValue.value = name;
}
function commitEdit() {
  if (editing.value && editValue.value.trim()) store.renameCloudModel(editing.value, editValue.value.trim());
  editing.value = null;
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <!-- Provider (built-in Ollama + user-added endpoints) -->
    <Select
      v-if="providers.length > 1"
      v-model="providerTab"
      :options="providers"
      optionLabel="displayName"
      optionValue="id"
      class="w-full"
    >
      <template #option="{ option }">
        <span class="flex items-center gap-2">
          <i :class="option.builtIn ? 'pi pi-home' : 'pi pi-cloud'" class="text-surface-400 text-xs" />
          {{ option.displayName }}
        </span>
      </template>
    </Select>

    <!-- Custom OpenAI-compatible provider: its live model list + a manual model name -->
    <template v-if="providerTab !== 'ollama'">
      <div class="flex items-center gap-2">
        <Select
          :modelValue="selectedProvider === providerTab ? selectedModel : null"
          :options="customModels"
          :loading="customLoading"
          filter
          placeholder="Select model"
          class="grow"
          @update:modelValue="chooseModel($event, 'local')"
        >
          <template #empty>
            <div class="text-surface-500 text-sm p-2">
              {{ customError ? "Endpoint unreachable." : "No models listed — type a name below." }}
            </div>
          </template>
        </Select>
        <Button
          icon="pi pi-refresh"
          text
          rounded
          severity="secondary"
          :loading="customLoading"
          v-tooltip.top="'Reload models'"
          @click="probeCustom"
        />
      </div>
      <!-- Not every endpoint lists its models (or the list is huge) — allow typing the name directly. -->
      <div class="flex items-center gap-2">
        <InputText
          v-model="manualModel"
          placeholder="…or type a model name, e.g. anthropic/claude-sonnet-5"
          class="grow p-inputtext-sm"
          @keyup.enter="chooseModel(manualModel, 'local')"
        />
        <Button label="Use" size="small" :disabled="!manualModel.trim()" @click="chooseModel(manualModel, 'local')" />
      </div>
      <div class="flex items-center gap-2 text-xs">
        <span
          class="inline-block w-2 h-2 rounded-full"
          :class="customLoading ? 'bg-surface-300' : customRunning ? 'bg-emerald-500' : 'bg-red-500'"
        />
        <span class="text-surface-500 truncate">
          {{
            customLoading
              ? "Checking endpoint…"
              : customRunning
                ? `${providerTabInfo?.displayName ?? "Endpoint"} reachable · ${customModels.length} model(s)`
                : (customError ?? "Endpoint not reachable")
          }}
        </span>
      </div>
    </template>

    <!-- Built-in Ollama: local / cloud sources as before -->
    <SelectButton
      v-if="providerTab === 'ollama'"
      v-model="sourceTab"
      :options="sourceOptions"
      optionLabel="label"
      optionValue="value"
      :allowEmpty="false"
    />

    <!-- Local models -->
    <template v-if="providerTab === 'ollama' && sourceTab === 'local'">
      <div class="flex items-center gap-2">
        <Select
          :modelValue="modelSource === 'local' ? selectedModel : null"
          :options="availableModels"
          :loading="modelsLoading"
          placeholder="Select local model"
          class="grow"
          @update:modelValue="chooseModel($event, 'local')"
        />
        <Button
          icon="pi pi-refresh"
          text
          rounded
          severity="secondary"
          :loading="modelsLoading"
          v-tooltip.top="'Reload models'"
          @click="store.loadModels()"
        />
      </div>
      <!-- Ollama status -->
      <div class="flex items-center gap-2 text-xs">
        <span
          class="inline-block w-2 h-2 rounded-full"
          :class="ollamaRunning ? 'bg-emerald-500' : 'bg-red-500'"
        />
        <span class="text-surface-500">
          {{
            ollamaRunning
              ? `Ollama running · ${availableModels.length} model(s)`
              : "Ollama not reachable"
          }}
        </span>
      </div>
    </template>

    <!-- Cloud models -->
    <template v-else-if="providerTab === 'ollama'">
      <Select
        :modelValue="modelSource === 'cloud' ? selectedModel : null"
        :options="cloudModels"
        placeholder="Select cloud model"
        class="w-full"
        @update:modelValue="chooseModel($event, 'cloud')"
      >
        <template #empty>
          <div class="text-surface-500 text-sm p-2">
            {{ manage ? "Add a cloud model below." : "No saved cloud models — add them in Settings." }}
          </div>
        </template>
      </Select>

      <!-- manage: add / rename / delete saved cloud models -->
      <template v-if="manage">
        <div class="flex items-center gap-2">
          <InputText
            v-model="newCloud"
            placeholder="e.g. gemma4:31b-cloud"
            class="grow"
            @keyup.enter="addCloud"
          />
          <Button icon="pi pi-plus" label="Add" size="small" :disabled="!newCloud.trim()" @click="addCloud" />
        </div>
        <div v-if="cloudModels.length" class="flex flex-col gap-1">
          <div
            v-for="m in cloudModels"
            :key="m"
            class="flex items-center gap-2 px-2 py-1 rounded hover:bg-surface-100"
          >
            <template v-if="editing === m">
              <InputText v-model="editValue" class="grow p-inputtext-sm" @keyup.enter="commitEdit" />
              <Button icon="pi pi-check" text rounded size="small" @click="commitEdit" />
              <Button icon="pi pi-times" text rounded size="small" severity="secondary" @click="editing = null" />
            </template>
            <template v-else>
              <button type="button" class="grow text-left text-sm" @click="chooseModel(m, 'cloud')">
                {{ m }}
                <span v-if="globalModel === m" class="text-primary-600 text-xs">· global</span>
              </button>
              <Button icon="pi pi-pencil" text rounded size="small" severity="secondary" @click="startEdit(m)" />
              <Button
                icon="pi pi-trash"
                text
                rounded
                size="small"
                severity="danger"
                @click="store.deleteCloudModel(m)"
              />
            </template>
          </div>
        </div>
      </template>
    </template>

    <!-- Active model -->
    <div class="flex items-center gap-2 text-xs">
      <i class="pi pi-check-circle text-emerald-500" />
      <span class="text-surface-600">
        Active: <b>{{ selectedModel || "—" }}</b>
        <span v-if="selectedModel" class="text-surface-400">
          @ {{ providers.find((p) => p.id === selectedProvider)?.displayName ?? selectedProvider }}
        </span>
      </span>
      <Tag v-if="!manage && isOverridden" value="this window only" severity="warn" />
    </div>

    <!-- manage: the provider endpoints themselves (add / edit / key / test) -->
    <div v-if="manage" class="border-t border-surface-200 pt-3">
      <AiProvidersPanel />
    </div>

    <!-- Scoped: confirm making the pick global -->
    <Dialog
      v-model:visible="confirmVisible"
      modal
      dismissableMask
      header="Set global AI model?"
      :style="{ width: '26rem' }"
    >
      <p class="text-sm">
        Make <b>{{ pending?.model }}</b> the global AI model for all windows?
      </p>
      <template #footer>
        <Button label="Use here only" text severity="secondary" @click="useHereOnly" />
        <Button label="Make global" icon="pi pi-check" @click="makeGlobal" />
      </template>
    </Dialog>
  </div>
</template>
