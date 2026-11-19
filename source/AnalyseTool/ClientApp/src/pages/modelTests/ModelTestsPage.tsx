import { Badge, Button, Input, Link, MessageBar, MessageBarBody, Spinner, Switch, Text, Tooltip } from "@fluentui/react-components";
import {
  Add16Regular,
  Checkmark16Regular,
  ChevronDown16Regular,
  ChevronRight16Regular,
  Cube16Regular,
  Cursor16Regular,
  Dismiss16Regular,
  Eye16Regular,
  Play16Regular,
  Search16Regular,
  Info16Regular,
  Warning16Regular,
  Wrench16Regular,
  Archive16Regular,
  ArrowRepeatAll16Regular,
  Circle16Regular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { bridge } from "revit-bridge";
import { appCommands } from "../../api/app";
import type { ExtensionSummary } from "../../api/extensions";
import {
  type AcceptModelTestFailuresRequest,
  type CreateModelTestSuiteRequest,
  type FixModelTestCasesRequest,
  type ModelTestBaselineResult,
  type ModelTestCase,
  type ModelTestFixResult,
  type ModelTestInfo,
  type ModelTestList,
  type ModelTestResult,
  type ModelTestRun,
  type ModelTestSuiteInfo,
  type RunModelTestsRequest,
  type TestStatus,
  modelTestCommands,
  modelTestsRanEvent,
} from "../../api/modelTests";
import { useConfirm } from "../../components/confirm";
import { plural, relativeTime } from "../../components/format";
import { errorText, useNotify } from "../../components/notify";
import { Empty, Page } from "../../components/Page";
import { useAppQuery } from "../../revit/hooks";

const WATCH_KEY = "analysetool.modelTests.watch";
const SLOW_KEY = "analysetool.modelTests.slow";
const SHOWN_CASES = 50;

type Selection = { testId: string; key: string | null };

function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === "1";
  } catch {
    return false;
  }
}

function writeFlag(key: string, on: boolean) {
  try {
    localStorage.setItem(key, on ? "1" : "0");
  } catch {
    // Storage blocked: the switch still works for this window
  }
}

/** The results the page shows: a full run replaces them, a partial one updates its tests. */
function useResults() {
  const [state, setState] = useState<{ run: ModelTestRun | null; byTest: Record<string, ModelTestResult> }>({ run: null, byTest: {} });
  const apply = useCallback((run: ModelTestRun | null) => {
    if (!run) return;
    setState((current) => {
      if (current.run?.at === run.at) return current;
      const byTest = run.full || current.run?.modelKey !== run.modelKey ? {} : { ...current.byTest };
      for (const result of run.results) byTest[result.testId] = result;
      return { run, byTest };
    });
  }, []);
  return { ...state, apply };
}

/** The model tests of the extensions in a window of their own. */
export function ModelTestsPage() {
  return <ModelTests layout="page" />;
}

/**
 * The model tests of the extensions: run them on the open model, look at what failed, repair or accept it. A window shows
 * them as a page; the Tests tab of the pane shows the same, its actions in a row instead of beside a large title.
 */
export function ModelTests({ layout, suite: only }: { layout: "page" | "pane"; suite?: string }) {
  const queryClient = useQueryClient();
  const notify = useNotify();
  const confirm = useConfirm();
  const tests = useAppQuery<ModelTestList>(modelTestCommands.GetModelTests);
  const latest = useQuery({
    queryKey: ["modelTestResults"],
    queryFn: ({ signal }) => bridge.invoke<ModelTestRun | null>(modelTestCommands.GetModelTestResults, null, { signal }),
  });
  const { run, byTest, apply } = useResults();
  const [running, setRunning] = useState(false);
  const [watch, setWatch] = useState(() => readFlag(WATCH_KEY));
  const [includeSlow, setIncludeSlow] = useState(() => readFlag(SLOW_KEY));
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [selection, setSelection] = useState<Selection | null>(null);
  const [newSuite, setNewSuite] = useState<string | null>(null);
  const watchTimer = useRef<number | undefined>(undefined);

  useEffect(() => apply(latest.data ?? null), [latest.data, apply]);

  const runTests = useCallback(
    async (request: RunModelTestsRequest = {}) => {
      setRunning(true);
      try {
        apply(await bridge.invoke<ModelTestRun>(modelTestCommands.RunModelTests, { includeSlow, trigger: "page", ...request }));
      } catch (error) {
        notify.failed(error, "The tests did not run");
      } finally {
        setRunning(false);
      }
    },
    [apply, includeSlow, notify],
  );

  // A run from an AI client or another window shows here too; a saved test file reloads the list (and, watching, runs)
  useEffect(() => bridge.on(modelTestsRanEvent, () => void queryClient.invalidateQueries({ queryKey: ["modelTestResults"] })), [queryClient]);
  useEffect(
    () =>
      bridge.on("buttonsChanged", () => {
        void tests.refetch();
        if (!watch) return;
        window.clearTimeout(watchTimer.current);
        watchTimer.current = window.setTimeout(() => void runTests({ trigger: "watch" }), 800);
      }),
    [tests, watch, runTests],
  );

  const suites = tests.data?.suites ?? [];
  // A suite opened in the Tests tab of the pane shows its own tests only
  const folderSuites = useMemo(() => (only ? suites.filter((suite) => suite.id === only) : null), [suites, only]);
  const allTests = useMemo(() => suites.flatMap((suite) => suite.tests.map((test) => ({ suite, test }))), [suites]);
  // Inside a folder the counts, «Run failed» and the list are the folder's
  const scope = only ? allTests.filter((item) => item.suite.id === only) : allTests;
  const filtered = useMemo(() => {
    const text = search.trim().toLowerCase();
    const shown = folderSuites ?? suites;
    if (!text) return shown;
    return shown
      .map((suite) => ({
        ...suite,
        tests: suite.tests.filter((test) =>
          [suite.name, test.name, test.description, ...test.traits].some((part) => part.toLowerCase().includes(text)),
        ),
      }))
      .filter((suite) => suite.tests.length > 0);
  }, [suites, folderSuites, search]);

  const failing = scope.filter(({ test }) => ["failed", "error"].includes(byTest[test.id]?.status ?? "")).map(({ test }) => test.id);
  const selected = selection ? allTests.find(({ test }) => test.id === selection.testId) : undefined;
  const selectedResult = selection ? byTest[selection.testId] : undefined;
  const selectedCase = selection?.key ? selectedResult?.cases.find((item) => item.key === selection.key) : undefined;

  // In the pane the details open under their row (ⓘ), not in a panel far below
  const inline = layout === "pane";
  function toggleSelection(next: Selection) {
    setSelection((current) => (current && current.testId === next.testId && current.key === next.key ? null : next));
  }

  function toggle(id: string) {
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function act<T>(command: string, payload: unknown, success: (data: T) => string | undefined, failure: string) {
    try {
      const data = await bridge.invoke<T>(command, payload);
      const text = success(data);
      if (text) notify.success(text);
      return data;
    } catch (error) {
      notify.failed(error, failure);
      return undefined;
    }
  }

  async function fix(test: ModelTestInfo, keys: string[] | null) {
    const result = await act<ModelTestFixResult>(
      modelTestCommands.FixModelTestCases,
      { testId: test.id, keys } satisfies FixModelTestCasesRequest,
      (data) => `Repaired ${plural(data.fixed, "element")}${data.problems.length ? `, ${plural(data.problems.length, "problem")}` : ""}`,
      "The repair did not run",
    );
    if (result?.problems.length) notify.warning("Not repaired", result.problems.slice(0, 8).join("\n"));
    if (result) await runTests({ tests: [test.id] });
  }

  async function accept(test: ModelTestInfo, keys: string[] | null, remove = false) {
    if (!keys && !remove) {
      const ok = await confirm({
        title: "Accept every failure?",
        message: `The failures of «${test.name}» on this model stop failing it; only new ones will. They are kept in the suite's baseline\\ folder, versioned with the tests.`,
        confirmLabel: "Accept",
      });
      if (!ok) return;
    }

    const result = await act<ModelTestBaselineResult>(
      modelTestCommands.AcceptModelTestFailures,
      { testId: test.id, keys, remove } satisfies AcceptModelTestFailuresRequest,
      (data) => (remove ? `Baseline of «${test.name}»: ${data.accepted} left` : `Accepted — ${plural(data.accepted, "failure")} in the baseline`),
      "The baseline did not change",
    );
    if (result) await runTests({ tests: [test.id] });
  }

  async function show(item: ModelTestCase, how: "select" | "isolate" | "geometry" | "3d") {
    const ids = [item.elementId, ...item.related].filter((id): id is number => id !== null);
    if (how === "select") {
      await act(modelTestCommands.SelectionInRevit, { elementIds: ids }, () => undefined, "Could not select");
    } else if (how === "3d") {
      await act<{ ok: boolean; error: string | null }>(
        modelTestCommands.ShowElementsIn3D,
        { elementIds: ids },
        (result) => (result.ok ? undefined : undefined),
        "Could not show it in 3D",
      ).then((result) => result && !result.ok && notify.warning("Not shown in 3D", result.error ?? undefined));
    } else if (how === "isolate") {
      await act(modelTestCommands.IsolationInRevit, { elementIds: ids }, () => undefined, "Could not isolate");
    } else {
      await bridge.invoke(modelTestCommands.ClearGeometry, null).catch(() => undefined);
      const box = item.box ? [{ min: item.box.slice(0, 3), max: item.box.slice(3, 6) }] : [];
      await act(
        modelTestCommands.ShowGeometry,
        { ids, boxes: box, points: item.point ? [item.point] : [], color: item.status === "failed" ? "#D13438" : "#CA5010" },
        () => undefined,
        "Could not show it",
      );
    }
  }

  async function createSuite() {
    const name = newSuite?.trim();
    if (!name) return;
    const created = await act<ExtensionSummary>(
      modelTestCommands.CreateModelTestSuite,
      { name } satisfies CreateModelTestSuiteRequest,
      () => `Created «${name}»`,
      "The suite was not created",
    );
    if (created) {
      setNewSuite(null);
      await tests.refetch();
    }
  }

  function openFile(suite: ModelTestSuiteInfo, test: ModelTestInfo) {
    const path = /^[a-z]:|^\\\\/i.test(test.file) ? test.file : `${suite.directory}\\${test.file}`;
    void bridge.invoke(appCommands.OpenPath, { path }).catch((error: unknown) => notify.failed(error, "Could not open the file"));
  }

  // The tiles add up every test shown, not only the latest run: running one test keeps the others' results
  const known = scope.map(({ test }) => byTest[test.id]).filter((result): result is ModelTestResult => !!result);
  const totals = known.length
    ? {
        tests: known.length,
        passed: known.filter((result) => result.status === "passed").length,
        failed: known.filter((result) => result.status === "failed").length,
        newCases: known.reduce((sum, result) => sum + result.new, 0),
        baselineCases: known.reduce((sum, result) => sum + result.baseline, 0),
        inconclusiveCases: known.reduce((sum, result) => sum + result.inconclusive, 0),
      }
    : undefined;
  const subtitle =
        run ? (
          <>
            {run.model} · {relativeTime(run.at)} · {(run.milliseconds / 1000).toFixed(1)} s{run.trigger !== "page" ? ` · ${run.trigger}` : ""}
          </>
        ) : (
          "Checks of the open model written as code: rules on parameters, checks per element, tests of the whole model."
        );
  const actions = (
        <>
          <Button appearance="primary" icon={running ? <Spinner size="extra-tiny" /> : <Play16Regular />} disabled={running || !allTests.length} onClick={() => void runTests()}>
            Run all
          </Button>
          <Button icon={<ArrowRepeatAll16Regular />} disabled={running || !failing.length} onClick={() => void runTests({ tests: failing })}>
            Run failed
          </Button>
          <Button icon={<Add16Regular />} onClick={() => setNewSuite(newSuite === null ? "" : null)}>
            New suite
          </Button>
        </>
  );
  const content = (
    <>
      {newSuite !== null && (
        <div className="mt-newsuite">
          <Input autoFocus placeholder="BEP Project X" value={newSuite} onChange={(_, data) => setNewSuite(data.value)} onKeyDown={(event) => event.key === "Enter" && void createSuite()} />
          <Button appearance="primary" disabled={!newSuite.trim()} onClick={() => void createSuite()}>
            Create
          </Button>
          <Text size={200} className="muted">
            A folder in your extensions with one C# suite of examples. Open it in your editor: every save runs again with Watch on.
          </Text>
        </div>
      )}

      {tests.isPending && <Spinner label="Loading the tests…" />}
      {tests.isError && (
        <MessageBar intent="error">
          <MessageBarBody>{errorText(tests.error)}</MessageBarBody>
        </MessageBar>
      )}

      {tests.isSuccess && !suites.length && (
        <Empty>
          No model tests yet. A suite is a C# class implementing <code>IModelTestSuite</code> in any of your extensions — start with
          «New suite», or ask your AI client to write one.
        </Empty>
      )}

      {suites.length > 0 && (
        <>
          <div className="usage-tiles mt-tiles">
            <Tile label="Passed" value={totals ? `${totals.passed} of ${totals.tests}` : "—"} />
            <Tile label="New failures" value={totals ? String(totals.newCases) : "—"} tone={totals?.newCases ? "error" : undefined} detail={totals ? `${plural(totals.failed, "test")} failing` : undefined} />
            <Tile label="Accepted" value={totals ? String(totals.baselineCases) : "—"} detail="in the baseline" />
            <Tile label="Inconclusive" value={totals ? String(totals.inconclusiveCases) : "—"} tone={totals?.inconclusiveCases ? "warning" : undefined} />
            {run && run.trend.length > 1 && <Trend run={run} />}
          </div>

          <div className="usage-toolbar">
            <Input size="small" contentBefore={<Search16Regular />} placeholder="Test, rule or trait" value={search} onChange={(_, data) => setSearch(data.value)} />
            <div className="mt-switches">
              <Switch
                label="Slow tests"
                checked={includeSlow}
                onChange={(_, data) => {
                  setIncludeSlow(data.checked);
                  writeFlag(SLOW_KEY, data.checked);
                }}
              />
              <Tooltip content="Runs the tests again whenever an extension is saved or reloaded" relationship="description">
                <Switch
                  label="Watch"
                  checked={watch}
                  onChange={(_, data) => {
                    setWatch(data.checked);
                    writeFlag(WATCH_KEY, data.checked);
                  }}
                />
              </Tooltip>
            </div>
          </div>

          <div className="mt-layout">
            <div className="mt-tree panel">
              {filtered.map((suite) => (
                <div key={suite.id} className="mt-suite">
                  <div className="mt-suite-head">
                    <Text weight="semibold" className="truncate">
                      {suite.name}
                    </Text>
                    <Text size={200} className="muted truncate">
                      {suite.extensionId}
                    </Text>
                    <span className="spacer" />
                    <Button size="small" appearance="subtle" icon={<Play16Regular />} disabled={running} onClick={() => void runTests({ suite: suite.id })} aria-label={`Run ${suite.name}`} />
                  </div>
                  {suite.tests.map((test) => {
                    const result = byTest[test.id];
                    const open = expanded.has(test.id);
                    return (
                      <div key={test.id}>
                        <div
                          className={`mt-row${selection?.testId === test.id && !selection.key ? " selected" : ""}`}
                          onClick={() => {
                            if (!inline) setSelection({ testId: test.id, key: null });
                            if (result?.cases.length) toggle(test.id);
                          }}
                        >
                          {result?.cases.length ? open ? <ChevronDown16Regular /> : <ChevronRight16Regular /> : <span className="mt-chevron-space" />}
                          <StatusIcon status={result?.status} />
                          <span className="grow truncate">{test.name}</span>
                          <Counts result={result} test={test} />
                          {inline && (
                            <RowButton title="Details" onClick={() => toggleSelection({ testId: test.id, key: null })}>
                              <Info16Regular />
                            </RowButton>
                          )}
                        </div>
                        {inline && selected && selection?.testId === test.id && !selection.key && (
                          <div className="mt-inline">
                            <Details
                              suite={selected.suite}
                              test={selected.test}
                              result={selectedResult}
                              item={undefined}
                              running={running}
                  onRun={() => void runTests({ tests: [selected.test.id] })}
                  onOpen={() => openFile(selected.suite, selected.test)}
                  onShow={(item, how) => void show(item, how)}
                  onFix={(keys) => void fix(selected.test, keys)}
                  onAccept={(keys, remove) => void accept(selected.test, keys, remove)}
                            />
                          </div>
                        )}
                        {open &&
                          result?.cases.slice(0, SHOWN_CASES).map((item) => (
                            <div key={item.key}>
                              <div
                                className={`mt-row mt-case${selection?.key === item.key ? " selected" : ""}`}
                                onClick={() => (inline ? toggleSelection({ testId: test.id, key: item.key }) : setSelection({ testId: test.id, key: item.key }))}
                              >
                                <CaseIcon item={item} />
                                <span className="grow truncate">{item.label ?? item.message}</span>
                                {item.inBaseline && (
                                  <Badge size="small" appearance="tint" color="subtle">
                                    accepted
                                  </Badge>
                                )}
                                <RowButton title="Details" onClick={() => toggleSelection({ testId: test.id, key: item.key })}>
                                  <Info16Regular />
                                </RowButton>
                                <RowButton title="Isolate in Revit and show in 3D" disabled={item.elementId === null} onClick={() => void show(item, "3d")}>
                                  <Cube16Regular />
                                </RowButton>
                              </div>
                              {inline && selected && selection?.key === item.key && (
                                <div className="mt-inline">
                                  <Details
                                    suite={selected.suite}
                                    test={selected.test}
                                    result={selectedResult}
                                    item={selectedCase}
                                    running={running}
                  onRun={() => void runTests({ tests: [selected.test.id] })}
                                    onOpen={() => openFile(selected.suite, selected.test)}
                                    onShow={(item, how) => void show(item, how)}
                                    onFix={(keys) => void fix(selected.test, keys)}
                                    onAccept={(keys, remove) => void accept(selected.test, keys, remove)}
                                  />
                                </div>
                              )}
                            </div>
                          ))}
                        {open && result && result.cases.length > SHOWN_CASES && (
                          <Text size={200} className="muted mt-more">
                            + {result.cases.length - SHOWN_CASES} more{result.truncated ? " (the run listed the first ones only)" : ""}
                          </Text>
                        )}
                      </div>
                    );
                  })}
                </div>
              ))}
            </div>

            {!inline && (
            <div className="mt-details panel">
              {!selected && <Empty>Pick a test or a failing element.</Empty>}
              {selected && (
                <Details
                  suite={selected.suite}
                  test={selected.test}
                  result={selectedResult}
                  item={selectedCase}
                  running={running}
                  onRun={() => void runTests({ tests: [selected.test.id] })}
                  onOpen={() => openFile(selected.suite, selected.test)}
                  onShow={(item, how) => void show(item, how)}
                  onFix={(keys) => void fix(selected.test, keys)}
                  onAccept={(keys, remove) => void accept(selected.test, keys, remove)}
                />
              )}
            </div>
            )}
          </div>
        </>
      )}
    </>
  );

  if (layout === "pane" && only) {
    return (
      <div className="mt-pane">
        <div className="mt-pane-actions">
          <Button appearance="primary" icon={running ? <Spinner size="extra-tiny" /> : <Play16Regular />} disabled={running || !scope.length} onClick={() => void runTests({ suite: only })}>
            Run suite
          </Button>
          <Button icon={<ArrowRepeatAll16Regular />} disabled={running || !failing.length} onClick={() => void runTests({ tests: failing })}>
            Run failed
          </Button>
        </div>
        {run && (
          <Text size={200} className="muted mt-pane-subtitle">
            {subtitle}
          </Text>
        )}
        {content}
      </div>
    );
  }

  if (layout === "pane") {
    return (
      <div className="mt-pane">
        <div className="mt-pane-actions">{actions}</div>
        <Text size={200} className="muted mt-pane-subtitle">
          {subtitle}
        </Text>
        {content}
      </div>
    );
  }

  return (
    <Page title="Model tests" subtitle={subtitle} actions={actions}>
      {content}
    </Page>
  );
}

function Details(props: {
  suite: ModelTestSuiteInfo;
  test: ModelTestInfo;
  result: ModelTestResult | undefined;
  item: ModelTestCase | undefined;
  running: boolean;
  onRun: () => void;
  onOpen: () => void;
  onShow: (item: ModelTestCase, how: "select" | "isolate" | "geometry" | "3d") => void;
  onFix: (keys: string[] | null) => void;
  onAccept: (keys: string[] | null, remove?: boolean) => void;
}) {
  const { suite, test, result, item } = props;
  return (
    <div className="mt-detail">
      <Text size={200} className="muted">
        {suite.name} · {test.kind} · {test.severity}
        {test.slow ? " · slow" : ""}
      </Text>
      <Text as="h3" size={400} weight="semibold" className="mt-detail-title">
        {item ? item.label ?? "Whole model" : test.name}
      </Text>
      {item ? (
        <div className={`mt-message ${item.status}`}>{item.message}</div>
      ) : (
        <Text className="muted">{test.description}</Text>
      )}

      {test.file && (
        <Link className="mono small" onClick={props.onOpen}>
          {test.file}
          {test.line ? `:${test.line}` : ""}
        </Link>
      )}
      {test.traits.length > 0 && (
        <div className="mt-traits">
          {test.traits.map((trait) => (
            <Badge key={trait} size="small" appearance="outline">
              {trait}
            </Badge>
          ))}
        </div>
      )}
      {test.skip && <Text className="muted">Skipped: {test.skip}</Text>}

      {item ? (
        <>
          <div className="mt-actions">
            <Button size="small" icon={<Cursor16Regular />} disabled={item.elementId === null} onClick={() => props.onShow(item, "select")}>
              Select
            </Button>
            <Button size="small" icon={<Eye16Regular />} disabled={item.elementId === null} onClick={() => props.onShow(item, "3d")}>
              Isolate in 3D
            </Button>
            <Button size="small" icon={<Cube16Regular />} onClick={() => props.onShow(item, "geometry")}>
              Show
            </Button>
          </div>
          <div className="mt-actions">
            {test.fix && item.status === "failed" && (
              <Button size="small" appearance="primary" icon={<Wrench16Regular />} onClick={() => props.onFix([item.key])}>
                {test.fix}
              </Button>
            )}
            {item.status === "failed" &&
              (item.inBaseline ? (
                <Button size="small" icon={<Dismiss16Regular />} onClick={() => props.onAccept([item.key], true)}>
                  Take out of baseline
                </Button>
              ) : (
                <Button size="small" icon={<Archive16Regular />} onClick={() => props.onAccept([item.key])}>
                  Accept
                </Button>
              ))}
          </div>
        </>
      ) : (
        <>
          {result && (
            <Text size={200} className="muted">
              {result.status === "skipped"
                ? result.skip
                : `${plural(result.checked, "element")} checked · ${result.new} new · ${result.baseline} accepted${result.resolved ? ` · ${result.resolved} accepted no longer fail` : ""} · ${result.milliseconds} ms`}
            </Text>
          )}
          <div className="mt-actions">
            <Button size="small" icon={<Play16Regular />} disabled={props.running} onClick={props.onRun}>
              Run this test
            </Button>
            {test.fix && !!result?.new && (
              <Button size="small" appearance="primary" icon={<Wrench16Regular />} onClick={() => props.onFix(null)}>
                {test.fix}: all {result.new}
              </Button>
            )}
            {!!result?.new && (
              <Button size="small" icon={<Archive16Regular />} onClick={() => props.onAccept(null)}>
                Accept all
              </Button>
            )}
            {!!result?.baseline && (
              <Button size="small" icon={<Dismiss16Regular />} onClick={() => props.onAccept(null, true)}>
                Clear baseline
              </Button>
            )}
          </div>
        </>
      )}
    </div>
  );
}

/** A small icon button at the end of a row: its click stays its own (it does not open or close the row). */
function RowButton({ title, onClick, disabled, children }: { title: string; onClick: () => void; disabled?: boolean; children: ReactNode }) {
  return (
    <Tooltip content={title} relationship="label">
      <button
        className="mt-row-button"
        disabled={disabled}
        onClick={(event) => {
          event.stopPropagation();
          onClick();
        }}
      >
        {children}
      </button>
    </Tooltip>
  );
}

function StatusIcon({ status }: { status: TestStatus | undefined }) {
  switch (status) {
    case "passed":
      return <Checkmark16Regular className="added-text" />;
    case "failed":
      return <Dismiss16Regular className="error-text" />;
    case "error":
      return <Warning16Regular className="error-text" />;
    case "inconclusive":
      return <Warning16Regular className="warning-text" />;
    default:
      return <Circle16Regular className="muted" />;
  }
}

function CaseIcon({ item }: { item: ModelTestCase }) {
  if (item.inBaseline) return <Archive16Regular className="muted" />;
  if (item.status === "inconclusive") return <Warning16Regular className="warning-text" />;
  if (item.status === "error") return <Warning16Regular className="error-text" />;
  return <Dismiss16Regular className="error-text" />;
}

function Counts({ result, test }: { result: ModelTestResult | undefined; test: ModelTestInfo }) {
  if (!result) return <span className="mt-count muted">{test.skip ? "skip" : test.slow ? "slow" : ""}</span>;
  if (result.status === "skipped") return <span className="mt-count muted">skipped</span>;
  const parts = [];
  if (result.new) parts.push(<span key="n" className="error-text">{result.new} new</span>);
  if (result.errors) parts.push(<span key="e" className="error-text">{result.errors} errors</span>);
  if (result.inconclusive) parts.push(<span key="i" className="warning-text">{result.inconclusive} ?</span>);
  if (result.baseline) parts.push(<span key="b" className="muted">{result.baseline} accepted</span>);
  if (!parts.length) parts.push(<span key="ok" className="muted">{result.checked} ok</span>);
  return <span className="mt-count">{parts}</span>;
}

function Tile({ label, value, detail, tone }: { label: string; value: string; detail?: string; tone?: "error" | "warning" }) {
  return (
    <div className="usage-tile">
      <Text size={200} className="muted">
        {label}
      </Text>
      <Text size={500} weight="semibold" className={tone === "error" ? "error-text" : tone === "warning" ? "warning-text" : undefined}>
        {value}
      </Text>
      {detail && <Text size={200}>{detail}</Text>}
    </div>
  );
}

/** New failures over the latest full runs of this model. */
function Trend({ run }: { run: ModelTestRun }) {
  const points = run.trend.slice(-12);
  const most = Math.max(1, ...points.map((point) => point.newCases));
  return (
    <div className="usage-tile">
      <Text size={200} className="muted">
        New failures, last {points.length} runs
      </Text>
      <div className="mt-trend">
        {points.map((point) => (
          <Tooltip key={point.at} content={`${new Date(point.at).toLocaleString()}: ${point.newCases} new, ${point.baselineCases} accepted`} relationship="label">
            <span className="mt-trend-bar" style={{ height: `${Math.max(4, (point.newCases / most) * 100)}%` }} />
          </Tooltip>
        ))}
      </div>
    </div>
  );
}
