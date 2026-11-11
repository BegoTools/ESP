import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { after, before, test } from "node:test";
import { exportJWK, generateKeyPair, SignJWT } from "jose";
import { Miniflare } from "miniflare";
import admin from "../admin-api/worker.mjs";
import client from "../client-api/worker.mjs";
import legacy from "../worker.mjs";
import { digest } from "../shared/http.mjs";

const origin = "https://stage.example";
const issuer = "https://spectravision-test.cloudflareaccess.com";
const owner = "owner@example.test";
const support = "support@example.test";
const version = "2.0.0";
const device = n => n.toString(16).padStart(64, "0");
const allow = { limit: async () => ({ success: true }) };
let env, runtime, keys, jwt, originalFetch, migrated, v1;

async function apply(file) {
  const sql = await readFile(new URL("../migrations/" + file, import.meta.url), "utf8");
  await env.DB.batch(sql.split(";").map(statement => statement.trim()).filter(Boolean).map(statement => env.DB.prepare(statement)));
}

async function call(worker, path, body, options = {}) {
  const headers = new Headers(options.headers);
  if (worker === admin) {
    headers.set("Origin", options.origin ?? origin);
    if (options.token !== null) headers.set("Cf-Access-Jwt-Assertion", options.token ?? jwt);
  }
  if (body !== undefined) headers.set("Content-Type", "application/json");
  const response = await worker.fetch(new Request(origin + path, {
    method: options.method ?? (body === undefined ? "GET" : "POST"), headers,
    body: body === undefined ? undefined : JSON.stringify(body)
  }), options.env ?? env);
  return { status: response.status, value: await response.json() };
}

const manage = (path, body, options) => call(admin, "/v2/admin/" + path, body, options);
const use = (path, body, options) => call(client, "/v2/client/" + path, body, options);
const newLicense = async (overrides = {}) => {
  const result = await manage("licenses", { label: "Synthetic license", limit: 1, days: 30, ...overrides });
  assert.equal(result.status, 201, JSON.stringify(result));
  return result.value;
};
const activate = (code, fingerprint, options) => use("activate", { code, device: fingerprint, version }, options);
const heartbeat = (token, fingerprint, options) => use("heartbeat", { token, device: fingerprint, version }, options);

async function sign(email, options = {}) {
  return new SignJWT({ email, type: "app" }).setProtectedHeader({ alg: "RS256", kid: "test" })
    .setIssuer(options.issuer ?? issuer).setAudience(options.aud ?? "stage-audience").setSubject(email)
    .setIssuedAt().setExpirationTime(options.exp ?? "5m").sign(options.key ?? keys.privateKey);
}

before(async () => {
  runtime = new Miniflare({
    compatibilityDate: "2026-04-21", modules: true,
    script: "export default { fetch() { return new Response('ok') } }",
    d1Databases: ["DB"]
  });
  env = {
    DB: await runtime.getD1Database("DB"), RELEASE_CHANNEL: "test", ACTIVATION_LIMIT: allow, SESSION_LIMIT: allow,
    ACCESS_ISSUER: issuer, ACCESS_AUD: "stage-audience", ADMIN_ROLES: JSON.stringify({ [owner]: "owner", [support]: "support" }),
    ASSETS: { fetch: async () => new Response("private dashboard") }
  };
  keys = await generateKeyPair("RS256", { extractable: true });
  const jwk = await exportJWK(keys.publicKey);
  originalFetch = globalThis.fetch;
  globalThis.fetch = (url, options) => String(url) === issuer + "/cdn-cgi/access/certs"
    ? Promise.resolve(Response.json({ keys: [{ ...jwk, kid: "test", alg: "RS256", use: "sig" }] }))
    : originalFetch(url, options);
  jwt = await sign(owner);
  await apply("0001_licenses.sql");
  await apply("0002_admin_accounts.sql");
  const code = "SV-ABABABAB-ABABABAB-ABABABAB-ABABABAB";
  const codeHash = await digest(code.replace(/-/g, "").toLowerCase());
  v1 = { id: "a".repeat(24), code, device: device(1), token: "b".repeat(64) };
  await env.DB.prepare("INSERT INTO licenses (id,code_hash,label,device_limit,created_at,duration_seconds) VALUES (?,?,?,?,?,?)")
    .bind(v1.id, codeHash, "Legacy", 1, 1, 86400).run();
  assert.equal((await call(legacy, "/api/activate", v1)).status, 200);
  const before = await env.DB.prepare("SELECT * FROM devices").first();
  await apply("0003_cloud_control.sql");
  const after = await env.DB.prepare("SELECT * FROM devices").first();
  assert.deepEqual(after, { ...before, last_seen: null });
  assert.equal((await env.DB.prepare("SELECT code_hash FROM licenses WHERE id = ?").bind(v1.id).first()).code_hash, codeHash);
});

after(async () => {
  if (originalFetch) globalThis.fetch = originalFetch;
  await runtime?.dispose();
});

test("Access covers HTML and API, validates JWT and enforces roles and CSRF", async () => {
  for (const token of [null, "forged", await sign(owner, { aud: "other" }), await sign(owner, { issuer: issuer + "/wrong" }),
    await sign(owner, { exp: 1 }), await sign(owner, { key: (await generateKeyPair("RS256")).privateKey })]) {
    assert.equal((await manage("overview", undefined, { token })).status, 401);
    assert.equal((await call(admin, "/", undefined, { token })).status, 401);
  }
  assert.equal((await manage("overview", undefined, { token: await sign("other@example.test") })).status, 403);
  const token = await sign(support);
  assert.equal((await manage("overview", undefined, { token })).value.actor.role, "support");
  assert.equal((await manage("licenses", { label: "denied", limit: 1, days: 1 }, { token })).status, 403);
  assert.equal((await manage("audit", undefined, { token })).status, 403);
  assert.equal((await manage("licenses", {}, { origin: "https://attacker.example" })).value.error, "bad_origin");
  const html = await admin.fetch(new Request(origin, { headers: { "Cf-Access-Jwt-Assertion": jwt } }), env);
  assert.equal(await html.text(), "private dashboard");
  assert.match(html.headers.get("content-security-policy"), /frame-ancestors 'none'/);
  assert.equal((await call(client, "/v2/admin/overview")).status, 404);
  assert.equal((await use("health")).status, 200);
  assert.equal((await use("version")).value.channel, "test");
});

test("GitHub login exchanges a state-bound PKCE code and authorizes only configured account IDs", async t => {
  const github = {
    ...env, ADMIN_AUTH_PROVIDER: "github", GITHUB_ADMIN_ORIGIN: origin,
    GITHUB_ADMIN_CLIENT_ID: "test-client", GITHUB_ADMIN_CLIENT_SECRET: "test-secret",
    GITHUB_ADMIN_SESSION_KEY: "ae".repeat(32), GITHUB_ADMIN_ROLES: '{"123":"owner"}'
  };
  const request = (path, headers, method) => new Request(origin + path, { headers, method });
  const fetchPage = (path, headers, method, config = github) => admin.fetch(request(path, headers, method), config);
  const overview = "/v2/admin/overview";
  const anonymous = await fetchPage("/");
  assert.equal(anonymous.status, 302);
  assert.equal(anonymous.headers.get("Location"), "/auth/login");
  assert.equal((await fetchPage(overview, { "Cf-Access-Jwt-Assertion": jwt })).status, 401);

  const login = await fetchPage("/auth/login");
  assert.equal(login.status, 303);
  const authorize = new URL(login.headers.get("Location"));
  assert.equal(authorize.origin, "https://github.com");
  assert.equal(authorize.searchParams.has("scope"), false);
  assert.equal(authorize.searchParams.get("code_challenge_method"), "S256");
  const loginCookie = login.headers.getSetCookie()[0].split(";")[0];
  assert.match(login.headers.getSetCookie()[0], /Secure; HttpOnly; SameSite=Lax/);
  const state = authorize.searchParams.get("state");
  const callback = "/auth/callback?code=example&state=";
  assert.equal((await fetchPage(callback + "wrong", { Cookie: loginCookie })).status, 401);
  assert.equal((await fetchPage(callback + state)).status, 401);

  let userId = 456;
  t.mock.method(globalThis, "fetch", async (target, options) => {
    assert.equal(options.redirect, "manual");
    if (target === "https://github.com/login/oauth/access_token") {
      assert.equal(options.body.get("code"), "example");
      assert.equal(options.body.get("redirect_uri"), origin + "/auth/callback");
      const data = new TextEncoder().encode(options.body.get("code_verifier"));
      const challenge = Buffer.from(await crypto.subtle.digest("SHA-256", data)).toString("base64url");
      assert.equal(challenge, authorize.searchParams.get("code_challenge"));
      return Response.json({ access_token: "synthetic-test-token" });
    }
    assert.equal(target, "https://api.github.com/user");
    assert.equal(options.headers.Authorization, "Bearer synthetic-test-token");
    return Response.json({ id: userId, login: "test-user" });
  });
  assert.equal((await fetchPage(callback + state, { Cookie: loginCookie })).status, 403);
  userId = 123;
  const accepted = await fetchPage(callback + state, { Cookie: loginCookie });
  assert.equal(accepted.status, 303);
  const session = accepted.headers.getSetCookie().find(value => value.startsWith("__Host-sv-admin-session="));
  assert.match(session, /Secure; HttpOnly; SameSite=Lax/);
  assert.doesNotMatch(session, /synthetic-test-token/);
  const headers = { Cookie: session.split(";")[0] };
  assert.equal(await (await fetchPage("/", headers)).text(), "private dashboard");
  assert.deepEqual((await (await fetchPage(overview, headers)).json()).actor, { id: "123", login: "test-user", role: "owner" });
  assert.equal((await fetchPage(overview, { Cookie: headers.Cookie + "tamper" })).status, 401);
  assert.equal((await fetchPage(overview, headers, "GET", { ...github, GITHUB_ADMIN_ROLES: "{}" })).status, 403);
  assert.equal((await fetchPage("/auth/logout", headers, "POST")).status, 403);
  const logout = await fetchPage("/auth/logout", { ...headers, Origin: origin }, "POST");
  assert.equal(logout.status, 303);
  assert.match(logout.headers.getSetCookie()[0], /__Host-sv-admin-session=; Max-Age=0/);
});

test("Existing device upgrades in place and v1 verification keeps working", async () => {
  migrated = (await use("activate", { legacyToken: v1.token, device: v1.device, version })).value;
  assert.equal(migrated.authorized, true);
  assert.match(migrated.token, /^[a-f0-9]{64}$/);
  assert.equal((await heartbeat(migrated.token, v1.device)).value.authorized, true);
  assert.equal((await call(legacy, "/api/verify", v1)).status, 200);
  assert.equal((await env.DB.prepare("SELECT COUNT(*) AS total FROM devices WHERE license_id = ?").bind(v1.id).first()).total, 1);
});

test("Activation, first-use expiry, competing seats, restart and token rotation", async () => {
  const { id, code } = await newLicense();
  assert.equal((await manage("licenses/" + id)).value.expires_at, null);
  assert.equal((await activate("bad-code", device(4))).value.error, "invalid_code");
  const attempts = await Promise.all([activate(code, device(2)), activate(code, device(3))]);
  assert.deepEqual(attempts.map(attempt => attempt.status).sort(), [200, 403]);
  const index = attempts.findIndex(attempt => attempt.status === 200);
  const selected = device(index + 2);
  const initial = attempts[index].value;
  assert.equal(attempts[1 - index].value.error, "device_limit");
  assert.equal(initial.expiresAt - initial.serverTime, 30 * 86400);
  assert.equal(initial.leaseExpiresAt - initial.serverTime, 128);
  assert.equal((await heartbeat(initial.token, selected)).value.authorized, true);
  const rotation = (await activate(code, selected)).value;
  assert.notEqual(rotation.token, initial.token);
  assert.equal((await heartbeat(initial.token, selected)).value.error, "device_removed");
  assert.equal((await heartbeat(rotation.token, device(999))).value.error, "device_removed");
  for (let n = 0; n < 2; n++)
    assert.equal((await use("deactivate", { token: rotation.token, device: selected, version })).status, 200);
  assert.equal((await heartbeat(rotation.token, selected)).value.error, "device_removed");
  assert.equal((await manage("licenses/" + id)).value.used, 1);
  assert.equal((await manage("licenses/" + id + "/devices/" + selected)).status, 200);
  assert.equal((await manage("licenses/" + id + "/devices/" + selected, undefined, { method: "DELETE" })).status, 200);
  assert.equal((await activate(code, device(9))).status, 200);
  const audit = (await manage("audit?search=" + id)).value.items;
  assert.deepEqual(audit.map(row => row.action).sort(), ["create", "unlink"]);
  assert.doesNotMatch(JSON.stringify(audit), /SV-|token_hash|code_hash/);
});

test("Admin changes affect later verification and all successful writes are audited", async () => {
  const plan = "synthetic";
  assert.equal((await manage("plans", { id: plan, name: "Test plan", status: "active", maxDevices: 2 }, { method: "PUT" })).status, 200);
  const { id, code } = await newLicense({ limit: 4, plan });
  const first = (await activate(code, device(4))).value;
  assert.equal((await activate(code, device(5))).status, 200);
  assert.equal((await activate(code, device(6))).value.error, "device_limit");
  assert.equal((await manage("plans", { id: plan, name: "Test plan", status: "active", maxDevices: 1 }, { method: "PUT" })).status, 409);
  assert.equal((await manage("features", { key: "live", scope: "plan_default", id: plan, enabled: false }, { method: "PUT" })).status, 200);
  assert.equal((await heartbeat(first.token, device(4))).value.permissions.live, false);
  assert.equal((await manage("features?search=plan_default")).value.items[0].scope_id, plan);
  assert.equal((await manage("features", { key: "live", scope: "license", id, enabled: true }, { method: "PUT" })).status, 200);
  assert.equal((await heartbeat(first.token, device(4))).value.permissions.live, true);
  assert.equal((await manage("licenses/" + id + "/revoke", {})).status, 200);
  assert.equal((await heartbeat(first.token, device(4))).value.error, "revoked");
  assert.equal((await manage("licenses/" + id + "/revoke", { revoked: false })).status, 200);
  await env.DB.prepare("UPDATE licenses SET expires_at = 1 WHERE id = ?").bind(id).run();
  assert.equal((await heartbeat(first.token, device(4))).value.error, "expired");
  assert.equal((await manage("licenses/" + id + "/extend", { days: 3 })).status, 200);
  assert.equal((await heartbeat(first.token, device(4))).value.authorized, true);
  assert.equal((await manage("licenses/" + id, { plan: null, limit: 2, label: "<script>test</script>" }, { method: "PATCH" })).status, 200);
  assert.equal((await manage("licenses?sort=label&order=asc&limit=1")).value.items.length, 1);
  const audit = (await manage("audit?search=" + id)).value.items;
  assert.deepEqual(audit.map(row => row.action).sort(), ["create", "edit", "extend", "restore", "revoke"]);
  assert.equal((await manage("audit?search=" + plan)).value.items.filter(row => row.entity_type === "plan").length, 1);
});

test("Version floor, maintenance, size bounds, rate limiting and service failures deny access", async () => {
  const checksum = "a".repeat(64);
  const manifestUrl = "https://github.com/BegoTools/ESP/releases/download/sv-test-v2.0.0/latest.json";
  assert.equal((await manage("releases", { channel: "test", version, checksum, manifestUrl })).status, 201);
  assert.equal((await manage("system", { channel: "test", minSupportedVersion: version, currentVersion: version, maintenance: false }, { method: "PUT" })).status, 200);
  assert.equal((await use("version")).value.manifestUrl, manifestUrl);
  assert.equal((await use("heartbeat", { device: v1.device, token: migrated.token, version: "1.9.9" })).value.error, "update_required");
  assert.equal((await manage("system", { maintenance: true }, { method: "PUT" })).status, 200);
  assert.equal((await heartbeat(migrated.token, v1.device)).value.error, "maintenance");
  assert.equal((await manage("system", { maintenance: false }, { method: "PUT" })).status, 200);
  assert.equal((await heartbeat(migrated.token, v1.device, { env: { ...env, SESSION_LIMIT: { limit: async () => ({ success: false }) } } })).status, 429);
  assert.equal((await heartbeat(migrated.token, v1.device, { env: { ...env, DB: { prepare: () => { throw new Error("D1 unavailable"); } } } })).status, 503);
  assert.equal((await use("activate", { code: "a".repeat(4100), device: v1.device, version })).status, 413);
  assert.equal((await manage("licenses?sort=label%3Bdrop%20table%20licenses")).status, 400);
});
