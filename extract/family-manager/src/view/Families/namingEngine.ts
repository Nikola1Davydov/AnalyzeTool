// Deterministic naming-template engine. A rule is a template string with {tokens}; the engine
// evaluates it against one element's context (name / family / category / type parameters) and an
// office-wide abbreviation dictionary. Deliberately AI-free: the AI's job (later) is to AUTHOR a
// template from an example — applying it stays reproducible and instant.
//
// Token syntax:   {source}  or  {source|mod|mod}
//   sources:      name, family, category, param:<Parameter Name>
//   modifiers:    abbr   — replace via the abbreviation dictionary (whole-value, case-insensitive)
//                 upper / lower
//                 clean  — strip copy suffixes ("Kopie", "Copy", "(1)") and squeeze whitespace
//                 nospace — remove spaces
//
// Empty values collapse their neighbouring separator, so a missing Material in
// "{category|abbr}_{param:Material|abbr}_{param:Width}" yields "Möb_1000", not "Möb__1000".

export interface NamingContext {
  name: string; // current (type or family) name
  family: string;
  category: string;
  params: Record<string, string>; // parameter display values, keyed by parameter name
}

export type AbbreviationDict = Record<string, string>; // full value (lowercased) -> abbreviation

type Segment = { kind: "literal" | "token"; text: string };

const TOKEN_RE = /\{([^{}]+)\}/g;

export function parseTemplate(template: string): Segment[] {
  const segments: Segment[] = [];
  let last = 0;
  for (const m of template.matchAll(TOKEN_RE)) {
    if (m.index! > last) segments.push({ kind: "literal", text: template.slice(last, m.index) });
    segments.push({ kind: "token", text: m[1] });
    last = m.index! + m[0].length;
  }
  if (last < template.length) segments.push({ kind: "literal", text: template.slice(last) });
  return segments;
}

function evalToken(token: string, ctx: NamingContext, abbr: AbbreviationDict): string {
  const [source, ...mods] = token.split("|").map((s) => s.trim());

  let value = "";
  const lower = source.toLowerCase();
  if (lower === "name") value = ctx.name;
  else if (lower === "family") value = ctx.family;
  else if (lower === "category") value = ctx.category;
  else if (lower.startsWith("param:")) {
    const paramName = source.slice(source.indexOf(":") + 1).trim();
    // Exact match first, then case-insensitive (parameter names differ between templates and projects).
    value =
      ctx.params[paramName] ??
      ctx.params[Object.keys(ctx.params).find((k) => k.toLowerCase() === paramName.toLowerCase()) ?? ""] ??
      "";
  }

  for (const mod of mods) {
    switch (mod.toLowerCase()) {
      case "abbr": {
        const hit = abbr[value.trim().toLowerCase()];
        if (hit) value = hit;
        break;
      }
      case "upper":
        value = value.toUpperCase();
        break;
      case "lower":
        value = value.toLowerCase();
        break;
      case "clean":
        value = value
          .replace(/\b(kopie|copy)\b\s*\d*/gi, "")
          .replace(/\(\d+\)/g, "")
          .replace(/\s+/g, " ")
          .trim();
        break;
      case "nospace":
        value = value.replace(/\s+/g, "");
        break;
    }
  }
  return value.trim();
}

/** Applies a template to one context. Empty token values swallow the separator before them. */
export function applyTemplate(template: string, ctx: NamingContext, abbr: AbbreviationDict): string {
  const segments = parseTemplate(template);
  const parts: string[] = [];
  let pendingLiteral: string | null = null;
  let hadValue = false;

  for (const seg of segments) {
    if (seg.kind === "literal") {
      pendingLiteral = (pendingLiteral ?? "") + seg.text;
      continue;
    }
    const value = evalToken(seg.text, ctx, abbr);
    if (!value) continue; // empty token: drop it AND the separator queued before it
    if (pendingLiteral !== null) {
      if (hadValue) parts.push(pendingLiteral); // a leading separator before the first value is dropped
      pendingLiteral = null;
    }
    parts.push(value);
    hadValue = true;
  }
  // A trailing literal only survives if it isn't a pure separator (e.g. template ends with "mm").
  if (pendingLiteral && /[\p{L}\p{N}]/u.test(pendingLiteral)) parts.push(pendingLiteral);

  return parts.join("").trim();
}

/** Distinct parameter names referenced by the template (for validation/UI hints). */
export function templateParams(template: string): string[] {
  const names = new Set<string>();
  for (const seg of parseTemplate(template)) {
    if (seg.kind !== "token") continue;
    const source = seg.text.split("|")[0].trim();
    if (source.toLowerCase().startsWith("param:")) names.add(source.slice(source.indexOf(":") + 1).trim());
  }
  return [...names];
}
