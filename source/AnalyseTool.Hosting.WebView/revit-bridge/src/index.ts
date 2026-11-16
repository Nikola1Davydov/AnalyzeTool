import { createMockBridge, createWebViewBridge, type MockHandler, type RevitBridge } from "./bridge";
import type { WebViewLike } from "./protocol";

export * from "./bridge";
export * from "./protocol";

const webview = (globalThis as { chrome?: { webview?: WebViewLike } }).chrome?.webview;
const mock = createMockBridge();

/** The bridge of this page: WebView2 inside Revit, the mock handlers in a normal browser. */
export const bridge: RevitBridge = webview ? createWebViewBridge(webview) : mock;

/** Registers mock handlers for development without Revit; ignored inside Revit. */
export function registerMocks(handlers: Record<string, MockHandler>): void {
  Object.assign(mock.handlers, handlers);
}
