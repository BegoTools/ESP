const DAY = 86400;
const encoder = new TextEncoder();
const hex = bytes => Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
const digest = async value => hex(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value))));
const random = size => hex(crypto.getRandomValues(new Uint8Array(size)));
const fingerprint = /^[a-f0-9]{64}$/;
const sessionCookie = "__Host-sv-admin";
const sessionSeconds = 8 * 60 * 60;

class InputError extends Error {
  constructor(status, code) { super(code); this.status = status; }
}

function json(body, status = 200, headers = {}) {
  return Response.json(body, { status, headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", ...headers } });
}

async function input(request) {
  if (!request.headers.get("Content-Type")?.startsWith("application/json")) throw new InputError(415, "bad_input");
  const reader = request.body?.getReader();
  if (!reader) throw new InputError(400, "bad_input");
  const decoder = new TextDecoder();
  let total = 0, text = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    total += value.length;
    if (total > 4096) { await reader.cancel(); throw new InputError(413, "bad_input"); }
    text += decoder.decode(value, { stream: true });
  }
  try {
    const value = JSON.parse(text + decoder.decode());
    if (value && typeof value === "object" && !Array.isArray(value)) return value;
  } catch { /* malformed JSON */ }
  throw new InputError(400, "bad_input");
}

function entitlement(row, now) {
  if (!row) throw new InputError(403, "invalid_code");
  if (row.revoked) throw new InputError(403, "revoked");
  if (row.expires_at && row.expires_at <= now) throw new InputError(403, "expired");
  if (!row.token_hash) throw new InputError(403, "device_limit");
  return json({
    expiresAt: row.expires_at,
    offlineUntil: Math.min(row.expires_at, now + DAY),
    serverTime: now
  });
}

async function activate(db, request, now) {
  const { code, device, token } = await input(request);
  if (typeof code !== "string" || typeof device !== "string" || typeof token !== "string" ||
      !fingerprint.test(device) || !fingerprint.test(token)) throw new InputError(400, "bad_input");
  const normalized = code.replace(/[\s-]/g, "").toLowerCase();
  if (!/^sv[a-f0-9]{32}$/.test(normalized)) throw new InputError(403, "invalid_code");
  const [codeHash, tokenHash] = await Promise.all([digest(normalized), digest(token)]);
  const results = await db.batch([
    db.prepare(`INSERT INTO devices (license_id, device, token_hash, activated_at)
      SELECT l.id, ?1, ?2, ?3 FROM licenses l
      WHERE l.code_hash = ?4 AND l.revoked = 0 AND (l.expires_at IS NULL OR l.expires_at > ?3)
        AND (EXISTS (SELECT 1 FROM devices WHERE license_id = l.id AND device = ?1)
          OR (SELECT COUNT(*) FROM devices WHERE license_id = l.id) < l.device_limit)
      ON CONFLICT(license_id, device) DO UPDATE SET token_hash = excluded.token_hash`)
      .bind(device, tokenHash, now, codeHash),
    db.prepare(`UPDATE licenses SET expires_at = ?1 + duration_seconds
      WHERE code_hash = ?2 AND expires_at IS NULL AND revoked = 0
        AND EXISTS (SELECT 1 FROM devices WHERE license_id = licenses.id AND device = ?3)`)
      .bind(now, codeHash, device),
    db.prepare(`SELECT l.expires_at, l.revoked, d.token_hash FROM licenses l
      LEFT JOIN devices d ON d.license_id = l.id AND d.device = ?1 WHERE l.code_hash = ?2`)
      .bind(device, codeHash)
  ]);
  return entitlement(results[2].results[0], now);
}

async function verify(db, request, now) {
  const { device, token } = await input(request);
  if (typeof device !== "string" || typeof token !== "string" ||
      !fingerprint.test(device) || !fingerprint.test(token)) throw new InputError(400, "bad_input");
  const row = await db.prepare(`SELECT l.expires_at, l.revoked, d.token_hash FROM devices d
    JOIN licenses l ON l.id = d.license_id WHERE d.token_hash = ? AND d.device = ?`).bind(await digest(token), device).first();
  if (!row) throw new InputError(403, "device_removed");
  return entitlement(row, now);
}

function matches(first, second) {
  return first.length === second.length && crypto.subtle.timingSafeEqual(encoder.encode(first), encoder.encode(second));
}

function requireAdminSecret(secret) {
  if (!secret || !fingerprint.test(secret)) throw new InputError(503, "admin_unavailable");
}

async function adminHash(secret, purpose, value) {
  requireAdminSecret(secret);
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return hex(new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(purpose + "\0" + value))));
}

function adminEmail(value) {
  const email = typeof value === "string" ? value.trim().toLowerCase() : "";
  if (email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) throw new InputError(400, "bad_input");
  return email;
}

function adminCookie(token, seconds) {
  return { "Set-Cookie": `${sessionCookie}=${token}; Path=/; Max-Age=${seconds}; HttpOnly; Secure; SameSite=Strict` };
}

async function sessionHash(request) {
  const match = /(?:^|;\s*)__Host-sv-admin=([a-f0-9]{64})(?:;|$)/.exec(request.headers.get("Cookie") || "");
  return match ? digest(match[1]) : null;
}

async function adminAuth(request, env, path, now) {
  const db = env.DB;
  const secret = env.ADMIN_TOKEN;
  if (path === "/api/admin/auth/status" && request.method === "GET") {
    const account = await db.prepare("SELECT email FROM admin_account WHERE id = 1").first();
    const tokenHash = await sessionHash(request);
    const session = account && tokenHash && await db.prepare(
      "SELECT 1 FROM admin_sessions WHERE token_hash = ? AND expires_at > ?"
    ).bind(tokenHash, now).first();
    return json({ needsSetup: !account, email: session ? account.email : null });
  }
  if (request.method !== "POST") throw new InputError(405, "method_not_allowed");
  if (path === "/api/admin/auth/logout") {
    const tokenHash = await sessionHash(request);
    if (tokenHash) await db.prepare("DELETE FROM admin_sessions WHERE token_hash = ?").bind(tokenHash).run();
    return json({ ok: true }, 200, adminCookie("", 0));
  }
  const payload = await input(request);
  const email = adminEmail(payload.email);
  if (path === "/api/admin/auth/salt") {
    // A deterministic, secret-derived salt avoids revealing which email is registered.
    return json({ salt: await adminHash(secret, "admin-password-salt-v1", email) });
  }
  if (path !== "/api/admin/auth/login" && path !== "/api/admin/auth/setup") throw new InputError(404, "not_found");
  const { success } = await env.ADMIN_LOGIN_LIMIT.limit({ key: request.headers.get("CF-Connecting-IP") || "local" });
  if (!success) throw new InputError(429, "try_later");
  if (typeof payload.verifier !== "string" || !fingerprint.test(payload.verifier)) throw new InputError(400, "bad_input");
  const passwordHash = await adminHash(secret, "admin-password-verifier-v1", email + "\0" + payload.verifier);
  if (path === "/api/admin/auth/setup") {
    if (typeof payload.setupKey !== "string" || !fingerprint.test(payload.setupKey) || !matches(secret, payload.setupKey))
      throw new InputError(401, "unauthorized");
    const created = await db.prepare("INSERT OR IGNORE INTO admin_account (id, email, password_hash) VALUES (1, ?, ?)")
      .bind(email, passwordHash).run();
    if (!created.meta.changes) throw new InputError(409, "already_configured");
  } else {
    const account = await db.prepare("SELECT email, password_hash FROM admin_account WHERE id = 1").first();
    if (!account || !matches(account.password_hash, passwordHash) || account.email !== email)
      throw new InputError(401, "unauthorized");
  }
  const token = random(32);
  await db.batch([
    db.prepare("DELETE FROM admin_sessions WHERE expires_at <= ?").bind(now),
    db.prepare("INSERT INTO admin_sessions (token_hash, expires_at) VALUES (?, ?)").bind(await digest(token), now + sessionSeconds)
  ]);
  return json({ email }, 200, adminCookie(token, sessionSeconds));
}

async function authorize(request, db, now) {
  const tokenHash = await sessionHash(request);
  if (!tokenHash || !await db.prepare("SELECT 1 FROM admin_sessions WHERE token_hash = ? AND expires_at > ?")
    .bind(tokenHash, now).first()) throw new InputError(401, "unauthorized");
}

function validDays(value) { return Number.isInteger(value) && value >= 1 && value <= 3650; }
function validLimit(value) { return Number.isInteger(value) && value >= 1 && value <= 100; }
function validLabel(value) { return typeof value === "string" && value.trim() && value.length <= 100; }
function validExpiry(value, now) { return Number.isSafeInteger(value) && value > now && value <= now + 3650 * DAY; }

const licenseColumns = `l.id, l.label, l.device_limit, l.created_at, l.expires_at, l.duration_seconds, l.revoked,
  (SELECT COUNT(*) FROM devices WHERE license_id = l.id) AS used`;

async function admin(request, env, path, now) {
  if (request.method !== "GET" && request.headers.get("Origin") !== new URL(request.url).origin)
    throw new InputError(403, "bad_origin");
  if (path.startsWith("/api/admin/auth/")) return adminAuth(request, env, path, now);
  const db = env.DB;
  await authorize(request, db, now);
  if (path === "/api/admin/summary" && request.method === "GET") {
    const [licenses, devices] = await db.batch([
      db.prepare(`SELECT COUNT(*) AS total,
        COUNT(CASE WHEN revoked = 0 AND (expires_at IS NULL OR expires_at > ?) THEN 1 END) AS available
        FROM licenses`).bind(now),
      db.prepare("SELECT COUNT(*) AS linked FROM devices")
    ]);
    return json({ ...licenses.results[0], ...devices.results[0] });
  }
  if (path === "/api/admin/devices" && request.method === "GET") {
    const params = new URL(request.url).searchParams;
    const search = (params.get("search") || "").trim().slice(0, 100);
    const offset = Number(params.get("offset") || 0);
    if (!Number.isSafeInteger(offset) || offset < 0) throw new InputError(400, "bad_input");
    const devices = await db.prepare(`SELECT d.device, d.activated_at, l.id AS license_id, l.label
      FROM devices d JOIN licenses l ON l.id = d.license_id
      WHERE instr(lower(l.label), lower(?1)) > 0 OR instr(d.device, lower(?1)) > 0
      ORDER BY d.activated_at DESC, d.license_id, d.device LIMIT 51 OFFSET ?2`).bind(search, offset).all();
    return json(devices.results);
  }
  if (path === "/api/admin/licenses" && request.method === "GET") {
    const params = new URL(request.url).searchParams;
    const search = (params.get("search") || "").trim().slice(0, 100);
    const offset = Number(params.get("offset") || 0);
    if (!Number.isSafeInteger(offset) || offset < 0) throw new InputError(400, "bad_input");
    const licenses = await db.prepare(`SELECT ${licenseColumns} FROM licenses l
      WHERE instr(lower(l.label), lower(?)) > 0
      ORDER BY l.created_at DESC, l.id DESC LIMIT 51 OFFSET ?`).bind(search, offset).all();
    return json(licenses.results);
  }
  if (path === "/api/admin/licenses" && request.method === "POST") {
    const { label, limit, days, expiresAt } = await input(request);
    const hasDate = expiresAt !== undefined;
    if (!validLabel(label) || !validLimit(limit) || (hasDate ? !validExpiry(expiresAt, now) : !validDays(days)))
      throw new InputError(400, "bad_input");
    const code = "SV-" + random(16).toUpperCase().match(/.{8}/g).join("-");
    await db.prepare(`INSERT INTO licenses (id, code_hash, label, device_limit, created_at, expires_at, duration_seconds)
      VALUES (?, ?, ?, ?, ?, ?, ?)`).bind(random(12), await digest(code.replace(/-/g, "").toLowerCase()),
      label.trim(), limit, now, hasDate ? expiresAt : null, hasDate ? null : days * DAY).run();
    return json({ code }, 201);
  }
  const match = /^\/api\/admin\/licenses\/([a-f0-9]{24})(?:\/(devices|extend|revoke))?$/.exec(path);
  if (!match) throw new InputError(404, "not_found");
  const [, id, action] = match;
  if (!action && request.method === "GET") {
    const license = await db.prepare(`SELECT ${licenseColumns} FROM licenses l WHERE l.id = ?`).bind(id).first();
    if (!license) throw new InputError(404, "not_found");
    return json(license);
  }
  if (action === "devices" && request.method === "GET") {
    return json((await db.prepare("SELECT device, activated_at FROM devices WHERE license_id = ? ORDER BY activated_at")
      .bind(id).all()).results);
  }
  if (request.method !== "POST") throw new InputError(405, "method_not_allowed");
  if (!action) {
    const { label, limit, days, expiresAt } = await input(request);
    if (!validLabel(label) || !validLimit(limit) || (days !== undefined && (!validDays(days) || expiresAt !== undefined)) ||
        (expiresAt !== undefined && !validExpiry(expiresAt, now))) throw new InputError(400, "bad_input");
    const duration = days === undefined ? null : days * DAY;
    const update = await db.prepare(`UPDATE licenses SET label = ?1, device_limit = ?2,
      expires_at = COALESCE(?3, expires_at),
      duration_seconds = CASE WHEN ?3 IS NOT NULL THEN NULL ELSE COALESCE(?4, duration_seconds) END
      WHERE id = ?5 AND (?4 IS NULL OR expires_at IS NULL)
        AND (SELECT COUNT(*) FROM devices WHERE license_id = ?5) <= ?2`)
      .bind(label.trim(), limit, expiresAt ?? null, duration, id).run();
    if (!update.meta.changes) {
      const existing = await db.prepare("SELECT id FROM licenses WHERE id = ?").bind(id).first();
      if (!existing) throw new InputError(404, "not_found");
      throw new InputError(409, "license_conflict");
    }
  } else if (action === "devices") {
    const { device } = await input(request);
    if (typeof device !== "string" || !fingerprint.test(device)) throw new InputError(400, "bad_input");
    await db.prepare("DELETE FROM devices WHERE license_id = ? AND device = ?").bind(id, device).run();
  } else if (action === "extend") {
    const { days } = await input(request);
    if (!validDays(days)) throw new InputError(400, "bad_input");
    await db.prepare(`UPDATE licenses SET expires_at = CASE WHEN expires_at IS NOT NULL THEN MAX(expires_at, ?1) + ?2 END,
      duration_seconds = CASE WHEN expires_at IS NULL THEN duration_seconds + ?2 ELSE duration_seconds END WHERE id = ?3`)
      .bind(now, days * DAY, id).run();
  } else if (action === "revoke") {
    const { revoked } = await input(request);
    if (typeof revoked !== "boolean") throw new InputError(400, "bad_input");
    await db.prepare("UPDATE licenses SET revoked = ? WHERE id = ?").bind(Number(revoked), id).run();
  } else {
    throw new InputError(404, "not_found");
  }
  return json({ ok: true });
}

export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    const now = Math.floor(Date.now() / 1000);
    try {
      if (path === "/api/health" && request.method === "GET") {
        await env.DB.prepare("SELECT id FROM licenses LIMIT 1").first();
        return json({ ok: true });
      }
      if (path === "/api/activate" && request.method === "POST") return await activate(env.DB, request, now);
      if (path === "/api/verify" && request.method === "POST") return await verify(env.DB, request, now);
      if (path.startsWith("/api/admin/")) return await admin(request, env, path, now);
      return json({ error: "not_found" }, 404);
    } catch (error) {
      if (error instanceof InputError) return json({ error: error.message }, error.status);
      console.error("License service error", error.name);
      return json({ error: "service_unavailable" }, 503);
    }
  }
};
