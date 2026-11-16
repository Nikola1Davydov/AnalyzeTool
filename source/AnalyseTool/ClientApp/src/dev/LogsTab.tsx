import { useEffect, useRef, useState } from "react";
import { bridge } from "revit-bridge";
import { DEV_COMMAND, LOG_LEVELS, type LogEntry, type LogLevel, type LogPage } from "./protocol";

/** How often the page asks for new records; the host drops the records of this polling from the buffer. */
const POLL_INTERVAL_MS = 500;
/** How many records the page keeps; the host keeps as many. */
const MAX_ENTRIES = 1000;

/** The live log: polls GetLogEntries with the sequence of the last record it has and appends what comes back. */
export function LogsTab() {
  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [paused, setPaused] = useState(false);
  const [minLevel, setMinLevel] = useState<LogLevel>("Debug");
  const [filter, setFilter] = useState("");
  const [follow, setFollow] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const last = useRef(0);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (paused) return;
    const abort = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;

    const poll = async () => {
      try {
        const page = await bridge.invoke<LogPage>(DEV_COMMAND, { afterSequence: last.current }, { signal: abort.signal });
        last.current = page.lastSequence;
        if (page.entries.length > 0) {
          setEntries((current) => [...current, ...page.entries].slice(-MAX_ENTRIES));
        }
        setError(null);
      } catch (reason) {
        if (abort.signal.aborted) return;
        setError(reason instanceof Error ? reason.message : String(reason));
      }
      if (!abort.signal.aborted) timer = setTimeout(() => void poll(), POLL_INTERVAL_MS);
    };

    void poll();
    return () => {
      abort.abort();
      clearTimeout(timer);
    };
  }, [paused]);

  useEffect(() => {
    if (follow) bottom.current?.scrollIntoView({ block: "end" });
  }, [entries, follow]);

  const minIndex = LOG_LEVELS.indexOf(minLevel);
  const needle = filter.trim().toLowerCase();
  const visible = entries.filter(
    (entry) =>
      LOG_LEVELS.indexOf(entry.level) >= minIndex &&
      (needle === "" || entry.message.toLowerCase().includes(needle) || entry.category.toLowerCase().includes(needle)),
  );

  return (
    <section className="logs">
      <div className="toolbar">
        <button onClick={() => setPaused((value) => !value)}>{paused ? "Resume" : "Pause"}</button>
        <button onClick={() => setEntries([])}>Clear</button>
        <select value={minLevel} onChange={(event) => setMinLevel(event.target.value as LogLevel)}>
          {LOG_LEVELS.map((level) => (
            <option key={level} value={level}>
              {level}+
            </option>
          ))}
        </select>
        <input placeholder="Filter message or category" value={filter} onChange={(event) => setFilter(event.target.value)} />
        <label>
          <input type="checkbox" checked={follow} onChange={(event) => setFollow(event.target.checked)} /> Follow
        </label>
        <span className="muted">
          {visible.length} / {entries.length}
        </span>
      </div>
      {error && <p className="error">{error}</p>}
      <ol className="log-list">
        {visible.map((entry) => (
          <li key={entry.sequence} className={`level-${entry.level.toLowerCase()}`}>
            <span className="time">{formatTime(entry.timestamp)}</span>
            <span className="level">{entry.level.slice(0, 3).toUpperCase()}</span>
            <span className="category" title={entry.category}>
              {shortCategory(entry.category)}
            </span>
            <span className="message">{entry.message}</span>
            {entry.exception && <pre className="exception">{entry.exception}</pre>}
          </li>
        ))}
        <div ref={bottom} />
      </ol>
    </section>
  );
}

function formatTime(timestamp: string): string {
  const date = new Date(timestamp);
  return Number.isNaN(date.getTime()) ? timestamp : date.toLocaleTimeString(undefined, { hour12: false }) + "." + String(date.getMilliseconds()).padStart(3, "0");
}

function shortCategory(category: string): string {
  return category.slice(category.lastIndexOf(".") + 1);
}
