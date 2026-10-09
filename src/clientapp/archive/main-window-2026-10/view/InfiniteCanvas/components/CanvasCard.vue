<script setup lang="ts">
import { inject, reactive, ref, watch, type Ref } from "vue";
import { useDrag } from "../composables/useDrag";

const props = defineProps<{
  title: string;
  initialX: number;
  initialY: number;
  initialWidth?: number;
  initialHeight?: number;
  selected?: boolean;
  closable?: boolean;
  refreshable?: boolean;
  refreshing?: boolean;
}>();

const emit = defineEmits<{
  refresh: [];
  close: [];
  layoutChange: [{ x: number; y: number; width: number; height: number }];
}>();

const pos = reactive({ x: props.initialX, y: props.initialY });
const width = ref(props.initialWidth ?? 520);
const height = ref(props.initialHeight ?? 380);

const minWidth = 320;
const minHeight = 220;

const fallbackZoom = ref(1);
const zoom = inject<Ref<number>>("canvasZoom", fallbackZoom);
const { startDrag } = useDrag(pos, zoom);

watch(
  [() => pos.x, () => pos.y, width, height],
  ([x, y, nextWidth, nextHeight]) => {
    emit("layoutChange", {
      x,
      y,
      width: nextWidth,
      height: nextHeight,
    });
  },
  { flush: "post" },
);

function startResize(e: MouseEvent) {
  e.stopPropagation();

  const startX = e.clientX;
  const startY = e.clientY;
  const startW = width.value;
  const startH = height.value;

  const move = (ev: MouseEvent) => {
    width.value = Math.max(minWidth, startW + (ev.clientX - startX) / zoom.value);
    height.value = Math.max(minHeight, startH + (ev.clientY - startY) / zoom.value);
  };

  const up = () => {
    window.removeEventListener("mousemove", move);
    window.removeEventListener("mouseup", up);
  };

  window.addEventListener("mousemove", move);
  window.addEventListener("mouseup", up);
}
</script>

<template>
  <article
    class="card"
    :class="{ 'card--selected': props.selected }"
    :style="{ left: `${pos.x}px`, top: `${pos.y}px`, width: `${width}px`, height: `${height}px` }"
    @mousedown.stop
  >
    <header class="card-header" @mousedown="startDrag">
      <span class="title">{{ props.title }}</span>
      <div class="actions">
        <button
          v-if="props.refreshable"
          type="button"
          class="icon-btn"
          title="Refresh"
          @mousedown.stop
          @click.stop="emit('refresh')"
        >
          <i class="pi pi-refresh" :class="props.refreshing ? 'pi-spin' : ''" />
        </button>

        <button
          v-if="props.closable"
          type="button"
          class="icon-btn"
          title="Close"
          @mousedown.stop
          @click.stop="emit('close')"
        >
          <i class="pi pi-times" />
        </button>
      </div>
    </header>
    <section class="card-body">
      <slot />
    </section>
    <div class="resize-handle" @mousedown="startResize" />
  </article>
</template>

<style scoped>
.card {
  position: absolute;
  min-width: 19rem;
  min-height: 13.75rem;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.75rem;
  background: var(--p-surface-0, #ffffff);
  box-shadow: 0 14px 34px -26px rgba(15, 23, 42, 0.55);
  overflow: hidden;
}

.card--selected {
  border-color: var(--p-primary-500, #0284c7);
  box-shadow:
    0 0 0 1px color-mix(in srgb, var(--p-primary-500, #0284c7) 30%, transparent),
    0 18px 34px -26px rgba(2, 132, 199, 0.65);
}

.card--selected .card-header {
  background: color-mix(in srgb, var(--p-primary-100, #e0f2fe) 78%, #ffffff);
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.55rem 0.7rem;
  background: var(--p-surface-100, #f1f5f9);
  border-bottom: 1px solid var(--p-surface-200, #e2e8f0);
  cursor: grab;
  user-select: none;
}

.card-header:active {
  cursor: grabbing;
}

.title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--p-surface-800, #1e293b);
}

.hint {
  font-size: 0.7rem;
  color: var(--p-surface-500, #64748b);
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.icon-btn {
  width: 1.7rem;
  height: 1.7rem;
  border: 1px solid var(--p-surface-300, #d1d5db);
  border-radius: 0.45rem;
  background: var(--p-surface-0, #ffffff);
  color: var(--p-surface-700, #334155);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.icon-btn:hover {
  background: var(--p-surface-100, #f1f5f9);
}

.card-body {
  flex: 1;
  padding: 0.7rem;
  overflow: auto;
}

.resize-handle {
  position: absolute;
  right: 0;
  bottom: 0;
  width: 1rem;
  height: 1rem;
  cursor: nwse-resize;
}

.resize-handle::before {
  content: "";
  position: absolute;
  right: 0.2rem;
  bottom: 0.2rem;
  width: 0.55rem;
  height: 0.55rem;
  border-right: 2px solid var(--p-surface-400, #94a3b8);
  border-bottom: 2px solid var(--p-surface-400, #94a3b8);
}
</style>
