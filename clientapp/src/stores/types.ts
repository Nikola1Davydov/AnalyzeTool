export interface ElementItem {
  name: string; // element name
  id: number; // Revit element id
  level: string; // level name
  categoryName: string; // category name
  isElementType: boolean;
  elementTypeId: number;
  parameters: ParameterData[];
}

export const ParameterOrgin = {
  Shared: "Shared",
  Project: "Project",
  BuiltIn: "BuiltIn",
} as const;

export interface ParameterData {
  name: string; // parameter name
  id: number; // parameter id
  value: string; // parameter value
  level: string; // level name
  elementId: number; // Revit element id
  isTypeParameter: boolean;
  orgin: (typeof ParameterOrgin)[keyof typeof ParameterOrgin];
  storageType: string;
  isReadOnly: boolean;
}
export interface UpdatePayload {
  currentVersion?: string;
  latestVersion?: string;
  isUpdateAvailable?: boolean;
  releaseUrl?: string;
}

export interface KeyValuePair {
  key: string;
  value: number;
}

export const SetDataToParametersModes = {
  Overwrite: "Overwrite",
  OnlyIfEmpty: "OnlyIfEmpty",
  SkipIfEqual: "SkipIfEqual",
} as const;

export interface SetDataToParameters {
  items: ParameterData[];
  mode: (typeof SetDataToParametersModes)[keyof typeof SetDataToParametersModes];
}
