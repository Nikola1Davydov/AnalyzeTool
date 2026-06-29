// Client-side cache for Family Control, stored in the WebView's IndexedDB (which lives in the WebView2
// user-data profile, so it persists across Revit sessions). Keyed by the family's Revit UniqueId —
// globally unique, so entries from different documents never collide. A cache hit renders instantly and
// never touches the (single-threaded, possibly busy) Revit thread.

const DB_NAME = "analysetool";
const VERSION = 3;
const PREVIEW_STORE = "family-previews";
const MESH_STORE = "family-meshes";

let dbPromise: Promise<IDBDatabase> | null = null;

function openDb(): Promise<IDBDatabase> {
  if (dbPromise) return dbPromise;
  dbPromise = new Promise<IDBDatabase>((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(PREVIEW_STORE)) db.createObjectStore(PREVIEW_STORE);
      // v3 changed the mesh shape (per-material parts with colour/opacity) — drop any old-shape meshes.
      if (db.objectStoreNames.contains(MESH_STORE)) db.deleteObjectStore(MESH_STORE);
      db.createObjectStore(MESH_STORE);
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
  return dbPromise;
}

// Each entry stores the family's VersionGuid alongside the data. A read whose stored version differs
// from the family's current VersionGuid (the family was edited/reloaded) is treated as a miss and gets
// overwritten — so the same key holds exactly one entry and stale previews/meshes never linger.
interface Entry<T> {
  v: string;
  d: T;
}

function get<T>(store: string, key: string, version: string): Promise<T | null> {
  return openDb()
    .then(
      (db) =>
        new Promise<T | null>((resolve) => {
          const req = db.transaction(store, "readonly").objectStore(store).get(key);
          req.onsuccess = () => {
            const entry = req.result as Entry<T> | undefined;
            resolve(entry && entry.v === version ? entry.d : null);
          };
          req.onerror = () => resolve(null);
        }),
    )
    .catch(() => null);
}

function put<T>(store: string, key: string, version: string, data: T): Promise<void> {
  return openDb()
    .then(
      (db) =>
        new Promise<void>((resolve) => {
          const tx = db.transaction(store, "readwrite");
          tx.objectStore(store).put({ v: version, d: data } as Entry<T>, key);
          tx.oncomplete = () => resolve();
          tx.onerror = () => resolve();
        }),
    )
    .catch(() => {});
}

/** One renderable part of a family mesh: all triangles sharing a material (approx colour + opacity). */
export interface FamilyMeshPart {
  color: number[]; // [r, g, b] 0-255
  opacity: number; // 0..1
  positions: number[];
  indices: number[];
}

export interface FamilyMeshData {
  available: boolean;
  reason?: string | null;
  parts?: FamilyMeshPart[];
}

export const getCachedPreview = (key: string, version: string) =>
  get<string>(PREVIEW_STORE, key, version);
export const setCachedPreview = (key: string, version: string, dataUri: string) =>
  put(PREVIEW_STORE, key, version, dataUri);
export const getCachedMesh = (key: string, version: string) =>
  get<FamilyMeshData>(MESH_STORE, key, version);
export const setCachedMesh = (key: string, version: string, mesh: FamilyMeshData) =>
  put(MESH_STORE, key, version, mesh);

/** Clears both caches — wire to a "rebuild previews" action when families are edited mid-session. */
export async function clearFamilyCache(): Promise<void> {
  try {
    const db = await openDb();
    await Promise.all(
      [PREVIEW_STORE, MESH_STORE].map(
        (store) =>
          new Promise<void>((resolve) => {
            const tx = db.transaction(store, "readwrite");
            tx.objectStore(store).clear();
            tx.oncomplete = () => resolve();
            tx.onerror = () => resolve();
          }),
      ),
    );
  } catch {
    /* best-effort */
  }
}
