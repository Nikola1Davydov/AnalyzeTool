import { defineConfig } from "vite";
import tailwindcss from "@tailwindcss/vite";
import plugin from "@vitejs/plugin-vue";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [plugin(), tailwindcss()],
  resolve: {
    alias: {
      "@": resolve(fileURLToPath(new URL(".", import.meta.url)), "./src"),
    },
  },
  server: {
    port: 22524,
    middlewareMode: false,
  },
  build: {
    rollupOptions: {
      output: {
        // Framework code changes only when npm packages are updated — its own chunk keeps a stable
        // hash, so the WebView2 cache reuses it across plugin releases. Per-view chunks come from
        // the dynamic imports in router/index.js.
        manualChunks: {
          vendor: ["vue", "vue-router", "pinia"],
        },
      },
    },
  },
});
