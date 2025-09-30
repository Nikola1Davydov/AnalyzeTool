<script setup>
import { onMounted } from "vue";
import { useElements } from "@/stores/useElements";
import { header } from "@primeuix/themes/aura/accordion";

import HeaderLayout from "./layout/HeaderLayout.vue";
import MainLayout from "./layout/MainLayout.vue";
const store = useElements();

onMounted(() => {
  if (window.chrome?.webview) {
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
</script>

<template>
  <div class="layout-wrapper layout-static">
    <HeaderLayout />
    <div class="layout-sidebar"></div>
    <div class="layout-main-container">
      <MainLayout />
      <div class="layout-footer"></div>
    </div>
  </div>
</template>

<style scoped></style>
