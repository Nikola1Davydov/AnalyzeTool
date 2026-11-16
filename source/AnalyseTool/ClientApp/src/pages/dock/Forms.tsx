import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import {
  type AddExtensionButtonRequest,
  type CreateExtensionFolderRequest,
  type ExtensionSummary,
  type UpdateExtensionButtonRequest,
  type UpdateExtensionFolderRequest,
  extensionCommands,
} from "../../api/extensions";
import type { PaneButton, PaneFolder } from "../../api/pane";
import { useProfile } from "./profile";
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
  const [tab, setTab] = useState("");
  const [panel, setPanel] = useState("");
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
        if (name.trim() && !create.isPending) create.mutate({ name: name.trim(), description: description.trim() || null, tab: tab.trim() || null, panel: panel.trim() || null });
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
      <RibbonPlace
        hint="Where the buttons of this folder go on the Revit ribbon:"
        tab={tab}
        panel={panel}
        tabDefault="AnalyseTool"
        panelDefault="Extensions"
        onTab={setTab}
        onPanel={setPanel}
      />
      <p className="muted small">A folder of buttons that belong together, with its own version history.</p>
      {create.isPending && <p className="muted small">Creating…</p>}
      {create.isError && <p className="error small">{messageOf(create.error)}</p>}
    </form>
  );
}

/** «+ New button» in a folder: a name and what it does — a page with its C# command, added to the folder as a version. */
export function NewButtonForm({ folder, onOpenFiles }: { folder: PaneFolder; onOpenFiles: () => void }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [tab, setTab] = useState("");
  const [panel, setPanel] = useState("");
  const add = useMutation({
    mutationFn: (request: AddExtensionButtonRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.AddExtensionButton, request),
    onSuccess: (_, request) => {
      refresh();
      actions.closeSheet();
      actions.toast(`«${request.name}» added to ${folder.name} — tap here to edit its code`, "ok", (
        <div className="sheet-form">
          <p className="small">
            The button runs a small example now. Its code is in the folder <strong>{folder.name}</strong>: edit it in any editor, or let your AI
            client do it — saving reloads the button.
          </p>
          <button onClick={onOpenFiles}>Open its files and versions</button>
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
        if (name.trim() && !add.isPending) add.mutate({ id: folder.id, name: name.trim(), description: description.trim() || null, template: "CommandWithPage", tab: tab.trim() || null, panel: panel.trim() || null });
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
      <RibbonPlace
        hint="By default the button sits where its folder puts it. Give it a tab or a panel of its own:"
        tab={tab}
        panel={panel}
        tabDefault={folder.tab || "AnalyseTool"}
        panelDefault={folder.panel || "Extensions"}
        onTab={setTab}
        onPanel={setPanel}
      />
      <p className="muted small">A tap opens its page; the page calls its C# command in Revit. Both start as a small example to edit.</p>
      {add.isPending && <p className="muted small">Adding…</p>}
      {add.isError && <p className="error small">{messageOf(add.error)}</p>}
    </form>
  );
}

/** ✎ on a button: its text and what it does. A new text is a new key — the dock, the stars and the order follow it. */
export function EditButtonForm({ folder, button, onOpenFiles }: { folder: PaneFolder; button: PaneButton; onOpenFiles: () => void }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const { update } = useProfile();
  const [name, setName] = useState(button.name);
  const [tooltip, setTooltip] = useState(button.tooltip && button.tooltip !== button.name ? button.tooltip : "");
  const [tab, setTab] = useState(button.tab ?? "");
  const [panel, setPanel] = useState(button.panel ?? "");
  const save = useMutation({
    mutationFn: (request: UpdateExtensionButtonRequest) => bridge.invoke<ExtensionSummary>(extensionCommands.UpdateExtensionButton, request),
    onSuccess: (_, request) => {
      const newKey = `${folder.id}/${request.name.split(/[\r\n]+/).map((part) => part.trim()).filter(Boolean).join(" ")}`;
      if (newKey !== button.key) {
        const rename = (key: string) => (key === button.key ? newKey : key);
        update((profile) => ({
          ...profile,
          dock: profile.dock.map(rename),
          ribbon: profile.ribbon.map(rename),
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
        if (name.trim() && !save.isPending) save.mutate({ id: folder.id, button: button.name, name: name.trim(), tooltip: tooltip.trim() || null, tab: tab.trim(), panel: panel.trim() });
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
      <RibbonPlace
        hint="By default the button sits where its folder puts it. Give it a tab or a panel of its own:"
        tab={tab}
        panel={panel}
        tabDefault={folder.tab || "AnalyseTool"}
        panelDefault={folder.panel || "Extensions"}
        onTab={setTab}
        onPanel={setPanel}
      />
      <p className="muted small">Saved as a version of {folder.name}. The code keeps its file; a new name starts the statistics of the button anew.</p>
      {save.isPending && <p className="muted small">Saving…</p>}
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
  const [tab, setTab] = useState(folder.tab ?? "");
  const [panel, setPanel] = useState(folder.panel ?? "");
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
        if (name.trim() && !save.isPending) save.mutate({ id: folder.id, name: name.trim(), description: description.trim() || null, tab: tab.trim(), panel: panel.trim() });
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
      <RibbonPlace
        hint="Where the buttons of this folder go on the Revit ribbon:"
        tab={tab}
        panel={panel}
        tabDefault="AnalyseTool"
        panelDefault="Extensions"
        onTab={setTab}
        onPanel={setPanel}
      />
      <p className="muted small">Saved as a version of the folder. Its id ({folder.id}) and its commands stay.</p>
      {save.isPending && <p className="muted small">Saving…</p>}
      {save.isError && <p className="error small">{messageOf(save.error)}</p>}
    </form>
  );
}

/** «Advanced»: the ribbon tab and panel. Closed by default — the defaults are fine; who knows the ribbon sets them. */
function RibbonPlace(props: {
  hint: string;
  tab: string;
  panel: string;
  tabDefault: string;
  panelDefault: string;
  onTab: (value: string) => void;
  onPanel: (value: string) => void;
}) {
  return (
    <details className="advanced" open={!!(props.tab || props.panel)}>
      <summary>Advanced: ribbon tab and panel</summary>
      <p className="muted small">{props.hint}</p>
      <label className="field">
        <span>Tab</span>
        <input value={props.tab} onChange={(event) => props.onTab(event.target.value)} placeholder={props.tabDefault} />
      </label>
      <label className="field">
        <span>Panel</span>
        <input value={props.panel} onChange={(event) => props.onPanel(event.target.value)} placeholder={props.panelDefault} />
      </label>
      <p className="muted small">Empty: {props.tabDefault} › {props.panelDefault}. A tab that does not exist yet is created.</p>
    </details>
  );
}
