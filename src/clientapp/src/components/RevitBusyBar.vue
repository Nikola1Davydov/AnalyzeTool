<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from "vue";
import { invoke } from "@/RevitBridge";

// Bottom status strip shown in EVERY window while the platform is busy. Answers the two "why is
// nothing happening?" cases:
//  • a command is running (long export, purge, AI call…) — show what and for how long;
//  • Revit can't execute queued work at all (user sits in a modal dialog / edit mode) — say so.
// The host pushes "QueueChanged" events on start/finish; while busy we additionally poll
// GetQueueStatus (which deliberately never touches the Revit thread, so it answers even when Revit
// is blocked). Quick commands never show up: the bar appears only after MIN_VISIBLE_SECONDS.

interface QueueStatus {
  running: { command: string; source: string; seconds: number }[];
  pendingRevitWork: number;
  waitingSeconds: number;
  waitingForUser: boolean;
}

const MIN_VISIBLE_SECONDS = 2; // don't flicker on fast commands
const POLL_BUSY_MS = 2000;
// Idle polling keeps the PROACTIVE warning armed: the host detects a blocked Revit via the Idling
// stamp (RevitDBExplorer technique) within ~a second, but that state only reaches us by asking.
// GetQueueStatus never touches the Revit thread, so this is cheap even while Revit is blocked.
const POLL_IDLE_MS = 2000;

const status = ref<QueueStatus | null>(null);
let pollTimer: number | null = null;

const busy = computed(() => (status.value?.running.length ?? 0) > 0 || (status.value?.pendingRevitWork ?? 0) > 0);
const longest = computed(() =>
  status.value?.running.reduce((max, r) => (r.seconds > (max?.seconds ?? -1) ? r : max), null as null | QueueStatus["running"][number]),
);
const visible = computed(
  () => !!status.value && (status.value.waitingForUser || (longest.value?.seconds ?? 0) >= MIN_VISIBLE_SECONDS),
);

async function refresh() {
  try {
    status.value = await invoke<QueueStatus>("GetQueueStatus");
  } catch {
    status.value = null; // transport gone (window closing) — hide
  }
  schedule();
}

function schedule() {
  if (pollTimer !== null) {
    clearTimeout(pollTimer);
    pollTimer = null;
  }
  pollTimer = window.setTimeout(refresh, busy.value ? POLL_BUSY_MS : POLL_IDLE_MS);
}

function onQueueChanged(e: Event) {
  const detail = (e as CustomEvent).detail as QueueStatus | undefined;
  if (detail) {
    status.value = detail;
    schedule();
  } else void refresh();
}

onMounted(() => {
  window.addEventListener("at:QueueChanged", onQueueChanged);
  void refresh(); // a window may open while a command started elsewhere is already running
});
onUnmounted(() => {
  window.removeEventListener("at:QueueChanged", onQueueChanged);
  if (pollTimer !== null) clearTimeout(pollTimer);
});
</script>

<template>
  <Transition name="busybar">
    <div
      v-if="visible"
      class="fixed bottom-0 inset-x-0 z-50 flex items-center gap-2 px-3 py-1.5 text-xs border-t shadow-lg"
      :class="
        status!.waitingForUser
          ? 'bg-amber-50 border-amber-300 text-amber-800'
          : 'bg-surface-0 border-surface-200 text-surface-600'
      "
    >
      <template v-if="status!.waitingForUser">
        <i class="pi pi-exclamation-triangle text-amber-500 shrink-0" />
        <!-- Proactive case: nothing of ours is queued by the user yet — Revit itself is held. -->
        <span v-if="!longest" class="truncate">
          <b>Revit is busy with another action</b> — finish the active Revit command or close the
          open dialog before using AnalyseTool.
        </span>
        <span v-else class="truncate">
          <b>Revit is waiting for you</b> — close the open dialog or finish the edit mode to let
          “{{ longest.command }}” run<template v-if="status!.waitingSeconds >= 1">
            ({{ Math.round(status!.waitingSeconds) }}s)</template
          >.
        </span>
      </template>
      <template v-else>
        <i class="pi pi-spin pi-spinner text-primary-500 shrink-0" />
        <span class="truncate">
          Revit is busy: <b>{{ longest?.command }}</b>
          <span v-if="longest && longest.source !== 'webview2'" class="text-surface-400">
            ({{ longest.source }})</span
          >
          · {{ Math.round(longest?.seconds ?? 0) }}s
          <span v-if="status!.running.length > 1" class="text-surface-400">
            · +{{ status!.running.length - 1 }} more</span
          >
        </span>
      </template>
    </div>
  </Transition>
</template>

<style scoped>
.busybar-enter-active,
.busybar-leave-active {
  transition:
    transform 0.2s ease,
    opacity 0.2s ease;
}
.busybar-enter-from,
.busybar-leave-to {
  transform: translateY(100%);
  opacity: 0;
}
</style>
