import { createMemoryHistory, createRouter } from "vue-router";

import MainLayout from "@/layout/MainLayout.vue";
import AboutView from "@/view/AboutView.vue";

const routes = [
  { path: "/", component: MainLayout },
  { path: "/about", component: AboutView },
];

const router = createRouter({
  history: createMemoryHistory(),
  routes,
});

export default router;
