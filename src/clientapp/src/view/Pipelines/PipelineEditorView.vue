<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from "vue";
import { useRoute } from "vue-router";
import { VueFlow, Handle, Position } from "@vue-flow/core";
import Message from "primevue/message";
import { invoke } from "@/RevitBridge";
import { usePipelineDoc } from "./usePipelineDoc";
import NodeInspector from "./NodeInspector.vue";
import { summarize, typeLabel } from "./schema";
import type { JsonSchema, NodeOutcome, RunResult } from "./types";
import "@vue-flow/core/dist/style.css";

// The pipeline editor: palette, canvas, inspector. Its own window, not the dock — a canvas needs
// room, and the runner is what belongs beside the model.
//
// The one thing this view insists on saying out loud: V1 executes nodes in FILE ORDER, and edges are
// lineage the validator checks, not a scheduler. So every card carries its run number, and the
// inspector can move a node up or down. A canvas that let you draw a convincing picture whose run
// bore no relation to it would be worse than no canvas.

const route = useRoute();
const pipeline = usePipelineDoc();
const {
  doc,
  commands,
  selected,
  selectedId,
  validation,
  busy,
  error,
  sourcesFor,
  commandInfo,
} = pipeline;

const search = ref("");
const saved = ref<string | null>(null);

// The last preview, keyed by node id. Cleared whenever the graph changes, because stale data shown
// beside an edited node is worse than none — it reads as "this is what it does now".
const outcomes = ref<Record<string, NodeOutcome>>({});
const previewing = ref(false);
const previewNote = ref<string | null>(null);

// Which node is executing right now, or -1. Derived from the progress FRACTION rather than parsed
// out of the message: the fraction is finished-nodes over total, so fraction × total is exactly the
// index of the node that started and has not finished. No string handling, no ambiguity.
const runningIndex = ref(-1);
const progressText = ref("");

/** Declared output schema per node id, so the inspector can offer real paths for a binding. */
const outputs = computed<Record<string, JsonSchema | null>>(() =>
  Object.fromEntries(doc.value.nodes.map((n) => [n.id, pipeline.effectiveOutput(n.id)])),
);

// Preview runs the selected node and everything it READS FROM — not everything before it. A read
// placed after a purge has nothing to do with that purge beyond running later, and previewing by
// position would make it uncheckable for a reason that is not about it. What comes back is the model
// as it is now, which is the honest answer to "what shape does this node return".
//
// It still refuses at anything that writes, naming it. Authoring never touches the model; running
// for real stays a deliberate act in the Pipelines pane.
async function preview() {
  previewing.value = true;
  previewNote.value = null;
  outcomes.value = {};
  runningIndex.value = 0;
  progressText.value = "";
  try {
    const result = await invoke<RunResult>(
      "PreviewPipeline",
      { pipeline: doc.value, untilNode: selectedId.value ?? "" },
      {
        onProgress: (p) => {
          const total = doc.value.nodes.length || 1;
          runningIndex.value = Math.min(Math.round((p.fraction ?? 0) * total), total - 1);
          progressText.value = p.message ?? "";
        },
      },
    );
    outcomes.value = Object.fromEntries(result.nodes.map((n) => [n.nodeId, n]));
  } catch (e: any) {
    previewNote.value = e?.message ?? String(e);
  } finally {
    previewing.value = false;
    runningIndex.value = -1;
  }
}

// Commands that belong to the PIPELINE rather than to the model. They are what a graph is built out
// of — Filter is in nearly every pipeline worth writing — and they do not deserve to be hunted for
// in an alphabetical list between GetWorksets and PlaceFamilyInstance.
//
// A list here rather than a flag on the attribute: this is an editing convenience, and the SDK does
// not need an opinion about it. It grows when #92 adds AI nodes.
const PINNED = ["Filter"];

const matches = (c: { name: string; description: string | null }, term: string) =>
  !term ||
  c.name.toLowerCase().includes(term) ||
  (c.description ?? "").toLowerCase().includes(term);

const term = computed(() => search.value.trim().toLowerCase());

const pinned = computed(() =>
  commands.value.filter((c) => PINNED.includes(c.name) && matches(c, term.value)),
);

const palette = computed(() =>
  [...commands.value]
    .filter((c) => !PINNED.includes(c.name) && matches(c, term.value))
    .sort((a, b) => a.name.localeCompare(b.name)),
);

const selectedIndex = computed(() =>
  doc.value.nodes.findIndex((n) => n.id === selectedId.value),
);

// What a node TAKES and what it HANDS ON, as ports on the card itself.
//
// The first canvas listed a node's output fields as a row of chips and drew every data wire to the
// box rather than to the field it filled, so the graph could not be read: which value went where
// was only visible by selecting each node in turn and looking at the inspector. Both halves are
// declared — #89 — and a declared shape that nothing draws is a shape nobody sees.
//
// Outputs come from effectiveOutput, not from the raw schema, so a Filter shows the rows it is
// actually handing on instead of an untyped `items`.
function portsOf(node: any) {
  const inputs = Object.entries(commandInfo(node.command)?.inputSchema?.properties ?? {}).map(
    ([key, schema]) => ({ key, type: typeLabel(schema as JsonSchema), bound: !!node.bind?.[key] }),
  );
  const outputs = Object.entries(pipeline.effectiveOutput(node.id)?.properties ?? {}).map(
    ([key, schema]) => ({ key, type: typeLabel(schema as JsonSchema) }),
  );
  return { inputs, outputs };
}

/** A colour per kind of value, so a wire's two ends can be compared without reading them. */
function dotClass(type: string): string {
  if (type.endsWith("[]")) return "bg-emerald-500";
  if (type === "integer" || type === "number") return "bg-sky-500";
  if (type === "string") return "bg-amber-500";
  if (type === "boolean") return "bg-violet-500";
  return "bg-surface-400";
}

// Vue Flow's model, derived from the document rather than kept alongside it: two sources of truth
// for the same graph is how an editor starts saving something other than what it shows.
const flowNodes = computed(() =>
  doc.value.nodes.map((node, index) => ({
    id: node.id,
    type: "command",
    position: node.ui ?? { x: 80, y: 80 + index * 110 },
    data: {
      node,
      index,
      destructive: commandInfo(node.command)?.destructive ?? false,
      ...portsOf(node),
      outcome: outcomes.value[node.id] ?? null,
      running: previewing.value && runningIndex.value === index,
    },
  })),
);

// Two kinds of line, because there are two different truths and conflating them is what made the
// first canvas unreadable.
//
//  • The SEQUENCE cord — thick, solid, node i to node i+1, derived from the array order. This is
//    what actually runs, in the order it runs, so it is drawn whether or not anyone wired anything.
//    It was missing entirely before: a canvas of unconnected boxes cannot show where a run begins.
//  • DATA wires — thin and dashed, one per binding, labelled with the payload property they fill.
//    These say where a value comes from, which is a different question from what runs next.
const flowEdges = computed(() => {
  const nodes = doc.value.nodes;
  const running = runningIndex.value;

  const sequence = nodes.slice(0, -1).map((node, index) => ({
    id: `seq:${node.id}->${nodes[index + 1].id}`,
    source: node.id,
    target: nodes[index + 1].id,
    // Its own pair of handles, top and bottom, kept apart from the data ports: the sequence is the
    // run order and a data wire is where a value comes from, and one gesture must not mean both.
    sourceHandle: "seq-out",
    targetHandle: "seq-in",
    // Animated only for the leg being executed, so "where is it now" is answerable at a glance
    // instead of the whole graph shimmering.
    animated: running === index + 1,
    style: { strokeWidth: 2.5 },
    markerEnd: "arrowclosed" as const,
    // The order is intrinsic to the node list; there is no such thing as deleting it, so the cord
    // does not offer to be deleted rather than vanishing and coming back on the next render.
    deletable: false,
  }));

  const wires = nodes.flatMap((node) =>
    Object.entries(node.bind ?? {})
      .map(([property, reference]) => {
        const source = reference.split(".")[0];
        if (!nodes.some((n) => n.id === source)) return null; // dangling: validation names it
        // The wire lands on the PORT it fills, at both ends: the output property it reads and the
        // payload property it feeds. `families[*].id` leaves the `families` port; the rest of the
        // path is the label, because that part is a choice the author made and not a port.
        const path = reference.slice(source.length + 1);
        const outKey = path.split(/[.[]/)[0];
        return {
          id: `bind:${source}->${node.id}:${property}`,
          source,
          target: node.id,
          sourceHandle: outKey ? `out:${outKey}` : "seq-out",
          targetHandle: `in:${property}`,
          label: path.slice(outKey.length) || undefined,
          style: { strokeDasharray: "4 3", strokeWidth: 1 },
          labelStyle: { fontSize: "10px" },
          data: { nodeId: node.id, property },
        };
      })
      .filter(Boolean),
  );

  return [...sequence, ...wires] as any[];
});

function onNodeDragStop({ node }: any) {
  const target = doc.value.nodes.find((n) => n.id === node.id);
  if (target) target.ui = { x: Math.round(node.position.x), y: Math.round(node.position.y) };
}

function invalidatePreview() {
  outcomes.value = {};
  previewNote.value = null;
}

// Two gestures, because there are two different truths on this canvas.
//
// Port to port is a DATA binding: "fill this field from that value". Card to card, on the sequence
// handles, is ORDER: "run this one right after that one" — and because the cord IS the order,
// connecting there has to reorder the file. A canvas where the line you drew and the sequence that
// runs are two different things is exactly the trap this editor exists to avoid.
function onConnect({ source, target, sourceHandle, targetHandle }: any) {
  if (sourceHandle?.startsWith("out:") && targetHandle?.startsWith("in:")) {
    const refusal = pipeline.connect(
      target,
      targetHandle.slice("in:".length),
      source,
      sourceHandle.slice("out:".length),
    );
    if (refusal) error.value = refusal;
  } else {
    pipeline.placeAfter(target, source);
  }
  invalidatePreview();
  void pipeline.validate();
}

// Delete on the canvas has to reach the document. A node that disappears from the drawing while the
// run still contains it is the editor lying about what it will do — the one thing a canvas over a
// file format must never do.
//
// Handled here rather than through Vue Flow's own delete key, which removes from ITS store: nodes
// are computed from the document, so that removal was rebuilt on the next render and only ever
// looked like it worked. This does exactly what the inspector's bin does, on the same selection.
function onKeyDown(event: KeyboardEvent) {
  if (event.key !== "Delete") return;

  // Not while typing. A payload value is edited in a text box a few pixels from the canvas, and a
  // Delete there means "delete a character".
  const target = event.target as HTMLElement | null;
  const tag = target?.tagName?.toLowerCase();
  if (tag === "input" || tag === "textarea" || target?.isContentEditable) return;

  if (!selectedId.value) return;
  event.preventDefault();
  removeSelected();
}

function removeSelected() {
  if (!selectedId.value) return;
  pipeline.removeNode(selectedId.value);
  invalidatePreview();
  void pipeline.validate();
}

/** Deleting a data wire drops that binding. The sequence cord is not deletable, so anything arriving
 *  here carries the node and property it fed. */
function onEdgesDelete(edges: any[]) {
  for (const edge of edges) {
    if (!edge?.data?.property) continue;
    pipeline.removeBinding(edge.data.nodeId, edge.data.property);
  }
  invalidatePreview();
  void pipeline.validate();
}

function addFromPalette(name: string) {
  // Dropped near the last node so a new one is visible rather than stacked at the origin.
  const last = doc.value.nodes[doc.value.nodes.length - 1];
  const at = last?.ui ? { x: last.ui.x, y: last.ui.y + 120 } : undefined;
  pipeline.addNode(name, at);
  invalidatePreview();
  void pipeline.validate();
}

function outcomeBorder(outcome: NodeOutcome | null): string {
  if (outcome?.state === "Completed") return "border-green-400";
  if (outcome?.state === "Failed") return "border-red-400";
  return "border-surface-300";
}

async function save() {
  if (!doc.value.name.trim()) return;
  saved.value = await pipeline.save();
}

onMounted(async () => {
  window.addEventListener("keydown", onKeyDown);
  await pipeline.loadCommands();
  const name = route.query.name;
  if (typeof name === "string" && name) await pipeline.load(name);
  else await pipeline.validate();
});

onUnmounted(() => window.removeEventListener("keydown", onKeyDown));

watch(() => doc.value.nodes.length, () => void pipeline.validate());
</script>

<template>
  <div class="flex h-screen w-full flex-col">
    <div class="flex items-center gap-2 border-b p-2">
      <InputText v-model="doc.name" placeholder="Pipeline name" size="small" class="w-64" />
      <InputText v-model="doc.id" placeholder="id (for run receipts)" size="small" class="w-56" />
      <span class="grow" />
      <Tag
        v-if="validation"
        :severity="validation.ok ? 'success' : 'danger'"
        :value="validation.ok ? 'valid' : `${validation.errors.length} error(s)`"
      />
      <Button label="Check" icon="pi pi-check" text size="small" @click="pipeline.validate()" />
      <Button
        :label="selectedId ? `Preview ${selectedId}` : 'Preview'"
        icon="pi pi-eye"
        text
        size="small"
        :loading="previewing"
        :disabled="!doc.nodes.length"
        v-tooltip.bottom="
          selectedId
            ? 'Runs this node and whatever it reads from, against the model as it is now.'
            : 'Select a node to preview just that one. Otherwise the whole pipeline is previewed, as far as its first model-changing node.'
        "
        @click="preview"
      />
      <Button
        label="Save"
        icon="pi pi-save"
        size="small"
        :loading="busy"
        :disabled="!doc.name.trim()"
        @click="save"
      />
    </div>

    <div v-if="previewing" class="flex items-center gap-2 border-b px-2 py-1">
      <ProgressBar
        :value="Math.round(((runningIndex + 1) / (doc.nodes.length || 1)) * 100)"
        style="height: 0.4rem"
        class="grow"
      />
      <span class="text-xs opacity-70">{{ progressText }}</span>
    </div>

    <div class="flex min-h-0 grow">
      <!-- Palette: the live command catalogue, so an installed extension's commands are here too. -->
      <div class="flex w-64 shrink-0 flex-col gap-2 border-r p-2">
        <InputText v-model="search" placeholder="Search commands" size="small" />
        <div class="flex min-h-0 grow flex-col gap-1 overflow-auto">
          <button
            v-for="command in pinned"
            :key="command.name"
            class="rounded border border-primary-300 bg-primary-50 p-2 text-left text-xs hover:bg-primary-100 dark:bg-primary-900/20 dark:hover:bg-primary-900/40"
            @click="addFromPalette(command.name)"
          >
            <div class="flex items-center gap-1">
              <i class="pi pi-filter text-primary-500" style="font-size: 0.7rem" />
              <span class="font-medium">{{ command.name }}</span>
            </div>
            <div class="line-clamp-2 opacity-60">{{ command.description }}</div>
          </button>
          <div v-if="pinned.length && palette.length" class="my-1 border-t opacity-40" />

          <button
            v-for="command in palette"
            :key="command.name"
            class="rounded border p-2 text-left text-xs hover:bg-surface-100 dark:hover:bg-surface-800"
            @click="addFromPalette(command.name)"
          >
            <div class="flex items-center gap-1">
              <span class="font-medium">{{ command.name }}</span>
              <i v-if="command.destructive" class="pi pi-exclamation-triangle text-red-500" />
            </div>
            <div class="line-clamp-2 opacity-60">{{ command.description }}</div>
          </button>
        </div>
      </div>

      <div class="min-w-0 grow">
        <VueFlow
          :nodes="flowNodes"
          :edges="flowEdges"
          fit-view-on-init
          @node-drag-stop="onNodeDragStop"
          @node-click="({ node }) => (selectedId = node.id)"
          :delete-key-code="null"
          @connect="onConnect"
          @edges-delete="onEdgesDelete"
        >
          <template #node-command="{ data }">
            <div
              class="min-w-44 rounded border-2 bg-white px-3 py-2 text-xs shadow dark:bg-surface-900"
              :class="[
                data.running
                  ? 'border-primary-500 ring-2 ring-primary-400'
                  : data.node.id === selectedId
                    ? 'border-primary-500'
                    : outcomeBorder(data.outcome),
                data.destructive ? 'ring-1 ring-red-400' : '',
              ]"
            >
              <Handle id="seq-in" type="target" :position="Position.Top" />
              <div class="flex items-center gap-1">
                <span class="rounded bg-surface-200 px-1 dark:bg-surface-700">
                  {{ data.index + 1 }}
                </span>
                <span class="font-medium">{{ data.node.id }}</span>
                <span class="grow" />
                <i v-if="data.running" class="pi pi-spin pi-spinner text-primary-500" />
                <i
                  v-else-if="data.outcome?.state === 'Completed'"
                  class="pi pi-check-circle text-green-500"
                />
                <i
                  v-else-if="data.outcome?.state === 'Failed'"
                  class="pi pi-times-circle text-red-500"
                />
              </div>
              <div class="opacity-60">{{ data.node.command }}</div>

              <!-- What goes in, on the left; what comes out, on the right. Both are declared, and a
                   declared shape nothing draws is a shape nobody sees: the previous card listed the
                   outputs as chips and landed every wire on the box, so which value fed which field
                   could only be found by selecting each node and reading the inspector. -->
              <div class="mt-2 flex gap-4">
                <div class="flex flex-col gap-0.5">
                  <div
                    v-for="port in data.inputs"
                    :key="'in-' + port.key"
                    class="relative flex items-center gap-1 pr-1"
                  >
                    <Handle
                      :id="`in:${port.key}`"
                      type="target"
                      :position="Position.Left"
                      :style="{ left: '-16px', top: '50%' }"
                    />
                    <span class="h-2 w-2 shrink-0 rounded-full" :class="dotClass(port.type)" />
                    <span class="font-mono text-[10px]" :class="port.bound ? '' : 'opacity-50'">
                      {{ port.key }}
                    </span>
                    <span class="text-[9px] opacity-40">{{ port.type }}</span>
                  </div>
                </div>

                <div class="ml-auto flex flex-col items-end gap-0.5">
                  <div
                    v-for="port in data.outputs"
                    :key="'out-' + port.key"
                    class="relative flex items-center gap-1 pl-1"
                  >
                    <span class="text-[9px] opacity-40">{{ port.type }}</span>
                    <span class="font-mono text-[10px]">{{ port.key }}</span>
                    <span class="h-2 w-2 shrink-0 rounded-full" :class="dotClass(port.type)" />
                    <Handle
                      :id="`out:${port.key}`"
                      type="source"
                      :position="Position.Right"
                      :style="{ right: '-16px', top: '50%' }"
                    />
                  </div>
                </div>
              </div>

              <div
                v-if="data.outcome"
                class="mt-1 text-[10px]"
                :class="data.outcome.state === 'Completed' ? 'opacity-70' : 'text-red-500'"
              >
                {{ data.outcome.error ?? summarize(data.outcome.result) }}
              </div>

              <Handle id="seq-out" type="source" :position="Position.Bottom" />
            </div>
          </template>
        </VueFlow>
      </div>

      <div class="flex w-80 shrink-0 flex-col border-l">
        <NodeInspector
          v-if="selected"
          :key="selected.id"
          :node="selected"
          :info="commandInfo(selected.command)"
          :sources="sourcesFor(selected.id)"
          :index="selectedIndex"
          :count="doc.nodes.length"
          :outputs="outputs"
          :outcome="outcomes[selected.id] ?? null"
          :renamer="(next: string) => pipeline.renameNode(selected!.id, next)"
          @move="(d) => pipeline.move(selected!.id, d)"
          @remove="removeSelected"
          @changed="pipeline.validate()"
        />
        <div v-else class="flex flex-col gap-2 p-3 text-sm opacity-70">
          <p>Pick a command on the left to add a node, then select it to fill in its payload.</p>
          <p class="text-xs">
            Start with something that reads — <span class="font-mono">GetFamilies</span>,
            <span class="font-mono">GetElementsByCategory</span> — then press
            <strong>Preview</strong>. It runs the read-only nodes for real and shows what each one
            returned, which is what the next node's binding has to be written against. Preview
            refuses to run anything that changes the model.
          </p>
        </div>
      </div>
    </div>

    <!-- Validation last, across the full width: errors block a run, warnings do not. -->
    <div v-if="validation || error || saved || previewNote" class="max-h-40 overflow-auto border-t p-2">
      <Message v-if="error" severity="error" size="small" variant="simple">{{ error }}</Message>
      <Message v-if="previewNote" severity="info" size="small" variant="simple">
        {{ previewNote }}
      </Message>
      <Message v-if="saved" severity="success" size="small" variant="simple">
        Saved as “{{ saved }}”. Run it from the Pipelines pane.
      </Message>
      <Message
        v-for="e in validation?.errors ?? []"
        :key="'e-' + e"
        severity="error"
        size="small"
        variant="simple"
        >{{ e }}</Message
      >
      <Message
        v-for="w in validation?.warnings ?? []"
        :key="'w-' + w"
        severity="warn"
        size="small"
        variant="simple"
        >{{ w }}</Message
      >
    </div>
  </div>
</template>
