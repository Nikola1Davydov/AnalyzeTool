import { defineStore } from "pinia";
import { ref } from "vue";

const STORAGE_KEY = "ollama-model";

export const useAiSettingsStore = defineStore("ai-settings", () => {
  const selectedModel = ref<string | null>(localStorage.getItem(STORAGE_KEY));
  const availableModels = ref<string[]>([]);
  const modelsLoading = ref(false);

  function setModel(model: string | null) {
    selectedModel.value = model;
    if (model) localStorage.setItem(STORAGE_KEY, model);
    else localStorage.removeItem(STORAGE_KEY);
  }

  return { selectedModel, availableModels, modelsLoading, setModel };
});
