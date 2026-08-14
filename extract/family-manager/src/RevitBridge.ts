// Bridge between this extension's UI and the Revit host.
//
// The host injects a minimal `window.AT` into every extension page BEFORE the page's own scripts run
// (see ExtensionBridgeScript on the host side), so `invoke` below just delegates to it. Two extras
// this module adds on top:
//
//   1. A fallback implementation, so the app still works when it is loaded outside the host
//      (`npm run dev` in a plain browser tab won't have window.AT — calls reject with a clear error).
//   2. Re-dispatch of host-initiated broadcasts as DOM events (`at:<Command>`), which the injected
//      bridge does not do. FamilyPaletteView listens for `at:DocumentChanged` and RevitBusyBar for
//      `at:QueueChanged`; without this they would never fire on an extension page.

export type WebViewMessage = {
  Type: string;
  Command: string;
  Payload: any;
  Id?: string;
  Error?: string | null;
};

/** Commands this extension calls. Names are resolved by the host dispatcher at runtime — this map is
 *  only here so typos surface at compile time rather than as a rejected promise.
 *
 *  Three groups, and the difference matters for what has to travel with this extension:
 *   - family commands, shipped BY this extension (its own C# assembly);
 *   - AI naming prompts, also shipped by this extension — nothing else calls them;
 *   - host services this extension merely consumes: selection/isolation, the folder picker, and the
 *     AI provider registry (API keys stay host-side; the frontend only ever sees `hasKey`).
 */
export const Commands = {
  // --- shipped by this extension -------------------------------------------------------------
  PlaceFamilyInstance: "PlaceFamilyInstance",
  PurgeFamilyTypes: "PurgeFamilyTypes",
  PurgeFamilies: "PurgeFamilies",
  GetLibraryFamilies: "GetLibraryFamilies",
  GetLibraryPreview: "GetLibraryPreview",
  LoadLibraryFamilies: "LoadLibraryFamilies",
  OllamaSuggestName: "OllamaSuggestName",
  OllamaSuggestNames: "OllamaSuggestNames",
  OllamaSuggestTemplate: "OllamaSuggestTemplate",

  // --- consumed from the host ----------------------------------------------------------------
  SelectionInRevit: "SelectionInRevit",
  IsolationInRevit: "IsolationInRevit",
  PickFolder: "PickFolder",
  OllamaGetModels: "OllamaGetModels",
  AiGetProviders: "AiGetProviders",
  AiSaveProvider: "AiSaveProvider",
  AiDeleteProvider: "AiDeleteProvider",
  AiGetModels: "AiGetModels",
} as const;

export const enum MessageType {
  Request = "Request",
  Response = "Response",
}

export type ProgressInfo = { fraction: number; message?: string | null };

export type InvokeOptions = {
  /** Called for each intermediate progress update pushed by a progress-aware host command. */
  onProgress?: (p: ProgressInfo) => void;
};

type HostBridge = {
  invoke: <T = any>(command: string, payload?: any, options?: InvokeOptions) => Promise<T>;
};

function hostBridge(): HostBridge | null {
  return (window as any).AT ?? null;
}

// --- Event broadcasts -----------------------------------------------------------------------
// Host-initiated messages carry no request Id (e.g. "DocumentChanged", "QueueChanged"). Re-dispatched
// as DOM events so any view can listen without importing a bus:
//   window.addEventListener("at:DocumentChanged", …)

function ensureEventListener(): void {
  const webview = (window as any).chrome?.webview;
  if (!webview || webview.__atEventsAttached) return;
  webview.__atEventsAttached = true;

  webview.addEventListener("message", (event: any) => {
    const message = event.data as WebViewMessage;
    if (message?.Type !== "Event") return;
    window.dispatchEvent(new CustomEvent(`at:${message.Command}`, { detail: message.Payload }));
  });
}

if (typeof window !== "undefined") ensureEventListener();

/**
 * Invoke a host command and await its result. Works for the commands this extension ships and for
 * anything else the host has registered, e.g. `invoke("GetCommands")`.
 */
export function invoke<T = any>(
  command: string,
  payload: any = null,
  options?: InvokeOptions,
): Promise<T> {
  const host = hostBridge();
  if (!host) {
    return Promise.reject(
      new Error(
        `Revit host bridge unavailable — cannot invoke "${command}". ` +
          `This page is running outside the Revit host (window.AT was never injected).`,
      ),
    );
  }
  ensureEventListener();
  return host.invoke<T>(command, payload, options);
}
