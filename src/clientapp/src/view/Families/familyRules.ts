// Saved filter rules for Family Control. A rule is a set of field/operator/value conditions combined
// with all (AND) / any (OR). Rules are persisted in localStorage (per-user, per-machine — same layer as
// the preview cache) and applied to the gallery, table and worksets views. Pinned rules surface as
// one-click quick-navigation chips.

import { reactive, watch } from "vue";
import type { FilterRule, RuleCondition, RuleOperator, RuleScope } from "./types";

const STORAGE_KEY = "analysetool.familyRules.v1";

// Fields a rule can test, per scope. `type` drives which operators the builder offers and how values
// are compared. `value` reads the field off a record (FamilyRow for families, FamilyInstanceDetail for
// instances).
export interface FieldDef {
  key: string;
  label: string;
  type: "text" | "number" | "bool";
  value: (item: any) => unknown;
}

export const FIELDS: Record<RuleScope, FieldDef[]> = {
  families: [
    { key: "name", label: "Name", type: "text", value: (f) => f.name },
    { key: "category", label: "Category", type: "text", value: (f) => f.category },
    { key: "typeCount", label: "Type count", type: "number", value: (f) => f.typeCount },
    { key: "instanceCount", label: "Instance count", type: "number", value: (f) => f.instanceCount },
    { key: "isInPlace", label: "In-place", type: "bool", value: (f) => f.isInPlace },
  ],
  types: [
    { key: "typeName", label: "Type", type: "text", value: (t) => t.typeName },
    { key: "familyName", label: "Family", type: "text", value: (t) => t.familyName },
    { key: "category", label: "Category", type: "text", value: (t) => t.category },
    { key: "workset", label: "Workset", type: "text", value: (t) => t.workset },
    { key: "instanceCount", label: "Instances", type: "number", value: (t) => t.instanceCount },
  ],
};

export const OPERATORS: Record<FieldDef["type"], { value: RuleOperator; label: string }[]> = {
  text: [
    { value: "contains", label: "contains" },
    { value: "notContains", label: "does not contain" },
    { value: "equals", label: "equals" },
    { value: "notEquals", label: "not equals" },
    { value: "isEmpty", label: "is empty" },
  ],
  number: [
    { value: "equals", label: "=" },
    { value: "notEquals", label: "≠" },
    { value: "gt", label: ">" },
    { value: "lt", label: "<" },
    { value: "gte", label: "≥" },
    { value: "lte", label: "≤" },
  ],
  bool: [
    { value: "isTrue", label: "is yes" },
    { value: "isFalse", label: "is no" },
  ],
};

function fieldDef(scope: RuleScope, key: string): FieldDef | undefined {
  return FIELDS[scope].find((f) => f.key === key);
}

function evalCondition(scope: RuleScope, cond: RuleCondition, item: any): boolean {
  const def = fieldDef(scope, cond.field);
  if (!def) return true;
  const raw = def.value(item);

  switch (cond.op) {
    case "isEmpty":
      return raw === null || raw === undefined || String(raw).trim() === "";
    case "isTrue":
      return raw === true;
    case "isFalse":
      return raw === false;
  }

  if (def.type === "number") {
    const a = Number(raw);
    const b = Number(cond.value);
    if (Number.isNaN(b)) return true;
    switch (cond.op) {
      case "equals":
        return a === b;
      case "notEquals":
        return a !== b;
      case "gt":
        return a > b;
      case "lt":
        return a < b;
      case "gte":
        return a >= b;
      case "lte":
        return a <= b;
    }
  }

  const text = String(raw ?? "").toLowerCase();
  const needle = String(cond.value ?? "").toLowerCase();
  switch (cond.op) {
    case "contains":
      return text.includes(needle);
    case "notContains":
      return !text.includes(needle);
    case "equals":
      return text === needle;
    case "notEquals":
      return text !== needle;
  }
  return true;
}

/** True if an item passes a rule (all/any over its conditions). A rule with no conditions matches all. */
export function matchesRule(rule: FilterRule, item: any): boolean {
  if (!rule.conditions.length) return true;
  const results = rule.conditions.map((c) => evalCondition(rule.scope, c, item));
  return rule.match === "all" ? results.every(Boolean) : results.some(Boolean);
}

function load(): FilterRule[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as FilterRule[]) : [];
  } catch {
    return [];
  }
}

// Single reactive list shared across all views; persisted on every change.
const rules = reactive<FilterRule[]>(load());
watch(
  rules,
  () => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(rules));
    } catch {
      /* best-effort */
    }
  },
  { deep: true },
);

function newId(): string {
  return `rule_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 7)}`;
}

export function useFamilyRules() {
  function emptyRule(scope: RuleScope): FilterRule {
    return {
      id: newId(),
      name: "",
      scope,
      match: "all",
      pinned: false,
      conditions: [{ field: FIELDS[scope][0].key, op: "contains", value: "" }],
    };
  }

  function upsert(rule: FilterRule) {
    const idx = rules.findIndex((r) => r.id === rule.id);
    if (idx >= 0) rules[idx] = rule;
    else rules.push(rule);
  }

  function remove(id: string) {
    const idx = rules.findIndex((r) => r.id === id);
    if (idx >= 0) rules.splice(idx, 1);
  }

  return { rules, emptyRule, upsert, remove, matchesRule };
}
