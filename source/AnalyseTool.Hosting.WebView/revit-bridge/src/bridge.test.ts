import { describe, expect, it } from "vitest";
import { CommandError, createMockBridge, createWebViewBridge } from "./bridge";
import type { RequestMessage, WebViewLike } from "./protocol";

/** A fake `chrome.webview`: records what the page posts and lets the test answer as the host. */
function fakeWebView() {
  const posted: unknown[] = [];
  let listener: ((event: { data: unknown }) => void) | undefined;
  const webview: WebViewLike = {
    postMessage: (message) => posted.push(message),
    addEventListener: (_, handler) => (listener = handler),
  };
  return {
    webview,
    posted,
    lastRequest: () => posted.at(-1) as RequestMessage,
    send: (data: unknown) => listener?.({ data }),
  };
}

describe("createWebViewBridge", () => {
  it("posts a request and resolves with the payload of the matching response", async () => {
    const host = fakeWebView();
    const bridge = createWebViewBridge(host.webview);

    const call = bridge.invoke<string>("Greet", { name: "Revit" }, { headers: { Document: "A.rvt" } });
    const request = host.lastRequest();
    host.send({ type: "response", id: request.id, payload: "Hello, Revit" });

    await expect(call).resolves.toBe("Hello, Revit");
    expect(request).toMatchObject({ type: "request", command: "Greet", headers: { Document: "A.rvt" }, payload: { name: "Revit" } });
  });

  it("resolves with null when the host omits the payload", async () => {
    const host = fakeWebView();
    const call = createWebViewBridge(host.webview).invoke("DoNothing");

    host.send({ type: "response", id: host.lastRequest().id });

    await expect(call).resolves.toBeNull();
  });

  it("rejects with a CommandError that keeps the code", async () => {
    const host = fakeWebView();
    const call = createWebViewBridge(host.webview).invoke("Rename");

    host.send({ type: "response", id: host.lastRequest().id, error: { code: "document_changed", message: "Refresh." } });

    await expect(call).rejects.toBeInstanceOf(CommandError);
    await expect(call).rejects.toMatchObject({ code: "document_changed", message: "Refresh." });
  });

  it("passes progress to onProgress until the response", async () => {
    const host = fakeWebView();
    const fractions: number[] = [];
    const call = createWebViewBridge(host.webview).invoke("Count", null, { onProgress: (p) => fractions.push(p.fraction) });
    const { id } = host.lastRequest();

    host.send({ type: "progress", id, payload: { fraction: 0.5 } });
    host.send({ type: "progress", id, payload: { fraction: 1 } });
    host.send({ type: "response", id, payload: 3 });

    await expect(call).resolves.toBe(3);
    expect(fractions).toEqual([0.5, 1]);
  });

  it("matches responses by id, not by order", async () => {
    const host = fakeWebView();
    const bridge = createWebViewBridge(host.webview);
    const first = bridge.invoke("A");
    const firstId = host.lastRequest().id;
    const second = bridge.invoke("B");
    const secondId = host.lastRequest().id;

    host.send({ type: "response", id: secondId, payload: "b" });
    host.send({ type: "response", id: firstId, payload: "a" });

    await expect(first).resolves.toBe("a");
    await expect(second).resolves.toBe("b");
  });

  it("sends cancel and rejects with AbortError when the signal aborts", async () => {
    const host = fakeWebView();
    const abort = new AbortController();
    const call = createWebViewBridge(host.webview).invoke("Long", null, { signal: abort.signal });
    const { id } = host.lastRequest();

    abort.abort();

    await expect(call).rejects.toMatchObject({ name: "AbortError" });
    expect(host.posted.at(-1)).toEqual({ type: "cancel", id });
  });
});

describe("createMockBridge", () => {
  it("calls the handler of the command", async () => {
    const bridge = createMockBridge({ Greet: (payload) => `Hello, ${(payload as { name: string }).name}` });

    await expect(bridge.invoke("Greet", { name: "browser" })).resolves.toBe("Hello, browser");
    expect(bridge.inRevit).toBe(false);
  });

  it("fails an unknown command with not_found", async () => {
    await expect(createMockBridge().invoke("Missing")).rejects.toMatchObject({ code: "not_found" });
  });
});
