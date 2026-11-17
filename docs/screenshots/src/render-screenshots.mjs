import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.resolve(__dirname, "..");

const scenes = [
  { html: "01-home.html", png: "01-home.png" },
  { html: "02-apps-page.html", png: "02-apps-page.png" },
  { html: "03-home-all-off.html", png: "03-home-all-off.png" },
  { html: "04-settings.html", png: "04-settings.png" },
  { html: "05-app-calc14.html", png: "05-app-calc14.png" },
  { html: "06-app-aetherpath-settings.html", png: "06-app-aetherpath-settings.png" },
];

await mkdir(outDir, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 720 });

for (const { html, png } of scenes) {
  await page.goto(pathToFileURL(path.join(__dirname, html)).href, { waitUntil: "load" });
  await page.waitForTimeout(150);
  await page.screenshot({ path: path.join(outDir, png), type: "png" });
  console.log("Wrote", png);
}

await browser.close();
