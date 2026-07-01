<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { useToast } from "primevue/usetoast";
import { invoke } from "@/RevitBridge";
import { useLibraryPaths } from "./libraryPaths";
import LibraryThumb from "./LibraryThumb.vue";

// Library mode: browse .rfa families under configured folders and load them into the current document.
// Folders are user-managed (add via native picker / list / remove) and persisted. All families across the
// folders show by default, with a mandatory path filter to narrow to one folder.

interface LibFamily {
  path: string;
  name: string;
  folder: string;
  loaded: boolean;
  version: string | null; // Revit version the .rfa was saved in (e.g. "2024")
  compatible: boolean; // false = saved in a newer Revit → can't be loaded here
}

const { paths, add, remove } = useLibraryPaths();
const toast = useToast();

const loading = ref(false);
const families = ref<LibFamily[]>([]);
const search = ref("");
const pathFilter = ref<string | null>(null); // null = all paths

const loadRunning = ref(false);
const loadProgress = ref(0);

// Short label for a folder chip / filter option (last path segment, full path in tooltip).
function folderLabel(p: string): string {
  const parts = p.replace(/[\\/]+$/, "").split(/[\\/]/);
  return parts[parts.length - 1] || p;
}

const pathOptions = computed(() => [
  { label: "All paths", value: null as string | null },
  ...paths.map((p) => ({ label: folderLabel(p), value: p })),
]);

async function reload() {
  if (!paths.length) {
    families.value = [];
    return;
  }
  loading.value = true;
  try {
    const res = await invoke<{ families: LibFamily[] }>("GetLibraryFamilies", { folders: [...paths] });
    families.value = res?.families ?? [];
  } catch (e) {
    console.error("Library scan failed", e);
    families.value = [];
  } finally {
    loading.value = false;
  }
}

// Rescan whenever the folder set changes (add/remove).
watch(paths, reload, { deep: true, immediate: true });

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase();
  return families.value.filter((f) => {
    if (pathFilter.value && f.folder !== pathFilter.value) return false;
    if (q && !f.name.toLowerCase().includes(q)) return false;
    return true;
  });
});

// Loadable = not already in the document AND compatible (not saved in a newer Revit).
const notLoadedInView = computed(() => filtered.value.filter((f) => !f.loaded && f.compatible));

// ---- path management ---------------------------------------------------------------------------
async function addFolder() {
  try {
    const res = await invoke<{ path: string | null }>("PickFolder");
    if (res?.path) {
      if (add(res.path)) reload();
      else toast.add({ severity: "info", summary: "Already added", detail: folderLabel(res.path), life: 2000 });
    }
  } catch (e) {
    toast.add({ severity: "error", summary: "Couldn't pick folder", detail: String((e as Error)?.message ?? e), life: 4000 });
  }
}
function removeFolder(p: string) {
  if (pathFilter.value === p) pathFilter.value = null;
  remove(p);
}

// ---- loading -----------------------------------------------------------------------------------
async function loadPaths(paths: string[]) {
  if (!paths.length) return;
  loadRunning.value = true;
  loadProgress.value = 0;
  try {
    const res = await invoke<{ ok: boolean; loaded: number; failed: number; error?: string }>(
      "LoadLibraryFamilies",
      { paths },
      { onProgress: (p) => (loadProgress.value = Math.round((p.fraction ?? 0) * 100)) },
    );
    if (!res?.ok) {
      toast.add({ severity: "error", summary: "Load failed", detail: res?.error ?? "Unknown error", life: 4000 });
    } else if (res.failed > 0) {
      toast.add({
        severity: "warn",
        summary: `Loaded ${res.loaded}`,
        detail: `${res.failed} could not be loaded (already present or invalid).`,
        life: 5000,
      });
    } else {
      toast.add({ severity: "success", summary: "Loaded", detail: `${res.loaded} family(ies) added.`, life: 2500 });
    }
  } catch (e) {
    toast.add({ severity: "error", summary: "Load failed", detail: String((e as Error)?.message ?? e), life: 4000 });
  } finally {
    loadRunning.value = false;
    await reload();
  }
}
const loadOne = (f: LibFamily) => loadPaths([f.path]);
const loadAllInView = () => loadPaths(notLoadedInView.value.map((f) => f.path));
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Path management -->
    <div class="p-2 border-b border-surface-200 shrink-0 flex flex-col gap-2">
      <div class="flex items-center gap-2">
        <Button icon="pi pi-folder-open" label="Add folder" size="small" @click="addFolder" />
        <span v-if="!paths.length" class="text-xs text-surface-400">No library folders yet.</span>
        <Button
          icon="pi pi-refresh"
          text
          size="small"
          class="ml-auto"
          :loading="loading"
          v-tooltip.bottom="'Rescan'"
          @click="reload"
        />
      </div>
      <div v-if="paths.length" class="flex flex-wrap gap-1">
        <span
          v-for="p in paths"
          :key="p"
          class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-surface-100 border border-surface-200"
          :title="p"
        >
          <i class="pi pi-folder text-[10px] text-surface-400" />
          {{ folderLabel(p) }}
          <button type="button" class="ml-0.5 text-surface-400 hover:text-red-500" @click="removeFolder(p)">
            <i class="pi pi-times text-[10px]" />
          </button>
        </span>
      </div>
    </div>

    <!-- Filter + search + load-all -->
    <div class="p-2 border-b border-surface-200 shrink-0 flex items-center gap-2 flex-wrap">
      <Select
        v-model="pathFilter"
        :options="pathOptions"
        optionLabel="label"
        optionValue="value"
        class="w-40"
        v-tooltip.bottom="'Filter by folder'"
      />
      <IconField class="grow min-w-40">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Search family…" class="w-full" />
      </IconField>
      <Button
        icon="pi pi-download"
        label="Load all"
        size="small"
        severity="secondary"
        outlined
        :disabled="!notLoadedInView.length"
        :badge="notLoadedInView.length ? String(notLoadedInView.length) : undefined"
        v-tooltip.bottom="'Load every not-yet-loaded family in view'"
        @click="loadAllInView"
      />
    </div>

    <!-- Grid -->
    <div class="grow overflow-y-auto">
      <div v-if="loading && !families.length" class="p-6 text-center text-surface-500">
        <i class="pi pi-spin pi-spinner mr-2" />Scanning…
      </div>
      <div v-else-if="!paths.length" class="p-6 text-center text-surface-500">
        Add a folder of Revit families to get started.
      </div>
      <div v-else-if="!filtered.length" class="p-6 text-center text-surface-500">No families match.</div>

      <div v-else class="grid grid-cols-2 sm:grid-cols-3 gap-2 p-2">
        <div v-for="f in filtered" :key="f.path" class="flex flex-col">
          <div class="relative aspect-square rounded-lg overflow-hidden border border-surface-200">
            <LibraryThumb :path="f.path" :name="f.name" />
            <!-- version badge -->
            <Tag
              v-if="f.version"
              :value="f.version"
              :severity="f.compatible ? 'secondary' : 'danger'"
              class="absolute top-1 left-1 text-[10px]"
              v-tooltip.right="f.compatible ? 'Saved in Revit ' + f.version : 'Saved in Revit ' + f.version + ' — newer than this Revit, can\'t load'"
            />
            <!-- status / action overlay -->
            <div class="absolute top-1 right-1">
              <Tag v-if="f.loaded" value="in document" severity="success" class="text-[10px]" />
              <i
                v-else-if="!f.compatible"
                class="pi pi-ban text-red-500 bg-surface-0/80 rounded-full p-1"
                v-tooltip.left="'Saved in a newer Revit — can\'t be loaded here'"
              />
              <Button
                v-else
                icon="pi pi-plus"
                rounded
                size="small"
                v-tooltip.left="'Load into document'"
                @click="loadOne(f)"
              />
            </div>
          </div>
          <div class="mt-1 text-xs truncate text-center" :title="f.path">{{ f.name }}</div>
        </div>
      </div>
    </div>

    <!-- Load progress -->
    <Dialog
      :visible="loadRunning"
      modal
      :closable="false"
      :closeOnEscape="false"
      header="Loading families…"
      :style="{ width: '24rem' }"
    >
      <div class="flex flex-col gap-2">
        <div class="h-2 w-full rounded bg-surface-200 overflow-hidden">
          <div class="h-2 rounded bg-primary-500 transition-all" :style="{ width: loadProgress + '%' }" />
        </div>
        <div class="text-xs text-surface-500 text-right">{{ loadProgress }}%</div>
      </div>
    </Dialog>
  </div>
</template>
