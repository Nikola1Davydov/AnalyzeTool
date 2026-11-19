import { useMutation, useQueryClient } from "@tanstack/react-query";
import { bridge } from "revit-bridge";
import { appQueryKey } from "../revit/hooks";
import { useNotify } from "./notify";

type ActionOptions<T, P> = {
  /** A toast on success; a function may return undefined for none. */
  success?: string | ((data: T, payload: P) => string | undefined);
  /** The title of the error toast. */
  failure?: string;
  onSuccess?: (data: T, payload: P) => void;
};

/**
 * Runs a command that changes the add-in's state (extensions, folders, settings): errors become a toast, and every
 * document-independent query is read again afterwards, so the lists show the new state.
 */
export function useAppAction<T, P = void>(command: string, options: ActionOptions<T, P> = {}) {
  const queryClient = useQueryClient();
  const notify = useNotify();
  return useMutation({
    mutationFn: (payload: P) => bridge.invoke<T>(command, payload ?? null),
    onSuccess: (data, payload) => {
      void queryClient.invalidateQueries({ queryKey: appQueryKey });
      const text = typeof options.success === "function" ? options.success(data, payload) : options.success;
      if (text) notify.success(text);
      options.onSuccess?.(data, payload);
    },
    onError: (error) => notify.failed(error, options.failure),
  });
}
