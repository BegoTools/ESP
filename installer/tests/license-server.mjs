import { createServer } from "node:http";
import { writeFileSync } from "node:fs";

let mode = "allow";
let timeout;
const server = createServer((request, response) => {
  if (request.url.startsWith("/fixture/")) {
    mode = request.url.slice("/fixture/".length);
    response.end("ok");
    return;
  }
  if (!["/v2/client/activate", "/v2/client/heartbeat"].includes(request.url) || request.method !== "POST") {
    response.writeHead(404).end();
    return;
  }
  let input = "";
  request.setEncoding("utf8");
  request.on("data", chunk => { input += chunk; });
  request.on("end", () => {
    const body = JSON.parse(input);
    if (body.version !== "2.0.0" || !/^[a-f0-9]{64}$/.test(body.device)) {
      response.writeHead(400).end("{}");
      return;
    }
    if (mode === "timeout") {
      timeout = setTimeout(() => response.end("{}"), 30000);
      response.on("close", () => clearTimeout(timeout));
      return;
    }
    const status = { denied: 403, rate_limited: 429, unavailable: 500, maintenance: 503, update_required: 426, revoked: 403,
      expired: 403, device_removed: 403 }[mode] ?? 200;
    response.writeHead(status, { "Content-Type": "application/json" });
    if (mode === "invalid") { response.end("{ malformed"); return; }
    if (["maintenance", "update_required", "revoked", "expired", "device_removed"].includes(mode)) {
      response.end(JSON.stringify({ error: mode }));
      return;
    }
    if (body.legacyToken && body.legacyToken !== "b".repeat(64)) { response.end("{}"); return; }
    const serverTime = Math.floor(Date.now() / 1000);
    response.end(JSON.stringify({
      authorized: true, serverTime, leaseExpiresAt: serverTime + (mode === "short" ? 2 : 128), expiresAt: serverTime + 600,
      permissions: { demo: true, live: mode !== "no-live", inspection: true },
      token: request.url.endsWith("/activate") ? "a".repeat(64) : undefined
    }));
  });
});
server.listen(0, "127.0.0.1", () => writeFileSync(process.argv[2], String(server.address().port)));
