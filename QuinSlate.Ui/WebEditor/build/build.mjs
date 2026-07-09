// Bundles src/main.js (CodeMirror 6 + the QuinSlate host bridge) into a single
// IIFE at ../editor.bundle.js. The output is committed to the repository so a
// normal QuinSlate build never runs npm. Rebuild only when the editor source or
// a pinned dependency changes:
//
//   npm ci        (restore the exact pinned versions from package-lock.json)
//   npm run build (regenerate ../editor.bundle.js)
//
import { build } from "esbuild";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));

await build({
  entryPoints: [resolve(here, "src/main.js")],
  outfile: resolve(here, "..", "editor.bundle.js"),
  bundle: true,
  format: "iife",
  target: "chrome110",
  legalComments: "none",
  minify: true,
  sourcemap: false,
});

console.log("Built editor.bundle.js");
