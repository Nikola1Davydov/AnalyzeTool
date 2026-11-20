import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import type { ExtensionSummary } from "../../api/extensions";
import {
  type CreateModelTestSuiteRequest,
  type ModelTestList,
  type ModelTestResult,
  type ModelTestRun,
  type ModelTestSuiteInfo,
  modelTestCommands,
} from "../../api/modelTests";
import { appQueryKey, useAppQuery } from "../../revit/hooks";
import { messageOf, SHEET_FORM, useActions } from "./Actions";
import { Tile, TileIcon } from "./Tile";

/** A folder of the Tests tab: an extension holding suites — a project, with as many suites as it likes. */
export type TestFolder = { id: string; name: string; directory: string; suites: ModelTestSuiteInfo[] };

/** The suites grouped by the folder (extension) they live in, in the order the host lists them. */
export function testFolders(list: ModelTestList | undefined): TestFolder[] {
  const folders = new Map<string, TestFolder>();
  for (const suite of list?.suites ?? []) {
    const folder = folders.get(suite.extensionId) ?? { id: suite.extensionId, name: suite.extensionName ?? suite.extensionId, directory: suite.directory, suites: [] };
    folder.suites.push(suite);
    folders.set(suite.extensionId, folder);
  }

  return [...folders.values()];
}

/** The model tests of every folder, for the tiles. */
export function useTestFolders(enabled = true) {
  return useAppQuery<ModelTestList>(modelTestCommands.GetModelTests, null, { enabled });
}

/**
 * The first screen of the Tests tab, laid out like the folders of Tools: a project is a folder, its suites are inside. A tile
 * shows a red badge with the new failures of the latest run.
 */
export function TestsScreen({ onOpen, onGuide }: { onOpen: (id: string) => void; onGuide: () => void }) {
  const actions = useActions();
  const tests = useTestFolders();
  const latest = useQuery({
    queryKey: ["modelTestResults"],
    queryFn: ({ signal }) => bridge.invoke<ModelTestRun | null>(modelTestCommands.GetModelTestResults, null, { signal }),
  });
  const results = new Map<string, ModelTestResult>((latest.data?.results ?? []).map((result) => [result.testId, result]));
  const folders = testFolders(tests.data);

  return (
    <>
      <h1 className="large-title">
        Tests
        {!bridge.inRevit && <span className="badge">mock</span>}
        <button className="help-button" title="How tests work" onClick={onGuide}>
          ?
        </button>
      </h1>
      {tests.isError && <p className="error small">{messageOf(tests.error)}</p>}
      <div className="grid">
        {folders.map((folder) => {
          const ids = folder.suites.flatMap((suite) => suite.tests.map((test) => test.id));
          const fresh = ids.reduce((sum, id) => sum + (results.get(id)?.new ?? 0), 0);
          const count = ids.length;
          return (
            <Tile
              key={folder.id}
              label={folder.name}
              title={`${folder.name}\n${folder.suites.length} suite(s), ${count} test(s)${fresh ? `\n${fresh} new failure(s) in the latest run` : ""}`}
              icon={
                <span className="tile-icon folder-icon">
                  {folder.suites.slice(0, 4).map((suite) => (
                    <TileIcon key={suite.id} title={suite.name} />
                  ))}
                </span>
              }
              className="folder"
              badge={fresh > 0 ? "failed" : null}
              onClick={() => onOpen(folder.id)}
            />
          );
        })}
        <Tile
          label="New folder"
          className="add"
          title="A new folder of tests — a project — with its first suite and its own version history"
          icon={<span className="tile-icon add">+</span>}
          onClick={() => actions.openSheet("New folder of tests", <NewTestsForm onCreated={onOpen} />, "Create")}
        />
      </div>
      <p className="muted small center">
        A folder is a project: its suites are C# files that check the open model. Open one to run its tests, see what fails, repair it or accept it.
      </p>
    </>
  );
}

/**
 * A folder of tests, laid out like a folder of Tools: every suite a tile (a red badge for its new failures), «New suite»
 * last. ▶ Run folder runs every suite of the folder; a tile opens the tests of its suite.
 */
export function TestFolderScreen({ folder, onOpen }: { folder: string; onOpen: (suite: string) => void }) {
  const actions = useActions();
  const queryClient = useQueryClient();
  const tests = useTestFolders();
  const latest = useQuery({
    queryKey: ["modelTestResults"],
    queryFn: ({ signal }) => bridge.invoke<ModelTestRun | null>(modelTestCommands.GetModelTestResults, null, { signal }),
  });
  const [running, setRunning] = useState(false);
  const found = testFolders(tests.data).find((item) => item.id === folder);
  const results = new Map<string, ModelTestResult>((latest.data?.results ?? []).map((result) => [result.testId, result]));

  async function runFolder() {
    setRunning(true);
    try {
      const run = await bridge.invoke<ModelTestRun>(modelTestCommands.RunModelTests, { suite: folder, trigger: "page" });
      queryClient.setQueryData(["modelTestResults"], run);
      const fresh = run.totals.newCases;
      actions.toast(fresh ? `${run.totals.failed} test(s) failing, ${fresh} new failure(s)` : `${run.totals.passed} of ${run.totals.tests} passed`, fresh ? "error" : "ok");
    } catch (failure) {
      actions.toast(messageOf(failure), "error");
    } finally {
      setRunning(false);
    }
  }

  if (!found) return <p className="muted small center">{tests.isPending ? "Loading…" : "This folder has no tests (any more)."}</p>;
  const ran = found.suites.flatMap((suite) => suite.tests).map((test) => results.get(test.id)).filter((result): result is ModelTestResult => !!result);
  return (
    <>
      <div className="tests-folder-head">
        <button className="tests-run" disabled={running} onClick={() => void runFolder()}>
          {running ? "Running…" : "▶ Run folder"}
        </button>
        <span className="muted small">
          {ran.length
            ? `${ran.filter((result) => result.status === "passed").length} of ${ran.length} passed · ${ran.reduce((sum, result) => sum + result.new, 0)} new failures`
            : `${found.suites.length} suite(s), ${found.suites.reduce((sum, suite) => sum + suite.tests.length, 0)} test(s)`}
        </span>
      </div>
      <div className="grid">
        {found.suites.map((suite) => {
          const own = suite.tests.map((test) => results.get(test.id)).filter((result): result is ModelTestResult => !!result);
          const fresh = own.reduce((sum, result) => sum + result.new, 0);
          return (
            <Tile
              key={suite.id}
              label={suite.name}
              title={`${suite.name}\n${suite.tests.length} test(s)${own.length ? `\n${own.filter((result) => result.status === "passed").length} passed, ${fresh} new failure(s)` : ""}`}
              icon={<TileIcon title={suite.name} />}
              badge={fresh > 0 ? "failed" : null}
              onClick={() => onOpen(suite.id)}
            />
          );
        })}
        <Tile
          label="New suite"
          className="add"
          title="One more suite in this folder: a C# file of example tests"
          icon={<span className="tile-icon add">+</span>}
          onClick={() => actions.openSheet(`New suite in ${found.name}`, <NewTestsForm extension={folder} />, "Create")}
        />
      </div>
    </>
  );
}

/** A new folder of tests, or one more suite in a folder. */
export function NewTestsForm({ extension, onCreated }: { extension?: string; onCreated?: (id: string) => void }) {
  const actions = useActions();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function create() {
    if (!name.trim() || busy) return;
    setBusy(true);
    setError(null);
    try {
      const created = await bridge.invoke<ExtensionSummary>(modelTestCommands.CreateModelTestSuite, { name: name.trim(), extension } satisfies CreateModelTestSuiteRequest);
      await queryClient.invalidateQueries({ queryKey: appQueryKey });
      actions.closeSheet();
      actions.toast(extension ? `Suite «${name.trim()}» added — its examples are a file to edit` : `Folder «${created.name}» with its first suite`);
      onCreated?.(created.id);
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        void create();
      }}
    >
      <label className="field">
        <span>{extension ? "Suite" : "Folder — a project"}</span>
        <input autoFocus value={name} onChange={(event) => setName(event.target.value)} placeholder={extension ? "Sheets" : "BEP Project X"} />
      </label>
      <p className="muted small">
        {extension
          ? "One more C# file in this folder with three example tests: a rule on doors, a rule on rooms, a test of the whole model."
          : "A folder with its first suite of three example tests. Open it in your editor or let an AI client change them: every save loads them again."}
      </p>
      {busy && <p className="muted small">Creating…</p>}
      {error && <p className="error small">{error}</p>}
    </form>
  );
}
