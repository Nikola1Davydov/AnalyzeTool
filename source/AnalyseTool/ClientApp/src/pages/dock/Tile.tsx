import { motion } from "motion/react";
import { useRef, type ReactNode } from "react";

/** The spring of the pane: quick, with a small overshoot, like iOS. */
export const spring = { type: "spring", stiffness: 520, damping: 34, mass: 0.8 } as const;

/** A stable colour per name, for the initials of a button without an icon. */
function hue(text: string) {
  let hash = 0;
  for (const character of text) hash = (hash * 31 + character.charCodeAt(0)) | 0;
  return Math.abs(hash) % 360;
}

/** The first letters of the first two words: «Перенумеровать стены» → «ПС». */
function initials(title: string) {
  const words = title.split(/[\s\-_]+/).filter(Boolean);
  return (words.length > 1 ? words[0][0] + words[1][0] : (words[0] ?? "?").slice(0, 2)).toUpperCase();
}

/**
 * The icon of a button: a PNG of its folder, a Segoe MDL2 Assets glyph (as on the ribbon), or the initials of the name on a
 * gradient.
 */
export function TileIcon({ glyph, image, title }: { glyph?: string | null; image?: string | null; title: string }) {
  if (image) return <img className="tile-icon image" src={image} alt="" draggable={false} />;
  const h = hue(title);
  const background = `linear-gradient(160deg, hsl(${h} 70% 58%), hsl(${(h + 25) % 360} 65% 42%))`;
  if (glyph) return <span className="tile-icon glyph" style={{ background }}>{glyph}</span>;
  return (
    <span className="tile-icon letters" style={{ background }}>
      {initials(title)}
    </span>
  );
}

/**
 * A square button of the grid, like an app on the home screen. The right mouse button starts the edit mode: the tiles
 * wiggle, can be dragged to another place and show their badges: pin to the dock or remove from it, and ✎ — change.
 */
export function Tile(props: {
  label: string;
  icon: ReactNode;
  title?: string;
  badge?: "error" | "locked" | null;
  busy?: boolean;
  dim?: boolean;
  className?: string;
  /** The key of the tile among its neighbours: makes it draggable in the edit mode and animates its moves. */
  dragKey?: string;
  /** Tiles are dropped only onto tiles of the same group: the grid, or the dock. */
  dragGroup?: string;
  editing?: boolean;
  /** The round badge at the top left in the edit mode: «−» or «+». */
  editBadge?: { symbol: "−" | "+"; title: string; onClick: () => void } | null;
  /** The round ✎ at the top right in the edit mode: opens the sheet of the button — change it, its file, its versions. */
  onChange?: { title: string; onClick: () => void } | null;
  /** 📁 at the bottom left in the edit mode: opens the folder of the button in Explorer — to edit it with an AI tool. */
  onOpenFolder?: { title: string; onClick: () => void } | null;
  /** The star: the button is on the Revit ribbon. A small ★ shows it; in the edit mode a ☆/★ at the bottom right toggles it. */
  star?: { on: boolean; title: string; onClick: () => void } | null;
  onClick: () => void;
  /** The right mouse button: starts the edit mode. */
  onEdit?: () => void;
  onMove?: (from: string, to: string) => void;
  /** The tile was opened: where on the screen it was, for the zoom of the screen that opens. */
  onOpenFrom?: (rect: DOMRect) => void;
}) {
  const element = useRef<HTMLButtonElement | null>(null);
  const draggable = props.editing && props.dragKey && props.onMove;

  // In the edit mode a tile follows the pointer and lands on the place of the tile it is dropped on
  const dropOn = (x: number, y: number) => {
    const target = document
      .elementsFromPoint(x, y)
      .map((candidate) => candidate.closest<HTMLElement>("[data-tile-key]"))
      .find((candidate) => candidate && candidate.dataset.tileKey !== props.dragKey && candidate.dataset.tileGroup === props.dragGroup);
    if (target?.dataset.tileKey) props.onMove!(props.dragKey!, target.dataset.tileKey);
  };

  // A wiggle of its own per tile, so they do not move in step
  const phase = useRef(Math.random() * 0.25);

  return (
    <motion.button
      ref={element}
      layout={props.dragKey ? "position" : false}
      transition={spring}
      // The resting scale: tap and drag always come back to it
      initial={false}
      animate={{ scale: 1 }}
      drag={!!draggable}
      dragSnapToOrigin
      dragElastic={1}
      dragMomentum={false}
      whileDrag={{ scale: 1.12, zIndex: 5 }}
      onDragEnd={(event) => {
        const point = "clientX" in event ? event : (event as TouchEvent).changedTouches?.[0];
        if (point) dropOn(point.clientX, point.clientY);
      }}
      data-tile-key={props.dragKey}
      data-tile-group={props.dragGroup}
      className={`tile ${props.dim ? "dim" : ""} ${props.editing ? "editing" : ""} ${props.className ?? ""}`}
      title={props.title ?? props.label}
      onClick={() => {
        if (props.editing) return;
        if (element.current) props.onOpenFrom?.(element.current.getBoundingClientRect());
        props.onClick();
      }}
      onContextMenu={(event) => {
        event.preventDefault();
        props.onEdit?.();
      }}
    >
      <motion.span
        className="tile-face"
        animate={props.editing ? { rotate: [-1.8, 1.8, -1.8] } : { rotate: 0 }}
        transition={props.editing ? { duration: 0.28, repeat: Infinity, ease: "easeInOut", delay: phase.current } : spring}
      >
        {props.icon}
        {props.badge === "error" && <span className="tile-badge error">!</span>}
        {props.badge === "locked" && <span className="tile-badge locked">🔒</span>}
        {props.busy && <span className="tile-busy" />}
        {props.star?.on && !props.editing && <span className="tile-star" title="On the Revit ribbon">★</span>}
      </motion.span>
      {props.editing && props.editBadge && (
        <motion.span
          role="button"
          className={`edit-badge ${props.editBadge.symbol === "+" ? "add" : "remove"}`}
          title={props.editBadge.title}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={spring}
          onClick={(event) => {
            event.stopPropagation();
            props.editBadge!.onClick();
          }}
        >
          {props.editBadge.symbol}
        </motion.span>
      )}
      {props.editing && props.onChange && (
        <motion.span
          role="button"
          className="edit-badge change"
          title={props.onChange.title}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={spring}
          onPointerDown={(event) => event.stopPropagation()}
          onClick={(event) => {
            event.stopPropagation();
            props.onChange!.onClick();
          }}
        >
          ✎
        </motion.span>
      )}
      {props.editing && props.onOpenFolder && (
        <motion.span
          role="button"
          className="edit-badge open-folder"
          title={props.onOpenFolder.title}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={spring}
          onPointerDown={(event) => event.stopPropagation()}
          onClick={(event) => {
            event.stopPropagation();
            props.onOpenFolder!.onClick();
          }}
        >
          <svg width="12" height="12" viewBox="0 0 16 16" aria-hidden="true">
            <path fill="currentColor" d="M1.5 3A1.5 1.5 0 0 1 3 1.5h3.1c.4 0 .78.16 1.06.44L8.62 3.4H13A1.5 1.5 0 0 1 14.5 4.9v7.6A1.5 1.5 0 0 1 13 14H3a1.5 1.5 0 0 1-1.5-1.5Z" />
          </svg>
        </motion.span>
      )}
      {props.editing && props.star && (
        <motion.span
          role="button"
          className={`edit-badge star ${props.star.on ? "on" : ""}`}
          title={props.star.title}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={spring}
          onPointerDown={(event) => event.stopPropagation()}
          onClick={(event) => {
            event.stopPropagation();
            props.star!.onClick();
          }}
        >
          {props.star.on ? "★" : "☆"}
        </motion.span>
      )}
      <span className="tile-label">{props.label}</span>
    </motion.button>
  );
}

/** Moves `from` to the place of `to` in the list of keys. */
export function move(keys: string[], from: string, to: string) {
  const result = keys.filter((key) => key !== from);
  result.splice(result.indexOf(to), 0, from);
  return result;
}

/** Sorts items by the saved order of their keys; items not in it keep their own order after the ordered ones. */
export function ordered<T>(items: T[], keyOf: (item: T) => string, order: string[] | undefined) {
  const index = new Map((order ?? []).map((key, position) => [key, position]));
  return items
    .map((item, position) => ({ item, position, rank: index.get(keyOf(item)) ?? Number.MAX_SAFE_INTEGER }))
    .sort((left, right) => left.rank - right.rank || left.position - right.position)
    .map(({ item }) => item);
}
