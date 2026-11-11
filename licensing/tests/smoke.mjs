import assert from "node:assert/strict";

const stage = process.argv[2] === "staging";
assert.ok(stage || process.argv[2] === "production", "Pass staging or production");
const requireAccess = process.argv.includes("--require-access");
const requireAuth = requireAccess || process.argv.includes("--require-auth");
const suffix = (stage ? "-staging" : "") + ".ahmed-mabrouk-dev.workers.dev";
const client = "https://sv-client-api-v2" + suffix;
const admin = "https://sv-admin-v2" + suffix;
const options = { redirect: "manual", signal: AbortSignal.timeout(8000) };
if (!process.argv.includes("--admin-only")) {
  const health = await fetch(client + "/v2/client/health", options);
  assert.equal(health.status, 200);
  assert.deepEqual(await health.json(), { ok: true });
  const version = await fetch(client + "/v2/client/version", options);
  assert.equal(version.status, 200);
  assert.equal((await version.json()).channel, stage ? "test" : "stable");
  assert.equal((await fetch(client + "/v2/admin/overview", options)).status, 404);
  console.log("Client health, version and API isolation passed.");
}

const root = await fetch(admin + "/", options);
if (!requireAccess && root.status === 302 && root.headers.get("location") === "/auth/login") {
  const login = await fetch(admin + "/auth/login", options);
  assert.equal(login.status, 303);
  const target = new URL(login.headers.get("location"));
  assert.equal(target.origin + target.pathname, "https://github.com/login/oauth/authorize");
  assert.equal(target.searchParams.get("redirect_uri"), admin + "/auth/callback");
  assert.equal(target.searchParams.get("code_challenge_method"), "S256");
  assert.equal((await fetch(admin + "/v2/admin/overview", options)).status, 401);
  console.log("GitHub OAuth protects the dashboard and the anonymous API request was denied.");
} else for (const [route, response] of [["/", root], ["/v2/admin/overview", await fetch(admin + "/v2/admin/overview", options)]]) {
  const location = response.headers.get("location");
  const access = response.status === 302 && location && new URL(location).hostname.endsWith(".cloudflareaccess.com");
  if (!access) {
    assert.ok(!requireAuth, "An admin login redirect was not observed on " + route);
    assert.equal(response.status, 401);
    assert.deepEqual(await response.json(), { error: "unauthorized" });
  }
  console.log(route + (access ? " Cloudflare Access enforced" : " Worker authentication enforced"));
}
