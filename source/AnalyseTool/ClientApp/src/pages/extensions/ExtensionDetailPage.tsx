import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Switch,
  Tab,
  TabList,
  Text,
} from "@fluentui/react-components";
import {
  ArrowClockwise16Regular,
  Delete16Regular,
  FolderOpen16Regular,
  Wrench16Regular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { openPath } from "../../api/app";
import {
  type BuildExtensionResult,
  type CompileDiagnostic,
  type ExtensionList,
  type ExtensionSummary,
  type PinExtensionVersionRequest,
  extensionCommands,
  sameVersion,
} from "../../api/extensions";
import { relativeTime } from "../../components/format";
import { errorText, useNotify } from "../../components/notify";
import { Empty, Page } from "../../components/Page";
import { useAppAction } from "../../components/useAppAction";
import { useAppQuery } from "../../revit/hooks";
import { navigate } from "../../router";
import { useExtensionActions } from "./actions";
import { FilesTab } from "./FilesTab";
import { DiagnosticsList, KindBadge, StatusBadges, useExtensionVersions, VersionBadge } from "./parts";
import { VersionsTab } from "./VersionsTab";

type TabName = "versions" | "files";

/** One extension: its versions (a git repository per folder, behind «save» and «restore»), its files, its build. */
export function ExtensionDetailPage({ id, query }: { id: string; query: URLSearchParams }) {
  const list = useAppQuery<ExtensionList>(extensionCommands.GetExtensions);
  const extension = list.data?.extensions.find((item) => item.id.toLowerCase() === id.toLowerCase());

  if (list.isPending) return <Page title={id} back={{ label: "Extensions", path: "/extensions" }}><Spinner /></Page>;
  if (!extension) {
    return (
      <Page title={id} back={{ label: "Extensions", path: "/extensions" }}>
        {list.isError ? (
          <MessageBar intent="error">
            <MessageBarBody>{errorText(list.error)}</MessageBarBody>
          </MessageBar>
        ) : (
          <Empty>There is no extension «{id}» — it may have been removed or renamed.</Empty>
        )}
      </Page>
    );
  }
  return <Detail extension={extension} query={query} />;
}

function Detail({ extension, query }: { extension: ExtensionSummary; query: URLSearchParams }) {
  const notify = useNotify();
  const actions = useExtensionActions(() => navigate("/extensions"));
  const versions = useExtensionVersions(extension.id);
  const [tab, setTab] = useState<TabName>(query.get("tab") === "files" ? "files" : "versions");
  const [fileTarget, setFileTarget] = useState<{ file: string; line?: number } | null>(
    query.get("file") ? { file: query.get("file")!, line: Number(query.get("line")) || undefined } : null,
  );
  const [build, setBuild] = useState<BuildExtensionResult | null>(null);

  // A link to a line (`?tab=files&file=…&line=…`) followed while the page is open
  const queryText = query.toString();
  useEffect(() => {
    const params = new URLSearchParams(queryText);
    if (params.get("tab") === "files") setTab("files");
    if (params.get("file")) setFileTarget({ file: params.get("file")!, line: Number(params.get("line")) || undefined });
  }, [queryText]);

  const buildAction = useAppAction<BuildExtensionResult, { id: string }>(extensionCommands.BuildExtension, {
    failure: "Could not build",
    onSuccess: (result) => {
      setBuild(result);
      if (result.success) notify.success("Built and loaded", result.diagnostics.length ? `${result.diagnostics.length} warnings` : undefined);
    },
  });

  const unpin = useAppAction<ExtensionSummary | null, PinExtensionVersionRequest>(extensionCommands.PinExtensionVersion, {
    success: "The button runs the current files again",
  });

  const showLine = (diagnostic: Pick<CompileDiagnostic, "file" | "line">) => {
    setFileTarget({ file: diagnostic.file, line: diagnostic.line });
    setTab("files");
  };

  const pinned = extension.loadedVersion
    ? (versions.data?.history.find((version) => sameVersion(version.id, extension.loadedVersion)) ?? versions.data?.info.pinned)
    : null;
  const isSource = extension.kind === "Source";
  const versionInfo = versions.data?.info;

  return (
    <Page
      title={extension.name}
      back={{ label: "Extensions", path: "/extensions" }}
      subtitle={
        <div className="detail-subtitle">
          <span className="badges">
            <KindBadge extension={extension} />
            <StatusBadges extension={extension} />
            <VersionBadge extension={extension} />
          </span>
          <span className="muted small">
            <span className="mono">{extension.id}</span> · v{extension.version}
            {extension.publisher && <> · {extension.publisher}</>}
            {extension.buttons.length > 0 && <> · buttons: {extension.buttons.join(", ")}</>}
            {extension.commands.length > 0 && <> · commands: {extension.commands.join(", ")}</>}
          </span>
          {extension.description && <Text>{extension.description}</Text>}
          <code className="path small">{extension.directory}</code>
        </div>
      }
      actions={
        <>
          <Switch
            label={extension.enabled ? "On" : "Off"}
            checked={extension.enabled}
            disabled={actions.setEnabled.isPending}
            onChange={(_, data) => actions.setEnabled.mutate({ id: extension.id, enabled: data.checked })}
          />
          {isSource && (
            <Button
              appearance="primary"
              icon={buildAction.isPending ? <Spinner size="tiny" /> : <Wrench16Regular />}
              disabled={buildAction.isPending}
              onClick={() => buildAction.mutate({ id: extension.id })}
            >
              Build
            </Button>
          )}
          <Button icon={<ArrowClockwise16Regular />} onClick={() => actions.reload.mutate({ id: extension.id })}>
            Reload
          </Button>
          <Button
            icon={<FolderOpen16Regular />}
            onClick={() => openPath(extension.directory).catch((error) => notify.failed(error))}
          >
            Open folder
          </Button>
          <Button appearance="subtle" icon={<Delete16Regular />} onClick={() => void actions.remove(extension)}>
            Remove
          </Button>
        </>
      }
    >
      {extension.loadedVersion && (
        <MessageBar intent="warning" layout="multiline" className="block">
          <MessageBarBody>
            <MessageBarTitle>Pinned to a saved version</MessageBarTitle>
            The button runs version <span className="mono">{extension.loadedVersion.slice(0, 7)}</span>
            {pinned && (
              <>
                {" "}
                «{pinned.message}» ({relativeTime(pinned.time)})
              </>
            )}
            , whatever the files say now. Edits to the files take effect when you go back to the current files.
          </MessageBarBody>
          <MessageBarActions>
            <Button disabled={unpin.isPending} onClick={() => unpin.mutate({ id: extension.id, version: null })}>
              Back to current files
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {extension.usesOlderBuild && (
        <MessageBar intent="warning" className="block">
          <MessageBarBody>
            <MessageBarTitle>The current sources do not compile</MessageBarTitle>
            The button runs the last build that did. Fix the errors below, then Build.
          </MessageBarBody>
        </MessageBar>
      )}

      {extension.errors.length > 0 && (
        <MessageBar intent="error" layout="multiline" className="block">
          <MessageBarBody>
            <MessageBarTitle>Not loaded</MessageBarTitle>
            {extension.errors.map((error, index) => (
              <div key={index}>{error}</div>
            ))}
          </MessageBarBody>
        </MessageBar>
      )}

      {build && (
        <MessageBar intent={build.success ? "success" : "error"} layout="multiline" className="block">
          <MessageBarBody>
            <MessageBarTitle>{build.success ? "Build succeeded" : "Build failed"}</MessageBarTitle>
            {build.diagnostics.length ? (
              <DiagnosticsList diagnostics={build.diagnostics} onOpen={showLine} />
            ) : (
              "No errors, no warnings — the button runs the new code."
            )}
          </MessageBarBody>
          <MessageBarActions>
            <Button appearance="transparent" onClick={() => setBuild(null)}>
              Close
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {!build && extension.diagnostics.length > 0 && (
        <section className="panel block">
          <Text weight="semibold">Compiler messages</Text>
          <DiagnosticsList diagnostics={extension.diagnostics} onOpen={showLine} />
        </section>
      )}

      {versionInfo && !versionInfo.available && (
        <MessageBar intent="info" className="block">
          <MessageBarBody>
            <MessageBarTitle>Versions are not available</MessageBarTitle>
            {versionInfo.unavailableReason ?? "Git cannot be used here."}
          </MessageBarBody>
        </MessageBar>
      )}

      <TabList selectedValue={tab} onTabSelect={(_, data) => setTab(data.value as TabName)} className="block">
        <Tab value="versions">
          Versions
          {versionInfo?.changes.length ? <span className="tab-count">{versionInfo.changes.length}</span> : null}
        </Tab>
        <Tab value="files">Files</Tab>
      </TabList>

      {tab === "versions" && (
        <VersionsTab
          extension={extension}
          versions={versions.data}
          loading={versions.isPending}
          error={versions.isError ? errorText(versions.error) : null}
        />
      )}
      {tab === "files" && <FilesTab extension={extension} target={fileTarget} onTarget={setFileTarget} />}
    </Page>
  );
}
