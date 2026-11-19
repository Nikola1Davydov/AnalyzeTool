import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
} from "@fluentui/react-components";
import { createContext, type ReactNode, useCallback, useContext, useRef, useState } from "react";

// One confirmation dialog per window, asked for with `await confirm({...})` instead of window.confirm.

export type ConfirmOptions = {
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** The action destroys or replaces something: the confirm button is red. */
  danger?: boolean;
};

type Ask = (options: ConfirmOptions) => Promise<boolean>;

const ConfirmContext = createContext<Ask>(async () => false);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<ConfirmOptions | null>(null);
  const resolver = useRef<((value: boolean) => void) | null>(null);

  const ask = useCallback<Ask>((next) => {
    resolver.current?.(false);
    setOptions(next);
    return new Promise<boolean>((resolve) => {
      resolver.current = resolve;
    });
  }, []);

  const close = (value: boolean) => {
    resolver.current?.(value);
    resolver.current = null;
    setOptions(null);
  };

  return (
    <ConfirmContext.Provider value={ask}>
      {children}
      <Dialog open={!!options} onOpenChange={(_, data) => !data.open && close(false)}>
        <DialogSurface style={{ maxWidth: "min(32rem, 95vw)" }}>
          <DialogBody>
            <DialogTitle>{options?.title}</DialogTitle>
            <DialogContent>{options?.message}</DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => close(false)}>
                {options?.cancelLabel ?? "Cancel"}
              </Button>
              <Button
                appearance="primary"
                className={options?.danger ? "danger-button" : undefined}
                onClick={() => close(true)}
              >
                {options?.confirmLabel ?? "OK"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </ConfirmContext.Provider>
  );
}

/** Asks the user to confirm; resolves `true` on the confirm button. */
export function useConfirm() {
  return useContext(ConfirmContext);
}
