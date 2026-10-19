<script setup lang="ts">
import { onMounted, watch, computed } from "vue";
import { useRoute } from "vue-router";
import { useToast } from "primevue/usetoast";
import { useUpdateStore } from "@/stores/useUpdateStore";
import { useDocumentDataStore } from "@/stores/useDocumentDataStore";
import { useNotificationStore } from "@/stores/useNotificationStore";

import HeaderLayout from "@/layout/HeaderLayout.vue";
import FooterLayout from "./layout/FooterLayout.vue";
import RevitBusyBar from "@/components/RevitBusyBar.vue";

const toast = useToast();
const notificationStore = useNotificationStore();
const updateStore = useUpdateStore();

// System pages (/system/*) render without the app chrome (header/footer).
const route = useRoute();
const isBare = computed(() => route.meta.layout === "bare");

watch(
  () => notificationStore.pending,
  (n) => {
    if (!n) return;
    toast.add({ severity: n.severity, summary: n.summary, detail: n.detail, life: 5000 });
    notificationStore.pending = null;
  },
);

// Each store now requests its own data via AT.invoke and resolves the result directly,
// so there is no central message listener routing responses by command name anymore.
onMounted(() => {
  updateStore.loadUpdateData();
  useDocumentDataStore().loadDocumentData();
});
</script>

<template>
  <Toast position="top-right" />

  <!-- Global busy strip: shows in EVERY window (bare and chrome layouts) while the platform runs a
       long command or Revit can't execute queued work (user in a modal dialog / edit mode). -->
  <RevitBusyBar />

  <!-- Bare layout for system pages -->
  <router-view v-if="isBare" />

  <!-- Default layout with the app chrome -->
  <div v-else class="layout-wrapper">
    <div>
      <HeaderLayout />
      <div class="layout-main-container">
        <router-view v-slot="{ Component }">
          <KeepAlive>
            <component :is="Component" />
          </KeepAlive>
        </router-view>
        <div class="layout-footer"></div>
      </div>
      <FooterLayout />
    </div>
  </div>
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
