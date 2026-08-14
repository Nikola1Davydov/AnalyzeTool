<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { storeToRefs } from "pinia";
import { useToast } from "primevue/usetoast";
import { Commands, invoke } from "@/RevitBridge";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";
import AiModelIndicator from "@/components/AiModelIndicator.vue";
import { applyTemplate, type NamingContext } from "./namingEngine";
import { useNamingRules, type NamingScope } from "./namingRules";

// Naming-rule builder: edit a template with insertable tokens, maintain the shared abbreviation
// dictionary, and watch a LIVE preview against real elements from the current selection — the rule is
// saved as data and applied deterministically, so what the preview shows is exactly what apply does.

const visible = defineModel<boolean>("visible", { required: true });
const props = defineProps<{
  scope: NamingScope;
  /** Parameters across the SELECTED elements; common=false means not every element carries it. */
  availableParams: { name: string; common: boolean }[];
  /** A few real contexts from the current selection for the live preview. */
  sampleContexts: NamingContext[];
}>();

const toast = useToast();
const naming = useNamingRules();

// ---- rule being edited ---------------------------------------------------------------------------
const ruleId = ref<string | null>(null);
const ruleName = ref("");
const template = ref("");

const scopeRules = computed(() => naming.rulesFor(props.scope));

function startNew() {
  ruleId.value = null;
  ruleName.value = "";
  template.value = props.scope === "type" ? "{param:Width}x{param:Height}" : "{category|abbr}_{name|clean}";
}
function edit(rule: { id: string; name: string; template: string }) {
  ruleId.value = rule.id;
  ruleName.value = rule.name;
  template.value = rule.template;
}
watch(visible, (open) => {
  if (!open) return;
  const first = scopeRules.value[0];
  first ? edit(first) : startNew();
});

function save() {
  if (!ruleName.value.trim() || !template.value.trim()) return;
  const id = ruleId.value ?? naming.newId();
  naming.saveRule({ id, name: ruleName.value.trim(), scope: props.scope, template: template.value.trim() });
  ruleId.value = id;
  toast.add({ severity: "success", summary: "Rule saved", detail: ruleName.value, life: 2000 });
}
function remove() {
  if (!ruleId.value) return;
  naming.deleteRule(ruleId.value);
  const next = scopeRules.value[0];
  next ? edit(next) : startNew();
}

// ---- token insertion -----------------------------------------------------------------------------
const templateInput = ref<{ $el?: HTMLElement } | null>(null);
const baseTokens = computed(() => [
  { label: "Category (abbr)", token: "{category|abbr}" },
  { label: "Family name", token: "{family}" },
  { label: "Current name (clean)", token: "{name|clean}" },
]);
function insertToken(token: string) {
  const el = templateInput.value?.$el as HTMLInputElement | undefined;
  const input = el?.tagName === "INPUT" ? el : el?.querySelector?.("input");
  if (input && typeof input.selectionStart === "number") {
    const pos = input.selectionStart;
    template.value = template.value.slice(0, pos) + token + template.value.slice(input.selectionEnd ?? pos);
  } else {
    template.value += token;
  }
}
const paramToken = ref<string | null>(null);
watch(paramToken, (p) => {
  if (!p) return;
  insertToken(`{param:${p}}`);
  paramToken.value = null;
});

// ---- AI: author the template from ONE example ------------------------------------------------------
// The user types the name they WANT for a sample element; the model matches its fragments against the
// element's real data and returns a template + implied abbreviations. The AI authors an editable rule —
// the live preview below immediately shows what it does; applying stays deterministic.
const aiStore = useAiSettingsStore();
const { selectedModel, selectedProvider, aiEnabled } = storeToRefs(aiStore);
const example = ref("");
const sampleIndex = ref(0);
const inferring = ref(false);
const sampleOptions = computed(() => props.sampleContexts.map((c, i) => ({ label: c.name, value: i })));
watch(visible, (open) => {
  if (open) sampleIndex.value = 0;
});

async function inferTemplate() {
  const ctx = props.sampleContexts[sampleIndex.value];
  if (!ctx || !example.value.trim()) return;
  if (!selectedModel.value) {
    toast.add({ severity: "warn", summary: "No AI model", detail: "Pick a model first.", life: 3000 });
    return;
  }
  inferring.value = true;
  try {
    const res = await invoke<{
      template: string | null;
      abbreviations: { full: string; abbr: string }[];
      error: string | null;
    }>(Commands.OllamaSuggestTemplate, {
      model: selectedModel.value,
      provider: selectedProvider.value,
      example: example.value.trim(),
      name: ctx.name,
      family: ctx.family,
      category: ctx.category,
      parameters: ctx.params,
    });
    if (res?.error || !res?.template) {
      toast.add({ severity: "error", summary: "AI failed", detail: res?.error ?? "No template returned.", life: 4000 });
      return;
    }
    template.value = res.template;
    let added = 0;
    for (const a of res.abbreviations ?? []) {
      naming.setAbbreviation(a.full, a.abbr);
      added++;
    }
    if (!ruleName.value.trim()) ruleName.value = `From example: ${example.value.trim()}`;
    toast.add({
      severity: "success",
      summary: "Template inferred",
      detail: added ? `${added} abbreviation(s) added to the dictionary — check the preview.` : "Check the preview.",
      life: 3500,
    });
  } catch (e) {
    toast.add({ severity: "error", summary: "AI failed", detail: String((e as Error)?.message ?? e), life: 4000 });
  } finally {
    inferring.value = false;
  }
}

// ---- live preview --------------------------------------------------------------------------------
const preview = computed(() =>
  props.sampleContexts.slice(0, 5).map((ctx) => ({
    old: ctx.name,
    next: applyTemplate(template.value, ctx, naming.store.abbreviations),
  })),
);

// ---- abbreviation dictionary ---------------------------------------------------------------------
const dictOpen = ref(false);
const newFull = ref("");
const newAbbr = ref("");
const dictEntries = computed(() =>
  Object.entries(naming.store.abbreviations).sort((a, b) => a[0].localeCompare(b[0])),
);
function addAbbr() {
  if (!newFull.value.trim() || !newAbbr.value.trim()) return;
  naming.setAbbreviation(newFull.value, newAbbr.value);
  newFull.value = "";
  newAbbr.value = "";
}

// ---- share (export / import the whole convention) -------------------------------------------------
async function exportConvention() {
  try {
    await navigator.clipboard.writeText(naming.exportJson());
    toast.add({ severity: "success", summary: "Copied", detail: "Convention JSON is in the clipboard.", life: 2500 });
  } catch {
    toast.add({ severity: "error", summary: "Copy failed", life: 2500 });
  }
}
async function importConvention() {
  try {
    const text = await navigator.clipboard.readText();
    const res = naming.importJson(text);
    toast.add({
      severity: "success",
      summary: "Imported",
      detail: `${res.rules} rule(s), ${res.abbreviations} abbreviation(s).`,
      life: 3000,
    });
  } catch {
    toast.add({ severity: "error", summary: "Import failed", detail: "Clipboard has no valid convention JSON.", life: 3500 });
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    dismissableMask
    header="Naming rules"
    :style="{ width: 'min(44rem, 95vw)' }"
  >
    <div class="flex flex-col gap-3">
      <!-- Saved rules of this scope -->
      <div class="flex items-center gap-2 flex-wrap">
        <Tag
          v-for="r in scopeRules"
          :key="r.id"
          :value="r.name"
          :severity="r.id === ruleId ? 'primary' : 'secondary'"
          class="cursor-pointer"
          @click="edit(r)"
        />
        <Button icon="pi pi-plus" label="New rule" text size="small" @click="startNew" />
        <span class="grow" />
        <Button icon="pi pi-upload" text size="small" severity="secondary" v-tooltip.bottom="'Copy convention JSON'" @click="exportConvention" />
        <Button icon="pi pi-download" text size="small" severity="secondary" v-tooltip.bottom="'Import convention JSON from clipboard'" @click="importConvention" />
      </div>

      <!-- Editor -->
      <div class="flex flex-col gap-2 rounded-lg border border-surface-200 p-3">
        <div class="flex items-center gap-2">
          <InputText v-model="ruleName" placeholder="Rule name, e.g. 'Furniture types'" class="grow" size="small" />
          <Button icon="pi pi-trash" text size="small" severity="danger" :disabled="!ruleId" v-tooltip.bottom="'Delete rule'" @click="remove" />
        </div>

        <InputText ref="templateInput" v-model="template" placeholder="Template, e.g. {category|abbr}_{param:Material|abbr}_{param:Width}x{param:Height}" class="w-full font-mono text-sm" />

        <!-- Token helpers -->
        <div class="flex items-center gap-2 flex-wrap">
          <Button
            v-for="t in baseTokens"
            :key="t.token"
            :label="t.label"
            size="small"
            text
            severity="secondary"
            class="!px-2 !py-1 text-xs"
            @click="insertToken(t.token)"
          />
          <Select
            v-model="paramToken"
            :options="availableParams"
            optionLabel="name"
            optionValue="name"
            placeholder="+ parameter…"
            filter
            size="small"
            class="w-52"
          >
            <template #option="{ option }">
              <span class="flex items-center gap-1.5" :class="option.common ? '' : 'text-surface-400'">
                <span class="truncate">{{ option.name }}</span>
                <i
                  v-if="!option.common"
                  class="pi pi-exclamation-circle text-amber-500 text-xs shrink-0"
                  v-tooltip.right="'Not present on all selected types — missing values collapse in the name'"
                />
              </span>
            </template>
          </Select>
        </div>
        <p class="text-xs text-surface-400">
          Modifiers after |: <code>abbr</code> (dictionary), <code>upper</code>, <code>lower</code>,
          <code>clean</code>, <code>nospace</code>. Empty values collapse their separator.
        </p>
      </div>

      <!-- AI: infer the template from one example name -->
      <div v-if="sampleContexts.length" class="rounded-lg border border-surface-200 bg-surface-50 p-3 flex flex-col gap-2">
        <div class="flex items-center gap-2">
          <i class="pi pi-sparkles text-primary-500" />
          <span class="text-sm font-medium">Create from example</span>
        </div>
        <AiModelIndicator />
        <div class="flex items-center gap-2 flex-wrap">
          <Select
            v-model="sampleIndex"
            :options="sampleOptions"
            optionLabel="label"
            optionValue="value"
            size="small"
            class="w-48"
            v-tooltip.bottom="'Sample element the example describes'"
          />
          <InputText
            v-model="example"
            placeholder="Desired name for it, e.g. Möb_Alu_1000x2000"
            size="small"
            class="grow min-w-40"
            @keyup.enter="inferTemplate"
          />
          <Button
            :icon="inferring ? 'pi pi-spin pi-spinner' : 'pi pi-sparkles'"
            label="Infer"
            size="small"
            :disabled="!aiEnabled || inferring || !example.trim()"
            @click="inferTemplate"
          />
        </div>
        <p class="text-xs text-surface-400">
          Write the name you WANT for the sample element — the AI matches its fragments against the
          element's data and fills in the template + abbreviations. Review the preview, then save.
        </p>
      </div>

      <!-- Live preview against real selected elements -->
      <div v-if="preview.length" class="rounded-lg border border-surface-200 overflow-hidden">
        <div class="px-3 py-1.5 bg-surface-100 text-xs font-semibold text-surface-600">Preview</div>
        <div v-for="(p, i) in preview" :key="i" class="flex items-center gap-2 px-3 py-1 text-sm border-t border-surface-100">
          <span class="text-surface-500 truncate basis-1/2" :title="p.old">{{ p.old }}</span>
          <i class="pi pi-arrow-right text-xs text-surface-400 shrink-0" />
          <span class="font-medium truncate" :title="p.next">{{ p.next || "—" }}</span>
        </div>
      </div>

      <!-- Abbreviation dictionary (shared by all rules) -->
      <div class="rounded-lg border border-surface-200">
        <button
          type="button"
          class="w-full flex items-center gap-2 px-3 py-2 text-sm font-medium hover:bg-surface-50"
          @click="dictOpen = !dictOpen"
        >
          <i :class="dictOpen ? 'pi pi-chevron-down' : 'pi pi-chevron-right'" class="text-xs" />
          Abbreviations
          <span class="text-xs text-surface-400 font-normal">{{ dictEntries.length }} · shared by all rules</span>
        </button>
        <div v-if="dictOpen" class="px-3 pb-3 flex flex-col gap-2">
          <div class="flex items-center gap-2">
            <InputText v-model="newFull" placeholder="Full value, e.g. Aluminium" size="small" class="grow" @keyup.enter="addAbbr" />
            <i class="pi pi-arrow-right text-xs text-surface-400" />
            <InputText v-model="newAbbr" placeholder="Abbr, e.g. Alu" size="small" class="w-28" @keyup.enter="addAbbr" />
            <Button icon="pi pi-plus" size="small" text :disabled="!newFull.trim() || !newAbbr.trim()" @click="addAbbr" />
          </div>
          <div v-if="dictEntries.length" class="max-h-40 overflow-y-auto flex flex-col">
            <div v-for="[full, abbr] in dictEntries" :key="full" class="flex items-center gap-2 py-0.5 text-sm">
              <span class="truncate basis-1/2 text-surface-600" :title="full">{{ full }}</span>
              <i class="pi pi-arrow-right text-xs text-surface-400 shrink-0" />
              <span class="font-medium grow">{{ abbr }}</span>
              <Button icon="pi pi-times" text rounded size="small" severity="secondary" @click="naming.setAbbreviation(full, '')" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Close" text severity="secondary" @click="visible = false" />
      <Button label="Save rule" icon="pi pi-check" :disabled="!ruleName.trim() || !template.trim()" @click="save" />
    </template>
  </Dialog>
</template>
