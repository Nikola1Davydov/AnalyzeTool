<script setup lang="ts">
/**
 * The main window's first page: every shared parameter — of the file Revit uses and of the open
 * project — in one table. Edit the file (parameters, groups), add parameters to the project, and pick
 * the ones the A4 report evaluates.
 */
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { storeToRefs } from "pinia";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import ParameterDialog from "./ParameterDialog.vue";
import GroupsDialog from "./GroupsDialog.vue";
import BindDialog from "./BindDialog.vue";
import { dataTypeLabel, type ParameterRow, type SharedParameter } from "./types";

const store = useSharedParametersStore();
const { file, rows, groups, dirty, loading, saving, selection, selectedRows, reportSelection } = storeToRefs(store);
const router = useRouter();

// ---- Filters: text, group, and where the parameter lives ---------------------------------------
type Scope = "all" | "file" | "project" | "notInProject";
const search = ref("");
const groupFilter = ref<number | null>(null);
const scope = ref<Scope>("all");
const scopeOptions = [
  { label: "All", value: "all" },
  { label: "In file", value: "file" },
  { label: "In project", value: "project" },
  { label: "Not in project", value: "notInProject" },
];

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase();
  return rows.value.filter((r) => {
    if (q && !`${r.name} ${r.description} ${r.groupName} ${r.guid ?? ""}`.toLowerCase().includes(q)) return false;
    if (groupFilter.value !== null && r.file?.groupId !== groupFilter.value) return false;
    if (scope.value === "file" && !r.file) return false;
    if (scope.value === "project" && !r.project) return false;
    if (scope.value === "notInProject" && (!r.file || r.project?.bound)) return false;
    return true;
  });
});

// DataTable selection works on row objects; the store keeps keys, so the selection survives reloads.
const selectedObjects = computed<ParameterRow[]>({
  get: () => selectedRows.value,
  set: (value) => (selection.value = value.map((r) => r.key)),
});

// ---- Actions -------------------------------------------------------------------------------------
const editVisible = ref(false);
const editTarget = ref<SharedParameter | null>(null);
const groupsVisible = ref(false);
const bindVisible = ref(false);

function newParameter() {
  editTarget.value = null;
  editVisible.value = true;
}
function editParameter(row: ParameterRow) {
  if (!row.file) return;
  editTarget.value = row.file;
  editVisible.value = true;
}

const selectedInFile = computed(() => selectedRows.value.filter((r) => r.file));

function deleteSelected(rowsToDelete: ParameterRow[]) {
  const inFile = rowsToDelete.filter((r) => r.file);
  if (!inFile.length) return;
  const inProject = inFile.filter((r) => r.project);
  const names = inFile.length === 1 ? `"${inFile[0].name}"` : `${inFile.length} parameters`;
  const note = inProject.length
    ? `\n\n${inProject.length} of them stay in the project — this only removes them from the file.`
    : "";
  if (!window.confirm(`Delete ${names} from the shared parameter file?${note}`)) return;
  store.deleteParameters(inFile.map((r) => r.guid!));
}

// Binding reads the file from disk, so unsaved edits would not be there yet.
const bindBlocker = computed(() => {
  if (!selectedInFile.value.length) return "Select parameters of the file";
  if (!file.value?.isRevitCurrent) return "Only parameters of Revit's current shared parameter file can be added";
  if (dirty.value) return "Save the file first";
  return null;
});

function openReport() {
  router.push("/report");
}

function bindingLabel(row: ParameterRow): string {
  const p = row.project;
  if (!p) return "";
  if (!p.bound) return "Families only";
  const n = p.categories.length;
  return `${p.isInstance ? "Instance" : "Type"} · ${n} ${n === 1 ? "category" : "categories"}`;
}

function onRowDblClick(e: { data: ParameterRow }) {
  editParameter(e.data);
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
  <div class="p-4 flex flex-col gap-3">
    <!-- The file: which one, and the save state -->
    <section class="rounded-xl border border-surface-200 bg-surface-0 p-3 flex flex-wrap items-center gap-3">
      <i class="pi pi-file text-surface-500" />
      <div class="min-w-0 grow">
        <template v-if="file?.path">
          <div class="font-semibold truncate" v-tooltip.bottom="file.path">
            {{ fileName }}
            <Tag v-if="!file.exists" value="missing" severity="danger" class="ml-1" />
            <Tag v-else-if="!file.isRevitCurrent" value="not Revit's current file" severity="warn" class="ml-1" />
          </div>
          <div class="text-xs text-surface-500 truncate">{{ file.path }}</div>
        </template>
        <div v-else class="text-surface-500 text-sm">Revit has no shared parameter file set — open one or create a new one.</div>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button label="Open…" icon="pi pi-folder-open" size="small" severity="secondary" @click="store.openFile(false)" />
        <Button label="New file…" icon="pi pi-file-plus" size="small" severity="secondary" @click="store.openFile(true)" />
        <Button
          icon="pi pi-refresh"
          size="small"
          severity="secondary"
          text
          :loading="loading"
          v-tooltip.bottom="'Reload the file and the project'"
          @click="reload"
        />
        <template v-if="dirty">
          <Button label="Discard" size="small" severity="secondary" text @click="store.discard()" />
          <Button label="Save" icon="pi pi-save" size="small" :loading="saving" @click="store.save()" />
        </template>
      </div>
    </section>

    <!-- Filters and actions -->
    <div class="flex flex-wrap items-center gap-2">
      <IconField class="grow max-w-xs">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Search name, description, GUID…" size="small" class="w-full" />
      </IconField>
      <Select
        v-model="groupFilter"
        :options="groups"
        placeholder="All groups"
        showClear
        optionLabel="name"
        optionValue="id"
        size="small"
        class="w-44"
      />
      <SelectButton v-model="scope" :options="scopeOptions" optionLabel="label" optionValue="value" size="small" :allowEmpty="false" />

      <div class="grow" />

      <Button label="Parameter" icon="pi pi-plus" size="small" :disabled="!file?.path" @click="newParameter" />
      <Button label="Groups" icon="pi pi-folder" size="small" severity="secondary" :disabled="!file?.path" @click="groupsVisible = true" />
    </div>

    <!-- Selection bar: always there, so ticking the first row does not push the table under the cursor -->
    <div
      class="flex flex-wrap items-center gap-2 rounded-lg border px-3 py-1.5 text-sm min-h-11"
      :class="selection.length ? 'bg-primary-50 border-primary-200' : 'bg-surface-0 border-surface-200'"
    >
      <span v-if="!selection.length" class="text-surface-500">
        Select parameters to add them to the project or to put them in the report.
      </span>
      <span v-else class="font-medium">{{ selection.length }} selected</span>
      <div class="grow" />
      <template v-if="selection.length">
        <span v-tooltip.top="bindBlocker">
          <Button label="Add to project" icon="pi pi-sign-in" size="small" :disabled="!!bindBlocker" @click="bindVisible = true" />
        </span>
        <span v-tooltip.top="reportSelection.length ? undefined : 'Select parameters that are bound in the project'">
          <Button
            :label="`Report (${reportSelection.length})`"
            icon="pi pi-chart-bar"
            size="small"
            severity="secondary"
            :disabled="!reportSelection.length"
            @click="openReport"
          />
        </span>
        <Button
          icon="pi pi-trash"
          size="small"
          severity="danger"
          text
          :disabled="!selectedInFile.length"
          v-tooltip.top="'Delete from the file'"
          @click="deleteSelected(selectedRows)"
        />
        <Button icon="pi pi-times" size="small" text severity="secondary" v-tooltip.top="'Clear selection'" @click="selection = []" />
      </template>
    </div>

    <DataTable
      v-model:selection="selectedObjects"
      :value="filtered"
      dataKey="key"
      :loading="loading"
      size="small"
      scrollable
      scrollHeight="flex"
      class="text-sm"
      style="height: calc(100vh - 16rem)"
      @row-dblclick="onRowDblClick"
    >
      <Column selectionMode="multiple" headerStyle="width: 2.5rem" />
      <Column header="Name" sortable sortField="name">
        <template #body="{ data: row }">
          <div class="font-medium" :class="{ 'text-surface-500': !row.file }">{{ row.name }}</div>
          <div v-if="row.description" class="text-xs text-surface-500 line-clamp-1">{{ row.description }}</div>
        </template>
      </Column>
      <Column header="Type" sortable sortField="dataType" class="w-32">
        <template #body="{ data: row }">{{ row.file ? dataTypeLabel(row.dataType) : row.dataType }}</template>
      </Column>
      <Column header="Group" field="groupName" sortable class="w-40" />
      <Column header="In file" class="w-24">
        <template #body="{ data: row }">
          <i v-if="row.file" class="pi pi-check text-green-600" />
          <span v-else class="text-xs text-surface-500" v-tooltip.top="row.guid ? 'Shared, but from another file' : 'Project parameter — no file has it'">
            {{ row.guid ? "other file" : "project param" }}
          </span>
        </template>
      </Column>
      <Column header="In project" class="w-44">
        <template #body="{ data: row }">
          <span
            v-if="row.project"
            class="text-xs"
            :class="row.project.bound ? 'text-surface-800' : 'text-surface-500'"
            v-tooltip.top="row.project.categories.join(', ') || undefined"
          >
            {{ bindingLabel(row) }}
          </span>
          <span v-else class="text-xs text-surface-400">—</span>
        </template>
      </Column>
      <Column class="w-20">
        <template #body="{ data: row }">
          <div v-if="row.file" class="flex">
            <Button icon="pi pi-pencil" size="small" text severity="secondary" v-tooltip.left="'Edit'" @click="editParameter(row)" />
            <Button icon="pi pi-trash" size="small" text severity="danger" v-tooltip.left="'Delete from the file'" @click="deleteSelected([row])" />
          </div>
        </template>
      </Column>
      <template #empty>
        <div class="text-surface-500 p-4">
          {{ file?.path ? "No parameters match." : "No shared parameter file." }}
        </div>
      </template>
    </DataTable>

    <ParameterDialog v-model:visible="editVisible" :parameter="editTarget" />
    <GroupsDialog v-model:visible="groupsVisible" />
    <BindDialog v-model:visible="bindVisible" :rows="selectedInFile" />
  </div>
</template>
