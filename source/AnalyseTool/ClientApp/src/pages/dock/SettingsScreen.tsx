import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import { type AppInfo, type McpInfo, appCommands, browseFolder, openPath } from "../../api/app";
import { type ExtensionRoots, extensionCommands } from "../../api/extensions";
import { copy } from "./clipboard";
import { messageOf, useActions } from "./Actions";

const states: Record<McpInfo["state"], string> = { off: "Off", listening: "Listening", portBusy: "Port in use" };

/** Settings, in the pane: the AI connection (MCP) and the folders your buttons live in. */
export function SettingsScreen() {
  return (
    <div className="profile settings">
      <McpSection />
      <FoldersSection />
    </div>
  );
}

function McpSection() {
  const actions = useActions();
  const client = useQueryClient();
  const info = useQuery({ queryKey: ["appInfo"], queryFn: ({ signal }) => bridge.invoke<AppInfo>(appCommands.GetAppInfo, null, { signal }) });
  const setEnabled = useMutation({
    mutationFn: (enabled: boolean) => bridge.invoke<McpInfo>(appCommands.SetMcpEnabled, { enabled }),
    onSuccess: () => void client.invalidateQueries({ queryKey: ["appInfo"] }),
    onError: (error) => actions.toast(messageOf(error), "error"),
  });
  const mcp = info.data?.mcp;
  const [copied, setCopied] = useState(false);

  return (
    <section>
      <h2>AI clients (MCP)</h2>
      {info.isPending && <p className="muted small">Loading…</p>}
      {info.isError && <p className="error small">{info.error.message}</p>}
      {mcp && (
        <>
          <label className="switch-row">
            <span>Let AI clients use AnalyseTool</span>
            <button role="switch" aria-checked={mcp.enabled} className={`switch ${mcp.enabled ? "on" : ""}`} disabled={setEnabled.isPending} onClick={() => setEnabled.mutate(!mcp.enabled)}>
              <span className="switch-knob" />
            </button>
          </label>
          <p className="muted small">
            Claude Code, Claude Desktop, Cursor or any MCP client calls the commands of AnalyseTool and of your buttons.
            {mcp.enabled && mcp.state === "off" ? " It starts with the next Revit session." : ""}
            {!mcp.enabled && mcp.state !== "off" ? " It stops when Revit restarts." : ""}
          </p>
          <dl className="key-list">
            <dt>State</dt>
            <dd className={mcp.state === "portBusy" ? "error" : undefined}>{states[mcp.state]}</dd>
            <dt>Address</dt>
            <dd className="mono">{mcp.url}</dd>
            <dt>Connection file</dt>
            <dd>
              <button className="link-button mono path" onClick={() => void openPath(mcp.connectionFile).catch((error: unknown) => actions.toast(messageOf(error), "error"))}>
                {mcp.connectionFile}
              </button>
            </dd>
          </dl>
          {mcp.claudeCode && (
            <>
              <p className="small">Connect Claude Code — run once in a terminal:</p>
              <pre className="result small">{mcp.claudeCode}</pre>
              <button onClick={() => void copy(mcp.claudeCode!).then(setCopied)}>{copied ? "Copied" : "Copy"}</button>
              <p className="muted small">Other clients read the address and the token from the connection file. Keep the token on this computer.</p>
            </>
          )}
        </>
      )}
    </section>
  );
}

function FoldersSection() {
  const actions = useActions();
  const client = useQueryClient();
  const roots = useQuery({ queryKey: ["roots"], queryFn: ({ signal }) => bridge.invoke<ExtensionRoots>(extensionCommands.GetExtensionRoots, null, { signal }) });
  const change = useMutation({
    mutationFn: ({ command, path }: { command: string; path: string }) => bridge.invoke<ExtensionRoots>(command, { path }),
    onSuccess: () => void client.invalidateQueries({ predicate: (query) => query.queryKey[0] !== "paneProfile" }),
    onError: (error) => actions.toast(messageOf(error), "error"),
  });

  async function addFolder() {
    try {
      const path = await browseFolder({ title: "A folder of buttons — yours, a network share or a git checkout" });
      if (path) change.mutate({ command: extensionCommands.AddExtensionRoot, path });
    } catch (error) {
      actions.toast(messageOf(error), "error");
    }
  }

  return (
    <section>
      <h2>Folders of buttons</h2>
      <p className="muted small">Every subfolder of these is a folder of the pane. Add a network share or a git checkout to share buttons with your team.</p>
      {roots.isPending && <p className="muted small">Loading…</p>}
      {roots.isError && <p className="error small">{roots.error.message}</p>}
      <ul className="dock-list root-list">
        {roots.data?.roots.map((root) => {
          const authoring = root.path === roots.data.authoringRoot;
          return (
            <li key={root.path}>
              <span className="dock-name">
                <button className="link-button mono path" title="Open in Explorer" onClick={() => void openPath(root.path).catch((error: unknown) => actions.toast(messageOf(error), "error"))}>
                  {root.path}
                </button>
                <span className="muted small">
                  {root.zone === "Dev" ? " · yours" : " · packages"}
                  {authoring ? " · new folders go here" : ""}
                </span>
              </span>
              {root.zone === "Dev" && !authoring && (
                <button className="text-button" title="New folders are created here" onClick={() => change.mutate({ command: extensionCommands.SetAuthoringRoot, path: root.path })}>
                  Use
                </button>
              )}
              {!root.isDefault && (
                <button
                  className="icon-button"
                  title="Remove from the list (nothing is deleted)"
                  onClick={() => {
                    if (confirm(`Stop reading ${root.path}? Its folders leave the pane and the ribbon; nothing is deleted.`)) change.mutate({ command: extensionCommands.RemoveExtensionRoot, path: root.path });
                  }}
                >
                  ✕
                </button>
              )}
            </li>
          );
        })}
      </ul>
      <button disabled={change.isPending} onClick={() => void addFolder()}>
        Add a folder…
      </button>
    </section>
  );
}
