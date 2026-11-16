import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { bridge } from "revit-bridge";
import { App } from "./App";
import { RevitQueryProvider } from "./revit/RevitQueryProvider";
import "./styles.css";

// The mock answers are for a normal browser only: inside Revit they stay out of the loaded code
if (!bridge.inRevit) {
  await import("./mocks");
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <RevitQueryProvider>
      <App />
    </RevitQueryProvider>
  </StrictMode>,
);
