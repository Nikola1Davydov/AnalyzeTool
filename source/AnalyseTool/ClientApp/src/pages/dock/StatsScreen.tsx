import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { bridge } from "revit-bridge";
import { appCommands } from "../../api/app";
import type { PaneOverview } from "../../api/pane";
import { type ButtonStatistic, type ButtonStatisticsResult, statisticsCommands } from "../../api/statistics";
import { messageOf, useActions } from "./Actions";
import { findButton } from "./profile";
import { TileIcon } from "./Tile";

const periods = [
  { days: 7, label: "7 days" },
  { days: 30, label: "30 days" },
  { days: 0, label: "All time" },
] as const;

type SortKey = "clicks" | "failures" | "average" | "last";

const duration = (milliseconds: number | null) =>
  milliseconds === null ? "—" : milliseconds < 1000 ? `${Math.round(milliseconds)} ms` : `${(milliseconds / 1000).toFixed(1)} s`;

/** Clicks of a button in the period: the last days of its 30, or all of them. */
function clicksIn(button: ButtonStatistic, days: number) {
  return days === 0 ? button.clicks : button.last30Days.slice(-days).reduce((sum, value) => sum + value, 0);
}

/**
 * The statistics of the buttons on this computer, in the pane: how often each is clicked, how long its command takes, how
 * often it fails. Of every button, or of one folder. The whole table opens in a window from the bottom link.
 */
export function StatsScreen({ overview, folder }: { overview: PaneOverview; folder?: string }) {
  const [days, setDays] = useState<number>(30);
  const [sort, setSort] = useState<SortKey>("clicks");
  const actions = useActions();
  const stats = useQuery({
    queryKey: ["buttonStatistics"],
    queryFn: ({ signal }) => bridge.invoke<ButtonStatisticsResult>(statisticsCommands.GetButtonStatistics, null, { signal }),
  });

  const all = useMemo(
    () => (stats.data?.buttons ?? []).filter((button) => !folder || button.extension.toLowerCase() === folder.toLowerCase()),
    [stats.data, folder],
  );
  const rows = useMemo(() => {
    const by: Record<SortKey, (row: ButtonStatistic) => number> = {
      clicks: (row) => clicksIn(row, days),
      failures: (row) => row.failures,
      average: (row) => row.averageMilliseconds ?? -1,
      last: (row) => (row.lastClick ? Date.parse(row.lastClick) : 0),
    };
    return all.filter((row) => clicksIn(row, days) > 0).sort((left, right) => by[sort](right) - by[sort](left));
  }, [all, days, sort]);

  const clicks = all.reduce((sum, button) => sum + clicksIn(button, days), 0);
  const failures = all.reduce((sum, button) => sum + button.failures, 0);
  const timed = all.filter((button) => button.averageMilliseconds !== null);
  const average = timed.length ? timed.reduce((sum, button) => sum + button.averageMilliseconds!, 0) / timed.length : null;
  const perDay = Array.from({ length: days === 7 ? 7 : 30 }, (_, index) => {
    const offset = (days === 7 ? 23 : 0) + index;
    return all.reduce((sum, button) => sum + (button.last30Days[offset] ?? 0), 0);
  });
  const folderName = folder ? (overview.folders.find((candidate) => candidate.id === folder)?.name ?? folder) : null;

  return (
    <div className="stats">
      <h1 className="large-title">{folderName ? `Statistics · ${folderName}` : "Statistics"}</h1>

      <div className="segmented stats-period" role="radiogroup">
        {periods.map((period) => (
          <button key={period.days} role="radio" aria-checked={days === period.days} className={days === period.days ? "on" : ""} onClick={() => setDays(period.days)}>
            {days === period.days && <span className="segmented-thumb" />}
            <span className="segmented-label">{period.label}</span>
          </button>
        ))}
      </div>

      {stats.isPending && <p className="muted small">Loading…</p>}
      {stats.isError && <p className="error small">{stats.error.message}</p>}

      {stats.isSuccess && (
        <>
          <div className="stat-tiles">
            <StatTile label="Clicks" value={clicks.toLocaleString()} />
            <StatTile label="Failed" value={failures.toLocaleString()} note="all time" alert={failures > 0} />
            <StatTile label="Usual run" value={duration(average)} note="average" />
          </div>

          <section className="stats-card">
            <h2>Clicks per day</h2>
            <DayChart values={perDay} />
          </section>

          <section className="stats-card">
            <h2>Buttons</h2>
            {rows.length === 0 ? (
              <p className="muted small">No clicks in this period.</p>
            ) : (
              <table className="stats-table">
                <thead>
                  <tr>
                    <th>Button</th>
                    <SortHeader label="Clicks" value="clicks" sort={sort} onSort={setSort} />
                    <SortHeader label="Failed" value="failures" sort={sort} onSort={setSort} />
                    <SortHeader label="Time" value="average" sort={sort} onSort={setSort} />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => {
                    const found = findButton(overview.folders, `${row.extension}/${row.button}`);
                    return (
                      <tr key={`${row.extension}/${row.button}`} onClick={() => actions.openSheet(row.button, <ButtonDetails row={row} />)}>
                        <td>
                          <span className="stats-name">
                            <TileIcon glyph={found?.button.glyph} image={found?.button.image} title={row.button} />
                            <span>
                              {row.button}
                              {!row.onRibbon && <span className="muted"> (gone)</span>}
                              {!folder && <span className="muted small stats-project">{row.extensionName}</span>}
                            </span>
                          </span>
                        </td>
                        <td className="number">{clicksIn(row, days)}</td>
                        <td className={`number ${row.failures > 0 ? "error" : "muted"}`}>{row.failures}</td>
                        <td className="number">{duration(row.averageMilliseconds)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </section>

          <p className="muted small center">
            Counted on this computer{stats.data.since ? ` since ${new Date(stats.data.since).toLocaleDateString()}` : ""}.{" "}
            <button
              className="link-button"
              onClick={() => void bridge.invoke(appCommands.OpenWindow, { route: "/statistics" }).catch((error: unknown) => actions.toast(messageOf(error), "error"))}
            >
              Open in a window
            </button>
          </p>
        </>
      )}
    </div>
  );
}

function StatTile({ label, value, note, alert }: { label: string; value: string; note?: string; alert?: boolean }) {
  return (
    <div className="stat-tile">
      <span className="stat-label">{label}</span>
      <span className={`stat-value ${alert ? "error" : ""}`}>{value}</span>
      {note && <span className="stat-note">{note}</span>}
    </div>
  );
}

function SortHeader(props: { label: string; value: SortKey; sort: SortKey; onSort: (value: SortKey) => void }) {
  return (
    <th className="number">
      <button className={`sort ${props.sort === props.value ? "on" : ""}`} onClick={() => props.onSort(props.value)}>
        {props.label}
        {props.sort === props.value ? " ↓" : ""}
      </button>
    </th>
  );
}

/** Clicks per day as thin bars rounded at the top, today on the right; a tooltip names the day and the clicks. */
function DayChart({ values }: { values: number[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const width = 300;
  const height = 110;
  const max = Math.max(1, ...values);
  const slot = width / values.length;
  const gap = Math.min(2, slot * 0.3);
  const bar = Math.max(1, slot - gap);
  const radius = Math.min(3, bar / 2);
  const dayOf = (index: number) => {
    const date = new Date();
    date.setDate(date.getDate() - (values.length - 1 - index));
    return date.toLocaleDateString(undefined, { day: "numeric", month: "short" });
  };

  if (values.every((value) => value === 0)) return <p className="muted small">No clicks yet.</p>;

  return (
    <div className="day-chart" onMouseLeave={() => setHover(null)}>
      <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label="Clicks per day">
        <line x1="0" y1={height - 0.5} x2={width} y2={height - 0.5} className="chart-baseline" />
        {values.map((value, index) => {
          const barHeight = value === 0 ? 0 : Math.max(2, (value / max) * (height - 4));
          const x = index * slot + gap / 2;
          const y = height - barHeight;
          return (
            <g key={index}>
              {barHeight > 0 && (
                <path
                  className={`chart-bar ${hover === index ? "on" : ""}`}
                  d={`M${x},${height} V${y + radius} Q${x},${y} ${x + radius},${y} H${x + bar - radius} Q${x + bar},${y} ${x + bar},${y + radius} V${height} Z`}
                />
              )}
              <rect x={index * slot} y="0" width={slot} height={height} fill="transparent" onMouseEnter={() => setHover(index)} />
            </g>
          );
        })}
      </svg>
      <div className="chart-axis">
        <span>{dayOf(0)}</span>
        <span>max {max} a day</span>
        <span>{dayOf(values.length - 1)}</span>
      </div>
      {hover !== null && (
        <div className="chart-tooltip" style={{ left: `${((hover + 0.5) / values.length) * 100}%` }}>
          <strong>{dayOf(hover)}</strong>
          <span>{values[hover]} click(s)</span>
        </div>
      )}
    </div>
  );
}

function ButtonDetails({ row }: { row: ButtonStatistic }) {
  return (
    <div className="stats-details">
      <p className="muted small">
        {row.extensionName} · {row.command ? <span className="mono">{row.command}</span> : "opens a page"}
      </p>
      <dl>
        <dt>Clicks</dt>
        <dd>{row.clicks}</dd>
        <dt>Failed</dt>
        <dd className={row.failures > 0 ? "error" : ""}>{row.failures}</dd>
        <dt>Average run</dt>
        <dd>{duration(row.averageMilliseconds)}</dd>
        <dt>First click</dt>
        <dd>{row.firstClick ? new Date(row.firstClick).toLocaleString() : "—"}</dd>
        <dt>Last click</dt>
        <dd>{row.lastClick ? new Date(row.lastClick).toLocaleString() : "—"}</dd>
      </dl>
      {row.lastError && (
        <>
          <p className="small">Last error{row.lastErrorAt ? ` (${new Date(row.lastErrorAt).toLocaleString()})` : ""}</p>
          <pre className="result error">{row.lastError}</pre>
        </>
      )}
    </div>
  );
}
