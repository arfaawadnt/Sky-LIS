# Master Implementation Prompts Playbook
## Greenfield Enterprise SaaS System (Phase 1 Engineering)

> **File:** `PROMPT.md`  
> **Target Audience:** Solution Architects, Lead Developers, AI Agents (Claude / Cursor / Antigravity)  
> **Specification Baseline:** `Version2/` Document Pack (`README.md`, `00`, `01`, `02`, `03`, `04`)  

---

## Table of Contents
1. [How to Use This Playbook](#how-to-use-this-playbook)
2. [PROMPT 0: The Master Ingestion Directive (First Message)](#prompt-0-the-master-ingestion-directive-first-message)
3. [PROMPT 1: Solution Scaffolding & Architecture Test Gates](#prompt-1-solution-scaffolding--architecture-test-gates)
4. [PROMPT 2: Database Migrations, PostgreSQL 17 FORCE RLS & Identity Plumbing](#prompt-2-database-migrations-postgresql-17-force-rls--identity-plumbing)
5. [PROMPT 3: Core Security Plumbing (Credential Envelope & Encrypted Tokens)](#prompt-3-core-security-plumbing-credential-envelope--encrypted-tokens)
6. [PROMPT 4: Frontend Scaffolding (Angular 22 Zoneless & Signal Store)](#prompt-4-frontend-scaffolding-angular-22-zoneless--signal-store)
7. [PROMPT 5: First Feature Module Implementation (DDD Flow)](#prompt-5-first-feature-module-implementation-ddd-flow)

---

## How to Use This Playbook

Do not paste all prompts at once. This playbook is designed to be executed **sequentially, step by step**:

1. **Step 0:** Open a new conversation with the AI agent. Copy and paste **PROMPT 0** along with the 6 specification files attached or pasted below it. Wait for the agent to acknowledge and confirm its understanding.
2. **Step 1:** Send **PROMPT 1** to scaffold the .NET 9 solution and project structure. Verify compilation and architecture tests.
3. **Step 2:** Send **PROMPT 2** to build the database context, PostgreSQL 17 FORCE RLS, and identity split.
4. **Step 3:** Send **PROMPT 3** to build the cryptographic plumbing (Credential Envelope filter, AES-GCM token encryption, refresh cookies).
5. **Step 4:** Send **PROMPT 4** to build the Angular 22 zoneless frontend.
6. **Step 5:** Send **PROMPT 5** whenever you are ready to implement a business feature using the intake sheet from Document 03.

---

```
  ┌────────────────────────────────────────────────────────────────────────┐
  │                        SEQUENTIAL EXECUTION FLOW                       │
  └────────────────────────────────────────────────────────────────────────┘
                                      │
   [ PROMPT 0: Ingest & Affirm ] ─────┼──► Attaches 6 Docs in Version2/
                                      │
   [ PROMPT 1: Solution Scaffold ] ───┼──► .NET 9 Projects + LayerRulesTests
                                      │
   [ PROMPT 2: Database & RLS ] ──────┼──► PostgreSQL 17 FORCE RLS + Identity
                                      │
   [ PROMPT 3: Security & Crypt ] ────┼──► Envelope + AES-GCM Encrypted Token
                                      │
   [ PROMPT 4: Angular Frontend ] ────┼──► Angular 22 Signals + Auth Store
                                      │
   [ PROMPT 5: Business Feature ] ────┴──► DDD Aggregate + CQRS + UI Flow
```

---

## PROMPT 0: The Master Ingestion Directive (First Message)

*Copy the entire block below and paste it as the very first message in the chat, attaching all 6 files from `Version2/`.*

```markdown
# SYSTEM DIRECTIVE: ENTERPRISE ARCHITECTURE INGESTION & BINDING CONSTITUTION

You are acting as the **Lead Solutions Architect & Principal Software Engineer** for our brand-new greenfield B2B Enterprise SaaS platform.

### MANDATORY REPOSITORY CONTEXT:
I have provided our complete, ratified **Master Architecture Specification Pack** (attached / pasted below):
1. `README.md` — Product Baseline, Ratified Decisions D-01 through D-07, and Boundaries.
2. `00-CRITICAL-ARCHITECTURAL-CHALLENGE-AND-ALTERNATIVES.md` — Architectural Critique, Rationale & Decisions.
3. `01-SYSTEM-ARCHITECTURE-BLUEPRINT.md` — Topology, Layering, Project Structure, DDL, and Enforcing Test Gates.
4. `02-SECURITY-AND-IDENTITY-SPECIFICATION.md` — Transport, Credential Envelopes, Tokens, Refresh Sessions, Rate Limits.
5. `03-BUSINESS-INTAKE-AND-DDD-GUIDE.md` — Generic Domain Modeling Framework: Invariants, States, SoD, Approvals.
6. `04-ARCHITECTURAL-PITFALLS-AND-AVOIDANCE-GUIDE.md` — The 40 Forbidden Anti-Patterns and Test Gates.

---

### YOUR NON-NEGOTIABLE LAWS OF IMPLEMENTATION:
Across all upcoming tasks, every line of code, DDL script, project configuration, and pattern you produce MUST strictly comply with these rules:

#### 1. Platform.Domain (01 §2.1, 04 P-03, P-05, P-10)
- **Zero External Dependencies:** Depends solely on `SharedKernel`. Zero references to EF Core, ASP.NET, MediatR, Contracts, or DTOs.
- **Aggregates Protect Invariants:** All entity properties have `private set;`. Mutations occur only through factory methods (`Create`) and explicit business methods (`Approve`, `Cancel`).
- **Clock Abstraction:** Never use `DateTime.UtcNow` or `DateTime.Now`. Injected `IClock` is passed to domain methods requiring time.
- **In-Domain Logic:** Business invariants, approval validations, and Segregation of Duties (SoD) are evaluated inside entity methods, NEVER solely in FluentValidation or UI code.

#### 2. Platform.Application (01 §2.1, 01 §3, 04 P-01, P-02, P-26)
- **Zero EF Core Leaks:** `DbContext`, `DbSet`, and `IQueryable` are strictly prohibited in the Application layer.
- **Repository Pattern:** Persistence is strictly orchestrated through `I<Aggregate>Repository` and `IUnitOfWork`. One repository per aggregate. Handlers commit changes exactly once.
- **Deny-by-Default Authorization (D-04):** Every MediatR request (both Commands AND Queries) MUST carry an explicit `[RequirePermissionPolicy]` attribute. An automated test gate (`RequestPolicyTests`) enforces this.
- **Read Purity:** Queries project directly to read-only DTO records defined in `Platform.Contracts`. Domain entities must never be returned across the application boundary.

#### 3. Platform.Infrastructure (01 §4, 04 P-04, P-07, P-08, P-30)
- **PostgreSQL 17 Multi-Tenancy:** Every tenant table has composite primary keys `(tenant_id, id)` and enforces `FORCE ROW LEVEL SECURITY (RLS)`.
- **Migration Law:** Every migration creating a tenant table MUST contain `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY`, and `CREATE POLICY tenant_isolation`.
- **Connection Stamping:** Runtime database connections are stamped via `TenantConnectionInterceptor` executing `SET LOCAL app.current_tenant = @tenantId`.
- **Migration Role Bypass:** Migrations run under a dedicated owner role with `BYPASSRLS` to prevent transaction-local setting traps.
- **Atomic Outbox:** Domain events are staged into the transactional outbox within the same database commit.

#### 4. Platform.WebApi & Security Edge (01 §1, 02 §2–§5, 04 P-21, P-24, P-40, P-41)
- **Skinny Controllers:** Controllers ONLY inject `ISender` to dispatch commands and queries. Zero business logic, zero direct database access.
- **Credential Protection (D-03):** All 9 credential endpoints require ephemeral JWE envelopes (RSA-OAEP-256 + AES-GCM). Credentials are unwrapped at the API edge via `CredentialTransportFilter`. Nonces and keys reside in shared storage (PostgreSQL), not in-process singletons.
- **Token Privacy (D-02):** Access tokens are standard RFC 7515 JWS. The `sub` claim is symmetrically encrypted via AES-256-GCM. The `email`, `name`, and `role` claims are STRICTLY FORBIDDEN in the token payload.
- **Session Architecture (D-01):** The Angular client uses access tokens in memory signals paired with rotating `__Secure-rt` HTTP-only cookies restricted to `Path=/api/v1/auth`.
- **Traffic Rules:** Same-Origin policy (No CORS enabled in production). Strict anti-CSRF header inspection (`X-Requested-With`). Sliding-window rate limiting per endpoint policy.

#### 5. Database Identity Split (D-06, 01 §4.1)
- Global authentication credentials live in `identity.platform_users`.
- Tenant profiles, roles, and memberships live in RLS-protected `app.tenant_memberships`.

#### 6. Frontend Architecture (01 §6, 04 P-60, P-61)
- Angular 22 standalone components, signals, and zoneless change detection.
- Strict TypeScript (`noImplicitAny`). Zero use of `any` or `$any()`.
- Credentials and access tokens MUST NEVER be persisted in `window.localStorage` or `sessionStorage`.

#### 7. The 40 Architectural Pitfalls (Doc 04)
- Zero tolerance for any pattern cataloged in Document 04. Any pattern marked `NOT THAT` will cause an immediate build rejection.

---

### INGESTION ACKNOWLEDGMENT:
Please read and ingest all attached documents.
**Do NOT write any project code yet.** Simply respond with:
1. Confirmation that you have ingested and adopted all 6 specification documents as your sovereign architectural constitution.
2. A bulleted confirmation of the 7 core laws above.
3. State: *"Ready for PROMPT 1: Solution Scaffolding & Architecture Test Gates."*

---
[ATTACH OR PASTE THE 6 SPECIFICATION FILES BELOW THIS LINE]
```

---

## PROMPT 1: Solution Scaffolding & Architecture Test Gates

*Send this prompt once the agent acknowledges PROMPT 0.*

```markdown
# TASK 1: SOLUTION SCAFFOLDING & ARCHITECTURAL ENFORCEMENT GATES

Now that you have ingested the Master Architecture Constitution, scaffold the complete .NET 9 Clean Architecture solution structure and architectural test gates according to Document 01 §2.1 and §2.3.

### REQUIREMENTS:

1. **Solution & Projects Creation:**
   Create a .NET 9 / C# 13 solution containing the following 6 production projects and 2 test projects:
   - `Platform.SharedKernel` (Class Library - net9.0)
   - `Platform.Domain` (Class Library - net9.0)
   - `Platform.Contracts` (Class Library - net9.0)
   - `Platform.Application` (Class Library - net9.0)
   - `Platform.Infrastructure` (Class Library - net9.0)
   - `Platform.WebApi` (Web API - net9.0)
   - `Platform.ArchitectureTests` (xUnit Test Project - net9.0)
   - `Platform.Domain.UnitTests` (xUnit Test Project - net9.0)

2. **Project References Matrix (Document 01 §2.1):**
   - `SharedKernel`: References BCL only.
   - `Domain`: References `SharedKernel` ONLY.
   - `Contracts`: References `SharedKernel` ONLY.
   - `Application`: References `Domain`, `Contracts`, `SharedKernel`, MediatR, FluentValidation.
   - `Infrastructure`: References `Application`, `Domain`, `SharedKernel`, EF Core 9, Npgsql.
   - `WebApi`: References `Application`, `Infrastructure`, `Contracts`.
   - `ArchitectureTests`: References NetArchTest.Rules or ArchUnitNET to enforce layer rules.

3. **Base Primitives in `Platform.SharedKernel`:**
   - `Entity<TId>` and `AggregateRoot<TId>` base classes.
   - `ValueObject` base class.
   - `IDomainEvent` interface and `DomainEvent` base record.
   - `IClock` interface with `SystemClock` implementation.
   - `Result` and `Result<T>` functional types with structured `Error`.
   - `ITenantScoped` marker interface.

4. **Architectural Test Gates in `Platform.ArchitectureTests`:**
   Implement xUnit tests that fail the build if:
   - `Application` references `Microsoft.EntityFrameworkCore` or `Npgsql`.
   - `Domain` references `Application`, `Infrastructure`, `Contracts`, or external packages.
   - Any Aggregate Root property exposes a public setter (`public set`).
   - Any MediatR `ICommand` or `IQuery` handler lacks an explicit `[RequirePermissionPolicy]` attribute.

Please output the `.csproj` definitions, directory tree, base primitives, and the `LayerRulesTests.cs` file.
```

---

## PROMPT 2: Database Migrations, PostgreSQL 17 FORCE RLS & Identity Plumbing

*Send this prompt after completing Task 1.*

```markdown
# TASK 2: DATABASE INFRASTRUCTURE, POSTGRESQL 17 FORCE RLS & IDENTITY SPLIT

Implement the database infrastructure and identity data model adhering strictly to Document 01 §4, Document 02 §4, and Pitfalls P-04, P-07, and P-30.

### REQUIREMENTS:

1. **Identity & Tenancy Separation (D-06):**
   Implement the EF Core configurations and DDL for:
   - `identity.platform_users`: Global credentials (id, username, password_hash, security_stamp, is_locked_out, lockout_end_utc, row_version).
   - `app.tenant_memberships`: RLS-isolated tenant profiles (tenant_id, id, platform_user_id, employee_code, full_name, is_active, row_version). Primary key MUST be composite: `(tenant_id, id)`.
   - `identity.refresh_sessions`: Database-backed session table (id, tenant_id, platform_user_id, refresh_token_hash, ip_address, user_agent, is_revoked, expires_at_utc).

2. **PostgreSQL 17 FORCE RLS Migration:**
   Write the initial EF Core Migration and SQL script ensuring:
   - `app.tenant_memberships` executes `ALTER TABLE app.tenant_memberships ENABLE ROW LEVEL SECURITY;`
   - `ALTER TABLE app.tenant_memberships FORCE ROW LEVEL SECURITY;`
   - Policy: `CREATE POLICY tenant_isolation ON app.tenant_memberships USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);`

3. **Tenant Connection Stamping Interceptor:**
   Implement `TenantConnectionInterceptor` in `Platform.Infrastructure`:
   - Intercepts connection opening.
   - Reads `ITenantContextAccessor.CurrentTenantId`.
   - Executes `SET LOCAL app.current_tenant = @tenantId;` on the active connection.

4. **UnitOfWork & Base Repository:**
   - Implement `IUnitOfWork` coordinating single atomic commits with outbox event dispatch.
   - Implement generic `IRepository<T>` ensuring entities are queried within tenant bounds. Ensure repositories NEVER call `SaveChangesAsync`.

Please output the entity configurations, `AppDbContext.cs`, the migration script, and the `TenantConnectionInterceptor.cs`.
```

---

## PROMPT 3: Core Security Plumbing (Credential Envelope & Encrypted Tokens)

*Send this prompt after completing Task 2.*

```markdown
# TASK 3: CORE SECURITY PLUMBING (CREDENTIAL ENVELOPE, ENCRYPTED TOKENS & SESSIONS)

Implement the core security mechanisms in `Platform.Infrastructure` and `Platform.WebApi` adhering strictly to Document 02 §2–§5 and Ratified Decisions D-01, D-02, and D-03.

### REQUIREMENTS:

1. **Ephemeral Credential Envelope (D-03):**
   - Implement `ICredentialEnvelopeService`: Generates 120-second bound nonces and RSA-2048 keypairs. Store nonces in PostgreSQL/shared store (NOT in-process singleton, avoiding Pitfall P-41).
   - Implement `CredentialTransportFilter` in WebApi: Intercepts POST requests on credential endpoints, unwraps JWE payloads (`RSA-OAEP-256 + AES-GCM`), verifies nonce & endpoint binding, and populates plaintext passwords into the request model.
   - Endpoint: `GET /api/v1/auth/credential-challenge` returning `{ publicKeyPem, nonce, expiresAtUtc }`.

2. **Access Token Generation with AES-GCM Encrypted `sub` (D-02):**
   - Implement `ITokenService`: Mints RFC 7515 JWS tokens signed with HMAC-SHA256 (or ES256).
   - Implement `ISubjectProtector`: Encrypts the user GUID into a string `ENC:AQID...` using AES-256-GCM with a fresh random 12-byte IV per token.
   - Strict Payload Rules: Token MUST contain `iss`, `aud`, `sub` (encrypted), `sid` (session id), `tid` (tenant id), `iat`, `exp` (15-60 min).
   - STRICTLY OMIT `email`, `name`, `role`, and permissions from the JWT claims (Pitfall P-21).

3. **Rotating Refresh Token Cookie (D-01):**
   - Implement cookie writer issuing `__Secure-rt` with: `HttpOnly=true; Secure=true; SameSite=Strict; Path=/api/v1/auth; Domain=null;` (Pitfall P-24).
   - Implement refresh rotation: Calling `/api/v1/auth/refresh` invalidates the old refresh token hash in `refresh_sessions`, validates expiration, and issues a new access token and new refresh cookie.

4. **Sliding-Window Rate Limiting:**
   - Configure ASP.NET Core rate limiting partition rules:
     - `rl-cred-challenge`: 60 req/min per IP.
     - `rl-auth-login`: 10 req/min per IP + Username.
     - `rl-auth-refresh`: 30 req/min per session cookie.
   - Ensure 429 rejections DO NOT increment account lockout counters (Pitfall P-40).

5. **Anti-CSRF Header Guard:**
   - Enforce that all state-modifying requests (POST, PUT, DELETE, PATCH) carry `X-Requested-With: XMLHttpRequest`. Reject missing headers with 403.

Please output the complete C# implementation for the Envelope Service, Subject Protector, Token Service, and WebApi middleware configuration.
```

---

## PROMPT 4: Frontend Scaffolding (Angular 22 Zoneless & Signal Store)

*Send this prompt after completing Task 3.*

```markdown
# TASK 4: FRONTEND SCAFFOLDING (ANGULAR 22 ZONELESS & SIGNAL AUTH STORE)

Scaffold the Angular 22 frontend application adhering strictly to Document 01 §6 and Pitfalls P-60 and P-61.

### REQUIREMENTS:

1. **Angular 22 Architecture:**
   - Standalone components, Zoneless change detection (`provideExperimentalZonelessChangeDetection()`).
   - Strict TypeScript configuration (`noImplicitAny: true`, `strict: true`). Zero `any` or `$any()`.

2. **Auth Signal Store & Memory Token (D-01, P-61):**
   - Implement `AuthStore` service using Signals (`signal()`, `computed()`).
   - The Access Token MUST live in an in-memory signal. NEVER store tokens in `localStorage` or `sessionStorage`.
   - Provide signals: `isAuthenticated()`, `currentUser()`, `currentTenant()`, `hasPermission(perm)`.

3. **HTTP Interceptors:**
   - `AuthInterceptor`: Attaches `Authorization: Bearer <token>` from the memory signal to `/api/**` requests.
   - `CsrfInterceptor`: Attaches `X-Requested-With: XMLHttpRequest` to all mutating requests.
   - `RefreshTokenInterceptor`: Catches `401 Unauthorized`, initiates a single-flight call to `/api/v1/auth/refresh` (which browser automatically sends with `__Secure-rt` cookie), updates the memory token signal, and replays failed requests.

4. **Credential Envelope Client Service (D-03):**
   - Implement `CredentialEnvelopeClient`:
     1. Fetches challenge from `/api/v1/auth/credential-challenge`.
     2. Uses native browser WebCrypto API (`crypto.subtle`) to encrypt credentials with RSA-OAEP-256 + AES-GCM.
     3. Produces compact JWE envelope string bound to the nonce and target endpoint. Zero third-party npm crypto libraries.

Please output the `app.config.ts`, `auth.store.ts`, the HTTP interceptors, and the `credential-envelope.client.ts`.
```

---

## PROMPT 5: First Feature Module Implementation (DDD Flow)

*Send this prompt when implementing an actual business feature (e.g., WorkItems, Invoices, Contracts, etc.).*

```markdown
# TASK 5: BUSINESS FEATURE MODULE IMPLEMENTATION (DDD AGGREGATE & CQRS FLOW)

Implement a business feature module according to the Domain-Driven Design intake framework in Document 03 and the pitfall catalog in Document 04.

### INPUT SPECIFICATION:
[PASTE HERE THE COMPLETED FEATURE INTAKE SPECIFICATION FROM DOCUMENT 03]
*(Example: Feature Name, States, Invariants, SoD Rules, Allowed Transitions)*

### IMPLEMENTATION REQUIREMENTS:

1. **Domain Layer (`Platform.Domain`):**
   - Implement the Aggregate Root: Private setters, factory `Create(...)` method, explicit transition methods (`Advance`, `Approve`, `Cancel`).
   - Invariants and SoD rules evaluated INSIDE the aggregate methods.
   - Raise Domain Events (`IDomainEvent`) on state changes.
   - State machine enum defining valid states.

2. **Contracts Layer (`Platform.Contracts`):**
   - Request DTOs and Response DTOs.
   - Define module permissions in `PermissionCatalog`.

3. **Application Layer (`Platform.Application`):**
   - Command records and Query records.
   - Command Handlers: Load aggregate from repository, invoke domain method, commit via UnitOfWork.
   - Query Handlers: Direct read-only DTO projection from read repository.
   - MANDATORY: Annotate every Command AND Query with `[RequirePermissionPolicy(PermissionCatalog.<Module>, PermissionAction.<Action>)]` (D-04).
   - FluentValidation for syntax/input shape only.

4. **Infrastructure Layer (`Platform.Infrastructure`):**
   - EF Core entity configuration mapping table with `(tenant_id, id)` composite key.
   - Database migration with `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY`.
   - Concrete `I<Feature>Repository` implementation.

5. **WebApi Layer (`Platform.WebApi`):**
   - Feature controller dispatching requests via `ISender`.

6. **Frontend Feature (`Angular 22`):**
   - Smart feature component with signals, reactive form, permission directive, and API service.

Please output the clean, production-grade code for all layers.
```

---
*End of Master Implementation Prompts Playbook (`PROMPT.md`)*
