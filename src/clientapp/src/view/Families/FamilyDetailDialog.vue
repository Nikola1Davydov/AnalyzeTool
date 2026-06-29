<script setup lang="ts">
import FamilyViewer3D from "@/view/Families/FamilyViewer3D.vue";
import FamilyTypePanel from "@/view/Families/FamilyTypePanel.vue";

interface FamilyRow {
  id: number;
  uniqueId: string;
  versionGuid: string;
  name: string;
  category: string;
  typeCount: number;
  instanceCount: number;
  isInPlace: boolean;
}

const visible = defineModel<boolean>("visible", { required: true });
defineProps<{ family: FamilyRow | null }>();
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    maximizable
    dismissableMask
    :header="family?.name ?? 'Family'"
    :style="{ width: '80vw', maxWidth: '1200px', height: '80vh' }"
    :contentStyle="{ height: '100%', padding: '0' }"
  >
    <!-- :key forces a fresh viewer/panel per family so 3D + type state never leak between cards. -->
    <div v-if="family" class="grid grid-cols-1 md:grid-cols-5 gap-4 h-full p-4">
      <div class="md:col-span-3 h-full min-h-[300px]">
        <FamilyViewer3D
          :key="family.id"
          :familyId="family.id"
          :uniqueId="family.uniqueId"
          :versionGuid="family.versionGuid"
        />
      </div>
      <div class="md:col-span-2 h-full min-h-0 md:border-l border-surface-200 md:pl-4">
        <FamilyTypePanel :key="family.id" :familyId="family.id" />
      </div>
    </div>
  </Dialog>
</template>
