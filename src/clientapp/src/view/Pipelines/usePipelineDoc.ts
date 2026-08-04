import { ref, computed } from "vue";
import { invoke } from "@/RevitBridge";
import type { CommandInfo, PipelineDoc, PipelineNodeDoc, ValidationResult } from "./types";

// The document the editor edits, plus the command catalogue it builds nodes from. Kept out of the
// view so the canvas file stays about drawing.
//
// The rule this module exists to hold: the pipeline runs in FILE ORDER, not in the order edges were
// drawn. V1 has no branching, so `edges` is lineage the canvas shows and the validator checks, while
// the array order is what actually executes. A canvas that hid that would let someone arrange a
// convincing picture whose run bears no relation to it, so order is explicit and editable, and every
// node carries its position in the list.

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
    selectedId.value = node.id;
    return node;
  }

  function removeNode(id: string) {
    doc.value.nodes = doc.value.nodes.filter((n) => n.id !== id);
    doc.value.edges = doc.value.edges.filter((e) => e.from !== id && e.to !== id);
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
  }

  function connect(from: string, to: string) {
    if (from === to) return;
    if (doc.value.edges.some((e) => e.from === from && e.to === to)) return;
    doc.value.edges.push({ from, to });
  }

  function disconnect(from: string, to: string) {
    doc.value.edges = doc.value.edges.filter((e) => !(e.from === from && e.to === to));
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
    connect,
    disconnect,
    validate,
    save,
  };
}
