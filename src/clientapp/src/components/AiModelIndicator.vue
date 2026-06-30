<script setup lang="ts">
import { onMounted } from "vue";
import { storeToRefs } from "pinia";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";

// Read-only AI status for non-settings windows: shows which global model is active and whether Ollama is
// running. Model selection lives only in the Settings window.
const store = useAiSettingsStore();
const { selectedModel, modelSource, ollamaRunning } = storeToRefs(store);

onMounted(() => store.loadModels());
</script>

<template>
  <div class="flex items-center gap-2 text-xs flex-wrap">
    <span
      class="inline-block w-2 h-2 rounded-full shrink-0"
      :class="ollamaRunning ? 'bg-emerald-500' : 'bg-red-500'"
    />
    <span class="text-surface-600">
      <template v-if="selectedModel">
        Model: <b>{{ selectedModel }}</b>
        <span class="text-surface-400">({{ modelSource }})</span>
      </template>
      <template v-else>No AI model selected</template>
    </span>
    <span v-if="!ollamaRunning && modelSource === 'local'" class="text-red-500">
      · Ollama not reachable
    </span>
  </div>
</template>
