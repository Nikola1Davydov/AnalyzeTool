import { createWebHashHistory, createRouter } from "vue-router";

import AboutView from "@/view/AboutView.vue";
import ParameterFilledEmptyPage from "@/view/ParameterFilledEmptyView.vue";
import RevitDocumentHealthView from "@/view/RevitDocumentHealthView.vue";
import ParameterValueCheckView from "@/view/ParameterValueCheckView.vue";
import FamiliesView from "@/view/FamiliesView.vue";
import ConnectParameters from "@/view/ConnectParameters/ConnectParametersView.vue";
import ParameterCanvasView from "@/view/InfiniteCanvas/ParameterCanvasView.vue";
import ExtensionsSettingsView from "@/view/System/ExtensionsSettingsView.vue";

const routes = [
  { path: "/", component: ParameterCanvasView },
  { path: "/index.html", redirect: "/" },
  { path: "/about", component: AboutView },
  { path: "/parameterFilledEmptyPage", component: ParameterFilledEmptyPage },
  { path: "/documenthealth", component: RevitDocumentHealthView },
  { path: "/parametervaluecheck", component: ParameterValueCheckView },
  { path: "/families", component: FamiliesView },
  { path: "/connectParameters", component: ConnectParameters },
  { path: "/parameterCanvasView", component: ParameterCanvasView },
  { path: "/system/settings", component: ExtensionsSettingsView, meta: { layout: "bare" } },
];

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;
