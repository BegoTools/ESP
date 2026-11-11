export const DAY = 86400;
export const fingerprint = /^[a-f0-9]{64}$/;
export const entityId = /^[a-f0-9]{24}$/;
export const planId = /^[a-z][a-z0-9-]{0,39}$/;
export const versionPattern = /^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$/;
export const validVersion = value => typeof value === "string" && versionPattern.test(value) && value.split(".").every(n => Number(n) <= 65534);
export const validLimit = value => Number.isInteger(value) && value >= 1 && value <= 100;
export const validDays = value => Number.isInteger(value) && value >= 1 && value <= 3650;
export const validLabel = value => typeof value === "string" && value.trim().length > 0 && value.length <= 100;
export const featureKeys = ["demo", "live", "inspection"];
const encoder = new TextEncoder();

export function compareVersions(left, right) {
  const first = left.split(".").map(Number);
  const second = right.split(".").map(Number);
  return first[0] - second[0] || first[1] - second[1] || first[2] - second[2];
}

function hex(bytes) {
  return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
}

export async function digest(value) {
  return hex(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value))));
}

export function random(size) {
  return hex(crypto.getRandomValues(new Uint8Array(size)));
}

export class HttpError extends Error {
  constructor(status, code) {
    super(code);
    this.status = status;
  }
}

export function requireInput(condition) {
  if (!condition) throw new HttpError(400, "bad_input");
}

export function json(value, status = 200) {
  return Response.json(value, {
    status,
    headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" }
  });
}

export function errorResponse(error) {
  if (error instanceof HttpError) return json({ error: error.message }, error.status);
  console.error("Cloud control request failed", error?.name);
  return json({ error: "service_unavailable" }, 503);
}

export async function input(request) {
  if (request.headers.get("Content-Type")?.split(";")[0].trim().toLowerCase() !== "application/json")
    throw new HttpError(415, "bad_input");
  const reader = request.body?.getReader();
  requireInput(reader);
  const decoder = new TextDecoder("utf-8", { fatal: true });
  let length = 0;
  let text = "";
  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      length += value.length;
      if (length > 4096) {
        await reader.cancel();
        throw new HttpError(413, "bad_input");
      }
      text += decoder.decode(value, { stream: true });
    }
    const body = JSON.parse(text + decoder.decode());
    if (body && typeof body === "object" && !Array.isArray(body)) return body;
  } catch (error) {
    if (error instanceof HttpError) throw error;
  } finally {
    reader.releaseLock();
  }
  throw new HttpError(400, "bad_input");
}

export async function page(db, url, query, sortColumns, defaultSort) {
  const search = (url.searchParams.get("search") || "").trim().slice(0, 100);
  const offset = Number(url.searchParams.get("offset") || 0);
  const limit = Number(url.searchParams.get("limit") || 25);
  const sort = url.searchParams.get("sort") || defaultSort;
  const order = url.searchParams.get("order") || "desc";
  requireInput(Number.isInteger(offset) && offset >= 0 && offset <= 100000 &&
    Number.isInteger(limit) && limit >= 1 && limit <= 100 && Object.hasOwn(sortColumns, sort) &&
    ["asc", "desc"].includes(order));
  const { results } = await db.prepare(query + " ORDER BY " + sortColumns[sort] + " " + order + ", 1 DESC LIMIT ?2 OFFSET ?3")
    .bind(search, limit + 1, offset).all();
  return { items: results.slice(0, limit), nextOffset: results.length > limit ? offset + limit : null };
}
