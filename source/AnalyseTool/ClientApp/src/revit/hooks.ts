import { useMutation, useQuery } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { bridge, type InvokeOptions, type ProgressInfo } from "revit-bridge";

// React hooks over revit-bridge. The page always works with the active document:
// every call carries its key in the Document header, and a call made with data of another document gets
// document_changed — RevitQueryProvider then re-reads the active document and the queries of the new one run.

export type ActiveDocument = { key: string; title: string };

export const activeDocumentKey = ["activeDocument"] as const;

/** The active document, or `null` when none is open. Asked without the Document header. */
export function useActiveDocument() {
  return useQuery({
    queryKey: activeDocumentKey,
    queryFn: ({ signal }) => bridge.invoke<ActiveDocument | null>("GetActiveDocument", null, { signal }),
  });
}

function documentHeaders(document: ActiveDocument | null | undefined): InvokeOptions["headers"] {
  return document ? { Document: document.key } : undefined;
}

/** Reads data with a command; cached per document, so going back to a document shows its data at once. */
export function useCommand<T>(command: string, payload: unknown = null) {
  const document = useActiveDocument();

  return useQuery({
    queryKey: [document.data?.key ?? null, command, payload],
    enabled: document.isSuccess,
    queryFn: ({ signal }) => bridge.invoke<T>(command, payload, { signal, headers: documentHeaders(document.data) }),
  });
}

/** Runs a command on demand, with its progress and a way to cancel it. */
export function useCommandMutation<T, TPayload = unknown>(command: string) {
  const document = useActiveDocument();
  const [progress, setProgress] = useState<ProgressInfo | null>(null);
  const abort = useRef<AbortController | null>(null);

  const mutation = useMutation({
    mutationFn: (payload: TPayload) => {
      abort.current = new AbortController();
      setProgress(null);
      return bridge.invoke<T>(command, payload, {
        signal: abort.current.signal,
        headers: documentHeaders(document.data),
        onProgress: setProgress,
      });
    },
    onSettled: () => setProgress(null),
  });

  return { ...mutation, progress, cancel: () => abort.current?.abort() };
}
