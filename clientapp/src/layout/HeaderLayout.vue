<script setup>
import { Button } from "primevue";
import { ref, inject } from "vue";

function toggleDarkMode() {
  document.documentElement.classList.toggle("my-app-dark");
}
const { openSidebar } = inject("sidebarActions");
</script>

<template>
  <header class="layout-topbar">
    <div class="flex justify-between border-b border-slate-300 px-8 h-10">
      <div class="flex items-center gap-4">
        <Button icon="pi pi-bars" @click="openSidebar" />
        <div>
          <h2 class="text-xl font-bold uppercase">Analyse Tool</h2>
        </div>
      </div>

      <ul class="flex items-center gap-5">
        <li class="flex items-center gap-3">
          <Button icon="pi pi-sun" @click="toggleDarkMode" />
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
</template>
