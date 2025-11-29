<script setup lang="ts">
import { ref, onMounted, provide } from "vue";
import { useElementsStore } from "@/stores/useElementsStore";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import { UpdateInfo, useUpdateStore } from "@/stores/useUpdateStore";
import type { WebViewMessage } from "@/RevitBridge";
import type { ElementItem } from "@/stores/types";
import { Commands, MessageType } from "@/RevitBridge";

import HeaderLayout from "@/layout/HeaderLayout.vue";
import Sidebar from "@/layout/Sidebar.vue";
import FooterLayout from "./layout/FooterLayout.vue";

const elementsStore = useElementsStore();
const categoriesStore = useCategoriesStore();
const updateStore = useUpdateStore();
const sidebarVisible = ref(false);

const openSidebar = () => {
  sidebarVisible.value = true;
};
const closeSidebar = () => {
  sidebarVisible.value = false;
};
const message: WebViewMessage = {
  Type: MessageType.Request,
  Command: Commands.GetCategories,
  Payload: null,
};

onMounted(() => {
  if ((window as any).chrome?.webview) {
    if (!(window as any).chrome?.webview) {
      console.warn("WebView2 messaging not available");
      return;
    }

    updateStore.loadUpdateData();

    (window as any).chrome.webview.addEventListener("message", (event) => {
      console.log("Data from Revit:", event.data, "type:", typeof event.data);
      try {
        const payload = event.data as WebViewMessage;

        if (payload.Command === Commands.GetCategories) {
          // payload is ElementItem[]
          categoriesStore.setCategories(payload.Payload as string[]);
          return;
        }
        if (payload.Command === Commands.GetDataByCategoryName) {
          // payload is ElementItem[]
          elementsStore.setItems(payload.Payload as ElementItem[]);
          return;
        }
        if (payload.Command === Commands.CheckUpdate) {
          // payload is UpdateInfo
          updateStore.setUpdateInfo(payload.Payload as UpdateInfo);
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
    <div>
      <HeaderLayout />
      <div class="layout-sidebar">
        <Sidebar />
      </div>
      <div class="layout-main-container">
        <router-view />
        <div class="layout-footer"></div>
      </div>
      <FooterLayout />
    </div>
  </div>
</template>

<style>
.layout-wrapper {
  background-color: var(--p-surface-0); /* фон из Aura */
  transition: background-color 0.3s, color 0.3s;
}
</style>
