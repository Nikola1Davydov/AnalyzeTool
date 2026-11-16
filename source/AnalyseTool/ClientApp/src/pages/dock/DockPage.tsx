import { useQuery, useQueryClient } from "@tanstack/react-query";
import { motion, MotionConfig } from "motion/react";
import { createContext, useContext, useEffect, useRef, useState } from "react";
import { bridge } from "revit-bridge";
import { appCommands } from "../../api/app";
import { extensionCommands } from "../../api/extensions";
import { type PaneButton, type PaneFolder, type PaneOverview, paneCommands } from "../../api/pane";
import { ActionsProvider, messageOf, ProblemList, useActions } from "./Actions";
import { EditButtonForm, EditFolderForm, NewButtonForm, NewFolderForm, PanelForm } from "./Forms";
import { AboutScreen } from "./AboutScreen";
import { HowItWorks } from "./HowItWorks";
import { PackagesScreen } from "./PackagesScreen";
import { FoldersScreen, McpScreen } from "./SettingsScreen";
import { ProfileScreen } from "./ProfileScreen";
import { applyTheme, findButton, pruneProfile, type TileSize, useProfile } from "./profile";
import { StatsScreen } from "./StatsScreen";
import { move, ordered, spring, Tile, TileIcon } from "./Tile";
import "./pane.css";

/** Where the pane is: the folders, the profile, the statistics, a folder, or a group (pulldown) of a folder. */
type Screen =
  | { kind: "home" }
  | { kind: "profile" }
  | { kind: "guide" }
  | { kind: "packages" }
  | { kind: "mcp" }
  | { kind: "about" }
  | { kind: "folders" }
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
    case "guide":
      return 1;
    case "stats":
    case "packages":
    case "mcp":
    case "folders":
    case "about":
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

/** Opens the folder of an extension in Explorer — where an AI tool (Claude Code, Cursor) edits its buttons. */
function openFolder(directory: string, onError: (message: string) => void) {
  void bridge.invoke(appCommands.OpenPath, { path: directory }).catch((error: unknown) => onError(messageOf(error)));
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
  const [refreshing, setRefreshing] = useState(false);
  const [transition, setTransition] = useState({ forward: true, origin: "50% 50%" });
  const main = useRef<HTMLElement | null>(null);
  const openedFrom = useRef<DOMRect | null>(null);


  // Buttons removed from a folder leave the dock and the order of the profile. Only when a new list of buttons arrives — not
  // on every change of the profile: a renamed button is renamed in the profile first and would be pruned against the old list
  const latestProfile = useRef(profile);
  latestProfile.current = profile;
  useEffect(() => {
    if (!loaded || !buttons.isSuccess) return;
    const current = latestProfile.current;
    const pruned = pruneProfile(current, buttons.data.folders);
    if (JSON.stringify({ ...pruned, updatedAt: "" }) !== JSON.stringify({ ...current, updatedAt: "" })) update((value) => pruneProfile(value, buttons.data.folders));
  }, [loaded, buttons.isSuccess, buttons.dataUpdatedAt, buttons.data, update]);

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
        <Header overview={overview} screen={current} folder={folder} onGo={go} onSize={cycleSize} size={profile.size} editing={editing} onDone={() => setEditing(false)} onRefresh={() => {
            setRefreshing(true);
            void bridge
              .invoke(paneCommands.RefreshButtons, null)
              .catch(() => undefined)
              .finally(() => {
                setRefreshing(false);
                void buttons.refetch();
              });
          }}
          refreshing={refreshing} />
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
              <ProfileScreen
                overview={overview}
                onStats={() => go({ kind: "stats" })}
                onPackages={() => go({ kind: "packages" })}
                onMcp={() => go({ kind: "mcp" })}
                onFolders={() => go({ kind: "folders" })}
                onAbout={() => go({ kind: "about" })}
              />
            ) : current.kind === "about" ? (
              <AboutScreen />
            ) : current.kind === "packages" ? (
              <PackagesScreen />
            ) : current.kind === "mcp" ? (
              <McpScreen />
            ) : current.kind === "folders" ? (
              <FoldersScreen />
            ) : current.kind === "guide" ? (
              <HowItWorks />
            ) : current.kind === "stats" ? (
              <StatsScreen overview={overview} folder={current.folder} />
            ) : current.kind === "home" || !folder ? (
              <HomeScreen overview={overview} onOpen={(id) => go({ kind: "folder", folder: id })} onGuide={() => go({ kind: "guide" })} />
            ) : (
              <FolderScreen folder={folder} folders={overview.folders} />
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
  refreshing?: boolean;
}) {
  const { screen, folder } = props;
  const actions = useActions();
  const toastError = (message: string) => actions.toast(message, "error");

  const back =
    screen.kind === "packages" || screen.kind === "mcp" || screen.kind === "folders" || screen.kind === "about"
      ? { label: "Profile", to: { kind: "profile" } as Screen }
      : screen.kind === "stats"
      ? screen.folder
        ? { label: folder?.name ?? "Back", to: { kind: "folder", folder: screen.folder } as Screen }
        : { label: "Profile", to: { kind: "profile" } as Screen }
      : screen.kind === "profile" || screen.kind === "guide"
        ? { label: "Folders", to: { kind: "home" } as Screen }
        : screen.kind === "folder" && screen.group !== undefined
          ? { label: folder?.name ?? "Back", to: { kind: "folder", folder: screen.folder } as Screen }
          : screen.kind === "folder"
            ? { label: "Folders", to: { kind: "home" } as Screen }
            : null;
  const title =
    screen.kind === "stats" ? "Statistics" : screen.kind === "packages" ? "Packages" : screen.kind === "mcp" ? "AI clients" : screen.kind === "about" ? "About" : screen.kind === "folders" ? "Folders of buttons" : screen.kind === "profile" ? "Profile" : screen.kind === "guide" ? "How it works" : screen.kind === "folder" && folder ? (screen.group ?? folder.name) : "AnalyseTool";

  const menu: ActionItem[] = [
    { label: sizeLabels[props.size], hint: "Tap to change", onClick: props.onSize, keepOpen: true },
    ...(folder && screen.kind === "folder"
      ? [
          { label: "Statistics of this folder", onClick: () => props.onGo({ kind: "stats", folder: folder.id }) },
          ...(folder.directory ? [{ label: "Open the folder", hint: "Edit its buttons with an AI tool", onClick: () => openFolder(folder.directory!, toastError) }] : []),
          ...(folder.canAddButtons ? [{ label: "Name and description…", onClick: () => actions.openSheet(`Folder ${folder.name}`, <EditFolderForm folder={folder} />, "Save") }] : []),
          ...(folder.builtIn
            ? []
            : [{ label: "Versions and files", onClick: () => openWindow(`/extensions/${encodeURIComponent(folder.id)}`, toastError) }]),
          ...(folder.canAddButtons
            ? [
                {
                  label: folder.enabled ? "Turn the folder off" : "Turn the folder on",
                  hint: folder.enabled ? "Its buttons and commands stop until you turn it on" : undefined,
                  onClick: () =>
                    void bridge
                      .invoke(extensionCommands.SetExtensionEnabled, { id: folder.id, enabled: !folder.enabled })
                      .then(props.onRefresh)
                      .catch((error: unknown) => toastError(messageOf(error))),
                },
                {
                  label: "Remove the folder…",
                  hint: "It moves to the AnalyseTool trash with its versions",
                  onClick: () => {
                    if (!confirm(`Remove «${folder.name}»? Its tab and buttons go; the folder moves to the AnalyseTool trash with its versions.`)) return;
                    void bridge
                      .invoke<{ movedTo: string }>(extensionCommands.RemoveExtension, { id: folder.id })
                      .then((result) => {
                        actions.toast(`Removed — the folder is in ${result.movedTo}`);
                        props.onGo({ kind: "home" });
                        props.onRefresh();
                      })
                      .catch((error: unknown) => toastError(messageOf(error)));
                  },
                },
              ]
            : []),
        ]
      : [
          { label: "New folder…", onClick: () => actions.openSheet("New folder", <NewFolderForm onCreated={(id) => props.onGo({ kind: "folder", folder: id })} />, "Create") },
        ]),
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
            {(screen.kind === "home" || screen.kind === "folder") && (
              <button
                className={`icon-button refresh${props.refreshing ? " spinning" : ""}`}
                title="Refresh: read every folder again, compile what changed, rebuild the ribbon"
                disabled={props.refreshing}
                onClick={props.onRefresh}
              >
                ↻
              </button>
            )}
            {(screen.kind === "home" || screen.kind === "folder") && (
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

/**
 * The first screen: the folders — each one a tab of the Revit ribbon. In the edit mode «+»/«−» puts its tab on the ribbon or
 * takes it off (it stays here); a folder off the ribbon is grey.
 */
function HomeScreen({ overview, onOpen, onGuide }: { overview: PaneOverview; onOpen: (id: string) => void; onGuide: () => void }) {
  const actions = useActions();
  const edit = useContext(EditContext);
  const { profile, update } = useProfile();
  const sorted = ordered(overview.folders, (folder) => folder.id, profile.order.home);

  return (
    <>
      <h1 className="large-title">
        Folders
        {!bridge.inRevit && <span className="badge">mock</span>}
        <button className="help-button" title="How it works" onClick={onGuide}>
          ?
        </button>
      </h1>
      <div className="grid">
        {sorted.map((folder) => {
          const hidden = !folder.builtIn && profile.hiddenFolders.includes(folder.id);
          return (
            <Tile
              key={folder.id}
              dragKey={folder.id}
              dragGroup="home"
              onMove={(from, to) => update((value) => ({ ...value, order: { ...value.order, home: move(sorted.map((item) => item.id), from, to) } }))}
              editing={edit.editing}
              onEdit={edit.start}
              editBadge={
                folder.builtIn
                  ? null
                  : hidden
                    ? { symbol: "+", title: "Put its tab on the Revit ribbon", onClick: () => toggleIn(update, "hiddenFolders", folder.id) }
                    : { symbol: "−", title: "Take its tab off the Revit ribbon (it stays here)", onClick: () => toggleIn(update, "hiddenFolders", folder.id) }
              }
              onChange={folder.canAddButtons ? { title: "Name and description", onClick: () => actions.openSheet(`Folder ${folder.name}`, <EditFolderForm folder={folder} />, "Save") } : null}
              onOpenFolder={folder.directory ? { title: "Open the folder (edit with an AI tool)", onClick: () => openFolder(folder.directory!, (message) => actions.toast(message, "error")) } : null}
              label={folder.name}
              title={`${folder.name} — the tab «${folder.tab ?? folder.name}»\n${folder.buttons.length} button(s)${folder.enabled ? "" : "\nTurned off"}${hidden ? "\nNot on the Revit ribbon" : ""}`}
              icon={<FolderIcon buttons={folder.buttons} />}
              className="folder"
              off={hidden}
              badge={folder.problems.length > 0 ? "error" : null}
              dim={!folder.enabled}
              onClick={() => onOpen(folder.id)}
            />
          );
        })}
        {!edit.editing && (
          <Tile
            label="New folder"
            className="add"
            title="A new folder — a new tab of the ribbon — with its own version history"
            icon={<span className="tile-icon add">+</span>}
            onClick={() => actions.openSheet("New folder", <NewFolderForm onCreated={onOpen} />, "Create")}
          />
        )}
      </div>
      <p className="muted small center">
        Each folder is a tab of the Revit ribbon, its sections are the panels. Right-click to edit: ☆ keeps a button in the dock below, + / −
        puts a folder or a button on the ribbon or takes it off, drag to move.
      </p>
    </>
  );
}

/** Adds a value to a list of the profile, or takes it out. */
function toggleIn(update: ReturnType<typeof useProfile>["update"], list: "hiddenFolders" | "hiddenButtons" | "ribbon" | "dock", value: string) {
  update((profile) => ({
    ...profile,
    [list]: profile[list].includes(value) ? profile[list].filter((item) => item !== value) : [...profile[list], value],
  }));
}

/**
 * A folder: its sections — the panels of its tab — each with its buttons and pulldowns (▾). In the edit mode the buttons drag
 * between sections (of a folder of the user), a section is renamed with ✎, «+ New panel» adds one.
 */
function FolderScreen({ folder, folders }: { folder: PaneFolder; folders: PaneFolder[] }) {
  const actions = useActions();
  const edit = useContext(EditContext);
  const refresh = useQueryClient();
  const movable = folder.canAddButtons;
  const toastError = (message: string) => actions.toast(message, "error");

  type Item = { key: string; name: string; button?: PaneButton; group?: PaneButton[] };
  const sections = (folder.panels?.length ? folder.panels : [folder.panel ?? folder.name]).map((panel) => {
    const items: Item[] = [];
    for (const button of folder.buttons) {
      if ((button.panel ?? folder.panel ?? folder.name).toLowerCase() !== panel.toLowerCase()) continue;
      if (!button.group) items.push({ key: `btn:${button.key}`, name: button.name, button });
      else if (!items.some((item) => item.group && item.name === button.group))
        items.push({ key: `group:${button.group}`, name: button.group, group: folder.buttons.filter((other) => other.group === button.group) });
    }
    return { panel, items };
  });

  // A drop: on a tile — before it, in its section; on a section — at its end
  const onMove = (from: string, to: string) => {
    const name = sections.flatMap((section) => section.items).find((item) => item.key === from)?.name;
    if (!name) return;
    let panel: string | undefined;
    let before: string | undefined;
    if (to.startsWith("section:")) panel = to.slice("section:".length);
    else {
      const section = sections.find((candidate) => candidate.items.some((item) => item.key === to));
      panel = section?.panel;
      before = section?.items.find((item) => item.key === to)?.name;
    }
    if (!panel) return;
    void bridge
      .invoke(extensionCommands.MoveExtensionButton, { id: folder.id, button: name, panel, before: before ?? null })
      .then(() => refresh.invalidateQueries({ predicate: (query) => query.queryKey[0] !== "paneProfile" }))
      .catch((error: unknown) => toastError(messageOf(error)));
  };

  return (
    <>
      <h1 className="large-title">{folder.name}</h1>
      {folder.tabFromPackage && <p className="muted small">On the tab «{folder.tab}» — its author chose it.</p>}
      {!folder.enabled && <div className="banner">This extension is turned off — turn it on in Manage buttons.</div>}
      {folder.problems.length > 0 && (
        <button className="banner error" onClick={() => actions.openSheet(`Problems of ${folder.name}`, <ProblemList problems={folder.problems} />)}>
          {folder.problems.length} problem(s) — the last good build still runs. Tap for details.
        </button>
      )}
      {sections.map(({ panel, items }) => (
        <section key={panel} className={`pane-section${edit.editing && movable ? " drop" : ""}`} data-section={panel} data-tile-group="grid">
          <header className="pane-section-header">
            <span>{panel}</span>
            {edit.editing && movable && (
              <button
                className="icon-button"
                title="Rename the panel"
                onClick={() => actions.openSheet("Panel", <PanelForm folder={folder} panel={panel} />, "Save")}
              >
                ✎
              </button>
            )}
          </header>
          <div className="grid">
            {items.map((item) =>
              item.button ? (
                <ButtonTile
                  key={item.key}
                  folder={folder}
                  folders={folders}
                  button={item.button}
                  dragKey={movable ? item.key : undefined}
                  dragGroup="grid"
                  onMove={onMove}
                />
              ) : (
                <Tile
                  key={item.key}
                  dragKey={movable ? item.key : undefined}
                  dragGroup="grid"
                  onMove={onMove}
                  editing={edit.editing}
                  onEdit={edit.start}
                  className="pulldown"
                  label={item.name}
                  title={`${item.name} ▾ — a pulldown of ${item.group!.length} button(s)`}
                  icon={<FolderIcon buttons={item.group!} />}
                  onClick={() =>
                    actions.openSheet(
                      `${item.name} ▾`,
                      <div className="grid pulldown-list">
                        {item.group!.map((button) => (
                          <ButtonTile key={button.key} folder={folder} folders={folders} button={button} />
                        ))}
                      </div>,
                    )
                  }
                />
              ),
            )}
            {items.length === 0 && edit.editing && movable && <p className="muted small drop-hint">Drop buttons here</p>}
            {movable && !edit.editing && (
              <Tile
                label="New button"
                className="add"
                title={`A new button in the panel «${panel}»`}
                icon={<span className="tile-icon add">+</span>}
                onClick={() =>
                  actions.openSheet(
                    `New button in ${panel}`,
                    <NewButtonForm
                      folder={folder}
                      panel={panel}
                      onOpenFiles={() => openWindow(`/extensions/${encodeURIComponent(folder.id)}`, toastError)}
                    />,
                    "Add",
                  )
                }
              />
            )}
          </div>
        </section>
      ))}
      {movable && (
        <button className="text-button new-panel" onClick={() => actions.openSheet("New panel", <PanelForm folder={folder} />, "Add")}>
          + New panel
        </button>
      )}
    </>
  );
}

/**
 * A button as a tile: a tap runs it. In the edit mode its four corners: −/+ loads it on the Revit ribbon or not, ✎ its name,
 * description and «Move to…», ☆/★ the dock below, 📁 its folder. A button that is not on the ribbon is grey.
 */
function ButtonTile(props: {
  folder: PaneFolder;
  folders: PaneFolder[];
  button: PaneButton;
  dragKey?: string;
  dragGroup?: string;
  onMove?: (from: string, to: string) => void;
}) {
  const { folder, button } = props;
  const actions = useActions();
  const edit = useContext(EditContext);
  const { profile, update } = useProfile();
  const inDock = profile.dock.includes(button.key);
  const folderHidden = !folder.builtIn && profile.hiddenFolders.includes(folder.id);
  const loaded = button.offRibbon ? profile.ribbon.includes(button.key) : !profile.hiddenButtons.includes(button.key);
  const onRibbon = loaded && !folderHidden;
  const toastError = (message: string) => actions.toast(message, "error");
  return (
    <Tile
      dragKey={props.dragKey}
      dragGroup={props.dragGroup}
      onMove={props.onMove}
      editing={edit.editing}
      onEdit={edit.start}
      editBadge={
        loaded
          ? { symbol: "−", title: "Do not load it on the Revit ribbon (it stays here)", onClick: () => toggleIn(update, button.offRibbon ? "ribbon" : "hiddenButtons", button.key) }
          : { symbol: "+", title: "Load it on the Revit ribbon", onClick: () => toggleIn(update, button.offRibbon ? "ribbon" : "hiddenButtons", button.key) }
      }
      onChange={
        folder.canAddButtons
          ? {
              title: "Name, description, move",
              onClick: () =>
                actions.openSheet(
                  button.name,
                  <EditButtonForm
                    folder={folder}
                    folders={props.folders}
                    button={button}
                    onOpenFiles={() => openWindow(`/extensions/${encodeURIComponent(folder.id)}`, toastError)}
                  />,
                  "Save",
                ),
            }
          : null
      }
      onOpenFolder={folder.directory ? { title: "Open the folder (edit with an AI tool)", onClick: () => openFolder(folder.directory!, toastError) } : null}
      star={{ on: inDock, title: inDock ? "Remove from the dock" : "Keep in the dock (quick access)", onClick: () => toggleIn(update, "dock", button.key) }}
      label={button.name}
      title={`${button.name}${button.tooltip ? `\n${button.tooltip}` : ""}\n${folder.name}${inDock ? "\n★ In the dock" : ""}${onRibbon ? "" : "\nNot on the Revit ribbon"}`}
      icon={<TileIcon glyph={button.glyph} image={button.image} title={button.name} />}
      off={!onRibbon}
      badge={folder.problems.length > 0 ? "error" : null}
      busy={actions.isRunning(button.key)}
      dim={!folder.enabled}
      onClick={() => actions.activate(folder, button)}
    />
  );
}

/** A folder or a pulldown shows the first icons of its buttons, like a folder of apps. */
function FolderIcon({ buttons }: { buttons: PaneButton[] }) {
  return (
    <span className="tile-icon folder-icon">
      {buttons.slice(0, 4).map((button) => (
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
      {pinned.length === 0 && <span className="dock-hint">Right-click a button and tap ☆ to keep it here</span>}
      {pinned.map(({ key, folder, button }) => (
        <ButtonTile
          key={key}
          folder={folder}
          folders={overview.folders}
          button={button}
          dragKey={key}
          dragGroup="dock"
          onMove={(from, to) => update((value) => ({ ...value, dock: move(value.dock, from, to) }))}
        />
      ))}
    </nav>
  );
}
