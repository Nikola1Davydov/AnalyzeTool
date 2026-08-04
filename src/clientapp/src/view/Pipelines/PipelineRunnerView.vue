<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { invoke } from "@/RevitBridge";
// Imported locally, not registered in main.js: this is the only view that uses it, and a global
// registration would put it in the entry chunk every window parses — including this dock pane.
import Message from "primevue/message";
import type { NodeOutcome, PipelineListResult, RunResult, ValidationResult } from "./types";

// The dockable runner. Deliberately NOT an editor: it lists the saved .atpipe files, checks one,
// runs it, and shows how far the run got. Authoring is a separate window, because the two have
// opposite shapes — this one has to stay usable in a pane docked beside the Project Browser.
//
// Everything here is a plain AT.invoke of a command that already exists; the panel adds no
// pipeline logic of its own. The one thing it OWNS is the confirmation: RunPipeline is hidden
// from MCP so that only a person starts a run, and until this view existed that person had to
// type an invoke call by hand. A Run button without a confirmation would quietly undo that
// decision, so a pipeline with destructive nodes asks once, naming them, before anything runs.

const names = ref<string[]>([]);
const selected = ref<string | null>(null);
const loading = ref(false);
const running = ref(false);

const validation = ref<ValidationResult | null>(null);
const result = ref<RunResult | null>(null);
const failure = ref<string | null>(null);

const progressFraction = ref(0);
const progressMessage = ref("");
const confirming = ref(false);

const canRun = computed(() => !!selected.value && !running.value && validation.value?.ok !== false);

async function loadNames() {
  loading.value = true;
  try {
    const list = await invoke<PipelineListResult>("ListPipelines");
    names.value = list?.pipelines ?? [];
    if (selected.value && !names.value.includes(selected.value)) selected.value = null;
  } catch (e: any) {
    failure.value = e?.message ?? String(e);
  } finally {
    loading.value = false;
  }
}

// Checked on selection, not on Run: a pipeline that cannot run should say so while the user is
// still choosing, and the destructive-node list is what the confirmation is built from.
async function select(name: string) {
  selected.value = name;
  result.value = null;
  failure.value = null;
  validation.value = null;
  try {
    validation.value = await invoke<ValidationResult>("ValidatePipeline", { name });
  } catch (e: any) {
    failure.value = e?.message ?? String(e);
  }
}

function requestRun() {
  if (!canRun.value) return;
  if (validation.value?.destructiveNodes?.length) {
    confirming.value = true;
    return;
  }
  void run();
}

async function run() {
  if (!selected.value) return;
  confirming.value = false;
  running.value = true;
  result.value = null;
  failure.value = null;
  progressFraction.value = 0;
  progressMessage.value = "";

  try {
    result.value = await invoke<RunResult>(
      "RunPipeline",
      { name: selected.value },
      {
        onProgress: (p) => {
          progressFraction.value = Math.round((p.fraction ?? 0) * 100);
          progressMessage.value = p.message ?? "";
        },
      },
    );
  } catch (e: any) {
    // A refusal (validation errors, a missing file) arrives as a rejected invoke, not as a run
    // result — there is no run to report on, so it gets its own line rather than an empty table.
    failure.value = e?.message ?? String(e);
  } finally {
    running.value = false;
  }
}

// Editing happens in a window the host opens for us — a WebView cannot open one itself. With no
// selection this opens a blank editor, which is also how a new pipeline starts.
async function edit() {
  try {
    await invoke("OpenPipelineEditor", { name: selected.value ?? "" });
  } catch (e: any) {
    failure.value = e?.message ?? String(e);
  }
}

function stateSeverity(state: string): string {
  if (state === "Completed") return "success";
  if (state === "Failed") return "danger";
  if (state === "Cancelled" || state === "Skipped") return "warn";
  return "info";
}

/** Warnings a node's command returned, when it returned any — the shape every write command shares. */
function nodeWarnings(node: NodeOutcome): string[] {
  const raw = (node.result as any)?.warnings;
  if (!Array.isArray(raw)) return [];
  return raw.map((w: any) => w?.description ?? String(w));
}

onMounted(loadNames);
</script>

<template>
  <div class="flex h-full flex-col gap-2 p-2 text-sm">
    <div class="flex items-center gap-2">
      <span class="font-semibold">Pipelines</span>
      <Button
        icon="pi pi-refresh"
        text
        rounded
        size="small"
        :loading="loading"
        aria-label="Reload the list"
        @click="loadNames"
      />
      <span class="grow" />
      <Button
        :label="selected ? 'Edit' : 'New'"
        :icon="selected ? 'pi pi-pencil' : 'pi pi-plus'"
        text
        size="small"
        :disabled="running"
        @click="edit"
      />
      <Button
        label="Run"
        icon="pi pi-play"
        size="small"
        :disabled="!canRun"
        :loading="running"
        @click="requestRun"
      />
    </div>

    <Select
      v-model="selected"
      :options="names"
      placeholder="Choose a pipeline"
      size="small"
      class="w-full"
      :disabled="running"
      @update:model-value="select"
    />

    <Message v-if="!loading && !names.length" severity="info" size="small" variant="simple">
      No saved pipelines. An agent can write one with SavePipeline, or drop an .atpipe file into the
      pipelines folder.
    </Message>

    <!-- Validation, before anything runs. Errors block the Run button; warnings do not. -->
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

    <div v-if="running" class="flex flex-col gap-1">
      <ProgressBar :value="progressFraction" style="height: 0.5rem" />
      <span class="text-xs opacity-70">{{ progressMessage }}</span>
    </div>

    <Message v-if="failure" severity="error" size="small" variant="simple">{{ failure }}</Message>

    <div v-if="result" class="flex min-h-0 grow flex-col gap-2 overflow-auto">
      <div class="flex items-center gap-2">
        <Tag :severity="stateSeverity(result.state)" :value="result.state" />
        <span v-if="result.stoppedAt" class="text-xs opacity-70">
          stopped at {{ result.stoppedAt }}
        </span>
      </div>

      <!-- Stop is not a rollback: which node was reached is what says how much of the model was
           already written, so every node is listed, not just the failing one. -->
      <div v-for="node in result.nodes" :key="node.nodeId" class="rounded border p-2">
        <div class="flex items-center gap-2">
          <Tag :severity="stateSeverity(node.state)" :value="node.state" />
          <span class="font-medium">{{ node.nodeId }}</span>
          <span class="text-xs opacity-60">{{ node.command }}</span>
        </div>
        <div v-if="node.error" class="mt-1 text-xs text-red-500">{{ node.error }}</div>
        <div v-if="nodeWarnings(node).length" class="mt-1 text-xs opacity-70">
          {{ nodeWarnings(node).length }} warning(s) resolved
        </div>
      </div>
    </div>

    <Dialog
      v-model:visible="confirming"
      modal
      header="This run changes the model"
      :style="{ width: '28rem' }"
    >
      <p class="mb-2 text-sm">
        <strong>{{ selected }}</strong> contains nodes that write to the document. Stopping a run
        does not undo what it already did.
      </p>
      <ul class="mb-3 list-inside list-disc text-sm">
        <li v-for="n in validation?.destructiveNodes ?? []" :key="n">{{ n }}</li>
      </ul>
      <template #footer>
        <Button label="Cancel" text size="small" @click="confirming = false" />
        <Button label="Run" icon="pi pi-play" severity="danger" size="small" @click="run" />
      </template>
    </Dialog>
  </div>
</template>
