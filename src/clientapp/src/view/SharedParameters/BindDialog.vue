<script setup lang="ts">
/**
 * Add the selected file parameters to the project: which categories, instance or type, which group in
 * the Properties palette. A parameter already in the project keeps its categories and gains these.
 */
import { computed, ref, watch } from "vue";
import Listbox from "primevue/listbox";
import RadioButton from "primevue/radiobutton";
import { invoke } from "@/RevitBridge";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { useNotificationStore } from "@/stores/useNotificationStore";
import { errorText, type BindingOptions, type ParameterRow } from "./types";

const props = defineProps<{ rows: ParameterRow[] }>();
const visible = defineModel<boolean>("visible", { required: true });

const store = useSharedParametersStore();
const notifications = useNotificationStore();

const options = ref<BindingOptions | null>(null);
const categoryIds = ref<number[]>([]);
const isInstance = ref(true);
const groupTypeId = ref("");
const showAnnotation = ref(false);
const busy = ref(false);

watch(visible, async (open) => {
  if (!open) return;
  // Start from what the parameters already have, when they all agree: adding a category to a bound
  // parameter is the common case, and an empty list would read as "remove them all".
  const bound = props.rows.map((r) => r.project).filter((p) => p?.bound);
  if (bound.length) isInstance.value = bound[0]!.isInstance;
  if (!options.value) {
    try {
      options.value = await invoke<BindingOptions>("GetBindingOptions");
      groupTypeId.value = options.value.defaultGroup;
    } catch (e) {
      notifications.error(errorText(e));
    }
  }
  const already = new Set(bound.flatMap((p) => p!.categories));
  categoryIds.value = (options.value?.categories ?? []).filter((c) => already.has(c.name)).map((c) => c.id);
});

const categories = computed(() =>
  (options.value?.categories ?? []).filter((c) => showAnnotation.value || c.type === "Model"),
);

async function apply() {
  busy.value = true;
  try {
    const res = await store.bind(
      props.rows.map((r) => r.guid!).filter(Boolean),
      categoryIds.value,
      isInstance.value,
      groupTypeId.value,
    );
    const done = res.added.length + res.updated.length;
    if (done) notifications.success(`Added ${res.added.length}, updated ${res.updated.length}.`);
    if (res.problems.length)
      notifications.warn(res.problems.map((p) => `${p.name}: ${p.reason}`).join("\n"));
    if (done) visible.value = false;
  } catch (e) {
    notifications.error(errorText(e));
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    header="Add to project"
    :style="{ width: 'min(36rem, 95vw)' }"
  >
    <div class="flex flex-col gap-4 text-sm">
      <div class="text-surface-600">
        {{ rows.length === 1 ? rows[0].name : `${rows.length} parameters` }}
      </div>

      <div class="flex flex-col gap-1">
        <div class="flex items-center justify-between">
          <span class="font-medium">Categories</span>
          <label class="flex items-center gap-2 text-xs text-surface-500">
            <Checkbox v-model="showAnnotation" binary />Show annotation categories
          </label>
        </div>
        <Listbox
          v-model="categoryIds"
          :options="categories"
          optionLabel="name"
          optionValue="id"
          multiple
          filter
          checkmark
          listStyle="max-height: 16rem"
          :loading="!options"
        />
        <small class="text-surface-500">{{ categoryIds.length }} selected</small>
      </div>

      <div class="grid grid-cols-2 gap-3">
        <div class="flex flex-col gap-2">
          <span class="font-medium">Binding</span>
          <label class="flex items-center gap-2"><RadioButton v-model="isInstance" :value="true" />Instance</label>
          <label class="flex items-center gap-2"><RadioButton v-model="isInstance" :value="false" />Type</label>
        </div>
        <label class="flex flex-col gap-1">
          <span class="font-medium">Group in Properties</span>
          <Select
            v-model="groupTypeId"
            :options="options?.groups ?? []"
            optionLabel="label"
            optionValue="typeId"
            filter
          />
        </label>
      </div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="visible = false" />
      <Button
        label="Add to project"
        icon="pi pi-plus"
        :loading="busy"
        :disabled="!categoryIds.length"
        @click="apply"
      />
    </template>
  </Dialog>
</template>
