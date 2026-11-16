import DOMPurify from "dompurify";
import { marked } from "marked";
import { useMemo } from "react";

// Links leave the add-in: the WebView opens another address in the system browser.
DOMPurify.addHook("afterSanitizeAttributes", (node) => {
  if (node.tagName === "A") {
    node.setAttribute("target", "_blank");
    node.setAttribute("rel", "noopener noreferrer");
  }
});

/** Markdown rendered to sanitized HTML. */
export function Markdown({ text, className }: { text: string; className?: string }) {
  const html = useMemo(() => DOMPurify.sanitize(marked.parse(text, { async: false, gfm: true })), [text]);
  return <div className={`markdown ${className ?? ""}`} dangerouslySetInnerHTML={{ __html: html }} />;
}
