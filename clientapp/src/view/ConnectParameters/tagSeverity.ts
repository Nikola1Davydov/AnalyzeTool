export type TagSeverity = "secondary" | "success" | "info" | "warn" | "danger" | "contrast";

function normalizeStorageType(storageType: string | null | undefined): string {
  return (storageType || "").trim().toLowerCase();
}

export function getScopeTagSeverity(scope: string | null | undefined): TagSeverity {
  if (scope === "Type") return "warn";
  if (scope === "Instance") return "info";
  return "secondary";
}

export function getValueTypeTagSeverity(valueType: string | null | undefined): TagSeverity {
  if (valueType === "Number") return "success";
  if (valueType === "String") return "contrast";
  return "secondary";
}

export function getStorageTypeTagSeverity(storageType: string | null | undefined): TagSeverity {
  const normalized = normalizeStorageType(storageType);
  if (!normalized) return "secondary";
  if (normalized.includes("string") || normalized.includes("text")) return "contrast";
  if (
    normalized.includes("double") ||
    normalized.includes("int") ||
    normalized.includes("integer") ||
    normalized.includes("number")
  ) {
    return "secondary";
  }
  return "secondary";
}
