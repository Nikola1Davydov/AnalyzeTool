// The wire contract of the model tests (source/AnalyseTool.Extensions/Features/ModelTestCommands.cs,
// Testing/ModelTestResults.cs): System.Text.Json, camelCase.

export const modelTestCommands = {
  GetModelTests: "GetModelTests",
  RunModelTests: "RunModelTests",
  GetModelTestResults: "GetModelTestResults",
  FixModelTestCases: "FixModelTestCases",
  AcceptModelTestFailures: "AcceptModelTestFailures",
  CreateModelTestSuite: "CreateModelTestSuite",
  SelectionInRevit: "SelectionInRevit",
  IsolationInRevit: "IsolationInRevit",
  ShowGeometry: "ShowGeometry",
  ClearGeometry: "ClearGeometry",
} as const;

/** Sent to every page after a run, whoever started it. */
export const modelTestsRanEvent = "modelTestsRan";

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
}

export interface ModelTestSuiteInfo {
  id: string;
  name: string;
  extensionId: string;
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

export interface CreateModelTestSuiteRequest {
  name: string;
  id?: string | null;
  description?: string | null;
}
