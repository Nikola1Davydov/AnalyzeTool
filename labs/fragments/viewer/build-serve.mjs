// Same viewer, but emitted as separate files to be served over HTTP.
//
// The single-file build inlines That Open's worker as a blob URL, which a browser refuses to start
// as an ES module when the page itself came from file:// (origin null). Served over HTTP the worker
// is a plain module URL and everything behaves like a real deployment — which is also how the
// plugin will ship it, since WebView2 serves the client app from a virtual host.
//
//   node build-serve.mjs [path-to.frag] [outdir]
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { resolve, join } from "node:path";

const fragPath = process.argv[2] ?? "../out/demo.frag";
const outDir = resolve(process.argv[3] ?? "../out/serve");

const fragmentsVersion = JSON.parse(
  readFileSync("node_modules/@thatopen/fragments/package.json", "utf8")).version;

mkdirSync(outDir, { recursive: true });

await build({
  entryPoints: ["main.js"],
  bundle: true,
  format: "esm",
  minify: true,
  target: ["es2020"],
  outfile: join(outDir, "app.js"),
  legalComments: "none",
});

// Drop the sourceMappingURL: the .map is 10 MB and its absence is a 404 in the console.
const workerSource = readFileSync("node_modules/@thatopen/fragments/dist/Worker/worker.min.mjs", "utf8")
  .replace(/\/\/# sourceMappingURL=.*$/m, "");
writeFileSync(join(outDir, "worker.mjs"), workerSource);

const frag = readFileSync(resolve(fragPath));
writeFileSync(join(outDir, "model.frag"), frag);

const template = readFileSync("shell.html", "utf8");
writeFileSync(join(outDir, "index.html"), template
  .replace("__HEAD__", `<script>
window.__FRAG_META__ = ${JSON.stringify({ fragmentsVersion, writer: "AnalyseTool.Fragments (C#)" })};
window.__FRAG_WORKER_URL__ = "./worker.mjs";
window.__FRAG_URL__ = "./model.frag";
</script>`)
  .replace("__BODY__", `<script type="module" src="./app.js"></script>`));

console.log(`wrote ${outDir}`);
console.log(`  model.frag : ${frag.length.toLocaleString()} bytes`);
