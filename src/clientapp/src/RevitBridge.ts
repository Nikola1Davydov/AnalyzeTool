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
  OllamaSuggestNames: "OllamaSuggestNames",
  OllamaSuggestTemplate: "OllamaSuggestTemplate",
  OllamaGetModels: "OllamaGetModels",
  AiGetProviders: "AiGetProviders",
  AiSaveProvider: "AiSaveProvider",
  AiDeleteProvider: "AiDeleteProvider",
  AiGetModels: "AiGetModels",
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
  /** Abandon an in-flight Request by its Id. The host still answers the original call. */
  Cancel = "Cancel",
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

  /**
   * Abort the call. The host cancels the command's CancellationToken; the promise still settles the
   * normal way — either rejecting with "Cancelled." or resolving with whatever the command chose to
   * return when interrupted. It is deliberately NOT settled here: one answer, one path, and no
   * pending entry left behind for a response that is still coming.
   */
  signal?: AbortSignal;
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

    // Already aborted: settle here and send nothing. Posting Cancel first was worse than useless — the
    // host drops it against an id it has not seen yet, and the Request that follows then runs the call
    // to completion, uncancelled. The wording matches what the host sends for a real cancellation.
    if (options?.signal?.aborted) {
      reject(new Error("Cancelled."));
      return;
    }

    const id = `at-${Date.now()}-${++invokeSeq}`;

    // Settled through these wrappers so the abort listener goes with the call. A view that keeps one
    // AbortController for the whole page would otherwise leave one listener per invoke on it, each
    // holding a finished request id and each posting a Cancel for it when the user finally aborts.
    let stopListening: (() => void) | undefined;
    pendingCalls.set(id, {
      resolve: (value) => { stopListening?.(); resolve(value); },
      reject: (reason) => { stopListening?.(); reject(reason); },
      onProgress: options?.onProgress,
    });

    const message: WebViewMessage = {
      Type: MessageType.Request,
      Command: command,
      Payload: payload,
      Id: id,
    };

    const signal = options?.signal;
    if (signal) {
      const requestCancel = () =>
        webview.postMessage({ Type: MessageType.Cancel, Command: command, Payload: null, Id: id });
      signal.addEventListener("abort", requestCancel, { once: true });
      stopListening = () => signal.removeEventListener("abort", requestCancel);
    }

    webview.postMessage(message);
  });
}

// Expose for extension UIs (and the console) running inside the WebView.
if (typeof window !== "undefined") {
  ensureInvokeListener();
  (window as any).AT = { invoke };
}
