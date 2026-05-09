import { defineStore } from "pinia";
import { computed, ref } from "vue";

const STORAGE_KEY = "ollama-model";

export const useAiSettingsStore = defineStore("ai-settings", () => {
  const selectedModel = ref<string | null>(localStorage.getItem(STORAGE_KEY));
  const availableModels = ref<string[]>([]);
  const modelsLoading = ref(false);
  const aiEnabled = computed(
    () => !!selectedModel.value && availableModels.value.includes(selectedModel.value),
  );

  function setModel(model: string | null) {
    selectedModel.value = model;
    if (model) localStorage.setItem(STORAGE_KEY, model);
    else localStorage.removeItem(STORAGE_KEY);
  }

  function startLoadingModels() {
    modelsLoading.value = true;
    availableModels.value = [];
  }

  function setAvailableModels(models: string[]) {
    availableModels.value = models;
    modelsLoading.value = false;

    if (selectedModel.value && !models.includes(selectedModel.value)) {
      setModel(null);
    }
  }

  return {
    selectedModel,
    availableModels,
    modelsLoading,
    aiEnabled,
    setModel,
    startLoadingModels,
    setAvailableModels,
  };
});
