<script setup lang="ts">
/**
 * Third-party install consent: the backend requires consent=true, logged host-side (#48).
 */
import { useExtensionManager } from "./useExtensionManager";

const {
  loading,
  installDialogVisible,
  installBusy,
  installError,
  installOrigin,
  installOverwrite,
  installSubject,
  confirmInstall,
} = useExtensionManager();
</script>

<template>
  <!-- Third-party install consent: the backend requires consent=true, logged host-side (#48). -->
  <Dialog
    v-model:visible="installDialogVisible"
    modal
    header="Install third-party extension"
    class="w-[34rem]"
    :closable="!installBusy"
    :closeOnEscape="!installBusy"
  >
    <div class="text-sm flex flex-col gap-3">
      <div class="break-all text-surface-500 font-mono text-xs">{{ installSubject }}</div>
      <p>
        This package contains <b>third-party code</b> that will run inside Revit with full access
        to your models and machine. Its <b>publisher is responsible</b> for what it does —
        AnalyseTool does not review, endorse or guarantee third-party extensions. Install only if
        you trust the source.
      </p>
      <p v-if="installOrigin?.kind === 'source'" class="text-xs text-surface-500">
        The package is downloaded from the publisher's own release, not from AnalyseTool.
      </p>
      <p v-if="installError" class="text-red-500">{{ installError }}</p>
    </div>
    <template #footer>
      <Button
        label="Cancel"
        text
        severity="secondary"
        :disabled="installBusy"
        @click="installDialogVisible = false"
      />
      <Button
        :label="installOverwrite ? 'Replace installed version' : 'I trust it — install'"
        :severity="installOverwrite ? 'danger' : undefined"
        :loading="installBusy"
        @click="confirmInstall"
      />
    </template>
  </Dialog>
</template>
