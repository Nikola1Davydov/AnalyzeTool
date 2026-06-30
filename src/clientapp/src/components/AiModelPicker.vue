<script setup lang="ts">
import { ref, watch, onMounted } from "vue";
import { storeToRefs } from "pinia";
import { useAiSettingsStore, type AiModelSource } from "@/stores/useAiSettingsStore";

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
  globalModel,
  globalSource,
  cloudModels,
  availableModels,
  modelsLoading,
  ollamaRunning,
  isOverridden,
} = storeToRefs(store);

// Which list (local / cloud) is shown — local UI state, only commits when a model is chosen.
const sourceTab = ref<AiModelSource>(modelSource.value);
watch(modelSource, (s) => (sourceTab.value = s));

const sourceOptions = [
  { label: "Local", value: "local" },
  { label: "Cloud", value: "cloud" },
];

onMounted(() => store.loadModels());

// --- choosing a model -------------------------------------------------------------------------
const confirmVisible = ref(false);
const pending = ref<{ model: string; source: AiModelSource } | null>(null);

function chooseModel(model: string | null, source: AiModelSource) {
  const m = String(model || "").trim();
  if (!m) return;
  if (props.manage) {
    store.setGlobal(m, source);
    return;
  }
  // Scoped window: same as global → just drop any override; different → ask to make it global.
  if (m === globalModel.value && source === globalSource.value) {
    store.clearOverride();
    return;
  }
  pending.value = { model: m, source };
  confirmVisible.value = true;
}
function makeGlobal() {
  if (pending.value) store.setGlobal(pending.value.model, pending.value.source);
  confirmVisible.value = false;
}
function useHereOnly() {
  if (pending.value) store.selectLocal(pending.value.model, pending.value.source);
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
    <!-- Source -->
    <SelectButton
      v-model="sourceTab"
      :options="sourceOptions"
      optionLabel="label"
      optionValue="value"
      :allowEmpty="false"
    />

    <!-- Local models -->
    <template v-if="sourceTab === 'local'">
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
    <template v-else>
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
      </span>
      <Tag v-if="!manage && isOverridden" value="this window only" severity="warn" />
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
