<script setup>
import { inject, ref } from "vue";
import { useRouter } from "vue-router";

const visible = inject("sidebarVisible", ref(false));
const { closeSidebar } = inject("sidebarActions");
const router = useRouter();

const menuItems = [
  { label: "Home", icon: "pi pi-home", to: "/" },
  { label: "About", icon: "pi pi-info-circle", to: "/about" },
];

const handleNavigation = (to) => {
  router.push(to);
  closeSidebar();
};
</script>

<template>
  <Drawer v-model:visible="visible" position="left" class="w-72">
    <template #header>
      <h2 class="text-xl font-bold">Menu</h2>
    </template>
    <template #container>
      <nav>
        <ul class="list-none p-0 m-0 overflow-hidden">
          <li v-for="item in menuItems" :key="item.to">
            <Button
              @click="handleNavigation(item.to)"
              class="w-full flex items-center cursor-pointer p-4 rounded text-surface-700 hover:bg-surface-100 dark:text-surface-0 dark:hover:bg-surface-800 duration-150 transition-colors border-0 bg-transparent text-left"
            >
              <i :class="`${item.icon} mr-2`"></i>
              <span class="font-medium">{{ item.label }}</span>
            </Button>
          </li>
        </ul>
      </nav>
    </template>
  </Drawer>
</template>
