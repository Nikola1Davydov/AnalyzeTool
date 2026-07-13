<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from "vue";
import { invoke } from "@/RevitBridge";
import { useFamilyActions } from "./familyActions";
import { useFamilyRules } from "./familyRules";
import { usePaletteSettings } from "./paletteSettings";
import RulesBar from "./RulesBar.vue";
import FamilyThumb from "./FamilyThumb.vue";
import FamilyLibraryView from "./FamilyLibraryView.vue";
import type { FamilyInventory, FamilyRow, TypeRow, TypeRowsResult } from "./types";

// Dockable placement palette. Two views:
//  • Gallery — a family shows its preview image; hovering flips the card into a scrollable list of its
//    types (the list may grow taller than the image). Click a type to place it.
//  • Table — compact rows with a small family thumbnail. A family with several types expands to a
//    type list (no images); a family with a single type is shown inline ("Family · Type") and placed on
//    click.
// Grouping (default: by category) and sorting are user-chosen and persisted (see paletteSettings).
// Only loadable families are placeable, so system types are excluded.

interface PalType {
  typeId: number;
  typeName: string;
  uniqueId: string;
  versionGuid: string;
  category: string;
}
interface PalFamily {
  id: number;
  name: string;
  category: string;
  uniqueId: string;
  versionGuid: string;
  instanceCount: number;
  isInPlace: boolean;
  types: PalType[];
}

// The palette keeps its OWN saved rules, separate from the Family Manager — same engine, different store.
const PALETTE_RULES_KEY = "analysetool.paletteRules.v1";

const actions = useFamilyActions();
const settings = usePaletteSettings();
const { rules, matchesRule } = useFamilyRules(PALETTE_RULES_KEY);

const loading = ref(false);
const families = ref<PalFamily[]>([]);
const search = ref("");
const expanded = ref<Set<number>>(new Set());
const settingsOpen = ref(false);
const activeRuleId = ref<string | null>(null);

async function load() {
  loading.value = true;
  try {
    const inv = await invoke<FamilyInventory>("GetFamilies");
    const famRows: FamilyRow[] = inv?.families ?? [];
    if (!famRows.length) {
      families.value = [];
      return;
    }
    const res = await invoke<TypeRowsResult>("GetFamilyTypeRows", {
      familyIds: famRows.map((f) => f.id),
    });
    const byFamily = new Map<number, PalType[]>();
    for (const r of (res?.rows ?? []).filter((r: TypeRow) => !r.isSystem)) {
      const list = byFamily.get(r.familyId) ?? byFamily.set(r.familyId, []).get(r.familyId)!;
      list.push({
        typeId: r.typeId,
        typeName: r.typeName,
        uniqueId: r.uniqueId,
        versionGuid: r.versionGuid,
        category: r.category,
      });
    }
    families.value = famRows
      // Only loadable, non-in-place families are placeable from a palette: system types are already
      // excluded above; in-place families are authored in the model and can't be placed interactively.
      .filter((f) => !f.isInPlace && byFamily.has(f.id))
      .map((f) => ({
        id: f.id,
        name: f.name,
        category: f.category || "Uncategorized",
        uniqueId: f.uniqueId,
        versionGuid: f.versionGuid,
        instanceCount: f.instanceCount,
        isInPlace: f.isInPlace,
        types: byFamily.get(f.id)!.sort((a, b) => a.typeName.localeCompare(b.typeName)),
      }));
  } catch (e) {
    console.error("Palette load failed", e);
    families.value = [];
  } finally {
    loading.value = false;
  }
}

// ---- filtering / grouping / sorting ------------------------------------------------------------
function matchesSearch(f: PalFamily, q: string): boolean {
  if (!q) return true;
  if (f.name.toLowerCase().includes(q)) return true;
  return f.types.some((t) => t.typeName.toLowerCase().includes(q));
}

function compare(a: PalFamily, b: PalFamily): number {
  let r = 0;
  if (settings.sortBy === "name") r = a.name.localeCompare(b.name);
  else if (settings.sortBy === "typeCount") r = a.types.length - b.types.length;
  else if (settings.sortBy === "instanceCount") r = a.instanceCount - b.instanceCount;
  if (r === 0) r = a.name.localeCompare(b.name); // stable tiebreak
  return settings.sortDir === "asc" ? r : -r;
}

interface Group {
  key: string;
  label: string;
  families: PalFamily[];
}

// Active saved rule (shared "families"-scope store — same rules as the Family Manager). The rule engine
// reads FamilyRow-shaped fields (name/category/typeCount/instanceCount/isInPlace), so map each family to that.
const activeRule = computed(() => rules.find((r) => r.id === activeRuleId.value) ?? null);
function toRuleItem(f: PalFamily) {
  return {
    name: f.name,
    category: f.category,
    typeCount: f.types.length,
    instanceCount: f.instanceCount,
    isInPlace: f.isInPlace,
  };
}

const groups = computed<Group[]>(() => {
  const q = search.value.trim().toLowerCase();
  const list = families.value
    .filter((f) => matchesSearch(f, q))
    .filter((f) => !activeRule.value || matchesRule(activeRule.value, toRuleItem(f)))
    .sort(compare);

  if (settings.groupBy === "none") {
    return list.length ? [{ key: "", label: "", families: list }] : [];
  }

  const map = new Map<string, PalFamily[]>();
  for (const f of list) {
    const k = f.category || "Uncategorized";
    (map.get(k) ?? map.set(k, []).get(k)!).push(f);
  }
  return [...map.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([key, fams]) => ({ key, label: key, families: fams }));
});

const totalFamilies = computed(() => groups.value.reduce((s, g) => s + g.families.length, 0));

// ---- interactions ------------------------------------------------------------------------------
function placeType(t: PalType) {
  void actions.place(t.typeId);
}
function isOpen(id: number) {
  return expanded.value.has(id);
}
function toggle(id: number) {
  const next = new Set(expanded.value);
  next.has(id) ? next.delete(id) : next.add(id);
  expanded.value = next;
}

// Toolbar option lists (kept tiny for the narrow dock).
const sourceOptions = [
  { value: "document", label: "In document", icon: "pi pi-box" },
  { value: "library", label: "Library", icon: "pi pi-folder" },
];
const viewOptions = [
  { value: "gallery", icon: "pi pi-images", label: "Gallery" },
  { value: "table", icon: "pi pi-list", label: "Table" },
];
const groupOptions = [
  { value: "category", label: "Category" },
  { value: "none", label: "None" },
];
const sortOptions = [
  { value: "name", label: "Name" },
  { value: "typeCount", label: "Types" },
  { value: "instanceCount", label: "Instances" },
];
function toggleSortDir() {
  settings.sortDir = settings.sortDir === "asc" ? "desc" : "asc";
}

// The dockable pane outlives documents: the host pushes "DocumentChanged" when the active document
// switches (title === null → all documents closed, so querying Revit would only error).
const noDocument = ref(false);
function onDocumentChanged(e: Event) {
  const title = (e as CustomEvent).detail?.title ?? null;
  noDocument.value = title === null;
  expanded.value = new Set();
  if (title === null) families.value = [];
  else void load();
}

onMounted(() => {
  void load();
  window.addEventListener("at:DocumentChanged", onDocumentChanged);
});
onUnmounted(() => window.removeEventListener("at:DocumentChanged", onDocumentChanged));
</script>

<template>
  <div class="flex flex-col h-screen text-sm">
    <!-- Source: families in the document vs on-disk library folders (collapsible to save space) -->
    <div class="border-b border-surface-200 shrink-0">
      <button
        type="button"
        class="w-full flex items-center gap-2 px-2 py-1 text-xs text-surface-500 hover:bg-surface-100"
        @click="settings.sourceBarOpen = !settings.sourceBarOpen"
      >
        <i :class="settings.sourceBarOpen ? 'pi pi-chevron-up' : 'pi pi-chevron-down'" class="text-[10px]" />
        <i :class="settings.source === 'library' ? 'pi pi-folder' : 'pi pi-box'" />
        <span class="font-medium">{{ settings.source === "library" ? "Library" : "In document" }}</span>
      </button>
      <div v-show="settings.sourceBarOpen" class="px-2 pb-2">
        <SelectButton
          :modelValue="settings.source"
          @update:modelValue="settings.source = $event"
          :options="sourceOptions"
          optionValue="value"
          :allowEmpty="false"
          dataKey="value"
        >
          <template #option="{ option }">
            <span class="flex items-center gap-1 text-xs"><i :class="option.icon" />{{ option.label }}</span>
          </template>
        </SelectButton>
      </div>
    </div>

    <!-- LIBRARY mode. Reload the document (placeable) list whenever families are loaded from disk. -->
    <FamilyLibraryView v-if="settings.source === 'library'" class="grow min-h-0" @loaded="load" />

    <!-- DOCUMENT mode -->
    <template v-else>
    <!-- Toolbar: search, view, settings (group/sort), refresh -->
    <div class="border-b border-surface-200 shrink-0 p-2 flex items-center gap-2">
      <IconField class="grow">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Search family / type…" class="w-full" />
      </IconField>
      <SelectButton
        :modelValue="settings.view"
        @update:modelValue="settings.view = $event"
        :options="viewOptions"
        optionValue="value"
        :allowEmpty="false"
        dataKey="value"
      >
        <template #option="{ option }">
          <i :class="option.icon" v-tooltip.bottom="option.label" />
        </template>
      </SelectButton>
      <Button
        icon="pi pi-cog"
        text
        severity="secondary"
        v-tooltip.bottom="'Grouping & sorting'"
        @click="settingsOpen = true"
      />
      <Button icon="pi pi-refresh" text :loading="loading" v-tooltip.bottom="'Refresh'" @click="load" />
    </div>

    <!-- Panel shows ONLY the pinned quick-filter chips; rules are created/managed in the settings dialog. -->
    <div class="px-2 py-1.5 border-b border-surface-200 shrink-0">
      <RulesBar
        scope="families"
        :storageKey="PALETTE_RULES_KEY"
        variant="chips"
        v-model:activeRuleId="activeRuleId"
      />
    </div>

    <!-- Grouping & sorting settings -->
    <Dialog
      v-model:visible="settingsOpen"
      modal
      dismissableMask
      header="View settings"
      :style="{ width: '22rem', maxWidth: '95vw' }"
    >
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Group by</label>
          <Select
            :modelValue="settings.groupBy"
            @update:modelValue="settings.groupBy = $event"
            :options="groupOptions"
            optionLabel="label"
            optionValue="value"
            class="w-full"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Sort by</label>
          <div class="flex items-center gap-2">
            <Select
              :modelValue="settings.sortBy"
              @update:modelValue="settings.sortBy = $event"
              :options="sortOptions"
              optionLabel="label"
              optionValue="value"
              class="grow"
            />
            <Button
              :icon="settings.sortDir === 'asc' ? 'pi pi-arrow-up' : 'pi pi-arrow-down'"
              severity="secondary"
              outlined
              v-tooltip.bottom="settings.sortDir === 'asc' ? 'Ascending' : 'Descending'"
              @click="toggleSortDir"
            />
          </div>
        </div>

        <!-- Rules: full create/edit/pin/delete list; pinned ones surface as chips on the panel. -->
        <div class="flex flex-col gap-1 border-t border-surface-200 pt-3">
          <RulesBar
            scope="families"
            :storageKey="PALETTE_RULES_KEY"
            variant="manage"
            v-model:activeRuleId="activeRuleId"
          />
        </div>
      </div>
    </Dialog>

    <!-- Body -->
    <div class="grow overflow-y-auto">
      <div v-if="loading && !totalFamilies" class="p-6 text-center text-surface-500">
        <i class="pi pi-spin pi-spinner mr-2" />Loading…
      </div>
      <div v-else-if="!totalFamilies" class="p-6 text-center text-surface-500">
        {{ noDocument ? "No open document." : "No placeable families." }}
      </div>

      <template v-else>
        <div v-for="g in groups" :key="g.key || '_all'">
          <!-- Group header (only when grouping) -->
          <div
            v-if="settings.groupBy !== 'none'"
            class="px-3 py-1.5 bg-surface-100 text-xs font-semibold text-surface-600 sticky top-0 z-10 flex items-center gap-2"
          >
            <span class="truncate">{{ g.label }}</span>
            <span class="text-surface-400 font-normal">{{ g.families.length }}</span>
          </div>

          <!-- Gallery view -->
          <div v-if="settings.view === 'gallery'" class="grid grid-cols-2 sm:grid-cols-3 gap-2 p-2">
            <div v-for="f in g.families" :key="f.id" class="flex flex-col">
              <div class="group relative aspect-square">
                <!-- Preview; a single-type family places directly on click -->
                <div
                  class="absolute inset-0 rounded-lg overflow-hidden border border-surface-200"
                  :class="f.types.length === 1 ? 'cursor-pointer' : ''"
                  @click="f.types.length === 1 && placeType(f.types[0])"
                >
                  <FamilyThumb
                    :family="{
                      id: f.id,
                      uniqueId: f.uniqueId,
                      versionGuid: f.versionGuid,
                      name: f.name,
                      category: f.category,
                    }"
                  />
                </div>

                <!-- Hover: image becomes a (possibly taller) list of types -->
                <div
                  class="absolute inset-x-0 top-0 z-20 hidden group-hover:block min-h-full rounded-lg border border-primary-300 bg-surface-0 shadow-xl overflow-hidden"
                >
                  <div class="px-2 py-1.5 text-xs font-medium border-b border-surface-200 truncate" :title="f.name">
                    {{ f.name }}
                  </div>
                  <div class="max-h-64 overflow-y-auto">
                    <button
                      v-for="t in f.types"
                      :key="t.typeId"
                      type="button"
                      class="w-full text-left px-2 py-1.5 text-xs hover:bg-primary-50 truncate flex items-center gap-1.5"
                      v-tooltip.top="'Place ' + t.typeName"
                      @click="placeType(t)"
                    >
                      <i class="pi pi-plus text-[10px] text-primary-500 shrink-0" />
                      <span class="truncate">{{ t.typeName }}</span>
                    </button>
                  </div>
                </div>
              </div>
              <div class="mt-1 text-xs truncate text-center" :title="f.name">{{ f.name }}</div>
            </div>
          </div>

          <!-- Table view -->
          <div v-else>
            <div v-for="f in g.families" :key="f.id" class="border-b border-surface-100">
              <div
                class="flex items-center gap-2 px-2 py-1.5 hover:bg-surface-100 cursor-pointer"
                @click="f.types.length === 1 ? placeType(f.types[0]) : toggle(f.id)"
              >
                <div class="w-9 h-9 rounded overflow-hidden border border-surface-200 shrink-0">
                  <FamilyThumb
                    :family="{
                      id: f.id,
                      uniqueId: f.uniqueId,
                      versionGuid: f.versionGuid,
                      name: f.name,
                      category: f.category,
                    }"
                  />
                </div>
                <div class="min-w-0 grow truncate">
                  <template v-if="f.types.length === 1">
                    <span class="font-medium">{{ f.name }}</span>
                    <span class="text-surface-300 mx-1">·</span>
                    <span class="text-surface-600">{{ f.types[0].typeName }}</span>
                  </template>
                  <span v-else class="font-medium">{{ f.name }}</span>
                </div>
                <span v-if="f.types.length > 1" class="text-xs text-surface-400 shrink-0">
                  {{ f.types.length }}
                </span>
                <i
                  v-if="f.types.length > 1"
                  class="pi text-xs text-surface-400 shrink-0"
                  :class="isOpen(f.id) ? 'pi-chevron-down' : 'pi-chevron-right'"
                />
                <i v-else class="pi pi-plus-circle text-primary-500 text-sm shrink-0" />
              </div>

              <!-- Expanded type list (multi-type only; types have no thumbnails) -->
              <div v-if="f.types.length > 1 && isOpen(f.id)" class="pl-12 pr-2 pb-1.5">
                <button
                  v-for="t in f.types"
                  :key="t.typeId"
                  type="button"
                  class="w-full text-left px-2 py-1 text-xs rounded hover:bg-primary-50 flex items-center gap-1.5"
                  v-tooltip.top="'Place ' + t.typeName"
                  @click="placeType(t)"
                >
                  <i class="pi pi-plus text-[10px] text-primary-500 shrink-0" />
                  <span class="truncate">{{ t.typeName }}</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      </template>
    </div>
    </template>
  </div>
</template>
