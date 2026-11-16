// The page <-> host protocol. Keep in step with Transport/WebViewTransport.cs (WebMessage).
//
//   page -> host: { type: "request", id, command, headers?, payload? }, { type: "cancel", id }
//   host -> page: { type: "progress", id, payload: { fraction, message } }, { type: "response", id, payload?, error? }

export type ProgressInfo = { fraction: number; message?: string | null };

export type CommandErrorInfo = { code: string; message: string };

export type RequestMessage = {
  type: "request";
  id: string;
  command: string;
  headers?: Record<string, string>;
  payload?: unknown;
};

export type CancelMessage = { type: "cancel"; id: string };

export type ProgressMessage = { type: "progress"; id: string; payload: ProgressInfo };

export type ResponseMessage = {
  type: "response";
  id: string;
  payload?: unknown;
  error?: CommandErrorInfo | null;
};

export type HostMessage = ProgressMessage | ResponseMessage;

/** The `window.chrome.webview` object of WebView2, or a fake of it in tests. */
export type WebViewLike = {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
};

/** The error codes the host itself returns; the add-in adds its own (for example `document_changed`). */
export const ErrorCodes = {
  NotFound: "not_found",
  BadRequest: "bad_request",
  AlreadyRunning: "already_running",
  Canceled: "canceled",
  Failed: "failed",
  NoDocument: "no_document",
  DocumentChanged: "document_changed",
} as const;
