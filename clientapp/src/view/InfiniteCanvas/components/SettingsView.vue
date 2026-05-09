<script setup lang="ts">
import { computed } from "vue";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";
import { Commands, sendRequest } from "@/RevitBridge";

const props = defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const aiSettingsStore = useAiSettingsStore();

const visibleProxy = computed({
  get: () => props.visible,
  set: (value: boolean) => emit("update:visible", value),
});

const modelSourceProxy = computed({
  get: () => aiSettingsStore.modelSource,
  set: (value: "local" | "cloud") => aiSettingsStore.setModelSource(value),
});

const CLOUD_MODEL_SUGGESTIONS = [
  "kimi-k2.6:cloud",
  "glm-5.1:cloud",
  "qwen3.5:cloud",
  "nemotron-3-super:cloud",
  "gemma4:31b-cloud",
];

function loadOllamaModels() {
  aiSettingsStore.startLoadingModels();
  sendRequest(Commands.GetOllamaModels, null);
}
</script>

<template>
  <Drawer
    v-model:visible="visibleProxy"
    header="AI Settings"
    position="right"
    :modal="true"
    :style="{ width: '22rem' }"
  >
    <div class="ai-settings-panel">
      <div class="ai-settings-section">
        <div class="ai-settings-label">AI Model</div>

        <div class="ai-source-choice" role="radiogroup" aria-label="Model source">
          <label class="ai-source-option">
            <input type="radio" name="ai-model-source" value="local" v-model="modelSourceProxy" />
            <span>Local models</span>
          </label>
          <label class="ai-source-option">
            <input type="radio" name="ai-model-source" value="cloud" v-model="modelSourceProxy" />
            <span>Cloud models</span>
          </label>
        </div>

        <div v-if="aiSettingsStore.modelSource === 'cloud'" class="ai-cloud-section">
          <div class="ai-settings-label ai-settings-label--inline">Cloud model name</div>
          <InputText
            :modelValue="aiSettingsStore.selectedModel || ''"
            placeholder="e.g. gemma4:31b-cloud"
            class="w-full"
            @update:modelValue="aiSettingsStore.setModel(String($event || ''))"
          />
          <div class="ai-settings-label ai-settings-label--inline">Quick suggestions</div>
          <div class="ai-cloud-suggestions">
            <button
              v-for="model in CLOUD_MODEL_SUGGESTIONS"
              :key="model"
              type="button"
              class="ai-cloud-suggestion-chip"
              @click="aiSettingsStore.setModel(model)"
            >
              {{ model }}
            </button>
          </div>
          <div class="ai-settings-hint">
            Enter the exact model name expected by your cloud provider.
          </div>
        </div>

        <Button
          v-if="aiSettingsStore.modelSource === 'local'"
          size="small"
          icon="pi pi-refresh"
          label="Load models"
          :loading="aiSettingsStore.modelsLoading"
          class="mb-3"
          @click="loadOllamaModels"
        />

        <div
          v-if="
            aiSettingsStore.modelSource === 'local' &&
            !aiSettingsStore.modelsLoading &&
            aiSettingsStore.availableModels.length === 0
          "
          class="ai-settings-empty"
        >
          <i class="pi pi-exclamation-triangle" />
          Ollama is unavailable or no models are installed. AI features are disabled.
        </div>

        <Select
          v-else-if="
            aiSettingsStore.modelSource === 'local' && aiSettingsStore.availableModels.length > 0
          "
          :options="aiSettingsStore.availableModels"
          :modelValue="aiSettingsStore.selectedModel"
          placeholder="Select model..."
          class="w-full"
          @update:modelValue="aiSettingsStore.setModel($event)"
        />

        <div v-if="aiSettingsStore.selectedModel" class="ai-settings-active">
          <i class="pi pi-check-circle text-emerald-500" />
          Active model: <b>{{ aiSettingsStore.selectedModel }}</b>
        </div>
      </div>
    </div>
  </Drawer>
</template>

<style scoped>
.ai-settings-panel {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 0.25rem 0;
}

.ai-settings-section {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.ai-settings-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--p-surface-500);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 0.25rem;
}

.ai-settings-label--inline {
  margin-bottom: 0;
}

.ai-source-choice {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.45rem;
}

.ai-source-option {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  border: 1px solid var(--p-surface-300);
  border-radius: 0.55rem;
  padding: 0.45rem 0.55rem;
  font-size: 0.78rem;
  color: var(--p-surface-700);
  cursor: pointer;
  background: var(--p-surface-0);
}

.ai-source-option input[type="radio"] {
  accent-color: var(--p-primary-500);
}

.ai-cloud-section {
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
}

.ai-cloud-suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.ai-cloud-suggestion-chip {
  border: 1px solid var(--p-surface-300);
  background: var(--p-surface-0);
  color: var(--p-surface-700);
  border-radius: 999px;
  padding: 0.22rem 0.55rem;
  font-size: 0.68rem;
  line-height: 1.2;
  cursor: pointer;
}

.ai-cloud-suggestion-chip:hover {
  border-color: var(--p-primary-400);
  background: var(--p-primary-50);
  color: var(--p-primary-700);
}

.ai-settings-hint {
  font-size: 0.72rem;
  color: var(--p-surface-500);
}

.ai-settings-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  padding: 1rem;
  text-align: center;
  font-size: 0.8rem;
  color: var(--p-surface-500);
  background: var(--p-surface-100);
  border-radius: 0.5rem;
}

.ai-settings-active {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.75rem;
  color: var(--p-surface-600);
  margin-top: 0.25rem;
}
</style>
