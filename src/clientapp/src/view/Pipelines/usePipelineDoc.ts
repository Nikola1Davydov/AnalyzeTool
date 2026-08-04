import { ref, computed } from "vue";
import { invoke } from "@/RevitBridge";
import type { CommandInfo, PipelineDoc, PipelineNodeDoc, ValidationResult } from "./types";

// The document the editor edits, plus the command catalogue it builds nodes from. Kept out of the
// view so the canvas file stays about drawing.
//
// The rule this module exists to hold: the pipeline runs in FILE ORDER. V1 has no branching, so the
// array order is the whole schedule, and `edges` is kept as a mirror of it rather than as a second,
// independently drawn graph — a stored edge that said anything else would be a lie waiting for
// whoever opens the file. That is why connecting two nodes on the canvas REORDERS them instead of
// adding a line, and why every node carries its position in the list.

export function usePipelineDoc() {
  const doc = ref<PipelineDoc>(blank());
  const commands = ref<CommandInfo[]>([]);
  const selectedId = ref<string | null>(null);
  const validation = ref<ValidationResult | null>(null);
  const busy = ref(false);
  const error = ref<string | null>(null);

  const selected = computed(() => doc.value.nodes.find((n) => n.id === selectedId.value) ?? null);

  /** Commands a node may bind FROM: everything listed before it, since that is what has run. */
  const sourcesFor = (nodeId: string) => {
    const index = doc.value.nodes.findIndex((n) => n.id === nodeId);
    return index <= 0 ? [] : doc.value.nodes.slice(0, index);
  };

  const commandInfo = (name: string) =>
    commands.value.find((c) => c.name.toLowerCase() === name.toLowerCase()) ?? null;

  function blank(): PipelineDoc {
    return { schema: 1, id: "", name: "", version: "1.0.0", nodes: [], edges: [] };
  }

  async function loadCommands() {
    try {
      const result = await invoke<{ commands: CommandInfo[] }>("GetCommands");
      // Hidden-from-MCP commands stay listed: this catalogue is for a person at a canvas, and the
      // MCP flag is about what an agent may reach, not about what a pipeline may contain.
      commands.value = result?.commands ?? [];
    } catch (e: any) {
      error.value = e?.message ?? String(e);
    }
  }

  async function load(name: string) {
    busy.value = true;
    try {
      const result = await invoke<{ name: string; pipeline: PipelineDoc }>("GetPipeline", { name });
      doc.value = { ...blank(), ...result.pipeline };
      doc.value.nodes ??= [];
      doc.value.edges ??= [];
      if (!doc.value.name) doc.value.name = result.name;
      selectedId.value = doc.value.nodes[0]?.id ?? null;
      await validate();
    } catch (e: any) {
      error.value = e?.message ?? String(e);
    } finally {
      busy.value = false;
    }
  }

  /** A unique id from the command name — "Filter", "Filter2", … so a fresh node is usable at once. */
  function nextId(command: string): string {
    const base = command.replace(/[^A-Za-z0-9]/g, "") || "node";
    const stem = base.charAt(0).toLowerCase() + base.slice(1);
    if (!doc.value.nodes.some((n) => n.id === stem)) return stem;
    for (let i = 2; ; i++) if (!doc.value.nodes.some((n) => n.id === stem + i)) return stem + i;
  }

  function addNode(command: string, at?: { x: number; y: number }) {
    const node: PipelineNodeDoc = {
      id: nextId(command),
      command,
      contract: 1,
      onFailure: "Stop",
      ui: at ?? { x: 80, y: 80 + doc.value.nodes.length * 110 },
    };
    doc.value.nodes.push(node);
    syncEdges();
    selectedId.value = node.id;
    return node;
  }

  function removeNode(id: string) {
    doc.value.nodes = doc.value.nodes.filter((n) => n.id !== id);
    syncEdges();
    // Bindings that pointed at it are left ALONE on purpose: silently rewriting them would hide that
    // the pipeline is now broken, and validation says so in a sentence naming the node.
    if (selectedId.value === id) selectedId.value = doc.value.nodes[0]?.id ?? null;
  }

  /** Renaming has to carry the references, or a rename silently unwires the graph. */
  function renameNode(oldId: string, newId: string) {
    const node = doc.value.nodes.find((n) => n.id === oldId);
    if (!node || !newId || oldId === newId) return;
    if (doc.value.nodes.some((n) => n.id === newId)) return;

    node.id = newId;
    for (const other of doc.value.nodes) {
      if (!other.bind) continue;
      for (const [key, ref_] of Object.entries(other.bind)) {
        const dot = ref_.indexOf(".");
        const source = dot < 0 ? ref_ : ref_.slice(0, dot);
        if (source === oldId) other.bind[key] = newId + (dot < 0 ? "" : ref_.slice(dot));
      }
    }
    for (const edge of doc.value.edges) {
      if (edge.from === oldId) edge.from = newId;
      if (edge.to === oldId) edge.to = newId;
    }
    if (selectedId.value === oldId) selectedId.value = newId;
  }

  function move(id: string, delta: number) {
    const from = doc.value.nodes.findIndex((n) => n.id === id);
    const to = from + delta;
    if (from < 0 || to < 0 || to >= doc.value.nodes.length) return;
    const [node] = doc.value.nodes.splice(from, 1);
    doc.value.nodes.splice(to, 0, node);
    syncEdges();
  }

  /**
   * Moves `id` to run immediately after `afterId`. This is what dragging a cord from one node to
   * another means, because the cord is the run order — connecting has to change the order, or the
   * picture and the run part ways.
   *
   * Refuses to move a node ahead of something it reads: a binding may only name a node listed
   * before it, so allowing the drag would produce a graph that cannot run and blame the author.
   */
  function placeAfter(id: string, afterId: string) {
    if (id === afterId) return;

    const node = doc.value.nodes.find((n) => n.id === id);
    if (!node) return;

    const target = doc.value.nodes.findIndex((n) => n.id === afterId);
    if (target < 0) return;

    const sourcesOfMoved = Object.values(node.bind ?? {}).map((r) => r.split(".")[0]);
    const wouldPrecede = doc.value.nodes
      .slice(0, target + 1)
      .filter((n) => n.id !== id)
      .map((n) => n.id);
    if (sourcesOfMoved.some((s) => !wouldPrecede.includes(s))) return;

    const from = doc.value.nodes.findIndex((n) => n.id === id);
    const [moved] = doc.value.nodes.splice(from, 1);
    const insertAt = doc.value.nodes.findIndex((n) => n.id === afterId) + 1;
    doc.value.nodes.splice(insertAt, 0, moved);

    // `edges` mirrors the sequence: V1 runs in file order, so a stored edge that says anything else
    // is a lie waiting to be read by the validator or by whoever opens the file in an editor.
    syncEdges();
  }

  /** Rewrites `edges` as the consecutive pairs the run actually follows. */
  function syncEdges() {
    doc.value.edges = doc.value.nodes
      .slice(0, -1)
      .map((node, index) => ({ from: node.id, to: doc.value.nodes[index + 1].id }));
  }

  /** Validated INLINE, so an unsaved draft is checked without writing it to disk first. */
  async function validate() {
    try {
      validation.value = await invoke<ValidationResult>("ValidatePipeline", { pipeline: doc.value });
    } catch (e: any) {
      error.value = e?.message ?? String(e);
    }
  }

  async function save() {
    busy.value = true;
    error.value = null;
    try {
      const result = await invoke<{ name: string; validation: ValidationResult }>("SavePipeline", {
        name: doc.value.name,
        pipeline: doc.value,
      });
      validation.value = result?.validation ?? null;
      return result?.name ?? null;
    } catch (e: any) {
      error.value = e?.message ?? String(e);
      return null;
    } finally {
      busy.value = false;
    }
  }

  return {
    doc,
    commands,
    selected,
    selectedId,
    validation,
    busy,
    error,
    sourcesFor,
    commandInfo,
    loadCommands,
    load,
    addNode,
    removeNode,
    renameNode,
    move,
    placeAfter,
    validate,
    save,
  };
}
