<script setup lang="ts">
/**
 * What the open project has: its bound parameters by category, then the shared ones only loaded
 * families carry. Dropping file parameters here adds them to the project — onto a category for that
 * category, onto the zone at the top for the full dialog.
 */
import { computed, ref } from "vue";
import { storeToRefs } from "pinia";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import type { ProjectParameter } from "./types";
import { isOurDrag, readDrop, startDrag } from "./dnd";

const emit = defineEmits<{
  /** Dropped on a category: bind there after a short confirmation. */
  quickBind: [guids: string[], categoryName: string];
  /** Dropped on the general zone: the full "Add to project" dialog. */
  bind: [guids: string[]];
}>();

const store = useSharedParametersStore();
const { project, parameters: fileParameters, selection } = storeToRefs(store);

const inFile = computed(() => new Set(fileParameters.value.map((p) => p.guid.toLowerCase())));
function origin(p: ProjectParameter): { text: string; cls: string; tip: string } {
  if (!p.guid) return { text: "project", cls: "bg-surface-100 text-surface-600", tip: "Project parameter — no shared parameter file has it" };
  if (inFile.value.has(p.guid.toLowerCase())) return { text: "file", cls: "bg-green-50 text-green-700", tip: "From the open shared parameter file" };
  return { text: "other file", cls: "bg-amber-50 text-amber-700", tip: "Shared, but from another shared parameter file" };
}

const search = ref("");
const matches = (p: ProjectParameter) => {
  const q = search.value.trim().toLowerCase();
  return !q || `${p.name} ${p.categories.join(" ")}`.toLowerCase().includes(q);
};

// One section per category the project binds parameters to; a parameter bound to three categories is
// in three sections — that is the question this pane answers ("what do doors have?").
const byCategory = computed(() => {
  const map = new Map<string, ProjectParameter[]>();
  for (const p of project.value)
    if (p.bound && matches(p))
      for (const c of p.categories) map.set(c, [...(map.get(c) ?? []), p]);
  const q = search.value.trim().toLowerCase();
  // A search that names a category keeps it as a section even when none of its parameters match.
  if (q)
    for (const p of project.value)
      if (p.bound) for (const c of p.categories) if (c.toLowerCase().includes(q) && !map.has(c)) map.set(c, project.value.filter((x) => x.bound && x.categories.includes(c)));
  return [...map.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, list]) => ({ name, items: list.sort((a, b) => a.name.localeCompare(b.name)) }));
});
const familiesOnly = computed(() => project.value.filter((p) => !p.bound && matches(p)));

// Many categories (one shared parameter in a sample model is bound to 150) start folded.
const expanded = ref(new Set<string>());
const startFolded = computed(() => byCategory.value.length > 12);
const isOpen = (name: string) => !!search.value.trim() || (startFolded.value ? expanded.value.has(name) : !expanded.value.has(name));
function toggle(name: string) {
  const next = new Set(expanded.value);
  if (next.has(name)) next.delete(name);
  else next.add(name);
  expanded.value = next;
}

// ---- Selection: the same keys as the table had — "g:<guid>" or "p:<id>" for project parameters --------
const key = (p: ProjectParameter) => (p.guid ? `g:${p.guid.toLowerCase()}` : `p:${p.id}`);
const selected = computed(() => new Set(selection.value));
function setSelected(p: ProjectParameter, on: boolean) {
  const k = key(p);
  selection.value = on ? [...selection.value.filter((x) => x !== k), k] : selection.value.filter((x) => x !== k);
}

// A parameter of the file can be dragged from here too — onto another category, to add that one.
function onDragStart(event: DragEvent, p: ProjectParameter) {
  if (!p.guid || !inFile.value.has(p.guid.toLowerCase())) {
    event.preventDefault();
    return;
  }
  startDrag(event, { guids: [p.guid] });
}

const dropTarget = ref<string | null>(null);
function onDragOver(event: DragEvent, target: string) {
  if (!isOurDrag(event)) return;
  event.preventDefault();
  if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
  dropTarget.value = target;
}
function onDragLeave(event: DragEvent, target: string) {
  const to = event.relatedTarget as Node | null;
  if (to && (event.currentTarget as HTMLElement).contains(to)) return;
  if (dropTarget.value === target) dropTarget.value = null;
}
function onDrop(event: DragEvent, target: string | null) {
  dropTarget.value = null;
  const payload = readDrop(event);
  if (!payload) return;
  event.preventDefault();
  if (target) emit("quickBind", payload.guids, target);
  else emit("bind", payload.guids);
}
</script>

<template>
  <section class="flex flex-col min-h-0 rounded-xl border border-surface-200 bg-surface-0">
    <header class="flex items-center gap-2 px-3 py-2 border-b border-surface-200">
      <span class="font-semibold text-sm mr-1"><i class="pi pi-building mr-1 text-surface-500" />Project</span>
      <IconField class="grow">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Search parameter or category…" size="small" class="w-full" />
      </IconField>
    </header>

    <div class="overflow-y-auto grow text-sm">
      <!-- The general drop zone -->
      <div
        class="m-2 p-3 rounded-lg border-2 border-dashed text-center text-xs"
        :class="dropTarget === '*' ? 'border-primary-400 bg-primary-50 text-primary-700' : 'border-surface-200 text-surface-500'"
        @dragover="onDragOver($event, '*')"
        @dragleave="onDragLeave($event, '*')"
        @drop="onDrop($event, null)"
      >
        <i class="pi pi-sign-in mr-1" />Drop parameters from the file here to add them to the project —
        or onto a category below
      </div>

      <div
        v-for="cat in byCategory"
        :key="cat.name"
        class="border-b border-surface-100"
        :class="{ 'drop-target': dropTarget === cat.name }"
        @dragover="onDragOver($event, cat.name)"
        @dragleave="onDragLeave($event, cat.name)"
        @drop="onDrop($event, cat.name)"
      >
        <div class="sticky top-0 z-[1] flex items-center gap-2 px-2 py-1.5 bg-surface-50 cursor-pointer" @click="toggle(cat.name)">
          <i class="pi text-xs w-5 text-center text-surface-500" :class="isOpen(cat.name) ? 'pi-chevron-down' : 'pi-chevron-right'" />
          <span class="grow truncate font-semibold">{{ cat.name }}</span>
          <span class="text-xs text-surface-500">{{ cat.items.length }}</span>
        </div>
        <template v-if="isOpen(cat.name)">
          <div
            v-for="p in cat.items"
            :key="cat.name + key(p)"
            :draggable="!!p.guid && inFile.has(p.guid.toLowerCase())"
            class="flex items-center gap-2 pl-9 pr-3 py-1 hover:bg-surface-50"
            :class="{ 'bg-primary-50': selected.has(key(p)) }"
            @dragstart="onDragStart($event, p)"
          >
            <Checkbox binary :modelValue="selected.has(key(p))" @update:modelValue="setSelected(p, !!$event)" />
            <span class="grow min-w-0 truncate">{{ p.name }}</span>
            <span class="text-xs text-surface-500">{{ p.isInstance ? "Instance" : "Type" }}</span>
            <span class="text-[11px] px-1.5 py-0.5 rounded w-16 text-center" :class="origin(p).cls" v-tooltip.left="origin(p).tip">
              {{ origin(p).text }}
            </span>
          </div>
        </template>
      </div>

      <!-- Shared parameters only loaded families bring in -->
      <div v-if="familiesOnly.length" class="border-b border-surface-100">
        <div class="sticky top-0 z-[1] flex items-center gap-2 px-2 py-1.5 bg-surface-50 cursor-pointer" @click="toggle('__families')">
          <i class="pi text-xs w-5 text-center text-surface-500" :class="isOpen('__families') ? 'pi-chevron-down' : 'pi-chevron-right'" />
          <span class="grow truncate font-semibold text-surface-600">Only in loaded families</span>
          <span class="text-xs text-surface-500">{{ familiesOnly.length }}</span>
        </div>
        <template v-if="isOpen('__families')">
          <div v-for="p in familiesOnly" :key="key(p)" class="flex items-center gap-2 pl-9 pr-3 py-1 text-surface-500">
            <span class="grow min-w-0 truncate">{{ p.name }}</span>
            <span class="text-[11px] px-1.5 py-0.5 rounded w-16 text-center" :class="origin(p).cls">{{ origin(p).text }}</span>
          </div>
        </template>
      </div>

      <div v-if="!byCategory.length && !familiesOnly.length" class="p-6 text-center text-surface-500">
        {{ search ? "Nothing matches." : "The project has no shared or project parameters yet." }}
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
