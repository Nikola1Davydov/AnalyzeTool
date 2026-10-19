<script setup lang="ts">
/**
 * What the dockable pane shows when no extension page is docked — e.g. when Revit restores the pane
 * at startup. The pane hosts extension pages only (manifest ui.dockable); nothing of the plugin's own
 * lives here.
 */
</script>

<template>
  <div class="h-full flex flex-col items-center justify-center gap-2 p-6 text-center text-surface-500">
    <i class="pi pi-th-large text-2xl text-surface-300" />
    <p class="text-sm">Nothing docked.</p>
    <p class="text-xs max-w-64">
      Extensions that open in the dock show their page here when you click their ribbon button.
    </p>
  </div>
</template>
