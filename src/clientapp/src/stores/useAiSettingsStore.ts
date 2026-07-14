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
const PROVIDER_KEY = "ai-provider";

export type AiModelSource = "local" | "cloud";

/** Wire shape of AiGetProviders — API keys never reach the frontend, only hasKey. */
export interface AiProviderInfo {
  id: string;
  displayName: string;
  type: "ollama" | "openaiCompatible";
  baseUrl: string;
  hasKey: boolean;
  timeoutSeconds: number;
  builtIn: boolean;
}

export const OLLAMA_PROVIDER = "ollama";

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
  const globalProvider = ref<string>(localStorage.getItem(PROVIDER_KEY) || OLLAMA_PROVIDER);
  const cloudModels = ref<string[]>(readCloud());

  // --- per-window override (in-memory, not persisted) ---
  const overrideModel = ref<string | null>(null);
  const overrideSource = ref<AiModelSource | null>(null);
  const overrideProvider = ref<string | null>(null);

  // --- local Ollama probe ---
  const availableModels = ref<string[]>([]);
  const modelsLoading = ref(false);
  const ollamaRunning = ref(false);

  // --- configured providers (built-in Ollama + user-added OpenAI-compatible endpoints) ---
  const providers = ref<AiProviderInfo[]>([]);
  const providersLoading = ref(false);

  // Effective selection this window uses (override wins over the global value).
  const selectedModel = computed(() => overrideModel.value ?? globalModel.value);
  const modelSource = computed<AiModelSource>(() => overrideSource.value ?? globalSource.value);
  const selectedProvider = computed(() => overrideProvider.value ?? globalProvider.value);
  const selectedProviderInfo = computed(
    () => providers.value.find((p) => p.id === selectedProvider.value) ?? null,
  );
  const isOverridden = computed(
    () =>
      (overrideModel.value !== null && overrideModel.value !== globalModel.value) ||
      (overrideSource.value !== null && overrideSource.value !== globalSource.value) ||
      (overrideProvider.value !== null && overrideProvider.value !== globalProvider.value),
  );

  const aiEnabled = computed(() => {
    const model = String(selectedModel.value || "").trim();
    if (!model) return false;
    // Custom endpoints can't be validated cheaply (the model may not appear in /models) — trust the pick.
    if (selectedProvider.value !== OLLAMA_PROVIDER) return true;
    if (modelSource.value === "cloud") return true;
    return availableModels.value.includes(model);
  });

  function reloadGlobal() {
    globalModel.value = localStorage.getItem(MODEL_KEY);
    globalSource.value = (localStorage.getItem(SOURCE_KEY) as AiModelSource) || "local";
    globalProvider.value = localStorage.getItem(PROVIDER_KEY) || OLLAMA_PROVIDER;
    cloudModels.value = readCloud();
  }

  // Keep windows in sync: a `storage` event fires in the OTHER open windows when one writes localStorage.
  if (typeof window !== "undefined") {
    window.addEventListener("storage", (e) => {
      if (e.key === MODEL_KEY || e.key === SOURCE_KEY || e.key === CLOUD_KEY || e.key === PROVIDER_KEY)
        reloadGlobal();
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
  function setGlobal(
    model: string | null,
    source: AiModelSource = modelSource.value,
    provider: string = selectedProvider.value,
  ) {
    const normalized = String(model || "").trim() || null;
    globalModel.value = normalized;
    globalSource.value = source;
    globalProvider.value = provider;
    overrideModel.value = null;
    overrideSource.value = null;
    overrideProvider.value = null;
    try {
      if (normalized) localStorage.setItem(MODEL_KEY, normalized);
      else localStorage.removeItem(MODEL_KEY);
      localStorage.setItem(SOURCE_KEY, source);
      localStorage.setItem(PROVIDER_KEY, provider);
    } catch {
      /* best-effort */
    }
  }

  /** Picks a model for THIS window only (does not change the global model). */
  function selectLocal(
    model: string | null,
    source: AiModelSource = modelSource.value,
    provider: string = selectedProvider.value,
  ) {
    overrideModel.value = String(model || "").trim() || null;
    overrideSource.value = source;
    overrideProvider.value = provider;
  }

  function clearOverride() {
    overrideModel.value = null;
    overrideSource.value = null;
    overrideProvider.value = null;
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

  // --- providers (backend registry; keys stay host-side) ------------------------------------------
  async function loadProviders(): Promise<void> {
    providersLoading.value = true;
    try {
      const res = await invoke<{ providers: AiProviderInfo[] }>(Commands.AiGetProviders, null);
      providers.value = res?.providers ?? [];
      // The selected provider may have been deleted from another window — fall back to Ollama.
      if (providers.value.length && !providers.value.some((p) => p.id === selectedProvider.value))
        setGlobal(globalModel.value, globalSource.value, OLLAMA_PROVIDER);
    } catch (e) {
      console.error("Failed to load AI providers", e);
      providers.value = [];
    } finally {
      providersLoading.value = false;
    }
  }

  async function saveProvider(p: {
    id?: string | null;
    displayName: string;
    baseUrl: string;
    apiKey?: string | null; // null/undefined = keep stored key, "" = clear
    timeoutSeconds?: number | null;
  }): Promise<string | null> {
    const res = await invoke<{ providers: AiProviderInfo[]; error: string | null }>(
      Commands.AiSaveProvider,
      p,
    );
    if (res?.providers) providers.value = res.providers;
    return res?.error ?? null;
  }

  async function deleteProvider(id: string): Promise<void> {
    const res = await invoke<{ ok: boolean; providers: AiProviderInfo[] }>(Commands.AiDeleteProvider, { id });
    if (res?.providers) providers.value = res.providers;
    if (selectedProvider.value === id) setGlobal(globalModel.value, globalSource.value, OLLAMA_PROVIDER);
  }

  /** Models of one provider — also the "test connection" call (running=false → unreachable/rejected). */
  async function listProviderModels(
    providerId: string,
  ): Promise<{ running: boolean; models: string[]; error: string | null }> {
    try {
      const res = await invoke<{ running: boolean; models: string[] | null; error: string | null }>(
        Commands.AiGetModels,
        { providerId },
      );
      return { running: !!res?.running, models: res?.models ?? [], error: res?.error ?? null };
    } catch (e) {
      return { running: false, models: [], error: String((e as Error)?.message ?? e) };
    }
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
    globalProvider,
    cloudModels,
    availableModels,
    modelsLoading,
    ollamaRunning,
    providers,
    providersLoading,
    // effective
    selectedModel,
    modelSource,
    selectedProvider,
    selectedProviderInfo,
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
    loadProviders,
    saveProvider,
    deleteProvider,
    listProviderModels,
  };
});
