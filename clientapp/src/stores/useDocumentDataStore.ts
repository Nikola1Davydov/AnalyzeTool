import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { Commands, sendRequest } from "@/RevitBridge";

export interface DocumentData {
  name: string;
  id: string;
}

export const useDocumentDataStore = defineStore("documentData", () => {
  const data = ref<DocumentData | null>(null);

  const hasData = computed(() => data.value !== null);
  const documentName = computed(() => data.value?.name || "");
  const documentId = computed(() => data.value?.id || "");

  async function loadDocumentData(): Promise<void> {
    sendRequest(Commands.GetDocumentData, null).catch((err) => {
      console.error("Failed to request document data", err);
    });
  }

  function setDocumentData(payload: DocumentData | null): void {
    data.value = payload;
  }

  function clear(): void {
    data.value = null;
  }

  return {
    data,
    hasData,
    documentName,
    documentId,
    loadDocumentData,
    setDocumentData,
    clear,
  };
});
