<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from "vue";
import { invoke, oldestPendingAge } from "@/RevitBridge";

// Bottom status strip shown in EVERY window while the platform is busy. Answers the two "why is
// nothing happening?" cases:
//  • a command is running (long export, purge, AI call…) — show what and for how long;
//  • Revit can't execute queued work at all (user sits in a modal dialog / edit mode) — say so.
// The host pushes "QueueChanged" events on start/finish; while busy we additionally poll
// GetQueueStatus (which deliberately never touches the Revit thread, so it answers even when Revit
// is blocked). Quick commands never show up: the bar appears only after MIN_VISIBLE_SECONDS.
//
// A THIRD case needs no host at all, and cannot have one (#102): a long Revit operation holds the
// UI thread that every WebView message is delivered on, so while it runs the host does not even
// receive our poll — GetQueueStatus was answering over MCP the whole time and the window could not
// ask. The only fact left on this side is that our own requests are not being answered, and that is
// what the "stalled" state reads: any call older than STALL_MS, checked every second from a plain
// timer that the host cannot block.

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
// GetQueueStatus never touches the Revit thread, so this is cheap even while Revit is blocked —
// still, a Revit session runs for hours with several windows open, so the idle cadence is much
// slower than the busy one. (Both were 2000 ms, which made the busy/idle choice below a no-op.)
//
// The comment above said that BEFORE this line said it: the constant stayed at 2000 while the text
// claimed otherwise, so the busy/idle branch really was a no-op and a session logged one poll every
// two seconds for hours. 10 s idle still arms the proactive warning well inside the time it takes a
// person to notice a frozen Revit, and the first sign of work flips the cadence back to 2 s.
const POLL_IDLE_MS = 10000;
// A call the host has not answered for this long is a host that is not receiving: the status poll
// itself answers in milliseconds when Revit's thread is free.
const STALL_MS = 3000;

const status = ref<QueueStatus | null>(null);
/** Seconds the oldest unanswered call has waited, 0 when the host is answering. */
const stalledSeconds = ref(0);
let pollTimer: number | null = null;
let stallTimer: number | null = null;
let disposed = false;

const busy = computed(() => (status.value?.running.length ?? 0) > 0 || (status.value?.pendingRevitWork ?? 0) > 0);
const longest = computed(() =>
  status.value?.running.reduce((max, r) => (r.seconds > (max?.seconds ?? -1) ? r : max), null as null | QueueStatus["running"][number]),
);
const stalled = computed(() => stalledSeconds.value * 1000 >= STALL_MS);
const visible = computed(
  () =>
    stalled.value ||
    (!!status.value && (status.value.waitingForUser || (longest.value?.seconds ?? 0) >= MIN_VISIBLE_SECONDS)),
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
  // schedule() also runs at the end of refresh(), i.e. AFTER an await — by which time the window
  // may already have closed. Without this check the poller resurrected itself past onUnmounted and
  // then rescheduled forever against a dead transport.
  if (disposed) return;
  pollTimer = window.setTimeout(refresh, busy.value ? POLL_BUSY_MS : POLL_IDLE_MS);
}

// Runs on a timer of its own, not on a reply: a reply is exactly what does not come while Revit is
// busy. Reads the bridge's pending calls — the poll above and anything else the window asked.
function watchStall() {
  stalledSeconds.value = Math.round(oldestPendingAge() / 1000);
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
  stallTimer = window.setInterval(watchStall, 1000);
  void refresh(); // a window may open while a command started elsewhere is already running
});
onUnmounted(() => {
  disposed = true;
  window.removeEventListener("at:QueueChanged", onQueueChanged);
  if (pollTimer !== null) clearTimeout(pollTimer);
  if (stallTimer !== null) clearInterval(stallTimer);
});
</script>

<template>
  <Transition name="busybar">
    <div
      v-if="visible"
      class="fixed bottom-0 inset-x-0 z-50 flex items-center gap-2 px-3 py-1.5 text-xs border-t shadow-lg"
      :class="
        stalled || status?.waitingForUser
          ? 'bg-amber-50 border-amber-300 text-amber-800'
          : 'bg-surface-0 border-surface-200 text-surface-600'
      "
    >
      <template v-if="stalled">
        <i class="pi pi-spin pi-spinner text-amber-500 shrink-0" />
        <!-- Detected from this side alone: the host has not answered anything for a while. What it
             is doing we cannot know — the last thing it told us, if anything, is the best we have. -->
        <span class="truncate">
          <b>Revit is busy</b> — it is running
          <template v-if="longest">“{{ longest.command }}”</template>
          <template v-else>something</template>
          on its main thread and this window will answer when it finishes ({{ stalledSeconds }}s).
        </span>
      </template>
      <template v-else-if="status!.waitingForUser">
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
