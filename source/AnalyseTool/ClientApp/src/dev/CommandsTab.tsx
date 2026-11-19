import { useState } from "react";
import { bridge, CommandError, type ProgressInfo } from "revit-bridge";
import { useActiveDocument } from "../revit/hooks";
import type { CommandInfo } from "./protocol";

type Outcome =
  | { kind: "success"; value: unknown; elapsedMs: number }
  | { kind: "error"; code: string; message: string; elapsedMs: number };

/** Runs any registered command with a JSON payload typed by hand — a Swagger of the pipeline. */
export function CommandsTab({ commands }: { commands: CommandInfo[] }) {
  const document = useActiveDocument();
  const [selectedName, setSelectedName] = useState(commands[0]?.name ?? "");
  const [payloadText, setPayloadText] = useState("");
  const [sendDocument, setSendDocument] = useState(true);
  const [running, setRunning] = useState<AbortController | null>(null);
  const [progress, setProgress] = useState<ProgressInfo | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [payloadError, setPayloadError] = useState<string | null>(null);

  const selected = commands.find((command) => command.name === selectedName);

  const select = (name: string) => {
    const command = commands.find((item) => item.name === name);
    setSelectedName(name);
    setPayloadText(command?.requestType ? "{\n  \n}" : "");
    setOutcome(null);
    setPayloadError(null);
  };

  const run = async () => {
    if (!selected) return;

    let payload: unknown = null;
    const text = payloadText.trim();
    if (text !== "") {
      try {
        payload = JSON.parse(text);
      } catch (reason) {
        setPayloadError(reason instanceof Error ? reason.message : String(reason));
        return;
      }
    }
    setPayloadError(null);

    // Like the pages: the Document header fixes the document of the call, so switching documents gives document_changed
    const headers = sendDocument && document.data ? { Document: document.data.key } : undefined;
    const abort = new AbortController();
    setRunning(abort);
    setProgress(null);
    setOutcome(null);
    const started = performance.now();
    try {
      const value = await bridge.invoke(selected.name, payload, { headers, signal: abort.signal, onProgress: setProgress });
      setOutcome({ kind: "success", value, elapsedMs: performance.now() - started });
    } catch (reason) {
      const elapsedMs = performance.now() - started;
      if (reason instanceof CommandError) setOutcome({ kind: "error", code: reason.code, message: reason.message, elapsedMs });
      else if (reason instanceof DOMException && reason.name === "AbortError") setOutcome({ kind: "error", code: "aborted", message: "Cancelled by the page.", elapsedMs });
      else setOutcome({ kind: "error", code: "failed", message: String(reason), elapsedMs });
    } finally {
      setRunning(null);
      setProgress(null);
    }
  };

  return (
    <section className="commands">
      <div className="toolbar">
        <select value={selectedName} onChange={(event) => select(event.target.value)}>
          {commands.map((command) => (
            <option key={command.name} value={command.name}>
              {command.name}
            </option>
          ))}
        </select>
        <label title="Send the key of the active document in the Document header, as the pages do">
          <input type="checkbox" checked={sendDocument} onChange={(event) => setSendDocument(event.target.checked)} /> Document header
        </label>
      </div>

      {selected && (
        <>
          <p className="muted">
            {selected.description ?? "No description."}
            <br />
            {selected.requestType ?? "no request"} → {selected.responseType}
          </p>
          <textarea
            rows={6}
            spellCheck={false}
            placeholder={selected.requestType ? `JSON of ${selected.requestType}` : "The command takes no payload"}
            disabled={!selected.requestType}
            value={payloadText}
            onChange={(event) => setPayloadText(event.target.value)}
          />
          {payloadError && <p className="error">Invalid JSON: {payloadError}</p>}
          <div className="toolbar">
            <button disabled={running !== null} onClick={() => void run()}>
              Run
            </button>
            {running && <button onClick={() => running.abort()}>Cancel</button>}
            {progress && (
              <>
                <progress value={progress.fraction} /> {progress.message}
              </>
            )}
          </div>
        </>
      )}

      {outcome?.kind === "success" && (
        <>
          <p className="muted">Succeeded in {Math.round(outcome.elapsedMs)} ms</p>
          <pre className="result">{JSON.stringify(outcome.value, null, 2) ?? "null"}</pre>
        </>
      )}
      {outcome?.kind === "error" && (
        <p className="error">
          {outcome.code} after {Math.round(outcome.elapsedMs)} ms: {outcome.message}
        </p>
      )}
    </section>
  );
}
