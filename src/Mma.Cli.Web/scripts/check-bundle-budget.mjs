import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { gzipSync } from "node:zlib";

const BUDGET_KB = 300;
const assetsDir = path.join(process.cwd(), "dist", "assets");

let files;
try {
  files = readdirSync(assetsDir).filter((file) => file.endsWith(".js"));
} catch {
  console.error(`[budget] dist/assets not found at ${assetsDir}`);
  process.exit(1);
}

const entry = files.find((file) => /^index-.*\.js$/.test(file));
if (!entry) {
  console.error(`[budget] entry chunk (index-*.js) not found among: ${files.join(", ")}`);
  process.exit(1);
}

const entryGzip = gzipSync(readFileSync(path.join(assetsDir, entry))).length;
const entryKb = entryGzip / 1024;

const rows = files
  .map((file) => {
    const bytes = readFileSync(path.join(assetsDir, file)).length;
    const gzip = gzipSync(readFileSync(path.join(assetsDir, file))).length;
    return { file, rawKb: bytes / 1024, gzipKb: gzip / 1024 };
  })
  .sort((a, b) => b.gzipKb - a.gzipKb);

for (const row of rows) {
  console.log(
    `[budget] ${row.file.padEnd(38)} raw ${row.rawKb.toFixed(1).padStart(8)} KB  gzip ${row.gzipKb
      .toFixed(1)
      .padStart(7)} KB`
  );
}

console.log(`[budget] entry ${entry}: ${entryKb.toFixed(1)} KB gzip (budget ${BUDGET_KB} KB)`);

if (entryKb > BUDGET_KB) {
  console.error(
    `[budget] FAIL: entry chunk exceeds the ${BUDGET_KB} KB gzip budget. Move heavy dependencies behind a lazy route.`
  );
  process.exit(1);
}

console.log("[budget] PASS");
