// Bundles the extension page into ../ui/, which the csproj copies into the extension folder.
//
// Kept out of the C# build on purpose: node is not a build dependency of the plugin, so the built
// output is committed nowhere and produced by hand — run this once, then dotnet build.
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync, copyFileSync } from "node:fs";
import { join } from "node:path";

const out = "../ui";
mkdirSync(out, { recursive: true });

await build({
  entryPoints: ["app.js"],
  bundle: true,
  format: "esm",
  minify: true,
  target: ["es2020"],
  outfile: join(out, "app.js"),
  legalComments: "none",
});

// That Open's runtime works inside this worker. WebView2 serves the extension folder from a real
// https:// virtual host, so it loads as a plain module URL — no blob needed.
const worker = readFileSync("node_modules/@thatopen/fragments/dist/Worker/worker.min.mjs", "utf8")
  .replace(/\/\/# sourceMappingURL=.*$/m, "");
writeFileSync(join(out, "worker.mjs"), worker);

copyFileSync("index.html", join(out, "index.html"));

console.log(`built the extension page into ${out}`);
