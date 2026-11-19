import { diffLines } from "diff";
import { Fragment, useMemo, useState } from "react";

// A line-based diff of two texts, unified or side by side. Long runs of unchanged lines fold away, as in a code review.

export type DiffMode = "unified" | "split";

type Line = { kind: "same" | "add" | "del"; oldNo?: number; newNo?: number; text: string };
type Pair = { left?: Line; right?: Line; changed: boolean };
type Item<T> = { type: "row"; row: T } | { type: "fold"; start: number; count: number; rows: T[] };

const CONTEXT = 3;

function splitLines(value: string) {
  const lines = value.split("\n");
  if (lines.length && lines[lines.length - 1] === "") lines.pop();
  return lines.map((line) => line.replace(/\r$/, ""));
}

export function diffToLines(before: string, after: string): Line[] {
  const lines: Line[] = [];
  let oldNo = 1;
  let newNo = 1;
  for (const part of diffLines(before, after)) {
    for (const text of splitLines(part.value)) {
      if (part.added) lines.push({ kind: "add", newNo: newNo++, text });
      else if (part.removed) lines.push({ kind: "del", oldNo: oldNo++, text });
      else lines.push({ kind: "same", oldNo: oldNo++, newNo: newNo++, text });
    }
  }
  return lines;
}

/** Removed and added lines next to each other, row by row. */
function toPairs(lines: Line[]): Pair[] {
  const pairs: Pair[] = [];
  let i = 0;
  while (i < lines.length) {
    if (lines[i].kind === "same") {
      pairs.push({ left: lines[i], right: lines[i], changed: false });
      i++;
      continue;
    }
    const removed: Line[] = [];
    const added: Line[] = [];
    while (i < lines.length && lines[i].kind === "del") removed.push(lines[i++]);
    while (i < lines.length && lines[i].kind === "add") added.push(lines[i++]);
    for (let k = 0; k < Math.max(removed.length, added.length); k++) {
      pairs.push({ left: removed[k], right: added[k], changed: true });
    }
  }
  return pairs;
}

/** Rows near a change stay; longer runs between them become folds. */
function fold<T>(rows: T[], changed: (row: T) => boolean, expanded: Set<number>): Item<T>[] {
  const keep = new Array<boolean>(rows.length).fill(false);
  rows.forEach((row, index) => {
    if (!changed(row)) return;
    for (let k = Math.max(0, index - CONTEXT); k <= Math.min(rows.length - 1, index + CONTEXT); k++) keep[k] = true;
  });
  const items: Item<T>[] = [];
  let index = 0;
  while (index < rows.length) {
    if (keep[index]) {
      items.push({ type: "row", row: rows[index++] });
      continue;
    }
    const start = index;
    while (index < rows.length && !keep[index]) index++;
    const run = rows.slice(start, index);
    // A fold of one or two lines saves nothing
    if (run.length <= 2 || expanded.has(start)) run.forEach((row) => items.push({ type: "row", row }));
    else items.push({ type: "fold", start, count: run.length, rows: run });
  }
  return items;
}

const sign = { same: " ", add: "+", del: "-" } as const;

type DiffViewProps = {
  before: string | null;
  after: string | null;
  binary?: boolean;
  mode: DiffMode;
};

export function DiffView({ before, after, binary, mode }: DiffViewProps) {
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const lines = useMemo(() => (binary ? [] : diffToLines(before ?? "", after ?? "")), [before, after, binary]);
  const pairs = useMemo(() => toPairs(lines), [lines]);

  if (binary) return <div className="diff-note">Binary file — only the fact that it changed is known.</div>;
  if (!lines.length) return <div className="diff-note">Empty file.</div>;
  if (!lines.some((line) => line.kind !== "same")) return <div className="diff-note">No changes in the text.</div>;

  const expand = (start: number) => setExpanded((current) => new Set(current).add(start));
  const foldRow = (item: { start: number; count: number }, span: number) => (
    <tr key={`fold-${item.start}`} className="diff-fold">
      <td colSpan={span}>
        <button type="button" onClick={() => expand(item.start)}>
          ⋯ {item.count} unchanged lines
        </button>
      </td>
    </tr>
  );

  if (mode === "unified") {
    const items = fold(lines, (line) => line.kind !== "same", expanded);
    return (
      <div className="diff-scroll">
        <table className="diff-table">
          <tbody>
            {items.map((item, index) =>
              item.type === "fold" ? (
                foldRow(item, 4)
              ) : (
                <tr key={index} className={`diff-${item.row.kind}`}>
                  <td className="diff-no">{item.row.oldNo}</td>
                  <td className="diff-no">{item.row.newNo}</td>
                  <td className="diff-sign">{sign[item.row.kind]}</td>
                  <td className="diff-text">{item.row.text}</td>
                </tr>
              ),
            )}
          </tbody>
        </table>
      </div>
    );
  }

  const items = fold(pairs, (pair) => pair.changed, expanded);
  const half = (line: Line | undefined, side: "left" | "right") => {
    const kind = !line ? "empty" : line.kind;
    return (
      <Fragment>
        <td className={`diff-no diff-${kind}`}>{side === "left" ? line?.oldNo : line?.newNo}</td>
        <td className={`diff-text diff-${kind}`}>{line?.text}</td>
      </Fragment>
    );
  };
  return (
    <div className="diff-scroll">
      <table className="diff-table split">
        <colgroup>
          <col className="diff-no-col" />
          <col />
          <col className="diff-no-col" />
          <col />
        </colgroup>
        <tbody>
          {items.map((item, index) =>
            item.type === "fold" ? (
              foldRow(item, 4)
            ) : (
              <tr key={index}>
                {half(item.row.left, "left")}
                {half(item.row.right, "right")}
              </tr>
            ),
          )}
        </tbody>
      </table>
    </div>
  );
}

/** How many lines a diff adds and removes. */
export function diffStats(before: string | null, after: string | null) {
  const lines = diffToLines(before ?? "", after ?? "");
  return {
    added: lines.filter((line) => line.kind === "add").length,
    removed: lines.filter((line) => line.kind === "del").length,
  };
}
