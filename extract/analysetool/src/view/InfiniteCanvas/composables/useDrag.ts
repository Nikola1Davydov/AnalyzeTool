import { Ref } from "vue";

interface Position {
  x: number;
  y: number;
}

// Drag individual canvas elements (zoom-aware)
export function useDrag(pos: Position, zoom: Ref<number>) {
  function startDrag(e: MouseEvent) {
    e.stopPropagation(); // don't trigger canvas pan

    const ox = pos.x,
      oy = pos.y;
    const sx = e.clientX,
      sy = e.clientY;

    const move = (ev: MouseEvent) => {
      // Divide by zoom — cursor travels faster than canvas at zoom < 1
      pos.x = ox + (ev.clientX - sx) / zoom.value;
      pos.y = oy + (ev.clientY - sy) / zoom.value;
    };
    const up = () => {
      window.removeEventListener("mousemove", move);
      window.removeEventListener("mouseup", up);
    };
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup", up);
  }

  return { startDrag };
}
