<script setup>
import Button from "primevue/button";
import { inject } from "vue";

function toggleDarkMode() {
  document.documentElement.classList.toggle("my-app-dark");
}
function printePage() {
  window.print();
}

const sidebarActions = inject("sidebarActions", { openSidebar: () => {} });
const { openSidebar } = sidebarActions;
</script>

<template>
  <header class="layout-topbar">
    <div class="flex justify-between items-center p-3">
      <div class="flex items-center gap-4">
        <Button icon="pi pi-bars" severity="secondary" text @click="openSidebar" />
        <div>
          <h2 class="text-xl font-bold uppercase m-0">Analyse Tool</h2>
        </div>
      </div>

      <ul class="flex items-center gap-6 m-0 p-0 list-none">
        <li class="flex items-center gap-2">
          <Button icon="pi pi-sun" severity="secondary" text @click="toggleDarkMode" />
        </li>
        <li class="flex items-center gap-2">
          <Button icon="pi pi-print" severity="secondary" text @click="printePage" />
        </li>
      </ul>
    </div>
  </header>
</template>
