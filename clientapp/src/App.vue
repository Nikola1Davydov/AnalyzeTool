<script setup lang="ts">
import { ref, onMounted, provide, watch } from "vue";
import { useToast } from "primevue/usetoast";
import { useElementsStore } from "@/stores/useElementsStore";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import { UpdateInfo, useUpdateStore } from "@/stores/useUpdateStore";
import type { WebViewMessage } from "@/RevitBridge";
import type { ElementItem } from "@/stores/types";
import { useDocumentDataStore, type DocumentData } from "@/stores/useDocumentDataStore";
import type { DocumentHealthPayload } from "./stores/useDocumentHealthStore";
import { Commands, MessageType } from "@/RevitBridge";
import { useNotificationStore } from "@/stores/useNotificationStore";
import { useAiSettingsStore } from "@/stores/useAiSettingsStore";

import HeaderLayout from "@/layout/HeaderLayout.vue";
import Sidebar from "@/layout/Sidebar.vue";
import FooterLayout from "./layout/FooterLayout.vue";
import { useDocumentHealthStore } from "./stores/useDocumentHealthStore";

const toast = useToast();
const notificationStore = useNotificationStore();
const aiSettingsStore = useAiSettingsStore();
const elementsStore = useElementsStore();
const categoriesStore = useCategoriesStore();
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

onMounted(() => {
  if ((window as any).chrome?.webview) {
    if (!(window as any).chrome?.webview) {
      console.warn("WebView2 messaging not available");
      return;
    }

    updateStore.loadUpdateData();
    useDocumentDataStore().loadDocumentData();

    (window as any).chrome.webview.addEventListener("message", (event) => {
      console.log("Data from Revit:", event.data, "type:", typeof event.data);
      try {
        const payload = event.data as WebViewMessage;

        if (payload.Command === Commands.GetCategoriesInRevit) {
          categoriesStore.setCategories(payload.Payload as string[]);
          return;
        }
        if (payload.Command === Commands.GetDataByCategoryName) {
          elementsStore.setItems(payload.Payload as ElementItem[]);
          return;
        }
        if (payload.Command === Commands.CheckUpdate) {
          updateStore.setUpdateInfo(payload.Payload as UpdateInfo);
          return;
        }
        if (payload.Command == Commands.GetDocumentHealthStatus) {
          useDocumentHealthStore().setHealth(payload.Payload as DocumentHealthPayload);
          return;
        }
        if (payload.Command == Commands.GetDocumentData) {
          useDocumentDataStore().setDocumentData(payload.Payload as DocumentData);
          return;
        }
        if (payload.Command === Commands.AiEditParameters) {
          window.dispatchEvent(new CustomEvent("revit:ai-edit", { detail: payload.Payload }));
          return;
        }
        if (payload.Command === Commands.AiAnalyse) {
          window.dispatchEvent(new CustomEvent("revit:ai-raw", { detail: payload.Payload }));
          return;
        }
        if (payload.Command === Commands.GetOllamaModels) {
          const models = Array.isArray(payload.Payload)
            ? (payload.Payload as unknown[]).map((m) => String(m))
            : [];
          aiSettingsStore.setAvailableModels(models);
          return;
        }

        // Fallback: любой ответ с { error } который не был обработан выше
        if (payload.Payload?.error) {
          notificationStore.error(String(payload.Payload.error));
          return;
        }
      } catch (err) {
        console.error("Parse error", err);
      }
    });
  }
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
