import { FluentProvider, Spinner, Toaster } from "@fluentui/react-components";
import { useIsFetching, useIsMutating } from "@tanstack/react-query";
import { type ReactNode, useEffect, useState } from "react";
import { applyTheme, useEffectiveTheme } from "../pages/dock/profile";
import { ConfirmProvider } from "./confirm";
import { darkTheme, lightTheme } from "./theme";
import { TOASTER_ID } from "./notify";

function usePrefersDark() {
  const [dark, setDark] = useState(() => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false);
  useEffect(() => {
    const media = window.matchMedia?.("(prefers-color-scheme: dark)");
    if (!media) return;
    const update = () => setDark(media.matches);
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);
  return dark;
}

/**
 * The strip at the bottom while Revit works on a call of this window. A call that ends within the delay never shows it,
 * so quick reads do not flash. The page that started a long command shows its own progress and Cancel.
 */
function BusyBar() {
  const mutating = useIsMutating();
  const fetching = useIsFetching();
  const busy = mutating + fetching > 0;
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (!busy) {
      setVisible(false);
      return;
    }
    const timer = setTimeout(() => setVisible(true), 600);
    return () => clearTimeout(timer);
  }, [busy]);

  if (!visible) return null;
  return (
    <div className="busy-bar no-print" role="status">
      <Spinner size="extra-tiny" />
      <span>Waiting for Revit…</span>
    </div>
  );
}

/** Theme, toasts, the confirmation dialog and the busy strip around every page. */
export function AppFrame({ children }: { children: ReactNode }) {
  // Light or dark as the profile of the pane says — Revit's theme by default — the same in the pane and in every window
  const theme = useEffectiveTheme();
  const systemDark = usePrefersDark();
  const dark = theme === "dark" || (theme === "system" && systemDark);
  useEffect(() => applyTheme(theme), [theme]);
  return (
    <FluentProvider theme={dark ? darkTheme : lightTheme} className="app-frame">
      <ConfirmProvider>
        {children}
        <Toaster toasterId={TOASTER_ID} position="top-end" pauseOnHover />
        <BusyBar />
      </ConfirmProvider>
    </FluentProvider>
  );
}
