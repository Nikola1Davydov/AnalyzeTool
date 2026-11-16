import type { CommandErrorInfo, HostMessage, ProgressInfo, RequestMessage, WebViewLike } from "./protocol";

/** A command failed in the host; `code` is stable, `message` is for the user. */
export class CommandError extends Error {
  readonly code: string;

  constructor(error: CommandErrorInfo) {
    super(error.message);
    this.name = "CommandError";
    this.code = error.code;
  }
}

export type InvokeOptions = {
  /** Intermediate progress of a long command. */
  onProgress?: (progress: ProgressInfo) => void;
  /** Cancels the call: the host gets `cancel`, the promise rejects with an `AbortError`. */
  signal?: AbortSignal;
  /** Values outside the payload, for example `Document` — the key of the document the data came from. */
  headers?: Record<string, string>;
};

export interface RevitBridge {
  /** `false` outside Revit, where calls go to the mock handlers. */
  readonly inRevit: boolean;
  invoke<T = unknown>(command: string, payload?: unknown, options?: InvokeOptions): Promise<T>;
}

type Pending = {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  onProgress?: (progress: ProgressInfo) => void;
};

/** The bridge over WebView2 messaging: requests and responses matched by `id`. */
export function createWebViewBridge(webview: WebViewLike): RevitBridge {
  const pending = new Map<string, Pending>();
  let sequence = 0;

  webview.addEventListener("message", (event) => {
    const message = event.data as HostMessage | undefined;
    if (!message?.id) return;

    const call = pending.get(message.id);
    if (!call) return; // cancelled or not ours

    if (message.type === "progress") {
      call.onProgress?.(message.payload);
      return;
    }

    pending.delete(message.id);
    if (message.error) call.reject(new CommandError(message.error));
    else call.resolve(message.payload ?? null); // Release omits null values
  });

  return {
    inRevit: true,
    invoke<T>(command: string, payload: unknown = null, options: InvokeOptions = {}) {
      return new Promise<T>((resolve, reject) => {
        const { signal } = options;
        if (signal?.aborted) {
          reject(abortError());
          return;
        }

        const id = `${Date.now().toString(36)}-${++sequence}`;
        pending.set(id, { resolve: resolve as (value: unknown) => void, reject, onProgress: options.onProgress });

        signal?.addEventListener(
          "abort",
          () => {
            if (!pending.delete(id)) return;
            webview.postMessage({ type: "cancel", id });
            reject(abortError());
          },
          { once: true },
        );

        const message: RequestMessage = { type: "request", id, command, headers: options.headers, payload };
        webview.postMessage(message);
      });
    },
  };
}

export type MockHandler = (payload: unknown, options: InvokeOptions) => unknown;

/**
 * The bridge outside Revit — `npm run dev` in a normal browser: commands go to handlers the page registers,
 * an unknown command fails with `not_found` as in the host.
 */
export function createMockBridge(handlers: Record<string, MockHandler> = {}): RevitBridge & {
  handlers: Record<string, MockHandler>;
} {
  return {
    inRevit: false,
    handlers,
    async invoke<T>(command: string, payload: unknown = null, options: InvokeOptions = {}) {
      const handler = handlers[command];
      if (!handler) throw new CommandError({ code: "not_found", message: `No mock for '${command}'.` });
      if (options.signal?.aborted) throw abortError();
      return (await handler(payload, options)) as T;
    },
  };
}

function abortError(): Error {
  return new DOMException("The call was cancelled.", "AbortError");
}
