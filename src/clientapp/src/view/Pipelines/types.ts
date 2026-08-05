// Shapes returned by the pipeline commands. They mirror the records in
// AnalyseTool.Core/Features/Pipelines and Common/Pipelines — camelCase on the wire, because
// Newtonsoft writes the [JsonProperty] names those records declare.

export type NodeState = "Queued" | "Executing" | "Completed" | "Failed" | "Skipped";
export type RunState = "Completed" | "Failed" | "Cancelled";

export interface PipelineListResult {
  pipelines: string[];
}

export interface ValidationResult {
  ok: boolean;
  errors: string[];
  warnings: string[];
  /** Nodes that CHANGE the model, as "id (Command)". Empty means nothing to confirm. */
  destructiveNodes: string[];
}

/** A command as the catalogue reports it — both schemas whole, which is what the editor's forms need. */
export interface CommandInfo {
  name: string;
  source: string;
  description: string | null;
  readOnly: boolean;
  destructive: boolean;
  exposedToMcp: boolean;
  /** What part this command plays in a pipeline, decided by the host (PipelineSafety.RoleOf) rather
   *  than worked out here — the same answer the graph validator uses. Older builds omit it. */
  role?: "read" | "narrow" | "ai" | "gate" | "write" | "report" | "other";
  inputSchema: JsonSchema | null;
  outputSchema: JsonSchema | null;
}

export interface JsonSchema {
  type?: string | string[];
  description?: string;
  properties?: Record<string, JsonSchema>;
  items?: JsonSchema;
  enum?: unknown[];
  [key: string]: unknown;
}

export interface PipelineNodeDoc {
  id: string;
  command: string;
  contract?: number;
  params?: Record<string, unknown>;
  /** Payload property → "<nodeId>" or "<nodeId>.<path>". Wins over a literal of the same name. */
  bind?: Record<string, string>;
  onFailure?: "Stop" | "Continue";
  /** Canvas position. The runner ignores it; a hand-written pipeline simply has none. */
  ui?: { x: number; y: number };
}

export interface PipelineEdgeDoc {
  from: string;
  to: string;
}

export interface PipelineDoc {
  schema: number;
  id: string;
  name: string;
  author?: string | null;
  version: string;
  nodes: PipelineNodeDoc[];
  edges: PipelineEdgeDoc[];
}

export interface NodeOutcome {
  nodeId: string;
  command: string;
  state: NodeState;
  result: unknown;
  error: string | null;
}

export interface RunResult {
  pipelineId: string;
  state: RunState;
  nodes: NodeOutcome[];
  /** The node the run stopped on, or null when it ran to the end. */
  stoppedAt: string | null;
}
