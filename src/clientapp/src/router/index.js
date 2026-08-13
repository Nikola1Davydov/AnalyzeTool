import { createWebHashHistory, createRouter } from "vue-router";

// This is the FRAMEWORK shell: it ships the Extension Manager and the About page, nothing else.
// Feature screens live in extensions, each serving its own SPA from its own folder — they never add
// routes here.
//
// Views are imported DYNAMICALLY: each host window is its own WebView that loads this SPA and shows
// exactly one route, so a static import would make every window parse the code of ALL pages. With
// dynamic imports Vite emits one chunk per view. NOTE: PrimeVue components stay globally registered
// in main.js on purpose — only the per-view code and its heavy deps (marked) are split out.
const routes = [
  { path: "/", redirect: "/system/settings" },
  { path: "/index.html", redirect: "/system/settings" },
  { path: "/about", component: () => import("@/view/AboutView.vue") },
  {
    path: "/system/settings",
    component: () => import("@/view/System/ExtensionsSettingsView.vue"),
    meta: { layout: "bare" },
  },
];

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;
