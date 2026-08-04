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
  try {
    const result = await invoke<RunResult>("PreviewPipeline", {
      pipeline: doc.value,
      untilNode: selectedId.value ?? "",
    });
    outcomes.value = Object.fromEntries(result.nodes.map((n) => [n.nodeId, n]));
  } catch (e: any) {
    previewNote.value = e?.message ?? String(e);
  } finally {
    previewing.value = false;
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
    },
  })),
);

const flowEdges = computed(() =>
  doc.value.edges.map((edge) => ({
    id: `${edge.from}->${edge.to}`,
    source: edge.from,
    target: edge.to,
  })),
);

function onNodeDragStop({ node }: any) {
  const target = doc.value.nodes.find((n) => n.id === node.id);
  if (target) target.ui = { x: Math.round(node.position.x), y: Math.round(node.position.y) };
}

function invalidatePreview() {
  outcomes.value = {};
  previewNote.value = null;
}

function onConnect({ source, target }: any) {
  pipeline.connect(source, target);
  void pipeline.validate();
}

function onEdgesDelete(edges: any[]) {
  for (const edge of edges) pipeline.disconnect(edge.source, edge.target);
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
          @connect="onConnect"
          @edges-delete="onEdgesDelete"
        >
          <template #node-command="{ data }">
            <div
              class="min-w-40 rounded border-2 bg-white px-3 py-2 text-xs shadow dark:bg-surface-900"
              :class="[
                data.node.id === selectedId ? 'border-primary-500' : 'border-surface-300',
                data.destructive ? 'ring-1 ring-red-400' : '',
              ]"
            >
              <Handle type="target" :position="Position.Top" />
              <div class="flex items-center gap-1">
                <span class="rounded bg-surface-200 px-1 dark:bg-surface-700">
                  {{ data.index + 1 }}
                </span>
                <span class="font-medium">{{ data.node.id }}</span>
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
          @rename="(v) => pipeline.renameNode(selected!.id, v)"
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
