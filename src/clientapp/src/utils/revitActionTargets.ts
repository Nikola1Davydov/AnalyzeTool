import type { ElementItem, ParameterData } from "@/stores/types";

export type RevitActionMatch = {
  element: ElementItem;
  parameter?: ParameterData | null;
};

function toPositiveNumber(value: unknown): number {
  const next = Number(value);
  return Number.isFinite(next) && next > 0 ? next : 0;
}

function buildElementIndexes(items: ElementItem[]) {
  const elementsById = new Map<number, ElementItem>();
  const instanceIdsByTypeId = new Map<number, number[]>();

  for (const item of items || []) {
    const elementId = toPositiveNumber(item?.id);
    if (!elementId) continue;

    elementsById.set(elementId, item);

    if (item?.isElementType) continue;

    const typeId = toPositiveNumber(item?.elementTypeId);
    if (!typeId) continue;

    if (!instanceIdsByTypeId.has(typeId)) instanceIdsByTypeId.set(typeId, []);
    instanceIdsByTypeId.get(typeId)!.push(elementId);
  }

  return { elementsById, instanceIdsByTypeId };
}

export function resolveInstanceActionElementIds(
  items: ElementItem[],
  matches: RevitActionMatch[],
): number[] {
  const { elementsById, instanceIdsByTypeId } = buildElementIndexes(items);
  const resolvedIds = new Set<number>();

  const addInstancesForType = (typeId: number) => {
    for (const instanceId of instanceIdsByTypeId.get(typeId) || []) {
      resolvedIds.add(instanceId);
    }
  };

  for (const match of matches || []) {
    const element = match?.element;
    const parameter = match?.parameter;
    if (!element) continue;

    const elementId = toPositiveNumber(element.id);
    const parameterElementId = toPositiveNumber(parameter?.elementId);
    const isTypeParameter = Boolean(parameter?.isTypeParameter);
    const isTypeElement = Boolean(element.isElementType);

    if (isTypeParameter || isTypeElement) {
      const typeId = isTypeElement
        ? elementId || parameterElementId
        : toPositiveNumber(element.elementTypeId) || parameterElementId;

      if (typeId) {
        const sizeBefore = resolvedIds.size;
        addInstancesForType(typeId);
        if (resolvedIds.size > sizeBefore) continue;
      }

      if (!isTypeElement && elementId) resolvedIds.add(elementId);
      continue;
    }

    const instanceId = parameterElementId || elementId;
    if (!instanceId) continue;

    const knownElement = elementsById.get(instanceId);
    if (!knownElement || !knownElement.isElementType) {
      resolvedIds.add(instanceId);
      continue;
    }

    addInstancesForType(instanceId);
  }

  return Array.from(resolvedIds);
}
