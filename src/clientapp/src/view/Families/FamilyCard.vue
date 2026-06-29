<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from "vue";
import { invoke } from "@/RevitBridge";
import { getCachedPreview, setCachedPreview } from "@/utils/familyCache";

interface FamilyRow {
  id: number;
  uniqueId: string;
  versionGuid: string;
  name: string;
  category: string;
  typeCount: number;
  instanceCount: number;
  isInPlace: boolean;
}

const props = defineProps<{ family: FamilyRow }>();
const emit = defineEmits<{ open: [] }>();

const root = ref<HTMLElement | null>(null);
const previewUri = ref<string | null>(null);
const state = ref<"idle" | "loading" | "loaded" | "none">("idle");
let observer: IntersectionObserver | null = null;

async function loadPreview() {
  if (state.value !== "idle") return;
  state.value = "loading";

  // 1. Client-side cache (IndexedDB) — a hit renders instantly, no Revit round-trip. Keyed by
  //    uniqueId + the family's VersionGuid, so an edited family re-renders instead of showing a stale image.
  const key = props.family.uniqueId;
  const version = props.family.versionGuid;
  const cached = key ? await getCachedPreview(key, version) : null;
  if (cached) {
    previewUri.value = cached;
    state.value = "loaded";
    return;
  }

  // 2. Miss → ask Revit to render it, then cache for next time.
  try {
    const res = await invoke<{ id: number; dataUri: string | null }>("GetFamilyPreview", {
      id: props.family.id,
    });
    if (res?.dataUri) {
      previewUri.value = res.dataUri;
      state.value = "loaded";
      if (key) void setCachedPreview(key, version, res.dataUri);
    } else {
      state.value = "none";
    }
  } catch (e) {
    console.error("Preview failed", e);
    state.value = "none";
  }
}

function initial(name: string): string {
  const c = (name ?? "").trim();
  return c ? c.charAt(0).toUpperCase() : "?";
}
function tileColor(category: string): string {
  let hash = 0;
  const key = category || "?";
  for (let i = 0; i < key.length; i++) hash = (hash * 31 + key.charCodeAt(i)) >>> 0;
  const hue = hash % 360;
  return `linear-gradient(135deg, hsl(${hue} 60% 52%), hsl(${(hue + 40) % 360} 60% 42%))`;
}

// Lazy: only render previews for cards scrolled into view (and only once the Gallery tab is shown —
// while hidden via v-show the cards aren't intersecting, so nothing is fetched).
onMounted(() => {
  observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((e) => e.isIntersecting)) {
        void loadPreview();
        observer?.disconnect();
        observer = null;
      }
    },
    { rootMargin: "150px" },
  );
  if (root.value) observer.observe(root.value);
});
onBeforeUnmount(() => observer?.disconnect());
</script>

<template>
  <div
    ref="root"
    class="rounded-xl border border-surface-200 bg-surface-0 overflow-hidden hover:shadow-md transition-shadow cursor-pointer"
    @click="emit('open')"
  >
    <div class="aspect-square relative bg-surface-50">
      <img
        v-if="previewUri"
        :src="previewUri"
        :alt="family.name"
        class="absolute inset-0 w-full h-full object-contain"
      />
      <div
        v-else
        class="absolute inset-0 flex items-center justify-center text-white text-5xl font-bold select-none"
        :style="{ background: tileColor(family.category) }"
      >
        <i v-if="state === 'loading'" class="pi pi-spin pi-spinner text-2xl opacity-80" />
        <template v-else>{{ initial(family.name) }}</template>
      </div>
    </div>
    <div class="p-3">
      <div class="font-semibold truncate" :title="family.name">{{ family.name }}</div>
      <div class="text-surface-500 text-xs truncate">{{ family.category || "—" }}</div>
      <div class="flex items-center gap-1 mt-2 flex-wrap">
        <Tag :value="`${family.typeCount} types`" severity="secondary" />
        <Tag
          :value="`${family.instanceCount} inst`"
          :severity="family.instanceCount === 0 ? 'warn' : 'info'"
        />
        <Tag v-if="family.isInPlace" value="in-place" severity="warn" />
      </div>
    </div>
  </div>
</template>
