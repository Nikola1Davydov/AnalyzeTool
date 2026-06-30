<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { invoke } from "@/RevitBridge";
import FamilyCard from "@/view/Families/FamilyCard.vue";
import FamilyThumb from "@/view/Families/FamilyThumb.vue";
import FamilyDetailDialog from "@/view/Families/FamilyDetailDialog.vue";
import FamilyTypesView from "@/view/Families/FamilyTypesView.vue";
import RenameDialog from "@/view/Families/RenameDialog.vue";
import RulesBar from "@/view/Families/RulesBar.vue";
import { useFamilyActions } from "@/view/Families/familyActions";
import { useFamilyRules } from "@/view/Families/familyRules";
import type { FamilyInventory, FamilyRow } from "@/view/Families/types";

type Section = "families" | "types";
type ViewMode = "table" | "gallery";
type QuickFilter = "All" | "Unused" | "In-place";

const actions = useFamilyActions();
const { rules, matchesRule } = useFamilyRules();

// `section` is the top-level dataset (families vs their types); `view` is how the families list is shown
// (table or gallery) — gallery is a view of the same list, not a separate tab.
const section = ref<Section>("families");
const view = ref<ViewMode>("table");
const families = ref<FamilyRow[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

const search = ref("");
const quickFilter = ref<QuickFilter>("All");
const quickFilters: QuickFilter[] = ["All", "Unused", "In-place"];
const categoryFilter = ref<string | null>(null);
const activeRuleId = ref<string | null>(null);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    const res = await invoke<FamilyInventory>("GetFamilies");
    families.value = res?.families ?? [];
  } catch (e) {
    console.error("Failed to load families", e);
    error.value = String((e as Error)?.message ?? e);
    families.value = [];
  } finally {
    loading.value = false;
  }
}

const categories = computed(() =>
  [...new Set(families.value.map((f) => f.category).filter(Boolean))].sort(),
);

const activeRule = computed(() => rules.find((r) => r.id === activeRuleId.value) ?? null);

const filteredFamilies = computed(() => {
  const q = search.value.trim().toLowerCase();
  return families.value.filter((f) => {
    if (quickFilter.value === "Unused" && f.instanceCount > 0) return false;
    if (quickFilter.value === "In-place" && !f.isInPlace) return false;
    if (categoryFilter.value && f.category !== categoryFilter.value) return false;
    if (activeRule.value && !matchesRule(activeRule.value, f)) return false;
    if (!q) return true;
    return f.name.toLowerCase().includes(q) || f.category.toLowerCase().includes(q);
  });
});

const stats = computed(() => {
  const list = families.value;
  return {
    families: list.length,
    types: list.reduce((sum, f) => sum + f.typeCount, 0),
    instances: list.reduce((sum, f) => sum + f.instanceCount, 0),
    unused: list.filter((f) => f.instanceCount === 0).length,
    inPlace: list.filter((f) => f.isInPlace).length,
  };
});

// Detail dialog
const detailVisible = ref(false);
const selectedFamily = ref<FamilyRow | null>(null);
function openDetail(family: FamilyRow) {
  selectedFamily.value = family;
  detailVisible.value = true;
}

// Row / card actions
function onAction(name: string, family: FamilyRow) {
  if (name === "select") void actions.select({ familyId: family.id });
  else if (name === "isolate") void actions.isolate({ familyId: family.id });
  else if (name === "rename") openRename(family);
  else if (name === "delete") openDelete(family);
}

// Rename dialog (shared component, with optional AI mode)
const renameVisible = ref(false);
const renameTarget = ref<FamilyRow | null>(null);
function openRename(family: FamilyRow) {
  renameTarget.value = family;
  renameVisible.value = true;
}
async function onRenameSubmit(newName: string) {
  if (!renameTarget.value) return;
  const done = await actions.renameFamily(renameTarget.value.id, newName);
  if (done) {
    renameVisible.value = false;
    await load();
  }
}

// Delete confirm dialog
const deleteVisible = ref(false);
const deleteTarget = ref<FamilyRow | null>(null);
function openDelete(family: FamilyRow) {
  deleteTarget.value = family;
  deleteVisible.value = true;
}
async function confirmDelete() {
  if (!deleteTarget.value) return;
  const done = await actions.deleteElements([deleteTarget.value.id]);
  if (done) {
    deleteVisible.value = false;
    await load();
  }
}

// Purge confirm dialog
const purgeVisible = ref(false);
const unusedCount = computed(() => families.value.filter((f) => f.instanceCount === 0).length);
async function confirmPurge() {
  const done = await actions.purgeUnused(families.value);
  purgeVisible.value = false;
  if (done) await load();
}

onMounted(load);
</script>

<template>
  <div class="p-6">
    <!-- Header -->
    <div class="flex items-start justify-between mb-4 gap-4">
      <div>
        <h1 class="text-xl font-bold">Family Manager</h1>
        <p class="text-xs text-surface-500">Browse, audit and manage the families in this project.</p>
      </div>
      <div class="flex gap-2 shrink-0">
        <SelectButton
          v-model="section"
          :options="(['families', 'types'] as Section[])"
          :allowEmpty="false"
        >
          <template #option="{ option }">
            <i :class="option === 'families' ? 'pi pi-box' : 'pi pi-list'" class="mr-1" />
            {{ option === "families" ? "Families" : "Family Types" }}
          </template>
        </SelectButton>
        <Button icon="pi pi-refresh" label="Refresh" :loading="loading" @click="load" />
      </div>
    </div>

    <!-- Stats -->
    <div class="grid grid-cols-2 md:grid-cols-5 gap-3 mb-5">
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-3">
        <div class="text-surface-500 text-xs">Families</div>
        <div class="text-lg font-bold">{{ stats.families }}</div>
      </div>
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-3">
        <div class="text-surface-500 text-xs">Types</div>
        <div class="text-lg font-bold">{{ stats.types }}</div>
      </div>
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-3">
        <div class="text-surface-500 text-xs">Instances</div>
        <div class="text-lg font-bold">{{ stats.instances }}</div>
      </div>
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-3">
        <div class="text-surface-500 text-xs">Unused</div>
        <div class="text-lg font-bold text-amber-600">{{ stats.unused }}</div>
      </div>
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-3">
        <div class="text-surface-500 text-xs">In-place</div>
        <div class="text-lg font-bold">{{ stats.inPlace }}</div>
      </div>
    </div>

    <div v-if="error" class="text-sm text-red-600 mb-3">Failed to load families: {{ error }}</div>

    <!-- Family Types section manages its own toolbar; families table + gallery share the one below. -->
    <FamilyTypesView v-if="section === 'types'" :families="families" />

    <template v-else>
      <!-- Family-scope quick filters (saved rules) -->
      <div class="mb-3">
        <RulesBar scope="families" v-model:activeRuleId="activeRuleId" />
      </div>

      <!-- Shared filter toolbar -->
      <div class="flex items-center justify-between mb-3 gap-3 flex-wrap">
        <div class="flex items-center gap-2 flex-wrap">
          <SelectButton v-model="quickFilter" :options="quickFilters" :allowEmpty="false" />
          <Select
            v-model="categoryFilter"
            :options="categories"
            placeholder="All categories"
            showClear
            filter
            class="w-56"
          />
        </div>
        <div class="flex items-center gap-2">
          <Button
            icon="pi pi-trash"
            label="Purge unused"
            severity="secondary"
            outlined
            size="small"
            :disabled="!unusedCount"
            :badge="unusedCount ? String(unusedCount) : undefined"
            @click="purgeVisible = true"
          />
          <IconField class="w-64">
            <InputIcon class="pi pi-search" />
            <InputText v-model="search" placeholder="Search name or category…" class="w-full" />
          </IconField>
          <!-- View switcher: table vs gallery (icon-only) -->
          <SelectButton v-model="view" :options="(['table', 'gallery'] as ViewMode[])" :allowEmpty="false">
            <template #option="{ option }">
              <i
                :class="option === 'table' ? 'pi pi-table' : 'pi pi-images'"
                v-tooltip.top="option === 'table' ? 'Table' : 'Gallery'"
              />
            </template>
          </SelectButton>
        </div>
      </div>

      <!-- Table view -->
      <section v-show="view === 'table'">
        <DataTable
          :value="filteredFamilies"
          :loading="loading"
          dataKey="id"
          scrollable
          scrollHeight="flex"
          paginator
          :rows="20"
          :rowsPerPageOptions="[20, 50, 100]"
          sortField="category"
          :sortOrder="1"
          rowHover
          class="text-sm"
        >
          <Column header="" class="w-16">
            <template #body="{ data: row }">
              <div
                class="w-10 h-10 rounded overflow-hidden border border-surface-200 cursor-pointer"
                @click="openDetail(row)"
              >
                <FamilyThumb :family="row" />
              </div>
            </template>
          </Column>
          <Column field="name" header="Family" sortable>
            <template #body="{ data: row }">
              <div class="font-semibold cursor-pointer" @click="openDetail(row)">{{ row.name }}</div>
              <div class="text-surface-500 text-xs">{{ row.category || "—" }}</div>
            </template>
          </Column>
          <Column field="category" header="Category" sortable class="hidden" />
          <Column field="typeCount" header="Types" sortable class="w-24" />
          <Column field="instanceCount" header="Instances" sortable class="w-28">
            <template #body="{ data: row }">
              <span :class="row.instanceCount === 0 ? 'text-amber-600 font-medium' : ''">
                {{ row.instanceCount }}
              </span>
            </template>
          </Column>
          <Column header="Flags" class="whitespace-nowrap w-40">
            <template #body="{ data: row }">
              <Tag v-if="row.isInPlace" value="in-place" severity="warn" class="mr-1" />
              <Tag v-if="row.instanceCount === 0" value="unused" severity="secondary" />
            </template>
          </Column>
          <Column header="" class="w-44">
            <template #body="{ data: row }">
              <div class="flex gap-1 justify-end">
                <Button
                  v-if="row.instanceCount > 0"
                  icon="pi pi-eye"
                  text
                  rounded
                  size="small"
                  severity="secondary"
                  v-tooltip.top="'Select instances'"
                  @click="onAction('select', row)"
                />
                <Button
                  v-if="row.instanceCount > 0"
                  icon="pi pi-filter"
                  text
                  rounded
                  size="small"
                  severity="secondary"
                  v-tooltip.top="'Isolate in view'"
                  @click="onAction('isolate', row)"
                />
                <Button
                  icon="pi pi-pencil"
                  text
                  rounded
                  size="small"
                  severity="secondary"
                  v-tooltip.top="'Rename'"
                  @click="onAction('rename', row)"
                />
                <Button
                  icon="pi pi-trash"
                  text
                  rounded
                  size="small"
                  severity="danger"
                  v-tooltip.top="'Delete'"
                  @click="onAction('delete', row)"
                />
              </div>
            </template>
          </Column>
          <template #empty>
            <div class="text-surface-500 p-4">No families match.</div>
          </template>
        </DataTable>
      </section>

      <!-- Gallery view -->
      <section v-show="view === 'gallery'">
        <div
          v-if="filteredFamilies.length"
          class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 gap-4"
        >
          <FamilyCard
            v-for="f in filteredFamilies"
            :key="f.id"
            :family="f"
            @open="openDetail(f)"
            @action="onAction($event, f)"
          />
        </div>
        <div v-else class="text-surface-500 p-10 text-center">No families match.</div>
      </section>
    </template>

    <FamilyDetailDialog v-model:visible="detailVisible" :family="selectedFamily" />

    <!-- Rename dialog (shared, with AI mode) -->
    <RenameDialog
      v-model:visible="renameVisible"
      title="Rename family"
      :currentName="renameTarget?.name ?? ''"
      :context="renameTarget?.category ?? ''"
      @submit="onRenameSubmit"
    />

    <!-- Delete confirm -->
    <Dialog
      v-model:visible="deleteVisible"
      modal
      dismissableMask
      header="Delete family"
      :style="{ width: '30rem' }"
    >
      <div class="flex gap-3 items-start">
        <i class="pi pi-exclamation-triangle text-2xl text-amber-500 mt-1" />
        <div class="text-sm">
          Delete <b>{{ deleteTarget?.name }}</b
          >? This removes the family, its {{ deleteTarget?.typeCount }} type(s)
          <template v-if="(deleteTarget?.instanceCount ?? 0) > 0">
            and all <b>{{ deleteTarget?.instanceCount }}</b> placed instance(s) </template
          >. This cannot be undone from here.
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="deleteVisible = false" />
        <Button label="Delete" icon="pi pi-trash" severity="danger" @click="confirmDelete" />
      </template>
    </Dialog>

    <!-- Purge confirm -->
    <Dialog
      v-model:visible="purgeVisible"
      modal
      dismissableMask
      header="Purge unused families"
      :style="{ width: '30rem' }"
    >
      <div class="flex gap-3 items-start">
        <i class="pi pi-exclamation-triangle text-2xl text-amber-500 mt-1" />
        <div class="text-sm">
          Delete all <b>{{ unusedCount }}</b> families with zero placed instances? This cannot be
          undone from here.
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="purgeVisible = false" />
        <Button label="Purge" icon="pi pi-trash" severity="danger" @click="confirmPurge" />
      </template>
    </Dialog>
  </div>
</template>
