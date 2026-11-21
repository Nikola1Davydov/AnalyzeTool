import { Badge, Button, Input, Link, MessageBar, MessageBarBody, Spinner, Text, Tooltip } from "@fluentui/react-components";
import {
  Add16Regular,
  Checkmark16Regular,
  ChevronDown16Regular,
  ChevronRight16Regular,
  Cursor16Regular,
  Dismiss16Regular,
  Eye16Regular,
  Play16Regular,
  Search16Regular,
  Info16Regular,
  ScanDash16Regular,
  Broom16Regular,
  Highlight16Regular,
  Warning16Regular,
  Wrench16Regular,
  Archive16Regular,
  ArrowRepeatAll16Regular,
  Circle16Regular,
} from "@fluentui/react-icons";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";
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
  extensionsChangedEvent,
} from "../../api/modelTests";
import { useConfirm } from "../../components/confirm";
import { plural, relativeTime } from "../../components/format";
import { errorText, useNotify } from "../../components/notify";
import { Empty, Page } from "../../components/Page";
import { RunProgress, useRunProgress } from "./RunProgress";
import { useAppQuery } from "../../revit/hooks";

const SHOWN_CASES = 50;

type Selection = { testId: string; key: string | null };

/** What a chip of the strip narrows the list to. */
type StatusFilter = "passed" | "new" | "accepted" | "inconclusive";

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
  const refetchTests = tests.refetch;
  const latest = useQuery({
    queryKey: ["modelTestResults"],
    queryFn: ({ signal }) => bridge.invoke<ModelTestRun | null>(modelTestCommands.GetModelTestResults, null, { signal }),
  });
  const { run, byTest, apply } = useResults();
  const progress = useRunProgress();
  const [running, setRunning] = useState(false);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [selection, setSelection] = useState<Selection | null>(null);
  const [newSuite, setNewSuite] = useState<string | null>(null);

  useEffect(() => apply(latest.data ?? null), [latest.data, apply]);

  const runTests = useCallback(
    async (request: RunModelTestsRequest = {}) => {
      setRunning(true);
      try {
        // Run means every test, the slow ones too: what the page shows is never half a run
        apply(await bridge.invoke<ModelTestRun>(modelTestCommands.RunModelTests, { includeSlow: true, trigger: "page", ...request }));
      } catch (error) {
        notify.failed(error, "The tests did not run");
      } finally {
        setRunning(false);
      }
    },
    [apply, notify],
  );

  // A run from an AI client or another window shows here too; a saved test file reloads the list
  useEffect(() => bridge.on(modelTestsRanEvent, () => void queryClient.invalidateQueries({ queryKey: ["modelTestResults"] })), [queryClient]);
  // Only a reload of the extensions (a saved test file): buttonsChanged also follows every click on a button
  useEffect(() => bridge.on(extensionsChangedEvent, () => void refetchTests()), [refetchTests]);

  const suites = tests.data?.suites ?? [];
  // A suite opened in the Tests tab of the pane shows its own tests only
  const folderSuites = useMemo(() => (only ? suites.filter((suite) => suite.id === only) : null), [suites, only]);
  const allTests = useMemo(() => suites.flatMap((suite) => suite.tests.map((test) => ({ suite, test }))), [suites]);
  // Inside a folder the counts, «Run failed» and the list are the folder's
  const scope = only ? allTests.filter((item) => item.suite.id === only) : allTests;
  const filtered = useMemo(() => {
    const text = search.trim().toLowerCase();
    const shown = folderSuites ?? suites;
    if (!text && !statusFilter) return shown;
    const byStatus = (test: ModelTestInfo) => {
      if (!statusFilter) return true;
      const result = byTest[test.id];
      switch (statusFilter) {
        case "passed":
          return result?.status === "passed";
        case "new":
          return (result?.new ?? 0) > 0;
        case "accepted":
          return (result?.baseline ?? 0) > 0;
        case "inconclusive":
          return (result?.inconclusive ?? 0) > 0;
      }
    };
    return shown
      .map((suite) => ({
        ...suite,
        tests: suite.tests.filter(
          (test) => byStatus(test) && (!text || [suite.name, test.name, test.description, ...test.traits].some((part) => part.toLowerCase().includes(text))),
        ),
      }))
      .filter((suite) => suite.tests.length > 0);
  }, [suites, folderSuites, search, statusFilter, byTest]);
  const toggleFilter = (filter: StatusFilter) => setStatusFilter((current) => (current === filter ? null : filter));

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

  async function show(item: Shown, how: "select" | "isolate" | "geometry" | "3d" | "box") {
    const ids = [item.elementId, ...item.related].filter((id): id is number => id !== null);
    if (how === "select") {
      await act(modelTestCommands.SelectionInRevit, { elementIds: ids }, () => undefined, "Could not select");
    } else if (how === "3d" || how === "box") {
      await act<{ ok: boolean; error: string | null }>(
        modelTestCommands.ShowElementsIn3D,
        { elementIds: ids, sectionBox: how === "box" },
        () => undefined,
        how === "box" ? "Could not show it in 3D" : "Could not isolate",
      ).then(
        (result) =>
          result?.error &&
          notify.warning(how === "box" ? "Not shown in 3D" : result.ok ? "Isolated in part" : "Not isolated", result.error),
      );
    } else if (how === "isolate") {
      await act(modelTestCommands.IsolationInRevit, { elementIds: ids }, () => undefined, "Could not isolate");
    } else {
      await bridge.invoke(modelTestCommands.ClearGeometry, null).catch(() => undefined);
      // Highlight shows where the problem is: the place the test found (a clash, a gap) when it gave one, else the elements
      const red = item.status === "failed" ? "#D13438" : "#CA5010";
      const boxes = (item.boxes ?? (item.box ? [item.box] : [])).map((box) => ({ min: box.slice(0, 3), max: box.slice(3, 6) }));
      const points = item.points ?? (item.point ? [item.point] : []);
      let shown: { hidden?: string | null } | undefined;
      if (boxes.length || points.length) {
        shown = await act<{ hidden?: string | null }>(modelTestCommands.ShowGeometry, { boxes, points, color: red, transparency: 15 }, () => undefined, "Could not show it");
      } else {
        shown = await act<{ hidden?: string | null }>(modelTestCommands.ShowGeometry, { ids, color: red }, () => undefined, "Could not show it");
      }
      // The Realistic display style draws none of the highlight: say so, or the button looks broken
      if (shown?.hidden) notify.warning("Not visible in this view", shown.hidden);
    }
  }

  // What «Highlight» drew over the views stays until it is cleared: one button for all of it
  async function clearShown() {
    await act(modelTestCommands.ClearGeometry, null, () => "Highlights cleared", "Could not clear the highlights");
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
          <Tooltip content="Removes the red highlights «Highlight» drew over the views" relationship="description">
            <Button icon={<Broom16Regular />} onClick={() => void clearShown()}>
              Clear highlights
            </Button>
          </Tooltip>
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
            A folder in your extensions with one C# suite of examples. Open it in your editor or ask your AI client; Run checks the model again.
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
          {progress && <RunProgress progress={progress} />}
          {layout === "pane" ? (
            <div className="mt-strip">
              <FilterChip active={statusFilter === "passed"} title="Show only the tests that passed" onToggle={() => toggleFilter("passed")}>
                <b>{totals ? `${totals.passed}/${totals.tests}` : "—"}</b> passed
              </FilterChip>
              <FilterChip active={statusFilter === "new"} tone={totals?.newCases ? "error" : undefined} title="Show only the tests with new failures" onToggle={() => toggleFilter("new")}>
                <b>{totals ? totals.newCases : "—"}</b> new
              </FilterChip>
              <FilterChip active={statusFilter === "accepted"} title="Show only the tests with accepted failures" onToggle={() => toggleFilter("accepted")}>
                <b>{totals ? totals.baselineCases : "—"}</b> accepted
              </FilterChip>
              {!!totals?.inconclusiveCases && (
                <FilterChip active={statusFilter === "inconclusive"} tone="warning" title="Show only the tests Revit could not tell" onToggle={() => toggleFilter("inconclusive")}>
                  <b>{totals.inconclusiveCases}</b> ?
                </FilterChip>
              )}
              {run && run.trend.length > 1 && <Sparkline run={run} />}
            </div>
          ) : (
          <div className="usage-tiles mt-tiles">
            <Tile label="Passed" value={totals ? `${totals.passed} of ${totals.tests}` : "—"} />
            <Tile label="New failures" value={totals ? String(totals.newCases) : "—"} tone={totals?.newCases ? "error" : undefined} detail={totals ? `${plural(totals.failed, "test")} failing` : undefined} />
            <Tile label="Accepted" value={totals ? String(totals.baselineCases) : "—"} detail="in the baseline" />
            <Tile label="Inconclusive" value={totals ? String(totals.inconclusiveCases) : "—"} tone={totals?.inconclusiveCases ? "warning" : undefined} />
            {run && run.trend.length > 1 && <Trend run={run} />}
          </div>
          )}

          <div className="usage-toolbar">
            <Input size="small" contentBefore={<Search16Regular />} placeholder="Test, rule or trait" value={search} onChange={(_, data) => setSearch(data.value)} />
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
                          {groupOf(result) && (
                            <>
                              <RowButton title="Isolate every failing element in the active view" onClick={() => void show(groupOf(result)!, "3d")}>
                                <Eye16Regular />
                              </RowButton>
                              <RowButton title="Selection box around every failing element" onClick={() => void show(groupOf(result)!, "box")}>
                                <ScanDash16Regular />
                              </RowButton>
                            </>
                          )}
                        </div>
                        {inline && selected && selection?.testId === test.id && !selection.key && (
                          <div className="mt-inline">
                            <Details
                              inline
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
                                <RowButton title="Isolate it in the active view" disabled={item.elementId === null} onClick={() => void show(item, "3d")}>
                                  <Eye16Regular />
                                </RowButton>
                                <RowButton title="Selection box: a section box around it in 3D, its surroundings visible" disabled={item.elementId === null} onClick={() => void show(item, "box")}>
                                  <ScanDash16Regular />
                                </RowButton>
                              </div>
                              {inline && selected && selection?.key === item.key && (
                                <div className="mt-inline">
                                  <Details
                                    inline
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
          <Tooltip content="Removes the red highlights «Highlight» drew over the views" relationship="description">
            <Button icon={<Broom16Regular />} onClick={() => void clearShown()}>
              Clear highlights
            </Button>
          </Tooltip>
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
  /** Under its row in the pane: the row says the name already. */
  inline?: boolean;
  suite: ModelTestSuiteInfo;
  test: ModelTestInfo;
  result: ModelTestResult | undefined;
  item: ModelTestCase | undefined;
  running: boolean;
  onRun: () => void;
  onOpen: () => void;
  onShow: (item: ModelTestCase, how: "select" | "isolate" | "geometry" | "3d" | "box") => void;
  onFix: (keys: string[] | null) => void;
  onAccept: (keys: string[] | null, remove?: boolean) => void;
}) {
  const { suite, test, result, item } = props;
  return (
    <div className="mt-detail">
      {!props.inline && (
        <>
          <Text size={200} className="muted">
            {suite.name} · {test.kind} · {test.severity}
            {test.slow ? " · slow" : ""}
          </Text>
          <Text as="h3" size={400} weight="semibold" className="mt-detail-title">
            {item ? item.label ?? "Whole model" : test.name}
          </Text>
        </>
      )}
      {item && <div className={`mt-message ${item.status}`}>{item.message}</div>}
      <WhatItChecks test={test} item={item} />

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
              Isolate
            </Button>
            <Button size="small" icon={<ScanDash16Regular />} disabled={item.elementId === null} onClick={() => props.onShow(item, "box")}>
              Selection box
            </Button>
            {(test.kind !== "Rule" || item.box || item.point) && (
              <Tooltip content="Draws where it fails in red over the 3D views and plans: the place the test found (a clash), else the element. Nothing is added to the model; Clear highlights removes it" relationship="description">
                <Button size="small" icon={<Highlight16Regular />} onClick={() => props.onShow(item, "geometry")}>
                  Highlight
                </Button>
              </Tooltip>
            )}
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
          {groupOf(result) && (
            <div className="mt-actions">
              <Button size="small" icon={<Cursor16Regular />} onClick={() => props.onShow(groupOf(result)!, "select")}>
                Select all
              </Button>
              <Button size="small" icon={<Eye16Regular />} onClick={() => props.onShow(groupOf(result)!, "3d")}>
                Isolate all
              </Button>
              <Button size="small" icon={<ScanDash16Regular />} onClick={() => props.onShow(groupOf(result)!, "box")}>
                Selection box
              </Button>
              {test.kind !== "Rule" && (
                <Tooltip content="Draws where they fail in red over the 3D views and plans: the places the test found (clashes), else the elements. Nothing is added to the model; Clear highlights removes it" relationship="description">
                  <Button size="small" icon={<Highlight16Regular />} onClick={() => props.onShow(groupOf(result)!, "geometry")}>
                    Highlight all
                  </Button>
                </Tooltip>
              )}
            </div>
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

/** What a test checks: its categories and parameter as Revit names them, with their API names, and the conditions. */
function WhatItChecks({ test, item }: { test: ModelTestInfo; item?: ModelTestCase }) {
  const rows: { label: string; value: ReactNode }[] = [];
  const categories = test.categories ?? [];
  if (categories.length) {
    rows.push({
      label: categories.length === 1 ? "Category" : "Categories",
      value: categories.map((category, index) => (
        <span key={category.api}>
          {index > 0 && ", "}
          {category.name} <span className="mono muted small">{category.api}</span>
        </span>
      )),
    });
  }
  if (test.elementClass) rows.push({ label: "Elements", value: <span className="mono">{test.elementClass}</span> });
  if (test.kind === "Model") rows.push({ label: "Elements", value: "Whole model" });
  if (test.parameter) {
    rows.push({
      label: test.typeParameter ? "Type parameter" : "Parameter",
      value: (
        <>
          {test.parameter.name}
          {test.parameter.api !== test.parameter.name && <span className="mono muted small"> {test.parameter.api}</span>}
        </>
      ),
    });
  }
  if (test.value) rows.push({ label: "Value of", value: test.value });
  // The element's own value: what is there now, against what the check wants
  if (item?.valueState) {
    rows.push({
      label: "Value",
      value:
        item.valueState === "missing" ? (
          <span className="error-text">no such parameter on this element</span>
        ) : item.valueState === "empty" ? (
          <span className="error-text">empty</span>
        ) : (
          <span className="strong">{item.value}</span>
        ),
    });
  }
  if (test.checks) rows.push({ label: "Check", value: test.checks });
  if (test.kind !== "Rule") rows.push({ label: "Check", value: "Code" });
  if (test.filtered) rows.push({ label: "Filter", value: "Where(…) in code" });
  return (
    <dl className="mt-what">
      {rows.map((row, index) => (
        <div key={index}>
          <dt>{row.label}</dt>
          <dd>{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** A count of the strip that is also a filter: pressed, the list shows only its tests; pressed again, everything. */
function FilterChip({ active, tone, title, onToggle, children }: { active: boolean; tone?: "error" | "warning"; title: string; onToggle: () => void; children: ReactNode }) {
  return (
    <button type="button" className={`mt-chip${tone ? ` ${tone}` : ""}${active ? " on" : ""}`} aria-pressed={active} title={title} onClick={onToggle}>
      {children}
    </button>
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

/**
 * Every failing element of a test as one case, for the actions on the whole group: the new failures, or every failure when
 * all are accepted; null when no element failed.
 */
/** What a group action works on: every failing element of a test, the ones they hit, and every place the test found. */
function groupOf(result: ModelTestResult | undefined): Shown | null {
  const failed = (result?.cases ?? []).filter((item) => item.elementId !== null && item.status !== "error");
  const shown = failed.some((item) => !item.inBaseline) ? failed.filter((item) => !item.inBaseline) : failed;
  const ids = [...new Set(shown.flatMap((item) => [item.elementId!, ...item.related]))];
  if (!ids.length) return null;
  return {
    key: "*",
    status: "failed",
    message: "",
    elementId: ids[0],
    label: null,
    related: ids.slice(1),
    inBaseline: false,
    point: null,
    box: null,
    boxes: shown.flatMap((item) => (item.box ? [item.box] : [])),
    points: shown.flatMap((item) => (item.point ? [item.point] : [])),
  };
}

/** A case, or the cases of a whole test at once (their places together). */
type Shown = ModelTestCase & { boxes?: number[][]; points?: number[][] };

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

/** The trend in a line: new failures of the latest full runs, small enough for the strip of the pane. */
function Sparkline({ run }: { run: ModelTestRun }) {
  const points = run.trend.slice(-12);
  const most = Math.max(1, ...points.map((point) => point.newCases));
  return (
    <Tooltip content={`New failures, last ${points.length} full runs: ${points.map((point) => point.newCases).join(" → ")}`} relationship="description">
      <span className="mt-spark">
        {points.map((point) => (
          <span key={point.at} style={{ height: `${Math.max(12, (point.newCases / most) * 100)}%` }} />
        ))}
      </span>
    </Tooltip>
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
