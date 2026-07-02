<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from "vue";
import { invoke } from "@/RevitBridge";
import { getCachedPreview, setCachedPreview } from "@/utils/familyCache";

// Lazy preview tile for an on-disk family file: renders the .rfa's embedded thumbnail (GetLibraryPreview),
// IndexedDB-cached by file path with the file's mtime as version — an edited .rfa gets a fresh thumbnail.
const props = defineProps<{ path: string; name: string; version: string }>();

const root = ref<HTMLElement | null>(null);
const previewUri = ref<string | null>(null);
const state = ref<"idle" | "loading" | "loaded" | "none">("idle");
let observer: IntersectionObserver | null = null;

async function loadPreview() {
  if (state.value !== "idle") return;
  state.value = "loading";

  const cached = await getCachedPreview(props.path, props.version);
  if (cached) {
    previewUri.value = cached;
    state.value = "loaded";
    return;
  }

  try {
    const res = await invoke<{ path: string; dataUri: string | null }>("GetLibraryPreview", {
      path: props.path,
    });
    if (res?.dataUri) {
      previewUri.value = res.dataUri;
      state.value = "loaded";
      void setCachedPreview(props.path, props.version, res.dataUri);
    } else {
      state.value = "none";
    }
  } catch (e) {
    console.error("Library preview failed", e);
    state.value = "none";
  }
}

function initial(name: string): string {
  const c = (name ?? "").trim();
  return c ? c.charAt(0).toUpperCase() : "?";
}
function tileColor(key: string): string {
  let hash = 0;
  for (let i = 0; i < key.length; i++) hash = (hash * 31 + key.charCodeAt(i)) >>> 0;
  const hue = hash % 360;
  return `linear-gradient(135deg, hsl(${hue} 60% 52%), hsl(${(hue + 40) % 360} 60% 42%))`;
}

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
      :alt="name"
      class="absolute inset-0 w-full h-full object-contain"
    />
    <div
      v-else
      class="absolute inset-0 flex items-center justify-center text-white font-bold select-none"
      :style="{ background: tileColor(name) }"
    >
      <i v-if="state === 'loading'" class="pi pi-spin pi-spinner opacity-80" />
      <span v-else>{{ initial(name) }}</span>
    </div>
  </div>
</template>
