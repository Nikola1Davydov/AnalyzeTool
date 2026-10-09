// Shapes of the SharedParameters commands (AnalyseTool.Tools/SharedParameters), and the row the
// table shows: one parameter, wherever it lives — in the file, in the project, or both.

export interface SharedParameterGroup {
  id: number;
  name: string;
}

export interface SharedParameter {
  guid: string;
  name: string;
  dataType: string;
  dataCategory: string;
  groupId: number;
  visible: boolean;
  description: string;
  userModifiable: boolean;
  hideWhenNoValue: boolean;
}

export interface SharedParameterFileData {
  path: string | null;
  exists: boolean;
  isRevitCurrent: boolean;
  stamp: string;
  groups: SharedParameterGroup[];
  parameters: SharedParameter[];
  error?: string;
}

export interface ProjectParameter {
  id: number;
  guid?: string;
  name: string;
  isShared: boolean;
  bound: boolean;
  isInstance: boolean;
  dataType: string;
  group: string;
  categories: string[];
}

export interface BindingOptions {
  categories: { id: number; name: string; type: string }[];
  groups: { typeId: string; label: string }[];
  defaultGroup: string;
}

export interface BindResult {
  added: string[];
  updated: string[];
  problems: { name: string; reason: string }[];
}

/** One table row: a parameter of the file, of the project, or of both (matched by GUID). */
export interface ParameterRow {
  key: string;
  name: string;
  guid?: string;
  file?: SharedParameter;
  project?: ProjectParameter;
  groupName: string;
  dataType: string;
  description: string;
}

/** What the report needs to find a parameter in the project: GUID for shared, name for project ones. */
export interface ParameterRef {
  guid?: string;
  name: string;
}

/**
 * The data types a new parameter can have, as the file spells them. Revit writes these names for the
 * common specs; a parameter with another one (from a newer Revit, or FAMILYTYPE) keeps its own.
 */
export const DATA_TYPES: { value: string; label: string }[] = [
  { value: "TEXT", label: "Text" },
  { value: "MULTILINETEXT", label: "Multiline text" },
  { value: "INTEGER", label: "Integer" },
  { value: "NUMBER", label: "Number" },
  { value: "LENGTH", label: "Length" },
  { value: "AREA", label: "Area" },
  { value: "VOLUME", label: "Volume" },
  { value: "ANGLE", label: "Angle" },
  { value: "SLOPE", label: "Slope" },
  { value: "CURRENCY", label: "Currency" },
  { value: "MASS_DENSITY", label: "Mass density" },
  { value: "YESNO", label: "Yes/No" },
  { value: "URL", label: "URL" },
  { value: "MATERIAL", label: "Material" },
  { value: "IMAGE", label: "Image" },
];

export function dataTypeLabel(value: string): string {
  return DATA_TYPES.find((t) => t.value === value)?.label ?? value;
}

export function errorText(e: unknown): string {
  return String((e as Error)?.message ?? e);
}
