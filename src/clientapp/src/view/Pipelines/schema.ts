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
export function bindablePaths(schema: JsonSchema | null | undefined): string[] {
  const properties = schema?.properties;
  if (!properties) return [];

  const paths: string[] = [];
  for (const [key, field] of Object.entries(properties)) {
    paths.push(key);
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
