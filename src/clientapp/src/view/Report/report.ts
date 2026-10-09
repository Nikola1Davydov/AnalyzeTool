// The A4 report as data: what GetParameterReport returns, what the user turned on, and the flat list
// of blocks that the pages are filled with. Kept free of Vue so the layout rules read in one place.

export interface ReportCount {
  name: string;
  total: number;
  filled: number;
}

export interface ReportValue {
  value: string;
  count: number;
}

export interface ParameterReport {
  name: string;
  guid?: string;
  isInstance: boolean;
  total: number;
  filled: number;
  categories: ReportCount[];
  levels: ReportCount[];
  values: ReportValue[];
  otherValuesCount: number;
  distinctValues: number;
  error?: string;
}

export interface ReportData {
  documentTitle: string;
  parameters: ParameterReport[];
}

/** The three analyses a parameter can show; each one as a chart, a table, or both. */
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

export type Block =
  | { kind: "title"; key: string }
  | { kind: "summary"; key: string; rows: ParameterReport[]; continued: boolean }
  | { kind: "heading"; key: string; param: ParameterReport }
  | { kind: "chart"; key: string; section: SectionKind; param: ParameterReport; heightMm: number }
  | { kind: "table"; key: string; section: SectionKind; param: ParameterReport; rows: TableRow[]; continued: boolean; footer?: TableRow };

export interface TableRow {
  label: string;
  /** Filled/empty tables: filled and total. Value tables: count, and the share of filled elements. */
  filled?: number;
  total?: number;
  count?: number;
  share?: number;
}

/** Rows per table block. A longer table continues in a second block, which may start a new page. */
const ROWS_PER_TABLE = 28;
const ROWS_PER_SUMMARY = 30;

export function percent(filled: number, total: number): number {
  return total > 0 ? Math.round((filled / total) * 100) : 0;
}

/** Bars × a few millimetres, within a range that stays readable printed and fits a page. */
export function chartHeightMm(bars: number): number {
  return Math.min(150, Math.max(45, 18 + bars * 6));
}

function chunk<T>(items: T[], size: number): T[][] {
  if (!items.length) return [[]];
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += size) out.push(items.slice(i, i + size));
  return out;
}

function sectionRows(section: SectionKind, p: ParameterReport): { rows: TableRow[]; footer?: TableRow } {
  if (section === "values") {
    const rows = p.values.map((v) => ({ label: v.value, count: v.count, share: percent(v.count, p.filled) }));
    const other = p.otherValuesCount;
    return {
      rows,
      footer: other > 0
        ? { label: `Other (${p.distinctValues - p.values.length} values)`, count: other, share: percent(other, p.filled) }
        : undefined,
    };
  }
  const source = section === "filled" ? p.categories : p.levels;
  return { rows: source.map((c) => ({ label: c.name, filled: c.filled, total: c.total })) };
}

function barCount(section: SectionKind, p: ParameterReport): number {
  if (section === "values") return p.values.length + (p.otherValuesCount > 0 ? 1 : 0);
  return (section === "filled" ? p.categories : p.levels).length;
}

/** The report, top to bottom, as blocks a page can hold whole. */
export function buildBlocks(data: ReportData | null, settings: ReportSettings): Block[] {
  if (!data) return [];
  const blocks: Block[] = [{ kind: "title", key: "title" }];

  if (settings.summary && data.parameters.length) {
    chunk(data.parameters, ROWS_PER_SUMMARY).forEach((rows, i) =>
      blocks.push({ kind: "summary", key: `summary:${i}`, rows, continued: i > 0 }),
    );
  }

  const sections = (Object.keys(settings.sections) as SectionKind[]).filter((s) => settings.sections[s].enabled);

  data.parameters.forEach((p, pi) => {
    blocks.push({ kind: "heading", key: `h:${pi}`, param: p });
    if (p.error || p.total === 0) return;

    for (const section of sections) {
      const s = settings.sections[section];
      const bars = barCount(section, p);
      if (!bars) continue;
      if (s.chart)
        blocks.push({ kind: "chart", key: `c:${pi}:${section}`, section, param: p, heightMm: chartHeightMm(bars) });
      if (s.table) {
        const { rows, footer } = sectionRows(section, p);
        const parts = chunk(rows, ROWS_PER_TABLE);
        parts.forEach((part, i) =>
          blocks.push({
            kind: "table",
            key: `t:${pi}:${section}:${i}`,
            section,
            param: p,
            rows: part,
            continued: i > 0,
            footer: i === parts.length - 1 ? footer : undefined,
          }),
        );
      }
    }
  });
  return blocks;
}

// ---- Pages --------------------------------------------------------------------------------------

export const PAGE = { widthMm: 210, heightMm: 297, marginMm: 14, headerMm: 7, footerMm: 7, gapMm: 4 };
export const PX_PER_MM = 96 / 25.4;

/** Content area of a page, in CSS pixels — what the blocks are measured against. */
export function contentHeightPx(): number {
  return (PAGE.heightMm - 2 * PAGE.marginMm - PAGE.headerMm - PAGE.footerMm) * PX_PER_MM;
}

/**
 * Fills pages with blocks in order, given each block's measured height. A heading or the title is
 * never the last thing on a page — it moves on with the block it introduces. A block taller than a
 * page gets a page of its own (tables are already chunked, so in practice that does not happen).
 */
export function paginate(blocks: Block[], heights: number[]): number[][] {
  const limit = contentHeightPx();
  const gap = PAGE.gapMm * PX_PER_MM;
  const pages: number[][] = [];
  let page: number[] = [];
  let used = 0;

  for (let i = 0; i < blocks.length; i++) {
    const h = heights[i] ?? 0;
    const keepWithNext = blocks[i].kind === "heading" && i + 1 < blocks.length && blocks[i + 1].kind !== "heading";
    const need = h + (keepWithNext ? gap + (heights[i + 1] ?? 0) : 0);
    const add = page.length ? gap + need : need;

    if (page.length && used + add > limit) {
      pages.push(page);
      page = [];
      used = 0;
    }
    used += (page.length ? gap : 0) + h;
    page.push(i);
  }
  if (page.length) pages.push(page);
  return pages;
}
