export type WebViewMessage = {
  Type: string;
  Command: string;
  Payload: any;
};

export function sendRequest(command: string, payload: any): Promise<any> {
  return new Promise((reject) => {
    if (!(window as any).chrome?.webview) {
      reject(new Error("WebView2 messaging not available"));
      return;
    }

    const message: WebViewMessage = {
      Type: "Request",
      Command: command,
      Payload: payload,
    };

    (window as any).chrome.webview.postMessage(message);
  });
}
