import { SignJWT, jwtVerify } from "jose";
import { HttpError, fingerprint, random } from "../shared/http.mjs";

const sessionCookie = "__Host-sv-admin-session";
const loginCookie = "__Host-sv-admin-login";
const loginSeconds = 600;
const sessionSeconds = 14400;
const callback = "/auth/callback";
const encoder = new TextEncoder();
const accountId = /^[1-9][0-9]{0,19}$/;
const loginName = /^[a-z\d](?:[a-z\d-]{0,37}[a-z\d])?$/i;

function configuration(env, url) {
  if (!/^https:\/\/[a-z0-9.-]+$/.test(env.GITHUB_ADMIN_ORIGIN ?? "") ||
      !env.GITHUB_ADMIN_CLIENT_ID || !env.GITHUB_ADMIN_CLIENT_SECRET ||
      !fingerprint.test(env.GITHUB_ADMIN_SESSION_KEY ?? "") || !env.GITHUB_ADMIN_ROLES)
    throw new HttpError(503, "service_unavailable");
  if (env.GITHUB_ADMIN_ORIGIN !== url.origin) throw new HttpError(403, "bad_origin");
  return {
    origin: env.GITHUB_ADMIN_ORIGIN,
    clientId: env.GITHUB_ADMIN_CLIENT_ID,
    clientSecret: env.GITHUB_ADMIN_CLIENT_SECRET,
    roles: JSON.parse(env.GITHUB_ADMIN_ROLES),
    key: Uint8Array.from(env.GITHUB_ADMIN_SESSION_KEY.match(/../g), value => parseInt(value, 16))
  };
}

function cookie(request, name) {
  return (request.headers.get("Cookie") ?? "").split(/;\s*/)
    .find(entry => entry.startsWith(name + "="))?.slice(name.length + 1);
}

function setCookie(name, value, seconds, sameSite = "Lax") {
  return `${name}=${value}; Max-Age=${seconds}; Path=/; Secure; HttpOnly; SameSite=${sameSite}`;
}

function redirect(location, cookies = []) {
  const headers = new Headers({ Location: location, "Cache-Control": "no-store", "Referrer-Policy": "no-referrer" });
  for (const value of cookies) headers.append("Set-Cookie", value);
  return new Response(null, { status: 303, headers });
}

function sign(config, purpose, claims, seconds) {
  return new SignJWT(claims).setProtectedHeader({ alg: "HS256" }).setIssuer(config.origin)
    .setAudience(config.clientId + "/" + purpose).setIssuedAt().setExpirationTime(seconds + "s").sign(config.key);
}

async function verify(config, purpose, token) {
  if (!token || token.length > 4096) throw new HttpError(401, "unauthorized");
  try {
    return (await jwtVerify(token, config.key, {
      issuer: config.origin, audience: config.clientId + "/" + purpose,
      algorithms: ["HS256"], requiredClaims: ["exp", "iat"]
    })).payload;
  } catch {
    throw new HttpError(401, "unauthorized");
  }
}

function actor(config, id, login) {
  if (typeof id !== "string" || !accountId.test(id) ||
      typeof login !== "string" || !loginName.test(login)) throw new HttpError(401, "unauthorized");
  const role = Object.hasOwn(config.roles, id) && config.roles[id];
  if (role !== "owner" && role !== "support") throw new HttpError(403, "forbidden");
  return { id, login, role };
}

export async function authenticateGitHub(request, env, url) {
  const config = configuration(env, url);
  const claims = await verify(config, "session", cookie(request, sessionCookie));
  return actor(config, claims.sub, claims.login);
}

async function beginLogin(config) {
  const state = random(32);
  const verifier = random(32);
  const hash = await crypto.subtle.digest("SHA-256", encoder.encode(verifier));
  const challenge = btoa(String.fromCharCode(...new Uint8Array(hash))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  const location = new URL("https://github.com/login/oauth/authorize");
  location.search = new URLSearchParams({
    client_id: config.clientId, redirect_uri: config.origin + callback, state,
    code_challenge: challenge, code_challenge_method: "S256", allow_signup: "false"
  });
  const nonce = await sign(config, "login", { state, verifier }, loginSeconds);
  return redirect(location, [setCookie(loginCookie, nonce, loginSeconds)]);
}

async function finishLogin(config, request, url) {
  const nonce = await verify(config, "login", cookie(request, loginCookie));
  const code = url.searchParams.get("code");
  if (!code || code.length > 128 || url.searchParams.get("state") !== nonce.state) throw new HttpError(401, "unauthorized");
  const exchange = await fetch("https://github.com/login/oauth/access_token", {
    method: "POST", redirect: "manual", signal: AbortSignal.timeout(5000),
    headers: { Accept: "application/json" },
    body: new URLSearchParams({
      client_id: config.clientId, client_secret: config.clientSecret,
      redirect_uri: config.origin + callback, code, code_verifier: nonce.verifier
    })
  });
  if (!exchange.ok) throw new HttpError(502, "login_failed");
  const { access_token: token } = await exchange.json();
  if (typeof token !== "string" || !token) throw new HttpError(401, "unauthorized");
  const profile = await fetch("https://api.github.com/user", {
    redirect: "manual", signal: AbortSignal.timeout(5000),
    headers: { Authorization: "Bearer " + token, Accept: "application/vnd.github+json", "User-Agent": "SpectraVision" }
  });
  if (!profile.ok) throw new HttpError(502, "login_failed");
  const user = await profile.json();
  if (!Number.isSafeInteger(user.id)) throw new HttpError(401, "unauthorized");
  const identity = actor(config, String(user.id), user.login);
  const session = await sign(config, "session", { sub: identity.id, login: identity.login }, sessionSeconds);
  return redirect("/", [setCookie(loginCookie, "", 0), setCookie(sessionCookie, session, sessionSeconds)]);
}

export function githubLogin(request, env, url) {
  const config = configuration(env, url);
  if (url.pathname === "/auth/login" && request.method === "GET") return beginLogin(config);
  if (url.pathname === callback && request.method === "GET") return finishLogin(config, request, url);
  if (url.pathname === "/auth/logout" && request.method === "POST") {
    if (request.headers.get("Origin") !== config.origin) throw new HttpError(403, "bad_origin");
    return redirect("/auth/signed-out", [setCookie(sessionCookie, "", 0), setCookie(loginCookie, "", 0)]);
  }
  if (url.pathname === "/auth/signed-out" && request.method === "GET") {
    return new Response('<!doctype html><html lang="en"><meta charset="utf-8"><title>SpectraVision</title><p>Signed out.</p><a href="/auth/login">Sign in with GitHub</a></html>', {
      headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store",
        "Content-Security-Policy": "default-src 'none'; frame-ancestors 'none'", "Referrer-Policy": "no-referrer" }
    });
  }
  throw new HttpError(404, "not_found");
}
