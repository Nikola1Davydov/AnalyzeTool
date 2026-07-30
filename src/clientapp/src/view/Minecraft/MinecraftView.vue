<script setup>
// Minecraft dock panel: pick a block type, click "Build" and place blocks by clicking in the model
// (Esc ends the session). Region fill/clear mirror the MCP commands so a human can do by hand what
// an AI does via MCP. Colors here are display-only — the real material colors live in the C# palette.
import { ref } from "vue";
import { invoke } from "@/RevitBridge";

const PALETTE = {
  grass: "#6b8e23",
  dirt: "#866043",
  stone: "#7d7d7d",
  cobblestone: "#646464",
  bedrock: "#3c3c3c",
  sand: "#dbcfa3",
  gravel: "#887e7e",
  oak_planks: "#a2824e",
  oak_log: "#665132",
  leaves: "#3c7828",
  glass: "#c8e6f0",
  water: "#3c5ac8",
  lava: "#e66e14",
  brick: "#963c32",
  stone_brick: "#6e6e6e",
  snow: "#f0f0fa",
  ice: "#a0bef0",
  obsidian: "#191228",
  wool_white: "#ebebeb",
  wool_red: "#b43232",
  wool_blue: "#374baa",
  wool_yellow: "#dcc83c",
  wool_green: "#50823c",
  wool_black: "#232323",
  gold: "#fad75a",
  iron: "#d7d7d7",
  diamond: "#78e6dc",
};

const selected = ref("grass");
const customBlock = ref("");
const blockSize = ref(1);
const busy = ref(false);
const status = ref("");
const statusIsError = ref(false);

const fillFrom = ref({ x: 0, y: 0, z: 0 });
const fillTo = ref({ x: 4, y: 3, z: 4 });
const hollow = ref(true);

function currentBlock() {
  return customBlock.value.trim() || selected.value;
}

function pick(name) {
  selected.value = name;
  customBlock.value = "";
}

function report(text, isError = false) {
  status.value = text;
  statusIsError.value = isError;
}

async function run(command, payload, describe) {
  busy.value = true;
  report("Working…");
  try {
    const res = await invoke(command, payload);
    if (res && res.ok === false) report(res.error || "Failed.", true);
    else report(describe(res));
  } catch (e) {
    report(e?.message || String(e), true);
  } finally {
    busy.value = false;
  }
}

function startBuilding() {
  run(
    "McBuildInteractive",
    { block: currentBlock(), blockSizeMeters: blockSize.value },
    (r) => `Session finished — ${r.placed} block(s) placed. Click Build to continue.`,
  );
}

function fillRegion() {
  run(
    "McFillRegion",
    {
      from: fillFrom.value,
      to: fillTo.value,
      block: currentBlock(),
      hollow: hollow.value,
      blockSizeMeters: blockSize.value,
    },
    (r) => `Filled: ${r.created} block(s)${r.failed ? `, ${r.failed} failed` : ""}.`,
  );
}

function clearRegion() {
  run(
    "McClearRegion",
    { from: fillFrom.value, to: fillTo.value, blockSizeMeters: blockSize.value },
    (r) => `Removed ${r.deleted} block(s).`,
  );
}
</script>

<template>
  <div class="mc-panel">
    <h3 class="mc-title">⛏️ Minecraft</h3>

    <div class="mc-section">
      <div class="mc-label">Block</div>
      <div class="mc-palette">
        <button
          v-for="(color, name) in PALETTE"
          :key="name"
          class="mc-swatch"
          :class="{ selected: selected === name && !customBlock.trim() }"
          :style="{ background: color }"
          :title="name"
          @click="pick(name)"
        />
      </div>
      <div class="mc-row">
        <input v-model="customBlock" class="mc-input" placeholder="…or custom block name" />
      </div>
      <div class="mc-row">
        <label class="mc-label">Block size, m</label>
        <input v-model.number="blockSize" class="mc-input mc-size" type="number" min="0.1" step="0.1" />
      </div>
    </div>

    <div class="mc-section">
      <button class="mc-button primary" :disabled="busy" @click="startBuilding">
        ▶ Build: {{ currentBlock() }}
      </button>
      <div class="mc-hint">Click in the model to place blocks, Esc to finish. Works in a floor plan (or a 3D view with a work plane). Ctrl+Z removes the last block.</div>
    </div>

    <div class="mc-section">
      <div class="mc-label">Region (cells, y is up)</div>
      <div class="mc-grid">
        <span></span><span>x</span><span>y</span><span>z</span>
        <span>from</span>
        <input v-model.number="fillFrom.x" type="number" class="mc-input" />
        <input v-model.number="fillFrom.y" type="number" class="mc-input" />
        <input v-model.number="fillFrom.z" type="number" class="mc-input" />
        <span>to</span>
        <input v-model.number="fillTo.x" type="number" class="mc-input" />
        <input v-model.number="fillTo.y" type="number" class="mc-input" />
        <input v-model.number="fillTo.z" type="number" class="mc-input" />
      </div>
      <label class="mc-row mc-check">
        <input v-model="hollow" type="checkbox" />
        <span>Hollow (shell only — walls, floor, ceiling)</span>
      </label>
      <div class="mc-row">
        <button class="mc-button" :disabled="busy" @click="fillRegion">Fill region</button>
        <button class="mc-button danger" :disabled="busy" @click="clearRegion">Clear region</button>
      </div>
    </div>

    <div v-if="status" class="mc-status" :class="{ error: statusIsError }">{{ status }}</div>
  </div>
</template>

<style scoped>
.mc-panel {
  padding: 10px 12px;
  font-size: 13px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.mc-title {
  margin: 0;
  font-size: 15px;
}
.mc-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.mc-label {
  font-weight: 600;
  opacity: 0.8;
}
.mc-palette {
  display: grid;
  grid-template-columns: repeat(9, 24px);
  gap: 4px;
}
.mc-swatch {
  width: 24px;
  height: 24px;
  border: 1px solid rgba(0, 0, 0, 0.35);
  border-radius: 4px;
  cursor: pointer;
  padding: 0;
}
.mc-swatch.selected {
  outline: 2px solid #3b82f6;
  outline-offset: 1px;
}
.mc-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mc-input {
  width: 100%;
  min-width: 0;
  padding: 4px 6px;
  border: 1px solid rgba(128, 128, 128, 0.5);
  border-radius: 4px;
  background: transparent;
  color: inherit;
  font-size: 12px;
}
.mc-size {
  max-width: 70px;
}
.mc-grid {
  display: grid;
  grid-template-columns: 38px 1fr 1fr 1fr;
  gap: 4px;
  align-items: center;
}
.mc-grid > span {
  opacity: 0.7;
  text-align: center;
}
.mc-grid > span:first-child,
.mc-grid > span:nth-child(5n) {
  text-align: left;
}
.mc-check {
  font-size: 12px;
}
.mc-button {
  padding: 6px 10px;
  border: 1px solid rgba(128, 128, 128, 0.5);
  border-radius: 6px;
  background: transparent;
  color: inherit;
  cursor: pointer;
  font-size: 13px;
}
.mc-button.primary {
  background: #3b82f6;
  border-color: #3b82f6;
  color: #fff;
  font-weight: 600;
}
.mc-button.danger {
  border-color: #c04545;
  color: #c04545;
}
.mc-button:disabled {
  opacity: 0.5;
  cursor: default;
}
.mc-hint {
  font-size: 11px;
  opacity: 0.65;
  line-height: 1.35;
}
.mc-status {
  font-size: 12px;
  padding: 6px 8px;
  border-radius: 4px;
  background: rgba(59, 130, 246, 0.12);
}
.mc-status.error {
  background: rgba(192, 69, 69, 0.15);
  color: #c04545;
}
</style>
