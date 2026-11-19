import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const bridge = fileURLToPath(new URL("../../AnalyseTool.Hosting.WebView/revit-bridge", import.meta.url));

export default defineConfig({
  plugins: [react()],
  // Relative asset paths: Release serves dist over the virtual host of WebViewHost
  base: "./",
  server: {
    // The address of ClientApp:DevServerUrl in appsettings; WebView2 loads it in Development
    port: 5173,
    strictPort: true,
    // revit-bridge is a local package outside this folder
    fs: { allow: [".", bridge] },
  },
  build: { outDir: "dist", emptyOutDir: true },
});
