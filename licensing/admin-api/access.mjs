import { createRemoteJWKSet, jwtVerify } from "jose";
import { HttpError } from "../shared/http.mjs";

let issuerKeys;
let cachedIssuer;

export async function authenticate(request, env) {
  const token = request.headers.get("Cf-Access-Jwt-Assertion");
  if (!token || token.length > 8192) throw new HttpError(401, "unauthorized");
  const issuer = env.ACCESS_ISSUER;
  const audience = env.ACCESS_AUD;
  if (!/^https:\/\/[a-z0-9-]+\.cloudflareaccess\.com$/.test(issuer ?? "") || !audience || !env.ADMIN_ROLES)
    throw new HttpError(503, "service_unavailable");
  if (cachedIssuer !== issuer) {
    issuerKeys = createRemoteJWKSet(new URL(issuer + "/cdn-cgi/access/certs"), { timeoutDuration: 3500 });
    cachedIssuer = issuer;
  }
  let email;
  try {
    const { payload } = await jwtVerify(token, issuerKeys, {
      issuer, audience, algorithms: ["RS256"], requiredClaims: ["exp", "sub", "email"]
    });
    if (payload.type !== "app" || typeof payload.email !== "string") throw new Error();
    email = payload.email.toLowerCase();
  } catch {
    throw new HttpError(401, "unauthorized");
  }
  const roles = JSON.parse(env.ADMIN_ROLES);
  const role = Object.hasOwn(roles, email) && roles[email];
  if (role !== "owner" && role !== "support") throw new HttpError(403, "forbidden");
  return { email, role };
}
