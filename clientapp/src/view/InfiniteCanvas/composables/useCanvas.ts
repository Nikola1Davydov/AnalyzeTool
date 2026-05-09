import { ref, reactive, computed } from "vue";

export function useCanvas() {
  const zoom = ref(1);
  const pan = reactive({ x: 0, y: 0 });

  // Zoom toward the mouse cursor position
  function onWheel(e: WheelEvent, viewport: HTMLElement) {
    e.preventDefault();
    const rect = viewport.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const delta = e.deltaY > 0 ? 0.9 : 1.1;
    const next = Math.min(3, Math.max(0.2, zoom.value * delta));

    // Keep the point under cursor stationary
    pan.x = mx - (mx - pan.x) * (next / zoom.value);
    pan.y = my - (my - pan.y) * (next / zoom.value);
    zoom.value = next;
  }

  // Pan by dragging the background
  function startPan(e: MouseEvent) {
    if (e.button !== 2) return;
    e.preventDefault();

    const ox = pan.x,
      oy = pan.y;
    const sx = e.clientX,
      sy = e.clientY;

    const move = (ev: MouseEvent) => {
      pan.x = ox + ev.clientX - sx;
      pan.y = oy + ev.clientY - sy;
    };
    const up = () => {
      window.removeEventListener("mousemove", move);
      window.removeEventListener("mouseup", up);
    };
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup", up);
  }

  // CSS transform applied to the canvas layer
  const transform = computed(() => `translate(${pan.x}px, ${pan.y}px) scale(${zoom.value})`);

  return { zoom, pan, transform, onWheel, startPan };
}
