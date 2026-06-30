<script setup lang="ts">
import { ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { useToast } from "primevue/usetoast";
import { Commands, invoke } from "@/RevitBridge";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";
import AiModelIndicator from "@/components/AiModelIndicator.vue";

// Shared Rename dialog for families and family types. Plain mode = type a name. AI mode reuses the same
// Ollama settings as the rest of the app (useAiSettingsStore) and the OllamaSuggestName command: write an
// instruction ("translate to English", "add prefix WALL_"…) and the model fills in the new name, which
// you can still edit before applying.
const visible = defineModel<boolean>("visible", { required: true });
const props = withDefaults(
  defineProps<{
    title?: string;
    label?: string;
    currentName: string;
    context?: string; // e.g. category — helps the model
    note?: string | null; // e.g. "renames N merged types"
  }>(),
  { title: "Rename", label: "New name", context: "", note: null },
);
const emit = defineEmits<{ submit: [name: string] }>();

const toast = useToast();
const aiStore = useAiSettingsStore();
const { selectedModel, aiEnabled } = storeToRefs(aiStore);

const value = ref(props.currentName);
const aiMode = ref(false);
const prompt = ref("");
const suggesting = ref(false);

watch(visible, (open) => {
  if (open) value.value = props.currentName;
});

async function suggest() {
  if (!selectedModel.value) {
    toast.add({ severity: "warn", summary: "No AI model", detail: "Pick a model first.", life: 3000 });
    return;
  }
  if (!prompt.value.trim()) return;
  suggesting.value = true;
  try {
    const res = await invoke<{ name: string | null; error: string | null }>(Commands.OllamaSuggestName, {
      model: selectedModel.value,
      prompt: prompt.value,
      currentName: value.value,
      context: props.context,
    });
    if (res?.error) {
      toast.add({ severity: "error", summary: "AI failed", detail: res.error, life: 4000 });
      return;
    }
    if (res?.name) value.value = res.name;
  } catch (e) {
    toast.add({ severity: "error", summary: "AI failed", detail: String((e as Error)?.message ?? e), life: 4000 });
  } finally {
    suggesting.value = false;
  }
}

function submit() {
  const v = value.value.trim();
  if (!v) return;
  emit("submit", v);
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    dismissableMask
    :header="title"
    :style="{ width: '30rem', maxWidth: '95vw' }"
  >
    <div class="flex flex-col gap-3">
      <p v-if="note" class="text-xs text-amber-600">{{ note }}</p>

      <!-- Current name (read-only reference, stays visible after editing / AI suggestion) -->
      <div class="flex flex-col gap-1">
        <span class="text-xs text-surface-500">Current name</span>
        <span class="text-sm font-medium break-all">{{ currentName || "—" }}</span>
      </div>

      <div class="flex flex-col gap-1">
        <label class="text-xs text-surface-500">{{ label }}</label>
        <InputText v-model="value" class="w-full" @keyup.enter="submit" />
      </div>

      <!-- AI mode toggle -->
      <label class="flex items-center gap-2 text-sm cursor-pointer">
        <Checkbox v-model="aiMode" binary />
        <i class="pi pi-sparkles text-primary-500" />
        Rename with AI
      </label>

      <!-- AI section -->
      <div v-if="aiMode" class="rounded-lg border border-surface-200 bg-surface-50 p-3 flex flex-col gap-2">
        <AiModelIndicator />

        <textarea
          v-model="prompt"
          rows="2"
          placeholder="What should the new name be? e.g. translate to English, add prefix WALL_"
          class="w-full text-sm rounded-md border border-surface-300 bg-surface-0 p-2 resize-y"
          @keydown.enter.exact.prevent="suggest"
        />
        <Button
          :icon="suggesting ? 'pi pi-spin pi-spinner' : 'pi pi-sparkles'"
          label="Suggest name"
          size="small"
          class="self-start"
          :disabled="!aiEnabled || suggesting || !prompt.trim()"
          @click="suggest"
        />
        <p v-if="!aiEnabled" class="text-xs text-surface-400">
          No usable AI model — choose one in the Settings window.
        </p>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" text severity="secondary" @click="visible = false" />
      <Button
        label="Rename"
        icon="pi pi-check"
        :disabled="!value.trim() || value === currentName"
        @click="submit"
      />
    </template>
  </Dialog>
</template>
