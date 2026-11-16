import { useEffect, useMemo, useRef } from "react";
import type { CompileDiagnostic } from "../api/extensions";

type CodeViewProps = {
  text: string;
  /** The line to scroll to and mark, from 1. */
  line?: number;
  /** Compiler messages of this file, marked on their lines. */
  diagnostics?: CompileDiagnostic[];
};

/** A read-only text with line numbers. */
export function CodeView({ text, line, diagnostics = [] }: CodeViewProps) {
  const lines = useMemo(() => text.replace(/\r\n/g, "\n").split("\n"), [text]);
  const target = useRef<HTMLTableRowElement | null>(null);
  const byLine = useMemo(() => {
    const map = new Map<number, CompileDiagnostic[]>();
    for (const diagnostic of diagnostics) map.set(diagnostic.line, [...(map.get(diagnostic.line) ?? []), diagnostic]);
    return map;
  }, [diagnostics]);

  useEffect(() => {
    target.current?.scrollIntoView({ block: "center" });
  }, [line, text]);

  return (
    <div className="code-view">
      <table>
        <tbody>
          {lines.map((content, index) => {
            const number = index + 1;
            const marks = byLine.get(number);
            const severity = marks?.some((mark) => mark.severity === "error") ? "error" : marks ? "warning" : "";
            return (
              <tr
                key={number}
                ref={number === line ? target : undefined}
                className={`${number === line ? "current" : ""} ${severity ? `has-${severity}` : ""}`}
                title={marks?.map((mark) => `${mark.code}: ${mark.message}`).join("\n")}
              >
                <td className="code-no">{number}</td>
                <td className="code-text">{content || " "}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
