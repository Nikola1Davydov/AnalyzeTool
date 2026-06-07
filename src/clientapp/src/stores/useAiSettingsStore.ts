import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { Commands, invoke } from "@/RevitBridge";

const STORAGE_KEY = "ollama-model";
const STORAGE_SOURCE_KEY = "ai-model-source";

export type AiModelSource = "local" | "cloud";

export const useAiSettingsStore = defineStore("ai-settings", () => {
  const selectedModel = ref<string | null>(localStorage.getItem(STORAGE_KEY));
  const modelSource = ref<AiModelSource>(
    (localStorage.getItem(STORAGE_SOURCE_KEY) as AiModelSource) || "local",
  );
  const availableModels = ref<string[]>([]);
  const modelsLoading = ref(false);
  const aiEnabled = computed(() => {
    const model = String(selectedModel.value || "").trim();
    if (!model) return false;
    if (modelSource.value === "cloud") return true;
    return availableModels.value.includes(model);
  });

  function setModel(model: string | null) {
    const normalized = String(model || "").trim();
    selectedModel.value = normalized || null;
    if (normalized) localStorage.setItem(STORAGE_KEY, normalized);
    else localStorage.removeItem(STORAGE_KEY);
  }

  function setModelSource(source: AiModelSource) {
    modelSource.value = source;
    localStorage.setItem(STORAGE_SOURCE_KEY, source);

    if (
      source === "local" &&
      selectedModel.value &&
      !availableModels.value.includes(selectedModel.value)
    ) {
      setModel(null);
    }
  }

  function startLoadingModels() {
    modelsLoading.value = true;
    availableModels.value = [];
  }

  function setAvailableModels(models: string[]) {
    availableModels.value = models;
    modelsLoading.value = false;

    if (
      modelSource.value === "local" &&
      selectedModel.value &&
      !models.includes(selectedModel.value)
    ) {
      setModel(null);
    }
  }

  async function loadModels(): Promise<void> {
    startLoadingModels();
    try {
      const models = await invoke<unknown>(Commands.GetOllamaModels, null);
      setAvailableModels(Array.isArray(models) ? models.map((m) => String(m)) : []);
    } catch (err) {
      console.error("Failed to load Ollama models", err);
      setAvailableModels([]);
    }
  }

  return {
    selectedModel,
    modelSource,
    availableModels,
    modelsLoading,
    aiEnabled,
    setModel,
    setModelSource,
    startLoadingModels,
    setAvailableModels,
    loadModels,
  };
});
