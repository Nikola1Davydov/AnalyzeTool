import { AnimatePresence, motion } from "motion/react";
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { bridge, CommandError } from "revit-bridge";
import { type ButtonRunResult, type PaneButton, type PaneFolder, paneCommands } from "../../api/pane";
import { spring } from "./Tile";

type Sheet = { title: string; body: ReactNode };
type Toast = { text: string; kind: "ok" | "error"; detail?: ReactNode; id: number };

type Actions = {
  /** A tap on a button: the same as a click on the ribbon — its command with its args, or its page — and counted. */
  activate: (folder: PaneFolder, button: PaneButton) => void;
  isRunning: (key: string) => boolean;
  openSheet: (title: string, body: ReactNode) => void;
  closeSheet: () => void;
  toast: (text: string, kind?: "ok" | "error", detail?: ReactNode) => void;
};

const ActionsContext = createContext<Actions | null>(null);

export function useActions() {
  const actions = useContext(ActionsContext);
  if (!actions) throw new Error("useActions outside ActionsProvider");
  return actions;
}

export function messageOf(error: unknown) {
  return error instanceof CommandError || error instanceof Error ? error.message : String(error);
}

function duration(milliseconds: number) {
  return milliseconds < 1000 ? `${Math.round(milliseconds)} ms` : `${(milliseconds / 1000).toFixed(1)} s`;
}

/**
 * The actions of the pane and the layers over the grid: a sheet (a result, errors, a list of actions) and a toast at the
 * top (the result of a run). Every tile — of the grid and of the dock — goes through `activate`.
 */
export function ActionsProvider({ children }: { children: ReactNode }) {
  const [sheet, setSheet] = useState<Sheet | null>(null);
  const [toastState, setToast] = useState<Toast | null>(null);
  const [running, setRunning] = useState<Set<string>>(new Set());
  const toastTimer = useRef<number | undefined>(undefined);

  const toast = useCallback((text: string, kind: "ok" | "error" = "ok", detail?: ReactNode) => {
    window.clearTimeout(toastTimer.current);
    setToast({ text, kind, detail, id: Date.now() });
    toastTimer.current = window.setTimeout(() => setToast(null), kind === "error" ? 8000 : 5000);
  }, []);

  const run = useCallback(
    async (button: PaneButton) => {
      setRunning((current) => new Set(current).add(button.key));
      try {
        const result = await bridge.invoke<ButtonRunResult>(paneCommands.RunButton, { key: button.key });
        if (result.openedPage) return;
        if (!result.ok) {
          const message = result.error ?? "Failed";
          toast(`${button.name}: ${message.split("\n")[0]}`, "error", <pre className="result error">{message}</pre>);
          return;
        }
        const text = result.text ?? "Done";
        toast(`${button.name}: ${text.split("\n")[0].slice(0, 80)}`, "ok", (
          <>
            <pre className="result">{text}</pre>
            <p className="muted small">{duration(result.elapsedMilliseconds)}</p>
          </>
        ));
      } catch (error) {
        toast(`${button.name}: ${messageOf(error).split("\n")[0]}`, "error", <pre className="result error">{messageOf(error)}</pre>);
      } finally {
        setRunning((current) => {
          const next = new Set(current);
          next.delete(button.key);
          return next;
        });
      }
    },
    [toast],
  );

  const activate = useCallback(
    (folder: PaneFolder, button: PaneButton) => {
      if (!folder.enabled) {
        setSheet({ title: folder.name, body: <p className="small">This extension is turned off. Turn it on in Manage buttons.</p> });
        return;
      }
      // An extension with compiler errors still runs its last good build: the problems stay one tap away on the folder
      void run(button);
    },
    [run],
  );

  const actions = useMemo<Actions>(
    () => ({
      activate,
      isRunning: (key) => running.has(key),
      openSheet: (title, body) => setSheet({ title, body }),
      closeSheet: () => setSheet(null),
      toast,
    }),
    [activate, running, toast],
  );

  return (
    <ActionsContext.Provider value={actions}>
      {children}
      <AnimatePresence>
        {sheet && (
          <motion.div
            key="backdrop"
            className="sheet-backdrop pane-sheet"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            onClick={() => setSheet(null)}
          >
            {/* A sheet like iOS: slides up on a spring, pulled down past a third it closes */}
            <motion.div
              className="sheet glass"
              role="dialog"
              initial={{ y: "100%" }}
              animate={{ y: 0 }}
              exit={{ y: "100%" }}
              transition={spring}
              drag="y"
              dragConstraints={{ top: 0, bottom: 0 }}
              dragElastic={{ top: 0.05, bottom: 0.9 }}
              onDragEnd={(_, info) => {
                if (info.offset.y > 120 || info.velocity.y > 600) setSheet(null);
              }}
              onClick={(event) => event.stopPropagation()}
            >
              <div className="sheet-grabber" />
              <div className="sheet-header">
                <strong>{sheet.title}</strong>
                <button className="text-button" onClick={() => setSheet(null)}>
                  Done
                </button>
              </div>
              {/* Drags in the body scroll it and do not pull the sheet */}
              <div className="sheet-body" onPointerDownCapture={(event) => event.stopPropagation()}>
                {sheet.body}
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
      <AnimatePresence>
        {toastState && (
          <motion.button
            key={toastState.id}
            className={`toast glass ${toastState.kind}`}
            title="Details"
            initial={{ y: -80, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: -80, opacity: 0 }}
            transition={spring}
            onClick={() => {
              if (toastState.detail) setSheet({ title: "Result", body: toastState.detail });
              setToast(null);
            }}
          >
            <span className={`toast-dot ${toastState.kind}`} />
            <span className="toast-text">{toastState.text}</span>
          </motion.button>
        )}
      </AnimatePresence>
    </ActionsContext.Provider>
  );
}

/** The problems of an extension: load errors and compiler errors with file and line. */
export function ProblemList({ problems }: { problems: string[] }) {
  return (
    <>
      {problems.slice(0, 30).map((problem, index) => (
        <div key={index} className="compile-error">
          <div className="error small">{problem}</div>
        </div>
      ))}
    </>
  );
}
