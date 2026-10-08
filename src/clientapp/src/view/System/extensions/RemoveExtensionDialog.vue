<script setup lang="ts">
/**
 * Delete / uninstall confirmation, for both zones.
 */
import { useExtensionManager } from "./useExtensionManager";

const {
  loading,
  removeDialogVisible,
  removeBusy,
  removeError,
  removeTarget,
  confirmRemove,
} = useExtensionManager();
</script>

<template>
  <!-- Delete confirmation, for both zones. -->
  <Dialog
    v-model:visible="removeDialogVisible"
    modal
    :header="removeTarget?.zone === 'dev' ? 'Delete extension' : 'Uninstall extension'"
    class="w-[28rem]"
  >
    <div class="text-sm flex flex-col gap-3">
      <p>
        Remove <b>{{ removeTarget?.name || removeTarget?.id }}</b> and delete its folder? This
        cannot be undone.
      </p>
      <!-- The path, for dev folders only. An installed package sits where the manager put it; one of
           your own could be anywhere, including a folder you share with your team. -->
      <p v-if="removeTarget?.zone === 'dev'" class="text-xs text-surface-500 break-all font-mono">
        {{ removeTarget?.directory }}
      </p>
      <!-- Two different losses, said differently. A saved command keeps its sources IN this folder
           (src), so they go with it; a project someone builds keeps them elsewhere. -->
      <p v-if="removeTarget?.zone === 'dev' && removeTarget?.hostBuilt" class="text-red-600">
        This is a saved command — its C# sources are in this folder and are deleted with it.
      </p>
      <p
        v-else-if="removeTarget?.zone === 'dev' && removeTarget?.kind === 'dll'"
        class="text-amber-600"
      >
        This is a compiled extension — its source project is somewhere else, but the built output
        here goes.
      </p>
      <p v-if="removeError" class="text-red-500">{{ removeError }}</p>
    </div>
    <template #footer>
      <Button
        label="Cancel"
        text
        severity="secondary"
        :disabled="removeBusy"
        @click="removeDialogVisible = false"
      />
      <Button
        :label="removeTarget?.zone === 'dev' ? 'Delete' : 'Uninstall'"
        severity="danger"
        :loading="removeBusy"
        @click="confirmRemove"
      />
    </template>
  </Dialog>
</template>
