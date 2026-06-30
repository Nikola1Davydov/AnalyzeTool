// Shared transport/UI shapes for Family Control. These mirror the records returned by the built-in
// read commands (FamiliesService) so every component agrees on one definition instead of redeclaring it.

/** One row of the GetFamilies inventory. */
export interface FamilyRow {
  id: number;
  uniqueId: string;
  versionGuid: string;
  name: string;
  category: string;
  typeCount: number;
  instanceCount: number;
  isInPlace: boolean;
}

export interface FamilyInventory {
  count: number;
  returned: number;
  families: FamilyRow[];
}

/** One placed instance of a family (GetFamilyInstances). */
export interface FamilyInstanceDetail {
  id: number;
  typeName: string;
  typeId: number;
  category: string;
  level: string;
  worksetId: number;
  workset: string;
}

export interface FamilyInstancesResult {
  familyId: number;
  isWorkshared: boolean;
  count: number;
  returned: number;
  instances: FamilyInstanceDetail[];
}

/** One family TYPE (FamilySymbol) row for the Family Types tab (GetFamilyTypeRows). */
export interface TypeRow {
  familyId: number;
  familyName: string;
  typeId: number;
  typeName: string;
  category: string;
  instanceCount: number;
  worksets: string[];
  isSystem: boolean;
}

export interface TypeRowsResult {
  isWorkshared: boolean;
  count: number;
  rows: TypeRow[];
}

/**
 * A displayed Family Types row: one or more TypeRows merged because they share the same type name.
 * Carries the underlying type/family ids so actions (rename / delete / select / isolate / move workset)
 * apply to the whole group.
 */
export interface TypeGroup {
  key: string;
  familyName: string; // distinct family names in the group
  typeName: string;
  category: string; // distinct categories
  workset: string; // distinct worksets the instances sit on
  instanceCount: number;
  typeIds: number[];
  familyIds: number[];
  isSystem: boolean;
}

/** One user workset (GetWorksets). */
export interface WorksetInfo {
  id: number;
  name: string;
  isOpen: boolean;
  isEditable: boolean;
  owner: string;
}

export interface WorksetsResult {
  isWorkshared: boolean;
  worksets: WorksetInfo[];
}

// ---- Filter rules (saved in IndexedDB, applied to gallery / table / worksets) -------------------

/** Fields a rule can test. Family-scope fields read off FamilyRow; type-scope off the Family Types rows. */
export type RuleScope = "families" | "types";

export type RuleOperator =
  | "contains"
  | "notContains"
  | "equals"
  | "notEquals"
  | "gt"
  | "lt"
  | "gte"
  | "lte"
  | "isEmpty"
  | "isTrue"
  | "isFalse";

export interface RuleCondition {
  field: string;
  op: RuleOperator;
  value?: string;
}

export interface FilterRule {
  id: string;
  name: string;
  scope: RuleScope;
  match: "all" | "any"; // AND / OR across conditions
  pinned: boolean; // shown as a quick-navigation chip
  color?: string;
  conditions: RuleCondition[];
}
