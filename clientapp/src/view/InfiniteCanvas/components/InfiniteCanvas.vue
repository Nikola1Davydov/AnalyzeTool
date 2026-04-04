<script setup lang="ts">
import { computed, provide, ref, watch } from "vue";
import { useCanvas } from "../composables/useCanvas";

const props = defineProps<{
  initialZoom?: number;
  initialPanX?: number;
  initialPanY?: number;
}>();

const emit = defineEmits<{
  viewportChange: [{ zoom: number; panX: number; panY: number }];
}>();

const { zoom, pan, transform, onWheel, startPan } = useCanvas();
const viewportRef = ref<HTMLElement | null>(null);

provide("canvasZoom", zoom);

if (typeof props.initialZoom === "number") zoom.value = props.initialZoom;
if (typeof props.initialPanX === "number") pan.x = props.initialPanX;
if (typeof props.initialPanY === "number") pan.y = props.initialPanY;

watch(
  [zoom, () => pan.x, () => pan.y],
  ([nextZoom, nextPanX, nextPanY]) => {
    emit("viewportChange", {
      zoom: nextZoom,
      panX: nextPanX,
      panY: nextPanY,
    });
  },
  { flush: "post" },
);

function onViewportWheel(e: WheelEvent) {
  if (!viewportRef.value) return;
  onWheel(e, viewportRef.value);
}

// Grid follows pan offset so dots feel stationary
const gridStyle = computed(() => ({
  backgroundPosition: `${pan.x % 32}px ${pan.y % 32}px`,
}));
</script>

<template>
  <div ref="viewportRef" class="viewport" @wheel.prevent="onViewportWheel" @mousedown="startPan">
    <!-- Grid background (doesn't scale, uses bg-size trick) -->
    <div class="grid" :style="gridStyle" />

    <!-- Canvas layer: all elements live here -->
    <div class="canvas-layer" :style="{ transform }">
      <slot />
    </div>

    <!-- HUD overlay -->
    <div class="hud">{{ Math.round(zoom * 100) }}%</div>
    <div class="toolbar-slot">
      <slot name="toolbar" />
    </div>
  </div>
</template>

<style scoped>
.viewport {
  position: relative;
  height: calc(100vh - 10rem);
  min-height: 32rem;
  overflow: hidden;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.75rem;
}

.grid {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background-image: radial-gradient(circle, rgba(100, 116, 139, 0.2) 1px, transparent 1px);
  background-size: 32px 32px;
}

.canvas-layer {
  position: absolute;
  inset: 0;
  transform-origin: 0 0;
  will-change: transform;
}

.hud {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
  padding: 0.2rem 0.5rem;
  border-radius: 0.5rem;
  border: 1px solid var(--p-surface-300, #d1d5db);
  background: color-mix(in srgb, var(--p-surface-0, #ffffff) 92%, transparent);
  font-size: 0.75rem;
  color: var(--p-surface-700, #334155);
}

.toolbar-slot {
  position: absolute;
  top: 0.75rem;
  left: 0.75rem;
}
</style>
