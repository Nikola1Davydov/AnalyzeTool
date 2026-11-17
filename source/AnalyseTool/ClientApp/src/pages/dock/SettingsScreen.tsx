import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import { type AppInfo, type McpInfo, appCommands, browseFolder, openPath } from "../../api/app";
import { type ExtensionRoots, extensionCommands } from "../../api/extensions";
import { copy } from "./clipboard";
import { messageOf, useActions } from "./Actions";

const states: Record<McpInfo["state"], string> = { off: "Off", listening: "Listening", portBusy: "Port in use" };

/** AI clients, in the pane: the MCP server inside Revit and how each client connects to it. */
export function McpScreen() {
  return (
    <div className="profile settings">
      <McpSection />
    </div>
  );
}

/** The folders your buttons live in: yours, a network share, a git checkout. */
export function FoldersScreen() {
  return (
    <div className="profile settings">
      <FoldersSection />
    </div>
  );
}

type Client = "claude-code" | "codex" | "cursor" | "vscode" | "claude-desktop" | "gemini";

const clients: { value: Client; label: string; where: string }[] = [
  { value: "claude-code", label: "Claude Code", where: "Run once in a terminal — the AnalyseTool plugin: the tools of every open Revit, no token to copy, and the guide for buttons:" },
  { value: "codex", label: "Codex", where: "Run once in a terminal — the AnalyseTool plugin: the tools of every open Revit, no token to copy, and the guide for buttons:" },
  { value: "cursor", label: "Cursor", where: "Add to ~/.cursor/mcp.json (or .cursor/mcp.json of a project):" },
  { value: "vscode", label: "VS Code", where: "Add to .vscode/mcp.json of a project (or «MCP: Open User Configuration»):" },
  { value: "claude-desktop", label: "Claude Desktop", where: "Add to claude_desktop_config.json (Settings › Developer › Edit Config); needs Node.js for mcp-remote:" },
  { value: "gemini", label: "Gemini CLI", where: "Add to ~/.gemini/settings.json:" },
];

/** What a client needs: a command or a piece of its configuration, with the address and the token of this Revit. */
function snippet(client: Client, mcp: McpInfo) {
  const name = mcp.name ?? "analysetool";
  const url = mcp.url;
  const auth = `Bearer ${mcp.token ?? "<token>"}`;
  const json = (value: unknown) => JSON.stringify(value, null, 2);
  switch (client) {
    case "claude-code":
      return mcp.claudePlugin
        ? `claude plugin marketplace add "${mcp.claudePlugin}"\nclaude plugin install analysetool@analysetool`
        : (mcp.claudeCode ?? `claude mcp add --transport http ${name} ${url} --header "Authorization: ${auth}"`);
    case "codex":
      if (mcp.claudePlugin) return `codex plugin marketplace add "${mcp.claudePlugin}"\ncodex plugin add analysetool@analysetool`;
      return `[mcp_servers.${name}]\nurl = "${url}"\nhttp_headers = { "Authorization" = "${auth}" }`;
    case "cursor":
      return json({ mcpServers: { [name]: { url, headers: { Authorization: auth } } } });
    case "vscode":
      return json({ servers: { [name]: { type: "http", url, headers: { Authorization: auth } } } });
    case "claude-desktop":
      return json({ mcpServers: { [name]: { command: "npx", args: ["-y", "mcp-remote", url, "--header", `Authorization:${auth}`] } } });
    case "gemini":
      return json({ mcpServers: { [name]: { httpUrl: url, headers: { Authorization: auth } } } });
  }
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
  const setScripting = useMutation({
    mutationFn: (enabled: boolean) => bridge.invoke<McpInfo>(appCommands.SetScriptingEnabled, { enabled }),
    onSuccess: () => void client.invalidateQueries({ queryKey: ["appInfo"] }),
    onError: (error) => actions.toast(messageOf(error), "error"),
  });
  const mcp = info.data?.mcp;

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
            Claude Code, Codex, Cursor, VS Code, Gemini or any MCP client calls the commands of AnalyseTool and of your buttons.
            {mcp.enabled && mcp.state === "off" ? " It did not start: see the log, or turn it off and on." : ""}
          </p>
          <label className="switch-row">
            <span>Let AI clients run any C#</span>
            <button
              role="switch"
              aria-checked={!!mcp.scripting}
              className={`switch ${mcp.scripting ? "on" : ""}`}
              disabled={setScripting.isPending}
              onClick={() => setScripting.mutate(!mcp.scripting)}
            >
              <span className="switch-knob" />
            </button>
          </label>
          <p className={`small scripting-warning ${mcp.scripting ? "on" : ""}`}>
            ⚠ <code>ExecuteCode</code>: a client writes C# and AnalyseTool runs it in Revit with your Windows rights — it can change
            or delete anything in the model, and read or write files on this computer. A run is one undo step, and every run is
            written to <span className="mono">%LOCALAPPDATA%\AnalyseTool\scripts</span>. Turn it on only for a client you
            trust, and try changes on a copy of the model.
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
          <ConnectClient mcp={mcp} />
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

/** Connect a client: pick it, copy what it needs. */
function ConnectClient({ mcp }: { mcp: McpInfo }) {
  const [client, setClient] = useState<Client>("claude-code");
  const [copied, setCopied] = useState(false);
  const chosen = clients.find((item) => item.value === client)!;
  const text = snippet(client, mcp);

  return (
    <>
      <h2>Connect a client</h2>
      <div className="client-list" role="radiogroup">
        {clients.map((item) => (
          <button
            key={item.value}
            role="radio"
            aria-checked={client === item.value}
            className={`chip ${client === item.value ? "on" : ""}`}
            onClick={() => {
              setClient(item.value);
              setCopied(false);
            }}
          >
            {item.label}
          </button>
        ))}
      </div>
      {!mcp.token && <p className="muted small">The server is not listening — turn it on above, then copy.</p>}
      <p className="small">{chosen.where}</p>
      <pre className="result small">{text}</pre>
      <button onClick={() => void copy(text).then(setCopied)}>{copied ? "Copied" : "Copy"}</button>
      <p className="muted small">
        One address and one token for every Revit version (also in the connection file): the first Revit you open answers and
        hands a call to the version it names — every tool takes <span className="mono">"revit": "2026"</span>, ListRevitVersions shows the
        open ones; without it a call goes to the Revit you used last. Keep the token on this computer — it lets a program drive the
        open model.
      </p>
    </>
  );
}
