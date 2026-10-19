// Shapes of the SharedParameters commands (AnalyseTool.Tools/SharedParameters), and the row the
// table shows: one parameter, wherever it lives — in the file, in the project, or both. The command
// shapes are generated from the C# records (src/generated/commands.ts, written by AnalyseTool.TsGen);
// the aliases keep the names the views use.
import type {
  BindingOptionsResult,
  BindResult,
  ParameterRefDto,
  ProjectParameterDto,
  SharedParameterDto,
  SharedParameterFileData,
  SharedParameterGroupDto,
} from "@/generated/commands";

export type { BindResult, SharedParameterFileData };
export type SharedParameterGroup = SharedParameterGroupDto;
export type SharedParameter = SharedParameterDto;
export type ProjectParameter = ProjectParameterDto;
export type BindingOptions = BindingOptionsResult;

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
export type ParameterRef = ParameterRefDto;

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
