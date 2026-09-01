<script setup lang="ts">
/**
 * The "New" ribbon button's window: the create-extension form and nothing else.
 *
 * Its own window rather than the manager with a drawer over it — pressing "New" means "I want to
 * make one", and a list of everything already installed is noise behind that. When the form is done
 * the page closes its own window (window.close(); the host handles WindowCloseRequested), so the
 * whole thing behaves like a dialog: open, fill in, gone.
 */
import { ref } from "vue";
import { invoke } from "@/RevitBridge";
import CreateExtensionForm from "@/view/System/CreateExtensionForm.vue";

const createdDirectory = ref<string | null>(null);
const formKey = ref(0);

function closeWindow() {
  window.close();
}

async function onCreated(directory: string) {
  createdDirectory.value = directory;
  // Reload so the new folder is scanned and its button appears on the ribbon without a restart.
  try {
    await invoke("ReloadExtensions");
  } catch (e) {
    console.error("Reload failed", e);
  }
}

function openFolder() {
  if (!createdDirectory.value) return;
  invoke("OpenFolder", { path: createdDirectory.value }).catch((e) => console.error(e));
}

function createAnother() {
  createdDirectory.value = null;
  formKey.value++; // remount = a fresh form
}
</script>

<template>
  <div class="p-6 max-w-2xl mx-auto">
    <template v-if="!createdDirectory">
      <h1 class="text-xl font-bold">New extension</h1>
      <p class="text-sm text-surface-500 mb-5">
        A folder with a manifest and a starting point. It appears on the ribbon as soon as it is
        created; from there you edit the files.
      </p>
      <CreateExtensionForm :key="formKey" @created="onCreated" @cancel="closeWindow" />
    </template>

    <!-- Done: the folder is what you need next, so it is the whole screen. -->
    <template v-else>
      <div class="flex items-center gap-3 mb-2">
        <i class="pi pi-check-circle text-green-600 text-2xl" />
        <h1 class="text-xl font-bold">Created</h1>
      </div>
      <p class="text-sm text-surface-500 mb-1">Its button is on the ribbon. The files are here:</p>
      <p class="text-sm font-mono break-all mb-5">{{ createdDirectory }}</p>
      <div class="flex flex-wrap gap-2">
        <Button label="Open folder" icon="pi pi-folder-open" @click="openFolder" />
        <Button label="Create another" icon="pi pi-plus" severity="secondary" @click="createAnother" />
        <Button label="Close" severity="secondary" text @click="closeWindow" />
      </div>
    </template>
  </div>
</template>
