// Drag and drop of file parameters between the two panes — native HTML5 DnD, which WebView2 supports.
// The payload travels under a type of our own, so a drop target ignores text or files dragged in from
// outside, and it can tell during dragover (before any drop) whether the drag is ours.

const TYPE = "application/x-at-parameters";

export interface DragPayload {
  /** GUIDs of the file parameters being dragged — the row under the cursor, or the whole selection. */
  guids: string[];
}

export function startDrag(event: DragEvent, payload: DragPayload) {
  if (!event.dataTransfer) return;
  event.dataTransfer.setData(TYPE, JSON.stringify(payload));
  event.dataTransfer.effectAllowed = "copyMove";
  if (payload.guids.length > 1) setCountImage(event.dataTransfer, payload.guids.length);
}

export function isOurDrag(event: DragEvent): boolean {
  return !!event.dataTransfer?.types.includes(TYPE);
}

export function readDrop(event: DragEvent): DragPayload | null {
  try {
    const raw = event.dataTransfer?.getData(TYPE);
    const payload = raw ? (JSON.parse(raw) as DragPayload) : null;
    return payload?.guids?.length ? payload : null;
  } catch {
    return null;
  }
}

/** "3 parameters" as the drag image — the browser's default shows only the row that was grabbed. */
function setCountImage(dt: DataTransfer, count: number) {
  const el = document.createElement("div");
  el.textContent = `${count} parameters`;
  el.style.cssText =
    "position:fixed;top:-100px;left:0;padding:4px 10px;border-radius:6px;font:600 12px system-ui;" +
    "background:#2563eb;color:white;box-shadow:0 2px 6px rgb(0 0 0/.2)";
  document.body.appendChild(el);
  dt.setDragImage(el, 10, 10);
  setTimeout(() => el.remove(), 0);
}
