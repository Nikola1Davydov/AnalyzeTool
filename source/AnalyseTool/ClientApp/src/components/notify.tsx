import { Toast, ToastBody, ToastTitle, useToastController } from "@fluentui/react-components";
import { useMemo } from "react";
import { CommandError } from "revit-bridge";

export const TOASTER_ID = "analysetool-toaster";

/** The text of an error for the user. */
export function errorText(error: unknown): string {
  if (error instanceof Error) return error.message;
  return String(error);
}

/** The user cancelled the call: not worth a message. */
export function isCancelled(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === "AbortError") ||
    (error instanceof CommandError && error.code === "canceled")
  );
}

type Intent = "success" | "error" | "warning" | "info";

/** Toasts in the corner of the window. */
export function useNotify() {
  const { dispatchToast } = useToastController(TOASTER_ID);
  return useMemo(() => {
    const show = (intent: Intent, title: string, body?: string) =>
      dispatchToast(
        <Toast>
          <ToastTitle>{title}</ToastTitle>
          {body && <ToastBody style={{ whiteSpace: "pre-wrap", wordBreak: "break-word" }}>{body}</ToastBody>}
        </Toast>,
        { intent, timeout: intent === "error" ? 8000 : 4000 },
      );
    return {
      success: (title: string, body?: string) => show("success", title, body),
      info: (title: string, body?: string) => show("info", title, body),
      warning: (title: string, body?: string) => show("warning", title, body),
      error: (title: string, body?: string) => show("error", title, body),
      /** A failed call; a cancelled one stays silent. */
      failed: (error: unknown, title = "Something went wrong") => {
        if (!isCancelled(error)) show("error", title, errorText(error));
      },
    };
  }, [dispatchToast]);
}
