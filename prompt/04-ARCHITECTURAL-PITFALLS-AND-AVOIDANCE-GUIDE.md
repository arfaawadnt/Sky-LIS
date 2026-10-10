# 04 — Architectural Pitfalls and Avoidance Guide

| Field | Value |
| ----- | ----- |
| Scope | The catalogue of anti-patterns a reviewer must refuse, each with the symptom, the reason it is wrong in a secure multi-tenant enterprise system, how to detect it (grep, gate or test), and a "do this, not that" pair |
| Audience | Every developer and reviewer; the automated review agent reads it too |
| Source | Defects and near-misses recorded in the NT.QAMS history (`IMPLEMENTATION_LOG.md`, `SCHEMA-HARDENING-REPORT.md`, ADRs, CLAUDE.md "conventions that have already caused defects"), plus the challenges in document 00 |
| Governs | **Ratified decisions D-01 … D-07 (`00 §0`)** fix the "do this" side of P-02, P-21, P-24, P-25, P-26, P-30 and P-41 |
| Examples | Code samples use illustrative placeholder aggregates (`PurchaseOrder`, `WorkItem`, `Order`). The names carry no business meaning and mandate no rule; the business domain is defined later through document 03 |

Numbering: `P-xx`. The six pitfalls named in the brief are P-01, P-10, P-20, P-21, P-30 and P-40; the rest are the ones the baseline's history says will otherwise recur. Each entry ends with the gate that makes the review unnecessary.

---

## A. Persistence and the Application boundary

### P-01 — Leaking EF Core (`IQueryable`, `DbSet`, `DbContext`) into Application handlers

**Symptom.** A handler composes LINQ against a context port, returns `IQueryable`, or calls `Include`, `AsNoTracking`, `IgnoreQueryFilters`, `SaveChangesAsync`.

**Why it is wrong.** The ORM becomes the Application's public API; every query is a hidden dependency on provider behaviour; the interceptor chain (tenant stamp, audit, outbox) is bypassable; the tenant filter can be switched off from anywhere; nothing can be unit-tested without a provider. The baseline lived through this (ADR-0008 → ADR-0010, 60 repositories, 17 commits to fix).

**Detect.** `LayerRulesTests.Application_carries_no_EntityFrameworkCore`; grep `IQueryable` in `Platform.Application`; `InfrastructureHardeningTests` for `SaveChangesAsync` inside repositories.

```csharp
// NOT THAT — Application
public sealed class ListOrdersHandler(IAppDbContext db) : IQueryHandler<ListOrdersQuery, Paged<OrderListItem>>
{
    public async Task<Paged<OrderListItem>> Handle(ListOrdersQuery q, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Where(o => o.Status == q.Status)
            .Include(o => o.Lines).OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new OrderListItem(...)).ToPagedAsync(q.Page, ct);
}

// DO THIS — Application declares the need; Infrastructure owns the SQL
public interface IPurchaseOrderRepository : IRepository<PurchaseOrder>
{
    Task<Paged<OrderListItem>> ListAsync(PurchaseOrderStatus? status, PageRequest page, CancellationToken ct);
    Task<bool> NumberExistsAsync(string number, CancellationToken ct);
}

public sealed class ListOrdersHandler(IPurchaseOrderRepository orders) : IQueryHandler<ListOrdersQuery, Paged<OrderListItem>>
{
    public Task<Paged<OrderListItem>> Handle(ListOrdersQuery q, CancellationToken ct) =>
        orders.ListAsync(q.Status, PageRequest.Normalized(q.Page, q.PageSize), ct);   // the clamp stays here: a decision
}
```

### P-02 — Repositories that save, or handlers that save twice

**Symptom.** `repository.AddAsync(x)` that calls `SaveChangesAsync`; a handler that records an approval, saves, then performs the state change and saves again.

**Why it is wrong.** An approval and the state change it authorises must be one transaction (D-07). Two saves mean an approval record can exist for a transition that failed, or a transition without its approval. Repositories saving means a handler cannot stage several changes atomically.

**Detect.** `InfrastructureHardeningTests` (no `SaveChangesAsync` in `Repositories/`); `TransitionAtomicityTests` (exactly one save in a command handler).

```csharp
// NOT THAT
order.RecordApproval(actor, now);           // staged
await unitOfWork.SaveChangesAsync(ct);      // committed alone
order.AdvanceToApproved();                  // may throw: the approval is already persisted
await unitOfWork.SaveChangesAsync(ct);

// DO THIS — one aggregate method, one commit
order.Approve(actor, clock.UtcNow, comment);   // whatever the domain defined: guards, SoD, recorded metadata, state, event
await unitOfWork.SaveChangesAsync(ct);
```

### P-03 — `IgnoreQueryFilters()` as a convenience

**Symptom.** A query uses `IgnoreQueryFilters()` to "see everything" for an admin screen or a report.

**Why it is wrong.** It removes the application-layer tenant filter; RLS still protects, but the code now depends on a single layer, and the next developer copies the call into a path that runs elevated.

**Detect.** `ApplicationHardeningTests` counts `IgnoreQueryFilters` across Application, Infrastructure and WebApi against an allow-list with a reason per site.

### P-04 — Forgetting RLS on a new table (or declaring it elsewhere)

**Symptom.** A migration creates a tenant table and relies on "the RLS migration" to pick it up, or a later "activate RLS" migration enumerates tables.

**Why it is wrong.** The table is unprotected between deployments; a later enumeration misses it.

**Detect.** `MigrationScriptTests`: a migration that calls `CreateTable` for a table with `tenant_id NOT NULL` must contain `FORCE ROW LEVEL SECURITY` and `CREATE POLICY tenant_isolation` for that table. Integration: the RLS parity query returns zero rows.

```csharp
// DO THIS — same migration, after CreateTable
migrationBuilder.Sql("""
    ALTER TABLE app.purchase_orders ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.purchase_orders FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.purchase_orders
        USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid OR current_setting('app.bypass_rls', true) = 'on')
        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid OR current_setting('app.bypass_rls', true) = 'on');
    """);
```

### P-05 — `SET LOCAL` outside a transaction, or `set_config` with the wrong scope

**Symptom.** A migration backfills from a `FORCE RLS` table with a bare `SELECT set_config('app.bypass_rls','on', true)`; or an interceptor sets the GUC transaction-locally for reads EF does not wrap in a transaction.

**Why it is wrong.** Transaction-local settings outside a transaction are no-ops with a warning; the backfill silently updates zero rows (INF-001 in the baseline). In V2 the migration role has `BYPASSRLS`, so the incantation is unnecessary and its presence marks a copied habit.

**Detect.** `MigrationScriptTests`: no migration contains `app.bypass_rls`. Runtime stamping is session-scoped and re-applied on every open (`01 §4.5`).

### P-06 — Identifier truncation past 62 characters

**Symptom.** An index or constraint name built from long table and column names; PostgreSQL truncates at 63, EF at 62, silently and mid-word, so two indexes collide or a `DROP INDEX` targets the wrong name.

**Detect.** `MigrationScriptTests.NoIdentifierExceeds62` over the model snapshot; pin names with `HasDatabaseName()` and the abbreviation map.

### P-07 — `UNIQUE (id)` or a single-column key on a tenant table

**Symptom.** `HasAlternateKey(x => x.Id)` or an FK referencing `id` alone.

**Why it is wrong.** It breaks partition-readiness, and an FK to `id` alone cannot carry the tenant, so a child can point across tenants.

**Detect.** `ConventionTests` (every `ITenantScoped` type has key `(TenantId, Id)`); integration FK census (every FK between tenant tables includes `tenant_id`).

### P-08 — `ON DELETE CASCADE` as a substitute for tenancy

**Symptom.** Child tables with a single-column cascading FK and no `tenant_id`.

**Why it is wrong.** Cascade protects deletion integrity; it never isolates reads. The baseline replaced 28 such FKs with tenant-composite ones and added a shadow `tenant_id` to owned children.

**Detect.** Integration census: every table that has a parent in `app.*` carries `tenant_id NOT NULL` and a composite FK.

### P-09 — Enum stored as string without a domain `CHECK`, or a `CHECK` that drifts

**Symptom.** A new enum member added in C#; the database still has the old `ck_<table>_<col>_domain`; inserts fail in production only.

**Detect.** `CheckConstraintTests` compares every domain constraint in the snapshot with the enum's members; a migration is required for every enum change.

### P-10 — Domain rules in FluentValidation (or in the handler) instead of the aggregate

**Symptom.** `RuleFor(x => x.Status).Must(s => s == "Submitted")`, or a handler that checks `if (order.Status != PurchaseOrderStatus.Submitted) throw …` before calling a setter.

**Why it is wrong.** Validators run only for commands that have them and only on the HTTP path; a job, a policy or a test can call the aggregate directly and bypass the rule. The aggregate is the only place that is on *every* path. Validators are for shape (required, length, format). The baseline's predecessor had all its rules in the UI and the backend was a pass-through.

**Detect.** `DomainHardeningTests` (private setters, no public constructors, state changed only by methods); review: a validator referencing a state, a role or another record is refused.

```csharp
// NOT THAT — validator knows business state
public sealed class ApproveOrderValidator : AbstractValidator<ApproveOrderCommand>
{
    public ApproveOrderValidator(IPurchaseOrderRepository orders) =>
        RuleFor(x => x.OrderId).MustAsync(async (id, ct) => (await orders.GetStatusAsync(id, ct)) == PurchaseOrderStatus.Submitted)
            .WithMessage("Only submitted orders can be approved.");
}

// DO THIS — validator checks shape; the aggregate decides
public sealed class ApproveOrderValidator : AbstractValidator<ApproveOrderCommand>
{
    public ApproveOrderValidator() { RuleFor(x => x.OrderId).NotEmpty(); RuleFor(x => x.Comment).MaximumLength(1000); }
}

public void Approve(Guid actorId, DateTimeOffset at, string? comment)   // on the illustrative PurchaseOrder aggregate
{
    Require(PurchaseOrderStatus.Submitted, ProcurementCodes.ApproveRequiresSubmitted, "approve");
    EnsureActorIsNotCreator(actorId, ProcurementCodes.SodApproverIsRequester);       // only if the intake defined this SoD rule
    // any further domain rule the intake defined goes here, as a guard with a coded refusal
    FirstApproval = new Approval(actorId, at, comment);
    Status = PurchaseOrderStatus.Approved;
    Raise(new PurchaseOrderApproved(Id, OrderNumber, actorId, TenantId));
}
```

### P-11 — Anemic aggregates: public setters, status as free text, defaults that invent data

**Symptom.** `public PurchaseOrderStatus Status { get; set; }`; `string Status`; a default risk score or priority that appears without anyone choosing it.

**Why it is wrong.** Illegal states become representable; three sources of truth diverge (the baseline's predecessor had UI, backend and docs disagreeing on the state set); fabricated defaults mislead reports.

**Detect.** `DomainHardeningTests`; review: any numeric default on an audited field is refused; factories require explicit values.

### P-12 — Reference numbers from `count + 1`

**Symptom.** `var next = await repo.CountAsync() + 1;` to build `PO-2026-00042`.

**Why it is wrong.** Races produce duplicates; a deletion renumbers everything; finance reconciliation breaks. Use a per-tenant, per-type, per-year counter row updated with `UPDATE … RETURNING` inside the transaction, never reused.

**Detect.** grep `CountAsync() + 1`; integration test: 50 parallel creates yield 50 distinct numbers.

### P-13 — Repository decisions: refusals, enum parsing and clamps hidden in SQL

**Symptom.** A repository throws `PO-404`; parses a string to an enum leniently; clamps page size.

**Why it is wrong.** The handler must keep every decision so it can be unit-tested and so the coded refusal is in one place. A repository returns `null`, takes `TEnum?`, and receives a normalised `PageRequest`.

**Detect.** `InfrastructureHardeningTests`: no `DomainException` thrown from `Repositories/`.

### P-14 — Time and identifiers from the environment

**Symptom.** `DateTime.Now`, `DateTimeOffset.UtcNow`, `Guid.NewGuid()`, database `DEFAULT now()` on audited timestamps.

**Why it is wrong.** Untestable, time-zone-ambiguous, and ids that do not exist before commit break the outbox (events need the id).

**Detect.** Analyzer ban on `DateTime.Now`/`UtcNow` outside `SystemClock`; `Entity` mints `Guid.CreateVersion7()`.

### P-15 — Jobs that set state directly

**Symptom.** A sweep does `UPDATE … SET status = 'Expired' WHERE approved_at < now() - interval '30 days'`.

**Why it is wrong.** It bypasses guards, events and attribution. The sweep must load each aggregate (elevated, per tenant) and call `Expire(today)`, which refuses when the order was issued meanwhile and raises the event.

**Detect.** `InfrastructureHardeningTests`: no `ExecuteUpdate`/raw `UPDATE` against an aggregate table outside an allow-list (counters, leases).

### P-16 — Business data in HTTP headers

**Symptom.** A rejection reason or an approval comment read from a custom header by middleware; the value bypasses validation, OpenAPI and the API-surface snapshot.

**Why it is wrong.** Business data has a contract; headers do not. The baseline carried a reason header enforced on `DELETE` and had to build a client-side interceptor to inject it.

**Detect.** `WebApiHardeningTests`: no middleware reads a custom `X-*` header other than `X-Requested-With` and `Idempotency-Key`; reasons are validated command properties.

```csharp
// NOT THAT
public sealed class ChangeReasonMiddleware { /* reads X-Change-Reason */ }

// DO THIS
public sealed record RejectPurchaseOrderCommand(Guid OrderId, string Reason) : ICommand;
// validator: RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(1000);
// aggregate: Reject(actor, reason) → Raise(new PurchaseOrderRejected(Id, OrderNumber, actor, reason, TenantId));
```

---

## B. Authorization and the client

### P-20 — Client-side state as truth; UI-only authorization

**Symptom.** A button hidden by `perms.can('purchase-orders.approve')` is the only check; a total computed in the SPA is sent back and stored; a role string stored in `localStorage` decides routes.

**Why it is wrong.** Anything the browser holds, the user controls. The baseline's predecessor had authorization entirely client-side with a binary `[Authorize]` on the backend and unknown roles defaulting to manager.

**Detect.** `RequestPolicyTests` (every command and query carries a policy, D-04); functional tests that call every endpoint with a user lacking the permission and expect 403; e2e: the SPA never sends a derived field.

```typescript
// NOT THAT — the SPA decides
if (perms.can('purchase-orders.approve')) { await api.updateOrder(id, { status: 'Approved', total }); }

// DO THIS — the SPA offers; the server decides; the command names the transition
@if (perms.can('purchase-orders.approve')) { <button (click)="facade.approve.execute({ id, comment })">Approve</button> }
// server: [RequirePermissionPolicy(PermissionCatalog.PurchaseOrders, PermissionAction.Approve)] ApprovePurchaseOrderCommand → order.Approve(actor, …)
```

### P-21 — PII or raw internal identity in a token the browser can decode

**Symptom.** `email`, `name`, `role`, `permissions` claims in the access token; the SPA decodes the token to render the header.

**Why it is wrong.** A token is a credential, not a profile; every claim is a disclosure to whoever holds it; role and permission claims go stale and then need a per-request DB check anyway, at which point the claim is a cache without invalidation.

**Detect.** `AccessTokenClaimsTests`: the payload contains only `iss aud sub sid tid scope jti iat nbf exp`; `sub` is `v1.<kid>.<…>` and differs between two tokens for the same user; `SubjectProtectorTests` for the encryption itself (D-02, `02 §4.6`). The SPA never decodes the token; it reads the profile from `AuthResponse` (`01 §6.4`).

```csharp
// NOT THAT
claims.Add(new(JwtRegisteredClaimNames.Email, user.Email));
claims.Add(new(JwtRegisteredClaimNames.Name, user.DisplayName));
claims.Add(new(ClaimTypes.Role, user.Role.ToString()));
claims.Add(new(JwtRegisteredClaimNames.Sub, user.Id.ToString()));     // plaintext user id

// DO THIS — D-01 / D-02
var claims = new Dictionary<string, object>
{
    ["sub"] = subjectProtector.Protect(user.Id),      // AES-256-GCM, fresh nonce per token, kid-prefixed (02 §4.6)
    ["sid"] = session.Id.ToString(),                  // plain: locates the session row; the server resolves tenant, role, privileges, display name
    ["tid"] = session.TenantId.ToString(),
    ["scope"] = session.Scope,
    ["jti"] = Guid.CreateVersion7().ToString(),
};
// exp = iat + 15 minutes; signed ES256 with a kid; no email, name or role anywhere in the payload
```

### P-22 — Symmetric signing with a human-chosen secret

**Symptom.** `new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]))` with a length check.

**Why it is wrong.** Entropy is the string's, not 256 bits; every verifier can mint.

**Detect.** `ConfigGuardTests`: signing key loads as ES256; no `HmacSha256` signing credentials in `Security/Tokens`.

### P-23 — Security mode controlled by environment name

**Symptom.** `if (!env.IsProduction()) allowPlaintext = true;`; guards that only warn outside Production.

**Why it is wrong.** A deployment label becomes a security boundary; evidence gathered on a `QA`-named environment does not describe Production.

**Detect.** `ConfigGuardTests` run the guards under every environment name and expect identical refusals; grep `IsProduction()` in `Security/` and `Startup/` returns nothing except logging verbosity.

### P-24 — Refresh cookie without the `__Secure-` prefix, with a `Domain`, with `SameSite=None`, or with a wide `Path`

**Symptom.** `Set-Cookie: rt=…; Path=/; Domain=.example.com; SameSite=None`.

**Why it is wrong.** A sub-domain can set or shadow it; `None` opens cross-site sending; a `Path=/` sends the refresh token on every data request, where nothing should ever read it (D-01).

**Detect.** `RefreshSessionTests` asserts `__Secure-rt; Path=/api/v1/auth; Secure; HttpOnly; SameSite=Strict` and no `Domain`.

### P-25 — Authenticating a data request with the refresh cookie, or reading it outside `/api/v1/auth`

**Symptom.** An authentication handler that falls back to the refresh cookie when the bearer header is absent; a middleware that reads `rt` to "keep the user signed in".

**Why it is wrong.** The refresh token is a long-lived credential whose only legitimate use is to mint a short-lived access token under rotation (D-01). Letting it authenticate data requests turns every endpoint into a CSRF target and defeats rotation and reuse detection.

**Detect.** `RefreshSessionTests`: a cookie-only request to a data endpoint → `401`; grep for the cookie name outside `Security/RefreshCookie.cs` and the auth controller returns nothing.

### P-26 — Queries without a permission policy

**Symptom.** "Reads are open to any authenticated user."

**Why it is wrong.** Deny-by-default that covers only writes is not deny-by-default; `viewConfidential` shows reads matter.

**Detect.** `RequestPolicyTests` covers `IQuery<>` (D-04).

### P-27 — `[Authorize(Roles = "Manager")]` on a tenant endpoint

**Symptom.** Role names in controller attributes.

**Why it is wrong.** Roles are tenant-configurable; the catalogue is the contract; the controller is not on the job/policy path.

**Detect.** `WebApiHardeningTests`: no `Roles=` on tenant controllers; only `[AllowAnonymous]` and the fallback policy.

---

## C. Tenancy and elevation

### P-30 — Cross-tenant leakage and improper RLS bypasses

**Symptom.** Tenant resolved from a header or query string; `Elevate()` called in a request handler "to look up a user"; a background job creating rows with the ambient (unresolved or wrong) tenant; a raw `NpgsqlConnection` opened without the GUC; a null-tolerant policy (`tenant_id IS NULL OR …`) to make a nullable column "work".

**Why it is wrong.** Each of these defeats one or more of the three layers. The header-derived tenant was the predecessor's spoofable path; a null-tolerant policy makes every platform-level row visible to every tenant; an elevated request path runs end-user input across tenants.

**Detect.** `ElevationScopingTests` (allow-listed call sites, always in `using`); `TenantResolutionMiddleware` reads the principal only; `InfrastructureHardeningTests` (no `NpgsqlConnection` outside Infrastructure; no `IS NULL OR` in a `tenant_isolation` policy except the `audit.*` `WITH CHECK` clauses); `TenantStampInterceptor` throws `TENANT-000` on an unresolved tenant instead of guessing; `RlsTenantIsolationTests` per table.

```csharp
// NOT THAT — a handler elevates to read across tenants
using (tenantSetter.Elevate()) { var user = await users.FindByEmailAsync(email, ct); }

// NOT THAT — a job relies on the ambient tenant
var order = PurchaseOrder.Create(...); orders.Add(order); await uow.SaveChangesAsync(ct);   // TenantStampInterceptor: TENANT-000

// DO THIS — the tenant is explicit where the ambient one cannot be trusted
var order = PurchaseOrder.CreateFromContract(tenantId: contract.TenantId, ...);           // factory requires it
// or, per tenant in a sweep:
foreach (var tenant in tenants) { using var scope = scopes.ForTenant(tenant.Id); /* ICurrentTenant set; no elevation */ }
```

```sql
-- NOT THAT — a "null-tolerant" policy
CREATE POLICY tenant_isolation ON identity.user_account USING (tenant_id IS NULL OR tenant_id = current_setting('app.current_tenant', true)::uuid);

-- DO THIS (D-06) — split the model: platform user (no tenant_id, no RLS, actor-keyed queries) + RLS-protected membership
CREATE TABLE identity.platform_users (id uuid PRIMARY KEY, email citext UNIQUE, ...);
CREATE TABLE app.tenant_memberships (tenant_id uuid NOT NULL, id uuid NOT NULL, user_id uuid NOT NULL, role_id uuid NOT NULL, ..., PRIMARY KEY (tenant_id, id));
ALTER TABLE app.tenant_memberships ENABLE ROW LEVEL SECURITY;  ALTER TABLE app.tenant_memberships FORCE ROW LEVEL SECURITY;
```

### P-31 — Owned child without a tenant

**Symptom.** An owned type keyed by `(OwnerId, Id)` with no `tenant_id`; RLS cannot fence it.

**Detect.** `TenantKeyConvention` adds the shadow `TenantId`; `OwnedChildTenancyTests`.

### P-32 — Audit rows attributed to no tenant

**Symptom.** `audit.field_changes` rows with `NULL` tenant for tenant records (the baseline had 21,209 such rows before the interceptor read the owned child's shadow tenant).

**Detect.** Integration test: provisioning a tenant writes zero null-tenant field changes; only pre-authentication security events may be null.

---

## D. Transport, rate limiting and CORS

### P-40 — Rate-limit bypasses and CORS misconfigurations

**Symptom.** Trusting `X-Forwarded-For` from any source so the partition key is attacker-chosen; IP-keyed credential limits only (an office behind one NAT is throttled; a distributed attacker is not); fixed windows; a 429 counting toward lockout (so an attacker locks accounts with garbage); `AllowAnyOrigin()` or `SetIsOriginAllowed(_ => true)` with `AllowCredentials()`; a dev-only CORS policy that ships.

**Why it is wrong.** Each turns a protective control into either a denial-of-service lever against legitimate users or a no-op against attackers. The same-origin design has no allow-list to misconfigure; adding CORS re-creates the risk.

**Detect.** `ForwardedHeadersTests`: a request from a non-trusted address with a spoofed header keeps the real address; `RateLimitTests`: lockout counter unchanged after 429s; `WebApiHardeningTests`: no `AddCors`/`UseCors` anywhere; functional test: an `Origin` header from another site on `/auth/refresh` → `403 CSRF-403`.

```csharp
// NOT THAT
options.ForwardedHeaders = ForwardedHeaders.All; options.KnownNetworks.Clear(); options.KnownProxies.Clear();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
options.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress!.ToString(), ...));

// DO THIS
options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;   // loopback trusted; KnownNetworks from config only
// no AddCors at all
options.AddPolicy(Policies.CredentialAccount, ctx => RateLimitPartition.GetSlidingWindowLimiter(AccountKeyHash(ctx), ...));   // + per-IP backstop
options.OnRejected = (ctx, _) => { /* 429 RATE-429 + Retry-After; NO lockout increment */ };
```

### P-41 — In-process counters, caches or keys that bear on correctness

**Symptom.** Replay nonces in `IMemoryCache`; rate-limit counters in process with "replicas: 1" in a manifest; an RSA key generated at start-up and never shared.

**Why it is wrong.** The second replica makes replays succeed, limits double and challenges fail. The baseline's ADR-0001 pinned a single replica for exactly this reason and ADR-0006 lifted it, then ADR-0012 re-pinned it. Under D-03 the envelope is mandatory, so V2 keeps its nonce set and key material in PostgreSQL (`01 §4.8`) rather than in process.

**Detect.** Review classification of every singleton holding state (cache / per-replica-tolerable / correctness-bearing); functional test with two factories over one database for nonces and idempotency.

### P-42 — HSTS or security headers only on success paths

**Symptom.** Headers added in a controller filter; errors from the exception handler ship without them.

**Detect.** `SecurityHeadersTests` asserts headers on 200, 400, 401, 403, 404, 429, 500 and on `/health`.

---

## E. Reliability and events

### P-50 — Publishing events before the commit, or outside the transaction

**Symptom.** `await publisher.Publish(new OrderApproved(...))` inside the handler; a message broker call before `SaveChangesAsync`.

**Why it is wrong.** The event escapes even when the save fails; or the save succeeds and the publish fails. Only the outbox drained by `SaveChanges` is atomic.

**Detect.** `ApplicationHardeningTests`: no `IPublisher`/broker client injected into a handler; events are raised on aggregates only.

### P-51 — Outbox without `SKIP LOCKED`, lease, back-off, dead-letter

**Symptom.** `SELECT * FROM outbox WHERE processed IS NULL LIMIT 100` with no claim; a failing event blocking the queue; infinite retries.

**Detect.** ADR-0006 carried forward; `OutboxProcessorTests` for each property; the dead-letter metric exists and is alerted.

### P-52 — Non-idempotent consumers

**Symptom.** A policy that opens an expected invoice without a natural unique key; two deliveries create two.

**Detect.** Every policy has a replay test; the unique index exists.

### P-53 — Audit chain appended without serialization

**Symptom.** Two writers read the same chain tip; the unique constraint rejects one; nobody knows what the losing writer does.

**Detect.** `AuditChainConcurrencyTests`; the appender takes `pg_advisory_xact_lock(hashtext('audit_chain:' || tenant_id))`.

### P-54 — Editing a record in a frozen state in place

**Symptom.** An "edit" endpoint that updates a record the domain declared frozen (for example after an approval gate); a support script that fixes a value by `UPDATE`.

**Why it is wrong.** The frozen state exists because the domain said the content must not change silently; in-place edits make the recorded decision and the audit trail meaningless and break downstream consumers.

**Detect.** `FrozenStateImmutabilityTests` (the trigger refuses); the only path is the correction route the intake defined (amendment, revision, new version) (D-07).

---

## F. Frontend

### P-60 — `any`, `$any()`, untyped event targets, string-built URLs

**Symptom.** `(input)="search.set($any($event.target).value)"`; `` `${base}?state=${encodeURIComponent(state)}` ``.

**Detect.** `strict` TypeScript with `noImplicitAny`; ESLint `no-explicit-any` as error; `HttpParams` only; generated client.

```typescript
// NOT THAT
<input (input)="search.set($any($event.target).value)" />
list(state?: string) { return this.http.get<Item[]>(state ? `${this.base}?state=${encodeURIComponent(state)}` : this.base); }

// DO THIS
<input (input)="onSearch($event)" />
onSearch(e: Event) { this.search.set((e.target as HTMLInputElement).value); }
list(state?: OrderState) { return this.http.get<Item[]>(this.base, { params: new HttpParams({ fromObject: state ? { state } : {} }) }); }
```

### P-61 — The SPA as the source of truth (token in storage, computed state sent back, permissions decided locally)

**Symptom.** `localStorage.setItem('token', …)`; the SPA computes `total = Σ lines` and posts it; `can()` is the only gate.

**Detect.** e2e asserts no credential in web storage (the access token lives in a signal only, D-01); request contracts have no derived fields; P-20.

### P-62 — Hand-written DTO mirrors, one giant `models.ts`

**Symptom.** 2,000+ lines of interfaces maintained by hand; a renamed backend property discovered in production.

**Detect.** Generated client from OpenAPI; CI fails on diff.

### P-63 — Zone-based change detection, `subscribe()` in components, per-facade loading boilerplate

**Symptom.** `provideZoneChangeDetection`; `this.api.list().subscribe(x => this.items = x)`; forty copies of `run()`.

**Detect.** `provideZonelessChangeDetection()`; ESLint rule forbidding `subscribe(` in `*.component.ts`; `resource()` and the shared `mutation()` helper.

### P-64 — Shipping every language to every user from a TypeScript object

**Symptom.** A 3,000-line dictionary in a root service.

**Detect.** Per-feature JSON loaded with the route; typed keys generated from the English file.

### P-65 — Features importing each other

**Symptom.** `features/orders/...` imports `features/vendors/...`.

**Detect.** ESLint boundary rule; shared pieces move to `shared/`.

---

## G. Process (the pitfalls that produce false evidence)

### P-70 — Claiming a build, test or migration result nobody watched

**Symptom.** "All tests pass" without a per-suite count; a run-log entry for a run that was interrupted.

**Why it is wrong.** An unverifiable claim is worse than no claim: it becomes the basis for a release decision.

**Detect.** The run log requires per-suite counts and the command run; the stop hook refuses a claim without them.

### P-71 — Bare totals

**Symptom.** "460 tests green." A suite that silently did not run is invisible in a total.

**Detect.** Per-suite reporting; a suite with zero tests executed fails the gate.

### P-72 — An automated agent recording an approval or a sign-off on a human's behalf

**Why it is wrong.** An approval is a human decision with a named accountable actor; an agent that writes `ApprovedByUserId` for someone, or marks a change request approved, forges accountability.

**Detect.** The guard-paths hook; approvals happen only through the authenticated command path.

### P-73 — `git add -A`

**Symptom.** Scratch files, secrets or generated artefacts committed.

**Detect.** The bash guard hook; staging by explicit path.

### P-74 — Numbers and dates in guides

**Symptom.** A test count or a version in `CLAUDE.md` that is wrong a week later.

**Detect.** `STATE.md` is generated; guides reference it.

---

## H. Quick reference: the gate for every pitfall

| Pitfall | Gate (exists in baseline → carry; new → build) |
| --- | --- |
| P-01, P-13 | `LayerRulesTests`, `InfrastructureHardeningTests` (carry) |
| P-02 | `TransitionAtomicityTests` (new, replaces the baseline's ceremony test) |
| P-03 | `ApplicationHardeningTests` census (carry) |
| P-04, P-05, P-06, P-07 | `MigrationScriptTests` (carry; add "no `app.bypass_rls`", "no single-column key on tenant table") |
| P-08, P-31, P-32 | `OwnedChildTenancyTests`, FK census (carry) |
| P-09 | `CheckConstraintTests` (carry; add snapshot-level) |
| P-10, P-11 | `DomainHardeningTests` (carry) |
| P-12 | parallel-create integration test (new) |
| P-14 | analyzer ban (new) |
| P-15 | raw-update census (new) |
| P-16 | `WebApiHardeningTests` header census (new) |
| P-20, P-26 | `RequestPolicyTests` over commands and queries (extend) |
| P-21, P-22 | `AccessTokenClaimsTests`, `SubjectProtectorTests`, `ConfigGuardTests` (new) |
| P-23 | `ConfigGuardTests` under every environment name (new) |
| P-24, P-25 | `RefreshSessionTests` (carry from the baseline's functional suite; extend) |
| P-27 | `WebApiHardeningTests` (carry) |
| P-30 | `ElevationScopingTests`, `RlsTenantIsolationTests`, `TenantStampInterceptor` (carry) |
| P-40 | `ForwardedHeadersTests`, `RateLimitTests`, no-CORS assertion (new/extend) |
| P-41 | two-factory functional tests (new) |
| P-42 | `SecurityHeadersTests` (carry) |
| P-50, P-51, P-52 | outbox tests (carry) |
| P-53 | `AuditChainConcurrencyTests` (new) |
| P-54 | `FrozenStateImmutabilityTests` (carry the trigger; new test name) |
| P-60–P-65 | ESLint rules, generated client CI diff (new) |
| P-70–P-74 | hooks and the run log (carry) |
