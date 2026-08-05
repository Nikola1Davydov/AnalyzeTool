import type { JsonSchema } from "./types";

// Reading declared schemas for the editor's benefit. Small on purpose: the point is to answer
// "what does this node take, and what does it hand on", which is the question an author cannot
// answer from a command name and was the first thing the editor got wrong.

/** Top-level property names of a schema, in declaration order. Empty when nothing is declared. */
export function fieldNames(schema: JsonSchema | null | undefined): string[] {
  return schema?.properties ? Object.keys(schema.properties) : [];
}

export function typeLabel(schema: JsonSchema | null | undefined): string {
  if (!schema) return "any";
  const t = Array.isArray(schema.type) ? schema.type.find((x) => x !== "null") : schema.type;
  if (t === "array") return `${typeLabel(schema.items) || "any"}[]`;
  return t ?? "any";
}

/**
 * Field paths a binding can usefully name, one level into arrays of objects.
 *
 * A pipeline almost never binds a whole result — it binds the ids out of a list of rows. So
 * `rows: [{ typeId, instanceCount }]` offers `rows`, `rows[*].typeId` and `rows[*].instanceCount`,
 * which are the expressions the engine's wildcard resolution actually supports. Going deeper is
 * possible and not offered: two levels of nesting is where a dropdown stops helping, and the path
 * box next to it takes anything.
 */
export function bindablePaths(
  schema: JsonSchema | null | undefined,
  target?: JsonSchema | null,
): string[] {
  const properties = schema?.properties;
  if (!properties) return [];

  // What the TARGET takes decides which paths are worth offering, and offering the rest is how a
  // wrong wire gets made: a Filter bound to `families[*].isInPlace` receives 286 bare booleans, has
  // no field on them to compare, and drops everything while looking correctly connected.
  //
  //  • target items are untyped or objects → it wants ROWS, so only whole arrays are offered;
  //  • target items are a scalar type      → it wants a list of values, so leaf paths are offered.
  const wantsRows = !target?.items?.type || target.items.type === "object" || !!target.items?.properties;

  const paths: string[] = [];
  for (const [key, field] of Object.entries(properties)) {
    const isArray = field.type === "array" || (Array.isArray(field.type) && field.type.includes("array"));
    if (!target || !isArray || wantsRows) paths.push(key);
    if (wantsRows && target) continue;

    const item = field.items;
    if (!item?.properties) continue;
    for (const inner of Object.keys(item.properties)) paths.push(`${key}[*].${inner}`);
  }
  return paths;
}

/**
 * Field names of the ROWS a node hands on, for a Filter's conditions.
 *
 * A Filter's `where` compares fields of each item, not of the envelope — so what it needs is the
 * shape INSIDE the array the previous node returned, not that node's top-level properties. Given
 * `{ rows: [{ typeId, instanceCount }] }` this answers typeId and instanceCount, which is the
 * difference between a condition editor that helps and one that offers "rows".
 */
export function itemFieldNames(schema: JsonSchema | null | undefined, arrayField?: string): string[] {
  const properties = schema?.properties;
  if (!properties) return [];

  const candidates = arrayField && properties[arrayField] ? [properties[arrayField]] : Object.values(properties);
  for (const field of candidates) {
    const item = field.items;
    if (item?.properties) return Object.keys(item.properties);
  }
  return [];
}

/** A compact one-line preview of a value, for a node card that has no room for JSON. */
export function summarize(value: unknown): string {
  if (value === null || value === undefined) return "null";
  if (Array.isArray(value)) return `${value.length} item(s)`;
  if (typeof value === "object") {
    const entries = Object.entries(value as Record<string, unknown>);
    return entries
      .slice(0, 3)
      .map(([k, v]) => `${k}: ${Array.isArray(v) ? `${v.length} item(s)` : String(v)}`)
      .join(", ");
  }
  return String(value);
}

/**
 * A binding this node probably wants from the one before it, or null when it is not obvious.
 *
 * Wiring a node by hand is where authoring goes wrong: the first agent to try bound a whole
 * envelope where a list of ids was wanted, and the first person to open the editor could not tell
 * what to connect at all. Both are the same gap — the shapes are declared, and nothing was reading
 * them. So the obvious case is done automatically and shown as an ordinary binding the author can
 * change; anything ambiguous is left alone rather than guessed at.
 *
 * Two shapes cover nearly everything a pipeline does:
 *  • the target takes a list of ROWS  → bind the source's single array property;
 *  • the target takes a list of IDS   → bind `rows[*].id`, matching the field by name.
 */
export function suggestBinding(
  target: JsonSchema | null | undefined,
  source: JsonSchema | null | undefined,
  targetName: string,
): string | null {
  const properties = source?.properties;
  if (!properties || !target) return null;

  const isArray = (s: JsonSchema | undefined) =>
    s?.type === "array" || (Array.isArray(s?.type) && s!.type.includes("array"));
  if (!isArray(target)) return null;

  const arrays = Object.entries(properties).filter(([, s]) => isArray(s));
  if (arrays.length !== 1) return null; // two candidate lists is a choice, not a default
  const [arrayName, arraySchema] = arrays[0];

  const wantsRows = !target.items?.type || target.items.type === "object" || !!target.items?.properties;
  if (wantsRows) return arrayName;

  // A list of ids. Match by name — "typeIds" wants `typeId` — and fall back to a plain `id`, which
  // is what a row of things generally calls itself.
  const inner = arraySchema.items?.properties;
  if (!inner) return null;

  const singular = targetName.replace(/s$/i, "").toLowerCase();
  const byName = Object.keys(inner).find((k) => k.toLowerCase() === singular);
  const byId = Object.keys(inner).find((k) => k.toLowerCase() === "id");
  const field = byName ?? byId;

  return field ? `${arrayName}[*].${field}` : null;
}
