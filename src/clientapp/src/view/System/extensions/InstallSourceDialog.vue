<script setup lang="ts">
/**
 * Install from a repository the user names: anything not in the catalog.
 */
import { useExtensionManager } from "./useExtensionManager";

const {
  sourceDialogVisible,
  sourceInput,
  proceedWithSource,
} = useExtensionManager();
</script>

<template>
  <!-- Install from a repository the user names: anything not in the catalog. -->
  <Dialog
    v-model:visible="sourceDialogVisible"
    modal
    header="Install from a repository"
    class="w-[34rem]"
  >
    <div class="text-sm flex flex-col gap-3">
      <p class="text-surface-600">
        Paste the repository of the extension. What gets installed is the package attached to its
        latest release.
      </p>
      <InputText
        v-model="sourceInput"
        placeholder="https://github.com/owner/repo"
        class="w-full"
        autofocus
        @keyup.enter="proceedWithSource"
      />
      <p class="text-xs text-surface-500">
        Accepted: a GitHub repository URL, <span class="font-mono">owner/repo</span>,
        <span class="font-mono">github:owner/repo</span>, or an https URL returning
        <span class="font-mono">version</span> and
        <span class="font-mono">downloadUrl</span>.
      </p>
    </div>
    <template #footer>
      <Button label="Cancel" text severity="secondary" @click="sourceDialogVisible = false" />
      <Button label="Continue" :disabled="!sourceInput.trim()" @click="proceedWithSource" />
    </template>
  </Dialog>
</template>
