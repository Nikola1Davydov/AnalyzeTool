<script setup lang="ts">
import { ref, computed, onMounted, watch } from "vue";
import { useRoute } from "vue-router";
import { VueFlow, Handle, Position } from "@vue-flow/core";
import Message from "primevue/message";
import { invoke } from "@/RevitBridge";
import { usePipelineDoc } from "./usePipelineDoc";
import NodeInspector from "./NodeInspector.vue";
import { fieldNames, summarize } from "./schema";
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
  Object.fromEntries(doc.value.nodes.map((n) => [n.id, commandInfo(n.command)?.outputSchema ?? null])),
);

// Preview runs the READ-ONLY prefix and refuses at the first node that writes, naming it. That is
// the whole safety story: authoring never touches the model, and "run it for real" stays a
// deliberate act in the Pipelines pane.
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

const palette = computed(() => {
  const term = search.value.trim().toLowerCase();
  const all = [...commands.value].sort((a, b) => a.name.localeCompare(b.name));
  if (!term) return all;
  return all.filter(
    (c) =>
      c.name.toLowerCase().includes(term) || (c.description ?? "").toLowerCase().includes(term),
  );
});

const selectedIndex = computed(() =>
  doc.value.nodes.findIndex((n) => n.id === selectedId.value),
);

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
      produces: fieldNames(commandInfo(node.command)?.outputSchema),
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
        return {
          id: `bind:${source}->${node.id}:${property}`,
          source,
          target: node.id,
          label: property,
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

// Dragging a connection means "run this one right after that one". The cord IS the order, so
// connecting has to change the order — a canvas where the line you drew and the sequence that runs
// are two different things is exactly the trap this editor is meant to avoid.
function onConnect({ source, target }: any) {
  pipeline.placeAfter(target, source);
  invalidatePreview();
  void pipeline.validate();
}

// Delete on the canvas has to reach the document. A node that disappears from the drawing while the
// run still contains it is the editor lying about what it will do — the one thing a canvas over a
// file format must never do.
function onNodesDelete(nodes: any[]) {
  for (const node of nodes) pipeline.removeNode(node.id);
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
  await pipeline.loadCommands();
  const name = route.query.name;
  if (typeof name === "string" && name) await pipeline.load(name);
  else await pipeline.validate();
});

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
        label="Preview"
        icon="pi pi-eye"
        text
        size="small"
        :loading="previewing"
        :disabled="!doc.nodes.length"
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
          :delete-key-code="['Delete', 'Backspace']"
          @connect="onConnect"
          @nodes-delete="onNodesDelete"
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
              <Handle type="target" :position="Position.Top" />
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

              <!-- The fields the next node can bind to. Without this a card is an opaque box and
                   the only way to learn its shape is to run the pipeline and read the JSON. -->
              <div v-if="data.produces.length" class="mt-1 flex flex-wrap gap-1">
                <span
                  v-for="field in data.produces"
                  :key="field"
                  class="rounded bg-surface-100 px-1 font-mono text-[10px] dark:bg-surface-800"
                >
                  {{ field }}
                </span>
              </div>

              <div
                v-if="data.outcome"
                class="mt-1 text-[10px]"
                :class="data.outcome.state === 'Completed' ? 'opacity-70' : 'text-red-500'"
              >
                {{ data.outcome.error ?? summarize(data.outcome.result) }}
              </div>

              <Handle type="source" :position="Position.Bottom" />
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
          @move="(d) => pipeline.move(selected!.id, d)"
          @remove="pipeline.removeNode(selected!.id); invalidatePreview()"
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
