<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { invoke } from "@/RevitBridge";
import FamilyCard from "@/view/Families/FamilyCard.vue";
import FamilyDetailDialog from "@/view/Families/FamilyDetailDialog.vue";

// Wire shape from the built-in GetFamilies command (see FamiliesService.FamilyInfo / FamilyInventory).
interface FamilyRow {
  id: number;
  uniqueId: string;
  versionGuid: string;
  name: string;
  category: string;
  typeCount: number;
  instanceCount: number;
  isInPlace: boolean;
}
interface FamilyInventory {
  count: number;
  returned: number;
  families: FamilyRow[];
}

type Tab = "table" | "gallery";
type QuickFilter = "All" | "Unused" | "In-place";

const tab = ref<Tab>("table");
const families = ref<FamilyRow[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

const search = ref("");
const quickFilter = ref<QuickFilter>("All");
const quickFilters: QuickFilter[] = ["All", "Unused", "In-place"];

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

const filteredFamilies = computed(() => {
  const q = search.value.trim().toLowerCase();
  return families.value.filter((f) => {
    if (quickFilter.value === "Unused" && f.instanceCount > 0) return false;
    if (quickFilter.value === "In-place" && !f.isInPlace) return false;
    if (!q) return true;
    return f.name.toLowerCase().includes(q) || f.category.toLowerCase().includes(q);
  });
});

// Header stats — computed over the full inventory, not the filtered view.
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

// Detail dialog: click a card (or table row) → 3D viewer + type panel for that family.
const detailVisible = ref(false);
const selectedFamily = ref<FamilyRow | null>(null);
function openDetail(family: FamilyRow) {
  selectedFamily.value = family;
  detailVisible.value = true;
}

onMounted(load);
</script>

<template>
  <div class="p-6">
    <!-- Header -->
    <div class="flex items-start justify-between mb-4 gap-4">
      <div>
        <h1 class="text-xl font-bold">Family Control</h1>
        <p class="text-xs text-surface-500">Browse, audit and manage the families in this project.</p>
      </div>
      <div class="flex gap-2 shrink-0">
        <SelectButton v-model="tab" :options="(['table', 'gallery'] as Tab[])" :allowEmpty="false">
          <template #option="{ option }">
            <i :class="option === 'table' ? 'pi pi-table' : 'pi pi-images'" class="mr-1" />
            {{ option === "table" ? "Table" : "Gallery" }}
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

    <!-- Shared filter toolbar (applies to both Table and Gallery). -->
    <div class="flex items-center justify-between mb-3 gap-3">
      <SelectButton v-model="quickFilter" :options="quickFilters" :allowEmpty="false" />
      <IconField class="w-72">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Search name or category…" class="w-full" />
      </IconField>
    </div>

    <!-- Table tab -->
    <section v-show="tab === 'table'">
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
        class="text-sm cursor-pointer"
        @row-click="openDetail($event.data)"
      >
        <Column field="name" header="Family" sortable>
          <template #body="{ data: row }">
            <div class="font-semibold">{{ row.name }}</div>
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
        <template #empty>
          <div class="text-surface-500 p-4">No families match.</div>
        </template>
      </DataTable>
    </section>

    <!-- Gallery tab: lazy per-card previews via GetFamilyPreview, cached in IndexedDB (FamilyCard). -->
    <section v-show="tab === 'gallery'">
      <div
        v-if="filteredFamilies.length"
        class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5 gap-4"
      >
        <FamilyCard
          v-for="f in filteredFamilies"
          :key="f.id"
          :family="f"
          @open="openDetail(f)"
        />
      </div>
      <div v-else class="text-surface-500 p-10 text-center">No families match.</div>
    </section>

    <FamilyDetailDialog v-model:visible="detailVisible" :family="selectedFamily" />
  </div>
</template>
