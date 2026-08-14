<script setup lang="ts">
import { watch } from "vue";
import { useToast } from "primevue/usetoast";
import { useNotificationStore } from "@/stores/useNotificationStore";
import RevitBusyBar from "@/components/RevitBusyBar.vue";

// Both of this extension's screens (the family browser and the dockable palette) are chrome-less:
// the host window IS the frame. So there is no header, sidebar or footer here — unlike the main
// AnalyseTool app, which needs them for navigation between its own pages.

const toast = useToast();
const notificationStore = useNotificationStore();

watch(
  () => notificationStore.pending,
  (n) => {
    if (!n) return;
    toast.add({ severity: n.severity, summary: n.summary, detail: n.detail, life: 5000 });
    notificationStore.pending = null;
  },
);
</script>

<template>
  <Toast position="top-right" />

  <!-- Busy strip: shows while the host runs a long command or Revit can't execute queued work
       (user in a modal dialog / edit mode). -->
  <RevitBusyBar />

  <router-view />
</template>

<!-- Global (unscoped): the Toast renders in a body-level portal, so scoped styles can't reach it.
     Cap its width to the viewport so it fits the narrow dockable pane; wide windows keep 25rem. -->
<style>
.p-toast {
  width: min(25rem, calc(100vw - 1.5rem));
  max-width: calc(100vw - 1.5rem);
  right: 0.75rem !important;
}
.p-toast .p-toast-message-text {
  min-width: 0;
}
.p-toast .p-toast-detail {
  word-break: break-word;
}
</style>
