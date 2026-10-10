# 00 — Critical Architectural Challenge, Alternatives and Ratified Decisions

| Field | Value |
| ----- | ----- |
| Scope | Greenfield high-security B2B enterprise SaaS ("Version 2") whose proposed baseline is the NT.QAMS platform as built at `59d5106` (develop, 2026-10-06) |
| Audience | Solution architects, lead developers, security lead |
| Status | **Ratified for Phase 1.** The critique (2026-10-07) was reviewed by leadership and engineering stakeholders; six decisions were ratified on 2026-10-08 (§0). On the same date leadership clarified the commercial scope: V2 is **not** a sector-regulated product, so the baseline's regulatory layer is not carried forward (D-07). The challenges remain in this document as the record of what was weighed |
| Evidence base | The NT.QAMS source, ADR-0001 … ADR-0012, CR-002, `SCHEMA-HARDENING-REPORT.md`, `NT_QMS_Database_AsBuilt.md`, the architecture test suite. Every claim about the baseline cites a file |
| Companion documents | `01-SYSTEM-ARCHITECTURE-BLUEPRINT.md`, `02-SECURITY-AND-IDENTITY-SPECIFICATION.md`, `03-BUSINESS-INTAKE-AND-DDD-GUIDE.md`, `04-ARCHITECTURAL-PITFALLS-AND-AVOIDANCE-GUIDE.md`, `README.md` |

This document is the one place in the pack that argues and records decisions. The other four specify.

---

## 0. Ratified decisions — these govern documents 01 to 04

| Id | Decision | Resolves | Realised in the pack as | Residual risk recorded |
| --- | --- | --- | --- | --- |
| **D-01** | **Browser authentication: access JWT in SPA memory (signals) + rotating refresh token in an `HttpOnly` cookie against the database-backed session table** | C-01, C-05 | `02 §4.1–4.3`, `01 §6.1/6.4`; access-token lifetime **15 minutes** (specification default), refresh rotation with family reuse detection | Page code (XSS) can use the in-memory token for ≤ 15 minutes; mitigated by CSP, per-request session check, short lifetime |
| **D-02** | **`sub` encrypted with AES-256-GCM before inclusion in the token; `email` removed** | C-03 | `02 §4.2`, `02 §4.6`; `name` and `role` are also removed and resolved per request (`02 §4.4`) | A new secret (`Jwt:SubjectEncryptionKey`) with a rotation runbook |
| **D-03** | **Credential envelope mandatory in the WebApi core: compact JWE RSA-OAEP-256 + A256GCM with 120-second bound nonces on every credential-bearing contract** | C-02 | `02 §3`; `[CredentialField]` on every password property, opened by a global action filter; **no `Optional` mode** (test harnesses seal like the browser); nonce replay set and envelope keys in a shared PostgreSQL store | Does not protect against code running in the page; one extra round trip on credential submission |
| **D-04** | **Deny-by-default on commands and queries: every request handler carries an explicit permission policy; an architecture test fails the build otherwise** | C-06 | `01 §3.4`, `01 §8` (`RequestPolicyTests`), `03 §9`, `04 P-26` | None |
| **D-05** | **Runtime: .NET 9 (C# 13), EF Core 9, PostgreSQL 17; LTS upgrade on the roadmap** | C-17 | All version references in 01–04 | **.NET 9 is a Standard-Term-Support release whose Microsoft support ended on 2026-05-12; no further security patches are issued for it.** Accepted for Phase 1 for library parity; the upgrade date is action A-1 |
| **D-06** | **Identity split: `platform_users` (global credentials) and `tenant_memberships` (tenant profile and role) under `FORCE RLS` with `(tenant_id, id)` composite keys** | C-11 | `01 §4.7`, `02 §4.4`, `04 P-30` | Sign-in selects a tenant when a user holds several memberships |
| **D-07** | **Scope: V2 is a high-security B2B enterprise SaaS whose business domain is not yet known, not a sector-regulated system. The baseline's regulatory layer is not carried forward: electronic-signature ceremonies (password + PIN re-prompt, meaning statements), the PIN credential, the sector-standard traceability and the validation document set. The platform ships domain-agnostic building blocks (permissioned commands, an optional `Approval` value object, SoD helpers, a frozen-state immutability trigger, the append-only tamper-evident audit trail and domain events); no business rule is hardcoded; the business analyst defines entities, transitions, approval gates and policies per domain (document 03)** | C-21 (new) | `01 §3.3`, `01 §4.8`, `02 §4.5` (capabilities, not mandates), `03` (meta-framework), `04 P-02` | A customer in a regulated sector would need the removed layer back as a separately priced module; the extension points are kept so that is additive |

Dispatcher: D-04's wording keeps **MediatR**; C-12 (in-house dispatcher) is the recorded alternative and the licence check is action A-2.

---

## 0.1 Verdict matrix (with ratification status)

| # | Baseline element | Verdict (2026-10-07) | Status (2026-10-08) |
| --- | --- | --- | --- |
| 1 | Clean Architecture, six assemblies, CQRS markers, pipeline behaviors | Keep | Adopted |
| 2 | Repositories per aggregate + `IUnitOfWork`, no EF in Application (ADR-0010) | Keep | Adopted |
| 3 | Domain-held invariants, guarded state machines, SoD inside aggregates | Keep | Adopted |
| 4 | Electronic-signature ceremonies, PIN credential, validation document set, sector-standard traceability | Keep (as a regulated product) | **Not carried forward: D-07** |
| 5 | Shared schema + `FORCE RLS` + composite tenant FKs + fail-closed nil tenant | Keep | Adopted |
| 6 | Composite PK `(tenant_id, id)` | Keep, with a convention | Adopted (**D-06** confirms) |
| 7 | Per-migration `SET LOCAL app.bypass_rls` | Replace: migration role `BYPASSRLS` | Adopted |
| 8 | `user_account` outside RLS, nullable tenant (B9) | Replace: platform user + RLS membership | **RATIFIED D-06** |
| 9 | Transactional outbox (ADR-0006) | Keep | Adopted |
| 10 | Deny-by-default command authorization | Keep and extend to queries | **RATIFIED D-04** |
| 11 | MediatR | Decide (licence) | Kept; licence check A-2 |
| 12 | Same origin, no CORS, proxy serves SPA and `/api` (ADR-0007) | Keep | Adopted |
| 13 | In-memory JWT + rotating refresh cookie (ADR-0009) | Replace for the browser with an opaque session cookie | **Overridden: RATIFIED D-01 keeps JWT + refresh rotation**; alternative recorded in C-01 |
| 14 | HS256 with a string secret | Replace | Adopted: **ES256**, key guard |
| 15 | Field-level AES-GCM encryption of `sub` (ADR-0012 Part B) | Replace with `sub` = session id | **Overridden: RATIFIED D-02**; alternative recorded in C-03 |
| 16 | `role` claim in token, checked against DB each request | Remove | Adopted: resolved per request |
| 17 | 120-minute access token | Reject | Adopted: 15-minute default under D-01 |
| 18 | Credential envelope, Option 1A | Do not carry into the core | **Overridden: RATIFIED D-03** |
| 19 | `Mode=Optional` by environment name | Reject | Adopted: no `Optional` mode anywhere |
| 20 | Fixed-window IP-keyed auth limiters | Replace | Adopted (`02 §5`) |
| 21 | In-process correctness-bearing state | Replace with shared store | Adopted; applies to the D-03 nonce set and keys |
| 22 | Two queries per request for session and privileges | Collapse to one | Adopted (`02 §4.4`) |
| 23 | `X-Change-Reason` HTTP header | Replace with typed command fields | Adopted |
| 24 | PBKDF2 via Identity hasher | Replace with Argon2id | Adopted |
| 25 | Hash chain without writer lock | Verify; serialize per tenant | Adopted (`01 §4.8`, `04 P-53`) |
| 26 | .NET 9 / C# 13 / EF Core 9 / PostgreSQL 17 | Replace with .NET 10 LTS / PostgreSQL 18 | **Overridden: RATIFIED D-05**; support-status risk recorded in §0 |
| 27 | Angular 22 with zone, `APP_INITIALIZER`, `withXhr()` | Modernise to zoneless, signals, `resource()` | Adopted (`01 §6`) |
| 28 | Flat frontend, 2,242-line `models.ts`, in-code dictionary | Restructure | Adopted (`01 §6`) |
| 29 | ADR governance, executable gates, per-suite test evidence | Keep | Adopted |

---

## 1. Where the baseline shines (carried forward unchanged)

### 1.1 Tenant isolation is three independent, verifiable layers

- **Application layer:** an EF global query filter on every `ITenantScoped` entity, applied by convention, composed with the per-user branch/department scope.
- **Engine layer:** `FORCE ROW LEVEL SECURITY` with a `tenant_isolation` policy on 90 of 96 tables, reading a per-connection GUC; an unresolved tenant stamps the nil UUID, which matches no row (fail-closed).
- **Schema layer:** 29 tenant-composite foreign keys and tenant-first composite primary keys on 88 tables.
- **Deployment guard:** `DatabaseRoleGuard` refuses to boot when the runtime role is `SUPERUSER`, `BYPASSRLS` or a table owner.
- **Elevation is explicit, scoped and disposable**; `ElevationScopingTests` police it.

### 1.2 Authorization is deny-by-default and compile-gated

`AuthorizationBehavior` throws `AUTHZ-000` for a command without a policy attribute; `CommandPolicyTests` makes the omission a build failure; `PermissionCatalog` is code-defined; an unknown key fails loudly. D-04 extends exactly this mechanism to queries.

### 1.3 The domain protects itself

Guarded state machines with private setters, factories and coded refusals; SoD as aggregate methods (the baseline's "creator is not the approver" guard with coded refusals); `DomainException` with a `DomainFault` kind so the HTTP layer never infers semantics from code spelling.

### 1.4 A transition and what it records commit together

In the baseline a recorded decision is **staged** and the handler owns the single `SaveChangesAsync`, so a record of a decision without its transition cannot be persisted; engine-level triggers make frozen rows immutable. V2 keeps the property as a building block (one commit per command; an immutability trigger available for any state the domain declares frozen) without the baseline's ceremony.

### 1.5 The outbox is already scale-safe

ADR-0006: same-transaction rows, `FOR UPDATE SKIP LOCKED` under a lease, exponential back-off with jitter, dead-letter with a metric, idempotent consumers, advisory-lock leader election.

### 1.6 The gates are executable

Sixteen architecture-test classes, API surface snapshots, migration script tests, interceptor-order tests, and a run log that forbids claiming a test result nobody watched.

---

## 2. Challenges (the record of what was weighed)

### C-01 — Browser authentication

**Baseline.** ADR-0009: a 15-minute (shipped: 120-minute) HS256 JWT in an Angular signal plus a rotating refresh token in an `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` cookie; silent refresh at start-up; single-flight refresh on 401.

**Why challenged.** Once the deployment is same-origin and cookie-capable, an opaque server-side session referenced by an `HttpOnly` cookie needs no bearer credential in JavaScript and no refresh dance; revocation is exact. Cost: CSRF becomes first-class; non-browser clients need a token path.

| Option | Browser holds | CSRF exposure | Token inspection | Revocation | Complexity |
| --- | --- | --- | --- | --- | --- |
| A. JWT in memory + refresh cookie (baseline) | Access token in JS | `/api/auth/refresh`, `/sign-out` only | Decodable; needs claim hygiene (D-02) | Per-request DB check | High |
| B. Opaque session cookie (recommended 2026-10-07) | Nothing readable | Every mutation; `SameSite` + `Origin` + header | None | Exact | Low |
| C. Full BFF with internal JWT | Nothing readable | As B | Internal only | Exact | Medium |
| D. Whole-token JWE | Encrypted token in JS | As A | None; tools lose visibility | As A | High |

**Decision D-01: Option A.** Consequences: ES256 signing (C-04), 15-minute lifetime (C-05), no `role`/`name`/`email` claims, `sub` encrypted (D-02), refresh cookie `__Secure-rt` scoped to `/api/v1/auth` with `SameSite=Strict`, rotation with family reuse detection. Option B is retained as the recorded alternative for a future revisit trigger.

### C-02 — The credential envelope

**Baseline.** ADR-0012 Part A. CR-002 records that engineering recommended TLS only.

**What it achieves.** Hides passwords from the Network tab, proxy and WAF logs, and TLS-terminating intermediaries (common in enterprise networks). It does not protect against code running in the page; a stolen access token remains a bearer credential.

**Why challenged.** Per-process keys and nonce cache pin the topology; `Optional` mode by environment name; one extra round trip; custom protocol surface; passkeys are the strategic answer to "no password on the wire".

| Option | Hides from Network tab / inspecting proxy | Hides from page code | Topology cost | Protocol surface |
| --- | --- | --- | --- | --- |
| 1A envelope | Yes | No | None **if** nonces and keys are in a shared store | Yes |
| 1D TLS only | No | No | None | None |
| Passkeys for sign-in | Yes | Yes | None | Standard (WebAuthn) |

**Decision D-03: Option 1A, mandatory in the core.** Every `[CredentialField]` property enveloped; global action filter; 120-second MAC'd nonces consumed once in a shared `UNLOGGED` table; RSA-3072 key pair rotated every 24 hours and stored protected so all replicas accept the same challenges; **no `Optional` mode** (C-07). With D-07 the credential set is passwords only (no PIN). Passkeys stay on the roadmap with no gate other than product priority (`02 §3.9`).

### C-03 — Encrypting `sub`

**Baseline.** ADR-0012 Part B: AES-256-GCM over the 16-byte user id, fresh nonce per token, decrypted in `OnTokenValidated`.

**Why challenged.** The requirement is non-disclosure and unlinkability; `sub` = session id gives both without a key or a rotation runbook, and the per-request session check already pays the lookup.

**Decision D-02: encrypted `sub`, `email` removed.** The design is cryptographically sound (fresh 96-bit nonce, constant AAD, fixed 16-byte plaintext, `kid` for rotation, signing separate). Consequences: `Jwt:SubjectEncryptionKey` with a previous-keys overlap; `sid` travels as a **plain** claim so the session row is found without decrypting; `name` and `role` also removed. The session-id-as-`sub` design is recorded in `02 §4.6` as the alternative.

### C-04 — HS256 with a string secret, and a `role` claim that exists only to be checked

**Adopted (within D-01).** ES256 (P-256) with `kid` and a JWKS document; the start-up guard refuses a key that is not a valid P-256 private key; no `role` claim. No new library: `Microsoft.IdentityModel.Tokens` in the .NET 9 baseline supports ES256.

### C-05 — Access-token lifetime

**Adopted (within D-01).** 15 minutes. The refresh design makes lifetime invisible to users, so the shortest value the refresh traffic allows is the right one; a gate fails when any committed configuration exceeds it.

### C-06 — Queries are not gated

**Decision D-04.** Every `ICommand<>` and `IQuery<>` carries exactly one policy attribute; `RequestPolicyTests` fails the build otherwise; read-only reviewer roles become `*.view` grants rather than a role exemption.

### C-07 — Security posture must never depend on an environment name

**Adopted.** One behaviour in every environment; relaxations are explicit, named flags that refuse non-loopback binds and write `SECURITY_RELAXATION_ACTIVE`.

### C-08 — In-process state that bears on correctness

**Adopted.** Every singleton's state is classified cache / per-replica-tolerable / correctness-bearing; the D-03 nonce set, the envelope keys, the session store, idempotency responses and outbox claims live in PostgreSQL.

### C-09 — Composite primary keys

**Adopted (confirmed by D-06).** Keep `(tenant_id, id)`; apply by one model convention; gate on single-column keys. PostgreSQL heap tables do not cluster on the PK, so the usual "wide clustering key" objection does not apply; the real cost is EF tooling, solved by the convention.

### C-10 — Migration role with `BYPASSRLS`

**Adopted.** The migration role has `BYPASSRLS`; the runtime role does not; a migration containing `app.bypass_rls` fails `MigrationScriptTests`.

### C-11 — Platform user and RLS-protected membership

**Decision D-06.** `identity.platform_users` and `app.tenant_memberships` (`FORCE RLS`, PK `(tenant_id, id)`). A consultant or a group administrator can hold memberships in several tenants under one credential; sign-in selects the tenant.

### C-12 — MediatR

**Kept.** The subset used is small. Action A-2: confirm the licence terms of the pinned version before the first release; the in-house dispatcher remains the fallback.

### C-13 — Rate limiting

**Adopted.** Sliding windows; credential partition keyed by `(client IP, account hash)` with a per-IP backstop and an IP-independent per-account partition; token bucket for uploads; concurrency limiter for exports; 429 never counts toward lockout; counters in a shared store when replicas > 1.

### C-14 — Reasons and comments are command data, not headers

**Baseline.** A `X-Change-Reason` header enforced by middleware on `DELETE`.

**Adopted.** Where the business requires a reason (rejection, cancellation), it is a typed, validated property of the command, recorded in the domain event. Header-borne business data bypasses validation, OpenAPI and the API-surface snapshot. No cross-cutting "reason on every mutation" rule exists in V2 (D-07).

### C-15 — Password hashing

**Adopted.** Argon2id (parameters pinned and tested). With D-07 there is no PIN credential, so the separate PIN lockout design is withdrawn.

### C-16 — Audit-chain concurrency

**Adopted.** The tamper-evident hash chain stays as a security feature; appends are serialized per tenant with `pg_advisory_xact_lock`; a concurrency test is in the integration suite.

### C-17 — Platform currency

**Recommendation.** .NET 10 LTS, PostgreSQL 18. **Decision D-05 overrides.** The support status of .NET 9 is recorded in §0 with the required mitigation. PostgreSQL 17 is supported until November 2029.

### C-18 — Frontend shape

**Adopted.** Zoneless, signals, `resource()` reads, shared `mutation()` for writes, feature-sliced folders, generated API client, per-feature lazy i18n, `HttpParams`, no `any`.

### C-19 — Modular monolith boundary

**Adopted.** Layer assemblies kept; module namespaces mirrored in every layer; boundary tests per layer; one DbContext.

### C-20 — Smaller items

Adopted: snapshot-level enum CHECK test; idempotency body hash and TTL; tenant id as a span attribute only; proxy strips inbound `X-Forwarded-*`; connection string without `No Reset On Close`; no raw `NpgsqlConnection` outside Infrastructure.

### C-21 — The regulatory layer (new, 2026-10-08)

**Baseline.** NT.QAMS is a sector-regulated product: approvals are two-component signing ceremonies with meaning statements and canonical content hashes; a PIN is a second credential; records carry sector-standard traceability; a validation document set accompanies every change.

**Decision D-07.** V2 is not a regulated product and its business domain is not yet known. What stays, as **building blocks** that mandate nothing: guarded state machines, SoD helpers inside aggregates, one commit per command, an optional `Approval` value object, an immutability trigger for states a domain declares frozen, an append-only tamper-evident audit trail with field-level change history, security events, and typed reasons where a domain wants them. What goes: the ceremony (password + PIN re-prompt, meaning statements), the PIN credential and its lockout, the canonical signing manifest, the sector-standard citations and the validation document set. No business policy (MFA enforcement, step-up scope, thresholds, approval rules) is hardcoded; each is configuration or an intake decision. The extension points are kept so a regulated edition could be added as a module later.

---

## 3. What changes in the structure (detail in 01)

1. Module-first namespaces mirrored across all six assemblies; boundary tests in every layer.
2. Identity split (D-06): `platform_users`, `tenant_memberships`, `refresh_sessions`.
3. One security edge in WebApi: JWT bearer (ES256, encrypted `sub`) + refresh endpoint with cookie (D-01, D-02); mandatory credential transport filter (D-03); store-backed rate limiting; origin check on the cookie-bearing endpoints.
4. Request pipeline on MediatR: Tracing → Logging → Authorization (commands **and** queries, D-04) → Validation → Idempotency → Handler.
5. Persistence: one DbContext, convention-applied composite keys and filters, migration role with `BYPASSRLS`, approved-record immutability trigger, tamper-evident audit chain.
6. Frontend: zoneless, feature-sliced, generated API types, lazy i18n, auth and envelope interceptors.
7. Domain-agnostic building blocks (D-07): permissioned commands, an optional `Approval` value object, SoD helpers, a frozen-state immutability trigger; MFA and step-up as configurable capabilities; business rules defined per domain through document 03.

---

## 4. Decision register (V2 ADR candidates, with status)

| ADR | Title | Status | Driven by |
| --- | --- | --- | --- |
| V2-ADR-001 | Platform: .NET 9 / C# 13, EF Core 9, PostgreSQL 17, Angular 22 zoneless; LTS upgrade roadmap with a dated commitment | **RATIFIED D-05** (risk recorded) | C-17, C-18 |
| V2-ADR-002 | Modular monolith with mirrored module namespaces and per-layer boundary tests | Adopted | C-19 |
| V2-ADR-003 | MediatR as the dispatcher; licence confirmation before release | Adopted (A-2 open) | C-12 |
| V2-ADR-004 | Browser sessions: ES256 access JWT (15 min) in SPA memory + rotating refresh token in `__Secure-rt` against `identity.refresh_sessions`; `sub` AES-256-GCM encrypted; no `email`, `name`, `role` claims | **RATIFIED D-01, D-02** | C-01, C-03, C-04, C-05 |
| V2-ADR-005 | Credential envelope mandatory in the WebApi core; shared nonce and key store; no `Optional` mode; passkeys roadmap | **RATIFIED D-03** | C-02, C-07, C-08 |
| V2-ADR-006 | Tenancy: shared schema, `FORCE RLS`, composite keys by convention, migration role `BYPASSRLS`, `platform_users` + `tenant_memberships` | **RATIFIED D-06** | C-09, C-10, C-11 |
| V2-ADR-007 | Authorization: deny-by-default on commands and queries; permissions code-defined | **RATIFIED D-04** | C-06 |
| V2-ADR-008 | Rate limiting: sliding windows, account-aware credential partitions, store-backed counters, 429 never counts toward lockout | Adopted | C-13, C-08 |
| V2-ADR-009 | Domain-agnostic building blocks: permissioned commands, optional `Approval` value, SoD helpers, frozen-state immutability trigger; no signing ceremony, no PIN; MFA and step-up as configurable capabilities; business rules defined per domain | **RATIFIED D-07** | C-21 |
| V2-ADR-010 | Credential hashing: Argon2id | Adopted | C-15 |
| V2-ADR-011 | Audit trail: append-only, hash-chained per tenant with serialized appends; field-level change history; security events | Adopted | C-16, C-21 |
| V2-ADR-012 | Outbox reliability model (ADR-0006 carried forward) | Adopted | §1.5 |
| V2-ADR-013 | Same-origin deployment, no CORS, TLS at the proxy, HSTS in-app (ADR-0002, ADR-0007 carried forward) | Adopted | §1 |

---

## 5. Stakeholder questions: answered and open

| # | Question (2026-10-07) | Status |
| --- | --- | --- |
| 1 | Does the "no plaintext password in network payloads" directive stand for V2? | **Answered: yes (D-03).** |
| 2 | Regulatory position on passkeys as a signature component | **Withdrawn by D-07.** Passkeys are a product roadmap item with no external gate |
| 3 | Will any client other than the same-origin SPA consume the API in the first two years? | **Open.** Phase 1 ships the machine-token path at minimum viable scope (`02 §4.2`) |
| 4 | Multi-tenant users at launch? | **Answered by D-06:** supported; tenant selection at sign-in is in Phase 1 |
| 5 | FIPS-validated cryptography | **Resolved as a capability, not blocking:** Argon2id is the default; a PBKDF2 mode is selectable by configuration if a customer contract requires it |
| 6 | Replica count target for the first production year? | **Resolved by design:** all correctness-bearing state is in PostgreSQL, so the answer no longer changes the architecture |
| — | **New (from D-05):** date of the .NET 10 LTS upgrade | **Open; must be set before first production deployment** (A-1) |
| — | **New (from C-12):** MediatR licence confirmation for the pinned version | **Open before first release** (A-2) |
