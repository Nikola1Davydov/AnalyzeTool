import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import {
  type AddExtensionButtonRequest,
  type CreateExtensionFolderRequest,
  type ExtensionSummary,
  type ExtensionTemplate,
  extensionCommands,
} from "../../api/extensions";
import type { PaneFolder } from "../../api/pane";
import { messageOf, useActions } from "./Actions";

const kinds: { value: ExtensionTemplate; label: string; hint: string }[] = [
  { value: "Command", label: "Runs C#", hint: "A tap runs a command in Revit and shows what it returns." },
  { value: "CommandWithPage", label: "Page + C#", hint: "A tap opens a page (inputs, a table) whose buttons call your C#." },
  { value: "Page", label: "Page only", hint: "A tap opens an HTML page, no C#." },
];

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

/** «+ New button» in a folder: a name, what it does, and what a tap does — C# (and/or a page) added to the folder as a version. */
export function NewButtonForm({ folder, onOpenFiles }: { folder: PaneFolder; onOpenFiles: () => void }) {
  const actions = useActions();
  const refresh = useRefreshAll();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [kind, setKind] = useState<ExtensionTemplate>("Command");
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
        if (name.trim()) add.mutate({ id: folder.id, name: name.trim(), description: description.trim() || null, template: kind });
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
      <div className="segmented" role="radiogroup">
        {kinds.map((option) => (
          <button type="button" key={option.value} role="radio" aria-checked={kind === option.value} className={kind === option.value ? "on" : ""} onClick={() => setKind(option.value)}>
            {kind === option.value && <span className="segmented-thumb" />}
            <span className="segmented-label">{option.label}</span>
          </button>
        ))}
      </div>
      <p className="muted small">{kinds.find((option) => option.value === kind)?.hint}</p>
      <button type="submit" disabled={!name.trim() || add.isPending}>
        {add.isPending ? "Adding…" : `Add to ${folder.name}`}
      </button>
      {add.isError && <p className="error small">{messageOf(add.error)}</p>}
    </form>
  );
}
