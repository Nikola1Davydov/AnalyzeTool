// The wire contract of the button statistics (source/AnalyseTool/Features/Statistics/ButtonStatistics.cs).

export const statisticsCommands = {
  GetButtonStatistics: "GetButtonStatistics",
  ResetButtonStatistics: "ResetButtonStatistics",
} as const;

export interface ButtonStatistic {
  /** The folder id; `analysetool` for AnalyseTool's own buttons. */
  extension: string;
  extensionName: string;
  button: string;
  /** null for a button that opens a page. */
  command: string | null;
  builtIn: boolean;
  /** false: an extension that is off or gone, or a renamed button. */
  onRibbon: boolean;
  clicks: number;
  failures: number;
  firstClick: string | null;
  lastClick: string | null;
  lastError: string | null;
  lastErrorAt: string | null;
  averageMilliseconds: number | null;
  /** Clicks per day, oldest first, today last (30 entries). */
  last30Days: number[];
}

export interface ButtonStatisticsResult {
  since: string | null;
  totalClicks: number;
  buttons: ButtonStatistic[];
}

export interface ResetButtonStatisticsRequest {
  extension?: string | null;
}

export interface ResetButtonStatisticsResult {
  removed: number;
}
