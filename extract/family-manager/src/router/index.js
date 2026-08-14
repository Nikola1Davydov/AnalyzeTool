import { createWebHashHistory, createRouter } from "vue-router";

// Two surfaces, both chrome-less: the full family browser window and the narrow dockable placement
// palette. Views are imported DYNAMICALLY so the palette does not have to parse the browser's code
// (and its heavy deps — three for the 3D preview, marked for the rename dialog) just to open.
const routes = [
  { path: "/", redirect: "/families" },
  { path: "/index.html", redirect: "/families" },
  { path: "/families", component: () => import("@/view/FamiliesView.vue") },
  { path: "/families-dock", component: () => import("@/view/Families/FamilyPaletteView.vue") },
];

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes,
});

export default router;
