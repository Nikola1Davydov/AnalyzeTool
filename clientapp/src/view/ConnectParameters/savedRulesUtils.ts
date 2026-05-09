import type { RuleBlock, RuleBlockKind, SavedRule } from "@/view/ConnectParameters/ruleTypes";

function isRuleBlockKind(kind: unknown): kind is RuleBlockKind {
  return kind === "parameter" || kind === "text" || kind === "number";
}

export function normalizeLoadedBlocks(rawBlocks: unknown): RuleBlock[] {
  if (!Array.isArray(rawBlocks)) return [];

  const result: RuleBlock[] = [];
  for (const raw of rawBlocks) {
    const kind = (raw as any)?.kind;
    if (!isRuleBlockKind(kind)) continue;

    result.push({
      id: Number((raw as any)?.id) || result.length + 1,
      kind,
      parameterName:
        typeof (raw as any)?.parameterName === "string" ? (raw as any).parameterName : null,
      literal: typeof (raw as any)?.literal === "string" ? (raw as any).literal : "",
    });
  }

  return result;
}

export function parseSavedRules<TMode extends string>(
  rawItems: unknown,
  allowedModes: readonly TMode[],
  defaultName: string,
): SavedRule<TMode>[] {
  if (!Array.isArray(rawItems)) return [];

  const modeSet = new Set<TMode>(allowedModes);

  return rawItems
    .map((x): SavedRule<TMode> | null => {
      const mode = (x as any)?.mode;
      if (!modeSet.has(mode)) return null;

      return {
        id: String((x as any)?.id || crypto.randomUUID()),
        name: String((x as any)?.name || defaultName),
        projectTag: typeof (x as any)?.projectTag === "string" ? (x as any).projectTag : null,
        groupTag: typeof (x as any)?.groupTag === "string" ? (x as any).groupTag : null,
        categoryName: typeof (x as any)?.categoryName === "string" ? (x as any).categoryName : null,
        targetParameterName:
          typeof (x as any)?.targetParameterName === "string"
            ? (x as any).targetParameterName
            : null,
        mode,
        blocks: normalizeLoadedBlocks((x as any)?.blocks),
        createdAt: String((x as any)?.createdAt || new Date().toISOString()),
        updatedAt: String((x as any)?.updatedAt || new Date().toISOString()),
      };
    })
    .filter(Boolean) as SavedRule<TMode>[];
}
