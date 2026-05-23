<script setup lang="ts">
import { ref, onMounted, provide, watch } from "vue";
import { useToast } from "primevue/usetoast";
import { useUpdateStore } from "@/stores/useUpdateStore";
import { useDocumentDataStore } from "@/stores/useDocumentDataStore";
import { useNotificationStore } from "@/stores/useNotificationStore";

import HeaderLayout from "@/layout/HeaderLayout.vue";
import Sidebar from "@/layout/Sidebar.vue";
import FooterLayout from "./layout/FooterLayout.vue";

const toast = useToast();
const notificationStore = useNotificationStore();
const updateStore = useUpdateStore();
const sidebarVisible = ref(false);

watch(
  () => notificationStore.pending,
  (n) => {
    if (!n) return;
    toast.add({ severity: n.severity, summary: n.summary, detail: n.detail, life: 5000 });
    notificationStore.pending = null;
  },
);

const openSidebar = () => {
  sidebarVisible.value = true;
};
const closeSidebar = () => {
  sidebarVisible.value = false;
};

// Each store now requests its own data via AT.invoke and resolves the result directly,
// so there is no central message listener routing responses by command name anymore.
onMounted(() => {
  updateStore.loadUpdateData();
  useDocumentDataStore().loadDocumentData();
});

provide("sidebarVisible", sidebarVisible);
provide("sidebarActions", {
  closeSidebar,
  openSidebar,
});
</script>

<template>
  <div class="layout-wrapper">
    <Toast position="bottom-right" />
    <div>
      <HeaderLayout />
      <div class="layout-sidebar">
        <Sidebar />
      </div>
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
