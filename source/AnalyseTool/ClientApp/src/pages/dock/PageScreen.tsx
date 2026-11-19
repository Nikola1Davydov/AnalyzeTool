import { useEffect, useRef } from "react";
import { bridge, CommandError } from "revit-bridge";

/**
 * A small interface of a button (<c>"dockable": true</c>) inside the pane: its page in a frame, the pane around it. The
 * page's <c>AT.invoke</c> (the frame bridge AnalyseTool puts into it) asks this screen, which runs the command and answers.
 */
export function PageScreen({ url, title }: { url: string; title: string }) {
  const frame = useRef<HTMLIFrameElement | null>(null);
  const origin = new URL(url).origin;

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      const message = event.data as { type?: string; id?: string; command?: string; payload?: unknown } | null;
      // Only the page of this screen, from its own origin
      if (event.origin !== origin || event.source !== frame.current?.contentWindow || message?.type !== "at-invoke" || !message.id || !message.command) return;
      const reply = (answer: Record<string, unknown>) => frame.current?.contentWindow?.postMessage({ type: "at-result", id: message.id, ...answer }, origin);
      bridge
        .invoke(message.command, message.payload ?? null)
        .then((value) => reply({ value }))
        .catch((error: unknown) =>
          reply({ error: { code: error instanceof CommandError ? error.code : "failed", message: error instanceof Error ? error.message : String(error) } }),
        );
    };
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, [origin]);

  return (
    <div className="page-screen">
      <iframe ref={frame} className="page-frame" src={url} title={title} />
    </div>
  );
}
