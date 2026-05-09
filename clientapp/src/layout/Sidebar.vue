<script setup>
import { inject, ref } from "vue";
import { useRouter } from "vue-router";

const visible = inject("sidebarVisible", ref(false));
const { closeSidebar } = inject("sidebarActions");
const router = useRouter();

const menuItems = [
  { label: "Home", icon: "pi pi-th-large", to: "/parameterCanvasView" },
  { label: "Parameter Empty Check", icon: "pi pi-home", to: "/parameterFilledEmptyPage" },
  { label: "Parameter Value Check", icon: "pi pi-check-square", to: "/parametervaluecheck" },
  // { label: "Document Health", icon: "pi pi-heart", to: "/documenthealth" },
  // { label: "Families", icon: "pi-objects-column", to: "/families" },
  // { label: "ConnectParameters", icon: "pi pi-info-circle", to: "/connectParameters" },
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
        <ul class="overflow-hidden pr-2">
          <li v-for="item in menuItems" :key="item.to">
            <Button @click="handleNavigation(item.to)" class="w-full m-1">
              <i :class="`${item.icon}`"></i>
              <span class="font-medium">{{ item.label }}</span>
            </Button>
          </li>
        </ul>
      </nav>
    </template>
  </Drawer>
</template>
