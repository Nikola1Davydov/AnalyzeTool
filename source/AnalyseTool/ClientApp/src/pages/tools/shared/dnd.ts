import type { DragEvent } from "react";

// Drag and drop of file parameters between the two panes — native HTML5 DnD, which WebView2 supports. The payload
// travels under a type of its own, so a drop target ignores text or files dragged in from outside, and can tell
// during dragover whether the drag is ours.

const TYPE = "application/x-analysetool-parameters";

export function startDrag(event: DragEvent, guids: string[]) {
  event.dataTransfer.setData(TYPE, JSON.stringify(guids));
  event.dataTransfer.effectAllowed = "copyMove";
  if (guids.length > 1) setCountImage(event.dataTransfer, guids.length);
}

export function isOurDrag(event: DragEvent) {
  return event.dataTransfer.types.includes(TYPE);
}

export function readDrop(event: DragEvent): string[] | null {
  try {
    const guids = JSON.parse(event.dataTransfer.getData(TYPE) || "null") as string[] | null;
    return guids?.length ? guids : null;
  } catch {
    return null;
  }
}

/** «3 parameters» as the drag image — the browser's default shows only the row that was grabbed. */
function setCountImage(transfer: DataTransfer, count: number) {
  const element = document.createElement("div");
  element.textContent = `${count} parameters`;
  element.style.cssText =
    "position:fixed;top:-100px;left:0;padding:4px 10px;border-radius:4px;font:600 12px 'Segoe UI';" +
    "background:#0f6cbd;color:white";
  document.body.appendChild(element);
  transfer.setDragImage(element, 10, 10);
  setTimeout(() => element.remove(), 0);
}

/** The drop target under the cursor, and the handlers that keep it. */
export function dropHandlers<T>(
  target: T,
  current: T | null,
  setCurrent: (value: T | null) => void,
  onDrop: (guids: string[]) => void,
  effect: "move" | "copy",
) {
  return {
    onDragOver: (event: DragEvent) => {
      if (!isOurDrag(event)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = effect;
      if (current !== target) setCurrent(target);
    },
    onDragLeave: (event: DragEvent) => {
      const to = event.relatedTarget as Node | null;
      if (to && (event.currentTarget as HTMLElement).contains(to)) return;
      if (current === target) setCurrent(null);
    },
    onDrop: (event: DragEvent) => {
      setCurrent(null);
      const guids = readDrop(event);
      if (!guids) return;
      event.preventDefault();
      onDrop(guids);
    },
  };
}
