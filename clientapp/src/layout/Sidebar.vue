<script setup>
import { Drawer } from "primevue";
import { inject, ref } from "vue";

const visible = inject("sidebarVisible", ref(false)); // ← fallback
</script>

<template>
  <Drawer v-model:visible="visible" position="left" class="w-72">
    <template #container="{ closeCallback }">
      <div class="flex flex-col h-full bg-surface-0 dark:bg-surface-900">
        <div
          class="flex justify-between items-center px-6 pt-4 border-b border-surface-200 dark:border-surface-700"
        >
          <span class="font-semibold text-xl text-primary">My Sidebar</span>
          <Button icon="pi pi-times" @click="closeCallback" rounded text />
        </div>

        <div class="p-4 space-y-3">
          <Button label="Dashboard" icon="pi pi-home" outlined class="w-full" />
          <Button
            label="Reports"
            icon="pi pi-chart-line"
            outlined
            class="w-full"
          />
          <Button label="Settings" icon="pi pi-cog" outlined class="w-full" />
        </div>
      </div>
    </template>
  </Drawer>
</template>
