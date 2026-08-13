<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { useToast } from "primevue/usetoast";
import { invoke } from "@/RevitBridge";
import { useFamilyActions } from "./familyActions";
import { useFamilyRules } from "./familyRules";
import RulesBar from "./RulesBar.vue";
import RenameDialog from "./RenameDialog.vue";
import FamilyThumb from "./FamilyThumb.vue";
import BulkRenameDialog from "./BulkRenameDialog.vue";
import NamingRuleDialog from "./NamingRuleDialog.vue";
import { applyTemplate, type NamingContext } from "./namingEngine";
import { useNamingRules } from "./namingRules";
import type { FamilyRow, TypeGroup, TypeRow, TypeRowsResult, WorksetInfo, WorksetsResult } from "./types";

// Family Types tab: family types (FamilySymbols) of the filtered families, grouped by type name.
// Columns: Family / Type / Category / Workset / Instances. Per-group actions mirror the families table
// (rename / delete / select / isolate), plus moving all the group's instances to another workset.
// Intentionally has no per-parameter column picker — type parameters can be huge and slow the table.
const props = defineProps<{ families: FamilyRow[] }>();
const emit = defineEmits<{
  stats: [{ groups: number; types: number; instances: number; unused: number }];
}>();

const actions = useFamilyActions();
const toast = useToast();
const { rules, matchesRule } = useFamilyRules();

const loading = ref(false);
const isWorkshared = ref(false);
const worksets = ref<WorksetInfo[]>([]);
const rows = ref<TypeRow[]>([]);

// Top-level filters (same kinds as the other tabs).
const search = ref("");
const categoryFilter = ref<string | null>(null);
const activeRuleId = ref<string | null>(null);

const selection = ref<TypeGroup[]>([]);
const targetWorkset = ref<number | null>(null);

async function load() {
  loading.value = true;
  selection.value = [];
  try {
    const ws = await invoke<WorksetsResult>("GetWorksets");
    isWorkshared.value = ws?.isWorkshared ?? false;
    worksets.value = ws?.worksets ?? [];

    const familyIds = props.families.map((f) => f.id);
    const res = await invoke<TypeRowsResult>("GetFamilyTypeRows", { familyIds });
    rows.value = res?.rows ?? [];
  } catch (e) {
    console.error("Type rows load failed", e);
    rows.value = [];
  } finally {
    loading.value = false;
  }
}

watch(() => props.families, load, { immediate: true });

const categories = computed(() =>
  [...new Set(rows.value.map((r) => r.category).filter(Boolean))].sort(),
);

// Map a raw type row to the shape the rule engine expects (workset[] flattened to a string).
function toRuleItem(row: TypeRow) {
  return {
    typeName: row.typeName,
    familyName: row.familyName,
    category: row.category,
    workset: row.worksets.join(", "),
    instanceCount: row.instanceCount,
  };
}

const activeRule = computed(() => rules.find((r) => r.id === activeRuleId.value) ?? null);

// 1) Filter raw type rows by search / category / saved rule.
const filteredRows = computed(() => {
  const q = search.value.trim().toLowerCase();
  return rows.value.filter((r) => {
    if (categoryFilter.value && r.category !== categoryFilter.value) return false;
    if (activeRule.value && !matchesRule(activeRule.value, toRuleItem(r))) return false;
    if (!q) return true;
    return (
      r.typeName.toLowerCase().includes(q) ||
      r.familyName.toLowerCase().includes(q) ||
      r.category.toLowerCase().includes(q)
    );
  });
});

// 2) Group by type name (identical type names merge into one row).
const groups = computed<TypeGroup[]>(() => {
  const map = new Map<string, TypeGroup>();
  for (const r of filteredRows.value) {
    let g = map.get(r.typeName);
    if (!g) {
      g = {
        key: r.typeName,
        familyName: r.familyName,
        typeName: r.typeName,
        category: r.category,
        workset: "",
        instanceCount: 0,
        typeIds: [],
        familyIds: [],
        isSystem: r.isSystem,
        previewTypeId: r.typeId,
        uniqueId: r.uniqueId,
        versionGuid: r.versionGuid,
      };
      map.set(r.typeName, g);
      (g as any)._families = new Set<string>();
      (g as any)._categories = new Set<string>();
      (g as any)._worksets = new Set<string>();
    }
    g.instanceCount += r.instanceCount;
    g.isSystem = g.isSystem || r.isSystem;
    g.typeIds.push(r.typeId);
    if (!g.familyIds.includes(r.familyId)) g.familyIds.push(r.familyId);
    (g as any)._families.add(r.familyName);
    (g as any)._categories.add(r.category);
    r.worksets.forEach((w) => (g as any)._worksets.add(w));
  }
  return [...map.values()]
    .map((g) => {
      g.familyName = [...(g as any)._families].join(", ");
      g.category = [...(g as any)._categories].join(", ");
      g.workset = [...(g as any)._worksets].sort().join(", ");
      return g;
    })
    .sort((a, b) => a.typeName.localeCompare(b.typeName));
});

const totals = computed(() => ({
  types: groups.value.reduce((s, g) => s + g.typeIds.length, 0),
  groups: groups.value.length,
  instances: groups.value.reduce((s, g) => s + g.instanceCount, 0),
  // "Unused" here is type-level: grouped type rows with no placed instances.
  unused: groups.value.filter((g) => g.instanceCount === 0).length,
}));

// Surface the type-level stats to the parent so its header can show type totals (incl. Unused) instead
// of the family inventory numbers while the Family Types view is active.
watch(totals, (t) => emit("stats", t), { immediate: true, deep: true });

// ---- purge unused types -------------------------------------------------------------------------
// All type ids across groups with no placed instances (a grouped row may hold several merged types).
const unusedTypeIds = computed(() =>
  groups.value.filter((g) => g.instanceCount === 0).flatMap((g) => g.typeIds),
);
const purgeVisible = ref(false);
const purgeRunning = ref(false);
const purgeProgress = ref(0); // 0..100

async function confirmPurgeUnused() {
  if (!unusedTypeIds.value.length) return;

  purgeVisible.value = false;
  purgeRunning.value = true;
  purgeProgress.value = 0;

  // One host call → one Undo entry; progress is pushed back live via the central progress channel.
  const r = await actions.purgeTypesProgress(unusedTypeIds.value, (f) => {
    purgeProgress.value = Math.round(f * 100);
  });

  purgeRunning.value = false;
  if (!r.ok) {
    toast.add({ severity: "error", summary: "Purge failed", detail: r.error ?? "Unknown error", life: 4000 });
  } else if (r.failed > 0) {
    toast.add({
      severity: "warn",
      summary: `Purged ${r.deleted} type(s)`,
      detail: `${r.failed} kept — still in use, or the last type of a (system) family, which Revit requires to remain.`,
      life: 5000,
    });
  } else {
    toast.add({ severity: "success", summary: "Purged", detail: `${r.deleted} unused type(s) removed.`, life: 2500 });
  }
  await load();
}

// ---- actions -----------------------------------------------------------------------------------
function selectGroup(g: TypeGroup) {
  void actions.select({ typeIds: g.typeIds });
}
function isolateGroup(g: TypeGroup) {
  void actions.isolate({ typeIds: g.typeIds });
}

const renameVisible = ref(false);
const renameTarget = ref<TypeGroup | null>(null);
function openRename(g: TypeGroup) {
  renameTarget.value = g;
  renameVisible.value = true;
}
async function onRenameSubmit(newName: string) {
  if (!renameTarget.value) return;
  const done = await actions.renameTypes(renameTarget.value.typeIds, newName);
  if (done) {
    renameVisible.value = false;
    await load();
  }
}

const deleteVisible = ref(false);
const deleteTarget = ref<TypeGroup | null>(null);
function openDelete(g: TypeGroup) {
  deleteTarget.value = g;
  deleteVisible.value = true;
}
async function confirmDelete() {
  if (!deleteTarget.value) return;
  const done = await actions.deleteElements([], deleteTarget.value.typeIds);
  if (done) {
    deleteVisible.value = false;
    await load();
  }
}

async function moveSelectedToWorkset() {
  if (targetWorkset.value == null || !selection.value.length) return;
  const typeIds = [...new Set(selection.value.flatMap((g) => g.typeIds))];
  const done = await actions.setWorkset({ typeIds }, targetWorkset.value);
  if (done) await load();
}

// ---- bulk rename (naming rule and/or AI) ----------------------------------------------------------
// Selected GROUPS expand to individual types: parameters differ per type, so each type gets its own
// computed name (unlike the single-rename path, which renames a merged group to one shared name).
const naming = useNamingRules();
const bulkRenameVisible = ref(false);
// "ai" = pure free-text AI (opens instantly, no parameter fetch); "rule" = naming rule only.
const bulkMode = ref<"ai" | "rule">("ai");
function openBulkRename(mode: "ai" | "rule") {
  bulkMode.value = mode;
  bulkRenameVisible.value = true;
}
const ruleDialogVisible = ref(false);
const selectedRuleId = ref<string | null>(null);
const typeRules = computed(() => naming.rulesFor("type"));

const selectedTypeRows = computed(() => {
  const ids = new Set(selection.value.flatMap((g) => g.typeIds));
  return rows.value.filter((r) => ids.has(r.typeId));
});
const bulkItems = computed(() =>
  selectedTypeRows.value.map((r) => ({
    id: r.typeId,
    name: r.typeName,
    context: `${r.familyName} (${r.category})`,
    scope: String(r.familyId), // type names are unique per family, not globally
  })),
);
const takenTypeNames = computed(() =>
  rows.value.map((r) => ({ name: r.typeName, scope: String(r.familyId) })),
);

// Type parameters are fetched once per dialog opening (one batch call) and turned into naming
// contexts for the rule engine + the builder's live preview.
const paramsByType = ref<Map<number, Record<string, string>>>(new Map());
const paramsLoading = ref(false);
watch(bulkRenameVisible, async (open) => {
  if (!open || bulkMode.value !== "rule") return; // pure-AI mode needs no parameter contexts
  paramsLoading.value = true;
  try {
    const res = await invoke<{ types: { typeId: number; parameters: { name: string; value: string }[] }[] }>(
      "GetTypeParameters",
      { typeIds: selectedTypeRows.value.map((r) => r.typeId) },
    );
    paramsByType.value = new Map(
      (res?.types ?? []).map((t) => [
        t.typeId,
        Object.fromEntries(t.parameters.map((p) => [p.name, p.value])),
      ]),
    );
  } catch (e) {
    console.error("GetTypeParameters failed", e);
    paramsByType.value = new Map();
  } finally {
    paramsLoading.value = false;
  }
});

function contextFor(r: TypeRow): NamingContext {
  return {
    name: r.typeName,
    family: r.familyName,
    category: r.category,
    params: paramsByType.value.get(r.typeId) ?? {},
  };
}
const sampleContexts = computed(() => selectedTypeRows.value.slice(0, 5).map(contextFor));
// Every parameter across the selected types, flagged whether ALL of them carry it — a token for a
// non-common parameter still works (missing values collapse), but the author should see the gap.
const availableParams = computed(() => {
  const total = selectedTypeRows.value.length;
  const counts = new Map<string, number>();
  for (const r of selectedTypeRows.value) {
    const params = paramsByType.value.get(r.typeId) ?? {};
    for (const n of Object.keys(params)) counts.set(n, (counts.get(n) ?? 0) + 1);
  }
  return [...counts.entries()]
    .map(([name, count]) => ({ name, common: count === total }))
    .sort((a, b) => Number(b.common) - Number(a.common) || a.name.localeCompare(b.name));
});

// Applying the selected rule fills the review rows via the dialog's `names` prop.
const ruleNames = ref<Record<number, string> | null>(null);
function applyRule() {
  const rule = typeRules.value.find((r) => r.id === selectedRuleId.value);
  if (!rule) return;
  const out: Record<number, string> = {};
  for (const r of selectedTypeRows.value) {
    const name = applyTemplate(rule.template, contextFor(r), naming.store.abbreviations);
    if (name) out[r.typeId] = name;
  }
  ruleNames.value = { ...out }; // fresh object so the watcher fires even for a re-apply
}

async function renameTypeQuiet(id: number, newName: string): Promise<{ ok: boolean; error?: string }> {
  try {
    const res = await invoke<{ ok: boolean; error?: string }>("RenameFamilyType", { id, newName });
    return { ok: !!res?.ok, error: res?.error };
  } catch (e) {
    return { ok: false, error: String((e as Error)?.message ?? e) };
  }
}
</script>

<template>
  <section>
    <!-- Type-scope quick filters (saved rules) -->
    <div class="mb-3">
      <RulesBar scope="types" v-model:activeRuleId="activeRuleId" />
    </div>

    <!-- Top toolbar: same filter kinds as the other tabs -->
    <div class="flex items-center justify-between mb-3 gap-3 flex-wrap">
      <Select
        v-model="categoryFilter"
        :options="categories"
        placeholder="All categories"
        showClear
        filter
        class="w-56"
      />
      <div class="flex items-center gap-2">
        <span class="text-xs text-surface-500">
          {{ totals.groups }} groups · {{ totals.types }} types · {{ totals.instances }} inst
        </span>
        <Button
          icon="pi pi-trash"
          label="Purge unused"
          severity="secondary"
          outlined
          size="small"
          :disabled="!unusedTypeIds.length"
          :badge="unusedTypeIds.length ? String(unusedTypeIds.length) : undefined"
          v-tooltip.bottom="'Delete all types with zero placed instances'"
          @click="purgeVisible = true"
        />
        <IconField class="w-64">
          <InputIcon class="pi pi-search" />
          <InputText v-model="search" placeholder="Search type / family…" class="w-full" />
        </IconField>
        <Button icon="pi pi-refresh" text :loading="loading" @click="load" />
      </div>
    </div>

    <!-- Contextual bulk-action bar — visible only while something is selected -->
    <div
      v-if="selection.length"
      class="flex items-center gap-2 mb-3 px-3 py-2 rounded-lg border border-primary-200 bg-primary-50 flex-wrap"
    >
      <Button
        icon="pi pi-times"
        text
        rounded
        size="small"
        severity="secondary"
        v-tooltip.bottom="'Clear selection'"
        @click="selection = []"
      />
      <span class="text-sm font-medium">{{ selection.length }} selected</span>
      <span class="grow" />
      <Button
        icon="pi pi-sparkles"
        label="Rename with AI"
        size="small"
        v-tooltip.bottom="'Free-text instruction — the AI names the whole selection'"
        @click="openBulkRename('ai')"
      />
      <Button
        icon="pi pi-book"
        label="Rename by rule"
        size="small"
        severity="secondary"
        outlined
        v-tooltip.bottom="'Deterministic naming rule (templates + abbreviations)'"
        @click="openBulkRename('rule')"
      />
      <template v-if="isWorkshared">
        <Select
          v-model="targetWorkset"
          :options="worksets"
          optionLabel="name"
          optionValue="id"
          placeholder="Move instances to workset…"
          size="small"
          class="w-56"
        />
        <Button
          label="Move"
          icon="pi pi-arrow-right"
          size="small"
          :disabled="targetWorkset == null"
          @click="moveSelectedToWorkset"
        />
      </template>
    </div>

    <DataTable
      v-model:selection="selection"
      :value="groups"
      :loading="loading"
      dataKey="key"
      scrollable
      scrollHeight="flex"
      paginator
      :rows="25"
      :rowsPerPageOptions="[25, 50, 100]"
      rowHover
      class="text-sm"
    >
      <Column selectionMode="multiple" class="w-12" />

      <!-- Preview thumbnail (type's Revit preview image) -->
      <Column header="" class="w-16">
        <template #body="{ data: g }">
          <div class="w-10 h-10 rounded overflow-hidden border border-surface-200">
            <FamilyThumb
              :family="{
                id: g.previewTypeId,
                uniqueId: g.uniqueId,
                versionGuid: g.versionGuid,
                name: g.typeName,
                category: g.category,
              }"
            />
          </div>
        </template>
      </Column>

      <!-- Family -->
      <Column field="familyName" header="Family" sortable>
        <template #body="{ data: g }">
          <div class="flex items-center gap-2">
            <span>{{ g.familyName }}</span>
            <Tag v-if="g.isSystem" value="system" severity="info" />
          </div>
        </template>
      </Column>

      <!-- Type -->
      <Column field="typeName" header="Type" sortable>
        <template #body="{ data: g }">
          <div class="font-semibold">{{ g.typeName }}</div>
          <div v-if="g.typeIds.length > 1" class="text-surface-400 text-xs">
            {{ g.typeIds.length }} types merged
          </div>
        </template>
      </Column>

      <!-- Workset (only on workshared projects) -->
      <Column v-if="isWorkshared" field="workset" header="Workset" sortable class="w-44">
        <template #body="{ data: g }">
          <Tag :value="g.workset || '—'" severity="secondary" />
        </template>
      </Column>

      <!-- Instance count -->
      <Column field="instanceCount" header="Instances" sortable class="w-32">
        <template #body="{ data: g }">
          <div class="flex items-center gap-2">
            <span :class="g.instanceCount === 0 ? 'text-amber-600 font-medium' : ''">
              {{ g.instanceCount }}
            </span>
            <Tag v-if="g.instanceCount === 0" value="unused" severity="secondary" />
          </div>
        </template>
      </Column>

      <!-- Actions -->
      <Column header="" class="w-44">
        <template #body="{ data: g }">
          <div class="flex gap-1 justify-end">
            <Button
              v-if="g.instanceCount > 0"
              icon="pi pi-eye"
              text
              rounded
              size="small"
              severity="secondary"
              v-tooltip.top="'Select instances'"
              @click="selectGroup(g)"
            />
            <Button
              v-if="g.instanceCount > 0"
              icon="pi pi-filter"
              text
              rounded
              size="small"
              severity="secondary"
              v-tooltip.top="'Isolate in view'"
              @click="isolateGroup(g)"
            />
            <Button
              icon="pi pi-pencil"
              text
              rounded
              size="small"
              severity="secondary"
              v-tooltip.top="'Rename type'"
              @click="openRename(g)"
            />
            <Button
              icon="pi pi-trash"
              text
              rounded
              size="small"
              severity="danger"
              v-tooltip.top="'Delete type'"
              @click="openDelete(g)"
            />
          </div>
        </template>
      </Column>

      <template #empty>
        <div class="text-surface-500 p-4">No types match.</div>
      </template>
    </DataTable>

    <!-- Bulk rename: naming rule (deterministic) and/or AI batch fill the review rows -->
    <BulkRenameDialog
      v-model:visible="bulkRenameVisible"
      :title="bulkMode === 'ai' ? 'Rename types with AI' : 'Rename types by rule'"
      :showAi="bulkMode === 'ai'"
      :items="bulkItems"
      :taken="takenTypeNames"
      :applyOne="renameTypeQuiet"
      :names="ruleNames"
      @applied="load"
    >
      <template #generator>
        <div v-if="bulkMode === 'rule'" class="rounded-lg border border-surface-200 bg-surface-50 p-3 flex flex-col gap-2">
          <div class="flex items-center gap-2 flex-wrap">
            <i class="pi pi-book text-primary-500" />
            <span class="text-sm font-medium">Naming rule</span>
            <span v-if="paramsLoading" class="text-xs text-surface-400">
              <i class="pi pi-spin pi-spinner mr-1" />loading type parameters…
            </span>
          </div>
          <div class="flex items-center gap-2 flex-wrap">
            <Select
              v-model="selectedRuleId"
              :options="typeRules"
              optionLabel="name"
              optionValue="id"
              placeholder="Pick a rule…"
              size="small"
              class="w-56"
            />
            <Button
              icon="pi pi-play"
              label="Apply rule"
              size="small"
              :disabled="!selectedRuleId || paramsLoading"
              @click="applyRule"
            />
            <Button
              icon="pi pi-cog"
              text
              size="small"
              severity="secondary"
              v-tooltip.bottom="'Manage rules & abbreviations'"
              @click="ruleDialogVisible = true"
            />
          </div>
        </div>
      </template>
    </BulkRenameDialog>

    <!-- Rule builder (templates + shared abbreviation dictionary, live preview on the selection) -->
    <NamingRuleDialog
      v-model:visible="ruleDialogVisible"
      scope="type"
      :availableParams="availableParams"
      :sampleContexts="sampleContexts"
    />

    <!-- Rename dialog (shared, with AI mode) -->
    <RenameDialog
      v-model:visible="renameVisible"
      title="Rename type"
      label="New type name"
      :currentName="renameTarget?.typeName ?? ''"
      :context="renameTarget?.category ?? ''"
      :note="
        (renameTarget?.typeIds.length ?? 0) > 1
          ? `This renames all ${renameTarget?.typeIds.length} merged types.`
          : null
      "
      @submit="onRenameSubmit"
    />

    <!-- Delete confirm -->
    <Dialog
      v-model:visible="deleteVisible"
      modal
      dismissableMask
      header="Delete type"
      :style="{ width: '30rem' }"
    >
      <div class="flex gap-3 items-start">
        <i class="pi pi-exclamation-triangle text-2xl text-amber-500 mt-1" />
        <div class="text-sm">
          Delete <b>{{ deleteTarget?.typeName }}</b>
          ({{ deleteTarget?.typeIds.length }} type(s))
          <template v-if="(deleteTarget?.instanceCount ?? 0) > 0">
            and all <b>{{ deleteTarget?.instanceCount }}</b> placed instance(s) </template
          >? This cannot be undone from here.
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="deleteVisible = false" />
        <Button label="Delete" icon="pi pi-trash" severity="danger" @click="confirmDelete" />
      </template>
    </Dialog>

    <!-- Purge unused types confirm -->
    <Dialog
      v-model:visible="purgeVisible"
      modal
      dismissableMask
      header="Purge unused types"
      :style="{ width: '30rem' }"
    >
      <div class="flex gap-3 items-start">
        <i class="pi pi-exclamation-triangle text-2xl text-amber-500 mt-1" />
        <div class="text-sm">
          <p>
            Delete all <b>{{ unusedTypeIds.length }}</b> family type(s) with zero placed instances? This
            cannot be undone from here.
          </p>
          <p class="mt-2 text-surface-500">
            Note: the last remaining type of a family is always kept — Revit does not allow a family
            (including system families like walls, floors or pipes) to exist without any type. Such types
            are skipped and reported, not treated as errors.
          </p>
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="purgeVisible = false" />
        <Button label="Purge" icon="pi pi-trash" severity="danger" @click="confirmPurgeUnused" />
      </template>
    </Dialog>

    <!-- Purge progress -->
    <Dialog
      :visible="purgeRunning"
      modal
      :closable="false"
      :closeOnEscape="false"
      header="Purging unused types…"
      :style="{ width: '24rem' }"
    >
      <div class="flex flex-col gap-2">
        <div class="h-2 w-full rounded bg-surface-200 overflow-hidden">
          <div
            class="h-2 rounded bg-primary-500 transition-all"
            :style="{ width: purgeProgress + '%' }"
          />
        </div>
        <div class="text-xs text-surface-500 text-right">{{ purgeProgress }}%</div>
      </div>
    </Dialog>
  </section>
</template>
