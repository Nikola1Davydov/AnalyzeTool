import type { ParameterReportDto, ReportCountDto } from "../../../api/tools";

// The report as data: what the user turned on, and the blocks the printable pages are made of.

/** The three analyses a parameter can show; each as a chart, a table, or both. */
export type SectionKind = "filled" | "values" | "levels";

export interface SectionSettings {
  enabled: boolean;
  chart: boolean;
  table: boolean;
}

export interface ReportSettings {
  title: string;
  summary: boolean;
  topValues: number;
  sections: Record<SectionKind, SectionSettings>;
}

export const SECTION_LABELS: Record<SectionKind, { title: string; hint: string }> = {
  filled: { title: "Filled vs empty", hint: "per category" },
  values: { title: "Values", hint: "most frequent first" },
  levels: { title: "Per level", hint: "filled vs empty, bottom to top" },
};

export const SECTION_KINDS = Object.keys(SECTION_LABELS) as SectionKind[];

export function defaultSettings(): ReportSettings {
  return {
    title: "Parameter report",
    summary: true,
    topValues: 10,
    sections: {
      filled: { enabled: true, chart: true, table: true },
      values: { enabled: true, chart: true, table: false },
      levels: { enabled: false, chart: true, table: false },
    },
  };
}

export function percent(filled: number, total: number) {
  return total > 0 ? Math.round((filled / total) * 100) : 0;
}

/** A bar of a chart: one value, or filled and empty stacked. */
export type Bar = { label: string; value: number; rest?: number };

export function sectionBars(section: SectionKind, parameter: ParameterReportDto): Bar[] {
  if (section === "values") {
    const bars: Bar[] = parameter.values.map((value) => ({ label: value.value, value: value.count }));
    if (parameter.otherValuesCount > 0) bars.push({ label: "Other", value: parameter.otherValuesCount });
    return bars;
  }
  const source: ReportCountDto[] = section === "filled" ? parameter.categories : parameter.levels;
  return source.map((count) => ({ label: count.name, value: count.filled, rest: count.total - count.filled }));
}

// ---- Settings remembered per viewer: a convenience, the report itself is rebuilt from the model ----------

const SETTINGS_KEY = "at.report.settings";

export function loadSettings(): ReportSettings {
  const base = defaultSettings();
  try {
    const saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) ?? "null");
    if (saved) return { ...base, ...saved, sections: { ...base.sections, ...saved.sections } };
  } catch {
    // no storage, or an old shape: defaults
  }
  return base;
}

export function storeSettings(settings: ReportSettings) {
  try {
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
  } catch {
    // storage unavailable: settings last for this window
  }
}

export function readStored<T>(key: string): T | null {
  try {
    return JSON.parse(localStorage.getItem(key) ?? "null") as T | null;
  } catch {
    return null;
  }
}

export function writeStored(key: string, value: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // storage unavailable
  }
}
