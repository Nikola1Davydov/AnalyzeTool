<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from "vue";
import { invoke } from "@/RevitBridge";
import { getCachedPreview, setCachedPreview } from "@/utils/familyCache";
import type { FamilyRow } from "./types";

// Reusable lazy preview tile: renders the family's PNG thumbnail (GetFamilyPreview), IndexedDB-first and
// keyed by uniqueId + VersionGuid, with a coloured placeholder while loading / when no preview exists.
// Used both by the gallery card (large) and by the table (small) — sizing comes from the parent.
const props = defineProps<{ family: FamilyRow }>();

const root = ref<HTMLElement | null>(null);
const previewUri = ref<string | null>(null);
const state = ref<"idle" | "loading" | "loaded" | "none">("idle");
let observer: IntersectionObserver | null = null;

async function loadPreview() {
  if (state.value !== "idle") return;
  state.value = "loading";

  const key = props.family.uniqueId;
  const version = props.family.versionGuid;
  const cached = key ? await getCachedPreview(key, version) : null;
  if (cached) {
    previewUri.value = cached;
    state.value = "loaded";
    return;
  }

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

// Only fetch previews for tiles scrolled into view (and, in the gallery, once that tab is shown).
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
  <div ref="root" class="w-full h-full relative bg-surface-50 overflow-hidden">
    <img
      v-if="previewUri"
      :src="previewUri"
      :alt="family.name"
      class="absolute inset-0 w-full h-full object-contain"
    />
    <div
      v-else
      class="absolute inset-0 flex items-center justify-center text-white font-bold select-none"
      :style="{ background: tileColor(family.category) }"
    >
      <i v-if="state === 'loading'" class="pi pi-spin pi-spinner opacity-80" />
      <span v-else>{{ initial(family.name) }}</span>
    </div>
  </div>
</template>
