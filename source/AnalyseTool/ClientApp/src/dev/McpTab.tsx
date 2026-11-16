import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import { MCP_STATUS_COMMAND, type McpStatus } from "./protocol";

/** How an AI client connects to the MCP server of this Revit: the state, ready-to-copy configuration and the tools. */
export function McpTab() {
  const status = useQuery({
    queryKey: ["dev", "mcp"],
    queryFn: ({ signal }) => bridge.invoke<McpStatus>(MCP_STATUS_COMMAND, null, { signal }),
  });

  if (status.isPending) return <p>Loading…</p>;
  if (status.isError) return <p className="error">{status.error.message}</p>;

  const mcp = status.data;
  return (
    <section className="mcp">
      <div className="toolbar">
        <StateBadge mcp={mcp} />
        <button onClick={() => void status.refetch()}>Refresh</button>
      </div>

      {mcp.state === "Off" && (
        <p>
          The server is off. Turn it on with <code>{'"Mcp": { "Enabled": true }'}</code> in <code>usersettings.json</code>{" "}
          and restart Revit; a Debug build has it on by default.
        </p>
      )}
      {mcp.state === "PortBusy" && (
        <p>
          The port of <code>{mcp.url}</code> is taken — usually by another Revit of the same version. Close it and restart
          Revit, or set another port with <code>{'"Mcp": { "Port": … }'}</code>.
        </p>
      )}

      {mcp.state === "Listening" && mcp.token && mcp.claudeCode && (
        <>
          <h2>Claude Code</h2>
          <ol className="steps">
            <li>
              Run in a terminal, in the folder of your project (add <code>--scope user</code> for every project):
              <Snippet shown={mask(mcp.claudeCode, mcp.token)} copied={mcp.claudeCode} />
            </li>
            <li>
              <code>claude mcp list</code> shows <code>{mcp.name}</code> as <code>Connected</code>.
            </li>
            <li>
              Start a new Claude Code session: <code>/mcp</code> lists the tools below. Ask, for example, «Which document
              is open in Revit?».
            </li>
          </ol>

          <h2>Cursor, VS Code, .mcp.json</h2>
          <p className="muted">
            The same server as JSON: <code>mcpServers</code> in <code>.mcp.json</code> or <code>.cursor/mcp.json</code>; VS
            Code names the key <code>servers</code>.
          </p>
          <Snippet shown={mask(jsonConfig(mcp, mcp.token), mcp.token)} copied={jsonConfig(mcp, mcp.token)} />
        </>
      )}

      <h2>Tools</h2>
      {mcp.tools.length === 0 ? (
        <p className="muted">
          No tools: mark a command with <code>[McpTool]</code> to open it to AI clients.
        </p>
      ) : (
        <ul className="tools">
          {mcp.tools.map((tool) => (
            <li key={tool.name}>
              <code>{tool.name}</code>
              {tool.readOnly && <span className="badge">read-only</span>} <span className="muted">{tool.description}</span>
            </li>
          ))}
        </ul>
      )}

      <p className="muted">
        A new Revit session keeps the token. The details are also in <code>{mcp.connectionFile}</code>.
      </p>
    </section>
  );
}

function StateBadge({ mcp }: { mcp: McpStatus }) {
  switch (mcp.state) {
    case "Listening":
      return (
        <span>
          <span className="badge ok">listening</span> <code>{mcp.url}</code>
        </span>
      );
    case "PortBusy":
      return <span className="badge error">port busy</span>;
    default:
      return <span className="badge">off</span>;
  }
}

/** Shows the text with the token masked, copies it whole: the pane may be on a shared screen. */
function Snippet({ shown, copied }: { shown: string; copied: string }) {
  const [done, setDone] = useState(false);

  const copy = async () => {
    await copyText(copied);
    setDone(true);
    setTimeout(() => setDone(false), 1500);
  };

  return (
    <div className="snippet">
      <pre>{shown}</pre>
      <button onClick={() => void copy()}>{done ? "Copied" : "Copy"}</button>
    </div>
  );
}

function mask(text: string, token: string) {
  return text.replaceAll(token, `${"•".repeat(8)}${token.slice(-4)}`);
}

function jsonConfig(mcp: McpStatus, token: string) {
  const config = {
    mcpServers: {
      [mcp.name]: { type: "http", url: mcp.url, headers: { Authorization: `Bearer ${token}` } },
    },
  };
  return JSON.stringify(config, null, 2);
}

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // Without clipboard permission: the old way through a selected text area
    const area = document.createElement("textarea");
    area.value = text;
    document.body.appendChild(area);
    area.select();
    document.execCommand("copy");
    area.remove();
  }
}
