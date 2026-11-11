import {
  compareVersions, digest, errorResponse, featureKeys, fingerprint, HttpError,
  input, json, random, requireInput, validVersion
} from "../shared/http.mjs";

const LEASE_SECONDS = 128;
const LAST_SEEN_SECONDS = 600;
const licenseFields = "l.id, l.revoked, l.expires_at, l.plan_id";
const gate = `EXISTS (SELECT 1 FROM release_channels
    WHERE channel = ?7 AND maintenance = 0 AND min_supported_version = ?8)
  AND NOT EXISTS (SELECT 1 FROM system_settings WHERE key = 'maintenance' AND value_json = 'true')`;

async function channelSettings(db, channel) {
  const row = await db.prepare(`SELECT c.*, r.manifest_url, r.checksum,
    COALESCE((SELECT value_json = 'true' FROM system_settings WHERE key = 'maintenance'), 0) AS global_maintenance
    FROM release_channels c LEFT JOIN releases r ON r.channel = c.channel
      AND r.version = c.current_version AND r.status = 'ready' WHERE c.channel = ?`).bind(channel).first();
  if (!row) throw new HttpError(503, "service_unavailable");
  return row;
}

function enforceChannel(row, version) {
  if (row.maintenance || row.global_maintenance) throw new HttpError(503, "maintenance");
  if (compareVersions(version, row.min_supported_version) < 0) throw new HttpError(426, "update_required");
}

function enforceLicense(row, now, missing) {
  if (!row) throw new HttpError(403, missing);
  if (row.revoked) throw new HttpError(403, "revoked");
  if (row.expires_at !== null && row.expires_at <= now) throw new HttpError(403, "expired");
}

async function entitlements(db, row, now) {
  const { results } = await db.prepare(`SELECT feature_key, enabled AS value, 2 AS priority
    FROM plan_features WHERE plan_id = ?1
    UNION ALL SELECT feature_key, value_json, CASE scope_type WHEN 'license' THEN 4 WHEN 'plan' THEN 3 ELSE 1 END
    FROM feature_flags WHERE (scope_type = 'global' AND scope_id = '')
      OR (scope_type = 'plan' AND scope_id = ?1) OR (scope_type = 'license' AND scope_id = ?2)
    ORDER BY priority`).bind(row.plan_id, row.id).all();
  const permissions = Object.fromEntries(featureKeys.map(key => [key, true]));
  for (const entry of results) {
    if (Object.hasOwn(permissions, entry.feature_key)) {
      permissions[entry.feature_key] = entry.value === 1 || entry.value === "true";
    }
  }
  return {
    authorized: true,
    leaseExpiresAt: Math.min(row.expires_at, now + LEASE_SECONDS),
    expiresAt: row.expires_at,
    serverTime: now,
    permissions
  };
}

async function limit(binding, keys) {
  const decisions = await Promise.all(keys.map(key => binding.limit({ key })));
  if (decisions.some(result => !result.success)) throw new HttpError(429, "rate_limited");
}

async function activate(request, env, body, now) {
  const { code, legacyToken, device } = body;
  requireInput((typeof code === "string" && legacyToken === undefined) ||
    (code === undefined && typeof legacyToken === "string" && fingerprint.test(legacyToken)));
  const normalized = code?.replace(/[\s-]/g, "").toLowerCase();
  if (code !== undefined && !/^sv[a-f0-9]{32}$/.test(normalized)) throw new HttpError(403, "invalid_code");
  await limit(env.ACTIVATION_LIMIT, ["ip:" + (request.headers.get("CF-Connecting-IP") || "unknown"), "device:" + device]);
  const channel = await channelSettings(env.DB, env.RELEASE_CHANNEL);
  enforceChannel(channel, body.version);
  const token = random(32);
  const [credentialHash, tokenHash, placeholder] = await Promise.all([
    digest(normalized ?? legacyToken), digest(token), digest(random(32))
  ]);
  const db = env.DB;
  const source = code !== undefined
    ? "l.code_hash = ?4"
    : "EXISTS (SELECT 1 FROM devices WHERE license_id = l.id AND device = ?1 AND token_hash = ?4)";
  const session = random(12);
  const values = [device, placeholder, now, credentialHash, tokenHash, session, channel.channel, channel.min_supported_version];
  // Every statement runs on the primary in a single transaction with the seat check.
  const [seat, , , added, selected] = await db.batch([
    db.prepare(`INSERT INTO devices (license_id, device, token_hash, activated_at, last_seen)
      SELECT l.id, ?1, ?2, ?3, ?3 FROM licenses l LEFT JOIN plans p ON p.id = l.plan_id
      WHERE ${source} AND l.revoked = 0 AND (l.expires_at IS NULL OR l.expires_at > ?3) AND ${gate}
        AND (EXISTS (SELECT 1 FROM devices WHERE license_id = l.id AND device = ?1)
          OR (SELECT COUNT(*) FROM devices WHERE license_id = l.id) < MIN(l.device_limit, COALESCE(p.max_devices, l.device_limit)))
      ON CONFLICT(license_id, device) DO UPDATE SET last_seen = excluded.last_seen`).bind(...values),
    db.prepare(`UPDATE licenses AS l SET expires_at = ?3 + duration_seconds
      WHERE changes() = 1 AND ${source} AND l.expires_at IS NULL`).bind(...values.slice(0, 4)),
    db.prepare(`UPDATE license_sessions SET revoked_at = ?3 WHERE device_id = ?1 AND revoked_at IS NULL
      AND license_id IN (SELECT l.id FROM licenses l WHERE ${source} AND l.revoked = 0 AND l.expires_at > ?3)
      AND ${gate}`).bind(...values),
    db.prepare(`INSERT INTO license_sessions (id, license_id, device_id, token_hash, created_at)
      SELECT ?6, l.id, ?1, ?5, ?3 FROM licenses l WHERE ${source}
        AND l.revoked = 0 AND l.expires_at > ?3 AND ${gate}
        AND EXISTS (SELECT 1 FROM devices WHERE license_id = l.id AND device = ?1)`).bind(...values),
    db.prepare(`SELECT ${licenseFields} FROM licenses l WHERE ${source}`).bind(...values.slice(0, 4))
  ]);
  const row = selected.results[0];
  if (!seat.meta.changes || !added.meta.changes) {
    enforceChannel(await channelSettings(db, channel.channel), body.version);
    enforceLicense(row, now, code !== undefined ? "invalid_code" : "device_removed");
    throw new HttpError(403, "device_limit");
  }
  return json({ ...await entitlements(db, row, now), token });
}

async function heartbeat(env, body, now) {
  const db = env.DB;
  const tokenHash = await digest(body.token);
  const row = await db.prepare(`SELECT ${licenseFields}, s.revoked_at, d.last_seen
    FROM license_sessions s JOIN licenses l ON l.id = s.license_id
    JOIN devices d ON d.license_id = s.license_id AND d.device = s.device_id
    WHERE s.token_hash = ? AND s.device_id = ?`).bind(tokenHash, body.device).first();
  enforceLicense(row, now, "device_removed");
  if (row.revoked_at !== null) throw new HttpError(403, "device_removed");
  if (row.last_seen === null || row.last_seen <= now - LAST_SEEN_SECONDS) {
    await db.prepare(`UPDATE devices SET last_seen = ?1 WHERE license_id = ?2 AND device = ?3
      AND (last_seen IS NULL OR last_seen <= ?4)`).bind(now, row.id, body.device, now - LAST_SEEN_SECONDS).run();
  }
  return json(await entitlements(db, row, now));
}

export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    const now = Math.floor(Date.now() / 1000);
    try {
      if (request.method === "GET" && path === "/v2/client/health") return json({ ok: true });
      if (request.method === "GET" && path === "/v2/client/version") {
        const channel = await channelSettings(env.DB, env.RELEASE_CHANNEL);
        return json({
          channel: channel.channel, currentVersion: channel.current_version, minSupportedVersion: channel.min_supported_version,
          manifestUrl: channel.manifest_url, checksum: channel.checksum, maintenance: Boolean(channel.maintenance || channel.global_maintenance)
        });
      }
      if (!["/v2/client/activate", "/v2/client/heartbeat", "/v2/client/deactivate"].includes(path))
        throw new HttpError(404, "not_found");
      if (request.method !== "POST") throw new HttpError(405, "method_not_allowed");
      const body = await input(request);
      requireInput(typeof body.device === "string" && fingerprint.test(body.device) && validVersion(body.version));
      if (path.endsWith("/activate")) return await activate(request, env, body, now);
      requireInput(typeof body.token === "string" && fingerprint.test(body.token));
      await limit(env.SESSION_LIMIT, ["ip:" + (request.headers.get("CF-Connecting-IP") || "unknown"), "session:" + await digest(body.token)]);
      if (path.endsWith("/deactivate")) {
        await env.DB.prepare(`UPDATE license_sessions SET revoked_at = ? WHERE token_hash = ?
          AND device_id = ? AND revoked_at IS NULL`).bind(now, await digest(body.token), body.device).run();
        return json({ ok: true });
      }
      const channel = await channelSettings(env.DB, env.RELEASE_CHANNEL);
      enforceChannel(channel, body.version);
      return await heartbeat(env, body, now);
    } catch (error) {
      return errorResponse(error);
    }
  }
};
