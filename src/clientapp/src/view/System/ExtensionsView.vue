<script setup lang="ts">
/**
 * The extension manager.
 *
 * It used to be two tabs inside "Settings", which put a page you VISIT TO WORK — install, update,
 * create, delete — behind a door labelled with something you configure once. Splitting it out is the
 * whole point of this window: preferences live in Settings, extensions live here.
 *
 * One page, top to bottom by how often it is needed: what is installed, what is your own, what else
 * is available, and — collapsed — the plumbing (which folders are scanned, where saved commands
 * land). A row says nothing while all is well: its status column only speaks about a problem or an
 * update.
 */
// The parts live in ./extensions: the state and actions in useExtensionManager (one instance per
// window, shared through provide/inject), one component per section and dialog.
import { defineAsyncComponent, onMounted, ref } from "vue";
import Menu from "primevue/menu";
import { provideExtensionManager } from "./extensions/useExtensionManager";
import InstalledSection from "./extensions/InstalledSection.vue";
import OwnSection from "./extensions/OwnSection.vue";
import AvailableSection from "./extensions/AvailableSection.vue";
import FoldersPanel from "./extensions/FoldersPanel.vue";
import InstallConsentDialog from "./extensions/InstallConsentDialog.vue";
import RemoveExtensionDialog from "./extensions/RemoveExtensionDialog.vue";
import InstallSourceDialog from "./extensions/InstallSourceDialog.vue";

const EditExtensionDrawer = defineAsyncComponent(
  () => import("@/view/System/EditExtensionDrawer.vue"),
);

const { loading, reload, pickPackageAndAskConsent, askForSource, editDrawerVisible, editTargetId, afterEdit, init } =
  provideExtensionManager();

// One "Install" button with its two sources, instead of a file button in the header and a repository
// button hidden on another tab.
const installMenu = ref<InstanceType<typeof Menu> | null>(null);
const installMenuItems = [
  { label: "From a file (.zip)…", icon: "pi pi-file", command: () => pickPackageAndAskConsent() },
  { label: "From a repository…", icon: "pi pi-github", command: () => askForSource() },
];
function toggleInstallMenu(event: Event) {
  installMenu.value?.toggle(event);
}

onMounted(init);
</script>

<template>
  <div class="p-6">
    <div class="flex items-start justify-between gap-4 mb-4 flex-wrap">
      <div>
        <h1 class="text-xl font-bold">Extensions</h1>
        <p class="text-sm text-surface-500">
          Everything that adds commands, buttons and pages to AnalyseTool. New ones are made with
          <b>New</b> on the ribbon.
        </p>
      </div>
      <div class="flex flex-wrap gap-2 justify-end">
        <Button
          label="Install"
          icon="pi pi-download"
          severity="secondary"
          aria-haspopup="true"
          @click="toggleInstallMenu"
        />
        <Menu ref="installMenu" :model="installMenuItems" popup />
        <Button label="Reload" icon="pi pi-refresh" :loading="loading" @click="reload" />
      </div>
    </div>

    <InstalledSection />
    <OwnSection />
    <AvailableSection />
    <FoldersPanel />

    <InstallConsentDialog />
    <RemoveExtensionDialog />
    <InstallSourceDialog />
    <EditExtensionDrawer
      v-model:visible="editDrawerVisible"
      :extensionId="editTargetId"
      @saved="afterEdit"
    />
  </div>
</template>
