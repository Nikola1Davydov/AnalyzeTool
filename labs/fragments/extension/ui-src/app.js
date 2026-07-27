// The extension's page: press a button, the open Revit model is exported to .frag and rendered
// right here. One command, called through the same bridge an AI agent uses over MCP.
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { FragmentsModels } from "@thatopen/fragments";

const COMMAND = "analysetool.fragments.Export";

// The command writes here by default, next to plugin.json, which is the folder WebView2 serves this
// page from — so the file is always one relative fetch away.
const MODEL_URL = "../model.frag";

const el = (id) => document.getElementById(id);
const setStatus = (text, kind = "") => {
  const node = el("status");
  node.textContent = text;
  node.className = kind;
};

const fact = (label, value, hero = false) => {
  const row = document.createElement("div");
  row.className = hero ? "fact hero" : "fact";
  row.innerHTML = "<span></span><b></b>";
  row.querySelector("span").textContent = label;
  row.querySelector("b").textContent = value;
  el("facts").appendChild(row);
};

let fragments;
let currentModel;
let scene, camera, renderer, controls;

function initScene() {
  const container = el("scene");
  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0b0e14);

  camera = new THREE.PerspectiveCamera(
    60, container.clientWidth / container.clientHeight, 0.1, 5000);
  camera.position.set(20, 16, 22);

  renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(container.clientWidth, container.clientHeight);
  container.appendChild(renderer.domElement);

  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;

  scene.add(new THREE.AmbientLight(0xffffff, 1.5));
  const sun = new THREE.DirectionalLight(0xffffff, 2.2);
  sun.position.set(20, 30, 12);
  scene.add(sun);
  // The grid sits on XZ because the format stores Y as up.
  scene.add(new THREE.GridHelper(200, 100, 0x232c3e, 0x161d29));

  new ResizeObserver(() => {
    if (!container.clientWidth) return;
    camera.aspect = container.clientWidth / container.clientHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(container.clientWidth, container.clientHeight);
  }).observe(container);

  (function animate() {
    requestAnimationFrame(animate);
    controls.update();
    fragments?.update();
    renderer.render(scene, camera);
  })();
}

async function exportAndShow(activeViewOnly) {
  if (!window.AT?.invoke) {
    setStatus("window.AT is missing — open this page from inside AnalyseTool.", "bad");
    return;
  }

  el("view").disabled = el("all").disabled = true;
  el("facts").innerHTML = "";
  el("path").textContent = "";
  setStatus(activeViewOnly ? "Exporting the active view…" : "Exporting the whole model…");

  try {
    const started = performance.now();
    const result = await window.AT.invoke(COMMAND, {
      activeViewOnly,
      includeAllParameters: el("params").checked,
    });
    const roundTrip = Math.round(performance.now() - started);

    el("path").textContent = result.path;

    fact("scope", result.scope);
    fact("items", result.items.toLocaleString());
    fact("unique geometries", result.shells.toLocaleString());
    fact("placements", result.samples.toLocaleString());
    // Above 1.0 means family geometry is shared rather than duplicated per instance.
    fact("reuse", `${result.reuse}×`, true);
    fact("triangles", result.triangles.toLocaleString());
    fact("materials", result.materials.toLocaleString());
    fact("tessellation", `${result.tessellateMs.toLocaleString()} ms`);
    fact("serialisation", `${result.writeMs.toLocaleString()} ms`);
    fact("file", `${(result.bytes / 1024).toFixed(0)} KB`);
    fact("round trip", `${roundTrip.toLocaleString()} ms`);

    setStatus("Exported. Loading…");
    await render();
    setStatus(`Done — ${result.items.toLocaleString()} items on screen.`, "ok");
  } catch (error) {
    setStatus(`Failed: ${error?.message ?? error}`, "bad");
    console.error(error);
  } finally {
    el("view").disabled = el("all").disabled = false;
  }
}

async function render() {
  fragments ??= new FragmentsModels("./worker.mjs");

  if (currentModel) {
    scene.remove(currentModel.object);
    await fragments.disposeModel(currentModel.modelId);
    currentModel = null;
  }

  // Cache-bust: the path never changes, only its contents.
  const response = await fetch(`${MODEL_URL}?t=${Date.now()}`);
  if (!response.ok) throw new Error(`could not read the exported file (${response.status})`);
  const bytes = new Uint8Array(await response.arrayBuffer());

  currentModel = await fragments.load(bytes, { modelId: `revit-${Date.now()}`, camera });
  scene.add(currentModel.object);
  await fragments.update(true);

  const box = currentModel.box;
  if (!box.isEmpty()) {
    const centre = new THREE.Vector3();
    const size = new THREE.Vector3();
    box.getCenter(centre);
    box.getSize(size);

    // Fit to the camera's field of view rather than guessing a multiple of the size, so a small
    // family and a whole building both land at a sensible distance.
    const half = Math.max(size.x, size.y, size.z) / 2;
    const distance = half / Math.tan((camera.fov * Math.PI) / 360) * 1.25 + 2;
    const direction = new THREE.Vector3(1, 0.75, 1).normalize().multiplyScalar(distance);

    controls.target.copy(centre);
    camera.position.copy(centre).add(direction);
    controls.update();

    fact("extents", `${size.x.toFixed(1)} × ${size.y.toFixed(1)} × ${size.z.toFixed(1)} m`);
  }
}

initScene();
el("view").addEventListener("click", () => exportAndShow(true));
el("all").addEventListener("click", () => exportAndShow(false));
