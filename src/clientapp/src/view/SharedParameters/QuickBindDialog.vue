<script setup lang="ts">
/**
 * The one question a drop onto a category leaves open: instance or type (and, folded away, the group
 * in the Properties palette). A parameter the project already has keeps its categories and its binding
 * kind is preselected. With unsaved file edits the file is saved first — Revit reads it from disk.
 */
import { computed, ref, watch } from "vue";
import RadioButton from "primevue/radiobutton";
import { storeToRefs } from "pinia";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import { errorText } from "./types";

const props = defineProps<{ guids: string[]; categoryName: string }>();
const visible = defineModel<boolean>("visible", { required: true });

const store = useSharedParametersStore();
const { parameters, project, bindBlocker, dirty, bindingOptions } = storeToRefs(store);
const notifications = useNotificationStore();

const isInstance = ref(true);
const groupTypeId = ref("");
const showGroup = ref(false);
const busy = ref(false);

const names = computed(() =>
  props.guids.map((g) => parameters.value.find((p) => p.guid.toLowerCase() === g.toLowerCase())?.name ?? g),
);
const existing = computed(() =>
  project.value.filter((p) => p.bound && p.guid && props.guids.some((g) => g.toLowerCase() === p.guid!.toLowerCase())),
);

watch(visible, async (open) => {
  if (!open) return;
  if (existing.value.length) isInstance.value = existing.value[0].isInstance;
  else isInstance.value = true;
  const options = await store.loadBindingOptions();
  if (options && !groupTypeId.value) groupTypeId.value = options.defaultGroup;
});

// Only the "save first" blocker can be solved from here; the others are about which file is open.
const blocker = computed(() => (bindBlocker.value && !dirty.value ? bindBlocker.value : null));

async function apply() {
  busy.value = true;
  try {
    if (dirty.value && !(await store.save())) return;
    const options = await store.loadBindingOptions();
    const category = options?.categories.find((c) => c.name === props.categoryName);
    if (!category) {
      notifications.error(`Category "${props.categoryName}" does not accept parameters.`);
      return;
    }
    const res = await store.bind(props.guids, [category.id], isInstance.value, groupTypeId.value);
    if (res.added.length + res.updated.length)
      notifications.success(`${props.categoryName}: added ${res.added.length}, extended ${res.updated.length}.`);
    if (res.problems.length) notifications.warn(res.problems.map((p) => `${p.name}: ${p.reason}`).join("\n"));
    visible.value = false;
  } catch (e) {
    notifications.error(errorText(e));
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="`Add to ${categoryName}`" :style="{ width: 'min(28rem, 95vw)' }">
    <div class="flex flex-col gap-3 text-sm">
      <div>
        <div class="font-medium">{{ names.length === 1 ? names[0] : `${names.length} parameters` }}</div>
        <div v-if="names.length > 1" class="text-xs text-surface-500 line-clamp-2">{{ names.join(", ") }}</div>
      </div>

      <div v-if="blocker" class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
        {{ blocker }}
      </div>
      <template v-else>
        <div class="flex gap-6">
          <label class="flex items-center gap-2"><RadioButton v-model="isInstance" :value="true" />Instance</label>
          <label class="flex items-center gap-2"><RadioButton v-model="isInstance" :value="false" />Type</label>
        </div>
        <div v-if="existing.length" class="text-xs text-surface-500">
          {{ existing.length === 1 ? "Already in the project" : `${existing.length} already in the project` }} — they keep
          their categories and gain {{ categoryName }}.
        </div>
        <div v-if="dirty" class="text-xs text-amber-700">The file has unsaved changes — it is saved first.</div>
        <button class="text-xs text-surface-500 text-left" @click="showGroup = !showGroup">
          <i class="pi text-[0.6rem] mr-1" :class="showGroup ? 'pi-chevron-down' : 'pi-chevron-right'" />Group in Properties
        </button>
        <Select
          v-if="showGroup"
          v-model="groupTypeId"
          :options="bindingOptions?.groups ?? []"
          optionLabel="label"
          optionValue="typeId"
          filter
          size="small"
        />
      </template>
    </div>
    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="visible = false" />
      <Button
        :label="dirty ? 'Save file and add' : 'Add'"
        icon="pi pi-check"
        :loading="busy"
        :disabled="!!blocker"
        @click="apply"
      />
    </template>
  </Dialog>
</template>
