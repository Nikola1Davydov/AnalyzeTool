<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "SavedRulesDrawer",
});
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { storeToRefs } from "pinia";
import { useDocumentDataStore } from "@/stores/useDocumentDataStore";
import type { SavedRule } from "@/view/ConnectParameters/ruleTypes";

const props = defineProps<{
  visible: boolean;
  rules: SavedRule[];
  selectedRuleId: string | null;
  ruleName: string;
  groupTag: string;
}>();

const emit = defineEmits<{
  "update:visible": [value: boolean];
  "update:ruleName": [value: string];
  "update:groupTag": [value: string];
  load: [rule: SavedRule];
  rename: [ruleId: string];
  delete: [ruleId: string];
  saveNew: [];
  updateSelected: [];
  exportJson: [];
  importJson: [];
}>();

function closeDrawer() {
  emit("update:visible", false);
}

const documentDataStore = useDocumentDataStore();
const { documentName } = storeToRefs(documentDataStore);

const showAllProjects = ref(false);

function normalizeProjectName(value: string | null | undefined): string {
  return (value || "").trim().toLowerCase();
}

const currentProjectName = computed(() => documentName.value.trim());

const rulesByProjectScope = computed(() => {
  if (showAllProjects.value) return props.rules;

  const normalizedCurrent = normalizeProjectName(currentProjectName.value);
  if (!normalizedCurrent) return props.rules;

  return props.rules.filter((rule) => normalizeProjectName(rule.projectTag) === normalizedCurrent);
});

const groupBy = ref<"all" | "category" | "mode" | "project">("all");
const selectedGroup = ref("all");
const groupTagSuggestions = ref<string[]>([]);

const availableGroupTags = computed(() =>
  Array.from(
    new Set(
      props.rules.map((rule) => rule.groupTag?.trim() || "").filter((value) => value.length > 0),
    ),
  ).sort((a, b) => a.localeCompare(b)),
);

function searchGroupTags(event: { query?: string }) {
  const query = (event.query || "").trim().toLowerCase();
  groupTagSuggestions.value = availableGroupTags.value.filter((value) =>
    !query ? true : value.toLowerCase().includes(query),
  );
}

function getRuleGroupingKey(rule: SavedRule, grouping: "category" | "mode" | "project"): string {
  if (grouping === "category") return rule.categoryName || "No category";
  if (grouping === "mode") return rule.mode || "No mode";
  return rule.projectTag || "No project";
}

const groupOptions = computed(() => {
  const grouping = groupBy.value;
  if (grouping === "all") return [];

  const map = new Map<string, number>();
  for (const rule of rulesByProjectScope.value) {
    const key = getRuleGroupingKey(rule, grouping);
    map.set(key, (map.get(key) || 0) + 1);
  }

  return Array.from(map.entries())
    .map(([value, count]) => ({ value, label: `${value} (${count})` }))
    .sort((a, b) => a.value.localeCompare(b.value));
});

const visibleRules = computed(() => {
  const grouping = groupBy.value;
  if (grouping === "all" || selectedGroup.value === "all") {
    return rulesByProjectScope.value;
  }

  return rulesByProjectScope.value.filter((rule) => {
    const key = getRuleGroupingKey(rule, grouping);
    return key === selectedGroup.value;
  });
});

watch(groupBy, () => {
  selectedGroup.value = "all";
});

watch(
  groupOptions,
  (next) => {
    if (selectedGroup.value === "all") return;
    const exists = next.some((x) => x.value === selectedGroup.value);
    if (!exists) selectedGroup.value = "all";
  },
  { immediate: true },
);
</script>

<template>
  <Drawer
    :visible="props.visible"
    position="right"
    header="Saved Rules"
    class="!w-full md:!w-80 lg:!w-[50rem]"
    @update:visible="(val) => emit('update:visible', !!val)"
  >
    <div class="flex h-full flex-col gap-4">
      <div class="rounded-xl border border-surface-200 bg-surface-0 p-3 flex flex-col gap-3">
        <div class="flex flex-row w-full gap-4">
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Rule Name</label>
            <InputText
              placeholder="e.g. Door Mark from Type + Prefix"
              :modelValue="props.ruleName"
              @update:modelValue="(val) => emit('update:ruleName', String(val || ''))"
            />
          </div>
          <div class="flex flex-col gap-1">
            <label class="text-sm font-medium">Group</label>
            <AutoComplete
              placeholder="e.g. Residential Core"
              :modelValue="props.groupTag"
              :suggestions="groupTagSuggestions"
              dropdown
              dropdownMode="blank"
              @complete="searchGroupTags"
              @update:modelValue="(val) => emit('update:groupTag', String(val || ''))"
            />
          </div>
        </div>

        <div class="flex flex-wrap gap-2">
          <Button icon="pi pi-save" label="Save New" size="small" @click="emit('saveNew')" />
          <Button
            icon="pi pi-pencil"
            label="Update Selected"
            size="small"
            outlined
            :disabled="!props.selectedRuleId"
            @click="emit('updateSelected')"
          />
          <Button
            icon="pi pi-download"
            label="Export JSON"
            size="small"
            outlined
            @click="emit('exportJson')"
          />
          <Button
            icon="pi pi-upload"
            label="Import JSON"
            size="small"
            outlined
            @click="emit('importJson')"
          />
        </div>
      </div>

      <div
        v-if="props.rules.length === 0"
        class="rounded-xl border border-dashed border-surface-300 p-4 text-sm text-surface-500"
      >
        No saved rules yet. Save the current builder as a new rule to create your first template.
      </div>

      <div v-else class="rounded-xl border border-surface-200 bg-surface-0 p-3 flex flex-col gap-3">
        <div class="flex items-center justify-between gap-3">
          <div class="text-xs text-surface-500">
            Project scope: {{ currentProjectName || "Unknown project" }}
          </div>
          <div class="flex flex-wrap gap-2">
            <Button
              label="Current project"
              size="small"
              :outlined="showAllProjects"
              :disabled="!currentProjectName"
              @click="showAllProjects = false"
            />
            <Button
              label="All projects"
              size="small"
              :outlined="!showAllProjects"
              @click="showAllProjects = true"
            />
          </div>
        </div>

        <div class="text-xs text-surface-500">Additional grouping</div>
        <div class="flex flex-wrap gap-2">
          <Button label="All" size="small" :outlined="groupBy !== 'all'" @click="groupBy = 'all'" />
          <Button
            label="Category"
            size="small"
            :outlined="groupBy !== 'category'"
            @click="groupBy = 'category'"
          />
          <Button
            label="Mode"
            size="small"
            :outlined="groupBy !== 'mode'"
            @click="groupBy = 'mode'"
          />
          <Button
            label="Project"
            size="small"
            :outlined="groupBy !== 'project'"
            @click="groupBy = 'project'"
          />
        </div>

        <div v-if="groupBy !== 'all'" class="flex flex-col gap-1">
          <label class="text-xs text-surface-500">Group</label>
          <Select
            :options="[{ value: 'all', label: 'All groups' }, ...groupOptions]"
            optionLabel="label"
            optionValue="value"
            :modelValue="selectedGroup"
            @update:modelValue="(val) => (selectedGroup = String(val || 'all'))"
          />
        </div>
      </div>

      <div
        v-if="visibleRules.length === 0"
        class="rounded-xl border border-surface-200 p-4 text-sm text-surface-500"
      >
        {{ showAllProjects ? "No rules in this selection." : "No rules for current project." }}
      </div>

      <div v-else class="flex flex-col gap-3 overflow-auto pr-1">
        <article
          v-for="rule in visibleRules"
          :key="rule.id"
          class="saved-rule-card rounded-xl border p-4 shadow-sm"
          :class="props.selectedRuleId === rule.id ? 'saved-rule-card--active' : ''"
        >
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <h4 class="truncate text-sm font-semibold">{{ rule.name }}</h4>
              <div class="mt-1 flex flex-wrap gap-2 text-xs text-surface-500">
                <span>{{ rule.targetParameterName || "No target" }}</span>
                <span>•</span>
                <span>{{ rule.mode }}</span>
              </div>
            </div>
            <Tag :value="rule.blocks.length + ' blocks'" severity="secondary" />
          </div>

          <div class="mt-3 flex flex-wrap gap-2 text-xs">
            <Tag :value="rule.projectTag || 'No project'" severity="warn" />
            <Tag :value="rule.groupTag || 'No group'" severity="secondary" />
            <Tag :value="rule.categoryName || 'No category'" severity="contrast" />
            <Tag :value="new Date(rule.updatedAt).toLocaleString()" severity="info" />
          </div>

          <div class="mt-4 flex flex-wrap gap-2">
            <Button size="small" label="Load" @click="emit('load', rule)" />
            <Button size="small" label="Rename" outlined @click="emit('rename', rule.id)" />
            <Button
              size="small"
              label="Delete"
              severity="danger"
              outlined
              @click="emit('delete', rule.id)"
            />
          </div>
        </article>
      </div>
    </div>
  </Drawer>
</template>

<style scoped>
.saved-rule-card {
  background: var(--p-surface-0, #ffffff);
  border-color: var(--p-surface-200, #e5e7eb);
}

.saved-rule-card--active {
  border-color: var(--p-primary-300, #93c5fd);
  box-shadow: 0 12px 24px -20px rgba(59, 130, 246, 0.45);
}
</style>
