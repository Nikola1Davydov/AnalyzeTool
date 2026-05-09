import { defineStore } from "pinia";
import type { KeyValuePair } from "@/stores/types";
import { Commands, sendRequest } from "@/RevitBridge";
import { ref } from "vue";

export interface DocumentHealthPayload {
  // warnings
  totalWarnings: KeyValuePair;
  fileSize: KeyValuePair;
  totalPlacedElements: KeyValuePair;

  // models
  modelGroups: KeyValuePair;
  detailGroups: KeyValuePair;
  inPlaceFamilies: KeyValuePair;

  // views and sheets
  totalViews: KeyValuePair;
  hiddenElementsInViews: KeyValuePair;
  viewsNotOnSheets: KeyValuePair;
  sheets: KeyValuePair;

  // links
  revitLinks: KeyValuePair;
  cadLinks: KeyValuePair;

  // imports
  cadImports: KeyValuePair;
}

export const useDocumentHealthStore = defineStore("documentHealth", () => {
  const health = ref<DocumentHealthPayload | null>(null);

  const setHealth = (payload: DocumentHealthPayload) => {
    console.log("Setting document health:", payload);
    health.value = payload;
  };

  function loadDocumentHealth(): void {
    sendRequest(Commands.GetDocumentHealthStatus, null);
  }

  const reset = () => {
    health.value = null;
  };

  return {
    health,
    setHealth,
    loadDocumentHealth,
    reset,
  };
});
