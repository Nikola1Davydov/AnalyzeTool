<script setup lang="ts">
/**
 * The main window's first page, in two halves: the shared parameter FILE on the left — organised in
 * its groups, by dragging — and the PROJECT on the right — what is bound, by category. Dragging from
 * the file into the project adds parameters to it. One selection spans both: it feeds the report and
 * the actions in the bar above.
 */
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { storeToRefs } from "pinia";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import FilePane from "./FilePane.vue";
import ProjectPane from "./ProjectPane.vue";
import ParameterDialog from "./ParameterDialog.vue";
import BindDialog from "./BindDialog.vue";
import QuickBindDialog from "./QuickBindDialog.vue";
import type { ParameterRow, SharedParameter } from "./types";

const store = useSharedParametersStore();
const { file, rows, dirty, loading, saving, selection, selectedRows, reportSelection, bindBlocker } = storeToRefs(store);
const notifications = useNotificationStore();
const router = useRouter();

// ---- Edit a parameter (or create one, optionally in a given group) -------------------------------
const editVisible = ref(false);
const editTarget = ref<SharedParameter | null>(null);
const editGroupId = ref<number | undefined>(undefined);
function edit(parameter: SharedParameter | null, groupId?: number) {
  editTarget.value = parameter;
  editGroupId.value = groupId;
  editVisible.value = true;
}

// ---- Add to project: the full dialog, or the quick one after a drop on a category -----------------
const bindVisible = ref(false);
const bindRows = ref<ParameterRow[]>([]);
const quickVisible = ref(false);
const quickGuids = ref<string[]>([]);
const quickCategory = ref("");

const rowsFor = (guids: string[]) => {
  const wanted = new Set(guids.map((g) => g.toLowerCase()));
  return rows.value.filter((r) => r.file && r.guid && wanted.has(r.guid));
};

/** The full dialog needs the file saved too; offer to do it rather than refuse. */
async function openBind(guids: string[]) {
  if (bindBlocker.value) {
    if (!dirty.value) return notifications.warn(bindBlocker.value);
    if (!window.confirm("Revit adds parameters from the file on disk. Save the file now?") || !(await store.save())) return;
  }
  bindRows.value = rowsFor(guids);
  if (bindRows.value.length) bindVisible.value = true;
}

function quickBind(guids: string[], categoryName: string) {
  quickGuids.value = guids;
  quickCategory.value = categoryName;
  quickVisible.value = true;
}

// ---- The selection bar ---------------------------------------------------------------------------
const selectedInFile = computed(() => selectedRows.value.filter((r) => r.file));

function deleteSelected() {
  const inFile = selectedInFile.value;
  if (!inFile.length) return;
  const inProject = inFile.filter((r) => r.project);
  const names = inFile.length === 1 ? `"${inFile[0].name}"` : `${inFile.length} parameters`;
  const note = inProject.length ? `\n\n${inProject.length} of them stay in the project — this only removes them from the file.` : "";
  if (window.confirm(`Delete ${names} from the shared parameter file?${note}`)) store.deleteParameters(inFile.map((r) => r.guid!));
}

async function reload() {
  if (await store.confirmDiscard()) await store.load();
}

const fileName = computed(() => file.value?.path?.split(/[\\/]/).pop() ?? "");

onMounted(() => {
  if (!file.value) store.load();
});
</script>

<template>
  <div class="p-4 flex flex-col gap-3 h-[calc(100vh-7.5rem)]">
    <!-- The file: which one, and the save state -->
    <section class="shrink-0 rounded-xl border border-surface-200 bg-surface-0 p-3 flex flex-wrap items-center gap-3">
      <i class="pi pi-file text-surface-500" />
      <div class="min-w-0 grow">
        <template v-if="file?.path">
          <div class="font-semibold truncate" v-tooltip.bottom="file.path">
            {{ fileName }}
            <Tag v-if="!file.exists" value="missing" severity="danger" class="ml-1" />
            <Tag v-else-if="!file.isRevitCurrent" value="not Revit's current file" severity="warn" class="ml-1" />
            <Tag v-if="dirty" value="unsaved changes" severity="warn" class="ml-1" />
          </div>
          <div class="text-xs text-surface-500 truncate">{{ file.path }}</div>
        </template>
        <div v-else class="text-surface-500 text-sm">Revit has no shared parameter file set — open one or create a new one.</div>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button label="Open…" icon="pi pi-folder-open" size="small" severity="secondary" @click="store.openFile(false)" />
        <Button label="New file…" icon="pi pi-file-plus" size="small" severity="secondary" @click="store.openFile(true)" />
        <Button icon="pi pi-refresh" size="small" severity="secondary" text :loading="loading" v-tooltip.bottom="'Reload the file and the project'" @click="reload" />
        <template v-if="dirty">
          <Button label="Discard" size="small" severity="secondary" text @click="store.discard()" />
          <Button label="Save" icon="pi pi-save" size="small" :loading="saving" @click="store.save()" />
        </template>
      </div>
    </section>

    <!-- Selection bar: always there, so ticking the first row does not move anything under the cursor -->
    <div
      class="shrink-0 flex flex-wrap items-center gap-2 rounded-lg border px-3 py-1.5 text-sm min-h-11"
      :class="selection.length ? 'bg-primary-50 border-primary-200' : 'bg-surface-0 border-surface-200'"
    >
      <span v-if="!selection.length" class="text-surface-500">
        Drag parameters between groups to organise the file, and into the project to add them. Tick them for the report.
      </span>
      <span v-else class="font-medium">{{ selection.length }} selected</span>
      <div class="grow" />
      <template v-if="selection.length">
        <span v-tooltip.top="selectedInFile.length ? undefined : 'Select parameters of the file'">
          <Button label="Add to project" icon="pi pi-sign-in" size="small" :disabled="!selectedInFile.length" @click="openBind(selectedInFile.map((r) => r.guid!))" />
        </span>
        <span v-tooltip.top="reportSelection.length ? undefined : 'Select parameters that are bound in the project'">
          <Button
            :label="`Report (${reportSelection.length})`"
            icon="pi pi-chart-bar"
            size="small"
            severity="secondary"
            :disabled="!reportSelection.length"
            @click="router.push('/report')"
          />
        </span>
        <Button icon="pi pi-trash" size="small" severity="danger" text :disabled="!selectedInFile.length" v-tooltip.top="'Delete from the file'" @click="deleteSelected" />
        <Button icon="pi pi-times" size="small" text severity="secondary" v-tooltip.top="'Clear selection'" @click="selection = []" />
      </template>
    </div>

    <!-- The two halves -->
    <div class="grow min-h-0 grid grid-cols-[minmax(0,3fr)_minmax(0,2fr)] gap-3">
      <FilePane @edit="edit" />
      <ProjectPane @quick-bind="quickBind" @bind="openBind" />
    </div>

    <ParameterDialog v-model:visible="editVisible" :parameter="editTarget" :groupId="editGroupId" />
    <BindDialog v-model:visible="bindVisible" :rows="bindRows" />
    <QuickBindDialog v-model:visible="quickVisible" :guids="quickGuids" :categoryName="quickCategory" />
  </div>
</template>
