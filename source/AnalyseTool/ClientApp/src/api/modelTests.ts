// The wire contract of the model tests (source/AnalyseTool.Extensions/Features/ModelTestCommands.cs,
// Testing/ModelTestResults.cs): System.Text.Json, camelCase.

export const modelTestCommands = {
  GetModelTests: "GetModelTests",
  RunModelTests: "RunModelTests",
  GetModelTestResults: "GetModelTestResults",
  FixModelTestCases: "FixModelTestCases",
  AcceptModelTestFailures: "AcceptModelTestFailures",
  CreateModelTestSuite: "CreateModelTestSuite",
  GetModelTestStatistics: "GetModelTestStatistics",
  ResetModelTestStatistics: "ResetModelTestStatistics",
  SelectionInRevit: "SelectionInRevit",
  IsolationInRevit: "IsolationInRevit",
  ShowGeometry: "ShowGeometry",
  ShowElementsIn3D: "ShowElementsIn3D",
  ClearGeometry: "ClearGeometry",
} as const;

/** Sent to every page after a run, whoever started it. */
export const modelTestsRanEvent = "modelTestsRan";

/** Sent when the extensions were loaded again (a saved file): what Watch runs on. */
export const extensionsChangedEvent = "extensionsChanged";

export type TestKind = "Rule" | "Check" | "Model";
export type TestSeverity = "Info" | "Warning" | "Error";
export type TestStatus = "passed" | "failed" | "error" | "inconclusive" | "skipped";
export type CaseStatus = "failed" | "error" | "inconclusive";

export interface ModelTestInfo {
  id: string;
  name: string;
  /** What it checks: `Doors · Fire Rating · not empty`. */
  description: string;
  kind: TestKind;
  severity: TestSeverity;
  /** `name=value`. */
  traits: string[];
  slow: boolean;
  skip: string | null;
  /** Relative to the suite's directory. */
  file: string;
  line: number;
  /** The label of its repair. */
  fix: string | null;
  /** The categories it runs over: as Revit names them, and their API names. */
  categories: RevitName[];
  /** The element class it runs over (`Room`), instead of categories. */
  elementClass: string | null;
  /** The parameter a rule checks. */
  parameter: RevitName | null;
  /** The parameter is one of the element's type. */
  typeParameter: boolean;
  /** What a rule checks when it is not a parameter. */
  value: string | null;
  /** The conditions of a rule: `not empty, unique`. */
  checks: string | null;
  /** The elements are filtered further in code. */
  filtered: boolean;
}

/** A category or a parameter: as Revit shows it, and its API name. */
export interface RevitName {
  name: string;
  api: string;
}

export interface ModelTestSuiteInfo {
  id: string;
  name: string;
  extensionId: string;
  /** The name of the folder (extension): the Tests tab groups the suites by it. */
  extensionName: string;
  directory: string;
  tests: ModelTestInfo[];
}

export interface ModelTestList {
  suites: ModelTestSuiteInfo[];
}

export interface ModelTestCase {
  key: string;
  status: CaseStatus;
  message: string;
  elementId: number | null;
  label: string | null;
  related: number[];
  inBaseline: boolean;
  /** [x, y, z] in feet. */
  point: number[] | null;
  /** [minX, minY, minZ, maxX, maxY, maxZ] in feet. */
  box: number[] | null;
  /** The value a rule found on the element, as Revit shows it. */
  value?: string | null;
  /** `set`, `empty` or `missing` for a case of a rule; null for code. */
  valueState?: "set" | "empty" | "missing" | null;
}

export interface ModelTestResult {
  testId: string;
  status: TestStatus;
  checked: number;
  failed: number;
  new: number;
  baseline: number;
  resolved: number;
  inconclusive: number;
  errors: number;
  skip: string | null;
  cases: ModelTestCase[];
  truncated: boolean;
  milliseconds: number;
}

export interface ModelTestTotals {
  tests: number;
  passed: number;
  failed: number;
  errors: number;
  inconclusive: number;
  skipped: number;
  newCases: number;
  baselineCases: number;
  inconclusiveCases: number;
}

export interface ModelTestTrendPoint {
  at: string;
  newCases: number;
  baselineCases: number;
  failed: number;
}

export interface ModelTestRun {
  model: string;
  modelKey: string;
  at: string;
  milliseconds: number;
  trigger: string;
  full: boolean;
  totals: ModelTestTotals;
  results: ModelTestResult[];
  trend: ModelTestTrendPoint[];
}

export interface RunModelTestsRequest {
  suite?: string | null;
  tests?: string[] | null;
  trait?: string | null;
  elementIds?: number[] | null;
  includeSlow?: boolean;
  maxCases?: number;
  trigger?: string | null;
}

export interface FixModelTestCasesRequest {
  testId: string;
  keys?: string[] | null;
}

export interface ModelTestFixResult {
  testId: string;
  fixed: number;
  problems: string[];
}

export interface AcceptModelTestFailuresRequest {
  testId: string;
  keys?: string[] | null;
  remove?: boolean;
}

export interface ModelTestBaselineResult {
  testId: string;
  accepted: number;
  file: string;
}

/** How one model test has run (GetModelTestStatistics). */
export interface ModelTestStatistic {
  testId: string;
  test: string;
  suite: string;
  folder: string;
  /** Still declared; false for a test that is gone or renamed. */
  exists: boolean;
  runs: number;
  /** Runs with new failures. */
  failed: number;
  /** Runs in which the test threw: a bug in the test. */
  errors: number;
  inconclusive: number;
  lastStatus: TestStatus | null;
  lastRun: string | null;
  lastNew: number;
  lastError: string | null;
  averageMilliseconds: number | null;
  /** Runs per day, oldest first, today last (30 entries). */
  last30Days: number[];
}

export interface ModelTestStatisticsResult {
  since: string | null;
  runs: number;
  tests: ModelTestStatistic[];
}

export interface CreateModelTestSuiteRequest {
  name: string;
  id?: string | null;
  description?: string | null;
  /** An existing folder to add the suite to; a new folder when absent. */
  extension?: string | null;
}
