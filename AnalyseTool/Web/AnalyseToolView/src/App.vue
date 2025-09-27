<script setup>
import TopPanel from "./components/TopPanel.vue";
import BodyTable from "./components/BodyTable.vue";
import { onMounted } from "vue";
import { useElements } from "@/stores/useElements";

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
  <div class="app-container flex flex-col h-full w-full p-5 gap-5 bg-black">
    <TopPanel />
    <BodyTable />
  </div>
</template>

<style scoped></style>
