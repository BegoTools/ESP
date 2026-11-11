import { authenticate } from "./access.mjs";
import {
  compareVersions, DAY, digest, entityId, errorResponse, featureKeys, fingerprint, HttpError, input,
  json, page, planId, random, requireInput, validDays, validLabel, validLimit, validVersion
} from "../shared/http.mjs";

const channels = ["stable", "test"];
const licenseColumns = `l.id, l.label, l.device_limit, l.created_at, l.expires_at, l.duration_seconds, l.revoked, l.plan_id,
  (SELECT COUNT(*) FROM devices WHERE license_id = l.id) AS used`;
const licenseQuery = "SELECT " + licenseColumns + " FROM licenses l";
const deviceQuery = `SELECT d.device, d.license_id, l.label, d.activated_at, d.last_seen
  FROM devices d JOIN licenses l ON l.id = d.license_id`;

function validExpiry(value, now) {
  return Number.isSafeInteger(value) && value > now && value <= now + 3650 * DAY;
}

function notFound() {
  throw new HttpError(404, "not_found");
}

function conflict() {
  throw new HttpError(409, "conflict");
}

async function mutate(db, actor, now, action, type, id, statement, metadata = {}, extra = []) {
  const audit = db.prepare(`INSERT INTO audit_logs (id, actor, action, entity_type, entity_id, occurred_at, metadata_json)
    SELECT ?1, ?2, ?3, ?4, ?5, ?6, ?7 WHERE changes() > 0`)
    .bind(random(12), actor.email, action, type, id, now, JSON.stringify(metadata));
  const [changed] = await db.batch([statement, audit, ...extra]);
  return changed.meta.changes;
}

async function overview(db, actor, now) {
  const [licenses, devices, releases] = await db.batch([
    db.prepare(`SELECT COUNT(*) AS licenses,
      COUNT(CASE WHEN revoked = 0 AND (expires_at IS NULL OR expires_at > ?) THEN 1 END) AS available FROM licenses`).bind(now),
    db.prepare(`SELECT COUNT(*) AS devices,
      COUNT(CASE WHEN d.last_seen >= ?1 AND l.revoked = 0 AND l.expires_at > ?2 THEN 1 END) AS recentlyActive
      FROM devices d JOIN licenses l ON l.id = d.license_id`).bind(now - 900, now),
    db.prepare("SELECT * FROM release_channels ORDER BY channel")
  ]);
  return json({ actor, ...licenses.results[0], ...devices.results[0], channels: releases.results });
}

async function createLicense(db, request, actor, now) {
  const { label, limit, days, expiresAt, plan = null } = await input(request);
  requireInput(validLabel(label) && validLimit(limit) &&
    (expiresAt === undefined ? validDays(days) : days === undefined && validExpiry(expiresAt, now)) &&
    (plan === null || typeof plan === "string" && planId.test(plan)));
  const code = "SV-" + random(16).toUpperCase().match(/.{8}/g).join("-");
  const id = random(12);
  const statement = db.prepare(`INSERT INTO licenses (id, code_hash, label, device_limit, created_at, expires_at, duration_seconds, plan_id)
    SELECT ?1, ?2, ?3, MIN(?4, COALESCE((SELECT max_devices FROM plans WHERE id = ?8), ?4)), ?5, ?6, ?7, ?8
    WHERE ?8 IS NULL OR EXISTS (SELECT 1 FROM plans WHERE id = ?8 AND status = 'active')`)
    .bind(id, await digest(code.replace(/-/g, "").toLowerCase()), label.trim(), limit,
      now, expiresAt ?? null, days === undefined ? null : days * DAY, plan);
  if (!await mutate(db, actor, now, "create", "license", id, statement, { plan })) conflict();
  return json({ id, code }, 201);
}

async function editLicense(db, request, actor, now, id, row) {
  const body = await input(request);
  const { label = row.label, limit = row.device_limit, days, expiresAt, plan = row.plan_id } = body;
  requireInput(validLabel(label) && validLimit(limit) &&
    (days === undefined || expiresAt === undefined && validDays(days)) &&
    (expiresAt === undefined || validExpiry(expiresAt, now)) &&
    (plan === null || typeof plan === "string" && planId.test(plan)));
  const statement = db.prepare(`UPDATE licenses SET label = ?1, device_limit = MIN(?2, COALESCE(
      (SELECT max_devices FROM plans WHERE id = ?5), ?2)),
    expires_at = COALESCE(?3, expires_at),
    duration_seconds = CASE WHEN ?3 IS NOT NULL THEN NULL ELSE COALESCE(?4, duration_seconds) END,
    plan_id = ?5
    WHERE id = ?6 AND (?4 IS NULL OR expires_at IS NULL)
      AND label = ?7 AND device_limit = ?8 AND expires_at IS ?9 AND duration_seconds IS ?10 AND plan_id IS ?11
      AND (?5 IS NULL OR plan_id = ?5 OR EXISTS (SELECT 1 FROM plans WHERE id = ?5 AND status = 'active'))
      AND (SELECT COUNT(*) FROM devices WHERE license_id = ?6) <=
        MIN(?2, COALESCE((SELECT max_devices FROM plans WHERE id = ?5), ?2))`)
    .bind(label.trim(), limit, expiresAt ?? null, days === undefined ? null : days * DAY, plan, id,
      row.label, row.device_limit, row.expires_at, row.duration_seconds, row.plan_id);
  if (!await mutate(db, actor, now, "edit", "license", id, statement, { fields: Object.keys(body).filter(key =>
    ["label", "limit", "days", "expiresAt", "plan"].includes(key)) })) conflict();
  return json({ ok: true });
}

async function licenseDetail(db, request, actor, now, id, action, device) {
  const row = await db.prepare(licenseQuery + " WHERE l.id = ?").bind(id).first();
  if (!row) notFound();
  if (request.method === "GET") {
    if (!action) return json(row);
    if (action !== "devices") notFound();
    const query = deviceQuery + " WHERE d.license_id = ?";
    if (!device) return json((await db.prepare(query + " ORDER BY d.activated_at DESC").bind(id).all()).results);
    const linked = await db.prepare(query + " AND d.device = ?").bind(id, device).first();
    if (!linked) notFound();
    return json(linked);
  }
  if (!action && request.method === "PATCH") return editLicense(db, request, actor, now, id, row);
  if (action === "devices" && device && request.method === "DELETE") {
    const statement = db.prepare("DELETE FROM devices WHERE license_id = ? AND device = ?").bind(id, device);
    if (!await mutate(db, actor, now, "unlink", "device", id, statement, { devicePrefix: device.slice(0, 12) })) notFound();
    return json({ ok: true });
  }
  if (request.method !== "POST" || !["revoke", "extend"].includes(action)) notFound();
  const body = await input(request);
  if (action === "revoke") {
    const { revoked = true } = body;
    requireInput(typeof revoked === "boolean");
    const statement = db.prepare("UPDATE licenses SET revoked = ? WHERE id = ?").bind(Number(revoked), id);
    await mutate(db, actor, now, revoked ? "revoke" : "restore", "license", id, statement);
  } else {
    requireInput(validDays(body.days));
    const statement = db.prepare(`UPDATE licenses SET expires_at = CASE WHEN expires_at IS NOT NULL THEN MAX(expires_at, ?1) + ?2 END,
      duration_seconds = CASE WHEN expires_at IS NULL THEN duration_seconds + ?2 ELSE duration_seconds END WHERE id = ?3
      AND (CASE WHEN expires_at IS NOT NULL THEN MAX(expires_at, ?1) - ?1 ELSE duration_seconds END) + ?2 <= ?4`)
      .bind(now, body.days * DAY, id, 3650 * DAY);
    if (!await mutate(db, actor, now, "extend", "license", id, statement, { days: body.days })) conflict();
  }
  return json({ ok: true });
}

async function plans(db, request, url, actor, now) {
  if (request.method === "GET") {
    const result = await page(db, url, "SELECT * FROM plans WHERE instr(lower(name), lower(?1)) > 0 OR instr(id, ?1) > 0",
      { name: "name", limit: "max_devices" }, "name");
    return json(result);
  }
  if (request.method !== "PUT") notFound();
  const { id, name, status, maxDevices } = await input(request);
  requireInput(typeof id === "string" && planId.test(id) && validLabel(name) &&
    ["active", "archived"].includes(status) && validLimit(maxDevices));
  const statement = db.prepare(`INSERT INTO plans(id, name, status, max_devices)
    SELECT ?1, ?2, ?3, ?4 WHERE NOT EXISTS (
      SELECT 1 FROM licenses l WHERE l.plan_id = ?1 AND
        (SELECT COUNT(*) FROM devices d WHERE d.license_id = l.id) > ?4
    ) ON CONFLICT(id) DO UPDATE SET name = excluded.name, status = excluded.status, max_devices = excluded.max_devices`)
    .bind(id, name.trim(), status, maxDevices);
  if (!await mutate(db, actor, now, "save", "plan", id, statement, { status, maxDevices }, [
    db.prepare(`UPDATE licenses SET device_limit = ?1 WHERE changes() = 1 AND plan_id = ?2 AND device_limit > ?1`).bind(maxDevices, id)
  ])) conflict();
  return json({ ok: true });
}

async function features(db, request, url, actor, now) {
  if (request.method === "GET") return json(await page(db, url, `
    SELECT feature_key, scope_type, scope_id, value_json FROM (
      SELECT feature_key, scope_type, scope_id, value_json FROM feature_flags
      UNION ALL
      SELECT feature_key, 'plan_default', plan_id, CASE enabled WHEN 1 THEN 'true' ELSE 'false' END FROM plan_features
    ) WHERE instr(feature_key || ' ' || scope_type || ' ' || scope_id, ?1) > 0`,
    { key: "feature_key", scope: "scope_type || ':' || scope_id" }, "key"));
  if (request.method !== "PUT") notFound();
  const { key, scope, id = "", enabled } = await input(request);
  requireInput(featureKeys.includes(key) && [true, false, null].includes(enabled) && typeof id === "string" &&
    (scope === "global" && id === "" || ["plan", "plan_default"].includes(scope) && planId.test(id) ||
      scope === "license" && entityId.test(id)));
  let statement;
  if (scope === "plan_default") {
    statement = enabled === null
      ? db.prepare("DELETE FROM plan_features WHERE plan_id = ? AND feature_key = ?").bind(id, key)
      : db.prepare(`INSERT INTO plan_features (plan_id, feature_key, enabled)
        SELECT ?1, ?2, ?3 WHERE EXISTS (SELECT 1 FROM plans WHERE id = ?1)
        ON CONFLICT (plan_id, feature_key) DO UPDATE SET enabled = excluded.enabled`).bind(id, key, Number(enabled));
  } else if (enabled === null) {
    statement = db.prepare("DELETE FROM feature_flags WHERE feature_key = ? AND scope_type = ? AND scope_id = ?").bind(key, scope, id);
  } else {
    statement = db.prepare(`INSERT INTO feature_flags (feature_key, scope_type, scope_id, value_json, updated_at)
      SELECT ?1, ?2, ?3, ?4, ?5 WHERE ?2 = 'global'
        OR (?2 = 'plan' AND EXISTS (SELECT 1 FROM plans WHERE id = ?3))
        OR (?2 = 'license' AND EXISTS (SELECT 1 FROM licenses WHERE id = ?3))
      ON CONFLICT (feature_key, scope_type, scope_id) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at`)
      .bind(key, scope, id, JSON.stringify(enabled), now);
  }
  if (!await mutate(db, actor, now, enabled === null ? "remove" : "save", "feature", key, statement, { scope, id, enabled })) notFound();
  return json({ ok: true });
}

async function releases(db, request, url, actor, now) {
  if (request.method === "GET") return json(await page(db, url,
    "SELECT * FROM releases WHERE instr(version || ' ' || channel || ' ' || status, ?1) > 0",
    { created: "created_at", channel: "channel" }, "created"));
  if (request.method !== "POST") notFound();
  const { version, channel, manifestUrl, checksum } = await input(request);
  requireInput(validVersion(version) && channels.includes(channel) && typeof checksum === "string" && fingerprint.test(checksum) &&
    manifestUrl === "https://github.com/BegoTools/ESP/releases/download/sv-" + channel + "-v" + version + "/latest.json");
  const id = random(12);
  const statement = db.prepare(`INSERT INTO releases(id, version, channel, manifest_url, checksum, status, created_at)
    VALUES (?, ?, ?, ?, ?, 'ready', ?) ON CONFLICT (channel, version) DO NOTHING`)
    .bind(id, version, channel, manifestUrl, checksum, now);
  if (!await mutate(db, actor, now, "register", "release", id, statement, { channel, version })) conflict();
  return json({ id }, 201);
}

async function settings(db, request, actor, now) {
  if (request.method === "GET") {
    const [global, releaseChannels] = await db.batch([
      db.prepare("SELECT value_json FROM system_settings WHERE key = 'maintenance'"),
      db.prepare("SELECT * FROM release_channels ORDER BY channel")
    ]);
    return json({ maintenance: global.results[0]?.value_json === "true", channels: releaseChannels.results });
  }
  if (request.method !== "PUT") notFound();
  const { channel, maintenance, currentVersion, minSupportedVersion } = await input(request);
  requireInput(typeof maintenance === "boolean");
  if (channel === undefined) {
    const statement = db.prepare(`INSERT INTO system_settings(key, value_json, updated_at) VALUES ('maintenance', ?, ?)
      ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at`)
      .bind(JSON.stringify(maintenance), now);
    await mutate(db, actor, now, "save", "system", "maintenance", statement, { maintenance });
  } else {
    requireInput(channels.includes(channel) && validVersion(minSupportedVersion) &&
      (currentVersion === null && minSupportedVersion === "0.0.0" ||
        validVersion(currentVersion) && compareVersions(minSupportedVersion, currentVersion) <= 0));
    const statement = db.prepare(`UPDATE release_channels SET maintenance = ?1, current_version = ?2, min_supported_version = ?3
      WHERE channel = ?4 AND (?2 IS NULL OR EXISTS (
        SELECT 1 FROM releases WHERE channel = ?4 AND version = ?2 AND status = 'ready'
      ))`).bind(Number(maintenance), currentVersion, minSupportedVersion, channel);
    if (!await mutate(db, actor, now, "save", "channel", channel, statement, { maintenance, currentVersion, minSupportedVersion })) conflict();
  }
  return json({ ok: true });
}

async function route(request, env, url, actor, now) {
  const db = env.DB;
  const path = url.pathname.slice("/v2/admin/".length);
  if (actor.role !== "owner" && (request.method !== "GET" || path === "audit" || path === "system"))
    throw new HttpError(403, "forbidden");
  if (path === "overview" && request.method === "GET") return overview(db, actor, now);
  if (path === "licenses" && request.method === "GET") return json(await page(db, url,
    licenseQuery + " WHERE instr(lower(l.label), lower(?1)) > 0 OR instr(l.id, ?1) > 0",
    { created: "l.created_at", label: "l.label", expiry: "l.expires_at" }, "created"));
  if (path === "licenses" && request.method === "POST") return createLicense(db, request, actor, now);
  const detail = /^licenses\/([a-f0-9]{24})(?:\/(revoke|extend|devices)(?:\/([a-f0-9]{64}))?)?$/.exec(path);
  if (detail && (!detail[3] || detail[2] === "devices")) return licenseDetail(db, request, actor, now, ...detail.slice(1));
  if (path === "devices" && request.method === "GET") return json(await page(db, url,
    deviceQuery + " WHERE instr(lower(l.label), lower(?1)) > 0 OR instr(d.device, lower(?1)) > 0",
    { activated: "d.activated_at", seen: "d.last_seen", label: "l.label" }, "seen"));
  if (path === "plans") return plans(db, request, url, actor, now);
  if (path === "features") return features(db, request, url, actor, now);
  if (path === "releases") return releases(db, request, url, actor, now);
  if (path === "system") return settings(db, request, actor, now);
  if (path === "audit" && request.method === "GET") return json(await page(db, url,
    `SELECT * FROM audit_logs WHERE instr(lower(actor || ' ' || action || ' ' || entity_id || ' ' || entity_type), lower(?1)) > 0`,
    { time: "occurred_at", action: "action" }, "time"));
  notFound();
}

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      const actor = await authenticate(request, env);
      if (url.pathname.startsWith("/v2/admin/")) {
        if (request.method !== "GET" && request.headers.get("Origin") !== url.origin)
          throw new HttpError(403, "bad_origin");
        return await route(request, env, url, actor, Math.floor(Date.now() / 1000));
      }
      if (!["GET", "HEAD"].includes(request.method) || url.pathname.startsWith("/v2/")) notFound();
      const asset = await env.ASSETS.fetch(request);
      const response = new Response(asset.body, asset);
      response.headers.set("Cache-Control", "private, no-store");
      response.headers.set("X-Content-Type-Options", "nosniff");
      response.headers.set("Referrer-Policy", "no-referrer");
      response.headers.set("Content-Security-Policy", "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
      return response;
    } catch (error) {
      return errorResponse(error);
    }
  }
};
