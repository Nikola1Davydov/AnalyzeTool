export type RuleBlockKind = "parameter" | "text" | "number";

export type RuleBlock = {
  id: number;
  kind: RuleBlockKind;
  parameterName: string | null;
  literal: string;
};

export type SavedRule<TMode = string> = {
  id: string;
  name: string;
  projectTag: string | null;
  groupTag: string | null;
  categoryName: string | null;
  targetParameterName: string | null;
  mode: TMode;
  blocks: RuleBlock[];
  createdAt: string;
  updatedAt: string;
};
