export interface ElementItem {
  name: string; // element name
  id: number; // Revit element id
  level: string; // level name
  categoryName: string; // category name
  isElementType: boolean;
  elementTypeId: number;
  parameters: ParameterData[];
}

export const ParameterOrigin = {
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
  origin: (typeof ParameterOrigin)[keyof typeof ParameterOrigin];
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

export interface AnalyzeParameterWithAi {
  items: ParameterData[];
  prompt: string;
  model: string;
}

/** Wire shape of one AI-proposed edit (OllamaEditParameters → AiParameterEdit). camelCase since the
 *  command declares an output schema and the host spells these names out to match it. */
export interface ParameterEdit {
  elementId: number;
  parameter: string;
  oldValue: string;
  newValue: string;
  reason: string;
}
