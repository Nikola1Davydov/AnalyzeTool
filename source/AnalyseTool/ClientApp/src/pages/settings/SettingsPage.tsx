import {
  Badge,
  Button,
  Link,
  MessageBar,
  MessageBarBody,
  Radio,
  Spinner,
  Switch,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import { Add16Regular, Delete16Regular, FolderOpen16Regular } from "@fluentui/react-icons";
import { type AppInfo, type McpInfo, type SetMcpEnabledRequest, appCommands, browseFolder, openPath } from "../../api/app";
import { type ExtensionRootRequest, type ExtensionRoots, extensionCommands } from "../../api/extensions";
import { useConfirm } from "../../components/confirm";
import { CopyField } from "../../components/CopyField";
import { errorText, useNotify } from "../../components/notify";
import { Page } from "../../components/Page";
import { useAppAction } from "../../components/useAppAction";
import { useAppQuery } from "../../revit/hooks";

/** The add-in's own preferences: MCP for AI clients, and the folders extensions load from. */
export function SettingsPage() {
  return (
    <Page title="Settings" narrow>
      <McpSection />
      <FoldersSection />
    </Page>
  );
}

const stateText: Record<McpInfo["state"], { label: string; color: "success" | "subtle" | "danger" }> = {
  listening: { label: "listening", color: "success" },
  off: { label: "off", color: "subtle" },
  portBusy: { label: "port busy", color: "danger" },
};

function McpSection() {
  const info = useAppQuery<AppInfo>(appCommands.GetAppInfo);
  const notify = useNotify();
  const setEnabled = useAppAction<McpInfo, SetMcpEnabledRequest>(appCommands.SetMcpEnabled, {
    failure: "Could not change the MCP server",
  });
  const mcp = info.data?.mcp;
  const state = mcp ? stateText[mcp.state] : null;
  // Asked on but not running: it did not start
  const didNotStart = mcp && mcp.enabled && mcp.state === "off";

  return (
    <section className="panel block">
      <div className="section-head">
        <div>
          <Text as="h2" size={500} weight="semibold">
            AI clients (MCP)
          </Text>
          <Text className="muted small block-text">
            Claude Code, Claude Desktop, Cursor or any client that speaks the Model Context Protocol calls AnalyseTool's
            commands — the built-in ones and your extensions — to read and change the open model.
          </Text>
        </div>
        {mcp && (
          <Switch
            checked={mcp.enabled}
            disabled={setEnabled.isPending}
            label={mcp.enabled ? "On" : "Off"}
            onChange={(_, data) => setEnabled.mutate({ enabled: data.checked })}
          />
        )}
      </div>

      {info.isPending && <Spinner size="small" />}
      {info.isError && <Text className="error-text">{errorText(info.error)}</Text>}

      {mcp && state && (
        <>
          <div className="key-values">
            <span className="muted">State</span>
            <span>
              <Badge appearance="tint" color={state.color}>
                {state.label}
              </Badge>
            </span>
            <span className="muted">Address</span>
            <span className="mono">{mcp.url}</span>
            <span className="muted">Connection file</span>
            <span>
              <Link onClick={() => openPath(mcp.connectionFile).catch((error) => notify.failed(error))} className="mono">
                {mcp.connectionFile}
              </Link>
            </span>
          </div>
          {didNotStart && (
            <MessageBar intent="info" className="block">
              <MessageBarBody>The server did not start — see the log, or turn it off and on.</MessageBarBody>
            </MessageBar>
          )}
          {mcp.state === "portBusy" && (
            <MessageBar intent="warning" className="block">
              <MessageBarBody>Another program uses the port. Close it, or restart Revit to pick the port again.</MessageBarBody>
            </MessageBar>
          )}
          {mcp.claudeCode && (
            <div className="block">
              <Text weight="semibold">Connect Claude Code</Text>
              <Text className="muted small block-text">Run this once in a terminal:</Text>
              <CopyField text={mcp.claudeCode} />
              <Text className="muted small block-text">
                Other clients read the address and the token from the connection file. Keep the token local, like any other
                credential of this machine.
              </Text>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function FoldersSection() {
  const roots = useAppQuery<ExtensionRoots>(extensionCommands.GetExtensionRoots);
  const notify = useNotify();
  const confirm = useConfirm();
  const add = useAppAction<ExtensionRoots, ExtensionRootRequest>(extensionCommands.AddExtensionRoot, {
    success: (_, request) => `Added ${request.path} — its extensions are loaded`,
    failure: "Could not add the folder",
  });
  const remove = useAppAction<ExtensionRoots, ExtensionRootRequest>(extensionCommands.RemoveExtensionRoot, {
    success: "Folder removed — its files stay where they are",
    failure: "Could not remove the folder",
  });
  const setAuthoring = useAppAction<ExtensionRoots, ExtensionRootRequest>(extensionCommands.SetAuthoringRoot, {
    success: "New buttons are created there now",
    failure: "Could not choose the folder",
  });

  async function addFolder() {
    try {
      const path = await browseFolder({ title: "A folder whose subfolders are extensions" });
      if (path) add.mutate({ path });
    } catch (error) {
      notify.failed(error);
    }
  }

  async function removeFolder(path: string) {
    const ok = await confirm({
      title: "Stop loading this folder?",
      message: (
        <p>
          The extensions in <code className="path">{path}</code> leave the ribbon. Nothing is deleted — add the folder again to
          get them back.
        </p>
      ),
      confirmLabel: "Remove folder",
    });
    if (ok) remove.mutate({ path });
  }

  return (
    <section className="panel block">
      <div className="section-head">
        <div>
          <Text as="h2" size={500} weight="semibold">
            Folders of extensions
          </Text>
          <Text className="muted small block-text">
            Every subfolder of these is an extension. Add a network share or a git checkout to share buttons with your team.
          </Text>
        </div>
        <Button icon={<Add16Regular />} onClick={() => void addFolder()} disabled={add.isPending}>
          Add folder…
        </Button>
      </div>
      {roots.isPending && <Spinner size="small" />}
      {roots.isError && <Text className="error-text">{errorText(roots.error)}</Text>}
      {roots.data && (
        <div className="list-card">
          {roots.data.roots.map((root) => {
            const authoring = root.path === roots.data.authoringRoot;
            return (
              <div key={root.path} className="list-item">
                <div className="list-row">
                  <div className="list-main">
                    <Link className="mono small path" onClick={() => openPath(root.path).catch((error) => notify.failed(error))}>
                      {root.path}
                    </Link>
                    <span className="badges list-badges">
                      <Badge appearance="outline" size="small" color={root.zone === "Dev" ? "brand" : "informative"}>
                        {root.zone === "Dev" ? "yours" : "installed"}
                      </Badge>
                      {root.isDefault && (
                        <Badge appearance="tint" size="small" color="subtle">
                          built-in
                        </Badge>
                      )}
                      {root.zone !== "Dev" && <span className="muted small">packages are installed here</span>}
                    </span>
                    {root.zone === "Dev" && (
                      <Radio
                        className="list-radio"
                        checked={authoring}
                        label={authoring ? "New buttons are created here" : "Create new buttons here"}
                        disabled={setAuthoring.isPending}
                        onChange={() => setAuthoring.mutate({ path: root.path })}
                      />
                    )}
                  </div>
                  <div className="list-trailing">
                    <Tooltip content="Open in Explorer" relationship="label">
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<FolderOpen16Regular />}
                        onClick={() => openPath(root.path).catch((error) => notify.failed(error))}
                      />
                    </Tooltip>
                    {!root.isDefault && (
                      <Tooltip content="Stop loading this folder" relationship="label">
                        <Button size="small" appearance="subtle" icon={<Delete16Regular />} onClick={() => void removeFolder(root.path)} />
                      </Tooltip>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
