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
