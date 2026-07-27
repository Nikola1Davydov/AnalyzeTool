// Verifies the extension page without Revit: serves the built extension folder over HTTP, stubs
// window.AT the way the host injects it, and drives the real button.
//
// It checks everything except the C# — that the bundle boots, the worker starts from the extension
// folder, the relative fetch of ../model.frag resolves, and the model renders.
//
//   node check.mjs [extensionDir] [screenshot]
import { chromium } from "playwright";
import { createServer } from "node:http";
import { readFileSync, existsSync, copyFileSync } from "node:fs";
import { join, resolve, extname } from "node:path";

const dir = resolve(process.argv[2] ?? "../ui-check");
const shot = resolve(process.argv[3] ?? "../ui-check.png");
const types = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript",
                ".json": "application/json", ".frag": "application/octet-stream" };

const server = createServer((req, res) => {
  const path = join(dir, decodeURIComponent(req.url.split("?")[0]).replace(/^\/+/, ""));
  if (!existsSync(path)) { res.writeHead(404); res.end("not found"); return; }
  res.writeHead(200, { "content-type": types[extname(path)] ?? "application/octet-stream" });
  res.end(readFileSync(path));
});
await new Promise((r) => server.listen(0, "127.0.0.1", r));
const port = server.address().port;

const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH || undefined,
  args: ["--use-gl=swiftshader", "--enable-unsafe-swiftshader", "--no-sandbox", "--no-proxy-server"],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });

const errors = [];
page.on("pageerror", (e) => errors.push(String(e)));
page.on("console", (m) => { if (m.type() === "error") errors.push(m.text()); });

// Stand in for the bridge the host injects with AddScriptToExecuteOnDocumentCreated. The numbers
// are the shape the real command returns; the file it claims to have written is already on disk.
await page.addInitScript(() => {
  window.AT = {
    invoke: async (command, payload) => {
      window.__INVOKED__ = { command, payload };
      return {
        path: "C:\\stub\\model.frag", scope: payload?.activeViewOnly ? "active view" : "whole model",
        items: 14, shells: 2, samples: 13, reuse: 6.5, triangles: 156, materials: 2,
        tessellateMs: 42, writeMs: 3, bytes: 1435,
      };
    },
  };
});

await page.goto(`http://127.0.0.1:${port}/ui/index.html`);
await page.click("#view");
await page.waitForFunction(
  () => { const s = document.getElementById("status"); return s.className === "ok" || s.className === "bad"; },
  { timeout: 60000 });
await page.waitForTimeout(2000);

const status = await page.textContent("#status");
const statusKind = await page.getAttribute("#status", "class");
const invoked = await page.evaluate(() => window.__INVOKED__);
const facts = await page.$$eval(".fact", (rows) =>
  rows.map((r) => [r.querySelector("span").textContent, r.querySelector("b").textContent]));
const drawn = await page.evaluate(() => {
  const canvas = document.querySelector("#scene canvas");
  if (!canvas) return { canvas: false };
  return { canvas: true, width: canvas.width, height: canvas.height };
});

await page.screenshot({ path: shot });
await browser.close();
server.close();

console.log(`command : ${invoked?.command}  ${JSON.stringify(invoked?.payload)}`);
console.log(`status  : [${statusKind}] ${status}`);
console.log("facts   :");
for (const [k, v] of facts) console.log(`  ${k.padEnd(20)} ${v}`);
console.log(`canvas  : ${drawn.canvas ? `${drawn.width}x${drawn.height}` : "MISSING"}`);
if (errors.length) {
  console.log("errors  :");
  for (const e of errors.slice(0, 8)) console.log(`  ${e}`);
}
console.log(`shot    : ${shot}`);

process.exit(statusKind === "ok" && drawn.canvas ? 0 : 1);
