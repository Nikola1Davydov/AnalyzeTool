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
});
