/**
 * ☆ on a button: a copy of its icon flies in an arc from the tile to the end of the dock, as an app does on a phone.
 * Nothing flies with the effects off (the profile) or when Windows asks for less motion.
 */
export function flyToDock(tile: HTMLElement) {
  const pane = tile.closest<HTMLElement>(".pane");
  const dock = pane?.querySelector<HTMLElement>(".dock");
  if (!pane || !dock || pane.classList.contains("no-effects") || window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;

  const face = tile.querySelector<HTMLElement>(".tile-face") ?? tile;
  const from = face.getBoundingClientRect();
  const docked = dock.querySelectorAll<HTMLElement>(".tile-face");
  const last = docked.length > 0 ? docked[docked.length - 1].getBoundingClientRect() : null;
  const box = dock.getBoundingClientRect();
  // Where it lands: next to the last button of the dock, or at its start when it is empty
  const size = last ? last.width : Math.min(from.width, box.height * 0.6);
  const toX = last ? last.right + 10 + size / 2 : box.left + 16 + size / 2;
  const toY = last ? last.top + last.height / 2 : box.top + box.height / 2;
  const dx = toX - (from.left + from.width / 2);
  const dy = toY - (from.top + from.height / 2);
  const scale = size / from.width;

  const ghost = face.cloneNode(true) as HTMLElement;
  ghost.classList.add("fly-ghost");
  Object.assign(ghost.style, { position: "fixed", left: `${from.left}px`, top: `${from.top}px`, width: `${from.width}px`, height: `${from.height}px`, margin: "0" });
  pane.appendChild(ghost);

  const lift = Math.min(120, Math.abs(dy) * 0.35 + 40);
  const duration = 560;
  const remove = () => ghost.remove();
  // Measured now, flown from the next frame: the click re-renders the dock first, and an animation started in the same
  // task would wait for it. The timer removes the copy even when no frame comes (a hidden pane).
  requestAnimationFrame(() => {
    const animation = ghost.animate(
      [
        { transform: "translate(0, 0) scale(1)", opacity: 1 },
        { transform: `translate(${dx * 0.45}px, ${dy * 0.45 - lift}px) scale(${(1 + scale) / 2 + 0.08})`, opacity: 1, offset: 0.45 },
        { transform: `translate(${dx}px, ${dy}px) scale(${scale})`, opacity: 0.85 },
      ],
      { duration, easing: "cubic-bezier(0.3, 0.1, 0.2, 1)", fill: "forwards" },
    );
    animation.onfinish = remove;
    animation.oncancel = remove;
  });
  window.setTimeout(remove, duration + 400);
}
