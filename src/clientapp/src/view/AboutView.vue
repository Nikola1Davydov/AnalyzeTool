<script setup lang="ts">
/**
 * About — what this is: the versions, what changed, where the code lives.
 *
 * Its own window (the "About" ribbon button) and a page of the main window's sidebar. It used to be
 * the bottom block of Settings, where it was found only by scrolling past switches; Settings is now
 * only what you change.
 */
import { onMounted, ref, watch } from "vue";
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
// One fold per release, split on the "## [1.5.2] / 2026-09-14" headings: the whole file as one page
// was a wall nobody scrolls. The newest release is open; the rest show only version, date and size.
interface Release {
  version: string;
  date: string | null;
  count: number; // top-level bullets — how big the release was, before opening it
  html: string;
}
const releases = ref<Release[] | null>(null);
const changelogError = ref<string | null>(null);

async function loadChangelog() {
  try {
    const res = await invoke<{ markdown: string | null; error: string | null }>("GetChangelog");
    if (res?.markdown) {
      const { marked } = await import("marked"); // lazy — its own chunk
      const parts = res.markdown.split(/^## /m).slice(1); // [0] is the "# Changelog" title
      releases.value = await Promise.all(
        parts.map(async (part) => {
          const newline = part.indexOf("\n");
          const heading = (newline < 0 ? part : part.slice(0, newline)).trim();
          const body = newline < 0 ? "" : part.slice(newline + 1);
          const [version, date] = heading.split("/").map((s) => s.trim().replace(/^\[|\]$/g, ""));
          return {
            version: version || heading,
            date: date || null,
            count: (body.match(/^- /gm) ?? []).length,
            html: await marked.parse(body),
          };
        }),
      );
    } else {
      changelogError.value = res?.error ?? "Changelog not available.";
    }
  } catch (e) {
    changelogError.value = errorText(e);
  }
}

// Fetched on the first expand, not on open — nobody reads it while it is folded.
const changelogCollapsed = ref(true);
watch(changelogCollapsed, (collapsed) => {
  if (!collapsed && !releases.value && !changelogError.value) loadChangelog();
});

onMounted(loadEnvironment);
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

    <!-- Collapsed: the changelog is long, and most visits to About are for the version. -->
    <Panel toggleable v-model:collapsed="changelogCollapsed" class="mb-4">
      <template #header>
        <span class="text-base font-bold">What's new</span>
      </template>
      <div v-if="changelogError" class="text-sm text-red-600">{{ changelogError }}</div>
      <div v-else-if="!releases" class="text-surface-500 text-sm p-4 text-center">
        <i class="pi pi-spin pi-spinner mr-2" />Loading…
      </div>
      <div v-else class="flex flex-col gap-2">
        <details
          v-for="(release, i) in releases"
          :key="release.version"
          :open="i === 0"
          class="release rounded-lg border border-surface-200"
        >
          <summary class="cursor-pointer select-none px-3 py-2 flex items-center gap-2 text-sm">
            <i class="release-chevron pi pi-chevron-right text-xs text-surface-400" />
            <span class="font-semibold">{{ release.version }}</span>
            <span v-if="release.date" class="text-surface-500">{{ release.date }}</span>
            <span class="ml-auto text-xs text-surface-400">
              {{ release.count }} {{ release.count === 1 ? "change" : "changes" }}
            </span>
          </summary>
          <div class="changelog-body px-3 pb-2" v-html="release.html" />
        </details>
      </div>
    </Panel>
  </div>
</template>

<style scoped>
/* A release fold: our own chevron instead of the browser's triangle, turned when open. */
.release > summary {
  list-style: none;
}
.release > summary::-webkit-details-marker {
  display: none;
}
.release-chevron {
  transition: transform 0.15s;
}
.release[open] .release-chevron {
  transform: rotate(90deg);
}

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
