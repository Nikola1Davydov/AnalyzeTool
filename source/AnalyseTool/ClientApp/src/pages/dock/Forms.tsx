import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import {
  type AddExtensionButtonRequest,
  type CreateExtensionFolderRequest,
  type ExtensionSummary,
  extensionCommands,
} from "../../api/extensions";
import type { PaneFolder } from "../../api/pane";
import { messageOf, useActions } from "./Actions";


/** After a change of the folders: the pane, the extension lists and the statistics read again. */
function useRefreshAll() {
  const client = useQueryClient();
  return () => void client.invalidateQueries();
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
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim()) create.mutate({ name: name.trim(), description: description.trim() || null });
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
      <p className="muted small">A folder of buttons that belong together, with its own version history. Its buttons go on the ribbon panel «Extensions».</p>
      <button type="submit" disabled={!name.trim() || create.isPending}>
        {create.isPending ? "Creating…" : "Create folder"}
      </button>
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
      className="sheet-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (name.trim()) add.mutate({ id: folder.id, name: name.trim(), description: description.trim() || null, template: "CommandWithPage" });
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
      <button type="submit" disabled={!name.trim() || add.isPending}>
        {add.isPending ? "Adding…" : `Add to ${folder.name}`}
      </button>
      {add.isError && <p className="error small">{messageOf(add.error)}</p>}
    </form>
  );
}
