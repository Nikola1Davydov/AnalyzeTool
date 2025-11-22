<script setup>
import { ref, onMounted, provide } from "vue";
import { useElements } from "@/stores/useElements";

import HeaderLayout from "@/layout/HeaderLayout.vue";
import Sidebar from "@/layout/Sidebar.vue";

const store = useElements();
const sidebarVisible = ref(false);

const openSidebar = () => {
  sidebarVisible.value = true;
};
const closeSidebar = () => {
  sidebarVisible.value = false;
};

onMounted(() => {
  if (window.chrome?.webview) {
    if (!window.chrome?.webview) {
      console.warn("WebView2 messaging not available");
      return;
    }
    window.chrome.webview.postMessage("readyMessage");

    console.log(window.chrome.webview);
    window.chrome.webview.addEventListener("message", (event) => {
      console.log("Из Revit пришло:", event.data);
      try {
        const data = JSON.parse(event.data);
        if (Array.isArray(data)) {
          store.setItems(data);
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
