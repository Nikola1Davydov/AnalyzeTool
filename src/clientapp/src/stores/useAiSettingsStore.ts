import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { Commands, invoke } from "@/RevitBridge";

// AI model settings live in the WebView's localStorage, which is shared across every plugin window (same
// WebView2 profile + origin). So the selected model is GLOBAL: set it once and all windows use it. A
// `storage` event keeps already-open windows in sync. Each window may also hold a temporary per-window
// OVERRIDE (used when a sub-window picks a different model but declines to make it global).

const MODEL_KEY = "ollama-model";
const SOURCE_KEY = "ai-model-source";
const CLOUD_KEY = "ai-cloud-models";

export type AiModelSource = "local" | "cloud";

function readCloud(): string[] {
  try {
    const raw = localStorage.getItem(CLOUD_KEY);
    const arr = raw ? JSON.parse(raw) : [];
    return Array.isArray(arr) ? arr.map((m) => String(m)) : [];
  } catch {
    return [];
  }
}

export const useAiSettingsStore = defineStore("ai-settings", () => {
  // --- global (persisted, shared across windows) ---
  const globalModel = ref<string | null>(localStorage.getItem(MODEL_KEY));
  const globalSource = ref<AiModelSource>((localStorage.getItem(SOURCE_KEY) as AiModelSource) || "local");
  const cloudModels = ref<string[]>(readCloud());

  // --- per-window override (in-memory, not persisted) ---
  const overrideModel = ref<string | null>(null);
  const overrideSource = ref<AiModelSource | null>(null);

  // --- local Ollama probe ---
  const availableModels = ref<string[]>([]);
  const modelsLoading = ref(false);
  const ollamaRunning = ref(false);

  // Effective selection this window uses (override wins over the global value).
  const selectedModel = computed(() => overrideModel.value ?? globalModel.value);
  const modelSource = computed<AiModelSource>(() => overrideSource.value ?? globalSource.value);
  const isOverridden = computed(
    () =>
      (overrideModel.value !== null && overrideModel.value !== globalModel.value) ||
      (overrideSource.value !== null && overrideSource.value !== globalSource.value),
  );

  const aiEnabled = computed(() => {
    const model = String(selectedModel.value || "").trim();
    if (!model) return false;
    if (modelSource.value === "cloud") return true;
    return availableModels.value.includes(model);
  });

  function reloadGlobal() {
    globalModel.value = localStorage.getItem(MODEL_KEY);
    globalSource.value = (localStorage.getItem(SOURCE_KEY) as AiModelSource) || "local";
    cloudModels.value = readCloud();
  }

  // Keep windows in sync: a `storage` event fires in the OTHER open windows when one writes localStorage.
  if (typeof window !== "undefined") {
    window.addEventListener("storage", (e) => {
      if (e.key === MODEL_KEY || e.key === SOURCE_KEY || e.key === CLOUD_KEY) reloadGlobal();
    });
  }

  function persistCloud() {
    try {
      localStorage.setItem(CLOUD_KEY, JSON.stringify(cloudModels.value));
    } catch {
      /* best-effort */
    }
  }

  /** Sets the GLOBAL model (persisted, shared) and clears any per-window override. */
  function setGlobal(model: string | null, source: AiModelSource = modelSource.value) {
    const normalized = String(model || "").trim() || null;
    globalModel.value = normalized;
    globalSource.value = source;
    overrideModel.value = null;
    overrideSource.value = null;
    try {
      if (normalized) localStorage.setItem(MODEL_KEY, normalized);
      else localStorage.removeItem(MODEL_KEY);
      localStorage.setItem(SOURCE_KEY, source);
    } catch {
      /* best-effort */
    }
  }

  /** Picks a model for THIS window only (does not change the global model). */
  function selectLocal(model: string | null, source: AiModelSource = modelSource.value) {
    overrideModel.value = String(model || "").trim() || null;
    overrideSource.value = source;
  }

  function clearOverride() {
    overrideModel.value = null;
    overrideSource.value = null;
  }

  // --- saved cloud models (persisted list, manageable in Settings) ---
  function addCloudModel(name: string) {
    const n = String(name || "").trim();
    if (!n || cloudModels.value.includes(n)) return;
    cloudModels.value = [...cloudModels.value, n];
    persistCloud();
  }
  function renameCloudModel(oldName: string, newName: string) {
    const n = String(newName || "").trim();
    if (!n) return;
    cloudModels.value = cloudModels.value.map((m) => (m === oldName ? n : m));
    persistCloud();
    if (globalModel.value === oldName) setGlobal(n, "cloud");
  }
  function deleteCloudModel(name: string) {
    cloudModels.value = cloudModels.value.filter((m) => m !== name);
    persistCloud();
  }

  async function loadModels(): Promise<void> {
    modelsLoading.value = true;
    try {
      const res = await invoke<{ running: boolean; models: string[] | null }>(
        Commands.OllamaGetModels,
        null,
      );
      ollamaRunning.value = !!res?.running;
      availableModels.value = Array.isArray(res?.models) ? res!.models!.map((m) => String(m)) : [];
    } catch (err) {
      console.error("Failed to load Ollama models", err);
      ollamaRunning.value = false;
      availableModels.value = [];
    } finally {
      modelsLoading.value = false;
    }
  }

  return {
    // state
    globalModel,
    globalSource,
    cloudModels,
    availableModels,
    modelsLoading,
    ollamaRunning,
    // effective
    selectedModel,
    modelSource,
    isOverridden,
    aiEnabled,
    // actions
    reloadGlobal,
    setGlobal,
    selectLocal,
    clearOverride,
    addCloudModel,
    renameCloudModel,
    deleteCloudModel,
    loadModels,
  };
});
