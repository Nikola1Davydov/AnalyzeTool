export type WebViewMessage = {
  Type: string;
  Command: string;
  Payload: any;
  Id?: string;
  Error?: string | null;
};

export const Commands = {
  SelectionInRevit: "SelectionInRevit",
  IsolationInRevit: "IsolationInRevit",
  GetCategoriesInRevit: "GetCategoriesInRevit",
  GetDataByCategoryName: "GetDataByCategoryName",
  CheckUpdate: "CheckUpdate",
  GetDocumentData: "GetDocumentData",
  SetDataToParameters: "SetDataToParameters",
  OllamaAnalyse: "OllamaAnalyse",
  OllamaEditParameters: "OllamaEditParameters",
  OllamaSuggestName: "OllamaSuggestName",
  OllamaGetModels: "OllamaGetModels",
  PlaceFamilyInstance: "PlaceFamilyInstance",
  PurgeFamilyTypes: "PurgeFamilyTypes",
  PurgeFamilies: "PurgeFamilies",
  GetLibraryFamilies: "GetLibraryFamilies",
  GetLibraryPreview: "GetLibraryPreview",
  LoadLibraryFamilies: "LoadLibraryFamilies",
  PickFolder: "PickFolder",
} as const;

export const enum MessageType {
  Request = "Request",
  Response = "Response",
}

// --- AT.invoke: correlated request/response over the same WebView channel -------------------
// Each call gets a unique Id; the host echoes it back, and we resolve the matching promise.
// This is the generic entry point any command (built-in or extension) can be called through.

export type ProgressInfo = { fraction: number; message?: string | null };

type PendingCall = {
  resolve: (value: any) => void;
  reject: (reason: any) => void;
  onProgress?: (p: ProgressInfo) => void;
};

export type InvokeOptions = {
  /** Called for each intermediate progress update pushed by a progress-aware host command. */
  onProgress?: (p: ProgressInfo) => void;
};

const pendingCalls = new Map<string, PendingCall>();
let invokeSeq = 0;

function ensureInvokeListener(): void {
  const webview = (window as any).chrome?.webview;
  if (!webview || webview.__atInvokeAttached) return;
  webview.__atInvokeAttached = true;

  webview.addEventListener("message", (event: any) => {
    const message = event.data as WebViewMessage;
    if (!message) return;

    // Host-initiated broadcasts (no request Id) — e.g. "DocumentChanged". Re-dispatched as DOM events
    // so any view can listen without importing a bus: window.addEventListener("at:DocumentChanged", …).
    if (message.Type === "Event") {
      window.dispatchEvent(new CustomEvent(`at:${message.Command}`, { detail: message.Payload }));
      return;
    }

    if (!message.Id) return; // only correlated responses are ours

    const pending = pendingCalls.get(message.Id);
    if (!pending) return;

    // Intermediate progress: notify but keep the call pending until the final response.
    if (message.Type === "Progress") {
      pending.onProgress?.(message.Payload as ProgressInfo);
      return;
    }

    pendingCalls.delete(message.Id);
    if (message.Error) pending.reject(new Error(message.Error));
    else pending.resolve(message.Payload);
  });
}

/**
 * Invoke any registered command and await its result.
 * Works for built-in commands and for commands added by C# extensions, e.g.
 *   const res = await invoke("acme.sample.Hello");
 */
export function invoke<T = any>(
  command: string,
  payload: any = null,
  options?: InvokeOptions,
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const webview = (window as any).chrome?.webview;
    if (!webview) {
      reject(new Error("WebView2 messaging not available"));
      return;
    }

    ensureInvokeListener();

    const id = `at-${Date.now()}-${++invokeSeq}`;
    pendingCalls.set(id, { resolve, reject, onProgress: options?.onProgress });

    const message: WebViewMessage = {
      Type: MessageType.Request,
      Command: command,
      Payload: payload,
      Id: id,
    };

    webview.postMessage(message);
  });
}

// Expose for extension UIs (and the console) running inside the WebView.
if (typeof window !== "undefined") {
  ensureInvokeListener();
  (window as any).AT = { invoke };
}
