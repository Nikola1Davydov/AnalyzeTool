export type WebViewMessage = {
  Type: string;
  Command: string;
  Payload: unknown;
  Id?: string;
  Error?: string | null;
};

/** The part of WebView2's window.chrome.webview this file uses — typed once instead of as any. */
type HostWebView = {
  postMessage(message: WebViewMessage): void;
  addEventListener(type: "message", listener: (event: { data: WebViewMessage }) => void): void;
  __atInvokeAttached?: boolean;
};

function hostWebView(): HostWebView | undefined {
  return (window as unknown as { chrome?: { webview?: HostWebView } }).chrome?.webview;
}

export const Commands = {
  SelectionInRevit: "SelectionInRevit",
  IsolationInRevit: "IsolationInRevit",
  GetCategoriesInRevit: "GetCategoriesInRevit",
  GetDataByCategoryName: "GetDataByCategoryName",
  CheckUpdate: "CheckUpdate",
  GetDocumentData: "GetDocumentData",
  SetDataToParameters: "SetDataToParameters",
} as const;

export const enum MessageType {
  Request = "Request",
  Response = "Response",
  /** Abandon an in-flight Request by its Id. The host still answers the original call. */
  Cancel = "Cancel",
  /** "Are you receiving?" — answered with a Pong from the host's receive handler, before any queue. */
  Ping = "Ping",
}

// --- AT.invoke: correlated request/response over the same WebView channel -------------------
// Each call gets a unique Id; the host echoes it back, and we resolve the matching promise.
// This is the generic entry point any command (built-in or extension) can be called through.

export type ProgressInfo = { fraction: number; message?: string | null };

type PendingCall = {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  onProgress?: (p: ProgressInfo) => void;
  /** When the request was posted — the age of the oldest one is the page's only way to know that
   *  the host has stopped answering (see oldestPendingAge). */
  sentAt: number;
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

/**
 * Milliseconds the longest-waiting call has been without an answer, 0 when nothing is pending.
 *
 * This is the page's stall detector (#102). Every message from the page reaches the host on Revit's
 * UI thread, and a long Revit operation holds that thread — so while Revit works, NOTHING the page
 * asks is even received, GetQueueStatus included, and the busy indicator that exists to explain the
 * silence goes silent with it. The one fact the page still has is its own unanswered requests.
 */
export function oldestPendingAge(now: number = Date.now()): number {
  let oldest = 0;
  for (const call of pendingCalls.values()) oldest = Math.max(oldest, now - call.sentAt);
  return oldest;
}

// --- Heartbeat: a Ping every tenth of a second, so the stall is noticed within half a second and not
// whenever the next real call happens to be sent (the idle status poll is ten seconds apart, which
// made the first version of the busy bar appear up to thirteen seconds late). At most one Ping is
// outstanding: while the host is held, nothing more is sent, and the age of that one Ping is the
// measurement. The host answers from its receive handler without touching the queue, so this is
// the cheapest round trip there is and it is never logged.
const HEARTBEAT_MS = 100;
let pingSentAt: number | null = null;
let heartbeatTimer: number | null = null;
let heartbeatUsers = 0;
// A host that has never answered a Ping is a host that does not know the message (a page deployed
// ahead of its DLL, which happens whenever Revit holds the old one during a build). Until the first
// Pong the heartbeat proves nothing, and hostStallMs falls back to the pending calls alone.
let heartbeatArmed = false;

function sendPing() {
  if (pingSentAt !== null) return; // the previous one has not come back — that is the signal
  const webview = hostWebView();
  if (!webview) return;
  ensureInvokeListener();
  pingSentAt = Date.now();
  webview.postMessage({ Type: MessageType.Ping, Command: "Ping", Payload: null, Id: "hb" });
}

/** Starts the heartbeat (reference-counted: every component that reads hostStallMs calls this). */
export function startHeartbeat(): void {
  heartbeatUsers++;
  if (heartbeatTimer !== null) return;
  heartbeatTimer = window.setInterval(sendPing, HEARTBEAT_MS);
  sendPing();
}

export function stopHeartbeat(): void {
  heartbeatUsers = Math.max(0, heartbeatUsers - 1);
  if (heartbeatUsers > 0 || heartbeatTimer === null) return;
  clearInterval(heartbeatTimer);
  heartbeatTimer = null;
  pingSentAt = null;
}

/**
 * Milliseconds the host has been silent: the age of the unanswered Ping, or of the oldest unanswered
 * call, whichever is longer. 0 when the host is answering.
 */
export function hostStallMs(now: number = Date.now()): number {
  const ping = heartbeatArmed && pingSentAt !== null ? now - pingSentAt : 0;
  return Math.max(ping, oldestPendingAge(now));
}

function ensureInvokeListener(): void {
  const webview = hostWebView();
  if (!webview || webview.__atInvokeAttached) return;
  webview.__atInvokeAttached = true;

  webview.addEventListener("message", (event) => {
    const message = event.data;
    if (!message) return;

    // Host-initiated broadcasts (no request Id) — e.g. "DocumentChanged". Re-dispatched as DOM events
    // so any view can listen without importing a bus: window.addEventListener("at:DocumentChanged", …).
    if (message.Type === "Event") {
      window.dispatchEvent(new CustomEvent(`at:${message.Command}`, { detail: message.Payload }));
      return;
    }

    if (message.Type === "Pong") {
      pingSentAt = null;
      heartbeatArmed = true;
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
// T defaults to any on purpose: an untyped call site (an extension page, the console) gets the result
// as it always did. Typed callers go through generated/commands.ts.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function invoke<T = any>(
  command: string,
  payload: unknown = null,
  options?: InvokeOptions,
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const webview = hostWebView();
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
      resolve: (value) => { stopListening?.(); resolve(value as T); },
      reject: (reason) => { stopListening?.(); reject(reason); },
      onProgress: options?.onProgress,
      sentAt: Date.now(),
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
  (window as unknown as { AT: unknown }).AT = { invoke };
}
