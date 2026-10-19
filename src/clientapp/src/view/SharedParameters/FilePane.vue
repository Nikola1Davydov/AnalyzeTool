<script setup lang="ts">
/**
 * The shared parameter file as a tree: its groups, each with its parameters. Groups are how a file is
 * organised ("Doors", "For families"…) — so organising is dragging: parameters onto another group, or
 * onto "new group". The selection is shared with the project pane and the selection bar.
 */
import { computed, nextTick, ref } from "vue";
import { storeToRefs } from "pinia";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { dataTypeLabel, type SharedParameter } from "./types";
import { isOurDrag, readDrop, startDrag } from "./dnd";

const emit = defineEmits<{ edit: [parameter: SharedParameter | null, groupId?: number] }>();

const store = useSharedParametersStore();
const { file, groups, parameters, project, selection } = storeToRefs(store);

// ---- What each file parameter is in the project ---------------------------------------------------
const projectByGuid = computed(() => new Map(project.value.filter((p) => p.guid).map((p) => [p.guid!.toLowerCase(), p])));
function projectBadge(p: SharedParameter): { text: string; tip: string } | null {
  const pp = projectByGuid.value.get(p.guid.toLowerCase());
  if (!pp) return null;
  if (!pp.bound) return { text: "families", tip: "In the project only through loaded families" };
  const n = pp.categories.length;
  return { text: `${pp.isInstance ? "Inst" : "Type"} · ${n}`, tip: `${pp.isInstance ? "Instance" : "Type"}: ${pp.categories.join(", ")}` };
}

// ---- Filtering --------------------------------------------------------------------------------------
const search = ref("");
const notInProjectOnly = ref(false);

const tree = computed(() => {
  const q = search.value.trim().toLowerCase();
  const filtering = !!q || notInProjectOnly.value;
  return [...groups.value]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((g) => {
      const items = parameters.value
        .filter((p) => p.groupId === g.id)
        .filter((p) => !q || `${p.name} ${p.description} ${p.guid}`.toLowerCase().includes(q))
        .filter((p) => !notInProjectOnly.value || !projectByGuid.value.get(p.guid.toLowerCase())?.bound)
        .sort((a, b) => a.name.localeCompare(b.name));
      return { group: g, items, total: parameters.value.filter((p) => p.groupId === g.id).length };
    })
    // While filtering, a group with no match is noise; without a filter an empty group is a drop target.
    .filter((node) => !filtering || node.items.length || node.group.name.toLowerCase().includes(q));
});

const collapsed = ref(new Set<number>());
function toggle(id: number) {
  const next = new Set(collapsed.value);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  collapsed.value = next;
}
const allCollapsed = computed(() => groups.value.length > 0 && groups.value.every((g) => collapsed.value.has(g.id)));
function toggleAll() {
  collapsed.value = allCollapsed.value ? new Set() : new Set(groups.value.map((g) => g.id));
}
const isOpen = (id: number) => !collapsed.value.has(id) || !!search.value.trim();

// ---- Selection (keys shared with the store: "g:<guid>") ----------------------------------------------
const key = (p: SharedParameter) => `g:${p.guid.toLowerCase()}`;
const selected = computed(() => new Set(selection.value));
function setSelected(p: SharedParameter, on: boolean) {
  const k = key(p);
  selection.value = on ? [...selection.value.filter((x) => x !== k), k] : selection.value.filter((x) => x !== k);
}
function setGroupSelected(items: SharedParameter[], on: boolean) {
  const keys = new Set(items.map(key));
  selection.value = on ? [...new Set([...selection.value, ...keys])] : selection.value.filter((k) => !keys.has(k));
}
const groupState = (items: SharedParameter[]) => {
  const n = items.filter((p) => selected.value.has(key(p))).length;
  return { all: n > 0 && n === items.length, some: n > 0 && n < items.length };
};

// ---- Drag: the grabbed row, or the whole selection when the row is part of it ------------------------
function onDragStart(event: DragEvent, p: SharedParameter) {
  const guids = selected.value.has(key(p))
    ? parameters.value.filter((x) => selected.value.has(key(x))).map((x) => x.guid)
    : [p.guid];
  startDrag(event, { guids });
}

const dropTarget = ref<number | "new" | null>(null);
function onDragOver(event: DragEvent, target: number | "new") {
  if (!isOurDrag(event)) return;
  event.preventDefault();
  if (event.dataTransfer) event.dataTransfer.dropEffect = "move";
  dropTarget.value = target;
}
function onDragLeave(event: DragEvent, target: number | "new") {
  // Leaving for a child of the same target is not leaving it.
  const to = event.relatedTarget as Node | null;
  if (to && (event.currentTarget as HTMLElement).contains(to)) return;
  if (dropTarget.value === target) dropTarget.value = null;
}
function onDrop(event: DragEvent, target: number | "new") {
  dropTarget.value = null;
  const payload = readDrop(event);
  if (!payload) return;
  event.preventDefault();
  let groupId: number;
  if (target === "new") {
    const group = store.addGroup(uniqueGroupName("New group"));
    groupId = group.id;
    startRename(group.id, group.name);
  } else groupId = target;
  store.moveParameters(payload.guids, groupId);
  const next = new Set(collapsed.value);
  next.delete(groupId);
  collapsed.value = next;
}

// ---- Groups: create, rename inline, delete when empty --------------------------------------------------
function uniqueGroupName(base: string) {
  const taken = new Set(groups.value.map((g) => g.name.toLowerCase()));
  let name = base;
  for (let i = 2; taken.has(name.toLowerCase()); i++) name = `${base} ${i}`;
  return name;
}

const renaming = ref<{ id: number; name: string } | null>(null);
const renameInput = ref<HTMLInputElement[] | null>(null);
function startRename(id: number, name: string) {
  renaming.value = { id, name };
  nextTick(() => {
    const el = renameInput.value?.[0];
    el?.focus();
    el?.select();
  });
}
const renameError = computed(() => {
  const r = renaming.value;
  if (!r) return "";
  const n = r.name.trim().toLowerCase();
  if (!n) return "A group needs a name";
  return groups.value.some((g) => g.id !== r.id && g.name.trim().toLowerCase() === n) ? "Name already used" : "";
});
function commitRename() {
  const r = renaming.value;
  if (!r) return;
  if (!renameError.value) store.renameGroup(r.id, r.name);
  renaming.value = null;
}

function addGroup() {
  const group = store.addGroup(uniqueGroupName("New group"));
  startRename(group.id, group.name);
}

function deleteParameter(p: SharedParameter) {
  const inProject = projectByGuid.value.get(p.guid.toLowerCase());
  const note = inProject ? "\n\nIt stays in the project — this only removes it from the file." : "";
  if (window.confirm(`Delete "${p.name}" from the shared parameter file?${note}`)) store.deleteParameters([p.guid]);
}
</script>

<template>
  <section class="flex flex-col min-h-0 rounded-xl border border-surface-200 bg-surface-0">
    <header class="flex flex-wrap items-center gap-2 px-3 py-2 border-b border-surface-200">
      <span class="font-semibold text-sm mr-1"><i class="pi pi-file mr-1 text-surface-500" />File</span>
      <IconField class="grow min-w-40">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Search the file…" size="small" class="w-full" />
      </IconField>
      <label class="flex items-center gap-1.5 text-xs text-surface-600" v-tooltip.top="'Only parameters the project has not bound'">
        <Checkbox v-model="notInProjectOnly" binary />Not in project
      </label>
      <Button
        :icon="allCollapsed ? 'pi pi-angle-double-down' : 'pi pi-angle-double-up'"
        size="small"
        text
        severity="secondary"
        v-tooltip.top="allCollapsed ? 'Expand all' : 'Collapse all'"
        @click="toggleAll"
      />
      <Button icon="pi pi-folder-plus" size="small" text severity="secondary" :disabled="!file?.path" v-tooltip.top="'New group'" @click="addGroup" />
      <Button label="Parameter" icon="pi pi-plus" size="small" :disabled="!file?.path" @click="emit('edit', null)" />
    </header>

    <div class="overflow-y-auto grow text-sm">
      <div
        v-for="node in tree"
        :key="node.group.id"
        class="border-b border-surface-100"
        :class="{ 'drop-target': dropTarget === node.group.id }"
        @dragover="onDragOver($event, node.group.id)"
        @dragleave="onDragLeave($event, node.group.id)"
        @drop="onDrop($event, node.group.id)"
      >
        <!-- Group header -->
        <div class="group/header sticky top-0 z-[1] flex items-center gap-2 px-2 py-1.5 bg-surface-50">
          <button class="w-5 text-surface-500" @click="toggle(node.group.id)">
            <i class="pi text-xs" :class="isOpen(node.group.id) ? 'pi-chevron-down' : 'pi-chevron-right'" />
          </button>
          <Checkbox
            v-if="node.items.length"
            binary
            :modelValue="groupState(node.items).all"
            :indeterminate="groupState(node.items).some"
            @update:modelValue="setGroupSelected(node.items, !!$event)"
          />
          <template v-if="renaming?.id === node.group.id">
            <input
              ref="renameInput"
              v-model="renaming.name"
              class="grow min-w-0 px-1.5 py-0.5 rounded border text-sm font-semibold"
              :class="renameError ? 'border-red-400' : 'border-primary-400'"
              @keydown.enter="commitRename"
              @keydown.esc="renaming = null"
              @blur="commitRename"
            />
            <small v-if="renameError" class="text-red-600 shrink-0">{{ renameError }}</small>
          </template>
          <span
            v-else
            class="grow min-w-0 truncate font-semibold cursor-pointer"
            v-tooltip.top="'Double-click to rename'"
            @click="toggle(node.group.id)"
            @dblclick.stop="startRename(node.group.id, node.group.name)"
          >
            {{ node.group.name }}
          </span>
          <span class="text-xs text-surface-500">{{ node.total }}</span>
          <span class="flex opacity-0 group-hover/header:opacity-100">
            <Button icon="pi pi-plus" size="small" text severity="secondary" v-tooltip.top="'New parameter in this group'" @click="emit('edit', null, node.group.id)" />
            <Button icon="pi pi-pencil" size="small" text severity="secondary" v-tooltip.top="'Rename group'" @click="startRename(node.group.id, node.group.name)" />
            <Button
              icon="pi pi-trash"
              size="small"
              text
              severity="danger"
              :disabled="node.total > 0"
              v-tooltip.top="node.total > 0 ? 'Move or delete its parameters first' : 'Delete group'"
              @click="store.deleteGroup(node.group.id)"
            />
          </span>
        </div>

        <!-- Parameters -->
        <template v-if="isOpen(node.group.id)">
          <div
            v-for="p in node.items"
            :key="p.guid"
            draggable="true"
            class="group/row flex items-center gap-2 pl-9 pr-2 py-1 cursor-grab hover:bg-surface-50"
            :class="{ 'bg-primary-50': selected.has(key(p)) }"
            @dragstart="onDragStart($event, p)"
            @dblclick="emit('edit', p)"
          >
            <Checkbox binary :modelValue="selected.has(key(p))" @update:modelValue="setSelected(p, !!$event)" />
            <span class="grow min-w-0">
              <span class="block truncate">{{ p.name }}</span>
              <span v-if="p.description" class="block truncate text-xs text-surface-500">{{ p.description }}</span>
            </span>
            <span class="text-xs text-surface-500 w-20 truncate text-right">{{ dataTypeLabel(p.dataType) }}</span>
            <span class="w-20 text-right">
              <span
                v-if="projectBadge(p)"
                class="text-[11px] px-1.5 py-0.5 rounded bg-green-50 text-green-700 border border-green-200 whitespace-nowrap"
                v-tooltip.left="projectBadge(p)!.tip"
              >
                {{ projectBadge(p)!.text }}
              </span>
            </span>
            <span class="flex opacity-0 group-hover/row:opacity-100">
              <Button icon="pi pi-pencil" size="small" text severity="secondary" @click="emit('edit', p)" />
              <Button icon="pi pi-trash" size="small" text severity="danger" @click="deleteParameter(p)" />
            </span>
          </div>
          <div v-if="!node.items.length" class="pl-9 py-2 text-xs text-surface-400">
            {{ node.total ? "No match here." : "Empty — drag parameters here." }}
          </div>
        </template>
      </div>

      <!-- Drop here: a new group, named right after -->
      <div
        v-if="file?.path"
        class="m-2 p-3 rounded-lg border-2 border-dashed text-center text-xs text-surface-500"
        :class="dropTarget === 'new' ? 'border-primary-400 bg-primary-50 text-primary-700' : 'border-surface-200'"
        @dragover="onDragOver($event, 'new')"
        @dragleave="onDragLeave($event, 'new')"
        @drop="onDrop($event, 'new')"
      >
        <i class="pi pi-folder-plus mr-1" />Drop here to put them in a new group
      </div>
      <div v-if="!file?.path" class="p-6 text-center text-surface-500">
        No shared parameter file — open one or create a new one above.
      </div>
    </div>
  </section>
</template>

<style scoped>
.drop-target {
  outline: 2px solid var(--p-primary-400);
  outline-offset: -2px;
  background: var(--p-primary-50);
}
</style>
