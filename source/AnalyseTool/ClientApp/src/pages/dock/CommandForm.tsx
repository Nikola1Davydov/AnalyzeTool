import { useState } from "react";
import { SHEET_FORM } from "./Actions";

/** A property of a JSON Schema, as far as a form needs it. */
type Property = {
  type?: string | string[];
  description?: string;
  title?: string;
  enum?: unknown[];
  default?: unknown;
};

type Schema = { properties?: Record<string, Property>; required?: string[] };

const kindOf = (property: Property) => {
  const types = (Array.isArray(property.type) ? property.type : [property.type]).filter((type) => type && type !== "null");
  if (property.enum?.length) return "enum";
  return (types[0] as string | undefined) ?? "string";
};

/** «CountWalls» / «count_walls» → «Count walls». */
const label = (name: string) =>
  name
    .replace(/[_-]+/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, (first) => first.toUpperCase())
    .replace(/ ([A-Z])(?=[a-z])/g, (_, letter: string) => ` ${letter.toLowerCase()}`);

/**
 * The form of a command, built from the schema of its input — no HTML to write: text, numbers, a switch for a yes/no, a
 * choice for an enum. It starts with the last values used, else the defaults of the schema; the header's «Run» submits it.
 */
export function CommandForm(props: { schema: unknown; initial?: Record<string, unknown>; description?: string | null; onRun: (values: Record<string, unknown>) => void }) {
  const schema = (props.schema ?? {}) as Schema;
  const properties = Object.entries(schema.properties ?? {});
  const required = new Set(schema.required ?? []);
  const [values, setValues] = useState<Record<string, unknown>>(() =>
    Object.fromEntries(properties.map(([name, property]) => [name, props.initial?.[name] ?? property.default ?? (kindOf(property) === "boolean" ? false : "")])),
  );
  const set = (name: string, value: unknown) => setValues((current) => ({ ...current, [name]: value }));

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form command-form"
      onSubmit={(event) => {
        event.preventDefault();
        // Numbers as numbers; empty optional fields left out, so the command sees its own defaults
        const input: Record<string, unknown> = {};
        for (const [name, property] of properties) {
          const value = values[name];
          const kind = kindOf(property);
          if ((value === "" || value === undefined) && !required.has(name)) continue;
          input[name] = kind === "integer" ? Math.trunc(Number(value)) : kind === "number" ? Number(value) : value;
        }
        props.onRun(input);
      }}
    >
      {props.description && <p className="muted small">{props.description}</p>}
      {properties.map(([name, property]) => {
        const kind = kindOf(property);
        const title = property.title ?? label(name);
        if (kind === "boolean") {
          const on = values[name] === true;
          return (
            <label key={name} className="switch-row">
              <span>{title}</span>
              <button type="button" role="switch" aria-checked={on} className={`switch ${on ? "on" : ""}`} onClick={() => set(name, !on)}>
                <span className="switch-knob" />
              </button>
            </label>
          );
        }
        return (
          <label key={name} className="field">
            <span>
              {title}
              {required.has(name) ? " *" : ""}
            </span>
            {kind === "enum" ? (
              <select value={String(values[name] ?? "")} onChange={(event) => set(name, event.target.value)}>
                {!required.has(name) && <option value="">—</option>}
                {property.enum!.map((option) => (
                  <option key={String(option)} value={String(option)}>
                    {String(option)}
                  </option>
                ))}
              </select>
            ) : (
              <input
                type={kind === "integer" || kind === "number" ? "number" : "text"}
                step={kind === "integer" ? 1 : "any"}
                required={required.has(name)}
                value={String(values[name] ?? "")}
                onChange={(event) => set(name, event.target.value)}
              />
            )}
            {property.description && <span className="muted small">{property.description}</span>}
          </label>
        );
      })}
    </form>
  );
}
