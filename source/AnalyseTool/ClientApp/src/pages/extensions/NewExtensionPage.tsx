import {
  Button,
  Card,
  Dropdown,
  Field,
  Input,
  Link,
  MessageBar,
  MessageBarBody,
  Option,
  Text,
  Textarea,
} from "@fluentui/react-components";
import { CheckmarkCircle24Filled, Code16Regular, FolderOpen16Regular, Open16Regular } from "@fluentui/react-icons";
import { useState } from "react";
import { bridge } from "revit-bridge";
import { joinPath, openPath } from "../../api/app";
import {
  type AddExtensionButtonRequest,
  type CreateExtensionRequest,
  type ExtensionFile,
  type ExtensionList,
  type ExtensionRoots,
  type ExtensionSummary,
  type ExtensionTemplate,
  extensionCommands,
} from "../../api/extensions";
import { plural } from "../../components/format";
import { errorText, useNotify } from "../../components/notify";
import { Page } from "../../components/Page";
import { useAppAction } from "../../components/useAppAction";
import { useAppQuery } from "../../revit/hooks";
import { navigate, useRoute } from "../../router";

const ID_PATTERN = /^[A-Za-z0-9._-]+$/;

/**
 * The «New button» form: a button in one of your folders, or in a new folder — C# sources AnalyseTool compiles itself, saved as
 * a version. `?folder=<id>` picks the folder.
 */
export function NewExtensionPage() {
  const [created, setCreated] = useState<ExtensionSummary | null>(null);
  const [formKey, setFormKey] = useState(0);

  return (
    <Page title="New button" back={{ label: "Extensions", path: "/extensions" }} narrow>
      {created ? (
        <Created
          extension={created}
          onAnother={() => {
            setCreated(null);
            setFormKey((key) => key + 1);
          }}
        />
      ) : (
        <CreateForm key={formKey} onCreated={setCreated} />
      )}
    </Page>
  );
}

function CreateForm({ onCreated }: { onCreated: (extension: ExtensionSummary) => void }) {
  const roots = useAppQuery<ExtensionRoots>(extensionCommands.GetExtensionRoots);
  const list = useAppQuery<ExtensionList>(extensionCommands.GetExtensions);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  // Every new button is a page with its C# command
  const template: ExtensionTemplate = "CommandWithPage";
  const [panel, setPanel] = useState("");
  const [root, setRoot] = useState<string | null>(null);
  const [id, setId] = useState("");
  const [showAdvanced, setShowAdvanced] = useState(false);
  const { query } = useRoute();
  /** "" — a new folder; otherwise the id of the folder the button goes into. */
  const [folder, setFolder] = useState(query.get("folder") ?? "");

  const create = useAppAction<ExtensionSummary, CreateExtensionRequest>(extensionCommands.CreateExtension, {
    failure: "Could not create the button",
    onSuccess: onCreated,
  });
  const add = useAppAction<ExtensionSummary, AddExtensionButtonRequest>(extensionCommands.AddExtensionButton, {
    failure: "Could not add the button",
    onSuccess: onCreated,
  });
  // Your own folders that take C#: sources or pages, not packages and not DLLs
  const folders = (list.data?.extensions ?? []).filter((item) => item.zone === "Dev" && item.kind !== "Dll");

  const devRoots = (roots.data?.roots ?? []).filter((item) => item.zone === "Dev");
  const chosenRoot = root ?? roots.data?.authoringRoot ?? null;

  const idError =
    id.trim() && !ID_PATTERN.test(id.trim())
      ? "Letters, digits, '.', '-' and '_' only."
      : id.trim() && list.data?.extensions.some((item) => item.id.toLowerCase() === id.trim().toLowerCase())
        ? "An extension with this id exists."
        : undefined;
  const canCreate = !!name.trim() && (folder !== "" || !idError) && !create.isPending && !add.isPending;

  function submit() {
    if (!canCreate) return;
    if (folder) {
      add.mutate({ id: folder, name: name.trim(), description: description.trim() || null, template });
      return;
    }
    create.mutate({
      name: name.trim(),
      description: description.trim() || null,
      id: id.trim() || null,
      template,
      panel: panel.trim() || null,
      root: chosenRoot,
    });
  }

  return (
    <form
      className="form"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <Text className="muted">
        A button is a folder of a ribbon tab (<code>Sheets.tab\Renumber.pushbutton\</code>) with its C# beside it, compiled by
        AnalyseTool itself — no project, no Visual Studio. The button appears at once, and every save is kept in its version
        history.
      </Text>

      <Field label="Folder" hint="A folder holds buttons that belong together; each has its own version history.">
        <Dropdown
          value={folder ? (folders.find((item) => item.id === folder)?.name ?? folder) : "New folder"}
          selectedOptions={[folder]}
          onOptionSelect={(_, data) => setFolder(data.optionValue ?? "")}
        >
          <Option value="" text="New folder">
            New folder (named after the button)
          </Option>
          {folders.map((item) => (
            <Option key={item.id} value={item.id} text={item.name}>
              {item.name} <span className="muted">· {plural(item.buttons.length, "button")}</span>
            </Option>
          ))}
        </Dropdown>
      </Field>

      <Field label="Name" required hint="The text of the ribbon button.">
        <Input value={name} onChange={(_, data) => setName(data.value)} placeholder="Count walls" autoFocus />
      </Field>

      <Field label="Description" hint="The tooltip of the button and the description of its command for AI clients.">
        <Textarea
          value={description}
          onChange={(_, data) => setDescription(data.value)}
          placeholder="Counts the walls of the active view by type."
          resize="vertical"
        />
      </Field>


      {!folder && (
      <>
      <Field label="Ribbon panel" hint="«Extensions» when empty.">
        <Input value={panel} onChange={(_, data) => setPanel(data.value)} placeholder="Extensions" />
      </Field>

      <Field label="Folder" hint="One of your folders of extensions; add more in Settings.">
        <Dropdown
          value={chosenRoot ?? ""}
          selectedOptions={chosenRoot ? [chosenRoot] : []}
          onOptionSelect={(_, data) => setRoot(data.optionValue ?? null)}
          placeholder={roots.isPending ? "Loading…" : "The default folder"}
        >
          {devRoots.map((item) => (
            <Option key={item.path} value={item.path} text={item.path}>
              {item.path}
              {item.path === roots.data?.authoringRoot && <span className="muted"> (default for new buttons)</span>}
            </Option>
          ))}
        </Dropdown>
      </Field>

      {showAdvanced ? (
        <Field
          label="Id"
          hint="The prefix of its command names; derived from the name when empty. Cannot change later."
          validationState={idError ? "error" : "none"}
          validationMessage={idError}
        >
          <Input value={id} onChange={(_, data) => setId(data.value)} placeholder="CountWalls" />
        </Field>
      ) : (
        <Link as="button" type="button" onClick={() => setShowAdvanced(true)}>
          Set the id yourself…
        </Link>
      )}
      </>
      )}

      <div className="form-actions">
        <Button appearance="primary" type="submit" disabled={!canCreate}>
          {create.isPending || add.isPending ? "Creating…" : folder ? "Add to the folder" : "Create"}
        </Button>
        <Button appearance="secondary" onClick={() => navigate("/extensions")}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

function Created({ extension, onAnother }: { extension: ExtensionSummary; onAnother: () => void }) {
  const notify = useNotify();
  const files = useAppQuery<ExtensionFile[]>(extensionCommands.ListExtensionFiles, { id: extension.id });

  // The file to edit first: the C# of the command, else the page
  const main =
    files.data?.find((file) => /^src[\\/].*\.cs$/i.test(file.path)) ??
    files.data?.find((file) => /\.html$/i.test(file.path)) ??
    files.data?.find((file) => file.path === "plugin.json");

  const open = (path: string) => openPath(path).catch((error) => notify.failed(error, "Could not open"));

  return (
    <Card className="created-card">
      <div className="created-title">
        <CheckmarkCircle24Filled className="success-icon" />
        <Text size={500} weight="semibold">
          «{extension.name}» is on the ribbon
        </Text>
      </div>
      <Text>The files are here — edit them in any editor, the button reloads when you save:</Text>
      <code className="path block">{extension.directory}</code>
      {extension.errors.length > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>{extension.errors.join("\n")}</MessageBarBody>
        </MessageBar>
      )}
      {files.isError && <Text className="error-text small">{errorText(files.error)}</Text>}
      <div className="form-actions">
        <Button appearance="primary" icon={<Open16Regular />} onClick={() => navigate(`/extensions/${encodeURIComponent(extension.id)}`)}>
          Open its page
        </Button>
        <Button icon={<FolderOpen16Regular />} onClick={() => open(extension.directory)}>
          Open folder
        </Button>
        <Button icon={<Code16Regular />} disabled={!main} onClick={() => main && open(joinPath(extension.directory, main.path))}>
          {main ? `Open ${main.path}` : "Open source file"}
        </Button>
        <Button appearance="subtle" onClick={onAnother}>
          Create another
        </Button>
      </div>
      {!bridge.inRevit && <Text className="muted small">In Revit the new button appears on the ribbon right away.</Text>}
    </Card>
  );
}
