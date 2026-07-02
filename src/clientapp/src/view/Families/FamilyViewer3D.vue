<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from "vue";
import { invoke } from "@/RevitBridge";
import { getCachedMesh, setCachedMesh, type FamilyMeshData } from "@/utils/familyCache";

const props = defineProps<{ familyId: number; uniqueId: string; versionGuid: string }>();

const container = ref<HTMLDivElement | null>(null);
const state = ref<"loading" | "ready" | "empty" | "error">("loading");
const message = ref("");

// three.js objects are kept out of Vue's reactivity (no ref) — proxying them breaks WebGL.
let THREE: any = null;
let OrbitControlsClass: any = null;
let renderer: any = null;
let scene: any = null;
let camera: any = null;
let controls: any = null;
let modelGroup: any = null;
let frame = 0;
let resizeObserver: ResizeObserver | null = null;
let disposed = false;

// force = bypass the cache (used by the Refresh button) and overwrite it with the fresh result.
async function fetchMesh(force: boolean): Promise<FamilyMeshData | null> {
  if (!force && props.uniqueId) {
    const cached = await getCachedMesh(props.uniqueId, props.versionGuid);
    if (cached) return cached;
  }
  const res = await invoke<FamilyMeshData>("GetFamilyMesh", { id: props.familyId });
  if (res && props.uniqueId) void setCachedMesh(props.uniqueId, props.versionGuid, res);
  return res;
}

async function ensureThree() {
  if (THREE) return;
  THREE = await import("three");
  const mod = await import("three/addons/controls/OrbitControls.js");
  OrbitControlsClass = mod.OrbitControls;
}

// Renderer / camera / lights / controls — created once, reused across refreshes.
function setupScene() {
  const el = container.value!;
  const width = el.clientWidth || 600;
  const height = el.clientHeight || 400;

  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(45, width / height, 0.01, 100000);

  renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
  renderer.setPixelRatio(window.devicePixelRatio || 1);
  renderer.setSize(width, height);
  el.appendChild(renderer.domElement);

  scene.add(new THREE.HemisphereLight(0xffffff, 0x444444, 1.1));
  const dir = new THREE.DirectionalLight(0xffffff, 1.3);
  dir.position.set(1, 1.5, 1);
  scene.add(dir);

  controls = new OrbitControlsClass(camera, renderer.domElement);
  controls.enableDamping = true;

  resizeObserver = new ResizeObserver(onResize);
  resizeObserver.observe(el);
  animate();
}

function disposeModel() {
  if (!modelGroup) return;
  scene?.remove(modelGroup);
  modelGroup.traverse((o: any) => {
    o.geometry?.dispose?.();
    o.material?.dispose?.();
  });
  modelGroup = null;
}

// (Re)build the mesh group from a fresh FamilyMeshData and frame the camera on it.
function buildModel(mesh: FamilyMeshData) {
  disposeModel();

  // One mesh per material part, each with its approximate colour + opacity from Revit.
  const group = new THREE.Group();
  const geometries: any[] = [];
  for (const part of mesh.parts ?? []) {
    const geom = new THREE.BufferGeometry();
    geom.setAttribute("position", new THREE.Float32BufferAttribute(part.positions, 3));
    geom.setIndex(part.indices);
    geom.computeVertexNormals();
    const [r, g, b] = part.color ?? [154, 166, 178];
    const transparent = part.opacity < 1;
    const material = new THREE.MeshStandardMaterial({
      color: new THREE.Color(r / 255, g / 255, b / 255),
      metalness: 0.1,
      roughness: 0.75,
      side: THREE.DoubleSide,
      transparent,
      opacity: part.opacity,
      depthWrite: !transparent, // let translucent parts show what's behind them
    });
    group.add(new THREE.Mesh(geom, material));
    geometries.push(geom);
  }

  // Recentre the whole model on its combined bounding box (so all parts move together), then rotate to
  // match Revit's Z-up orientation.
  const box = new THREE.Box3().setFromObject(group);
  const center = box.getCenter(new THREE.Vector3());
  geometries.forEach((g) => g.translate(-center.x, -center.y, -center.z));
  group.rotateX(-Math.PI / 2);
  scene.add(group);
  modelGroup = group;

  // Frame the model: place the camera on the bounding sphere at a comfortable distance.
  const radius = box.getBoundingSphere(new THREE.Sphere()).radius || 1;
  const dist = radius / Math.sin((camera.fov * Math.PI) / 360);
  camera.position.set(dist, dist * 0.85, dist);
  camera.near = Math.max(radius / 1000, 0.001);
  camera.far = dist * 12;
  camera.updateProjectionMatrix();
  controls.target.set(0, 0, 0);
  controls.update();
}

async function load(force: boolean) {
  state.value = "loading";
  let mesh: FamilyMeshData | null;
  try {
    mesh = await fetchMesh(force);
  } catch (e) {
    console.error("Mesh load failed", e);
    state.value = "error";
    message.value = String((e as Error)?.message ?? e);
    return;
  }
  if (disposed) return;

  if (!mesh?.available || !mesh.parts?.length) {
    disposeModel();
    state.value = "empty";
    message.value = mesh?.reason ?? "No 3D geometry available.";
    return;
  }

  // Lazy-load three only when a viewer is actually shown, so the gallery grid stays light.
  await ensureThree();
  if (disposed) return;
  if (!renderer) setupScene();

  buildModel(mesh);
  state.value = "ready";
}

const refreshing = ref(false);
async function refresh() {
  if (refreshing.value) return;
  refreshing.value = true;
  try {
    await load(true); // bypass + overwrite the cache
  } finally {
    refreshing.value = false;
  }
}

function animate() {
  frame = requestAnimationFrame(animate);
  controls?.update();
  if (renderer && scene && camera) renderer.render(scene, camera);
}

function onResize() {
  const el = container.value;
  if (!renderer || !camera || !el) return;
  const w = el.clientWidth;
  const h = el.clientHeight;
  if (!w || !h) return;
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  renderer.setSize(w, h);
}

onMounted(() => load(false));
onBeforeUnmount(() => {
  disposed = true;
  cancelAnimationFrame(frame);
  resizeObserver?.disconnect();
  controls?.dispose?.();
  disposeModel();
  renderer?.dispose?.();
  if (renderer?.domElement && container.value?.contains(renderer.domElement))
    container.value.removeChild(renderer.domElement);
  scene = null;
});
</script>

<template>
  <div class="relative w-full h-full min-h-[300px] bg-surface-100 rounded-lg overflow-hidden">
    <div ref="container" class="absolute inset-0" />

    <!-- Rebuild the geometry from Revit and overwrite the cache. -->
    <div class="absolute z-10" style="top: 0.5rem; right: 0.5rem">
      <Button
        icon="pi pi-refresh"
        rounded
        size="small"
        severity="secondary"
        :loading="refreshing"
        :disabled="refreshing"
        v-tooltip.left="'Rebuild 3D & refresh cache'"
        @click="refresh"
      />
    </div>

    <div
      v-if="state !== 'ready'"
      class="absolute inset-0 flex flex-col items-center justify-center text-surface-500 gap-2 pointer-events-none px-4 text-center"
    >
      <i v-if="state === 'loading'" class="pi pi-spin pi-spinner text-2xl" />
      <i v-else-if="state === 'empty'" class="pi pi-box text-3xl" />
      <i v-else class="pi pi-exclamation-triangle text-3xl text-amber-500" />
      <span class="text-sm">{{ state === "loading" ? "Loading 3D…" : message }}</span>
    </div>
  </div>
</template>
