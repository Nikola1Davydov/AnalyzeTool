import {
  Badge,
  Button,
  Input,
  Link,
  MessageBar,
  MessageBarBody,
  Spinner,
  Tab,
  TabList,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import { ArrowClockwise16Regular, Delete16Regular, Search16Regular } from "@fluentui/react-icons";
import { useMemo, useState } from "react";
import {
  type ButtonStatistic,
  type ButtonStatisticsResult,
  type ResetButtonStatisticsRequest,
  type ResetButtonStatisticsResult,
  statisticsCommands,
} from "../../api/statistics";
import { useConfirm } from "../../components/confirm";
import { fullTime, plural, relativeTime } from "../../components/format";
import { errorText } from "../../components/notify";
import { Empty, Page } from "../../components/Page";
import { useAppAction } from "../../components/useAppAction";
import { useAppQuery } from "../../revit/hooks";
import { href } from "../../router";

type Filter = "all" | "builtIn" | "extensions" | "unused";
type SortKey = "button" | "clicks" | "lastClick" | "average" | "failures";

const sorters: Record<SortKey, (a: ButtonStatistic, b: ButtonStatistic) => number> = {
  button: (a, b) => a.button.localeCompare(b.button),
  clicks: (a, b) => a.clicks - b.clicks,
  lastClick: (a, b) => (a.lastClick ?? "").localeCompare(b.lastClick ?? ""),
  average: (a, b) => (a.averageMilliseconds ?? -1) - (b.averageMilliseconds ?? -1),
  failures: (a, b) => a.failures - b.failures,
};

/** How every ribbon button is used — AnalyseTool's own and the extensions' alike. */
export function StatisticsPage() {
  const query = useAppQuery<ButtonStatisticsResult>(statisticsCommands.GetButtonStatistics);
  const confirm = useConfirm();
  const reset = useAppAction<ResetButtonStatisticsResult, ResetButtonStatisticsRequest>(statisticsCommands.ResetButtonStatistics, {
    success: (result) => (result.removed ? `Forgot the counts of ${plural(result.removed, "button")}` : "Nothing to forget"),
    failure: "Could not reset",
  });
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({ key: "clicks", descending: true });

  const buttons = query.data?.buttons ?? [];
  const most = Math.max(1, ...buttons.map((button) => button.clicks));
  const rows = useMemo(() => {
    const text = search.trim().toLowerCase();
    const filtered = buttons.filter((button) => {
      if (filter === "builtIn" && !button.builtIn) return false;
      if (filter === "extensions" && button.builtIn) return false;
      if (filter === "unused" && button.clicks > 0) return false;
      return (
        !text ||
        button.button.toLowerCase().includes(text) ||
        button.extension.toLowerCase().includes(text) ||
        button.extensionName.toLowerCase().includes(text) ||
        (button.command ?? "").toLowerCase().includes(text)
      );
    });
    const compare = sorters[sort.key];
    return [...filtered].sort((a, b) => (sort.descending ? -compare(a, b) : compare(a, b)) || b.clicks - a.clicks);
  }, [buttons, filter, search, sort]);

  const used = buttons.filter((button) => button.clicks > 0);
  const failures = buttons.reduce((sum, button) => sum + button.failures, 0);
  const top = used[0];

  function header(key: SortKey, label: string, className?: string) {
    const active = sort.key === key;
    return (
      <TableHeaderCell
        className={className}
        sortDirection={active ? (sort.descending ? "descending" : "ascending") : undefined}
        onClick={() => setSort({ key, descending: active ? !sort.descending : key !== "button" })}
      >
        {label}
      </TableHeaderCell>
    );
  }

  async function resetAll() {
    const ok = await confirm({
      title: "Forget all counts?",
      message: "Every button starts again from zero. The buttons themselves stay as they are.",
      confirmLabel: "Forget",
      danger: true,
    });
    if (ok) reset.mutate({});
  }

  return (
    <Page
      title="Statistics"
      subtitle={
        <>
          How often every button is clicked — AnalyseTool&apos;s own and your extensions&apos;. Counted on this machine only
          {query.data?.since ? <>, since {new Date(query.data.since).toLocaleDateString()}.</> : "."}
        </>
      }
      actions={
        <>
          <Button icon={<ArrowClockwise16Regular />} disabled={query.isFetching} onClick={() => void query.refetch()}>
            Refresh
          </Button>
          <Button icon={<Delete16Regular />} disabled={reset.isPending || !used.length} onClick={() => void resetAll()}>
            Reset…
          </Button>
        </>
      }
    >
      {query.isPending && <Spinner label="Counting…" />}
      {query.isError && (
        <MessageBar intent="error">
          <MessageBarBody>{errorText(query.error)}</MessageBarBody>
        </MessageBar>
      )}

      {query.isSuccess && (
        <>
          <div className="usage-tiles">
            <StatTile label="Clicks" value={query.data.totalClicks.toLocaleString()} />
            <StatTile label="Buttons used" value={`${used.length} of ${buttons.length}`} />
            <StatTile label="Most used" value={top ? top.button : "—"} detail={top ? plural(top.clicks, "click") : undefined} />
            <StatTile label="Failed runs" value={failures.toLocaleString()} warning={failures > 0} />
          </div>

          <div className="usage-toolbar">
            <TabList size="small" selectedValue={filter} onTabSelect={(_, data) => setFilter(data.value as Filter)}>
              <Tab value="all">All</Tab>
              <Tab value="builtIn">AnalyseTool</Tab>
              <Tab value="extensions">Extensions</Tab>
              <Tab value="unused">Never used</Tab>
            </TabList>
            <Input
              size="small"
              contentBefore={<Search16Regular />}
              placeholder="Button, extension or command"
              value={search}
              onChange={(_, data) => setSearch(data.value)}
            />
          </div>

          {!rows.length ? (
            <Empty>{buttons.length ? "No button matches." : "No buttons yet."}</Empty>
          ) : (
            <div className="table-scroll">
              <Table size="small" sortable className="usage-table">
                <TableHeader>
                  <TableRow>
                    {header("button", "Button")}
                    {header("clicks", "Clicks", "col-clicks")}
                    <TableHeaderCell className="col-days">Last 30 days</TableHeaderCell>
                    {header("lastClick", "Last used", "col-time")}
                    {header("average", "Avg. run", "col-number")}
                    {header("failures", "Failed", "col-number")}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((button) => (
                    <StatRow key={`${button.extension}/${button.button}`} button={button} most={most} />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </>
      )}
    </Page>
  );
}

function StatTile({ label, value, detail, warning }: { label: string; value: string; detail?: string; warning?: boolean }) {
  return (
    <div className="usage-tile">
      <Text size={200} className="muted">
        {label}
      </Text>
      <Text size={500} weight="semibold" className={warning ? "warning-text" : undefined} truncate wrap={false}>
        {value}
      </Text>
      {detail && <Text size={200}>{detail}</Text>}
    </div>
  );
}

function StatRow({ button, most }: { button: ButtonStatistic; most: number }) {
  const source = button.builtIn ? (
    <span>AnalyseTool</span>
  ) : (
    <Link href={href(`/extensions/${encodeURIComponent(button.extension)}`)}>{button.extensionName}</Link>
  );
  return (
    <TableRow className={button.onRibbon ? undefined : "usage-gone"}>
      <TableCell>
        <div className="cell-stack">
          <span className="strong">
            {button.button}{" "}
            {!button.onRibbon && (
              <Badge size="small" appearance="tint" color="subtle">
                not on the ribbon
              </Badge>
            )}
          </span>
          <span className="muted small">
            {source} · {button.command ? <span className="mono">{button.command}</span> : "opens a page"}
          </span>
        </div>
      </TableCell>
      <TableCell>
        <div className="usage-clicks">
          <span className="usage-clicks-number">{button.clicks}</span>
          <span className="usage-bar">
            <span style={{ width: `${(button.clicks / most) * 100}%` }} />
          </span>
        </div>
      </TableCell>
      <TableCell>
        <Sparkline days={button.last30Days} />
      </TableCell>
      <TableCell>
        {button.lastClick ? (
          <Tooltip content={fullTime(button.lastClick)} relationship="description">
            <span>{relativeTime(button.lastClick)}</span>
          </Tooltip>
        ) : (
          <span className="muted">never</span>
        )}
      </TableCell>
      <TableCell>{button.averageMilliseconds === null ? <span className="muted">—</span> : duration(button.averageMilliseconds)}</TableCell>
      <TableCell>
        {button.failures > 0 ? (
          <Tooltip
            content={`${button.lastError ?? "Failed"}${button.lastErrorAt ? ` (${fullTime(button.lastErrorAt)})` : ""}`}
            relationship="description"
          >
            <span className="error-text">{button.failures}</span>
          </Tooltip>
        ) : (
          <span className="muted">0</span>
        )}
      </TableCell>
    </TableRow>
  );
}

/** 30 small bars, today on the right. */
function Sparkline({ days }: { days: number[] }) {
  const max = Math.max(1, ...days);
  const total = days.reduce((sum, value) => sum + value, 0);
  const width = 3;
  const gap = 1;
  const height = 20;
  return (
    <Tooltip content={`${plural(total, "click")} in the last 30 days`} relationship="description">
      <svg className="sparkline" width={days.length * (width + gap)} height={height} role="img" aria-label={`${total} clicks in 30 days`}>
        {days.map((value, index) => {
          const barHeight = value ? Math.max(2, (value / max) * height) : 1;
          return (
            <rect
              key={index}
              x={index * (width + gap)}
              y={height - barHeight}
              width={width}
              height={barHeight}
              rx={1}
              className={value ? "sparkline-bar" : "sparkline-empty"}
            />
          );
        })}
      </svg>
    </Tooltip>
  );
}

function duration(milliseconds: number) {
  if (milliseconds < 1000) return `${Math.round(milliseconds)} ms`;
  if (milliseconds < 60_000) return `${(milliseconds / 1000).toFixed(1)} s`;
  return `${(milliseconds / 60_000).toFixed(1)} min`;
}
