<script setup lang="ts">
// The main window's two pages as tabs — a menu behind a hamburger hid them, and there are only two.
import { useRoute, useRouter } from "vue-router";

const route = useRoute();
const router = useRouter();

const tabs = [
  { label: "Shared parameters", icon: "pi pi-list", to: "/" },
  { label: "Report", icon: "pi pi-file-pdf", to: "/report" },
];
</script>

<template>
  <header class="layout-topbar no-print border-b border-surface-200 bg-surface-0">
    <div class="flex items-center gap-6 px-4 h-12">
      <h2 class="text-base font-bold uppercase m-0 tracking-wide">Analyse Tool</h2>
      <nav class="flex items-stretch h-full">
        <button
          v-for="tab in tabs"
          :key="tab.to"
          class="flex items-center gap-2 px-3 text-sm border-b-2 transition-colors"
          :class="
            route.path === tab.to
              ? 'border-primary-500 text-primary-700 font-semibold'
              : 'border-transparent text-surface-600 hover:text-surface-900'
          "
          @click="router.push(tab.to)"
        >
          <i :class="tab.icon" />{{ tab.label }}
        </button>
      </nav>
      <div class="grow" />
      <Button icon="pi pi-info-circle" severity="secondary" text size="small" v-tooltip.left="'About'" @click="router.push('/about')" />
    </div>
  </header>
</template>
