export type WebViewMessage = {
  Type: string;
  Command: string;
  Payload: any;
};

export interface UpdatePayload {
  currentVersion?: string;
  latestVersion?: string;
  isUpdateAvailable?: boolean;
  releaseUrl?: string;
}

export interface CommandPayloads {
  Selection: { elementIds: number[] };
  Isolation: { elementIds: number[] };
  GetCategories: { null: null };
  UpdateDataParameterFilledEmptyPage: { categoryName: string };
  CheckUpdate: UpdatePayload;
  // … добавляешь сюда все команды
}

export function sendRequest<C extends keyof CommandPayloads>(
  command: C,
  payload: CommandPayloads[C]
): Promise<any> {
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
