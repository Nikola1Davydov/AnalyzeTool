// The Debug-only commands of the add-in that the Dev page uses, and the built-in Host.GetCommands.

/** The Debug-only log command; its absence in Host.GetCommands means a Release build. */
export const DEV_COMMAND = "GetLogEntries";

/** An item of Host.GetCommands. */
export type CommandInfo = { name: string; description: string | null; requestType: string | null; responseType: string };

export type LogLevel = "Trace" | "Debug" | "Information" | "Warning" | "Error" | "Critical";

/** A record of the add-in log (LogBuffer.cs). */
export type LogEntry = {
  sequence: number;
  timestamp: string;
  level: LogLevel;
  category: string;
  message: string;
  exception: string | null;
};

/** The answer of GetLogEntries. */
export type LogPage = { entries: LogEntry[]; lastSequence: number };

export const LOG_LEVELS: LogLevel[] = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

/** The Debug-only command with the state of the MCP server. */
export const MCP_STATUS_COMMAND = "GetMcpStatus";

/** The answer of GetMcpStatus (McpEndpointInfo in AnalyseTool.Hosting.Mcp). */
export type McpStatus = {
  state: "Off" | "Listening" | "PortBusy";
  url: string;
  name: string;
  token: string | null;
  claudeCode: string | null;
  connectionFile: string;
  tools: { name: string; description: string | null; readOnly: boolean }[];
};
