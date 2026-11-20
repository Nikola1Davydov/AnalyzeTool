import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { bridge } from "revit-bridge";
import { type ModelTestStatistic, type ModelTestStatisticsResult, modelTestCommands } from "../../api/modelTests";
import { useActions } from "./Actions";
import { DayChart, StatTile } from "./StatsScreen";
import { TileIcon } from "./Tile";

type SortKey = "runs" | "failed" | "errors" | "average";

const duration = (milliseconds: number | null) =>
  milliseconds === null ? "—" : milliseconds < 1000 ? `${Math.round(milliseconds)} ms` : `${(milliseconds / 1000).toFixed(1)} s`;

const statusText: Record<string, string> = {
  passed: "passed",
  failed: "failed",
  error: "error — a bug in the test",
  inconclusive: "inconclusive",
};

/** Runs of a test in the period: the last days of its 30, or all of them. */
function runsIn(test: ModelTestStatistic, days: number) {
  return days === 0 ? test.runs : test.last30Days.slice(-days).reduce((sum, value) => sum + value, 0);
}

/**
 * The model tests tab of the statistics in the pane: how often each test ran, how often it found new failures, how often
 * it threw (a bug in the test, not the model), how long it takes.
 */
export function TestStats({ days }: { days: number }) {
  const [sort, setSort] = useState<SortKey>("runs");
  const actions = useActions();
  const stats = useQuery({
    queryKey: ["modelTestStatistics"],
    queryFn: ({ signal }) => bridge.invoke<ModelTestStatisticsResult>(modelTestCommands.GetModelTestStatistics, null, { signal }),
  });

  const all = stats.data?.tests ?? [];
  const rows = useMemo(() => {
    const by: Record<SortKey, (row: ModelTestStatistic) => number> = {
      runs: (row) => runsIn(row, days),
      failed: (row) => row.failed,
      errors: (row) => row.errors,
      average: (row) => row.averageMilliseconds ?? -1,
    };
    return all.filter((row) => runsIn(row, days) > 0).sort((left, right) => by[sort](right) - by[sort](left));
  }, [all, days, sort]);

  if (stats.isPending) return <p className="muted small">Loading…</p>;
  if (stats.isError) return <p className="error small">{stats.error.message}</p>;

  const runs = all.reduce((sum, test) => sum + runsIn(test, days), 0);
  const failingNow = all.filter((test) => test.exists && test.lastStatus === "failed").length;
  const broken = all.filter((test) => test.exists && test.lastStatus === "error").length;
  const timed = all.filter((test) => test.averageMilliseconds !== null);
  const average = timed.length ? timed.reduce((sum, test) => sum + test.averageMilliseconds!, 0) / timed.length : null;
  const perDay = Array.from({ length: days === 7 ? 7 : 30 }, (_, index) => {
    const offset = (days === 7 ? 23 : 0) + index;
    return all.reduce((sum, test) => sum + (test.last30Days[offset] ?? 0), 0);
  });

  return (
    <>
      <div className="stat-tiles">
        <StatTile label="Test runs" value={runs.toLocaleString()} />
        <StatTile label="Failing now" value={String(failingNow)} note="last run" alert={failingNow > 0} />
        <StatTile label="Broken tests" value={String(broken)} note="threw" alert={broken > 0} />
        <StatTile label="Usual run" value={duration(average)} note="per test" />
      </div>

      <section className="stats-card">
        <h2>Test runs per day</h2>
        <DayChart values={perDay} unit="test run(s)" empty="No test runs yet." />
      </section>

      <section className="stats-card">
        <h2>Tests</h2>
        {rows.length === 0 ? (
          <p className="muted small">No test ran in this period.</p>
        ) : (
          <table className="stats-table">
            <thead>
              <tr>
                <th>Test</th>
                <Sort label="Runs" value="runs" sort={sort} onSort={setSort} />
                <Sort label="Failed" value="failed" sort={sort} onSort={setSort} />
                <Sort label="Errors" value="errors" sort={sort} onSort={setSort} />
                <Sort label="Time" value="average" sort={sort} onSort={setSort} />
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.testId} onClick={() => actions.openSheet(row.test, <TestDetails row={row} />)}>
                  <td>
                    <span className="stats-name">
                      <TileIcon title={row.suite || row.test} />
                      <span>
                        {row.test}
                        {!row.exists && <span className="muted"> (gone)</span>}
                        <span className="muted small stats-project">
                          {row.folder} · {row.suite}
                        </span>
                      </span>
                    </span>
                  </td>
                  <td className="number">{runsIn(row, days)}</td>
                  <td className={`number ${row.failed > 0 ? "error" : "muted"}`}>{row.failed}</td>
                  <td className={`number ${row.errors > 0 ? "error" : "muted"}`}>{row.errors}</td>
                  <td className="number">{duration(row.averageMilliseconds)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <p className="muted small center">
        Every run counts — from the pane, Watch or an AI client — on this computer
        {stats.data.since ? ` since ${new Date(stats.data.since).toLocaleDateString()}` : ""}.
      </p>
    </>
  );
}

function Sort(props: { label: string; value: SortKey; sort: SortKey; onSort: (value: SortKey) => void }) {
  return (
    <th className="number">
      <button className={`sort ${props.sort === props.value ? "on" : ""}`} onClick={() => props.onSort(props.value)}>
        {props.label}
        {props.sort === props.value ? " ↓" : ""}
      </button>
    </th>
  );
}

function TestDetails({ row }: { row: ModelTestStatistic }) {
  return (
    <div className="stats-details">
      <p className="muted small">
        {row.folder} · {row.suite}
      </p>
      <dl>
        <dt>Runs</dt>
        <dd>{row.runs}</dd>
        <dt>With new failures</dt>
        <dd className={row.failed > 0 ? "error" : ""}>{row.failed}</dd>
        <dt>Threw (bug in the test)</dt>
        <dd className={row.errors > 0 ? "error" : ""}>{row.errors}</dd>
        <dt>Inconclusive</dt>
        <dd>{row.inconclusive}</dd>
        <dt>Last run</dt>
        <dd>
          {row.lastRun ? new Date(row.lastRun).toLocaleString() : "—"}
          {row.lastStatus ? ` · ${statusText[row.lastStatus] ?? row.lastStatus}` : ""}
          {row.lastNew ? ` · ${row.lastNew} new` : ""}
        </dd>
        <dt>Average run</dt>
        <dd>{duration(row.averageMilliseconds)}</dd>
      </dl>
      {row.lastError && (
        <>
          <p className="small">Last error</p>
          <pre className="result error">{row.lastError}</pre>
        </>
      )}
    </div>
  );
}
