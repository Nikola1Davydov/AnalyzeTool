// Serves the split build over HTTP and runs the headless check against it.
//   node serve-check.mjs [outdir] [screenshot]
import { createServer } from "node:http";
import { readFileSync, existsSync } from "node:fs";
import { join, resolve, extname } from "node:path";
import { spawn } from "node:child_process";

const dir = resolve(process.argv[2] ?? "../out/serve");
const shot = resolve(process.argv[3] ?? "../out/viewer.png");
const types = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript",
                ".frag": "application/octet-stream", ".map": "application/json" };

const server = createServer((req, res) => {
  const path = join(dir, decodeURIComponent(req.url.split("?")[0]).replace(/^\/+/, "") || "index.html");
  if (!existsSync(path)) { res.writeHead(404); res.end("not found"); return; }
  res.writeHead(200, { "content-type": types[extname(path)] ?? "application/octet-stream" });
  res.end(readFileSync(path));
});

await new Promise((r) => server.listen(0, "127.0.0.1", r));
const url = `http://127.0.0.1:${server.address().port}/index.html`;
console.log(`serving ${dir} at ${url}\n`);

// spawn, not spawnSync: the static server lives in THIS process, and a synchronous child would
// block the event loop so no request ever gets answered.
const child = spawn("node", ["check.mjs", url, shot], { stdio: "inherit", env: process.env });
const status = await new Promise((r) => child.on("exit", r));
server.close();
process.exit(status ?? 1);
