import { useQuery } from "@tanstack/react-query";
import { motion, MotionConfig } from "motion/react";
import { createContext, useContext, useEffect, useRef, useState } from "react";
import { bridge } from "revit-bridge";
import { appCommands } from "../../api/app";
import { type PaneButton, type PaneFolder, type PaneOverview, paneCommands } from "../../api/pane";
import { ActionsProvider, messageOf, ProblemList, useActions } from "./Actions";
import { ProfileScreen } from "./ProfileScreen";
import { applyTheme, findButton, pruneProfile, type TileSize, useProfile } from "./profile";
import { StatsScreen } from "./StatsScreen";
import { move, ordered, spring, Tile, TileIcon } from "./Tile";
import "./pane.css";

/** Where the pane is: the folders, the profile, the statistics, a folder, or a group (pulldown) of a folder. */
type Screen =
  | { kind: "home" }
  | { kind: "profile" }
  | { kind: "stats"; folder?: string }
  | { kind: "folder"; folder: string; group?: string };

const sizes: TileSize[] = ["small", "medium", "large"];
const sizeLabels: Record<TileSize, string> = { small: "Small buttons", medium: "Medium buttons", large: "Large buttons" };

/** The edit mode: the right mouse button starts it, «Done» or Escape ends it. */
const EditContext = createContext<{ editing: boolean; start: () => void }>({ editing: false, start: () => {} });

function depth(screen: Screen) {
  switch (screen.kind) {
    case "folder":
      return screen.group === undefined ? 1 : 2;
    case "profile":
      return 1;
    case "stats":
      return 2;
    default:
      return 0;
  }
}

function screenKey(screen: Screen) {
  return screen.kind === "folder" ? `folder:${screen.folder}:${screen.group ?? ""}` : screen.kind === "stats" ? `stats:${screen.folder ?? ""}` : screen.kind;
}

/** Going deeper grows out of the tile that opened it; going back the outer screen settles from a little larger. */
function screenEnter(forward: boolean, origin: string) {
  return forward ? { scale: 0.3, opacity: 0, transformOrigin: origin } : { scale: 1.06, opacity: 0, transformOrigin: "50% 40%" };
}

function openWindow(route: string, onError: (message: string) => void) {
  void bridge.invoke(appCommands.OpenWindow, { route }).catch((error: unknown) => onError(messageOf(error)));
}

/**
 * The dock pane, laid out like the home screen of a phone: AnalyseTool and every extension as a tile; a folder as a grid of
 * its buttons (a pulldown of the ribbon as a folder inside); the dock at the bottom holds the favourite buttons of any
 * folder. A tap is a click on the ribbon button — the same command, counted in the statistics. The profile (avatar, top
 * right) keeps the dock, the order, the tile size and the theme in the AnalyseTool profile folder.
 */
export function DockPage() {
  const { profile } = useProfile();
  return (
    <MotionConfig reducedMotion={profile.effects ? "user" : "always"}>
      <ActionsProvider>
        <Pane />
      </ActionsProvider>
    </MotionConfig>
  );
}

function usePaneButtons() {
  return useQuery({
    queryKey: ["paneButtons"],
    queryFn: ({ signal }) => bridge.invoke<PaneOverview>(paneCommands.GetPaneButtons, null, { signal }),
    // Extensions come and go while the pane is open: a saved file, a new button made by an agent
    refetchInterval: 5000,
  });
}

function Pane() {
  const buttons = usePaneButtons();
  const { profile, loaded, update } = useProfile();
  const [screen, setScreen] = useState<Screen | null>(null);
  const [editing, setEditing] = useState(false);
  const [transition, setTransition] = useState({ forward: true, origin: "50% 50%" });
  const main = useRef<HTMLElement | null>(null);
  const openedFrom = useRef<DOMRect | null>(null);

  useEffect(() => applyTheme(profile.theme), [profile.theme]);

  // Buttons removed from a folder leave the dock and the order of the profile
  useEffect(() => {
    if (!loaded || !buttons.isSuccess) return;
    const pruned = pruneProfile(profile, buttons.data.folders);
    if (JSON.stringify({ ...pruned, updatedAt: "" }) !== JSON.stringify({ ...profile, updatedAt: "" })) update((current) => pruneProfile(current, buttons.data.folders));
  }, [loaded, buttons.isSuccess, buttons.data, profile, update]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && setEditing(false);
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  // The pane opens with the folder the user had open last time
  useEffect(() => {
    if (screen || !buttons.isSuccess || !loaded) return;
    const last = profile.lastFolder;
    setScreen(last && buttons.data.folders.some((folder) => folder.id === last) ? { kind: "folder", folder: last } : { kind: "home" });
  }, [buttons.isSuccess, buttons.data, screen, loaded, profile.lastFolder]);

  if (buttons.isPending) return <p className="page">Loading…</p>;
  if (buttons.isError) return <p className="page error">{buttons.error.message}</p>;

  const overview = buttons.data;
  const current = screen ?? { kind: "home" as const };
  const folderId = current.kind === "folder" || current.kind === "stats" ? current.folder : undefined;
  const folder = folderId ? overview.folders.find((candidate) => candidate.id === folderId) : undefined;

  const go = (next: Screen) => {
    const forward = depth(next) > depth(current);
    const rect = openedFrom.current;
    const box = main.current?.getBoundingClientRect();
    const origin = rect && box ? `${rect.left + rect.width / 2 - box.left}px ${rect.top + rect.height / 2 - box.top + (main.current?.scrollTop ?? 0)}px` : transition.origin;
    openedFrom.current = null;
    setTransition({ forward, origin });
    setEditing(false);
    setScreen(next);
    if (next.kind === "home" || next.kind === "folder") {
      const lastFolder = next.kind === "folder" ? next.folder : null;
      if (lastFolder !== profile.lastFolder) update((value) => ({ ...value, lastFolder }));
    }
  };

  const cycleSize = () => update((value) => ({ ...value, size: sizes[(sizes.indexOf(value.size) + 1) % sizes.length] }));

  return (
    <EditContext.Provider value={{ editing, start: () => setEditing(true) }}>
      <div className={`pane projects tiles-${profile.size} ${profile.effects ? "" : "no-effects"}`}>
        <Header overview={overview} screen={current} folder={folder} onGo={go} onSize={cycleSize} size={profile.size} editing={editing} onDone={() => setEditing(false)} onRefresh={() => void bridge.invoke(paneCommands.RefreshButtons, null).finally(() => void buttons.refetch())} />
        <main
          className="pane-main"
          ref={main}
          onClick={(event) => {
            if (editing && event.target === event.currentTarget) setEditing(false);
          }}
          onClickCapture={(event) => {
            const tile = (event.target as HTMLElement).closest(".tile");
            if (tile) openedFrom.current = tile.getBoundingClientRect();
          }}
        >
          <motion.div
            key={screenKey(current)}
            className="screen"
            initial={screen === null ? false : screenEnter(transition.forward, transition.origin)}
            animate={{ scale: 1, opacity: 1 }}
            transition={spring}
          >
            {current.kind === "profile" ? (
              <ProfileScreen overview={overview} onStats={() => go({ kind: "stats" })} />
            ) : current.kind === "stats" ? (
              <StatsScreen overview={overview} folder={current.folder} />
            ) : current.kind === "home" || !folder ? (
              <HomeScreen overview={overview} onOpen={(id) => go({ kind: "folder", folder: id })} />
            ) : (
              <FolderScreen folder={folder} group={current.group} onOpenGroup={(group) => go({ kind: "folder", folder: folder.id, group })} />
            )}
          </motion.div>
        </main>
        <Dock overview={overview} />
      </div>
    </EditContext.Provider>
  );
}

/** The navigation bar of iOS: «‹ back» on the left, the title in the middle, the actions and the avatar on the right. */
function Header(props: {
  overview: PaneOverview;
  screen: Screen;
  folder?: PaneFolder;
  size: TileSize;
  editing: boolean;
  onGo: (screen: Screen) => void;
  onSize: () => void;
  onDone: () => void;
  onRefresh: () => void;
}) {
  const { screen, folder } = props;
  const actions = useActions();
  const toastError = (message: string) => actions.toast(message, "error");

  const back =
    screen.kind === "stats"
      ? screen.folder
        ? { label: folder?.name ?? "Back", to: { kind: "folder", folder: screen.folder } as Screen }
        : { label: "Profile", to: { kind: "profile" } as Screen }
      : screen.kind === "profile"
        ? { label: "Folders", to: { kind: "home" } as Screen }
        : screen.kind === "folder" && screen.group !== undefined
          ? { label: folder?.name ?? "Back", to: { kind: "folder", folder: screen.folder } as Screen }
          : screen.kind === "folder"
            ? { label: "Folders", to: { kind: "home" } as Screen }
            : null;
  const title =
    screen.kind === "stats" ? "Statistics" : screen.kind === "profile" ? "Profile" : screen.kind === "folder" && folder ? (screen.group ?? folder.name) : "AnalyseTool";

  const menu: ActionItem[] = [
    { label: sizeLabels[props.size], hint: "Tap to change", onClick: props.onSize, keepOpen: true },
    ...(folder && screen.kind === "folder"
      ? [
          { label: "Statistics of this folder", onClick: () => props.onGo({ kind: "stats", folder: folder.id }) },
          ...(folder.builtIn
            ? []
            : [{ label: "Versions and files", onClick: () => openWindow(`/extensions/${encodeURIComponent(folder.id)}`, toastError) }]),
        ]
      : [
          { label: "New folder…", onClick: () => openWindow("/extensions/new", toastError) },
          { label: "Manage buttons", onClick: () => openWindow("/extensions", toastError) },
        ]),
    { label: "Refresh", onClick: props.onRefresh },
  ];

  return (
    <header className="pane-header glass">
      <div className="nav-side left">
        {back && (
          <button className="back-button" onClick={() => props.onGo(back.to)} title="Back">
            <span className="chevron">‹</span>
            <span className="back-label">{back.label}</span>
          </button>
        )}
      </div>
      <strong className="pane-title" title={title}>
        {title}
      </strong>
      <div className="nav-side right">
        {props.editing ? (
          <button className="text-button bold" onClick={props.onDone}>
            Done
          </button>
        ) : (
          <>
            {screen.kind !== "profile" && screen.kind !== "stats" && (
              <button className="icon-button more" title="More" onClick={() => actions.openSheet(folder && screen.kind === "folder" ? folder.name : "AnalyseTool", <ActionList items={menu} />)}>
                ⋯
              </button>
            )}
            <button className="avatar" title={`Profile of ${props.overview.userName}`} onClick={() => props.onGo({ kind: "profile" })}>
              {props.overview.userName.slice(0, 2).toUpperCase()}
            </button>
          </>
        )}
      </div>
    </header>
  );
}

type ActionItem = { label: string; hint?: string; onClick: () => void; keepOpen?: boolean };

/** The action sheet of iOS: a tap runs an action and closes the sheet. */
function ActionList({ items }: { items: ActionItem[] }) {
  const actions = useActions();
  return (
    <div className="action-list">
      {items.map((item) => (
        <button
          key={item.label}
          className="action-item"
          onClick={() => {
            if (!item.keepOpen) actions.closeSheet();
            item.onClick();
          }}
        >
          <span>{item.label}</span>
          {item.hint && <span className="muted small">{item.hint}</span>}
        </button>
      ))}
    </div>
  );
}

/** The first screen: AnalyseTool and every extension with buttons as a tile, and «+» for a new button. */
function HomeScreen({ overview, onOpen }: { overview: PaneOverview; onOpen: (id: string) => void }) {
  const actions = useActions();
  const edit = useContext(EditContext);
  const { profile, update } = useProfile();
  const sorted = ordered(overview.folders, (folder) => folder.id, profile.order.home);

  return (
    <>
      <h1 className="large-title">
        Folders
        {!bridge.inRevit && <span className="badge">mock</span>}
      </h1>
      <div className="grid">
        {sorted.map((folder) => (
          <Tile
            key={folder.id}
            dragKey={folder.id}
            dragGroup="home"
            onMove={(from, to) => update((value) => ({ ...value, order: { ...value.order, home: move(sorted.map((item) => item.id), from, to) } }))}
            editing={edit.editing}
            onEdit={edit.start}
            label={folder.name}
            title={`${folder.name}\n${folder.buttons.length} button(s)${folder.enabled ? "" : "\nTurned off"}`}
            icon={<FolderIcon folder={folder} />}
            className="folder"
            badge={folder.problems.length > 0 ? "error" : null}
            dim={!folder.enabled}
            onClick={() => onOpen(folder.id)}
          />
        ))}
        {!edit.editing && (
          <Tile
            label="New folder"
            className="add"
            title="A new folder with its first button: C# that AnalyseTool compiles, with its own versions"
            icon={<span className="tile-icon add">+</span>}
            onClick={() => openWindow("/extensions/new", (message) => actions.toast(message, "error"))}
          />
        )}
      </div>
      <p className="muted small center">Each extension is a folder of buttons. In a folder, right-click a button: + keeps it in the dock below, ☆ puts it on the Revit ribbon.</p>
    </>
  );
}

/** A folder: its buttons and its pulldowns (as folders), in the order the user put them. */
function FolderScreen(props: { folder: PaneFolder; group?: string; onOpenGroup: (group: string) => void }) {
  const { folder, group } = props;
  const actions = useActions();
  const edit = useContext(EditContext);
  const { profile, update } = useProfile();

  type Item = { key: string; group?: string; button?: PaneButton };
  const groups = [...new Set(folder.buttons.flatMap((button) => (button.group ? [button.group] : [])))];
  const items: Item[] =
    group === undefined
      ? [
          ...folder.buttons.filter((button) => !button.group).map((button) => ({ key: `btn:${button.key}`, button })),
          ...groups.map((name) => ({ key: `group:${name}`, group: name })),
        ]
      : folder.buttons.filter((button) => button.group === group).map((button) => ({ key: `btn:${button.key}`, button }));
  const order = profile.order[folder.id];
  const sorted = ordered(items, (item) => item.key, order);

  const onMove = (from: string, to: string) => {
    const keys = move(sorted.map((item) => item.key), from, to);
    const rest = (order ?? []).filter((key) => !keys.includes(key));
    update((value) => ({ ...value, order: { ...value.order, [folder.id]: [...keys, ...rest] } }));
  };

  const togglePin = (button: PaneButton) =>
    update((value) => ({ ...value, dock: value.dock.includes(button.key) ? value.dock.filter((item) => item !== button.key) : [...value.dock, button.key] }));

  return (
    <>
      <h1 className="large-title">{group ?? folder.name}</h1>
      {!folder.enabled && <div className="banner">This extension is turned off — turn it on in Manage buttons.</div>}
      {folder.problems.length > 0 && (
        <button className="banner error" onClick={() => actions.openSheet(`Problems of ${folder.name}`, <ProblemList problems={folder.problems} />)}>
          {folder.problems.length} problem(s) — the last good build still runs. Tap for details.
        </button>
      )}
      <div className="grid">
        {sorted.map((item) =>
          item.button ? (
            <ButtonTile
              key={item.key}
              folder={folder}
              button={item.button}
              dragKey={item.key}
              dragGroup="grid"
              onMove={onMove}
              editBadge={
                profile.dock.includes(item.button.key)
                  ? { symbol: "−", title: "Remove from the dock", onClick: () => togglePin(item.button!) }
                  : { symbol: "+", title: "Add to the dock", onClick: () => togglePin(item.button!) }
              }
            />
          ) : (
            <Tile
              key={item.key}
              dragKey={item.key}
              dragGroup="grid"
              onMove={onMove}
              editing={edit.editing}
              onEdit={edit.start}
              className="folder"
              label={item.group!}
              icon={<FolderIcon folder={{ ...folder, buttons: folder.buttons.filter((button) => button.group === item.group) }} />}
              onClick={() => props.onOpenGroup(item.group!)}
            />
          ),
        )}
      </div>
      {sorted.length === 0 && <p className="muted small center">No buttons here.</p>}
    </>
  );
}

/** A button as a tile: a tap runs it; in the edit mode ✎ opens its versions and files (extensions only). */
function ButtonTile(props: {
  folder: PaneFolder;
  button: PaneButton;
  dragKey: string;
  dragGroup: string;
  onMove: (from: string, to: string) => void;
  editBadge: { symbol: "−" | "+"; title: string; onClick: () => void };
}) {
  const { folder, button } = props;
  const actions = useActions();
  const edit = useContext(EditContext);
  const { profile, update } = useProfile();
  const starred = profile.ribbon.includes(button.key);
  return (
    <Tile
      dragKey={props.dragKey}
      dragGroup={props.dragGroup}
      onMove={props.onMove}
      editing={edit.editing}
      onEdit={edit.start}
      editBadge={props.editBadge}
      onChange={
        folder.builtIn
          ? null
          : { title: "Versions and files", onClick: () => openWindow(`/extensions/${encodeURIComponent(folder.id)}`, (message) => actions.toast(message, "error")) }
      }
      star={{
        on: starred,
        title: starred ? "Remove from the Revit ribbon" : "Put on the Revit ribbon (Favourites panel)",
        onClick: () =>
          update((value) => ({
            ...value,
            ribbon: value.ribbon.includes(button.key) ? value.ribbon.filter((item) => item !== button.key) : [...value.ribbon, button.key],
          })),
      }}
      label={button.name}
      title={`${button.name}${button.tooltip ? `\n${button.tooltip}` : ""}\n${folder.name}${starred ? "\n★ On the Revit ribbon" : ""}`}
      icon={<TileIcon glyph={button.glyph} image={button.image} title={button.name} />}
      badge={folder.problems.length > 0 ? "error" : null}
      busy={actions.isRunning(button.key)}
      dim={!folder.enabled}
      onClick={() => actions.activate(folder, button)}
    />
  );
}

/** A folder shows the first icons of its buttons, like a folder of apps — also with one button: a folder is never a button. */
function FolderIcon({ folder }: { folder: PaneFolder }) {
  return (
    <span className="tile-icon folder-icon">
      {folder.buttons.slice(0, 4).map((button) => (
        <TileIcon key={button.key} glyph={button.glyph} image={button.image} title={button.name} />
      ))}
    </span>
  );
}

/** The dock: the favourite buttons of any folder, floating at the bottom on frosted glass. */
function Dock({ overview }: { overview: PaneOverview }) {
  const { profile, update } = useProfile();
  const pinned = profile.dock.flatMap((key) => {
    const found = findButton(overview.folders, key);
    return found ? [{ key, ...found }] : [];
  });

  // The toast sits just above the dock, whose height follows the tile size and the pinned buttons
  const nav = useRef<HTMLElement | null>(null);
  useEffect(() => {
    const element = nav.current;
    if (!element) return;
    const observer = new ResizeObserver(() => document.documentElement.style.setProperty("--dock-height", `${element.offsetHeight}px`));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <nav className="dock glass" aria-label="Dock" ref={nav}>
      {pinned.length === 0 && <span className="dock-hint">Right-click a button and tap + to keep it here</span>}
      {pinned.map(({ key, folder, button }) => (
        <ButtonTile
          key={key}
          folder={folder}
          button={button}
          dragKey={key}
          dragGroup="dock"
          onMove={(from, to) => update((value) => ({ ...value, dock: move(value.dock, from, to) }))}
          editBadge={{ symbol: "−", title: "Remove from the dock", onClick: () => update((value) => ({ ...value, dock: value.dock.filter((item) => item !== key) })) }}
        />
      ))}
    </nav>
  );
}
