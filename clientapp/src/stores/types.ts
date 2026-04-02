export interface ElementItem {
  name: string; // element name
  id: number; // Revit element id
  level: string; // level name
  categoryName: string; // category name
  isElementType: boolean;
  parameters: ParameterData[];
}
export interface ParameterData {
  name: string; // parameter name
  id: number; // parameter id
  value: string; // parameter value
  level: string; // level name
  elementId: number; // Revit element id
  isTypeParameter: boolean;
  orgin: number;
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

export interface CombineRulePayload {
  kind: "parameter" | "text" | "number";
  value: string;
  order: number;
}

export interface ApplyCombinedParameterItem {
  elementId: number;
  oldValue: string;
  newValue: string;
}

export interface ApplyCombinedParametersPayload {
  categoryName: string;
  targetParameterName: string;
  mode: "Overwrite" | "OnlyIfEmpty" | "SkipIfEqual";
  rules: CombineRulePayload[];
  items: ParameterData[];
}
