import { createWebHashHistory, createRouter } from "vue-router";

// Views are imported DYNAMICALLY: each plugin window is its own WebView that loads this SPA and
// shows exactly one route — a static import would make every window (even the narrow dockable
// palette) parse the code of ALL pages. With dynamic imports Vite emits one chunk per view, so a
// window parses the app core + its own page only. NOTE: PrimeVue components stay globally
// registered in main.js on purpose (the canvas relies on runtime component resolution) — only the
// per-view code and its heavy deps (chart.js, tables, marked) are split out.
const routes = [
  { path: "/", component: () => import("@/view/InfiniteCanvas/ParameterCanvasView.vue") },
  { path: "/index.html", redirect: "/" },
  { path: "/about", component: () => import("@/view/AboutView.vue") },
  { path: "/parameterFilledEmptyPage", component: () => import("@/view/ParameterFilledEmptyView.vue") },
  { path: "/parametervaluecheck", component: () => import("@/view/ParameterValueCheckView.vue") },
  { path: "/parameterCanvasView", component: () => import("@/view/InfiniteCanvas/ParameterCanvasView.vue") },
  {
    // The dockable pane's resting content, before any extension page is docked.
    path: "/dock",
    component: () => import("@/view/DockEmptyView.vue"),
    meta: { layout: "bare" },
  },
  {
    // The plugin's own preferences — one screen, no tabs. Extensions are a window of their own.
    path: "/system/settings",
    component: () => import("@/view/System/SettingsView.vue"),
    meta: { layout: "bare" },
  },
  {
    // The extension manager: installed, catalog, dev folders.
    path: "/system/extensions",
    component: () => import("@/view/System/ExtensionsView.vue"),
    meta: { layout: "bare" },
  },
];

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;
