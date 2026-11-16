import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import {
  type AddExtensionButtonRequest,
  type CreateExtensionFolderRequest,
  type ExtensionSummary,
  type MoveExtensionButtonRequest,
  type SetExtensionPanelRequest,
  type UpdateExtensionButtonRequest,
  type UpdateExtensionFolderRequest,
  extensionCommands,
} from "../../api/extensions";
import type { PaneButton, PaneFolder } from "../../api/pane";
import { useProfile, folderOf } from "./profile";
import { messageOf, SHEET_FORM, useActions } from "./Actions";


/** After a change of the folders: the pane, the extension lists and the statistics read again. */
function useRefreshAll() {
  const client = useQueryClient();
  // Not the profile: the pane owns it and may have just changed it (a renamed button) — reading it back could race the
  // write and bring the old one
  return () => void client.invalidateQueries({ predicate: (query) => query.queryKey[0] !== "paneProfile" });
}

/** «New folder»: a name (and what its buttons are for) — an empty folder with its own version history. */
export function NewFolderForm({ onCreated }: { onCreated: (id: string) => void }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const create = useMutation({
    mutationFn: (request: CreateExtensionFolderRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.CreateExtensionFolder, request),
    onSuccess: (folder) => {
      refresh();
      actions.closeSheet();
      actions.toast(`Folder «${folder.name}» created — add its first button with +`);
      onCreated(folder.id);
    },
  });

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim() && !create.isPending) create.mutate({ name: name.trim(), description: description.trim() || null });
      }}
    >
      <label className="field">
        <span>Name</span>
        <input autoFocus value={name} onChange={(event) => setName(event.target.value)} placeholder="Sheets" />
      </label>
      <label className="field">
        <span>What its buttons are for</span>
        <textarea rows={3} value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Create, renumber and check sheets." />
      </label>
      <p className="muted small">A folder of buttons that belong together, with its own version history.</p>
      {create.isPending && <p className="muted small">Creating…</p>}
      {create.isError && <p className="error small">{messageOf(create.error)}</p>}
    </form>
  );
}

/** «+ New button» in a folder: a name and what it does — a page with its C# command, added to the folder as a version. */
export function NewButtonForm({ folder, panel, onOpenFiles }: { folder: PaneFolder; panel?: string; onOpenFiles: (id: string) => void }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const add = useMutation({
    mutationFn: (request: AddExtensionButtonRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.AddExtensionButton, request),
    onSuccess: (created, request) => {
      refresh();
      actions.closeSheet();
      actions.toast(`«${request.name}» added to ${folder.name} — tap here to edit its code`, "ok", (
        <div className="sheet-form">
          <p className="small">
            The button runs a small example now. Its code is in the folder <strong>{folder.name}</strong>: edit it in any editor, or let your AI
            client do it — saving reloads the button.
          </p>
          <button onClick={() => onOpenFiles(created.id)}>Open its files and versions</button>
        </div>
      ));
    },
  });

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim() && !add.isPending) add.mutate({ id: folder.id, name: name.trim(), description: description.trim() || null, template: "CommandWithPage", panel: panel ?? null });
      }}
    >
      <label className="field">
        <span>Name</span>
        <input autoFocus value={name} onChange={(event) => setName(event.target.value)} placeholder="Renumber sheets" />
      </label>
      <label className="field">
        <span>What it does</span>
        <textarea rows={3} value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Renumbers the selected sheets from 101." />
      </label>
      <p className="muted small">A tap opens its page; the page calls its C# command in Revit. Both start as a small example to edit.</p>
      {add.isPending && <p className="muted small">Adding…</p>}
      {add.isError && <p className="error small">{messageOf(add.error)}</p>}
    </form>
  );
}

/** ✎ on a button: its text and what it does. A new text is a new key — the dock, the stars and the order follow it. */
export function EditButtonForm({ folder, folders, button, onOpenFiles }: { folder: PaneFolder; folders: PaneFolder[]; button: PaneButton; onOpenFiles: () => void }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const { update } = useProfile();
  const [name, setName] = useState(button.name);
  const [tooltip, setTooltip] = useState(button.tooltip && button.tooltip !== button.name ? button.tooltip : "");
  const [dockable, setDockable] = useState(button.dockable === true);
  const save = useMutation({
    mutationFn: (request: UpdateExtensionButtonRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.UpdateExtensionButton, request),
    onSuccess: (_, request) => {
      const newKey = `${folderOf(button.key)}/${request.name.split(/[\r\n]+/).map((part) => part.trim()).filter(Boolean).join(" ")}`;
      if (newKey !== button.key) {
        const rename = (key: string) => (key === button.key ? newKey : key);
        update((profile) => ({
          ...profile,
          dock: profile.dock.map(rename),
          ribbon: profile.ribbon.map(rename),
          hiddenButtons: profile.hiddenButtons.map(rename),
          order: Object.fromEntries(Object.entries(profile.order).map(([id, keys]) => [id, keys.map((key) => (key === `btn:${button.key}` ? `btn:${newKey}` : key))])),
        }));
      }
      refresh();
      actions.closeSheet();
      actions.toast(`«${request.name}» saved`);
    },
  });

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim() && !save.isPending) save.mutate({ id: folder.id, button: button.name, name: name.trim(), tooltip: tooltip.trim() || null, dockable: button.hasPage ? dockable : null });
      }}
    >
      <label className="field">
        <span>Name</span>
        <input autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      </label>
      <label className="field">
        <span>What it does</span>
        <textarea rows={3} value={tooltip} onChange={(event) => setTooltip(event.target.value)} placeholder="The tooltip of the button" />
      </label>
      <p className="muted small">Saved as a version of {folder.name}. The code keeps its file; a new name starts the statistics of the button anew.</p>
      {save.isPending && <p className="muted small">Saving…</p>}
      {button.hasPage && (
        <>
          <label className="switch-row">
            <span>Opens in the pane</span>
            <button type="button" role="switch" aria-checked={dockable} className={`switch ${dockable ? "on" : ""}`} onClick={() => setDockable(!dockable)}>
              <span className="switch-knob" />
            </button>
          </label>
          <p className="muted small">A small interface — a few fields and a button — fits the pane. A big table or a report opens in a window (the default).</p>
        </>
      )}
      {!button.group && <MoveTo folder={folder} folders={folders} button={button} />}
      <button type="button" onClick={onOpenFiles}>
        Code, files and versions…
      </button>
      {save.isError && <p className="error small">{messageOf(save.error)}</p>}
    </form>
  );
}

/** ✎ on a folder: its name and what its buttons are for. */
export function EditFolderForm({ folder }: { folder: PaneFolder }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const [name, setName] = useState(folder.name);
  const [description, setDescription] = useState(folder.description && folder.description !== folder.name ? folder.description : "");
  const save = useMutation({
    mutationFn: (request: UpdateExtensionFolderRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.UpdateExtensionFolder, request),
    onSuccess: (_, request) => {
      refresh();
      actions.closeSheet();
      actions.toast(`Folder «${request.name}» saved`);
    },
  });

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim() && !save.isPending) save.mutate({ id: folder.id, name: name.trim(), description: description.trim() || null });
      }}
    >
      <label className="field">
        <span>Name</span>
        <input autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      </label>
      <label className="field">
        <span>What its buttons are for</span>
        <textarea rows={3} value={description} onChange={(event) => setDescription(event.target.value)} />
      </label>
      <p className="muted small">
        {folder.id.toLowerCase().endsWith(".tab")
          ? "The name of the tab on the ribbon. Its folder on disk keeps its name."
          : `Saved as a version of the folder. Its id (${folder.id}) and its commands stay.`}
      </p>
      {save.isPending && <p className="muted small">Saving…</p>}
      {save.isError && <p className="error small">{messageOf(save.error)}</p>}
    </form>
  );
}

/** «Move to…»: another panel of the folder, or a panel of another folder of the user. */
function MoveTo({ folder, folders, button }: { folder: PaneFolder; folders: PaneFolder[]; button: PaneButton }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  // A button moves between tabs (its folder goes along) or between folders of plugin.json, not from one to the other
  const isTab = (candidate: PaneFolder) => candidate.id.toLowerCase().endsWith(".tab");
  const targets = folders.filter((candidate) => candidate.canAddButtons && isTab(candidate) === isTab(folder));
  const [to, setTo] = useState(folder.id);
  const target = targets.find((candidate) => candidate.id === to) ?? folder;
  const panels = target.panels?.length ? target.panels : [target.panel ?? target.name];
  const current = button.panel ?? folder.panel ?? folder.name;
  const [panel, setPanel] = useState(current);
  const move = useMutation({
    mutationFn: (request: MoveExtensionButtonRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.MoveExtensionButton, request),
    onSuccess: () => {
      refresh();
      actions.closeSheet();
      actions.toast(`«${button.name}» moved to ${target.name} › ${panel}`);
    },
  });
  const same = to === folder.id && panel.toLowerCase() === current.toLowerCase();

  return (
    <details className="advanced">
      <summary>Move to…</summary>
      <label className="field">
        <span>Folder (tab)</span>
        <select
          value={to}
          onChange={(event) => {
            setTo(event.target.value);
            const next = targets.find((candidate) => candidate.id === event.target.value);
            setPanel(next?.panels?.[0] ?? next?.name ?? "");
          }}
        >
          {targets.map((candidate) => (
            <option key={candidate.id} value={candidate.id}>
              {candidate.name}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>Panel</span>
        <select value={panel} onChange={(event) => setPanel(event.target.value)}>
          {panels.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>
      </label>
      {to !== folder.id &&
        (isTab(folder) ? (
          <p className="muted small">Its folder moves with its code, page and versions.</p>
        ) : (
          <p className="muted small">Its C# file and page go along; its command becomes {target.id}.… A version is saved in both folders.</p>
        ))}
      <button type="button" disabled={same || move.isPending} onClick={() => move.mutate({ id: folder.id, button: button.name, panel, toId: to === folder.id ? null : to })}>
        {move.isPending ? "Moving…" : "Move"}
      </button>
      {move.isError && <p className="error small">{messageOf(move.error)}</p>}
    </details>
  );
}

/** A panel of a folder: a new one, or a new name for one — its buttons follow. */
export function PanelForm({ folder, panel }: { folder: PaneFolder; panel?: string }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const [name, setName] = useState(panel ?? "");
  const save = useMutation({
    mutationFn: (request: SetExtensionPanelRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.SetExtensionPanel, request),
    onSuccess: (_, request) => {
      refresh();
      actions.closeSheet();
      actions.toast(panel ? `Panel «${request.name}» saved` : `Panel «${request.name}» added — drag buttons into it`);
    },
  });

  return (
    <form
      id={SHEET_FORM}
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim() && !save.isPending) save.mutate({ id: folder.id, panel: panel ?? null, name: name.trim() });
      }}
    >
      <label className="field">
        <span>Name</span>
        <input autoFocus value={name} onChange={(event) => setName(event.target.value)} placeholder="Checks" />
      </label>
      <p className="muted small">A section of {folder.name} — a panel of its tab on the Revit ribbon.</p>
      {save.isPending && <p className="muted small">Saving…</p>}
      {save.isError && <p className="error small">{messageOf(save.error)}</p>}
    </form>
  );
}
