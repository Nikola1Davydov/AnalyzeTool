<script setup>
import TopPanel from "./components/TopPanel.vue";
import BodyTable from "./components/BodyTable.vue";
import CategoriesChart from "./components/CategoriesChart.vue";
import { onMounted } from "vue";
import { useElements } from "@/stores/useElements";
import { header } from "@primeuix/themes/aura/accordion";

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
    <header class="layout-topbar">
      <div
        class="flex justify-between border-b border-slate-300 bg-black px-8 h-10"
      >
        <div class="flex items-center gap-4">
          <i class="pi pi-bars" />
          <div class="">
            <h2 class="text-xl font-bold uppercase">Analyse Tool</h2>
          </div>
        </div>

        <ul class="flex items-center gap-5">
          <li class="flex items-center gap-3">
            <i class="pi pi-sun" />
          </li>
          <li class="flex items-center gap-3">
            <i class="pi pi-print" />
          </li>
          <li class="flex items-center gap-3">
            <i class="pi pi-sync" />
            <b>Update Data</b>
          </li>
        </ul>
      </div>
    </header>
    <div class="layout-sidebar"></div>
    <div class="layout-main-container">
      <div class="layout-main">
        <div class="h-full w-full p-5 gap-5 bg-black">
          <TopPanel />
          <CategoriesChart />
          <BodyTable />
        </div>
      </div>
      <div class="layout-footer"></div>
    </div>
  </div>
</template>

<style scoped></style>
