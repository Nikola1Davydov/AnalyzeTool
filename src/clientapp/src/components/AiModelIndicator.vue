<script setup lang="ts">
import { onMounted } from "vue";
import { storeToRefs } from "pinia";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";

// Read-only AI status for non-settings windows: shows which global model is active and whether Ollama is
// running. Model selection lives only in the Settings window.
const store = useAiSettingsStore();
const { selectedModel, modelSource, selectedProvider, selectedProviderInfo, ollamaRunning } =
  storeToRefs(store);

onMounted(() => {
  store.loadModels();
  store.loadProviders();
});
</script>

<template>
  <div class="flex items-center gap-2 text-xs flex-wrap">
    <!-- The reachability dot is only meaningful for the local Ollama; custom endpoints are probed in
         Settings (Test connection), not from every window. -->
    <span
      class="inline-block w-2 h-2 rounded-full shrink-0"
      :class="selectedProvider !== 'ollama' ? 'bg-sky-500' : ollamaRunning ? 'bg-emerald-500' : 'bg-red-500'"
    />
    <span class="text-surface-600">
      <template v-if="selectedModel">
        Model: <b>{{ selectedModel }}</b>
        <span class="text-surface-400">
          ({{ selectedProvider === "ollama" ? modelSource : (selectedProviderInfo?.displayName ?? selectedProvider) }})
        </span>
      </template>
      <template v-else>No AI model selected</template>
    </span>
    <span v-if="selectedProvider === 'ollama' && !ollamaRunning && modelSource === 'local'" class="text-red-500">
      · Ollama not reachable
    </span>
  </div>
</template>
