<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref, watch } from "vue";
import { sendRequest, Commands } from "@/RevitBridge";
import type { KeyValuePair } from "@/stores/types";
import { useDocumentHealthStore } from "@/stores/useDocumentHealthStore";
import type { DocumentHealthPayload } from "@/stores/useDocumentHealthStore";

const WarningCard = defineAsyncComponent(() => import("@/components/WarningCard.vue") as any);

const loading = ref(true);
const store = useDocumentHealthStore();

// Load data
onMounted(async () => {
  store.loadDocumentHealth();
  loading.value = false;
});

const health = computed(() => store.health);
const format = (item?: KeyValuePair) => (item ? `${item.value}` : "–");
</script>

<template>
  <div class="p-6 space-y-6">
    <!-- Header -->
    <div>
      <h1 class="text-2xl font-bold">Revit Document Health</h1>
      <p class="text-sm text-surface-500">Technical overview of model complexity and quality</p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="text-surface-400">Loading document health…</div>

    <!-- Dashboard -->
    <div v-else-if="health" class="space-y-8">
      <!-- WARNINGS -->
      <section>
        <h2 class="text-lg font-semibold mb-3">Warnings & Size</h2>
        <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
          <WarningCard title="Total Warnings" :message="format(health.totalWarnings)" />
          <WarningCard title="File Size (MB)" :message="format(health.fileSize)" />
          <WarningCard title="Placed Elements" :message="format(health.totalPlacedElements)" />
        </div>
      </section>

      <!-- MODELS -->
      <section>
        <h2 class="text-lg font-semibold mb-3">Model Structure</h2>
        <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
          <WarningCard title="Model Groups" :message="format(health.modelGroups)" />
          <WarningCard title="Detail Groups" :message="format(health.detailGroups)" />
          <WarningCard title="In-Place Families" :message="format(health.inPlaceFamilies)" />
        </div>
      </section>

      <!-- VIEWS -->
      <section>
        <h2 class="text-lg font-semibold mb-3">Views & Sheets</h2>
        <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
          <WarningCard title="Total Views" :message="format(health.totalViews)" />
          <WarningCard title="Hidden Elements" :message="format(health.hiddenElementsInViews)" />
          <WarningCard title="Views not on Sheets" :message="format(health.viewsNotOnSheets)" />
          <WarningCard title="Sheets" :message="format(health.sheets)" />
        </div>
      </section>

      <!-- LINKS -->
      <section>
        <h2 class="text-lg font-semibold mb-3">Links</h2>
        <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
          <WarningCard title="Revit Links" :message="format(health.revitLinks)" />
          <WarningCard title="CAD Links" :message="format(health.cadLinks)" />
        </div>
      </section>

      <!-- IMPORTS -->
      <section>
        <h2 class="text-lg font-semibold mb-3">Imports</h2>
        <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
          <WarningCard title="CAD Imports" :message="format(health.cadImports)" />
        </div>
      </section>
    </div>
  </div>
</template>
