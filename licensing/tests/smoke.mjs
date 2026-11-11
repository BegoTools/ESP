import assert from "node:assert/strict";

const stage = process.argv[2] === "staging";
assert.ok(stage || process.argv[2] === "production", "Pass staging or production");
const requireAccess = process.argv.includes("--require-access");
const suffix = (stage ? "-staging" : "") + ".ahmed-mabrouk-dev.workers.dev";
const client = "https://sv-client-api-v2" + suffix;
const admin = "https://sv-admin-v2" + suffix;
const options = { redirect: "manual", signal: AbortSignal.timeout(8000) };
const health = await fetch(client + "/v2/client/health", options);
assert.equal(health.status, 200);
assert.deepEqual(await health.json(), { ok: true });
const version = await fetch(client + "/v2/client/version", options);
assert.equal(version.status, 200);
assert.equal((await version.json()).channel, stage ? "test" : "stable");
assert.equal((await fetch(client + "/v2/admin/overview", options)).status, 404);

for (const route of ["/", "/v2/admin/overview"]) {
  const response = await fetch(admin + route, options);
  const location = response.headers.get("location");
  const access = response.status === 302 && location && new URL(location).hostname.endsWith(".cloudflareaccess.com");
  if (!access) {
    assert.ok(!requireAccess, "Cloudflare Access login redirect was not observed on " + route);
    assert.equal(response.status, 401);
    assert.deepEqual(await response.json(), { error: "unauthorized" });
  }
  console.log(route + (access ? " Cloudflare Access enforced" : " Worker authentication enforced"));
}
console.log("Client health, version and API isolation passed.");
