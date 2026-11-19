// The wire contract of the built-in commands ported from AnalyseTool 1.x (Features/Elements, Features/Actions,
// Features/SharedParameters). Written by hand to match the records in C#: System.Text.Json with camelCase names,
// enums as strings, null written as null — except the properties marked optional here, which the C# side omits
// when null (JsonIgnoreCondition.WhenWritingNull), as 1.x did.
// Command names are the 1.x names, so a page of 1.x maps 1:1:
//   const elements = useCommand<ElementsResult>(toolCommands.GetElements, request);

/** Every ported command by name. */
export const toolCommands = {
  // Elements
  GetCadImports: "GetCadImports",
  GetCategoriesInRevit: "GetCategoriesInRevit",
  GetCategoryParameters: "GetCategoryParameters",
  GetDataByCategoryName: "GetDataByCategoryName",
  GetDocumentData: "GetDocumentData",
  GetElements: "GetElements",
  GetLinksInRevit: "GetLinksInRevit",
  GetModelOverview: "GetModelOverview",
  GetTypeParameters: "GetTypeParameters",
  GetViewsAndSheets: "GetViewsAndSheets",
  GetWarningsInRevit: "GetWarningsInRevit",
  GetWorksets: "GetWorksets",
  // Actions
  IsolationInRevit: "IsolationInRevit",
  SelectionInRevit: "SelectionInRevit",
  SetDataToParameters: "SetDataToParameters",
  // SharedParameters
  BindSharedParameters: "BindSharedParameters",
  GetBindingOptions: "GetBindingOptions",
  GetParameterReport: "GetParameterReport",
  GetParameterSets: "GetParameterSets",
  GetProjectParameters: "GetProjectParameters",
  GetSharedParameterFile: "GetSharedParameterFile",
  SaveParameterSets: "SaveParameterSets",
  SaveSharedParameterFile: "SaveSharedParameterFile",
  SetSharedParameterFile: "SetSharedParameterFile",
} as const;

export type ToolCommandName = keyof typeof toolCommands;

// ---- Shared -------------------------------------------------------------------------------------

export type ParameterOrigin = "Shared" | "Project" | "BuiltIn";

/** One parameter of an element (GetDataByCategoryName). */
export interface ParameterData {
  name: string;
  id: number;
  level: string;
  elementId: number;
  isTypeParameter: boolean;
  origin: ParameterOrigin;
  /** In the document's display unit, invariant culture. */
  value: string;
  /** "String" | "Integer" | "Double" | "ElementId" | "None" */
  storageType: string;
  isReadOnly: boolean;
}

/** A Revit warning a write transaction resolved on its own. */
export interface TransactionWarning {
  description: string;
  elementIds: number[];
}

// ---- Elements -----------------------------------------------------------------------------------

export interface ImportInfo {
  id: number;
  name: string;
}

export interface LinkInfo {
  id: number;
  name: string;
}

export interface LinksResult {
  revitLinks: LinkInfo[];
  cadLinks: LinkInfo[];
}

export interface GetCategoryParametersRequest {
  builtInCategory?: string | null;
  category?: string | null;
}

export interface CategoryParameterInfo {
  id: number;
  builtInParameter?: string;
  name: string;
  storageType: string;
  spec?: string;
  unit?: string;
  isReadOnly: boolean;
  isType: boolean;
}

export interface CategoryParametersResult {
  category: string | null;
  builtInCategory: string | null;
  count: number;
  parameters: CategoryParameterInfo[];
  error: string | null;
  didYouMean?: string[];
}

export interface GetDataByCategoryNameRequest {
  categoryName?: string;
}

export interface DataElement {
  name: string;
  id: number;
  level: string;
  isElementType: boolean;
  elementTypeId: number;
  categoryName: string | null;
  parameters: ParameterData[];
}

export interface DocumentData {
  name: string;
  /** The document's CreationGUID. */
  id: string;
}

export type ElementKind = "instances" | "types" | "all";

export interface GetElementsRequest {
  category?: string | null;
  builtInCategory?: string | null;
  elementKind?: ElementKind | null;
  parameterNames?: string[] | null;
  nameContains?: string | null;
  familyNameContains?: string | null;
  typeNameContains?: string | null;
  limit?: number | null;
}

export interface ElementItem {
  id: number;
  name: string;
  category: string;
  builtInCategory?: string;
  level: string;
  levelId?: number;
  isType: boolean;
  familyId?: number;
  familyName?: string;
  typeName?: string;
  parameters?: Record<string, string>;
}

export interface ElementsResult {
  category: string | null;
  elementKind: string;
  count: number;
  returned: number;
  elements: ElementItem[];
  error: string | null;
  didYouMean?: string[];
}

export interface LevelInfo {
  id: number;
  name: string;
  elevation: number;
}

export interface ActiveViewInfo {
  name: string;
  type: string;
  levelId: number | null;
}

export interface CategoryInstanceCount {
  name: string;
  builtInCategory: string;
  count: number;
}

export interface ModelOverview {
  title: string;
  revitVersion: string;
  language: string;
  displayUnits: string;
  isWorkshared: boolean;
  activeView: ActiveViewInfo | null;
  levels: LevelInfo[];
  /** Localised category name → instance count. */
  categoryCounts: Record<string, number>;
  categories: CategoryInstanceCount[];
  hasTopography: boolean;
}

export interface GetTypeParametersRequest {
  typeIds?: number[] | null;
}

export interface FamilyParameterInfo {
  name: string;
  value: string;
  id: number;
  builtInParameter?: string;
  spec?: string;
  unit?: string;
}

export interface TypeParametersInfo {
  typeId: number;
  parameters: FamilyParameterInfo[];
}

export interface TypeParametersResult {
  types: TypeParametersInfo[];
}

export interface ViewInfo {
  id: number;
  name: string;
  viewType: string;
  isOnSheet: boolean;
}

export interface SheetInfo {
  id: number;
  number: string;
  name: string;
}

export interface ViewsAndSheetsResult {
  views: ViewInfo[];
  sheets: SheetInfo[];
}

export interface WarningInRevitModel {
  warningDescription: string;
  failingElements: number[];
  additionalElements: number[];
}

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

// ---- Actions ------------------------------------------------------------------------------------

export interface IsolationInRevitRequest {
  elementIds?: number[] | null;
}

export interface IsolationResult {
  ok: boolean;
  isolated: number;
  error: string | null;
  warnings: TransactionWarning[];
}

export interface SelectionInRevitRequest {
  elementIds?: number[] | null;
}

export interface SelectionResult {
  ok: boolean;
  selected: number;
  error: string | null;
  ignoredIds?: number[];
}

export type SetDataMode = "Overwrite" | "OnlyIfEmpty" | "SkipIfEqual";

export interface SetParamItem {
  elementId: number;
  /** A BuiltInParameter value or a ParameterElement id (GetCategoryParameters → id). */
  id: number;
  value?: string | null;
}

export interface SetDataToParametersRequest {
  items: (SetParamItem | null)[];
  mode?: SetDataMode;
}

export interface WriteProblem {
  elementId: number;
  parameterId: number;
  reason: string;
}

export interface SetDataResult {
  ok: boolean;
  written: number;
  skipped: number;
  error: string | null;
  warnings: TransactionWarning[];
  problems?: WriteProblem[];
}

// ---- SharedParameters ---------------------------------------------------------------------------

export interface BindSharedParametersRequest {
  guids: string[];
  categoryIds?: number[] | null;
  isInstance?: boolean;
  groupTypeId?: string | null;
}

export interface BindProblemDto {
  name: string;
  reason: string;
}

export interface BindResult {
  added: string[];
  updated: string[];
  problems: BindProblemDto[];
}

export interface BindableCategoryDto {
  id: number;
  name: string;
  /** "Model" | "Annotation" */
  type: string;
}

export interface ParameterGroupOptionDto {
  typeId: string;
  label: string;
}

export interface BindingOptionsResult {
  categories: BindableCategoryDto[];
  groups: ParameterGroupOptionDto[];
  defaultGroup: string;
}

export interface ParameterRef {
  guid?: string | null;
  name?: string | null;
}

export interface GetParameterReportRequest {
  parameters?: ParameterRef[] | null;
  /** 1–100, default 10. */
  topValues?: number;
}

export interface ReportCountDto {
  name: string;
  total: number;
  filled: number;
}

export interface ReportValueDto {
  value: string;
  count: number;
}

export interface ParameterReportDto {
  name: string;
  guid?: string;
  isInstance: boolean;
  total: number;
  filled: number;
  categories: ReportCountDto[];
  levels: ReportCountDto[];
  values: ReportValueDto[];
  otherValuesCount: number;
  distinctValues: number;
  error?: string;
}

export interface ParameterReportResult {
  documentTitle: string;
  parameters: ParameterReportDto[];
}

export interface ParameterRefDto {
  guid?: string;
  name: string;
}

export interface ParameterSetDto {
  id: string;
  name: string;
  parameters: ParameterRefDto[];
  updated: string;
  projectId?: string;
  projectName?: string;
}

export interface ParameterSetsResult {
  path: string;
  sets: ParameterSetDto[];
  error?: string;
}

export interface SaveParameterSetsRequest {
  sets?: ParameterSetDto[] | null;
}

export interface ProjectParameterDto {
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

export interface ProjectParametersResult {
  parameters: ProjectParameterDto[];
}

export interface SharedParameterGroupDto {
  id: number;
  name: string;
}

export interface SharedParameterDto {
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
  groups: SharedParameterGroupDto[];
  parameters: SharedParameterDto[];
  error?: string;
}

export interface GetSharedParameterFileRequest {
  path?: string | null;
}

export interface SaveSharedParameterFileRequest {
  path: string;
  stamp?: string | null;
  groups?: SharedParameterGroupDto[] | null;
  parameters?: SharedParameterDto[] | null;
}

export interface SetSharedParameterFileRequest {
  path: string;
  create?: boolean;
}

// ---- The map ------------------------------------------------------------------------------------

/** Request and response of every command; `void` for a command without input. */
export interface ToolCommandTypes {
  GetCadImports: { request: void; response: ImportInfo[] };
  GetCategoriesInRevit: { request: void; response: string[] };
  GetCategoryParameters: { request: GetCategoryParametersRequest; response: CategoryParametersResult };
  GetDataByCategoryName: { request: GetDataByCategoryNameRequest; response: DataElement[] };
  GetDocumentData: { request: void; response: DocumentData };
  GetElements: { request: GetElementsRequest; response: ElementsResult };
  GetLinksInRevit: { request: void; response: LinksResult };
  GetModelOverview: { request: void; response: ModelOverview };
  GetTypeParameters: { request: GetTypeParametersRequest; response: TypeParametersResult };
  GetViewsAndSheets: { request: void; response: ViewsAndSheetsResult };
  GetWarningsInRevit: { request: void; response: WarningInRevitModel[] };
  GetWorksets: { request: void; response: WorksetsResult };
  IsolationInRevit: { request: IsolationInRevitRequest; response: IsolationResult };
  SelectionInRevit: { request: SelectionInRevitRequest; response: SelectionResult };
  SetDataToParameters: { request: SetDataToParametersRequest; response: SetDataResult };
  BindSharedParameters: { request: BindSharedParametersRequest; response: BindResult };
  GetBindingOptions: { request: void; response: BindingOptionsResult };
  GetParameterReport: { request: GetParameterReportRequest; response: ParameterReportResult };
  GetParameterSets: { request: void; response: ParameterSetsResult };
  GetProjectParameters: { request: void; response: ProjectParametersResult };
  GetSharedParameterFile: { request: GetSharedParameterFileRequest; response: SharedParameterFileData };
  SaveParameterSets: { request: SaveParameterSetsRequest; response: ParameterSetsResult };
  SaveSharedParameterFile: { request: SaveSharedParameterFileRequest; response: SharedParameterFileData };
  SetSharedParameterFile: { request: SetSharedParameterFileRequest; response: SharedParameterFileData };
}

export type ToolRequest<TName extends ToolCommandName> = ToolCommandTypes[TName]["request"];
export type ToolResponse<TName extends ToolCommandName> = ToolCommandTypes[TName]["response"];
