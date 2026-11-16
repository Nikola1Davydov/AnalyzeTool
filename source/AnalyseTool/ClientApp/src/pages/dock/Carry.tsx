import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { TileIcon } from "./Tile";

/** A button picked up in the edit mode: where it comes from and what may happen to it. */
export interface CarryItem {
  key: string;
  name: string;
  /** The folder of the pane it sits in: a tab (<name>.tab), or a folder of plugin.json. */
  folderId: string;
  /** The extension of the button: the button folder of a tab, or the plugin.json folder. */
  extensionId: string;
  glyph?: string | null;
  image?: string | null;
  /** The trash takes it: a button of a tab (its folder goes to the AnalyseTool trash). */
  canRemove: boolean;
}

type Zone = "folders" | "trash" | null;

interface CarryState {
  item: CarryItem;
  /** drag: the tile follows the pointer in its folder; carry: it was taken to the folders and floats over the pane. */
  phase: "drag" | "carry";
  x: number;
  y: number;
  zone: Zone;
}

interface Carry {
  state: CarryState | null;
  begin: (item: CarryItem) => void;
  move: (x: number, y: number) => void;
  /** The tile was let go in its folder: true when a zone took it (the trash), so the tile does not move. */
  drop: (x: number, y: number) => boolean;
}

const CarryContext = createContext<Carry>({ state: null, begin: () => undefined, move: () => undefined, drop: () => false });

export function useCarry() {
  return useContext(CarryContext);
}

function zoneAt(x: number, y: number): Zone {
  const zone = document
    .elementsFromPoint(x, y)
    .map((element) => element.closest<HTMLElement>("[data-drop-zone]"))
    .find(Boolean)?.dataset.dropZone;
  return zone === "folders" || zone === "trash" ? zone : null;
}

function folderAt(x: number, y: number) {
  return document
    .elementsFromPoint(x, y)
    .map((element) => element.closest<HTMLElement>('[data-tile-group="home"][data-tile-key]'))
    .find(Boolean)?.dataset.tileKey;
}

/**
 * Moving a button out of its folder, as on a phone: while a button is dragged in the edit mode the trash shows at the
 * bottom and «into another folder» at the top. Over the top the pane turns to the folders and the button floats with the
 * pointer; let go on a folder — it moves there; on the trash — it is removed; anywhere else, or Esc — nothing happens.
 */
export function CarryProvider(props: {
  children: ReactNode;
  /** The pointer reached the top: show the folders. */
  onToFolders: () => void;
  onMove: (item: CarryItem, folderId: string) => void;
  onRemove: (item: CarryItem) => void;
}) {
  const [state, setState] = useState<CarryState | null>(null);
  const current = useRef<CarryState | null>(null);
  current.current = state;
  const handlers = useRef(props);
  handlers.current = props;

  const finish = useCallback((x: number, y: number) => {
    const carried = current.current;
    setState(null);
    if (!carried) return;
    if (zoneAt(x, y) === "trash" && carried.item.canRemove) {
      handlers.current.onRemove(carried.item);
      return;
    }
    const folder = folderAt(x, y);
    if (folder && folder !== carried.item.folderId) handlers.current.onMove(carried.item, folder);
  }, []);

  // Carried over the folders: the tile it started from is gone, the window follows the pointer
  const carrying = state?.phase === "carry";
  useEffect(() => {
    if (!carrying) return;
    const onMove = (event: PointerEvent) => setState((value) => (value ? { ...value, x: event.clientX, y: event.clientY, zone: zoneAt(event.clientX, event.clientY) } : value));
    const onUp = (event: PointerEvent) => finish(event.clientX, event.clientY);
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && setState(null);
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("keydown", onKey);
    };
  }, [carrying, finish]);

  const value = useMemo<Carry>(
    () => ({
      state,
      begin: (item) => setState({ item, phase: "drag", x: 0, y: 0, zone: null }),
      move: (x, y) => {
        const carried = current.current;
        if (!carried || carried.phase !== "drag") return;
        const zone = zoneAt(x, y);
        if (zone === "folders") {
          setState({ ...carried, phase: "carry", x, y, zone: null });
          handlers.current.onToFolders();
          return;
        }
        if (zone !== carried.zone) setState({ ...carried, x, y, zone });
      },
      drop: (x, y) => {
        const carried = current.current;
        if (!carried || carried.phase !== "drag") return false;
        const zone = zoneAt(x, y);
        setState(null);
        if (zone === "trash" && carried.item.canRemove) {
          handlers.current.onRemove(carried.item);
          return true;
        }
        return false;
      },
    }),
    [state],
  );

  return (
    <CarryContext.Provider value={value}>
      {props.children}
      {state && <CarryLayer state={state} />}
    </CarryContext.Provider>
  );
}

/** The zones while a button is in hand, and the button itself once it floats over the folders. */
function CarryLayer({ state }: { state: CarryState }) {
  return (
    <>
      {state.phase === "drag" ? (
        <div className="drop-zone top" data-drop-zone="folders">
          ↑ Into another folder
        </div>
      ) : (
        <div className="drop-zone top hint">Drop «{state.item.name}» on a folder — Esc to cancel</div>
      )}
      {state.item.canRemove && (
        <div className={`drop-zone trash${state.zone === "trash" ? " over" : ""}`} data-drop-zone="trash">
          🗑 Remove the button
        </div>
      )}
      {state.phase === "carry" && (
        <div className="carry-ghost" style={{ left: state.x, top: state.y }}>
          <span className="tile-face">
            <TileIcon glyph={state.item.glyph} image={state.item.image} title={state.item.name} />
          </span>
        </div>
      )}
    </>
  );
}
