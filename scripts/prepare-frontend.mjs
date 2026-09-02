// Build the Tauri frontend by publishing the self-contained designv2 island UI
// (designv2.html) as the app's index page.
//
// designv2.html is a single-file page — all CSS/JS is inline and it references
// no local assets — so we skip the React/Vite bundler entirely and just copy it
// into dist/. The transparent, always-on-top Tauri window then loads it
// directly (the window's default url is "index.html").
import { cpSync, mkdirSync, rmSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const dist = join(root, "dist");

rmSync(dist, { recursive: true, force: true });
mkdirSync(dist, { recursive: true });

// index.html is what the Tauri window loads by default; designv2.html keeps a
// stable, directly-addressable copy too.
cpSync(join(root, "designv2.html"), join(dist, "index.html"));
cpSync(join(root, "designv2.html"), join(dist, "designv2.html"));

console.log("Prepared frontend: dist/index.html <- designv2.html");
