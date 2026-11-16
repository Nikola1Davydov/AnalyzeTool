import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { bridge } from "revit-bridge";
import { CommandsTab } from "./CommandsTab";
import { LogsTab } from "./LogsTab";
import { McpTab } from "./McpTab";
import { type CommandInfo, DEV_COMMAND, MCP_STATUS_COMMAND } from "./protocol";

type Tab = "logs" | "commands" | "mcp";

/**
 * The «Dev» page of the dock pane (Debug only): the live log of the add-in, a form that runs any registered command and
 * how to connect an AI client to the MCP server.
 * The dist ships the page in Release too, where the Debug-only commands are absent: it says so instead of polling.
 */
export function DevPage() {
  const [tab, setTab] = useState<Tab>("logs");
  const commands = useQuery({
    queryKey: ["dev", "commands"],
    queryFn: ({ signal }) => bridge.invoke<CommandInfo[]>("Host.GetCommands", null, { signal }),
  });

  const isDebug = commands.data?.some((command) => command.name === DEV_COMMAND) ?? false;
  const hasMcp = commands.data?.some((command) => command.name === MCP_STATUS_COMMAND) ?? false;

  return (
    <main className="page compact dev">
      <header>
        <h1>Dev</h1>
        {!bridge.inRevit && <span className="badge">mock — outside Revit</span>}
        <nav className="tabs">
          <button className={tab === "logs" ? "active" : ""} onClick={() => setTab("logs")}>
            Logs
          </button>
          <button className={tab === "commands" ? "active" : ""} onClick={() => setTab("commands")}>
            Commands
          </button>
          <button className={tab === "mcp" ? "active" : ""} onClick={() => setTab("mcp")}>
            MCP
          </button>
        </nav>
      </header>

      {commands.isPending && <p>Loading…</p>}
      {commands.isError && <p className="error">{commands.error.message}</p>}
      {commands.isSuccess && tab === "logs" && (isDebug ? <LogsTab /> : <p>The log is available in a Debug build only.</p>)}
      {commands.isSuccess && tab === "commands" && <CommandsTab commands={commands.data} />}
      {commands.isSuccess && tab === "mcp" && (hasMcp ? <McpTab /> : <p>The MCP details are available in a Debug build only.</p>)}
    </main>
  );
}
