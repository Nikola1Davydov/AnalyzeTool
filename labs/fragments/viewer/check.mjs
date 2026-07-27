// Headless verification of the bundled viewer page: loads it in Chromium, waits for the runtime to
// report a verdict, and prints what That Open's library read back out of our file. Exits non-zero if
// the page did not reach PASS, so this can gate the writer the same way the unit tests do.
//
//   node check.mjs [../out/viewer.html] [../out/viewer.png]
import { chromium } from "playwright";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const target = process.argv[2] ?? "../out/viewer.html";
const shot_path = resolve(process.argv[3] ?? "../out/viewer.png");

// Software GL: there is no GPU here, and three.js still needs a real WebGL context.
const browser = await chromium.launch({
  // Point at a preinstalled Chromium when the environment provides one whose build number does not
  // match this playwright package (CHROMIUM_PATH), otherwise use playwright's own.
  executablePath: process.env.CHROMIUM_PATH || undefined,
  // --no-proxy-server: everything here is local, and the sandbox's HTTPS_PROXY would
  // otherwise swallow requests to 127.0.0.1.
  args: ["--use-gl=swiftshader", "--enable-unsafe-swiftshader", "--disable-gpu-sandbox",
         "--no-sandbox", "--no-proxy-server"],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });

const consoleErrors = [];
page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });
page.on("pageerror", (e) => consoleErrors.push(String(e)));

await page.goto(target.startsWith("http") ? target : pathToFileURL(resolve(target)).href);

let verdict = "(timed out)";
try {
  await page.waitForFunction(
    () => {
      const el = document.getElementById("verdict");
      return el && el.textContent && el.textContent !== "running…";
    },
    { timeout: 60000 });
  verdict = await page.textContent("#verdict");
} catch {
  /* fall through to the log dump below */
}

// Let a couple of frames render before the screenshot.
await page.waitForTimeout(2500);

const facts = await page.$$eval(".fact", (rows) =>
  rows.map((r) => [r.querySelector("span").textContent, r.querySelector("b").textContent]));
const log = await page.$$eval(".line", (rows) =>
  rows.map((r) => `${r.classList.contains("ok") ? "ok  " : r.classList.contains("bad") ? "BAD " : "    "}${r.textContent}`));

// Did anything actually get drawn? Sample the canvas for non-background pixels.
const drawn = await page.evaluate(() => {
  const canvas = document.querySelector("#scene canvas");
  if (!canvas) return { canvas: false };
  const probe = document.createElement("canvas");
  probe.width = canvas.width;
  probe.height = canvas.height;
  probe.getContext("2d").drawImage(canvas, 0, 0);
  const { data } = probe.getContext("2d").getImageData(0, 0, probe.width, probe.height);
  let lit = 0;
  for (let i = 0; i < data.length; i += 4) {
    // Background is #10141c; anything meaningfully brighter is geometry or helpers.
    if (data[i] > 40 || data[i + 1] > 44 || data[i + 2] > 52) lit++;
  }
  return { canvas: true, width: probe.width, height: probe.height, lit, total: data.length / 4 };
});

await page.screenshot({ path: shot_path });
await browser.close();

console.log("VERDICT:", verdict.trim());
console.log("\nFACTS");
for (const [k, v] of facts) console.log(`  ${k.padEnd(20)} ${v}`);
console.log("\nLOG");
for (const line of log) console.log(`  ${line}`);
console.log("\nCANVAS");
if (!drawn.canvas) {
  console.log("  no canvas — WebGL never initialised");
} else {
  const pct = ((drawn.lit / drawn.total) * 100).toFixed(1);
  console.log(`  ${drawn.width}x${drawn.height}, ${drawn.lit.toLocaleString()} lit pixels (${pct}%)`);
}
if (consoleErrors.length) {
  console.log("\nCONSOLE ERRORS");
  for (const e of consoleErrors.slice(0, 10)) console.log(`  ${e}`);
}
console.log(`\nscreenshot: ${shot_path}`);

process.exit(verdict.startsWith("PASS") ? 0 : 1);
