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
}

export enum RevitCommand {
  GetCategories = "GetCategories",
  UpdateDataParameterFilledEmptyPage = "UpdateDataParameterFilledEmptyPage",
  Isolation = "Isolation",
  Selection = "Selection",
}
