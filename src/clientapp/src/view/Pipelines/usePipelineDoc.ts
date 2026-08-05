import { ref, computed } from "vue";
import { invoke } from "@/RevitBridge";
import { suggestBinding } from "./schema";
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

  /**
   * A node's output schema with pass-through rows resolved.
   *
   * A Filter's output IS its input, so it declares `items` as an untyped array — nothing can be
   * said about the rows in advance. Left at that, every shape-based suggestion stops dead at the
   * first Filter and reaches PAST it to the raw source. In front of a purge that is the difference
   * between deleting the two unused families and deleting all 286.
   *
   * So a node with one untyped array out and one array wired in is understood as handing on the
   * rows it was given, and the item schema is borrowed from wherever they came from.
   */
  const isArray = (s: any) =>
    s?.type === "array" || (Array.isArray(s?.type) && s.type.includes("array"));

  /** The output property a command hands its input through unchanged, if it has one — an array whose
   *  items are undeclared, because nothing CAN be declared about rows that came from elsewhere. */
  function passThroughOf(command: string): [string, any] | null {
    const properties = commandInfo(command)?.outputSchema?.properties;
    if (!properties) return null;
    return (
      Object.entries(properties).find(
        ([, s]: any) => isArray(s) && !s.items?.properties && !s.items?.type,
      ) ?? null
    );
  }

  function effectiveOutput(nodeId: string): any {
    const node = doc.value.nodes.find((n) => n.id === nodeId);
    if (!node) return null;

    const own = commandInfo(node.command)?.outputSchema ?? null;
    if (!own?.properties || !node.bind) return own;

    const passThrough = passThroughOf(node.command);
    if (!passThrough) return own;

    for (const reference of Object.values(node.bind)) {
      const [sourceId, ...rest] = reference.split(".");
      const sourceSchema = effectiveOutput(sourceId);
      const arrayName = rest.join(".").split("[")[0];
      const rows = sourceSchema?.properties?.[arrayName];
      if (!isArray(rows) || !rows.items) continue;

      const [key, schema] = passThrough;
      return { ...own, properties: { ...own.properties, [key]: { ...(schema as any), items: rows.items } } };
    }
    return own;
  }

  const commandInfo = (name: string) =>
    commands.value.find((c) => c.name.toLowerCase() === name.toLowerCase()) ?? null;

  /**
   * A fresh pipeline id.
   *
   * It keys the run receipt and names the run in every log line, and nothing about its value is a
   * decision — so it is minted rather than left as an empty box the author is warned about after
   * building a whole pipeline. The field stays editable for anyone who wants a readable one.
   */
  function newId(): string {
    const uuid = globalThis.crypto?.randomUUID?.();
    return uuid ? uuid.slice(0, 8) : Math.random().toString(36).slice(2, 10);
  }

  function blank(): PipelineDoc {
    return { schema: 1, id: newId(), name: "", version: "1.0.0", nodes: [], edges: [] };
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
      // An older file written without one gets an id here, and keeps it on the next Save. Warning
      // about a missing key the editor can supply itself is not a useful thing to tell an author.
      if (!doc.value.id) doc.value.id = newId();
      if (!doc.value.name) doc.value.name = result.name;
      selectedId.value = doc.value.nodes[0]?.id ?? null;
      await validate();
    } catch (e: any) {
      error.value = e?.message ?? String(e);
    } finally {
      busy.value = false;
    }
  }

  /**
   * The id a new node gets.
   *
   * Usually the command name is the whole answer: `purgeFamilies` says what the node is. The
   * exception is a node that hands its input through unchanged — a Filter has no shape of its own,
   * so `filter` and `filter2` name nothing, and those are precisely the ids an author reads in a
   * binding (`filter2.items[*].id`) while deciding whether it is the right list. A pass-through
   * node is therefore named after WHERE ITS DATA CAME FROM: `filterFamilies`, `filterFamilyTypeRows`.
   *
   * The leading verb of the source is dropped — `getFamilies` contributes "Families", not
   * "GetFamilies" — since the reading is the source's business, not this node's.
   */
  function nextId(command: string, bind?: Record<string, string>): string {
    const base = command.replace(/[^A-Za-z0-9]/g, "") || "node";
    let stem = base.charAt(0).toLowerCase() + base.slice(1);

    const sourceId = bind && Object.values(bind)[0]?.split(".")[0];
    if (sourceId && passThroughOf(command)) {
      const subject = sourceId.replace(/^(get|list|read|find)/i, "");
      // Skipped when the source is itself named after this one ("filter" over "filterFamilies"),
      // where appending it would only stutter; the number then does the distinguishing.
      if (subject && !sourceId.toLowerCase().startsWith(stem.toLowerCase()))
        stem += subject.charAt(0).toUpperCase() + subject.slice(1);
    }

    if (!doc.value.nodes.some((n) => n.id === stem)) return stem;
    for (let i = 2; ; i++) if (!doc.value.nodes.some((n) => n.id === stem + i)) return stem + i;
  }

  function addNode(command: string, at?: { x: number; y: number }) {
    // Wired automatically where the shapes leave no real choice, so a fresh Filter arrives already
    // reading the list it is meant to filter instead of sitting there doing nothing. Worked out
    // BEFORE the id, because for a pass-through node the wire is what the id is made of.
    let bind: Record<string, string> | undefined;
    const inputs = commandInfo(command)?.inputSchema?.properties ?? {};
    for (const [key, schema] of Object.entries(inputs)) {
      const found = findSource(schema, key, doc.value.nodes.length);
      if (!found) continue;
      bind = { [key]: `${found.nodeId}.${found.path}` };
      break; // one wire, the obvious one — the rest is the author's call
    }

    const node: PipelineNodeDoc = {
      id: nextId(command, bind),
      command,
      contract: 1,
      onFailure: "Stop",
      ui: at ?? { x: 80, y: 80 + doc.value.nodes.length * 110 },
    };
    if (bind) node.bind = bind;

    doc.value.nodes.push(node);
    syncEdges();
    selectedId.value = node.id;
    return node;
  }

  /**
   * The nearest earlier node whose output fits this input, searched BACKWARDS from the one just
   * before `beforeIndex`.
   *
   * Nearest rather than merely previous, because the immediately preceding node often has nothing
   * to give: a purge returns `{deleted, failed}`, so a Filter placed after one has to reach past it
   * to the list it is actually meant to narrow. Backwards, because the closest fitting node is
   * nearly always the intended one — and when it is not, the author changes the source, which is
   * why that choice stays available at all.
   */
  function findSource(target: any, targetName: string, beforeIndex: number) {
    for (let i = Math.min(beforeIndex, doc.value.nodes.length) - 1; i >= 0; i--) {
      const candidate = doc.value.nodes[i];
      const path = suggestBinding(target, effectiveOutput(candidate.id), targetName);
      if (path) return { nodeId: candidate.id, path };
    }
    return null;
  }

  /**
   * Renames a node AND every binding that reads it.
   *
   * A bare id field was removed once, and rightly: rewriting an id left the bindings pointing at a
   * node that no longer existed, and the editor said nothing until the run failed. The fix is not to
   * forbid renaming — a generated name is a guess, and `filter2` in a purge pipeline is worth
   * correcting — it is to make the rename carry its references with it, which is possible precisely
   * because every reference lives in this one document.
   *
   * Returns the reason it refused, or null when it went through.
   */
  function renameNode(id: string, next: string): string | null {
    const name = next.trim();
    if (name === id) return null;
    if (!name) return "A node needs an id.";
    // The dot separates the node from the path inside its result, so it cannot appear in either.
    if (name.includes(".")) return "A node id cannot contain a dot.";
    if (doc.value.nodes.some((n) => n.id === name)) return `There is already a node called '${name}'.`;

    const node = doc.value.nodes.find((n) => n.id === id);
    if (!node) return null;
    node.id = name;

    for (const other of doc.value.nodes) {
      if (!other.bind) continue;
      for (const [key, reference] of Object.entries(other.bind)) {
        const dot = reference.indexOf(".");
        const sourceId = dot < 0 ? reference : reference.slice(0, dot);
        if (sourceId !== id) continue;
        other.bind[key] = dot < 0 ? name : name + reference.slice(dot);
      }
    }

    syncEdges();
    if (selectedId.value === id) selectedId.value = name;
    return null;
  }

  function removeNode(id: string) {
    doc.value.nodes = doc.value.nodes.filter((n) => n.id !== id);
    syncEdges();
    // Bindings that pointed at it are left ALONE on purpose: silently rewriting them would hide that
    // the pipeline is now broken, and validation says so in a sentence naming the node.
    if (selectedId.value === id) selectedId.value = doc.value.nodes[0]?.id ?? null;
  }

  /** Drops one binding, leaving the node in place — deleting a data wire says "stop feeding this
   *  field", not "remove the node it fed". */
  function removeBinding(nodeId: string, property: string) {
    const node = doc.value.nodes.find((n) => n.id === nodeId);
    if (!node?.bind) return;
    delete node.bind[property];
    if (!Object.keys(node.bind).length) delete node.bind;
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
    renameNode,
    removeNode,
    removeBinding,
    move,
    placeAfter,
    findSource,
    effectiveOutput,
    validate,
    save,
  };
}
