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
  Radio,
  RadioGroup,
  Text,
  Textarea,
} from "@fluentui/react-components";
import { CheckmarkCircle24Filled, Code16Regular, FolderOpen16Regular, Open16Regular } from "@fluentui/react-icons";
import { useState } from "react";
import { bridge } from "revit-bridge";
import { joinPath, openPath } from "../../api/app";
import {
  type CreateExtensionRequest,
  type ExtensionFile,
  type ExtensionList,
  type ExtensionRoots,
  type ExtensionSummary,
  type ExtensionTemplate,
  extensionCommands,
  templateLabels,
} from "../../api/extensions";
import { errorText, useNotify } from "../../components/notify";
import { Page } from "../../components/Page";
import { useAppAction } from "../../components/useAppAction";
import { useAppQuery } from "../../revit/hooks";
import { navigate } from "../../router";

const ID_PATTERN = /^[A-Za-z0-9._-]+$/;

/** The «New button» form: a folder with plugin.json and C# sources AnalyseTool compiles itself, and its first version. */
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
  const [template, setTemplate] = useState<ExtensionTemplate>("Command");
  const [panel, setPanel] = useState("");
  const [root, setRoot] = useState<string | null>(null);
  const [id, setId] = useState("");
  const [showAdvanced, setShowAdvanced] = useState(false);

  const create = useAppAction<ExtensionSummary, CreateExtensionRequest>(extensionCommands.CreateExtension, {
    failure: "Could not create the button",
    onSuccess: onCreated,
  });

  const devRoots = (roots.data?.roots ?? []).filter((item) => item.zone === "Dev");
  const chosenRoot = root ?? roots.data?.authoringRoot ?? null;

  const idError =
    id.trim() && !ID_PATTERN.test(id.trim())
      ? "Letters, digits, '.', '-' and '_' only."
      : id.trim() && list.data?.extensions.some((item) => item.id.toLowerCase() === id.trim().toLowerCase())
        ? "An extension with this id exists."
        : undefined;
  const canCreate = !!name.trim() && !idError && !create.isPending;

  function submit() {
    if (!canCreate) return;
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
        A folder with <code>plugin.json</code> and C# in <code>src\</code> that AnalyseTool compiles itself — no project, no
        Visual Studio. The button appears on the ribbon at once, and its first version is saved.
      </Text>

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

      <Field label="Template">
        <RadioGroup value={template} onChange={(_, data) => setTemplate(data.value as ExtensionTemplate)}>
          {(Object.keys(templateLabels) as ExtensionTemplate[]).map((key) => (
            <Radio
              key={key}
              value={key}
              label={
                <span>
                  <strong>{templateLabels[key].title}</strong>
                  {key === "Command" && <span className="muted"> (default)</span>}
                  <br />
                  <span className="muted small">{templateLabels[key].hint}</span>
                </span>
              }
            />
          ))}
        </RadioGroup>
      </Field>

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

      <div className="form-actions">
        <Button appearance="primary" type="submit" disabled={!canCreate}>
          {create.isPending ? "Creating…" : "Create"}
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
