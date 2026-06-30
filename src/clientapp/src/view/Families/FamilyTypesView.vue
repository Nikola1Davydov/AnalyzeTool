<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { invoke } from "@/RevitBridge";
import { useFamilyActions } from "./familyActions";
import { useFamilyRules } from "./familyRules";
import RulesBar from "./RulesBar.vue";
import RenameDialog from "./RenameDialog.vue";
import type { FamilyRow, TypeGroup, TypeRow, TypeRowsResult, WorksetInfo, WorksetsResult } from "./types";

// Family Types tab: family types (FamilySymbols) of the filtered families, grouped by type name.
// Columns: Family / Type / Category / Workset / Instances. Per-group actions mirror the families table
// (rename / delete / select / isolate), plus moving all the group's instances to another workset.
// Intentionally has no per-parameter column picker — type parameters can be huge and slow the table.
const props = defineProps<{ families: FamilyRow[] }>();

const actions = useFamilyActions();
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
}));

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
        <IconField class="w-64">
          <InputIcon class="pi pi-search" />
          <InputText v-model="search" placeholder="Search type / family…" class="w-full" />
        </IconField>
        <Button icon="pi pi-refresh" text :loading="loading" @click="load" />
      </div>
    </div>

    <!-- Workset move bar (only when workshared) -->
    <div v-if="isWorkshared" class="flex items-center gap-2 mb-3">
      <span class="text-sm text-surface-500">{{ selection.length }} selected</span>
      <Select
        v-model="targetWorkset"
        :options="worksets"
        optionLabel="name"
        optionValue="id"
        placeholder="Move instances to workset…"
        class="w-60"
      />
      <Button
        label="Move"
        icon="pi pi-arrow-right"
        :disabled="targetWorkset == null || !selection.length"
        @click="moveSelectedToWorkset"
      />
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
      <Column v-if="isWorkshared" selectionMode="multiple" class="w-12" />

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

      <!-- Workset -->
      <Column field="workset" header="Workset" sortable class="w-44">
        <template #body="{ data: g }">
          <Tag :value="g.workset || '—'" severity="secondary" />
        </template>
      </Column>

      <!-- Instance count -->
      <Column field="instanceCount" header="Instances" sortable class="w-28">
        <template #body="{ data: g }">
          <span :class="g.instanceCount === 0 ? 'text-amber-600 font-medium' : ''">
            {{ g.instanceCount }}
          </span>
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
  </section>
</template>
