// Single-file build that works when the .html is opened straight off disk (file://) — the form you
// can attach to a message and have someone just double-click.
//
// Two things make that possible, and both are non-obvious:
//
//  1. The worker is re-bundled as a CLASSIC script and started with `classicWorker: true`. Their
//     shipped worker is an ES module, and a MODULE worker cannot be created from a blob URL on a
//     file:// page, while a classic one can. The library supports this explicitly.
//
//  2. Nothing is inlined as raw source. Both bundles are embedded base64 and decoded at runtime:
//     minified JS is full of sequences that terminate a <script> tag or otherwise need escaping,
//     and getting that exactly right is a losing game. Base64 has no such characters at all.
//
//   node build-file.mjs [path-to.frag] [output.html]
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

const fragPath = process.argv[2] ?? "../out/demo.frag";
const outPath = process.argv[3] ?? "../out/viewer-file.html";

const fragmentsVersion = JSON.parse(
  readFileSync("node_modules/@thatopen/fragments/package.json", "utf8")).version;

const app = await build({
  entryPoints: ["main.js"], bundle: true, format: "iife", minify: true,
  write: false, target: ["es2020"], legalComments: "none",
});

// Bundling their worker.mjs as an IIFE strips the module syntax, which is what lets it run as a
// classic worker.
const worker = await build({
  entryPoints: ["node_modules/@thatopen/fragments/dist/Worker/worker.mjs"],
  bundle: true, format: "iife", minify: true, write: false,
  target: ["es2020"], legalComments: "none",
});

const b64 = (text) => Buffer.from(text, "utf8").toString("base64");
const frag = readFileSync(resolve(fragPath));

const html = readFileSync("shell.html", "utf8")
  .replace("__HEAD__", `<script>
window.__FRAG_META__ = ${JSON.stringify({ fragmentsVersion, writer: "AnalyseTool.Fragments (C#)" })};
window.__FRAG_DATA__ = "${frag.toString("base64")}";
window.__FRAG_WORKER_CLASSIC__ = true;
window.__B64_WORKER__ = "${b64(worker.outputFiles[0].text)}";
window.__B64_APP__ = "${b64(app.outputFiles[0].text)}";
</script>`)
  .replace("__BODY__", `<script>
(function () {
  var decode = function (base64) {
    var binary = atob(base64);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return new TextDecoder().decode(bytes);
  };
  window.__FRAG_WORKER__ = decode(window.__B64_WORKER__);
  new Function(decode(window.__B64_APP__))();
})();
</script>`);

mkdirSync(dirname(resolve(outPath)), { recursive: true });
writeFileSync(resolve(outPath), html);

console.log(`wrote ${outPath}`);
console.log(`  model   : ${frag.length.toLocaleString()} bytes`);
console.log(`  worker  : ${worker.outputFiles[0].text.length.toLocaleString()} bytes (classic)`);
console.log(`  app     : ${app.outputFiles[0].text.length.toLocaleString()} bytes`);
console.log(`  html    : ${html.length.toLocaleString()} bytes`);
