<script setup lang="ts">
import { computed, provide, ref, watch } from "vue";
import { useCanvas } from "../composables/useCanvas";

type SelectionRect = {
  x: number;
  y: number;
  width: number;
  height: number;
};

const props = defineProps<{
  initialZoom?: number;
  initialPanX?: number;
  initialPanY?: number;
}>();

const emit = defineEmits<{
  viewportChange: [{ zoom: number; panX: number; panY: number }];
  selectionChange: [SelectionRect];
  selectionEnd: [SelectionRect | null];
}>();

const { zoom, pan, transform, onWheel, startPan } = useCanvas();
const viewportRef = ref<HTMLElement | null>(null);
const selectionBox = ref({
  active: false,
  x: 0,
  y: 0,
  width: 0,
  height: 0,
});

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

function getCanvasPoint(e: MouseEvent) {
  if (!viewportRef.value) return null;

  const rect = viewportRef.value.getBoundingClientRect();
  const viewportX = e.clientX - rect.left;
  const viewportY = e.clientY - rect.top;

  return {
    viewportX,
    viewportY,
    canvasX: (viewportX - pan.x) / zoom.value,
    canvasY: (viewportY - pan.y) / zoom.value,
  };
}

function buildSelectionRect(startX: number, startY: number, endX: number, endY: number) {
  return {
    x: Math.min(startX, endX),
    y: Math.min(startY, endY),
    width: Math.abs(endX - startX),
    height: Math.abs(endY - startY),
  };
}

function startSelection(e: MouseEvent) {
  if (e.button !== 0) return;

  const startPoint = getCanvasPoint(e);
  if (!startPoint) return;

  e.preventDefault();

  const minCanvasSize = 4 / zoom.value;
  selectionBox.value = {
    active: true,
    x: startPoint.viewportX,
    y: startPoint.viewportY,
    width: 0,
    height: 0,
  };

  const move = (ev: MouseEvent) => {
    const nextPoint = getCanvasPoint(ev);
    if (!nextPoint) return;

    selectionBox.value = {
      active: true,
      ...buildSelectionRect(
        startPoint.viewportX,
        startPoint.viewportY,
        nextPoint.viewportX,
        nextPoint.viewportY,
      ),
    };

    emit(
      "selectionChange",
      buildSelectionRect(
        startPoint.canvasX,
        startPoint.canvasY,
        nextPoint.canvasX,
        nextPoint.canvasY,
      ),
    );
  };

  const up = (ev: MouseEvent) => {
    window.removeEventListener("mousemove", move);
    window.removeEventListener("mouseup", up);

    const endPoint = getCanvasPoint(ev) || startPoint;
    const finalRect = buildSelectionRect(
      startPoint.canvasX,
      startPoint.canvasY,
      endPoint.canvasX,
      endPoint.canvasY,
    );

    selectionBox.value = {
      active: false,
      x: 0,
      y: 0,
      width: 0,
      height: 0,
    };

    if (finalRect.width < minCanvasSize && finalRect.height < minCanvasSize) {
      emit("selectionEnd", null);
      return;
    }

    emit("selectionEnd", finalRect);
  };

  window.addEventListener("mousemove", move);
  window.addEventListener("mouseup", up);
}

function onViewportMouseDown(e: MouseEvent) {
  if (e.button === 2) {
    startPan(e);
    return;
  }

  if (e.button === 0) startSelection(e);
}

// Grid follows pan offset so dots feel stationary
const gridStyle = computed(() => ({
  backgroundPosition: `${pan.x % 32}px ${pan.y % 32}px`,
}));
</script>

<template>
  <div
    ref="viewportRef"
    class="viewport"
    @wheel.prevent="onViewportWheel"
    @contextmenu.prevent
    @mousedown="onViewportMouseDown"
  >
    <!-- Grid background (doesn't scale, uses bg-size trick) -->
    <div class="grid" :style="gridStyle" />

    <!-- Canvas layer: all elements live here -->
    <div class="canvas-layer" :style="{ transform }">
      <slot />
    </div>

    <div
      v-if="selectionBox.active"
      class="selection-box"
      :style="{
        left: `${selectionBox.x}px`,
        top: `${selectionBox.y}px`,
        width: `${selectionBox.width}px`,
        height: `${selectionBox.height}px`,
      }"
    />

    <!-- HUD overlay -->
    <div class="hud">{{ Math.round(zoom * 100) }}%</div>
    <div class="toolbar-slot" @mousedown.stop>
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
  background: var(--p-surface-0, #ffffff);
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

.selection-box {
  position: absolute;
  border: 1px solid var(--p-primary-500, #0284c7);
  background: color-mix(in srgb, var(--p-primary-500, #0284c7) 14%, transparent);
  pointer-events: none;
  z-index: 2;
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
  z-index: 3;
}
</style>
