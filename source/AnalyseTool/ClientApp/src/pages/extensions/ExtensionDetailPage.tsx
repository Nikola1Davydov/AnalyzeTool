import {
  Button,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Spinner,
  Tab,
  TabList,
  Text,
} from "@fluentui/react-components";
import {
  ArrowClockwise16Regular,
  Delete16Regular,
  FolderOpen16Regular,
  Info16Regular,
  MoreHorizontal16Regular,
  ToggleRightRegular,
  Wrench16Regular,
} from "@fluentui/react-icons";
import { useEffect, useState } from "react";
import { openPath } from "../../api/app";
import {
  type BuildExtensionResult,
  type CompileDiagnostic,
  type ExtensionList,
  type ExtensionSummary,
  extensionCommands,
} from "../../api/extensions";
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

  const showLine = (diagnostic: Pick<CompileDiagnostic, "file" | "line">) => {
    setFileTarget({ file: diagnostic.file, line: diagnostic.line });
    setTab("files");
  };

  const isSource = extension.kind === "Source";
  const versionInfo = versions.data?.info;

  return (
    <Page
      title={extension.name}
      back={{ label: "Extensions", path: "/extensions" }}
      titleExtra={
        <span className="badges">
          <KindBadge extension={extension} />
          <StatusBadges extension={extension} />
          <VersionBadge extension={extension} />
          {/* The reference card: everything that is good to have but not worked with every day */}
          <Popover withArrow positioning="below-start">
            <PopoverTrigger disableButtonEnhancement>
              <Button appearance="subtle" size="small" icon={<Info16Regular />} aria-label="About this extension" />
            </PopoverTrigger>
            <PopoverSurface className="detail-info">
              <span className="muted small">
                <span className="mono">{extension.id}</span> · v{extension.version}
                {extension.publisher && <> · {extension.publisher}</>}
              </span>
              {extension.buttons.length > 0 && <span className="small">Buttons: {extension.buttons.join(", ")}</span>}
              {extension.commands.length > 0 && <span className="small">Commands: {extension.commands.join(", ")}</span>}
              {extension.description && <Text>{extension.description}</Text>}
              <code className="path small">{extension.directory}</code>
            </PopoverSurface>
          </Popover>
        </span>
      }
      actions={
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Button icon={<MoreHorizontal16Regular />} aria-label="Extension actions" />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {isSource && (
                <MenuItem icon={<Wrench16Regular />} disabled={buildAction.isPending} onClick={() => buildAction.mutate({ id: extension.id })}>
                  Build
                </MenuItem>
              )}
              <MenuItem icon={<ArrowClockwise16Regular />} onClick={() => actions.reload.mutate({ id: extension.id })}>
                Reload
              </MenuItem>
              <MenuItem icon={<FolderOpen16Regular />} onClick={() => void openPath(extension.directory).catch((error) => notify.failed(error))}>
                Open folder
              </MenuItem>
              <MenuItem
                icon={<ToggleRightRegular />}
                disabled={actions.setEnabled.isPending}
                onClick={() => actions.setEnabled.mutate({ id: extension.id, enabled: !extension.enabled })}
              >
                {extension.enabled ? "Turn off" : "Turn on"}
              </MenuItem>
              <MenuItem icon={<Delete16Regular />} onClick={() => void actions.remove(extension)}>
                Remove…
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      }
    >
      {extension.usesOlderBuild && (
        <MessageBar intent="warning" className="block">
          <MessageBarBody>
            <MessageBarTitle>The current sources do not compile</MessageBarTitle>
            The button runs the last build that did. Fix the errors below, then Build.
          </MessageBarBody>
          <MessageBarActions>
            <Button
              icon={buildAction.isPending ? <Spinner size="tiny" /> : <Wrench16Regular />}
              disabled={buildAction.isPending}
              onClick={() => buildAction.mutate({ id: extension.id })}
            >
              Build
            </Button>
          </MessageBarActions>
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
        <Tab value="files">Code</Tab>
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
