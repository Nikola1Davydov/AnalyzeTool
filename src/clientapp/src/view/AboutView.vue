<script setup lang="ts">
/**
 * About — what this is: the versions, what changed, where the code lives.
 *
 * Its own window (the "About" ribbon button) and a page of the main window's sidebar. It used to be
 * the bottom block of Settings, where it was found only by scrolling past switches; Settings is now
 * only what you change.
 */
import { onMounted, ref } from "vue";
import { storeToRefs } from "pinia";
import { invoke } from "@/RevitBridge";
import { useUpdateStore } from "@/stores/useUpdateStore";
import { useNotificationStore } from "@/stores/useNotificationStore";

const notifications = useNotificationStore();

const REPO_URL = "https://github.com/Nikola1Davydov/AnalyzeTool";
/** Where "Report a bug" goes — the same issues page the ribbon button opens. */
const ISSUES_URL = `${REPO_URL}/issues`;

function errorText(e: unknown): string {
  return String((e as Error)?.message ?? e);
}

// --- The host facts. Same command the extension manager uses; we only read its header. -----------
interface EnvironmentData {
  hostRevit: string;
  hostSdkVersion: string;
  pluginVersion: string;
  /** The user's own extensions folder (created on first open). */
  devRoot: string;
}
const env = ref<EnvironmentData | null>(null);

async function loadEnvironment() {
  try {
    env.value = await invoke<EnvironmentData>("GetInstalledExtensions");
  } catch (e) {
    console.error("Failed to load environment info", e);
  }
}

// Same update check the main AnalyseTool window uses (CheckUpdate). No loadUpdateData() here: App.vue
// already runs it for every window.
const { updateInfo } = storeToRefs(useUpdateStore());

function openFolder(path: string | undefined) {
  if (!path) return;
  invoke("OpenFolder", { path }).catch((e) => notifications.error(errorText(e)));
}

// --- What's new: CHANGELOG.md ships next to the plugin DLL, rendered as markdown. -----------------
const changelogHtml = ref<string | null>(null);
const changelogError = ref<string | null>(null);

async function loadChangelog() {
  try {
    const res = await invoke<{ markdown: string | null; error: string | null }>("GetChangelog");
    if (res?.markdown) {
      const { marked } = await import("marked"); // lazy — its own chunk
      changelogHtml.value = await marked.parse(res.markdown);
    } else {
      changelogError.value = res?.error ?? "Changelog not available.";
    }
  } catch (e) {
    changelogError.value = errorText(e);
  }
}

onMounted(() => {
  loadEnvironment();
  loadChangelog();
});
</script>

<template>
  <div class="p-6 max-w-3xl mx-auto">
    <h1 class="text-xl font-bold">About AnalyseTool</h1>
    <p class="text-sm text-surface-500 mb-6">
      An open-source Revit add-in: collect, analyze and edit parameters, and add your own commands
      and pages as extensions — or let your AI write them over MCP.
    </p>

    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-4">
      <div class="grid grid-cols-3 gap-3 text-sm">
        <div>
          <div class="text-surface-500 text-xs">Plugin version</div>
          <div class="flex items-center gap-2 flex-wrap">
            <span>{{ env?.pluginVersion ?? "—" }}</span>
            <template v-if="updateInfo?.isUpdateAvailable">
              <span
                class="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full text-white"
                :style="{ background: 'var(--p-primary-color)' }"
              >
                <i class="pi pi-arrow-up text-[10px]" />
                v{{ updateInfo.latestVersion }}
              </span>
              <a
                v-if="updateInfo.releaseUrl"
                :href="updateInfo.releaseUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="text-primary-600 underline font-semibold text-xs"
              >
                Download
              </a>
            </template>
          </div>
        </div>
        <div>
          <div class="text-surface-500 text-xs">Revit</div>
          <div>{{ env?.hostRevit ?? "—" }}</div>
        </div>
        <div>
          <div class="text-surface-500 text-xs">SDK version</div>
          <div>{{ env?.hostSdkVersion ?? "—" }}</div>
        </div>
      </div>

      <div class="flex flex-wrap items-center gap-3 mt-4">
        <a
          :href="REPO_URL"
          target="_blank"
          rel="noopener noreferrer"
          class="text-sm text-primary-600 underline inline-flex items-center gap-1"
        >
          <i class="pi pi-github text-xs" />Source code
        </a>
        <a
          :href="ISSUES_URL"
          target="_blank"
          rel="noopener noreferrer"
          class="text-sm text-primary-600 underline inline-flex items-center gap-1"
        >
          <i class="pi pi-comment text-xs" />Report a bug or an idea
        </a>
        <Button
          label="Extensions folder"
          icon="pi pi-folder-open"
          size="small"
          text
          severity="secondary"
          :disabled="!env?.devRoot"
          v-tooltip.top="env?.devRoot"
          @click="openFolder(env?.devRoot)"
        />
      </div>
      <p class="text-xs text-surface-500 mt-3">
        Pull requests, issues and ideas are welcome — even small improvements count.
      </p>
    </section>

    <section class="rounded-xl border border-surface-200 bg-surface-0 p-4 mb-4">
      <h2 class="text-base font-bold mb-3">What's new</h2>
      <div v-if="changelogError" class="text-sm text-red-600">{{ changelogError }}</div>
      <div v-else-if="!changelogHtml" class="text-surface-500 text-sm p-4 text-center">
        <i class="pi pi-spin pi-spinner mr-2" />Loading…
      </div>
      <div v-else class="changelog-body" v-html="changelogHtml" />
    </section>
  </div>
</template>

<style scoped>
/* Minimal markdown styling for the changelog (marked outputs plain h2/ul/li/p). */
.changelog-body :deep(h2) {
  font-size: 1rem;
  font-weight: 700;
  margin: 1rem 0 0.5rem;
  padding-bottom: 0.25rem;
  border-bottom: 1px solid var(--p-surface-200);
}
.changelog-body :deep(h2:first-child) {
  margin-top: 0;
}
.changelog-body :deep(h1) {
  display: none; /* the section heading already says what this is */
}
.changelog-body :deep(ul) {
  list-style: disc;
  padding-left: 1.25rem;
  margin: 0.25rem 0 0.75rem;
}
.changelog-body :deep(ul ul) {
  list-style: circle;
  margin: 0.125rem 0;
}
.changelog-body :deep(li) {
  font-size: 0.875rem;
  margin: 0.125rem 0;
}
.changelog-body :deep(p) {
  font-size: 0.875rem;
  margin: 0.375rem 0;
}
.changelog-body :deep(code) {
  background: var(--p-surface-100);
  border-radius: 0.25rem;
  padding: 0 0.25rem;
  font-size: 0.8em;
}
</style>
