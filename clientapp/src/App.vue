<script setup lang="ts">
import { ref, onMounted, provide } from "vue";
import { useElementsStore } from "@/stores/useElementsStore";
import { useCategoriesStore } from "@/stores/useCategoriesStore";
import type { WebViewMessage } from "@/RevitBridge";
import type { ElementItem } from "./types";

import HeaderLayout from "@/layout/HeaderLayout.vue";
import Sidebar from "@/layout/Sidebar.vue";

const elementsStore = useElementsStore();
const categoriesStore = useCategoriesStore();
const sidebarVisible = ref(false);

const openSidebar = () => {
  sidebarVisible.value = true;
};
const closeSidebar = () => {
  sidebarVisible.value = false;
};
const message: WebViewMessage = {
  CommandsEnum: "GetCategories",
  JsonData: null,
};
function sendRequest(command: string, payload: any): Promise<any> {
  return new Promise((reject) => {
    if (!(window as any).chrome?.webview) {
      reject(new Error("WebView2 messaging not available"));
      return;
    }

    const message: WebViewMessage = {
      CommandsEnum: command,
      JsonData: payload,
    };

    (window as any).chrome.webview.postMessage(message);
  });
}
onMounted(() => {
  if ((window as any).chrome?.webview) {
    if (!(window as any).chrome?.webview) {
      console.warn("WebView2 messaging not available");
      return;
    }

    sendRequest("GetCategories", null);

    (window as any).chrome.webview.addEventListener("message", (event) => {
      console.log("Из Revit пришло:", event.data, "тип:", typeof event.data);
      try {
        const payload = event.data as WebViewMessage;

        if (payload.CommandsEnum === 4) {
          // payload is ElementItem[]
          categoriesStore.setCategories(payload.JsonData as string[]);
          return;
        }
        if (payload.CommandsEnum === 0) {
          // payload is ElementItem[]
          elementsStore.setItems(payload.JsonData as ElementItem[]);
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
    </div>
  </div>
</template>

<style>
.layout-wrapper {
  background-color: var(--p-surface-0); /* фон из Aura */
  transition: background-color 0.3s, color 0.3s;
}
</style>
