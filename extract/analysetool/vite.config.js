import { defineConfig } from "vite";
import tailwindcss from "@tailwindcss/vite";
import plugin from "@vitejs/plugin-vue";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";

// https://vitejs.dev/config/
export default defineConfig({
  // Relative asset URLs: the host serves this folder over a private virtual host
  // (https://ext-<id>/…), and the entry HTML may sit in a sub-path. Root-relative "/assets/…"
  // would break in that case; "./" always resolves against the page.
  base: "./",
  plugins: [plugin(), tailwindcss()],
  resolve: {
    alias: {
      "@": resolve(fileURLToPath(new URL(".", import.meta.url)), "./src"),
    },
  },
  server: {
    // Must match "ui.devUrl" in plugin.json so the host can load the dev server with HMR.
    port: 22525,
    middlewareMode: false,
  },
  build: {
    rollupOptions: {
      output: {
        // Framework code changes only when npm packages are updated — its own chunk keeps a stable
        // hash, so the WebView2 cache reuses it across releases. Per-view chunks come from the
        // dynamic imports in router/index.js.
        manualChunks: {
          vendor: ["vue", "vue-router", "pinia"],
        },
      },
    },
  },
});
