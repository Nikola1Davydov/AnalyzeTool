<script setup lang="ts">
/**
 * Choose the report's parameters. Left: everything the project has bound, searchable and grouped — by
 * the shared parameter file's groups, by category or by binding — with a whole group tickable at once.
 * Right: the chosen ones in report order, to reorder or drop. Works on a copy; Apply hands it back.
 */
import { computed, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { refKey } from "@/stores/useParameterSetsStore";
import type { ParameterRef } from "../SharedParameters/types";

const props = defineProps<{ modelValue: ParameterRef[] }>();
const emit = defineEmits<{ apply: [refs: ParameterRef[]] }>();
const visible = defineModel<boolean>("visible", { required: true });

const shared = useSharedParametersStore();
const { project, parameters: fileParameters, groups: fileGroups } = storeToRefs(shared);

interface Item {
  key: string;
  ref: ParameterRef;
  name: string;
  binding: string;
  categories: string[];
  fileGroup: string;
}

// Everything the report can count: what the project has bound to categories.
const items = computed<Item[]>(() => {
  const groupName = new Map(fileGroups.value.map((g) => [g.id, g.name]));
  const fileGroupByGuid = new Map(fileParameters.value.map((p) => [p.guid.toLowerCase(), groupName.get(p.groupId) ?? ""]));
  return project.value
    .filter((p) => p.bound)
    .map((p) => {
      const ref: ParameterRef = { guid: p.guid, name: p.name };
      const guid = p.guid?.toLowerCase();
      return {
        key: refKey(ref),
        ref,
        name: p.name,
        binding: p.isInstance ? "Instance" : "Type",
        categories: p.categories,
        fileGroup: guid ? fileGroupByGuid.get(guid) || "Shared, from another file" : "Project parameters",
      };
    })
    .sort((a, b) => a.name.localeCompare(b.name));
});
const itemByKey = computed(() => new Map(items.value.map((i) => [i.key, i])));

// ---- The working copy ---------------------------------------------------------------------------
const chosen = ref<ParameterRef[]>([]);
const chosenKeys = computed(() => new Set(chosen.value.map(refKey)));
watch(visible, (open) => {
  if (open) {
    chosen.value = props.modelValue.map((r) => ({ ...r }));
    search.value = "";
  }
});

function toggle(item: Item, on: boolean) {
  if (on && !chosenKeys.value.has(item.key)) chosen.value.push({ ...item.ref });
  if (!on) chosen.value = chosen.value.filter((r) => refKey(r) !== item.key);
}

// ---- Left: search and grouping -------------------------------------------------------------------
type GroupBy = "fileGroup" | "category" | "binding";
const groupBy = ref<GroupBy>("fileGroup");
const groupByOptions = [
  { label: "File group", value: "fileGroup" },
  { label: "Category", value: "category" },
  { label: "Binding", value: "binding" },
];
const search = ref("");
const collapsed = ref(new Set<string>());

const groups = computed(() => {
  const q = search.value.trim().toLowerCase();
  const map = new Map<string, Item[]>();
  for (const item of items.value) {
    if (q && !`${item.name} ${item.categories.join(" ")} ${item.fileGroup}`.toLowerCase().includes(q)) continue;
    const names =
      groupBy.value === "category"
        ? item.categories.length ? item.categories : ["(no category)"]
        : [groupBy.value === "binding" ? item.binding : item.fileGroup || "(no group)"];
    for (const name of names) map.set(name, [...(map.get(name) ?? []), item]);
  }
  return [...map.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, list]) => {
      const picked = list.filter((i) => chosenKeys.value.has(i.key)).length;
      return { name, items: list, picked, all: picked === list.length, some: picked > 0 && picked < list.length };
    });
});

function toggleGroup(group: { items: Item[]; all: boolean }) {
  for (const item of group.items) toggle(item, !group.all);
}

function toggleCollapsed(name: string) {
  const next = new Set(collapsed.value);
  if (next.has(name)) next.delete(name);
  else next.add(name);
  collapsed.value = next;
}

// ---- Right: order ----------------------------------------------------------------------------------
function move(index: number, delta: number) {
  const to = index + delta;
  if (to < 0 || to >= chosen.value.length) return;
  const list = [...chosen.value];
  [list[index], list[to]] = [list[to], list[index]];
  chosen.value = list;
}

function sortByName() {
  chosen.value = [...chosen.value].sort((a, b) => a.name.localeCompare(b.name));
}

/** A few names, or a count — one shared parameter here is bound to 150 categories. */
function categoriesText(categories: string[]) {
  return categories.length <= 3 ? categories.join(", ") : `${categories.length} categories`;
}

function apply() {
  emit("apply", chosen.value);
  visible.value = false;
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    header="Choose parameters"
    :style="{ width: 'min(64rem, 96vw)' }"
    :contentStyle="{ padding: 0 }"
  >
    <div class="grid grid-cols-[3fr_2fr] h-[min(36rem,70vh)] border-y border-surface-200 text-sm">
      <!-- Available -->
      <div class="flex flex-col min-h-0 border-r border-surface-200">
        <div class="p-3 flex flex-wrap items-center gap-2 border-b border-surface-200">
          <IconField class="grow min-w-48">
            <InputIcon class="pi pi-search" />
            <InputText v-model="search" placeholder="Search name, category, group…" size="small" class="w-full" autofocus />
          </IconField>
          <SelectButton v-model="groupBy" :options="groupByOptions" optionLabel="label" optionValue="value" :allowEmpty="false" size="small" />
        </div>

        <div class="overflow-y-auto grow">
          <div v-for="group in groups" :key="group.name">
            <div class="sticky top-0 z-[1] flex items-center gap-2 px-3 py-1.5 bg-surface-50 border-b border-surface-200">
              <button class="text-surface-500 w-4" @click="toggleCollapsed(group.name)">
                <i class="pi text-xs" :class="collapsed.has(group.name) && !search ? 'pi-chevron-right' : 'pi-chevron-down'" />
              </button>
              <Checkbox
                :modelValue="group.all"
                :indeterminate="group.some"
                binary
                v-tooltip.top="group.all ? 'Untick the whole group' : 'Tick the whole group'"
                @update:modelValue="toggleGroup(group)"
              />
              <span class="font-semibold grow truncate cursor-pointer" @click="toggleCollapsed(group.name)">{{ group.name }}</span>
              <span class="text-xs text-surface-500">{{ group.picked }}/{{ group.items.length }}</span>
            </div>
            <template v-if="!collapsed.has(group.name) || search">
              <label
                v-for="item in group.items"
                :key="group.name + item.key"
                class="flex items-center gap-2 pl-9 pr-3 py-1.5 hover:bg-surface-50 cursor-pointer"
              >
                <Checkbox :modelValue="chosenKeys.has(item.key)" binary @update:modelValue="toggle(item, !!$event)" />
                <span class="grow min-w-0">
                  <span class="block truncate">{{ item.name }}</span>
                  <span class="block text-xs text-surface-500 truncate">
                    {{ item.binding }} · {{ categoriesText(item.categories) }}
                  </span>
                </span>
              </label>
            </template>
          </div>
          <div v-if="!groups.length" class="p-6 text-center text-surface-500">
            {{ items.length ? "Nothing matches." : "The project has no bound shared or project parameters." }}
          </div>
        </div>
      </div>

      <!-- Chosen, in report order -->
      <div class="flex flex-col min-h-0">
        <div class="p-3 flex items-center gap-2 border-b border-surface-200">
          <span class="font-semibold grow">In the report <span class="text-surface-500 font-normal">({{ chosen.length }})</span></span>
          <Button label="A–Z" size="small" text severity="secondary" :disabled="chosen.length < 2" v-tooltip.top="'Sort by name'" @click="sortByName" />
          <Button label="Clear" size="small" text severity="secondary" :disabled="!chosen.length" @click="chosen = []" />
        </div>
        <div class="overflow-y-auto grow">
          <div
            v-for="(r, i) in chosen"
            :key="refKey(r)"
            class="group flex items-center gap-2 px-3 py-1.5 border-b border-surface-100"
            :class="{ 'text-surface-400': !itemByKey.has(refKey(r)) }"
          >
            <span class="w-6 text-right text-xs text-surface-400">{{ i + 1 }}</span>
            <span class="grow min-w-0">
              <span class="block truncate">{{ r.name }}</span>
              <span v-if="!itemByKey.has(refKey(r))" class="block text-xs">not bound in this project — skipped</span>
            </span>
            <span class="flex opacity-40 group-hover:opacity-100">
              <Button icon="pi pi-arrow-up" size="small" text severity="secondary" :disabled="i === 0" @click="move(i, -1)" />
              <Button icon="pi pi-arrow-down" size="small" text severity="secondary" :disabled="i === chosen.length - 1" @click="move(i, 1)" />
              <Button icon="pi pi-times" size="small" text severity="danger" @click="chosen.splice(i, 1)" />
            </span>
          </div>
          <div v-if="!chosen.length" class="p-6 text-center text-surface-500">
            Tick parameters on the left.<br />They appear here in report order.
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="visible = false" />
      <Button label="Apply" icon="pi pi-check" @click="apply" />
    </template>
  </Dialog>
</template>
