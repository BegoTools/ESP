import assert from "node:assert/strict";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { setTimeout } from "node:timers/promises";
import { digest, random } from "../shared/http.mjs";

const origin = "https://sv-client-api-v2-staging.ahmed-mabrouk-dev.workers.dev";
const database = "spectravision-licenses-staging";
const dir = await mkdtemp(join(tmpdir(), "sv-staging-load-"));
const config = fileURLToPath(new URL("../wrangler.staging.client.jsonc", import.meta.url));
const wrangler = fileURLToPath(new URL("../node_modules/wrangler/bin/wrangler.js", import.meta.url));
const id = random(12);
const seatId = random(12);
const now = Math.floor(Date.now() / 1000);

function sql(...args) {
  const output = execFileSync(process.execPath, [wrangler, "d1", "execute", database, "--remote", "--json",
    "--config", config, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  if (args[0] === "--file") return;
  const results = JSON.parse(output);
  assert.ok(results.every(result => result.success));
  return results;
}

try {
  const code = "SV-" + random(16);
  const codeHash = await digest(code.replace("-", "").toLowerCase());
  sql("--command", `INSERT INTO licenses (id, code_hash, label, device_limit, created_at, duration_seconds)
    VALUES ('${seatId}', '${codeHash}', 'Synthetic seat race', 1, ${now}, 300);`);
  const competing = await Promise.all([random(32), random(32)].map(device =>
    fetch(origin + "/v2/client/activate", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code, device, version: "2.0.0" }), signal: AbortSignal.timeout(8000)
    })));
  assert.deepEqual(competing.map(response => response.status).sort(), [200, 403]);
  assert.equal((await competing.find(response => response.status === 403).json()).error, "device_limit");
  console.log("Simultaneous activation allocated exactly one seat.");
  const statements = [`INSERT INTO licenses (id,code_hash,label,device_limit,created_at,expires_at)
    VALUES ('${id}','${await digest(random(16))}','Synthetic load test',50,${now},${now + 300});`];
  const devices = await Promise.all(Array.from({ length: 50 }, async () => {
    const device = random(32);
    const token = random(32);
    statements.push(`INSERT INTO devices(license_id,device,token_hash,activated_at,last_seen)
      VALUES('${id}','${device}','${await digest(random(32))}',${now},${now - 601});`);
    statements.push(`INSERT INTO license_sessions(id,license_id,device_id,token_hash,created_at)
      VALUES('${random(12)}','${id}','${device}','${await digest(token)}',${now});`);
    return { device, token, version: "2.0.0" };
  }));
  const file = join(dir, "seed.sql");
  await writeFile(file, statements.join("\n"));
  sql("--file", file);
  for (const count of [5, 20, 50]) {
    const requests = devices.slice(0, count).map(async (body, index) => {
      await setTimeout(index * 350);
      const started = performance.now();
      const response = await fetch(origin + "/v2/client/heartbeat", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body), signal: AbortSignal.timeout(8000)
      });
      assert.equal(response.status, 200);
      const reply = await response.json();
      assert.equal(reply.authorized, true);
      assert.ok(reply.leaseExpiresAt > reply.serverTime && reply.leaseExpiresAt <= reply.serverTime + 128);
      return performance.now() - started;
    });
    const results = await Promise.allSettled(requests);
    const failed = results.filter(result => result.status === "rejected");
    assert.equal(failed.length, 0, failed.map(result => result.reason.message).join("\n"));
    const timings = results.map(result => result.value).sort((a, b) => a - b);
    console.log(JSON.stringify({ devices: count, requests: timings.length, p95ms: Math.round(timings[Math.ceil(count * .95) - 1]) }));
  }
  const [seen] = sql("--command", `SELECT COUNT(*) AS updated FROM devices WHERE license_id='${id}' AND last_seen >= ${now};`);
  assert.equal(seen.results[0].updated, 50);
  console.log("75 heartbeats, 50 initially stale devices updated, synthetic licenses and sessions removed on exit.");
} finally {
  try { sql("--command", `DELETE FROM licenses WHERE id IN ('${id}', '${seatId}');`); }
  finally { await rm(dir, { recursive: true, force: true }); }
}
