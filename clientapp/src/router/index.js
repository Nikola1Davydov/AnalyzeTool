import { createWebHistory, createRouter } from "vue-router";

import AboutView from "@/view/AboutView.vue";
import ParameterFilledEmptyPage from "@/layout/ParameterFilledEmptyPage/ParameterFilledEmptyPage.vue";

const routes = [
  { path: "/", component: ParameterFilledEmptyPage },
  { path: "/index.html", redirect: "/" }, // чтобы загрузка index.html попадала на главную
  { path: "/about", component: AboutView },
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;
