// Bundles the viewer into ONE self-contained .html: script, That Open's worker and the .frag
// payload are all inlined, so the page renders with no network access at all.
//
//   node build.mjs [path-to.frag] [output.html]
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

const fragPath = process.argv[2] ?? "../out/demo.frag";
const outPath = process.argv[3] ?? "../out/viewer.html";

const fragmentsVersion = JSON.parse(
  readFileSync("node_modules/@thatopen/fragments/package.json", "utf8")).version;

const bundle = await build({
  entryPoints: ["main.js"],
  bundle: true,
  format: "iife",
  minify: true,
  write: false,
  target: ["es2020"],
  legalComments: "none",
});
const script = bundle.outputFiles[0].text;

// Their runtime does the real work inside this worker — inline it rather than fetching it.
const worker = readFileSync("node_modules/@thatopen/fragments/dist/Worker/worker.min.mjs", "utf8")
  .replace(/\/\/# sourceMappingURL=.*$/m, "");
const frag = readFileSync(resolve(fragPath));

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>Revit → .frag — does it actually load?</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body { margin: 0; font: 14px/1.5 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif;
         background: #0b0e14; color: #d7dce5; display: grid; grid-template-columns: 1fr 380px; height: 100vh; }
  #scene { position: relative; }
  aside { border-left: 1px solid #1d2431; padding: 20px; overflow-y: auto; background: #0e1219; }
  h1 { font-size: 16px; margin: 0 0 4px; color: #fff; }
  p.sub { margin: 0 0 18px; color: #8b95a7; font-size: 13px; }
  #verdict { font-weight: 700; padding: 10px 12px; border-radius: 8px; margin-bottom: 18px; font-size: 13px; }
  #verdict.pass { background: #10331f; color: #6ee7a0; border: 1px solid #1d5c37; }
  #verdict.fail { background: #351417; color: #f2969b; border: 1px solid #6b2429; }
  .fact { display: flex; justify-content: space-between; gap: 12px; padding: 6px 0;
          border-bottom: 1px solid #171d29; font-size: 13px; }
  .fact span { color: #8b95a7; }
  .fact b { color: #e8edf5; font-weight: 600; text-align: right; word-break: break-word; }
  h2 { font-size: 12px; text-transform: uppercase; letter-spacing: .08em; color: #7c869a;
       margin: 22px 0 8px; }
  .line { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px;
          padding: 3px 0; color: #9aa4b6; }
  .line.ok { color: #6ee7a0; }
  .line.bad { color: #f2969b; }
  footer { margin-top: 22px; font-size: 12px; color: #6b7688; border-top: 1px solid #171d29; padding-top: 12px; }
  @media (max-width: 900px) { body { grid-template-columns: 1fr; grid-template-rows: 60vh 1fr; } }
</style>
</head>
<body>
<div id="scene"></div>
<aside>
  <h1>Revit → .frag writer</h1>
  <p class="sub">A file written by our C#, loaded by That Open's own runtime.</p>
  <div id="verdict">running…</div>
  <div id="facts"></div>
  <h2>Log</h2>
  <div id="log"></div>
  <footer>
    Drag to orbit. Everything on this page is inlined — the model, the viewer and its worker.
    Numbers are read back out of the loaded model by the library, not by the writer that produced it.
  </footer>
</aside>
<script>
window.__FRAG_META__ = ${JSON.stringify({
  fragmentsVersion,
  writer: "AnalyseTool.Fragments (C#)",
})};
window.__FRAG_DATA__ = "${frag.toString("base64")}";
window.__FRAG_WORKER__ = ${JSON.stringify(worker)};
</script>
<script>${script}</script>
</body>
</html>
`;

mkdirSync(dirname(resolve(outPath)), { recursive: true });
writeFileSync(resolve(outPath), html);
console.log(`wrote ${outPath}`);
console.log(`  frag payload : ${frag.length.toLocaleString()} bytes`);
console.log(`  worker       : ${worker.length.toLocaleString()} bytes`);
console.log(`  script       : ${script.length.toLocaleString()} bytes`);
console.log(`  html total   : ${html.length.toLocaleString()} bytes`);
