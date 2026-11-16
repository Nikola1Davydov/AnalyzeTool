import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { CommandError, ErrorCodes } from "revit-bridge";
import { activeDocumentKey } from "./hooks";

// No push events in the template: the page learns about another active document from the
// document_changed answer to its next call, and re-reads the active document then.
function onError(error: unknown) {
  if (error instanceof CommandError && (error.code === ErrorCodes.DocumentChanged || error.code === ErrorCodes.NoDocument)) {
    void queryClient.invalidateQueries({ queryKey: activeDocumentKey });
  }
}

const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: {
    // A failed Revit call is an answer, not a network glitch: do not repeat it
    queries: { retry: false, refetchOnWindowFocus: false },
  },
});

export function RevitQueryProvider({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
