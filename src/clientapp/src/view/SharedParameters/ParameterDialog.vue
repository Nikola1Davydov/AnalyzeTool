<script setup lang="ts">
/**
 * Create or edit one parameter of the file. The GUID is the parameter's identity: shown, never edited —
 * a renamed parameter stays the same parameter in every project that uses it.
 */
import { computed, ref, watch } from "vue";
import Textarea from "primevue/textarea";
import ToggleSwitch from "primevue/toggleswitch";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";
import { DATA_TYPES, type SharedParameter } from "./types";

const props = defineProps<{ parameter: SharedParameter | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const store = useSharedParametersStore();

const draft = ref<SharedParameter>(blank());
const newGroupName = ref("");
const isNew = computed(() => !props.parameter);

function blank(): SharedParameter {
  return {
    guid: crypto.randomUUID(),
    name: "",
    dataType: "TEXT",
    dataCategory: "",
    groupId: store.groups[0]?.id ?? 0,
    visible: true,
    description: "",
    userModifiable: true,
    hideWhenNoValue: false,
  };
}

watch(visible, (open) => {
  if (!open) return;
  draft.value = props.parameter ? { ...props.parameter } : blank();
  newGroupName.value = "";
});

// A type the list does not know (a newer Revit's spec, FAMILYTYPE) stays selectable as itself.
const typeOptions = computed(() =>
  DATA_TYPES.some((t) => t.value === draft.value.dataType)
    ? DATA_TYPES
    : [...DATA_TYPES, { value: draft.value.dataType, label: draft.value.dataType }],
);

const nameTaken = computed(() => {
  const name = draft.value.name.trim().toLowerCase();
  return (
    !!name &&
    store.parameters.some((p) => p.guid !== draft.value.guid && p.name.trim().toLowerCase() === name)
  );
});

const groupMissing = computed(() => !store.groups.some((g) => g.id === draft.value.groupId) && !newGroupName.value.trim());

const canSave = computed(() => !!draft.value.name.trim() && !nameTaken.value && !groupMissing.value);

function apply() {
  if (!canSave.value) return;
  if (newGroupName.value.trim()) draft.value.groupId = store.addGroup(newGroupName.value).id;
  store.upsertParameter({ ...draft.value, name: draft.value.name.trim() });
  visible.value = false;
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="isNew ? 'New shared parameter' : 'Edit shared parameter'"
    :style="{ width: 'min(34rem, 95vw)' }"
  >
    <div class="flex flex-col gap-4 text-sm">
      <label class="flex flex-col gap-1">
        <span class="font-medium">Name</span>
        <InputText v-model="draft.name" autofocus :invalid="nameTaken" @keydown.enter="apply" />
        <small v-if="nameTaken" class="text-red-600">Another parameter in this file has this name.</small>
      </label>

      <div class="grid grid-cols-2 gap-3">
        <label class="flex flex-col gap-1">
          <span class="font-medium">Data type</span>
          <Select
            v-model="draft.dataType"
            :options="typeOptions"
            optionLabel="label"
            optionValue="value"
            :disabled="!isNew"
            v-tooltip.top="isNew ? undefined : 'Revit cannot change the type of an existing parameter'"
          />
        </label>
        <label class="flex flex-col gap-1">
          <span class="font-medium">Group</span>
          <Select
            v-model="draft.groupId"
            :options="store.groups"
            optionLabel="name"
            optionValue="id"
            placeholder="Pick a group"
            :disabled="!!newGroupName.trim()"
          />
        </label>
      </div>
      <label class="flex flex-col gap-1">
        <span class="text-surface-500">…or a new group</span>
        <InputText v-model="newGroupName" placeholder="New group name" />
      </label>

      <label class="flex flex-col gap-1">
        <span class="font-medium">Description <span class="text-surface-500 font-normal">(tooltip)</span></span>
        <Textarea v-model="draft.description" rows="2" autoResize />
      </label>

      <div class="flex flex-wrap gap-x-6 gap-y-2">
        <label class="flex items-center gap-2"><ToggleSwitch v-model="draft.visible" />Visible</label>
        <label class="flex items-center gap-2"><ToggleSwitch v-model="draft.userModifiable" />User modifiable</label>
        <label class="flex items-center gap-2"><ToggleSwitch v-model="draft.hideWhenNoValue" />Hide when empty</label>
      </div>

      <div class="text-xs text-surface-500 font-mono break-all">GUID {{ draft.guid }}</div>
    </div>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="visible = false" />
      <Button :label="isNew ? 'Add' : 'Apply'" icon="pi pi-check" :disabled="!canSave" @click="apply" />
    </template>
  </Dialog>
</template>
