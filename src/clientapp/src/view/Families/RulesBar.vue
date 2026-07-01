<script setup lang="ts">
import { computed, ref } from "vue";
import { useFamilyRules } from "./familyRules";
import RuleBuilderDialog from "./RuleBuilderDialog.vue";
import type { FilterRule, RuleScope } from "./types";

// Quick-navigation bar: pinned rules of this scope render as toggle chips; clicking one sets it as the
// active filter (v-model activeRuleId). A manage panel lists every rule of the scope for apply / edit /
// pin / delete. The same store backs the table+gallery (families scope) and the worksets view (instances).
// `storageKey` selects which persisted rule set to use — omit for the shared Family Manager rules, or
// pass a distinct key (e.g. the placement palette) to keep a separate, independently saved set.
// `variant` splits the two responsibilities: "chips" shows only the pinned quick-filter chips (for a
// panel), "manage" shows the create/edit/pin/delete list (for a settings dialog), "full" (default) is
// the original inline bar that does both.
const props = withDefaults(
  defineProps<{ scope: RuleScope; storageKey?: string; variant?: "full" | "chips" | "manage" }>(),
  { variant: "full" },
);
const activeRuleId = defineModel<string | null>("activeRuleId", { default: null });

const { rules, emptyRule, upsert, remove } = useFamilyRules(props.storageKey);

const scopeRules = computed(() => rules.filter((r) => r.scope === props.scope));
const pinned = computed(() => scopeRules.value.filter((r) => r.pinned));

const builderVisible = ref(false);
const editing = ref<FilterRule | null>(null);
const manageOpen = ref(false);

function toggle(id: string) {
  activeRuleId.value = activeRuleId.value === id ? null : id;
}
function newRule() {
  editing.value = emptyRule(props.scope);
  builderVisible.value = true;
}
function edit(rule: FilterRule) {
  editing.value = rule;
  builderVisible.value = true;
}
function onSave(rule: FilterRule) {
  upsert(rule);
}
function del(rule: FilterRule) {
  if (activeRuleId.value === rule.id) activeRuleId.value = null;
  remove(rule.id);
}
function togglePin(rule: FilterRule) {
  upsert({ ...rule, pinned: !rule.pinned });
}
</script>

<template>
  <!-- CHIPS: only pinned quick-filter chips (for a panel) -->
  <div v-if="variant === 'chips'" class="flex items-center gap-2 flex-wrap">
    <template v-if="pinned.length">
      <i class="pi pi-bolt text-surface-400 text-sm" v-tooltip.top="'Quick filters'" />
      <button
        v-for="rule in pinned"
        :key="rule.id"
        type="button"
        class="px-2.5 py-1 rounded-full text-xs font-medium border transition-colors"
        :class="
          activeRuleId === rule.id
            ? 'bg-primary-500 border-primary-500 text-white'
            : 'bg-surface-0 border-surface-300 text-surface-600 hover:border-primary-400'
        "
        @click="toggle(rule.id)"
      >
        {{ rule.name }}
      </button>
    </template>
    <span v-else class="text-xs text-surface-400">No quick filters — add rules in settings.</span>
  </div>

  <!-- MANAGE: create / edit / pin / delete list (for a settings dialog) -->
  <div v-else-if="variant === 'manage'" class="flex flex-col gap-1">
    <div class="flex items-center justify-between">
      <span class="text-xs text-surface-500">Rules</span>
      <Button icon="pi pi-plus" label="Rule" text size="small" @click="newRule" />
    </div>
    <div v-if="!scopeRules.length" class="text-surface-500 text-sm p-2">
      No rules yet. Create one with “+ Rule”. Pin a rule to show it as a quick filter on the panel.
    </div>
    <div
      v-for="rule in scopeRules"
      :key="rule.id"
      class="flex items-center gap-2 px-2 py-1 rounded hover:bg-surface-100"
    >
      <button
        type="button"
        class="grow text-left text-sm"
        :class="activeRuleId === rule.id ? 'font-semibold text-primary-600' : ''"
        @click="toggle(rule.id)"
      >
        {{ rule.name }}
        <span class="text-surface-400 text-xs">
          · {{ rule.match === "all" ? "all" : "any" }} of {{ rule.conditions.length }}
        </span>
      </button>
      <Button
        :icon="rule.pinned ? 'pi pi-bookmark-fill' : 'pi pi-bookmark'"
        text
        rounded
        size="small"
        severity="secondary"
        v-tooltip.top="rule.pinned ? 'Unpin' : 'Pin as quick filter'"
        @click="togglePin(rule)"
      />
      <Button icon="pi pi-pencil" text rounded size="small" severity="secondary" @click="edit(rule)" />
      <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click="del(rule)" />
    </div>

    <RuleBuilderDialog v-model:visible="builderVisible" :rule="editing" @save="onSave" />
  </div>

  <!-- FULL: original inline bar (default; Family Manager) -->
  <div v-else class="flex items-center gap-2 flex-wrap">
    <i class="pi pi-bolt text-surface-400 text-sm" v-tooltip.top="'Quick filters'" />

    <!-- Pinned rule chips -->
    <button
      v-for="rule in pinned"
      :key="rule.id"
      type="button"
      class="px-2.5 py-1 rounded-full text-xs font-medium border transition-colors"
      :class="
        activeRuleId === rule.id
          ? 'bg-primary-500 border-primary-500 text-white'
          : 'bg-surface-0 border-surface-300 text-surface-600 hover:border-primary-400'
      "
      @click="toggle(rule.id)"
    >
      {{ rule.name }}
    </button>

    <Button icon="pi pi-plus" label="Rule" text size="small" @click="newRule" />
    <Button
      icon="pi pi-cog"
      text
      size="small"
      severity="secondary"
      :badge="scopeRules.length ? String(scopeRules.length) : undefined"
      @click="manageOpen = !manageOpen"
    />

    <!-- Manage panel -->
    <div
      v-if="manageOpen"
      class="w-full mt-2 rounded-lg border border-surface-200 bg-surface-0 p-2 flex flex-col gap-1"
    >
      <div v-if="!scopeRules.length" class="text-surface-500 text-sm p-2">
        No rules yet. Create one with “+ Rule”.
      </div>
      <div
        v-for="rule in scopeRules"
        :key="rule.id"
        class="flex items-center gap-2 px-2 py-1 rounded hover:bg-surface-100"
      >
        <button
          type="button"
          class="grow text-left text-sm"
          :class="activeRuleId === rule.id ? 'font-semibold text-primary-600' : ''"
          @click="toggle(rule.id)"
        >
          {{ rule.name }}
          <span class="text-surface-400 text-xs">
            · {{ rule.match === "all" ? "all" : "any" }} of {{ rule.conditions.length }}
          </span>
        </button>
        <Button
          :icon="rule.pinned ? 'pi pi-bookmark-fill' : 'pi pi-bookmark'"
          text
          rounded
          size="small"
          severity="secondary"
          v-tooltip.top="rule.pinned ? 'Unpin' : 'Pin as quick filter'"
          @click="togglePin(rule)"
        />
        <Button
          icon="pi pi-pencil"
          text
          rounded
          size="small"
          severity="secondary"
          @click="edit(rule)"
        />
        <Button icon="pi pi-trash" text rounded size="small" severity="danger" @click="del(rule)" />
      </div>
    </div>

    <RuleBuilderDialog v-model:visible="builderVisible" :rule="editing" @save="onSave" />
  </div>
</template>
