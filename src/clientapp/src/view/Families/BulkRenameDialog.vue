<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { storeToRefs } from "pinia";
import { useToast } from "primevue/usetoast";
import { Commands, invoke } from "@/RevitBridge";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";
import AiModelIndicator from "@/components/AiModelIndicator.vue";

// Bulk rename with a mandatory REVIEW step: the AI fills the "new name" column in ONE batch call
// (consistent scheme across the list), every row stays editable, conflicting rows are flagged, and
// only checked+changed+valid rows are applied — sequentially, with progress. Reused for families and
// (later) types via the applyOne prop, so the dialog knows nothing about specific rename commands.

export interface BulkRenameItem {
  id: number;
  name: string;
  context?: string; // e.g. category — passed to the model
}

interface Row {
  id: number;
  oldName: string;
  context?: string;
  newName: string;
  include: boolean;
  error?: string; // apply failure, shown inline after a partial run
}

const visible = defineModel<boolean>("visible", { required: true });
const props = defineProps<{
  items: BulkRenameItem[];
  /** Every name currently taken in the project (collision check), including the items themselves. */
  takenNames: string[];
  /** Applies a single rename (quiet — the dialog reports the summary). */
  applyOne: (id: number, newName: string) => Promise<{ ok: boolean; error?: string }>;
}>();
const emit = defineEmits<{ applied: [renamed: number] }>();

const toast = useToast();
const aiStore = useAiSettingsStore();
const { selectedModel, aiEnabled } = storeToRefs(aiStore);

// The naming convention is a per-office constant — persist it so it never has to be retyped.
const PROMPT_KEY = "analysetool.bulkRenamePrompt.v1";
const prompt = ref(localStorage.getItem(PROMPT_KEY) ?? "");
watch(prompt, (v) => localStorage.setItem(PROMPT_KEY, v));

const rows = ref<Row[]>([]);
watch(visible, (open) => {
  if (open)
    rows.value = props.items.map((i) => ({
      id: i.id,
      oldName: i.name,
      context: i.context,
      newName: i.name,
      include: true,
    }));
});

// ---- validation ---------------------------------------------------------------------------------
// A row is applied when: included + non-empty + actually changed + no conflict. Conflicts: duplicate
// new names within the batch, or a name already taken by an element OUTSIDE the batch (a name being
// renamed away inside the batch is fine only if its owner really gets renamed — too order-dependent
// to promise, so batch-internal old names still count as taken unless that row changes).
const conflicts = computed<Set<number>>(() => {
  const bad = new Set<number>();

  // Names taken outside this batch (the batch's own old names are being renamed away).
  const taken = new Set(props.takenNames.map((n) => n.toLowerCase()));
  for (const r of rows.value) taken.delete(r.oldName.toLowerCase());

  const seen = new Map<string, number>(); // new name -> first row id
  for (const r of rows.value) {
    if (!r.include) continue;
    const name = r.newName.trim().toLowerCase();
    if (!name || name === r.oldName.trim().toLowerCase()) continue;
    if (taken.has(name)) bad.add(r.id);
    const first = seen.get(name);
    if (first !== undefined) {
      bad.add(r.id);
      bad.add(first);
    } else seen.set(name, r.id);
  }
  return bad;
});

function isChanged(r: Row): boolean {
  const v = r.newName.trim();
  return v.length > 0 && v !== r.oldName;
}
const applicable = computed(() =>
  rows.value.filter((r) => r.include && isChanged(r) && !conflicts.value.has(r.id)),
);

// ---- AI generate (one batch round-trip) ---------------------------------------------------------
const generating = ref(false);
async function generate() {
  if (!selectedModel.value) {
    toast.add({ severity: "warn", summary: "No AI model", detail: "Pick a model first.", life: 3000 });
    return;
  }
  if (!prompt.value.trim()) return;
  generating.value = true;
  try {
    const res = await invoke<{ suggestions: { id: number; name: string }[]; error: string | null }>(
      Commands.OllamaSuggestNames,
      {
        model: selectedModel.value,
        prompt: prompt.value,
        items: rows.value.map((r) => ({ id: r.id, currentName: r.oldName, context: r.context ?? "" })),
      },
    );
    if (res?.error) {
      toast.add({ severity: "error", summary: "AI failed", detail: res.error, life: 4000 });
      return;
    }
    const byId = new Map((res?.suggestions ?? []).map((s) => [s.id, s.name]));
    let filled = 0;
    for (const r of rows.value) {
      const name = byId.get(r.id);
      if (name) {
        r.newName = name;
        filled++;
      }
    }
    if (filled < rows.value.length)
      toast.add({
        severity: "warn",
        summary: "Partial answer",
        detail: `The model suggested ${filled} of ${rows.value.length} names — the rest are unchanged.`,
        life: 4000,
      });
  } catch (e) {
    toast.add({ severity: "error", summary: "AI failed", detail: String((e as Error)?.message ?? e), life: 4000 });
  } finally {
    generating.value = false;
  }
}

// ---- apply --------------------------------------------------------------------------------------
const applying = ref(false);
const progress = ref(0); // 0..100

async function apply() {
  const targets = applicable.value;
  if (!targets.length) return;

  applying.value = true;
  progress.value = 0;
  let done = 0;
  let failed = 0;

  for (let i = 0; i < targets.length; i++) {
    const r = targets[i];
    const res = await props.applyOne(r.id, r.newName.trim());
    if (res.ok) {
      done++;
      r.error = undefined;
    } else {
      failed++;
      r.error = res.error ?? "Rename failed";
    }
    progress.value = Math.round(((i + 1) / targets.length) * 100);
  }

  applying.value = false;

  if (failed === 0) {
    toast.add({ severity: "success", summary: "Renamed", detail: `${done} element(s) renamed.`, life: 3000 });
    visible.value = false;
  } else {
    // Keep the dialog open so the per-row errors are visible.
    toast.add({
      severity: "warn",
      summary: `Renamed ${done}, failed ${failed}`,
      detail: "Failed rows are marked below.",
      life: 5000,
    });
  }
  if (done > 0) emit("applied", done);
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :closable="!applying"
    :dismissableMask="!applying"
    header="Rename with AI"
    :style="{ width: 'min(46rem, 95vw)' }"
  >
    <div class="flex flex-col gap-3">
      <!-- Instruction + generate -->
      <div class="rounded-lg border border-surface-200 bg-surface-50 p-3 flex flex-col gap-2">
        <AiModelIndicator />
        <textarea
          v-model="prompt"
          rows="2"
          placeholder="Naming convention for the whole list, e.g. 'prefix by category, German abbreviations: Möb_…'"
          class="w-full text-sm rounded-md border border-surface-300 bg-surface-0 p-2 resize-y"
          @keydown.enter.exact.prevent="generate"
        />
        <div class="flex items-center gap-2">
          <Button
            :icon="generating ? 'pi pi-spin pi-spinner' : 'pi pi-sparkles'"
            :label="generating ? 'Generating…' : 'Generate names'"
            size="small"
            :disabled="!aiEnabled || generating || applying || !prompt.trim()"
            @click="generate"
          />
          <span class="text-xs text-surface-400">One request for the whole list — review before applying.</span>
        </div>
        <p v-if="!aiEnabled" class="text-xs text-surface-400">
          No usable AI model — choose one in the Settings window.
        </p>
      </div>

      <!-- Review table -->
      <div class="max-h-[45vh] overflow-y-auto rounded-lg border border-surface-200">
        <div
          v-for="r in rows"
          :key="r.id"
          class="flex items-center gap-2 px-2 py-1.5 border-b border-surface-100 last:border-b-0"
          :class="{ 'opacity-50': !r.include }"
        >
          <Checkbox v-model="r.include" binary :disabled="applying" />
          <span class="text-sm truncate basis-1/3 shrink-0" :title="r.oldName">{{ r.oldName }}</span>
          <i class="pi pi-arrow-right text-xs text-surface-400 shrink-0" />
          <div class="grow min-w-0 flex items-center gap-1.5">
            <InputText
              v-model="r.newName"
              class="w-full"
              size="small"
              :disabled="!r.include || applying"
              :invalid="conflicts.has(r.id)"
            />
            <i
              v-if="conflicts.has(r.id)"
              class="pi pi-exclamation-triangle text-amber-500 shrink-0"
              v-tooltip.left="'Name conflict — already taken or duplicated in this list'"
            />
            <i
              v-else-if="r.error"
              class="pi pi-times-circle text-red-500 shrink-0"
              v-tooltip.left="r.error"
            />
            <i
              v-else-if="r.include && isChanged(r)"
              class="pi pi-check text-green-500 text-xs shrink-0"
            />
          </div>
        </div>
      </div>

      <ProgressBar v-if="applying" :value="progress" class="h-2" :showValue="false" />
    </div>

    <template #footer>
      <span class="text-xs text-surface-500 mr-auto self-center">
        {{ applicable.length }} of {{ rows.length }} will be renamed
      </span>
      <Button label="Cancel" text severity="secondary" :disabled="applying" @click="visible = false" />
      <Button
        :label="applying ? 'Renaming…' : `Apply ${applicable.length} rename(s)`"
        icon="pi pi-check"
        :disabled="!applicable.length || applying || generating"
        @click="apply"
      />
    </template>
  </Dialog>
</template>
