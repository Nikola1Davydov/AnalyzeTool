import { UpdatePayload } from "./stores/types";
import type { SetDataToParameters } from "./stores/types";
import type { DocumentHealthPayload } from "./stores/useDocumentHealthStore";

export type WebViewMessage = {
  Type: string;
  Command: string;
  Payload: any;
};

export interface CommandPayloads {
  [Commands.Selection]: { elementIds: number[] };
  [Commands.Isolation]: { elementIds: number[] };
  [Commands.GetCategories]: { null: null };
  [Commands.GetDataByCategoryName]: { categoryName: string };
  [Commands.GetDocumentHealth]: DocumentHealthPayload;
  [Commands.CheckUpdate]: UpdatePayload;
  [Commands.SetDataToParameters]: SetDataToParameters;
  // … add hier a new commads and their payloads
}
export const Commands = {
  Selection: "Selection",
  Isolation: "Isolation",
  GetCategories: "GetCategories",
  GetDataByCategoryName: "GetDataByCategoryName",
  GetDocumentHealth: "GetDocumentHealth",
  CheckUpdate: "CheckUpdate",
  SetDataToParameters: "SetDataToParameters",
} as const;

export const enum MessageType {
  Request = "Request",
  Response = "Response",
}

export function sendRequest<C extends keyof CommandPayloads>(
  command: C,
  payload: CommandPayloads[C],
): Promise<any> {
  return new Promise((reject) => {
    if (!(window as any).chrome?.webview) {
      reject(new Error("WebView2 messaging not available"));
      return;
    }

    const message: WebViewMessage = {
      Type: MessageType.Request,
      Command: command,
      Payload: payload,
    };

    (window as any).chrome.webview.postMessage(message);
  });
}
