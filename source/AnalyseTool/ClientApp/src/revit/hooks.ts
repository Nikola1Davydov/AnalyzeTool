import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback, useRef, useState } from "react";
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

/** Calls a command with the Document header of the active document — for calls made from event handlers. */
export function useInvoke() {
  const document = useActiveDocument();
  const key = document.data?.key;
  return useCallback(
    <T,>(command: string, payload: unknown = null, options: InvokeOptions = {}) =>
      bridge.invoke<T>(command, payload, {
        ...options,
        headers: key ? { Document: key, ...options.headers } : options.headers,
      }),
    [key],
  );
}

/** The query key prefix of the commands that do not depend on the document (extensions, settings, versions). */
export const appQueryKey = ["app"] as const;

/** Reads data with a command that does not depend on the active document. */
export function useAppQuery<T>(command: string, payload: unknown = null, options: { enabled?: boolean; refetchInterval?: number } = {}) {
  return useQuery({
    queryKey: [...appQueryKey, command, payload],
    queryFn: ({ signal }) => bridge.invoke<T>(command, payload, { signal }),
    ...options,
  });
}
