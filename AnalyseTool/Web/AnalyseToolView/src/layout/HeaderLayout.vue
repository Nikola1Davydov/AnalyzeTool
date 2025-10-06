<script setup>
import { Button } from "primevue";

function toggleDarkMode() {
  document.documentElement.classList.toggle("my-app-dark");
}
</script>

<template>
  <header class="layout-topbar">
    <div class="flex justify-between border-b border-slate-300 px-8 h-10">
      <div class="flex items-center gap-4">
        <i class="pi pi-bars" />
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
