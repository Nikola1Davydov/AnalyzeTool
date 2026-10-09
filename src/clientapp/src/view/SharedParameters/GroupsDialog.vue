<script setup lang="ts">
/** The file's groups: rename, add, and delete the empty ones (a parameter cannot be left without one). */
import { computed, ref } from "vue";
import { useSharedParametersStore } from "@/stores/useSharedParametersStore";

const visible = defineModel<boolean>("visible", { required: true });
const store = useSharedParametersStore();
const newName = ref("");

const counts = computed(() => {
  const m = new Map<number, number>();
  for (const p of store.parameters) m.set(p.groupId, (m.get(p.groupId) ?? 0) + 1);
  return m;
});

function nameClash(id: number, name: string) {
  const n = name.trim().toLowerCase();
  return store.groups.some((g) => g.id !== id && g.name.trim().toLowerCase() === n);
}

function add() {
  const name = newName.value.trim();
  if (!name || nameClash(-1, name)) return;
  store.addGroup(name);
  newName.value = "";
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Groups" :style="{ width: 'min(30rem, 95vw)' }">
    <div class="flex flex-col gap-2 text-sm">
      <div v-for="g in store.groups" :key="g.id" class="flex items-center gap-2">
        <InputText
          :modelValue="g.name"
          class="grow"
          size="small"
          :invalid="!g.name.trim() || nameClash(g.id, g.name)"
          @update:modelValue="(v) => store.renameGroup(g.id, String(v ?? ''))"
        />
        <span class="text-xs text-surface-500 w-16 text-right">{{ counts.get(g.id) ?? 0 }} params</span>
        <Button
          icon="pi pi-trash"
          size="small"
          text
          severity="danger"
          :disabled="(counts.get(g.id) ?? 0) > 0"
          v-tooltip.left="(counts.get(g.id) ?? 0) > 0 ? 'Move or delete its parameters first' : 'Delete group'"
          @click="store.deleteGroup(g.id)"
        />
      </div>
      <div v-if="!store.groups.length" class="text-surface-500">No groups yet.</div>

      <div class="flex items-center gap-2 mt-3 pt-3 border-t border-surface-200">
        <InputText v-model="newName" placeholder="New group" class="grow" size="small" @keydown.enter="add" />
        <Button label="Add" icon="pi pi-plus" size="small" :disabled="!newName.trim() || nameClash(-1, newName)" @click="add" />
      </div>
    </div>
    <template #footer>
      <Button label="Done" @click="visible = false" />
    </template>
  </Dialog>
</template>
