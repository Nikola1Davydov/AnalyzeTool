import {
  Badge,
  Button,
  Field,
  Input,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
  Spinner,
  Tab,
  TabList,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import {
  ArrowUndo16Regular,
  ChevronDown16Regular,
  ChevronRight16Regular,
  History16Regular,
  MoreHorizontal16Regular,
  Pin16Regular,
  PinOff16Regular,
  Save16Regular,
} from "@fluentui/react-icons";
import { type ReactNode, useState } from "react";
import {
  type ExtensionDiffRequest,
  type ExtensionFileDiff,
  type ExtensionFileRequest,
  type ExtensionSummary,
  type ExtensionVersion,
  type ExtensionVersions,
  type PinExtensionVersionRequest,
  type RestoreExtensionVersionRequest,
  type SaveExtensionVersionRequest,
  type SaveExtensionVersionResult,
  extensionCommands,
  sameVersion,
} from "../../api/extensions";
import { useConfirm } from "../../components/confirm";
import { type DiffMode, DiffView, diffStats } from "../../components/DiffView";
import { fullTime, relativeTime } from "../../components/format";
import { errorText } from "../../components/notify";
import { Empty } from "../../components/Page";
import { useAppAction } from "../../components/useAppAction";
import { useAppQuery } from "../../revit/hooks";
import { ChangeStateBadge } from "./parts";

type Selection = { kind: "unsaved" } | { kind: "version"; version: ExtensionVersion };

type VersionsTabProps = {
  extension: ExtensionSummary;
  versions: ExtensionVersions | undefined;
  loading: boolean;
  error: string | null;
};

/** The history of an extension: save a version, compare, restore, run an older one. */
export function VersionsTab({ extension, versions, loading, error }: VersionsTabProps) {
  const confirm = useConfirm();
  const [message, setMessage] = useState("");
  const [picked, setPicked] = useState<Selection | null>(null);

  const changes = versions?.info.changes ?? [];
  const history = versions?.history ?? [];
  const latest = versions?.info.latest ?? history[0] ?? null;
  const isSource = extension.kind === "Source";

  // What the diff shows: the user's pick, else the unsaved changes, else the latest version
  const selection: Selection | null =
    picked && (picked.kind === "unsaved" || history.some((version) => version.id === picked.version.id))
      ? picked
      : changes.length
        ? { kind: "unsaved" }
        : latest
          ? { kind: "version", version: latest }
          : null;

  const save = useAppAction<SaveExtensionVersionResult, SaveExtensionVersionRequest>(extensionCommands.SaveExtensionVersion, {
    success: (result) => (result.version ? `Saved version ${result.version.shortId}` : "Nothing changed since the latest version"),
    failure: "Could not save the version",
    onSuccess: (result) => {
      setMessage("");
      if (result.version) setPicked({ kind: "version", version: result.version });
    },
  });

  const restore = useAppAction<SaveExtensionVersionResult, RestoreExtensionVersionRequest>(
    extensionCommands.RestoreExtensionVersion,
    {
      success: (_, request) =>
        request.file ? `Restored ${request.file}` : `Restored version ${request.version.slice(0, 7)} — saved as a new version`,
      failure: "Could not restore",
      onSuccess: () => setPicked(null),
    },
  );

  const pin = useAppAction<ExtensionSummary | null, PinExtensionVersionRequest>(extensionCommands.PinExtensionVersion, {
    success: (_, request) =>
      request.version ? `The button now runs version ${request.version.slice(0, 7)}` : "The button runs the current files again",
    failure: "Could not change the version that runs",
  });

  const deleteFile = useAppAction<ExtensionSummary | null, ExtensionFileRequest>(extensionCommands.DeleteExtensionFile, {
    success: (_, request) => `Deleted ${request.path}`,
    failure: "Could not delete the file",
  });

  async function restoreVersion(version: ExtensionVersion) {
    const ok = await confirm({
      title: `Restore version ${version.shortId}?`,
      message: (
        <>
          <p>
            The files of «{extension.name}» become as they were in «{version.message}» ({relativeTime(version.time)}).
          </p>
          <p>
            Nothing is lost: {changes.length ? "your unsaved changes are saved as a version first, and " : ""}the restored state
            is saved as a new version. Every earlier version stays in the history.
          </p>
        </>
      ),
      confirmLabel: "Restore",
    });
    if (ok) restore.mutate({ id: extension.id, version: version.id });
  }

  async function restoreFile(file: string, version: ExtensionVersion, discard: boolean) {
    const ok = await confirm({
      title: discard ? `Discard the changes to ${file}?` : `Restore ${file}?`,
      message: discard ? (
        <p>
          The file goes back to the latest version ({version.shortId}). Your changes are saved as a version first, so you can get
          them back from the history.
        </p>
      ) : (
        <p>
          The file becomes as it was in version {version.shortId} «{version.message}». Unsaved changes are saved as a version
          first — nothing is lost.
        </p>
      ),
      confirmLabel: discard ? "Discard changes" : "Restore file",
    });
    if (ok) restore.mutate({ id: extension.id, version: version.id, file });
  }

  async function removeNewFile(file: string) {
    const ok = await confirm({
      title: `Delete ${file}?`,
      message: <p>The file is not in any saved version yet, so it cannot be brought back from the history.</p>,
      confirmLabel: "Delete",
      danger: true,
    });
    if (ok) deleteFile.mutate({ id: extension.id, path: file });
  }

  if (loading) return <Spinner label="Reading the history…" />;
  if (error) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }
  if (!versions?.info.available) return <Empty>Versions are not available for this extension.</Empty>;

  return (
    <div className="versions-layout">
      <div className="versions-side">
        {/* Unsaved changes and saving */}
        <section
          className={`panel selectable ${selection?.kind === "unsaved" ? "selected" : ""}`}
          onClick={() => changes.length && setPicked({ kind: "unsaved" })}
        >
          <Text weight="semibold">Unsaved changes</Text>
          {changes.length ? (
            <ul className="file-changes">
              {changes.map((change) => (
                <li key={change.file}>
                  <ChangeStateBadge state={change.state} />
                  <span className="mono small">{change.file}</span>
                </li>
              ))}
            </ul>
          ) : (
            <Text className="muted small block-text">
              {versions.info.hasHistory ? "None — the files match the latest version." : "No files yet."}
            </Text>
          )}
          <form
            className="save-form"
            onClick={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              if (changes.length) save.mutate({ id: extension.id, message: message.trim() || null });
            }}
          >
            <Field label="What changed" size="small">
              <Input
                size="small"
                value={message}
                disabled={!changes.length}
                onChange={(_, data) => setMessage(data.value)}
                placeholder="Count only the walls of the active view"
              />
            </Field>
            <Button
              type="submit"
              appearance="primary"
              size="small"
              icon={<Save16Regular />}
              disabled={!changes.length || save.isPending}
            >
              Save version
            </Button>
          </form>
        </section>

        {/* History */}
        <section className="panel">
          <Text weight="semibold">
            <History16Regular className="inline-icon" /> History
          </Text>
          {!history.length && <Text className="muted small block-text">No saved versions yet.</Text>}
          <ul className="history">
            {history.map((version, index) => {
              const running = sameVersion(version.id, extension.loadedVersion);
              const selected = selection?.kind === "version" && selection.version.id === version.id;
              return (
                <li
                  key={version.id}
                  className={`${selected ? "selected" : ""} ${running ? "running" : ""}`}
                  onClick={() => setPicked({ kind: "version", version })}
                >
                  <div className="history-main">
                    <span className="history-message">{version.message || <em className="muted">no message</em>}</span>
                    <span className="muted small">
                      {version.author} ·{" "}
                      <Tooltip content={fullTime(version.time)} relationship="description">
                        <span>{relativeTime(version.time)}</span>
                      </Tooltip>{" "}
                      · <span className="mono">{version.shortId}</span>
                    </span>
                  </div>
                  <span className="badges">
                    {index === 0 && (
                      <Badge appearance="tint" color="brand" size="small">
                        latest
                      </Badge>
                    )}
                    {running && (
                      <Badge appearance="filled" color="warning" size="small">
                        running
                      </Badge>
                    )}
                  </span>
                  <Menu>
                    <MenuTrigger disableButtonEnhancement>
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<MoreHorizontal16Regular />}
                        aria-label="Version actions"
                        onClick={(event) => event.stopPropagation()}
                      />
                    </MenuTrigger>
                    <MenuPopover>
                      <MenuList>
                        <MenuItem onClick={() => setPicked({ kind: "version", version })}>Show changes</MenuItem>
                        <MenuItem icon={<ArrowUndo16Regular />} onClick={() => void restoreVersion(version)}>
                          Restore this version…
                        </MenuItem>
                        {isSource &&
                          (running ? (
                            <MenuItem
                              icon={<PinOff16Regular />}
                              onClick={() => pin.mutate({ id: extension.id, version: null })}
                            >
                              Back to current files
                            </MenuItem>
                          ) : (
                            <MenuItem
                              icon={<Pin16Regular />}
                              onClick={() => pin.mutate({ id: extension.id, version: version.id })}
                            >
                              Run this version
                            </MenuItem>
                          ))}
                      </MenuList>
                    </MenuPopover>
                  </Menu>
                </li>
              );
            })}
          </ul>
          {isSource && (
            <Text className="muted small block-text">
              «Run this version» makes the button run a saved version without touching the files — to compare, or to keep
              working while the current code is broken.
            </Text>
          )}
        </section>
      </div>

      <div className="versions-main">
        {selection ? (
          <ChangesPanel
            extension={extension}
            selection={selection}
            latest={latest}
            onRestoreVersion={restoreVersion}
            onRestoreFile={restoreFile}
            onDeleteNewFile={removeNewFile}
            onPin={(version) => pin.mutate({ id: extension.id, version: version?.id ?? null })}
          />
        ) : (
          <Empty>Nothing to compare yet.</Empty>
        )}
      </div>
    </div>
  );
}

type ChangesPanelProps = {
  extension: ExtensionSummary;
  selection: Selection;
  latest: ExtensionVersion | null;
  onRestoreVersion: (version: ExtensionVersion) => void;
  onRestoreFile: (file: string, version: ExtensionVersion, discard: boolean) => void;
  onDeleteNewFile: (file: string) => void;
  onPin: (version: ExtensionVersion | null) => void;
};

/** The diff of the unsaved changes, or of what one version changed, file by file. */
function ChangesPanel({ extension, selection, latest, onRestoreVersion, onRestoreFile, onDeleteNewFile, onPin }: ChangesPanelProps) {
  const [mode, setMode] = useState<DiffMode>(() => (window.innerWidth > 1100 ? "split" : "unified"));
  const request: ExtensionDiffRequest =
    selection.kind === "unsaved" ? { id: extension.id } : { id: extension.id, to: selection.version.id };
  const diff = useAppQuery<ExtensionFileDiff[]>(extensionCommands.GetExtensionDiff, request);
  const version = selection.kind === "version" ? selection.version : null;
  const running = version && sameVersion(version.id, extension.loadedVersion);

  return (
    <section className="panel changes-panel">
      <header className="changes-header">
        <div>
          <Text weight="semibold">
            {version ? (
              <>
                Changes in <span className="mono">{version.shortId}</span> «{version.message}»
              </>
            ) : (
              "Unsaved changes"
            )}
          </Text>
          {version && (
            <div className="muted small">
              {version.author}, {fullTime(version.time)} — compared with the version before it
            </div>
          )}
          {!version && <div className="muted small">The files now, compared with the latest saved version</div>}
        </div>
        <div className="changes-actions">
          {version && (
            <>
              <Button size="small" icon={<ArrowUndo16Regular />} onClick={() => onRestoreVersion(version)}>
                Restore this version
              </Button>
              {extension.kind === "Source" && (
                <Button
                  size="small"
                  icon={running ? <PinOff16Regular /> : <Pin16Regular />}
                  onClick={() => onPin(running ? null : version)}
                >
                  {running ? "Back to current files" : "Run this version"}
                </Button>
              )}
            </>
          )}
          <TabList size="small" selectedValue={mode} onTabSelect={(_, data) => setMode(data.value as DiffMode)}>
            <Tab value="unified">Unified</Tab>
            <Tab value="split">Side by side</Tab>
          </TabList>
        </div>
      </header>

      {diff.isPending && <Spinner size="small" label="Comparing…" />}
      {diff.isError && <Text className="error-text">{errorText(diff.error)}</Text>}
      {diff.isSuccess && !diff.data.length && <Empty>No changes.</Empty>}
      {diff.data?.map((file) => (
        <FileDiff
          key={`${request.to ?? "work"}:${file.file}`}
          file={file}
          mode={mode}
          action={
            version ? (
              <Tooltip
                content={file.after === null && !file.binary ? "This version deleted the file" : "Bring this file back as it was in this version"}
                relationship="description"
              >
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<ArrowUndo16Regular />}
                  disabled={file.state === "deleted"}
                  onClick={() => onRestoreFile(file.file, version, false)}
                >
                  Restore file
                </Button>
              </Tooltip>
            ) : file.state === "added" ? (
              <Button size="small" appearance="subtle" onClick={() => onDeleteNewFile(file.file)}>
                Delete file
              </Button>
            ) : latest ? (
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowUndo16Regular />}
                onClick={() => onRestoreFile(file.file, latest, true)}
              >
                Discard changes
              </Button>
            ) : null
          }
        />
      ))}
    </section>
  );
}

function FileDiff({ file, mode, action }: { file: ExtensionFileDiff; mode: DiffMode; action: ReactNode }) {
  const [open, setOpen] = useState(true);
  const stats = file.binary ? null : diffStats(file.before, file.after);
  return (
    <div className="file-diff">
      <div className="file-diff-header">
        <button type="button" className="link-button" onClick={() => setOpen(!open)} aria-label={open ? "Fold" : "Unfold"}>
          {open ? <ChevronDown16Regular /> : <ChevronRight16Regular />}
        </button>
        <ChangeStateBadge state={file.state} />
        <span className="mono file-diff-name">{file.file}</span>
        {stats && (
          <span className="small">
            <span className="added-text">+{stats.added}</span> <span className="removed-text">−{stats.removed}</span>
          </span>
        )}
        <span className="spacer" />
        {action}
      </div>
      {open && <DiffView before={file.before} after={file.after} binary={file.binary} mode={mode} />}
    </div>
  );
}

