# 02 — Security and Identity Specification

| Field | Value |
| ----- | ----- |
| Scope | Transport and origin policy, CSRF on the cookie-bearing endpoints, the mandatory credential envelope protocol, access and refresh token design, the encrypted subject claim, per-request principal construction, rate limiting, credential storage, security events, verification |
| Audience | Security lead, lead developers, validation lead |
| Status | **Ratified for Phase 1 (2026-10-08).** Implements `D-01` (access JWT + refresh rotation), `D-02` (encrypted `sub`, no `email`), `D-03` (envelope mandatory in the core), `D-04` (queries gated), `D-05` (.NET 9 / PostgreSQL 17), `D-06` (platform users + tenant memberships), `D-07` (no PIN and no signing ceremony; passwords are the only enveloped credential; step-up re-authentication for sensitive actions) from `00 §0`. Items marked `[C-nn]` were adopted from the critique. Protocol sections (§3, §4.6) are the 2026-10-07 text, now normative |
| Libraries (D-05) | `Microsoft.AspNetCore.Authentication.JwtBearer` 9.x, `Microsoft.IdentityModel.*` 8.x, `System.Security.Cryptography`, Npgsql 9; no new cryptographic dependency |
| Posture | High-security B2B enterprise SaaS; no sector-specific regulatory regime is assumed (D-07) |
| Date | 2026-10-07 |

---

## 1. Threat model, stated before the controls

A control that does not name its threat cannot be validated. The table is the contract for the rest of this document.

| Threat | In scope | Primary control | Secondary | Explicitly not addressed by |
| --- | --- | --- | --- | --- |
| Passive network interception | Yes | TLS 1.2+/1.3 at the proxy, HSTS | — | — |
| TLS-terminating intermediary logging request bodies (corporate inspection proxy) | **Yes (D-03)** | Credential envelope (§3): passwords and PINs are ciphertext in every HTTP body | Rate limits, lockout | The access token and refresh cookie are still visible to such a proxy; their lifetimes bound the exposure |
| Credential stuffing / password guessing | Yes | Account-aware sliding-window limits (§5), lockout (§6), MFA (§4.5) | Security events, alerts | — |
| Step-up bypass for operations the configuration protects | Yes | Recent password proof (§4.5), enveloped like sign-in; the protected set is configuration | `step-up` rate limit (§5.2) | Operations the business has not listed |
| Stolen access token | Yes | 15-minute lifetime (D-01); per-request session check (§4.4) so revocation lands within the cache window; `sub` encrypted so the token reveals no identity (D-02) | Security events on anomalies | The token remains a bearer credential for its remaining lifetime |
| Stolen refresh cookie | Yes | `HttpOnly`, `Secure`, `__Secure-` prefix, `Path=/api/v1/auth`, `SameSite=Strict`; rotation on every use; **family revocation on reuse** | Idle and absolute expiry | — |
| CSRF against the cookie-bearing endpoints (`/auth/refresh`, `/auth/sign-out` only) | Yes | `SameSite=Strict` + `Origin`/`Sec-Fetch-Site` check + required custom header (§2.3) | Every other endpoint is bearer-only and ignores cookies | — |
| XSS in the SPA | Partially | Strict CSP (`script-src 'self'`, no inline), Angular's sanitizer, token in memory only (nothing durable to exfiltrate), refresh cookie unreadable by script | 15-minute token, step-up re-authentication for sensitive actions | **Envelopes, claim encryption and refresh rotation do nothing here**; page code can act as the user while the page lives and can use the in-memory token for ≤ 15 minutes |
| Token inspection (jwt.io) revealing identity | Yes | `sub` AES-256-GCM encrypted and different per token; no `email`, `name`, `role` (D-02, §4.6) | — | `tid` and `scope` are readable by design |
| Cross-tenant data access | Yes | RLS + filters + composite keys (01 §4) | Deny-by-default authorization | — |
| Privilege escalation via stale claims | Yes | No role or privilege claims; resolved per request from the session (§4.4) | 10-second cache evicted by events | — |
| Replay of a captured mutation | Yes | Idempotency key with body hash; natural unique keys | — | — |
| Insider with database access | Partially | Runtime role least privilege; append-only, hash-chained ledgers; approved-record immutability triggers | Security events on elevation | A superuser can still read; use encryption at rest and access logging at the platform level |
| Cross-tenant read of identity data | Yes | `tenant_memberships` under `FORCE RLS`; `platform_users` queried only by the actor's own id or a session key (D-06) | Deny-by-default on queries (D-04) | — |
| Misconfigured environment weakening controls | Yes | Guards run in **every** environment `[C-07]`; no `Optional` envelope mode exists | Start-up security event | — |

---

## 2. Traffic and origin policy

### 2.1 TLS and HSTS (ADR-0002 carried forward)

- TLS terminates at the reverse proxy; TLS 1.2 minimum, 1.3 preferred; HTTP → HTTPS `301`; certificates auto-renewed; OCSP stapling on.
- The application emits `Strict-Transport-Security: max-age=63072000; includeSubDomains` on every response outside local development. Preload is a deployment decision per customer domain.
- Kestrel binds loopback HTTP only. `UseHttpsRedirection` is not used (it would loop behind a proxy). The start-up guard refuses a non-loopback bind unless `Security:AllowInsecureLocalDevelopment=true`, which itself refuses a non-loopback bind.

### 2.2 Reverse proxy rules

The proxy serves the SPA, forwards `/api`, `/health`, `/metrics` and **strips** inbound forwarding headers.

```nginx
# deploy/nginx/platform.conf (reference; IIS/ARR equivalent in deploy/iis/web.config)
server {
    listen 443 ssl http2;
    server_name lab.example.com;
    ssl_protocols TLSv1.2 TLSv1.3;

    # SPA
    root /srv/platform/spa;
    location / {
        try_files $uri $uri/ /index.html;
        add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'" always;
        add_header X-Content-Type-Options nosniff always;
        add_header Referrer-Policy no-referrer always;
        add_header Cache-Control "no-store" always;   # index.html; hashed assets get immutable caching in a sub-location
    }

    # API — never trust client-supplied forwarding headers
    location ~ ^/(api|health|metrics)/ {
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $remote_addr;    # overwrite, do not append
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Host  $host;
        proxy_hide_header X-Powered-By;
        proxy_read_timeout 60s;
        client_max_body_size 50m;                           # file uploads; the API enforces its own limit too
        proxy_pass http://127.0.0.1:5080;
    }
}
server { listen 80; server_name lab.example.com; return 301 https://$host$request_uri; }
```

`style-src 'unsafe-inline'` is the one concession Angular's component styles need in this configuration; if the build is configured to emit external styles, drop it.

Application side: `UseForwardedHeaders` with `XForwardedFor | XForwardedProto`, loopback trusted by default, `KnownNetworks` extended only through configuration for a proxy on another host. The API's own headers (`SecurityHeadersMiddleware`): `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` on every `/api` response.

### 2.3 Same-origin enforcement and CSRF on the cookie-bearing endpoints (D-01)

The API registers **no CORS policy**. Under D-01 only two endpoints read a cookie: `POST /api/v1/auth/refresh` and `POST /api/v1/auth/sign-out`. Every other endpoint authenticates by bearer token only and ignores cookies, so CSRF cannot reach it (`RefreshSessionTests` proves a cookie-only request to a data endpoint is `401`). Three explicit checks make the defence on the two cookie-bearing endpoints testable rather than incidental.

For `POST /api/v1/auth/refresh` and `POST /api/v1/auth/sign-out`:

1. **`Sec-Fetch-Site`** must be `same-origin` or `none` when present (all evergreen browsers send it).
2. **`Origin`** (or, when absent, `Referer`) must equal the configured public origin(s) of the deployment.
3. **`X-Requested-With: Platform`** must be present. A custom header cannot be set by a cross-site form post and triggers a CORS preflight (which fails, since no CORS policy exists) from a cross-origin script.

Failure is `403 CSRF-403` with a security event `CSRF_REJECTED` (origin, path, no body). Machine clients refresh through the same endpoint with the refresh token in the body instead of a cookie and are exempt from checks 1–3 (no browser context). The SPA sends the custom header on every request through its interceptor, so the two cookie-bearing calls always carry it.

```csharp
public sealed class AuthOriginCheckMiddleware(RequestDelegate next, IOptions<OriginOptions> origins)
{
    private static readonly PathString AuthPrefix = "/api/v1/auth";
    private static readonly string[] CookieEndpoints = ["/refresh", "/sign-out"];

    public async Task InvokeAsync(HttpContext context)
    {
        var isCookieEndpoint = HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.StartsWithSegments(AuthPrefix, out var rest)
            && CookieEndpoints.Contains(rest.Value, StringComparer.OrdinalIgnoreCase)
            && context.Request.Cookies.ContainsKey(RefreshCookie.Name);          // machine clients send the token in the body
        if (!isCookieEndpoint)
        {
            await next(context); return;
        }

        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite is not ("" or "same-origin" or "none")) { await Reject(context, "fetch-site"); return; }

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) origin = OriginOf(context.Request.Headers.Referer.ToString());
        if (!origins.Value.Allowed.Contains(origin, StringComparer.OrdinalIgnoreCase)) { await Reject(context, "origin"); return; }

        if (context.Request.Headers["X-Requested-With"] != "Platform") { await Reject(context, "header"); return; }

        await next(context);
    }
}
```

### 2.4 The refresh cookie (D-01)

```http
Set-Cookie: __Secure-rt=<sessionId>.<base64url 32 random bytes>; Path=/api/v1/auth; Secure; HttpOnly; SameSite=Strict
```

- `__Secure-` prefix forces `Secure`. `Path=/api/v1/auth` keeps the cookie off every data endpoint; the `__Host-` prefix would require `Path=/` and is therefore not used.
- `SameSite=Strict`: the cookie rides only same-site requests; the two endpoints that read it are `POST` only.
- No `Max-Age`: a session cookie; the server-side sliding and absolute expiries (§4.3) are authoritative.
- The server stores only the SHA-256 of the secret part (`identity.refresh_sessions.token_hash`). The cookie value never appears in logs, events or the database.
- Local development over loopback HTTP uses `rt` without the prefix and without `Secure`, only when `Security:AllowInsecureLocalDevelopment=true`.

---

## 3. Credential envelope protocol (mandatory in the WebApi core, D-03)

### 3.1 Scope, and what it does and does not do

Every password in every request body travels as an envelope sealed in the browser and opened by a global action filter before the action runs (D-03). Passwords therefore never appear in plaintext in an HTTP body: not in the Network tab, not in proxy or WAF logs, not to a TLS-terminating intermediary. It does **not** protect against code running in the page and does **not** make a stolen token less useful; both facts are recorded in the security design record.

Scope is derived from the `[CredentialField]` attribute, never from a hand-kept list: `CredentialFieldTests` fails the build when a password-named string property on a request record lacks it. The credential-bearing contracts in V2 are: sign-in, change my password, administrator password reset, user registration and invitation acceptance, tenant provisioning, step-up re-authentication (§4.5), and machine-credential creation where a secret is supplied rather than generated. The baseline's inventory (nine contracts) is the reference for completeness.

There is **no `Optional` mode** (`[C-07]`): the filter is on in every environment. Functional tests and the Postman collection use a test-only sealing helper that behaves like the browser; a plaintext credential is refused everywhere with `CRED-005`. Below the edge nothing changes: handlers, Application and Domain receive plaintext in server memory exactly as before.

### 3.2 Sequence

```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant S as SPA (WebCrypto)
    participant F as Edge filter (WebApi)
    participant N as Nonce store (PostgreSQL UNLOGGED)
    participant H as Handler

    U->>S: enters password
    S->>F: GET /api/v1/auth/credential-challenge
    F-->>S: 200 {kid, jwk(RSA-OAEP-256 public), enc: A256GCM, nonce, expiresAtUtc}  (nonce = random ‖ expiry, HMAC'd; stateless to issue)
    S->>S: importKey(jwk) · generate CEK (AES-256) · wrap CEK with RSA-OAEP-256
    S->>S: for each [CredentialField]: AES-GCM(CEK, iv, plaintext={v,f,aud,n}, aad=protected header)
    S->>F: POST /api/v1/auth/sign-in {email, password: <compact JWE>, ...}
    F->>F: parse JWE · kid known? · unwrap CEK · AEAD open · check n (MAC, expiry) · aud == "POST /api/v1/auth/sign-in" · f == "password"
    F->>N: INSERT nonce_hash (fails if present)
    N-->>F: ok | duplicate
    alt duplicate
        F-->>S: 400 CRED-003 (replay) + security event; not counted toward lockout
    else ok
        F->>F: replace property value with plaintext
        F->>H: action executes with plaintext in server memory only
        H-->>S: normal response
    end
```

### 3.3 The challenge

`GET /api/v1/auth/credential-challenge` — anonymous, rate-limit partition `credential-challenge` (60/min per client address, separate from the credential budget).

```json
{
  "kid": "2026-10-07T00:00Z#1",
  "publicKey": { "kty": "RSA", "n": "<base64url>", "e": "AQAB", "alg": "RSA-OAEP-256", "use": "enc" },
  "algorithm": "RSA-OAEP-256",
  "encryption": "A256GCM",
  "nonce": "<base64url(32 random bytes ‖ expiry-unix-seconds ‖ HMAC-SHA256(key, random ‖ expiry))>",
  "expiresAtUtc": "2026-10-07T10:02:00Z"
}
```

- The nonce is **stateless to issue** (MAC'd) and **stateful to consume** (inserted into `app.credential_nonces` on first use; the primary key refuses a second use). Validity 120 seconds.
- One challenge serves one HTTP request; every credential field in that request carries the same nonce.

### 3.4 Envelope format (compact JWE, RFC 7516)

```text
BASE64URL(UTF8(protected header)) '.' BASE64URL(encrypted CEK) '.' BASE64URL(IV) '.' BASE64URL(ciphertext) '.' BASE64URL(tag)

protected header: {"alg":"RSA-OAEP-256","enc":"A256GCM","kid":"<kid>"}
CEK: 32 random bytes, wrapped with RSA-OAEP (SHA-256, MGF1-SHA-256), RSA-3072
IV: 12 random bytes
plaintext (UTF-8 JSON): {"v":"<field value>","f":"<camelCase property name>","aud":"<METHOD> <path without query>","n":"<nonce>"}
AAD: the ASCII bytes of BASE64URL(protected header)
tag: 16 bytes
```

### 3.5 Browser side (WebCrypto, no dependency)

```typescript
// core/http/credential-envelope.ts
export async function seal(challenge: CredentialChallenge, field: string, value: string, aud: string): Promise<string> {
  const header = b64u(new TextEncoder().encode(JSON.stringify({ alg: 'RSA-OAEP-256', enc: 'A256GCM', kid: challenge.kid })));
  const rsa = await crypto.subtle.importKey('jwk', challenge.publicKey, { name: 'RSA-OAEP', hash: 'SHA-256' }, false, ['encrypt']);
  const cek = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt']);
  const rawCek = await crypto.subtle.exportKey('raw', cek);
  const wrapped = await crypto.subtle.encrypt({ name: 'RSA-OAEP' }, rsa, rawCek);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const plaintext = new TextEncoder().encode(JSON.stringify({ v: value, f: field, aud, n: challenge.nonce }));
  const sealed = new Uint8Array(await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv, additionalData: new TextEncoder().encode(header), tagLength: 128 }, cek, plaintext));
  const ciphertext = sealed.slice(0, sealed.length - 16);
  const tag = sealed.slice(sealed.length - 16);
  return [header, b64u(wrapped), b64u(iv), b64u(ciphertext), b64u(tag)].join('.');
}
```

The `credentialTransportInterceptor` reads the generated client's metadata to know which properties of which endpoints are `[CredentialField]`, fetches one challenge per request, seals each field with the same nonce, and retries **once** with a fresh challenge on `CRED-002` or `CRED-003`.

### 3.6 Server side: the edge filter

```csharp
/// <summary>Opens every [CredentialField] value before model validation runs. The only code that knows the transport.</summary>
public sealed class CredentialTransportFilter(ICredentialEnvelopeOpener opener, ISecurityEventLog events) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        foreach (var (argument, property) in CredentialFieldMap.For(ctx.ActionDescriptor))   // cached reflection per action
        {
            var value = (string?)property.GetValue(ctx.ActionArguments[argument]);
            if (value is null) continue;

            var result = await opener.OpenAsync(
                value,
                expectedField: property.Name.ToCamelCase(),
                expectedAudience: $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}",
                ctx.HttpContext.RequestAborted);

            if (!result.Ok)
            {
                await events.WriteAsync("CREDENTIAL_ENVELOPE_REJECTED", tenantId: null, actor: null, detail: result.Code, ctx.HttpContext.RequestAborted);
                ctx.Result = ProblemResult.BadRequest(result.Code, result.Title);   // never counts toward lockout
                return;
            }
            property.SetValue(ctx.ActionArguments[argument], result.Plaintext);
        }
        await next();
    }
}
```

Checks, in order, each with its refusal code:

| Order | Check | Code | Notes |
| --- | --- | --- | --- |
| 1 | Five segments, header parses, `alg`/`enc` exact | `CRED-001` | malformed |
| 2 | `kid` is the active key or the previous key within overlap | `CRED-002` | key unknown / rotated out |
| 3 | RSA-OAEP unwrap succeeds, AES-GCM tag verifies with header as AAD | `CRED-001` | tamper |
| 4 | Nonce MAC verifies; not expired | `CRED-002` | expired challenge |
| 5 | Nonce inserted into `app.credential_nonces` (PK) | `CRED-003` | replay |
| 6 | `aud` equals actual method and path; `f` equals the property | `CRED-004` | re-targeted |
| 7 | Module is on and the value was plaintext | `CRED-005` | plaintext where envelope required |

### 3.7 Keys and nonces: shared, not per process `[C-08]`

- RSA-3072 key pair generated at start-up by the **first** replica to acquire `pg_try_advisory_lock(EnvelopeKeyLock)` and stored encrypted (ASP.NET Data Protection with a shared key ring in PostgreSQL) in `identity.envelope_keys (kid, created_at_utc, retired_at_utc, private_key_enc)`. Other replicas read it. Rotation every 24 hours; the previous key accepted for 10 minutes.
- The nonce HMAC key is stored the same way.
- `app.credential_nonces` is an `UNLOGGED` table: nonces are 120-second facts; losing them on a crash only forces fresh challenges.
- A restart therefore does **not** invalidate outstanding challenges.

### 3.8 What the module must never do

- Count a `CRED-*` refusal toward any lockout (no credential was tested).
- Offer an `Optional` mode or a plaintext fallback anywhere. Test harnesses seal like the browser does, or swap `ICredentialEnvelopeOpener` in the test factory; no configuration key controls the filter.
- Log the envelope, the plaintext, or the nonce.

### 3.9 Roadmap note: passkeys

WebAuthn sign-in (resident keys, user verification required) removes the password from the wire for authentication and is the measure that satisfies "no plaintext password in network payloads" without custom protocol. It is a product roadmap item with no external gate (D-07). Until adopted, sign-in and step-up are password + TOTP, with the password enveloped.

---

## 4. Sessions and tokens

### 4.1 Browser sign-in, hydrate and refresh (D-01)

**Sign-in flow**

```mermaid
sequenceDiagram
    autonumber
    participant S as SPA
    participant A as /api/v1/auth
    participant DB as identity.* · app.*
    S->>A: POST /sign-in {tenant, email, password}   (credential fields enveloped, §3)
    A->>DB: platform_users by email · Argon2id verify · lockout · active tenant_memberships row (D-06)
    alt MFA enrolled
        A->>DB: refresh_sessions row {kind: browser, scope: mfa_pending, new family_id}
        A-->>S: 200 {step: "mfa"} + Set-Cookie __Secure-rt (scope mfa_pending)
        S->>A: POST /mfa/verify {code}  (cookie identifies the pending session)
        A->>DB: TOTP verify · scope := full · rotate refresh token
        A-->>S: 200 AuthResponse {accessToken (ES256, 15 min), expiresAtUtc, profile} + rotated Set-Cookie
    else MFA not enrolled and Security:Mfa:Policy requires it for this user
        A->>DB: refresh_sessions row {scope: mfa_enrollment}
        A-->>S: 200 AuthResponse {accessToken with scope=mfa_enrollment} + Set-Cookie
        Note over S,A: MfaEnrollmentGateMiddleware admits only /mfa/enroll and /mfa/confirm until enrolment completes
    else MFA not enrolled and the policy does not require it
        A->>DB: refresh_sessions row {kind: browser, scope: full, new family_id}
        A-->>S: 200 AuthResponse {accessToken (ES256, 15 min), expiresAtUtc, profile} + Set-Cookie
    end
    S->>S: AuthService.token.set(accessToken); profile → signals; nothing in web storage
    Note over S,A: page reload: POST /refresh (cookie) → new access token + rotated cookie (hydrate)
    Note over S,A: on 401 from a data endpoint: one single-flight POST /refresh, then retry the original request once
```

If the user holds several active memberships and `tenant` is omitted, sign-in returns `200 {step: "select-tenant", tenants: [...]}` and the SPA re-posts with the choice (D-06). `AuthResponse.profile` carries display name, tenant, role name, permission keys and preferred language; the SPA reads identity from this body and **never** decodes the token. Whether the `mfa` step appears depends on `Security:Mfa:Policy` (§4.5).

**Session rules**

| Rule | Value | Rationale |
| --- | --- | --- |
| Access token lifetime | **15 minutes** (specification default under D-01; a gate fails when the code default or any committed configuration exceeds it) | a stolen token expires fast; refresh makes the value invisible to users |
| Sliding refresh expiry (idle) | 8 hours for browser sessions (configurable 30 min to 8 h per tenant), extended on each refresh; the SPA's 30-minute inactivity watch signs out earlier | session hygiene; limits what an unattended workstation exposes |
| Absolute expiry | 12 hours for browser sessions | bounds a stolen cookie regardless of activity |
| Concurrent sessions | allowed; listed in "My sessions"; each revocable | users work from several devices |
| Password change | revokes every other session of the user | credential compromise response |
| Admin deactivation / role change | revokes all sessions immediately | least privilege takes effect immediately |
| Tenant suspension | revokes all sessions of its memberships | control plane |
| IP or user-agent change | **signal**, not refusal: writes `SESSION_CONTEXT_CHANGED`; refusal is a tenant policy option | mobile and VPN users change addresses |
| Step-up re-authentication (capability) | operations listed in `Security:StepUp:Operations` require a fresh password proof (`POST /auth/reauthenticate`, enveloped) within `Security:StepUp:MaxAge` (default 5 min); refused with `403 AUTH-STEPUP-REQUIRED` otherwise; the list is empty until the business configures it | limits what a hijacked session can do, where the business chooses to apply it |
| Logout | `POST /sign-out` revokes the session and clears the cookie | — |

Revocation is exact: the `refresh_sessions` row is marked revoked; the next request carrying an access token with that `sid` (≤ 10 s with the cache) fails `AUTH-006`, and the next refresh fails `401`.

### 4.2 Access token (browser and machine clients, D-01, D-02)

One token format for both kinds of client. Browser tokens are issued by sign-in, MFA verification and refresh (§4.1). Machine tokens are issued by `POST /api/v1/auth/token` to a **machine credential** (client id + secret created by a tenant administrator, or a user's personal access token) and refreshed with the refresh token in the request body instead of a cookie.

**Header**

```json
{ "alg": "ES256", "typ": "at+jwt", "kid": "2026-10#1" }
```

**Payload**

| Claim | Value | Plain/opaque | Why |
| --- | --- | --- | --- |
| `iss` | `https://lab.example.com` | plain | RFC 9068 |
| `aud` | `platform-api` | plain | — |
| `sub` | **AES-256-GCM ciphertext of the user id**, `v1.<kid>.<base64url>` (§4.6) | opaque; **different in every token** | D-02 |
| `sid` | refresh-session id (`identity.refresh_sessions.id`, UUIDv7) | plain | locates the session row without decrypting; exact revocation |
| `tid` | tenant id | plain | lets the edge select the tenant GUC before any DB read; a tenant id is not PII |
| `scope` | `full` \| `mfa_enrollment` \| machine scopes | plain | gates |
| `jti` | random | plain | revocation list for emergencies |
| `iat`, `nbf`, `exp` | `exp` = `iat` + 15 min | plain | `[C-05]` |

**Absent by design:** `email` (D-02), `name`, `role`, `permissions`. Display name, role and privileges are resolved per request from the session (§4.4), so revocation and privilege changes take effect within the cache window rather than at token expiry.

**Signature.** ES256 (P-256) with the private key in the secret store; public keys published at `/.well-known/jwks.json` for any future internal verifier; `kid` rotation quarterly with a 24-hour overlap. The start-up guard refuses a key that is not a valid P-256 private key.

### 4.3 Refresh token rotation lifecycle (ADR-0009 model under D-01)

| Item | Specification |
| --- | --- |
| Format | `"<sessionId>.<base64url 32 random bytes>"`; browser: `__Secure-rt` cookie (§2.4); machine: response body, stored by the client |
| Storage | `identity.refresh_sessions`: `token_hash = SHA-256(secret)`, `family_id`, `kind`, `scope`, expiries, `rotated_at_utc`, `successor_id`, `revoked_at_utc`, `revocation_reason` (`01 §4.7`) |
| Rotation | every `POST /auth/refresh` marks the presented row rotated, issues a successor in the same `family_id`, and returns a new access token; the old secret is dead immediately |
| Reuse detection | presenting a rotated or revoked token revokes **the whole family** (`revocation_reason = reuse_detected`) and writes `REFRESH_REUSE_DETECTED`; the legitimate user signs in again |
| Lifetimes | browser: sliding 8 h, absolute 12 h; machine: sliding 30 days, absolute 90 days |
| Revocation rules | sign-out revokes the family; password change revokes every other family of the user; administrator deactivation, role change or membership removal revokes all families of the user; tenant suspension revokes all families of its memberships; expiry is checked on every refresh and on every authenticated request (§4.4) |
| Refusals | missing, unknown, expired, rotated or revoked token → `401 AUTH-012`; the SPA treats it as "signed out" and routes to sign-in |
| Concurrency | two tabs refreshing at once: the SPA's single-flight guard prevents it; if it happens anyway (two devices), the second presentation of the same secret is a reuse and revokes the family by design, which is the documented trade of rotation |

**Validation per request.** Signature and lifetime by the JWT handler (ES256, `MapInboundClaims = false`, clock skew 60 s); then `sub` is decrypted (§4.6) and `sid` read; then the session row for `sid` must exist, be unrevoked, be within both expiries and belong to the decrypted user id (`401 AUTH-011` otherwise); then the principal is built as in §4.4.

### 4.3a What authenticates which request

| Endpoint | Credential read | Credential ignored |
| --- | --- | --- |
| `POST /api/v1/auth/refresh`, `POST /api/v1/auth/sign-out` | `__Secure-rt` cookie (browser) or refresh token in the body (machine); origin checks of §2.3 apply to the cookie path | `Authorization` header |
| `POST /api/v1/auth/mfa/verify` | the pending session's cookie | — |
| every other endpoint | `Authorization: Bearer <access token>` | any cookie; a cookie-only request is `401` |
| `[AllowAnonymous]` endpoints (sign-in, credential challenge, health, JWKS) | none | both |

### 4.4 Principal construction (one query per request) `[C-04, C-21]`

```sql
-- identity.resolve_session(session_id uuid, user_id uuid) — the one read on every authenticated request
-- $1 = the plain "sid" claim; $2 = the decrypted "sub" (D-02); a mismatch returns no row → 401 AUTH-011
SELECT s.id AS session_id, s.kind, s.scope, s.expires_at_utc, s.absolute_expires_at_utc, s.revoked_at_utc,
       u.id AS user_id, u.display_name, u.is_active, s.reauthenticated_at_utc,
       s.tenant_id, m.role_id, r.name AS role_name,
       array_agg(rp.permission_key) FILTER (WHERE rp.permission_key IS NOT NULL) AS permissions,
       m.branch_ids, m.department_ids, m.preferred_language
FROM identity.refresh_sessions s
JOIN identity.platform_users u ON u.id = s.user_id
LEFT JOIN app.tenant_memberships m ON m.tenant_id = s.tenant_id AND m.user_id = u.id AND m.is_active
LEFT JOIN app.roles r ON r.tenant_id = m.tenant_id AND r.id = m.role_id
LEFT JOIN app.role_permissions rp ON rp.tenant_id = r.tenant_id AND rp.role_id = r.id
WHERE s.id = $1 AND s.user_id = $2 AND s.revoked_at_utc IS NULL
GROUP BY s.id, u.id, m.role_id, r.name, m.branch_ids, m.department_ids, m.preferred_language;
```

This query runs under elevation (it must read `app.*` before the tenant is known) inside the authentication handler, which is one of the three allow-listed elevation sites (`ElevationScopingTests`). Its result populates `ICurrentUser`, `ICurrentTenant`, `IUserPrivileges` and the display name for audit attribution (as of the action). Cache: per session id, ≤ 10 s, evicted by `SessionRevoked`, `MembershipChanged`, `RoleChanged`, `UserDeactivated` events.

### 4.5 MFA and step-up

- **MFA is a capability; whether it is required is an application-level policy, not a platform mandate.** TOTP (RFC 6238) enrolment, the `mfa_pending` and `mfa_enrollment` session scopes, `MfaEnrollmentGateMiddleware` (which, when enrolment is being forced, admits only the enrolment endpoints) and recovery codes ship with the platform. `Security:Mfa:Policy` selects `Optional` (users may enrol), `RequiredForAll`, or `RequiredForPermissions` (required for members holding any of a listed set of permission keys); the business or the tenant sets it once the domain is known. Every policy value is tested; the plumbing is identical under all of them.
- Recovery codes: 10 single-use, Argon2id-hashed, regenerable by the user after MFA.
- **Step-up re-authentication is a capability; which operations require it is configuration, not a platform mandate.** `POST /api/v1/auth/reauthenticate` takes the enveloped password, verifies it with the normal lockout rules, and stamps `reauthenticated_at_utc` on the session row. A command marked `[RequireRecentAuthentication]` is refused with `403 AUTH-STEPUP-REQUIRED` when the stamp is older than `Security:StepUp:MaxAge` (default 5 minutes); the SPA shows the re-authenticate dialog and retries. The set of operations that carry the marker is `Security:StepUp:Operations` (empty by default); account-security operations (password change, MFA reset, recovery-code regeneration, machine-credential creation, changing another member's role) are the **recommended** entries, and business modules add their own through the intake (`03 §6.3`). Whether an administrator action on another user's credentials also requires the administrator's own TOTP code is `Security:StepUp:AdminActionsRequireMfa` (default off until the MFA policy is chosen).

### 4.6 The encrypted subject claim (D-02, normative)

Ratified by D-02: the user id is encrypted before it enters `sub`; `email` is removed (and `name`, `role` with it). The session id travels as the plain `sid` claim (§4.2) so the session row is found without decrypting. Specification:

| Item | Value |
| --- | --- |
| Algorithm | AES-256-GCM |
| Key | `Jwt:SubjectEncryptionKey`: 32 random bytes, base64; distinct from the signing key; refused at start-up if absent or not 32 bytes |
| Nonce | 96-bit, fresh per token (CSPRNG) |
| Plaintext | the 16 bytes of the user id (`Guid.ToByteArray()`, RFC 4122 byte order) |
| AAD | `platform:sub:v1` ‖ `kid` of the signing key (binds the ciphertext to this token's key epoch) |
| Encoding | `v1.<kid>.<base64url(nonce ‖ ciphertext ‖ tag)>` where `kid` = first 8 hex of SHA-256(key); 69 characters |
| Rotation | `Jwt:PreviousSubjectEncryptionKeys[]` accepted for decryption for at least one access-token lifetime; new tokens always use the active key; runbook: add the new key as active, keep the old one in the previous list for 24 hours, then remove it |
| Decryption | in `OnTokenValidated`, after signature and lifetime checks; the claim is replaced with the plaintext id in the principal |
| Refusals | missing, plaintext GUID, unknown `kid`, tag failure → `401 AUTH-010`; never retried |
| Unlinkability | two tokens for one user have different `sub` values (fresh nonce) |
| Threat it addresses | a token holder learning the user id; **not** forgery (signing is separate) |
| Cost | one AES-GCM decryption per request; one more secret and rotation runbook; every pre-deployment token refused at cut-over |

The alternative weighed (`sub` = session id with no encryption, `00 C-03`) is recorded in document 00; under D-02 the session id travels as `sid` and the user id as the encrypted `sub`, so both the lookup and the privacy property hold.

---

## 5. Rate limiting matrix `[C-13]`

### 5.1 Principles

1. **Partition by what the attack targets**: an account (credential guessing), an actor (step-up attempts, mutation floods), a client address (scraping), a tenant (resource exhaustion).
2. **Sliding window** (or token bucket for bursty legitimate traffic), never fixed window.
3. **A 429 never counts toward any lockout.** Lockout counts *failed credential checks*; a throttled request checked nothing.
4. **Counters live in a shared store** when replicas > 1 (PostgreSQL `UNLOGGED` counters or Redis); otherwise in-process, with limits documented as per-replica.
5. **Health, readiness and metrics are outside the limiter.**
6. **Rejections** are `429` `application/problem+json` `RATE-429` with `Retry-After` seconds, a `RateLimit-Policy` header naming the policy, and a metric by policy. No security event per rejection (volume), one `RATE_LIMIT_SUSTAINED` event when a partition is rejected for > 60 s continuously.

### 5.2 Policies

| Policy | Endpoints | Partition key | Algorithm | Limit | Queue | On reject |
| --- | --- | --- | --- | --- | --- | --- |
| `global` | everything under `/api` | client IP (after forwarded headers) | sliding, 1 min, 6 segments | 600/min | 0 | 429 |
| `tenant-global` | everything authenticated | tenant id | sliding, 1 min | 3,000/min (plan-dependent) | 0 | 429 |
| `credential-account` | `POST /auth/sign-in`, `POST /auth/mfa/verify`, `POST /auth/password/*`, `POST /auth/reauthenticate`, `POST /auth/recovery` | `sha256(lower(tenant + ":" + email))` — **independent of IP** | sliding, 10 min | 20/10 min | 0 | 429; **also** the lockout counter still applies to *checked* failures only |
| `credential-client` | same endpoints | client IP | sliding, 1 min | 60/min | 0 | 429 (an office of 150 users behind one NAT address is not throttled by this; a single scripted attacker is) |
| `credential-challenge` | `GET /auth/credential-challenge` | client IP | sliding, 1 min | 120/min | 0 | 429 (it is not a credential check, so it never consumes the credential budgets) |
| `refresh` | `POST /auth/refresh` | `sid` from the presented token (fallback: client IP) | sliding, 1 min | 30/min | 0 | 429 (a misbehaving tab; a healthy tab refreshes about every 15 minutes) |
| `machine-token` | `POST /auth/token`, `POST /auth/token/refresh` | client id | sliding, 1 min | 30/min | 0 | 429 |
| `step-up` | `POST /auth/reauthenticate`; administrator credential actions on other users | **user id** (resolved from the session, never from a token claim) | sliding, 1 min | 10/min | 0 | 429; the password lockout counts only *checked* wrong passwords |
| `mutation` | every non-GET authenticated request not covered above | user id | token bucket: 120 capacity, 2/s refill | 0 | 429 |
| `upload` | `POST /files` | user id | token bucket: 20 capacity, 1 per 3 s refill; plus body size 50 MB | 0 | 429 / 413 |
| `export` | `POST /exports/*`, `GET /reports/*/pdf` | tenant id | **concurrency**: 2 in flight, queue 4 | 4 | 429 after queue |
| `platform-admin` | `/platform/*` | user id | sliding, 1 min | 120/min | 0 | 429 |
| exempt | `/health/*`, `/metrics`, `/.well-known/jwks.json` | — | — | — | — | — |

Policies chain: a request must pass `global`, then its endpoint policy, then `tenant-global` where authenticated (`PartitionedRateLimiter.CreateChained`).

### 5.3 Implementation sketch

```csharp
options.AddPolicy(Policies.CredentialAccount, ctx =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: AccountKey(ctx),                         // sha256(tenant:email) from the body-bound request, or "unknown"
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = settings.CredentialAccountPer10Min,  // 20
            Window = TimeSpan.FromMinutes(10),
            SegmentsPerWindow = 10,
            QueueLimit = 0,
        }));

options.AddPolicy(Policies.StepUp, ctx =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: ctx.ResolvedUserId()?.ToString() ?? ClientIp(ctx),   // from the session, §4.4
        factory: _ => new SlidingWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));

options.OnRejected = (ctx, _) =>
{
    ctx.HttpContext.Response.Headers.RetryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? ((int)ra.TotalSeconds).ToString() : "60";
    ctx.HttpContext.Response.Headers["RateLimit-Policy"] = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
    Metrics.RateLimitRejected.Add(1, new("policy", ...));
    return ProblemResponse.WriteAsync(ctx.HttpContext, 429, "Too many requests.", "RATE-429");
};
```

The account key for `credential-account` needs the request body's identifier before model binding; the limiter middleware reads a small, size-capped (4 KB), buffered copy of the body for the credential endpoints only, or the endpoints accept the identifier as a route value. Either way the key is a hash, never the e-mail.

### 5.4 Ordering

`UseForwardedHeaders → Observability → SecurityHeaders → ExceptionHandler → Authentication (JWT bearer; OnTokenValidated decrypts sub and loads the session) → RateLimiter → AuthOriginCheck → TenantResolution → MfaEnrollmentGate → Authorization → endpoints (CredentialTransportFilter runs as a global action filter before model validation)`. The limiter runs after authentication so actor-keyed policies can key on the resolved user; `PipelineOrder` is a constant asserted by `WebApiHardeningTests`.

---

## 6. Credential storage and policy `[C-15]`

### 6.1 Passwords

| Item | Value |
| --- | --- |
| Hash | Argon2id, memory 64 MiB, iterations 3, parallelism 1, salt 16 bytes, tag 32 bytes, PHC string format; parameters pinned in code and asserted by a test; a rehash-on-login path upgrades old parameters |
| FIPS alternative | PBKDF2-HMAC-SHA256, 600,000 iterations, only when contractually required (recorded in the ADR) |
| Policy (shipped defaults; every value is tenant-configurable) | min 12 characters; no composition rules; deny list (breached-password set, local); not equal to e-mail or display name; history 12; max age 180 days configurable per tenant (0 = no expiry, allowed when MFA is on) |
| Lockout | 5 consecutive failed **checks** → 30 minutes; counter resets on success; 429s do not count; `USER_LOCKED_OUT` event |
| Reset | administrator-issued one-time link (24 h), forces change on first use, revokes other sessions |

### 6.2 MFA secrets and recovery codes

TOTP secret encrypted at rest with ASP.NET Data Protection (purpose string `mfa-secret`), key ring in PostgreSQL, protected by a master key in the secret store. Recovery codes Argon2id-hashed (lighter parameters), single use.

### 6.3 Machine credentials

| Item | Value |
| --- | --- |
| Shape | client id (public) + client secret (32 random bytes, base64url), or a personal access token for a user's scripts |
| Storage | secret shown once at creation; stored Argon2id-hashed (lighter parameters, the secret is random); `identity.machine_credentials` with scopes, expiry (default 1 year), `revoked_at_utc` |
| Creation | requires the permission the catalogue assigns (placeholder key `integrations.manage`) and step-up if the operation is listed in `Security:StepUp:Operations`; writes `MACHINE_CREDENTIAL_CREATED` |
| Use | `POST /auth/token` → access token + refresh token (kind = machine, §4.3); the `machine-token` rate limit applies |
| Rotation | create the successor, update the client, revoke the predecessor; no overlap enforced by the platform beyond both being valid until revoked |

### 6.4 Secrets in configuration

| Secret | Form | Guard |
| --- | --- | --- |
| `Jwt:SubjectEncryptionKey` (D-02) | 32 random bytes base64 | refuse if absent or not 32 bytes |
| `Jwt:SigningKey` | P-256 private key (PEM or JWK) | refuse if not loadable as ES256 |
| `DataProtection:MasterKey` | 32 random bytes | refuse if absent |
| `ConnectionStrings:Postgres` | runtime role credentials | refuse if role over-privileged |
| `Jwt:PreviousSubjectEncryptionKeys[]` (rotation overlap) | 32 random bytes each | refuse any entry that is not 32 bytes |

No secret in `appsettings*.json`; environment variables or a secret store; `deploy/SECRETS.md` lists each with a generation command.

---

## 7. Security event catalogue

Every event is appended to `audit.security_events` (append-only, hash-chained with the audit trail), carries `tenant_id` (nullable before authentication), `actor_user_id` (nullable), `client_ip`, `user_agent_hash`, `correlation_id`, a `detail` that **never contains credential material**, and is visible in the tenant's security log (platform-level events only to platform administrators).

| Event | When | Severity |
| --- | --- | --- |
| `SIGN_IN_SUCCEEDED` / `SIGN_IN_FAILED` | credential check outcome (failure reason category, never which field) | info / warning |
| `MFA_ENROLLED` / `MFA_VERIFIED` / `MFA_FAILED` / `RECOVERY_CODE_USED` | | info / warning |
| `USER_LOCKED_OUT` | lockout counter | warning |
| `SESSION_CREATED` / `SESSION_REVOKED` (reason) / `SESSION_CONTEXT_CHANGED` | lifecycle | info |
| `REFRESH_REUSE_DETECTED` | a rotated or revoked refresh token was presented; the family is revoked (§4.3) | **error** |
| `CSRF_REJECTED` | §2.3 | warning |
| `TOKEN_SUBJECT_REJECTED` / `TOKEN_SESSION_MISMATCH` | §4.6 (`AUTH-010`), §4.4 (`AUTH-011`) | warning |
| `CREDENTIAL_ENVELOPE_REJECTED` (code) | §3 | warning |
| `RATE_LIMIT_SUSTAINED` (policy, partition hash) | §5 | warning |
| `STEP_UP_SUCCEEDED` / `STEP_UP_FAILED` | every re-authentication attempt (§4.5) | info / warning |
| `MACHINE_CREDENTIAL_CREATED` / `MACHINE_CREDENTIAL_REVOKED` | §6.3 | info |
| `PRIVILEGE_MATRIX_CHANGED` / `MEMBERSHIP_CHANGED` / `USER_DEACTIVATED` | administration | info |
| `ELEVATION_USED` (site) | every `Elevate()` outside the three allow-listed sites would be a test failure; the allow-listed ones log at debug | debug |
| `SECURITY_RELAXATION_ACTIVE` | start-up with any relaxation flag | **error** |
| `ROLE_GUARD_VIOLATION` | runtime role over-privileged (process refuses to start; the event is written if the database is reachable) | **error** |

---

## 8. Verification

| Control | Test | Evidence id |
| --- | --- | --- |
| HSTS and security headers on every response including errors | `SecurityHeadersTests` (functional) | SEC-E01 |
| CSRF on `/auth/refresh`: cross-origin `POST` with cookie refused; same-origin without header refused; same-origin with header accepted | `CsrfTests` (functional), Playwright negative case | SEC-E02 |
| Refresh cookie flags (`__Secure-rt`, `Path=/api/v1/auth`, `HttpOnly`, `Secure`, `SameSite=Strict`) | `RefreshSessionTests` | SEC-E03 |
| Sliding and absolute expiry; revocation visible on the next request and on the next refresh | `RefreshSessionTests` with `IClock` control | SEC-E04 |
| Cookie-only request to a data endpoint → 401; rotation issues a successor; reuse of a rotated token revokes the family | `RefreshSessionTests` | SEC-E05 |
| Access token: three segments, ES256, no `email`/`name`/`role`, `sub` opaque and different per token, `sid` present, lifetime ≤ 15 min; plaintext, tampered or unknown-`kid` `sub` → 401 | `AccessTokenClaimsTests`, `SubjectProtectorTests` | SEC-E06 |
| Principal is rebuilt per request; a revoked privilege is gone within the cache window | `PrivilegeResolutionTests` | SEC-E07 |
| Rate limits per policy, 429 shape, `Retry-After`, no lockout increment on 429 | `RateLimitTests` with swapped settings | SEC-E08 |
| Lockout after 5 checked failures; 429s and envelope refusals never increment it; step-up failures count as password failures | `LockoutTests` | SEC-E09 |
| Argon2id parameters pinned; rehash-on-login | `PasswordHasherTests` | SEC-E10 |
| Envelope (D-03): each `CRED-*` code; plaintext refused with `CRED-005` in every environment; replay across replicas refused (two factories, one database); lockout counters unchanged | `CredentialEnvelopeTests`, `CredentialFieldTests` | SEC-E11 |
| Guards run in every environment name | `ConfigGuardTests` | SEC-E12 |
| Security events written for every row in §7 | `SecurityEventTests` | SEC-E13 |
| Independent penetration test | external | finding SEC-001 equivalent; open until evidence |

Every functional test above runs under `WebApplicationFactory` with a real PostgreSQL for the session and RLS paths; counts are reported per suite in the run log, never as a bare total.
