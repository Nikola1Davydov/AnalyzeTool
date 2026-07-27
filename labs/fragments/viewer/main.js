// Loads a .frag written by our C# writer into That Open's OWN runtime and renders it.
//
// This is the check that cannot be done headless: everything up to here proved the buffer parses,
// but only their FragmentsModels can say whether it also loads, tessellates and draws. The page
// reports what the runtime reads back out of the file, so a green render is backed by numbers.
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { FragmentsModels } from "@thatopen/fragments";

const log = (message, kind = "") => {
  const el = document.createElement("div");
  el.className = `line ${kind}`;
  el.textContent = message;
  document.getElementById("log").appendChild(el);
};

const fact = (label, value) => {
  const row = document.createElement("div");
  row.className = "fact";
  row.innerHTML = `<span>${label}</span><b></b>`;
  row.querySelector("b").textContent = value;
  document.getElementById("facts").appendChild(row);
};

function base64ToBytes(base64) {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

async function main() {
  const container = document.getElementById("scene");

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x10141c);

  const camera = new THREE.PerspectiveCamera(
    60, container.clientWidth / container.clientHeight, 0.1, 2000);
  camera.position.set(14, 12, 16);

  // preserveDrawingBuffer so an automated check can read pixels back off the canvas after
  // the frame has been composited.
  const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(container.clientWidth, container.clientHeight);
  container.appendChild(renderer.domElement);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;

  scene.add(new THREE.AmbientLight(0xffffff, 1.6));
  const sun = new THREE.DirectionalLight(0xffffff, 2.2);
  sun.position.set(12, 20, 8);
  scene.add(sun);
  // Grid on XZ — which is the ground plane precisely because the format stores Y as up.
  scene.add(new THREE.GridHelper(40, 40, 0x2a3550, 0x1c2437));
  scene.add(new THREE.AxesHelper(3));

  window.addEventListener("resize", () => {
    camera.aspect = container.clientWidth / container.clientHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(container.clientWidth, container.clientHeight);
  });

  log("booting That Open runtime…");

  // Two packagings, one code path:
  //  - served over HTTP: the worker is a real module URL, the model is fetched.
  //  - single self-contained file: both are inlined, the worker as a blob URL.
  // The blob form only works where the page has a real origin — a module worker created from a
  // blob is refused when the page itself came from file://.
  const workerUrl = window.__FRAG_WORKER_URL__
    ?? URL.createObjectURL(new Blob([window.__FRAG_WORKER__], { type: "text/javascript" }));

  const fragments = new FragmentsModels(workerUrl);
  log(`@thatopen/fragments ${window.__FRAG_META__.fragmentsVersion} · three ${THREE.REVISION}`, "ok");

  const bytes = window.__FRAG_URL__
    ? new Uint8Array(await (await fetch(window.__FRAG_URL__)).arrayBuffer())
    : base64ToBytes(window.__FRAG_DATA__);
  log(`loading ${bytes.length} bytes written by ${window.__FRAG_META__.writer}…`);

  const model = await fragments.load(bytes, { modelId: "analysetool-demo", camera });
  scene.add(model.object);
  await fragments.update(true);

  log("model loaded", "ok");

  // Everything below is read back out of the loaded model by THEIR code, not ours.
  const localIds = await model.getLocalIds();
  const categories = await model.getCategories();
  const withGeometry = await model.getItemsIdsWithGeometry();
  const guids = await model.getGuids();
  const box = model.box;

  const size = new THREE.Vector3();
  box.getSize(size);

  fact("items", localIds.length);
  fact("with geometry", withGeometry.length);
  fact("categories", categories.join(", "));
  fact("guids", guids.length);
  fact("first localIds", localIds.slice(0, 6).join(", "));
  fact("bbox size (x,y,z)", `${size.x.toFixed(2)} × ${size.y.toFixed(2)} × ${size.z.toFixed(2)} m`);
  fact("bbox min y", box.min.y.toFixed(2));
  fact("bbox max y", box.max.y.toFixed(2));

  let meshes = 0;
  let triangles = 0;
  model.object.traverse((child) => {
    if (!child.isMesh) return;
    meshes++;
    const geometry = child.geometry;
    const count = geometry.index ? geometry.index.count : geometry.attributes.position.count;
    triangles += (count / 3) * (child.count ?? 1);
  });
  fact("meshes in scene", meshes);
  fact("triangles drawn", Math.round(triangles));

  // Pass/fail has to hold for ANY file, including a real Revit export — so it asserts only what
  // must be true of every model: it loaded, it has items, and it produced drawable geometry.
  const loaded = localIds.length > 0 && meshes > 0 && triangles > 0;

  // Informational, not a verdict: on a floor plate Y is the short axis, on a tower it is the long
  // one. What it is NOT, if the export is right, is a Z-up model lying on its side.
  log(`extents  x ${size.x.toFixed(1)}  y ${size.y.toFixed(1)}  z ${size.z.toFixed(1)} m ` +
      `— y is the up axis, so that is the model's height`);
  log(`localIds ${localIds.slice(0, 4).join(", ")}${localIds.length > 4 ? " …" : ""} ` +
      `— from Revit these are ElementIds`);
  if (withGeometry.length && localIds.length) {
    const share = (withGeometry.length / localIds.length) * 100;
    log(`${withGeometry.length} of ${localIds.length} items carry geometry (${share.toFixed(0)}%)`);
  }

  document.getElementById("verdict").textContent = loaded
    ? "PASS — the file loads, renders and reads back through That Open's runtime"
    : "FAIL — nothing drawable came out of the file";
  document.getElementById("verdict").className = loaded ? "pass" : "fail";

  // Frame the model.
  const center = new THREE.Vector3();
  box.getCenter(center);
  controls.target.copy(center);
  camera.position.copy(center).add(new THREE.Vector3(12, 9, 14));
  controls.update();

  function animate() {
    requestAnimationFrame(animate);
    controls.update();
    fragments.update();
    renderer.render(scene, camera);
  }
  animate();
}

main().catch((error) => {
  log(`FAILED: ${error?.message ?? error}`, "bad");
  document.getElementById("verdict").textContent = "FAIL — see log";
  document.getElementById("verdict").className = "fail";
  console.error(error);
});
