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

function loadOllamaModels() {
  aiSettingsStore.startLoadingModels();
  sendRequest(Commands.GetOllamaModels, null);
}
</script>

<template>
  <Drawer
    v-model:visible="visibleProxy"
    header="KI-Einstellungen"
    position="right"
    :modal="true"
    :style="{ width: '22rem' }"
  >
    <div class="ai-settings-panel">
      <div class="ai-settings-section">
        <div class="ai-settings-label">Ollama-Modell</div>

        <Button
          size="small"
          icon="pi pi-refresh"
          label="Modelle laden"
          :loading="aiSettingsStore.modelsLoading"
          class="mb-3"
          @click="loadOllamaModels"
        />

        <div
          v-if="!aiSettingsStore.modelsLoading && aiSettingsStore.availableModels.length === 0"
          class="ai-settings-empty"
        >
          <i class="pi pi-exclamation-triangle" />
          Ollama nicht verfügbar oder keine Modelle installiert. KI-Funktionen sind deaktiviert.
        </div>

        <Select
          v-else-if="aiSettingsStore.availableModels.length > 0"
          :options="aiSettingsStore.availableModels"
          :modelValue="aiSettingsStore.selectedModel"
          placeholder="Modell wählen…"
          class="w-full"
          @update:modelValue="aiSettingsStore.setModel($event)"
        />

        <div v-if="aiSettingsStore.selectedModel" class="ai-settings-active">
          <i class="pi pi-check-circle text-emerald-500" />
          Aktives Modell: <b>{{ aiSettingsStore.selectedModel }}</b>
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
