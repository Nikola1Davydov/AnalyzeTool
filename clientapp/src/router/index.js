import { createWebHistory, createRouter } from "vue-router";

import AboutView from "@/view/AboutView.vue";
import ParameterFilledEmptyPage from "@/view/ParameterFilledEmptyView.vue";
import RevitDocumentHealthView from "@/view/RevitDocumentHealthView.vue";
import ParameterValueCheckView from "@/view/ParameterValueCheckView.vue";

const routes = [
  { path: "/", component: ParameterFilledEmptyPage },
  { path: "/index.html", redirect: "/" }, // чтобы загрузка index.html попадала на главную
  { path: "/about", component: AboutView },
  { path: "/documenthealth", component: RevitDocumentHealthView },
  { path: "/parametervaluecheck", component: ParameterValueCheckView },
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;
