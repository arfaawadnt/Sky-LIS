# LIS — Workspace Rules

This workspace (`E:\LIS`) is a **new, standalone application**. Everything in it — design
decisions, domain model, database schemas, source code, configuration, scripts, data and
deployment artifacts — is **fully isolated** from every other application on this machine.

## 1. Isolation rules (non-negotiable)

1. **Own mindset.** Design LIS from its own requirements. Do not inherit architecture,
   naming, entities, workflows or "how we did it before" from any other project.
2. **No cross-project reads or reuse.** Never read, copy, import, reference, symlink or
   borrow from other applications on this machine. In particular, do not touch:
   - `E:\QMS`, `E:\qms publish`, `E:\ntqams-data`, `E:\ntqams-backups`, `E:\ntqams-hqms`
   - `C:\inetpub\wwwroot\qams-ui*`, `C:\install\secrets-vault.dpapi`
   - any file named like `*QAMS*`, `*QMS*`, `*ntqams*`
3. **Own git repository.** `E:\LIS` is its own repo (branch `main`). No submodules,
   subtrees or remotes pointing at other local projects. Its only remote is `origin` =
   `https://github.com/arfaawadnt/Sky-LIS` (**public** repository, linked 2026-10-10).
   Everything committed is published, so never commit secrets or client data.
4. **Own database.** LIS never connects to another app's database (e.g. `ntqams`). It has
   its own database, its own roles and its own credentials. See section 2.
5. **Own secrets.** Never reuse another project's credentials. LIS secrets live only in
   LIS-specific stores (dotnet user-secrets with a LIS `UserSecretsId`, git-ignored `.env`
   files, or a LIS-only vault). Never commit secrets.
6. **Own runtime footprint.** Distinct ports, service names, IIS sites, container, volume
   and network names and data folders, reserved in section 2. Never reuse, stop or alter
   another app's service, site, container or port.
7. **Own dependencies.** `node_modules`, build output, local tool manifests and package
   lock files live inside `E:\LIS`. No global `npm link`, no project references outside
   this folder. (Machine-wide package caches are fine; they hold no app state.)
8. **Own documentation.** Specs, ADRs, audits and reports for LIS are written fresh inside
   this repo (`docs/`). Do not cite or adapt another project's documents.
9. **Own configuration root.** This host sets machine-wide variables for the other application
   (`ConnectionStrings__Postgres`, `Jwt__Secret`, `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`) that every .NET
   process inherits. LIS reads configuration only under the **`Lis`** root (`Lis:Database:ConnectionString`,
   `Lis:Jwt:*`, `Lis:DataProtection:*`, `Lis:Security:*`; environment form `Lis__…`), pins its Kestrel endpoint in
   `appsettings.json`, and its start-up guard refuses any database not named `lis*` or role not named `lis_*`
   before connecting. Gates: `WebApiHardeningTests.The_host_reads_no_generic_configuration_key`,
   `InfrastructureHardeningTests.The_database_guard_refuses_a_target_that_is_not_LIS`. (Found on 2026-10-09: a
   first smoke run of the API read the machine-wide connection string and reached the other application's
   database with read-only probes; nothing was written.)

## 2. Reserved resource names for LIS

Chosen to avoid everything already in use on this machine (checked 2026-10-08).

| Resource                     | LIS value                                                | Avoid (already taken)                        |
|------------------------------|----------------------------------------------------------|----------------------------------------------|
| Workspace                    | `E:\LIS`                                                 | `E:\QMS`, `E:\qms publish`, `E:\ntqams-*`    |
| Code prefix / namespace      | `Platform.*` (per the architecture pack, `prompt/`; the product name may replace it later) | `NT.QAMS.*`  |
| Dev API (HTTP / HTTPS)       | `5100` / `5101`                                          | `5000`, `5080` (QMS APIs)                    |
| Dev SPA server               | `4300`                                                   | `4200` (keep free for other dev work)        |
| Dev PostgreSQL               | private instance `E:\LIS\.local\pg17` (in use) or Docker container `lis-postgres`; host port `5433` | `5432` (shared PostgreSQL 17, holds `ntqams`) |
| Other dev services           | ports `5102`–`5199` (`5102` = validation API beside the installed service; `5110` = second dev SPA `ux-spa` proxying the validation API) | `80`, `443`, `8080`, `5985`, `3389`          |
| Database and roles           | db `lis`, owner role `lis_owner`, app role `lis_app`     | db `ntqams`, role `postgres`                 |
| Docker Compose project       | `name: lis` → containers `lis-*`, volumes `lis_*`, network `lis_default` | none yet (daemon was off)    |
| Windows services (prod/test) | `NTLIS` (port `5100`) / `NTLIS_TEST` (port `5180`); `LIS_POSTGRES` = the private PostgreSQL 17 instance of this host as a service (5433, NetworkService) | `NTQAMS`, `NTQAMS_TEST`, `postgresql-x64-17` (5432) |
| IIS sites                    | `lis-ui` (own host header on 443, plus `https://*:5443` for access by IP) / `lis-ui-test` (port `8081`) | `qams-ui` (80/443), `qams-ui-test` (8080), `8443` (QAMS ORDS firewall rule) |
| IIS physical paths           | `C:\inetpub\wwwroot\lis-ui*`                             | `C:\inetpub\wwwroot\qams-ui*`                |
| Prod data / backups          | `E:\lis-data`, `E:\lis-backups`                          | `E:\ntqams-data`, `E:\ntqams-backups`        |
| Uploaded files (`Lis:Files:Root`) | dev `E:\LIS\.local\files` (git-ignored), production `E:\lis-data\files` | none |
| Notification pickup (`Lis:Notifications:PickupDirectory`) | dev `E:\LIS\.local\mail-pickup` (git-ignored; validation stack `…\mail-pickup\validation`), production `E:\lis-data\mail-pickup` (test service `E:\lis-data\mail-pickup-test`; ACL SYSTEM, Administrators, service account) | none |
| Publish output               | `E:\LIS\publish` (git-ignored)                           | `E:\qms publish`                             |
| API install root (service binaries + protected config) | `C:\lis-app\NTLIS`, `C:\lis-app\NTLIS_TEST` (`deploy/windows/install-api-service.ps1`) | none |
| Service accounts             | virtual accounts `NT SERVICE\NTLIS`, `NT SERVICE\NTLIS_TEST` | none                                   |
| Test / drill databases       | `lis_test` (NTLIS_TEST), `lis_drill` (restore drills)    | none                                         |
| Backup credentials file      | `E:\lis-data\postgres.env` (read by `backup-database.ps1` / `restore-database.ps1`) | none              |
| Release staging on a server  | `E:\lis-data\releases\<version>`                         | none                                         |

If a reserved value turns out to be taken when it is first used, pick another LIS-prefixed
value and update this table in the same change.

## 3. Machine toolchain (as found on 2026-10-08)

- .NET SDK **9.0.316** (only SDK installed). Pin with `global.json` once scaffolded.
- Node **v24.18.1**, npm **11.16.0**. Pin with `.nvmrc` and `engines` once scaffolded.
- PostgreSQL **17** at `127.0.0.1:5432` (shared instance; LIS gets its own db and roles
  there, or its own Docker instance on 5433). A PostgreSQL 16 service exists but is stopped.
- Docker Desktop **29.6.1** (daemon was not running at setup time; start it before Compose).
- git **2.55.0**; IIS with the WebAdministration module.

## 4. Domain knowledge base — LDM

`LDM_Manual.html` (repo root) is the engineer's guide to **LDM — Laboratory Data Manager 9.2.0/9.3.0**, National
Technology's existing Oracle Forms LIS. It is the reference for the business domain LIS serves. It is extracted into
`docs/knowledge/ldm/`:

- `LDM_Business_Knowledge.md` — structured synthesis of the business, rules and features. **Read this first** before
  designing any LIS feature; cite its manual section numbers (§x.y) in design docs.
- `LDM_Manual.en.md` / `LDM_Manual.ar.md` — full extracted text; `img/` — the 117 screenshots.

LDM is domain reference material, not code: nothing is imported from it, and LIS is designed on its own merits
(see the "known weaknesses" section of the synthesis). The manual leaves 73 items unconfirmed (its chapter 21);
treat those as open questions for the user, never as facts.

### Sky LIS prototypes — the target product

`Sky-LIS-Client-Portal-Interactive-Prototype-v1.html` and `Sky-LIS-Super-Admin-Production-Prototype-v2 (1).html`
(repo root) are interactive UI prototypes of **Sky LIS**, National Technology's new cloud multi-tenant LIS. Synthesis in
`docs/knowledge/sky-lis/`:

- `SkyLIS_Prototypes_Knowledge.md` — every screen, field and rule in both portals, plus a Sky ↔ LDM terminology map.
- `SkyLIS_vs_LDM_Gap_Analysis.md` — what matches LDM, what Sky adds, what Sky still lacks, by area.

Use Sky LIS terminology (Service / Test / Package, Payer / Contract, Clinical Group, Treasury) in LIS design. The
prototypes cite an SRS that is not in this repo; ask for it before treating a gap as a scope decision.

### Prototypes v2 / v3 (built here)

`prototypes/dist/` holds the rebuilt Sky LIS prototypes that implement the gap-analysis items: the Client Portal
(`Sky-LIS-Client-Portal-Prototype-v2.html`) and the Super Admin Portal (`Sky-LIS-Super-Admin-Portal-Prototype-v3.html`),
plus the **release-1 scoped builds** (`…-v2.1-Release1.html`, `…-v3.1-Release1.html`) produced from the same sources with
`window.SCOPE = "r1"` (client: existing pages enhanced only, payment inside registration, no Location/Room; admin: all
except the integration library). Scope rules live in `app.client.1.js`/`app.client.4.js` (`R1`) and `app.admin.js`.
They are **generated** from `prototypes/src/` (see `prototypes/README.md`): edit the sources, then run
`node prototypes/src/build.js`. Never edit the dist HTML by hand. The design system (`core.css`) carries the brand
palette derived from the logo; keep new screens on it. Preview with the `prototypes-preview` launch configuration
(port 5151). The original v1/v2 prototypes supplied by the user stay in the repo root as reference.

`docs/prototypes/Sky-LIS_Prototype_Comparison.html` compares old vs new prototypes (enhanced pages, new pages, new
modules, dependencies). Generated from `docs/prototypes/src/comparison.data.js` via `node docs/prototypes/src/build.js`.
A PDF twin for hard-copy review is produced with headless Edge: `powershell -File docs/prototypes/src/pdf.ps1 -Html <file.html>`
(no Python on this machine; Edge honours the documents' `@media print` CSS). Regenerate the PDF after every HTML rebuild.

`docs/plan/Sky-LIS_Build_Phases.html` (+ `.pdf`) is the phased build plan (foundation → existing pages enhanced → new
pages → new modules → go-live). Generated from `docs/plan/src/plan.data.js` via `node docs/plan/src/build.js`.

`docs/plan/Sky-LIS_Release-1_Remaining-Points.html` (+ `.pdf`, LIS-RP-001) lists every open point after Release 1 by phase
(A decisions, B pilot prerequisites, C hardening, D acceptance evidence, E Release 2) with stable ids `A-nn`…`E-nn`; hand-written
HTML (no build step), PDF via the same `pdf.ps1`. Update it with the UAT scripts and the runbook.

### Validation

`docs/validation/<date>/` holds a validation campaign: `Sky-LIS_Validation_Report.html` (+ `.pdf`, LIS-VR-001; hand-written,
PDF via `pdf.ps1`) and the detailed result files per area (defect ids `A-nn` client operations, `B-nn` client set-up,
`C-nn` Super Admin and public, `D-nn` backend cycle). `tests/validation/backend-cycle.mjs` (Node, no dependencies) provisions a
client and drives a full laboratory cycle through the API with database assertions; secrets only from git-ignored files.
The validation stack runs beside the installed service: launch configurations `validation-api` (API on 127.0.0.1:5102, via
`--Kestrel:Endpoints:Http:Url`) and `validation-spa` (4300, proxy `frontend/proxy.validation.json`) or, to pin the build under test so working-tree edits cannot
reach it, `validation-ui` (`tests/validation/serve-ui.mjs` serving the git-ignored copy `.local/validation/ui` of a release
build on 4300, same proxy rules); browser sessions are per
host name, so parallel testers use `127.0.0.1` and `localhost` (the built-in browser opens no other names).

### Requirements specification

`docs/requirements/LIS_Requirements_Specification.html` (LIS-RS-001) is the consolidated requirements document:
functional requirements per module (PLT, SEC, CAT, REG, SMP, RES, RPT, WRK, FIN, INT, AUT, DSH, QCM), non-functional
requirements, business rules (BR-xx), gap register, open questions and glossary. It is **generated**: edit
`docs/requirements/src/requirements.data.js`, then run `node docs/requirements/src/build.js`. Never edit the HTML by
hand. Requirement IDs are stable; bump `meta.version` and add a revision-history row on every change. Preview with
the `requirements-preview` launch configuration (port 5150, inside the LIS reserved range).

### Business SRS

`docs/srs/Sky-LIS_Business_SRS.html` (+ `.pdf`, LIS-SRS-001) is the business Software Requirements Specification of
both portals as last built: actors, modules, every page (CP-01…CP-68 client, SA-01…SA-21 admin) with purpose, data,
actions, rules and dependencies, status models, 12 end-to-end workflows (WF-01…WF-12), business rules BR-01…BR-27,
conceptual data model, documents, release-1 scope matrix and glossary. Generated from `docs/srs/src/srs.data.js` via
`node docs/srs/src/build.js`; PDF via the same `pdf.ps1`. Page IDs, WF and BR codes are stable; bump `meta.version`
and add a history row on every change. Preview with the `srs-preview` launch configuration (port 5153).

### UAT scripts

`docs/uat/Release-1_UAT_Scripts.md` (LIS-UAT-001) holds the Release-1 acceptance scripts per persona (ids `UAT-<area>-nn`,
SRS page / WF / BR / intake scenario references, negative tests, Arabic check, pass/fail box) and `docs/uat/README.md` how a
session is run and recorded. Keep script ids stable; add a revision-history row on every change.

## 5. Backend solution (`Platform.sln`)

The backend is built task by task from the **architecture pack in `prompt/`** (README + documents 00–04: ratified
decisions D-01…D-07, blueprint, security spec, intake guide, pitfalls). Those documents are the constitution; the
business content comes from the SRS (`docs/srs`) and the prototypes. The pack cites NT.QAMS as its baseline, but the
QMS source is never read or copied (section 1); everything here is written from the documents.

- Layout (01 §3.2): `src/Platform.{SharedKernel,Domain,Contracts,Application,Infrastructure,WebApi}`,
  `tests/Platform.{ArchitectureTests,Domain.UnitTests}`; module-first folders inside every layer.
- Reference matrix (01 §2.1) is enforced by `tests/Platform.ArchitectureTests/LayerRulesTests.cs` on both the IL and
  the `.csproj` files. Other gates: `DomainHardeningTests` (no public set/init on entities, roots sealed, factories),
  `RequestPolicyTests` (every `ICommand`/`IQuery` carries exactly one policy attribute with a catalogued key; no
  handler for a raw `IRequest`; no `RequireRole`), `GateSelfTests` (proves each gate on deliberately wrong fixtures).
  Add a gate for every new rule; a rule without a test is a suggestion (01 §8).
- Build hygiene: `Directory.Build.props` (net9.0, C# 13, nullable, TreatWarningsAsErrors, analyzers) and
  `Directory.Packages.props` (central versions; MediatR pinned to **12.5.0**, the last Apache-2.0 release, until
  action A-2 closes). `BannedSymbols.txt` makes `DateTime.Now/UtcNow`, `DateTimeOffset.Now/UtcNow` and
  `Guid.NewGuid` compile errors everywhere except `SystemClock` (pitfall P-14).
- Commands: `dotnet build Platform.sln -c Release` · `dotnet test Platform.sln -c Release` (report per-suite counts,
  never a bare total: pitfall P-71). WebApi listens on `http://127.0.0.1:5100` (launch profile); TLS is the proxy's.
- Persistence (Task 2, 01 §4 / 02 §4): `AppDbContext` with `TenantKeyConvention` (every `ITenantScoped` keyed
  `(tenant_id, id)`), `XminConcurrencyConvention` (`uint RowVersion` ↔ the `xmin` system column, no physical column),
  a tenant query filter applied to every tenant type, enum `CHECK` constraints `ck_<table>_<col>_domain`, snake_case
  names (EFCore.NamingConventions). Interceptors in fixed order: `TenantConnectionInterceptor` (session-scoped
  `set_config('app.current_tenant')` + `app.bypass_rls` on every connection open; never `SET LOCAL`), then
  `TenantStampInterceptor` (TENANT-000 on an unresolved tenant, TENANT-403 on a cross-tenant write),
  `AuditStampInterceptor`, `OutboxInterceptor` (events → `ops.outbox_events` in the same transaction). `UnitOfWork` is
  the only type that saves and maps engine refusals to CONCURRENCY-409 / TENANT-403 / UNIQUE-409. Repositories extend
  `Repository<T>` and never save. Identity split (D-06): `identity.platform_users`, `identity.refresh_sessions` (no
  RLS) and `app.tenant_memberships` (FORCE RLS + `tenant_isolation` policy declared in the creating migration).
- Migrations: `dotnet ef migrations add <Name> --project src/Platform.Infrastructure --startup-project src/Platform.Infrastructure --output-dir Persistence/Migrations`
  (local tool in `.config/dotnet-tools.json`; the design-time factory needs no server). After every migration regenerate
  the committed idempotent script: `dotnet ef migrations script --idempotent -o deploy/postgres/migrations.sql --project src/Platform.Infrastructure --startup-project src/Platform.Infrastructure`
  (MigrationScriptTests fails when it is stale). Tenant tables declare RLS in their own migration; migrations never set `app.bypass_rls`.
- Database roles and bootstrap: `deploy/postgres/roles.sql` (lis_owner BYPASSRLS = migrations, lis_app NOBYPASSRLS =
  runtime) and `schema.sql` (schemas, grants, default privileges) run once before the first migration;
  `deploy/postgres/compose.yaml` is the Docker variant (Docker Desktop cannot start on this server: no WSL distribution).
  The working dev server is a **private PostgreSQL 17 instance on 127.0.0.1:5433** under git-ignored `E:\LIS\.local\pg17`
  (initialised from the installed 17 binaries, separate from the shared 5432 cluster). Since 2026-10-10 it runs as the
  Windows service `LIS_POSTGRES` (automatic; `Start-Service` / `Stop-Service LIS_POSTGRES`; never start it with `pg_ctl`
  while the service runs); passwords are in git-ignored `deploy/postgres/.env`. The same instance and database `lis`
  serve the `NTLIS` installation of this host (`docs/deploy/Installations.md`).
- Integration tests (`tests/Platform.IntegrationTests`, real PostgreSQL) run only when `LIS_TEST_OWNER_CONNECTION` and
  `LIS_TEST_APP_CONNECTION` are set (owner/app role connection strings); otherwise they are skipped with a reason, never
  faked. The WebApi reads `Lis:Database:ConnectionString` (user-secrets or environment; appsettings carries no password).
- Security edge (Task 3, 02 §2–§6, D-01/D-02/D-03): Infrastructure `Security/` holds `ConfigGuard` + `SecurityOptions`,
  `MasterKeyProtector` (HKDF + AES-GCM at rest), `Argon2idHasher`, `SigningKeys` (ES256, JWKS), `SubjectProtector`
  (`sub` = `v1.<kid>.<…>`), `AccessTokenService` (claims iss/aud/sub/sid/tid/scope/jti/iat/nbf/exp only, 15 min),
  `RefreshTokenService`, `EnvelopeKeyStore` (RSA-3072 in `identity.envelope_keys`, rotated 24 h, lazy under an advisory
  lock), `NonceStore` (`app.credential_nonces`, UNLOGGED, insert = consume), `CredentialEnvelopeOpener` (CRED-001…005 in
  the specified order), `SessionPrincipalQuery` (one elevated query per request, cached 10 s) and `MembershipDirectory`
  (sign-in tenant selection); both are the only allow-listed `Elevate()` sites (gate in WebApiHardeningTests).
  WebApi: `PipelineOrder.Sequence` is walked by `Program` (ForwardedHeaders → Correlation → SecurityHeaders →
  ExceptionHandler → Authentication → CredentialAccountKey → RateLimiter → AuthOriginCheck → TenantResolution →
  MfaEnrollmentGate → PasswordChangeGate → Authorization → Endpoints; `PasswordChangeGate` refuses every `/api` request but
  `/api/v1/auth/**`, the own profile, password, preferences and privileges with `AUTH-016` while the user must change the
  password, validation C-04); `EncryptedSubjectEvents` decrypts `sub` and loads the session;
  refresh-token reuse allows a grace for the token just rotated (`Lis:Security:Session:RefreshReuseGrace`, default 5 s, max
  30 s: the successor session gets an access token and no new cookie; older generations or later replays revoke the family,
  validation C-02), and the SPA serialises refreshes across tabs with the Web Locks API (`core/auth/refresh-lock.ts`);
  user mobiles are stored in E.164 and unique in every spelling (`IUserMobileNumbers`, validation C-03);
  `RefreshCookie` (`__Secure-rt; Path=/api/v1/auth; Secure; HttpOnly; SameSite=Strict`, `rt` only under
  `Lis:Security:AllowInsecureLocalDevelopment`); `AuthOriginCheckMiddleware` guards the two cookie endpoints with
  Sec-Fetch-Site + Origin + `X-Requested-With: Platform`; `CredentialTransportFilter` is a global action filter; MediatR
  behaviors run Authorization before Validation, and an unreadable or incomplete body is reported (400) only after the
  action's requests are authorized (`ActionRequestPolicies` from the actions' IL, called by `ProblemResponse.InvalidModelState`;
  gates in WebApiHardeningTests; validation F-17); rate
  limits follow the 02 §5.2 matrix (policies `credential-account`, `credential-challenge`, `refresh`, global chain),
  in-process counters. Endpoints: `GET/POST /api/v1/auth/{credential-challenge,sign-in,refresh,sign-out,me}`,
  `/.well-known/jwks.json`, `/health/{live,ready}`. Every setting is read lazily from `IConfiguration` (a test host
  injects configuration at `Build()`); `StartupGuards.RunAsync` enforces forbidden keys, ConfigGuard, loopback bind,
  relaxation event and `DatabaseRoleGuard`. Secrets and rotation runbooks: `deploy/SECRETS.md`; dev secrets:
  `node scripts/new-dev-secrets.mjs` (user-secrets store). Security events currently go to the structured log
  (`LoggerSecurityEventLog`) until the audit ledger lands.
- Test suites: Domain.UnitTests, ArchitectureTests (gates), Application.UnitTests (crypto primitives, ConfigGuard),
  IntegrationTests and WebApi.FunctionalTests (both real PostgreSQL via the two env vars; each process takes the
  advisory lock `lis:test-database` for its lifetime so suites serialize by themselves). Run everything with
  `dotnet test Platform.sln -c Release` and report per-suite counts.
- Frontend (Task 4, 01 §6): `frontend/` is an Angular 22 zoneless workspace (project `platform-web`, Vitest + jsdom,
  TypeScript 6 strict with `noUncheckedIndexedAccess` and `exactOptionalPropertyTypes`, angular-eslint). Dev server on
  `127.0.0.1:4300` proxying `/api`, `/health`, `/.well-known` to the API on 5100 (`proxy.conf.json`), so the browser sees one
  origin. Structure: `core/auth` (`AuthStore`: token in a signal only, `hydrate` = silent refresh at start-up, single-flight
  `refresh`, `isAuthenticated/currentUser/currentTenant/hasPermission`; guards), `core/http` (`csrfInterceptor` sends
  `X-Requested-With: Platform` on every API request, `authInterceptor` bearer + one refresh and replay on 401,
  `credentialEnvelopeInterceptor` seals `[CredentialField]` properties from `core/api/credential-fields.ts` and retries once
  on CRED-002/003, `problemDetailsInterceptor` → typed `ProblemError`), `core/crypto` (`credential-envelope.ts`: WebCrypto-only
  compact JWE seal; `CredentialEnvelopeClient` fetches the challenge), `core/api` (hand-written auth contracts until the
  OpenAPI generator lands), `core/i18n` (lang/dir signals, per-feature JSON in `public/i18n`), `core/state/mutation.ts`,
  `shell/nav.registry.ts`, `features/auth` (login page), `features/home`; `shared/patient` (patient picker, form, demographics
  and lookup API shared by Patients and Registration; reads the `patients` dictionary); `core/release.ts` (`RELEASE_SCOPE`,
  `inReleaseScope`); `AuthStore.updateLanguage` saves the language switch (CP-65) and `hydrate` applies the saved one.
  **Session rules (validation fixes C-01, A-01, 2026-10-11)**: `AuthStore.refreshOnce()` compares user and tenant with the
  tab's profile and never adopts another identity (state cleared, full load of `/login`); the interceptor never replays
  after a failed or foreign refresh; `signOut()` ends with a full page load of `/login` (callers must not navigate) so no
  route-provided facade survives a user change; `core/auth/session-channel.ts` (`SESSION_CHANNEL`, BroadcastChannel
  `platform-session`, `provideSessionSync()` in `app.config.ts`) tells other tabs of a sign-in/sign-out and of a change of
  branch (`branch-changed`; the shell's user menu offers "Change branch" through `SessionBranches` when the user has more than one,
  validation F-05);
  `core/auth/session-navigation.ts` (`BROWSER_NAVIGATION`, `signInUrl`, `meaningfulReturnUrl`, `isLoginRoute`); specs use
  `provideSessionTesting(new FakeBrowserNavigation(), …)` from `core/auth/session-testing.ts`. **Route guard**:
  `shell/nav.guard.ts` `navRegistryGuard` (first `canActivateChild` of the shell route) refuses a URL whose longest
  matching `NAV_REGISTRY` entry lacks the user's permission or is outside `RELEASE_SCOPE`, and a feature whose entries are
  all Release 2; `/account` carries `tenantGuard`; a platform user forced to change password goes to `/admin/profile`.
  `NavEntry.menu` (`false` or a release) marks action-only pages: in scope and guarded but not in the sidebar (e.g.
  `/billing/refunds`, opened from Payments in Release 1) — never mark such a page `release: 'R2'`. A refusal shows
  `shell.pageNotAvailable`; the client home forwards to the dashboard or the first menu entry (`landingEntry`). Other tabs
  of the same session follow a profile change (`profile-changed` on the session channel, `profileChangeInterceptor` for
  `PROFILE_CHANGES` endpoints, `followProfileChanges()` in the shell). A sign-in revokes the session of the refresh cookie
  the browser presented (secret checked). **Idle timeout (validation F-01; FND intake §6b)**: the tenant setting
  `sessionTimeoutMinutes` (tenant, else platform, else 30) is read in `SessionPrincipalQuery`'s one query and sent as
  `AuthProfile.sessionTimeoutMinutes`; `RefreshSessionCommand` revokes a browser session not refreshed for longer
  (`Expired`, `SESSION_REVOKED` info `idle_timeout`) and answers `401 AUTH-017`; an access token already issued stays valid
  until it expires. `core/auth/idle-watcher.ts` (`provideIdleWatch()` in `app.config.ts`, `IdleWarningComponent` in the app
  root, never in `shell/`) shares activity across tabs (`activity`/`renewed` on the session channel), renews an active
  session at most once a minute, warns a minute before the end and signs out to `/login?reason=idle`.
  **Lists (validation v0.5.0, 2026-10-12)**: paged lists sort on the server before paging — `sort`/`direction` query
  parameters, an allow-list per list (`<Module>SortKeys.cs` over `Abstractions/Persistence/ListSorting.cs`: `ListSort`,
  `SortColumns<T>`, text keys case-insensitive via `lower()`, stable tie-breaker), VALIDATION-400 for anything else, and the
  functional gate `Every_allow_listed_sort_key_of_every_register_is_served`; pages opt in with `sortMode: 'server'` /
  `TableColumn.sortKey`; browser-paged lists call `sortRows` before slicing (`TableColumn.sortValue` when the cell text does
  not order). Every `EF.Functions.ILike` passes its escape `"\\"` (gate `SearchPatternGateTests`). A master-data row opened
  without the config's update permission is a read-only "View" drawer. The session profile carries the role and user names
  in both languages (`roleNameAr`, `displayNameEn/Ar`; `core/auth/role-name.ts`, `display-name.ts`). VIP identity is
  masked on the server for users without See-VIP (list, export, picker, similar check as a masked entry; assumption awaiting
  the business owner, PAT Q8 / REG Q12).
  Every route-provided facade re-reads on each visit of its page (`refresh()` from the page constructor). **Validation
  messages**: `VALIDATION-400` problem details carry an optional `rules` entry per field (FluentValidation rule + numeric
  bounds, never request values); `shared/forms/problem-text.ts` renders them field by field in both languages. Commands in `frontend/`: `npm run build`,
  `npm run lint` (ESLint + `scripts/check-boundaries.mjs`), `npm run test:ci`. Rules: no `any`/`$any()`, no web storage, no
  `subscribe()` in components, core/shared never import features, features never import each other.
- Business modules (Task 5, 03 §3–§10, D-04/D-07): the first feature module is **Treasury access** (SRS CP-28/CP-29,
  BR-27), specified in `docs/intake/LIS-INT-TRS-001_Treasury-Access.md` (full 16-section intake; the business owner has not
  yet reviewed it). Pattern for every module: aggregates in `Platform.Domain/<Module>` (state enum, codes `<MOD>-nnn`,
  events, SoD inside the transition methods via `EnsureActorIsNot`), contracts in `Platform.Contracts/<Module>`, commands and
  queries in `Platform.Application/<Module>/{Commands,Queries}` (every request carries `[RequirePermissionPolicy]`; one
  `SaveChangesAsync` per command handler, enforced by `TransitionAtomicityTests`), EF configuration + repositories in
  `Platform.Infrastructure/Persistence/<Module>` (key `(tenant_id, id)`, RLS declared in the creating migration),
  controllers under `Platform.WebApi/Controllers/<Module>` (ISender only), and an Angular feature under
  `frontend/src/app/features/<module>` (API service → facade with `resource()`/`mutation()` → smart page; `*appCan`
  gates buttons, the server decides). Permissions come from tenant roles: `Role` aggregate (`app.roles`, permission keys
  validated against `PermissionCatalog`) assigned to a `TenantMembership` (`role_id`); `SessionPrincipalQuery` joins the role
  so sign-in, refresh and `me` return the same permission set the authorization behavior enforces. Reference numbers
  (`TRQ-yyyy-nnnnn`) come from `IReferenceNumbers` over `app.reference_counters` (atomic upsert). Endpoints:
  `/api/v1/treasuries`, `/api/v1/treasury-access-requests`. Frontend route `/treasury/access`, nav entry gated by
  `treasury.view`, dictionaries `public/i18n/treasury.{en,ar}.json`, device id minted by `core/device` (the only allowed
  localStorage use, documented inline).
- Dev tooling: `tools/Platform.DevTools` (`seed-user <username> <password> [tenantId]`,
  `grant-role <username> <tenantId> <roleNameEn> <perm,perm,…>`, `seed-treasury <tenantId> <code> <nameEn> <nameAr>`,
  `seed-demo-lab <username> <password> [tenantId]` (a complete Release-1 laboratory: branch, catalogue with prices, reasons, treasury, a
  role with every permission, users `<username>` and `<username>.manager`; the dev database `lis` holds one, credentials in
  git-ignored `.local/dev-credentials.md`), `seed-reasons <tenantId>` (cancellation/refund/discount/rejection reasons) with
  `LIS_DEVTOOLS_CONNECTION`, guarded like the API; the test suites truncate the dev database, so reseed after a test run);
  launch configurations `backend-api` (5100) and `frontend-dev` (4300) in `.claude/launch.json`. Stop `backend-api` before
  `dotnet build`: the running API locks the Release binaries. The launch runs with `--no-build`, so build
  `src/Platform.WebApi` without a slot first or the API starts from stale binaries (found 2026-10-10: the outbox could not
  resolve newer event types).
- Foundation (wave 0, slot `found`; intake `docs/intake/LIS-INT-FND-001_Foundation.md`; migration `Foundation`):
  **platform tenant** — `seed-platform-admin <username> <password>` in DevTools (→ `PlatformAdministratorSeeder`: user +
  membership in `TenantIds.Platform` + role "Platform Administrator" with every catalogue key). **Branch of the session**
  (CP-04) — `RefreshSession.BranchId` (inherited through rotation, auto-selected when the membership has one branch),
  `POST /api/v1/auth/select-branch` (`TENANT-005` when not assigned), `ICurrentUser.BranchId`, `AuthProfile.BranchId/BranchIds`,
  `BranchScope.Require(ICurrentUser)` → `TENANT-004` for branch-stamped modules. **Audit** — `audit.field_changes`
  (`FieldChangeInterceptor`: every aggregate/entity/`IFieldChangeTracked` row, before/after, reason from the event),
  `audit.audit_trail` (`AuditTrailInterceptor`: per-tenant sequence under `pg_advisory_xact_lock('audit_chain:<tenant>')`,
  SHA-256 chain over the stored columns), `audit.security_events` (`SecurityEventLedger`, one INSERT per event, nullable
  tenant with its own policy); all append-only (trigger `audit.refuse_change()`); `GET /api/v1/audit/entries` and
  `/verify-chain` [audit.view]. Interceptor order (gated): TenantConnection → TenantStamp → AuditStamp → FieldChange →
  AuditTrail → Outbox. **Outbox processor** — `OutboxProcessor.RunOnceAsync` (claim `FOR UPDATE SKIP LOCKED` + 30 s lease,
  batch 50, back-off `2 s·2^n`, dead letter at 10, tenant scope per event, no elevation) hosted by `OutboxHostedService`
  under `Lis:Outbox:{Enabled,PollInterval,BatchSize,Lease,MaxAttempts}`; policies = `sealed class XPolicy :
  IDomainEventHandler<TEvent>` under `Application/<Module>/Policies/`, registered by `AddApplication`
  (`DomainEventPolicyTests`). **Notifications/OTP** — `INotificationSender`, `INotificationTemplateSource` (codes `otp`,
  `password-recovery`, `portal-url`, `treasury-request`, `panic-value`; defaults `TryAdd`ed after the convention so a
  module adapter wins), `IOtpService` over `identity.otp_challenges` (HMAC digests, 5 min, 5 attempts;
  `Lis:Security:Session:Otp{Length,Lifetime,MaxAttempts}`; the code is logged at Debug only under
  `AllowInsecureLocalDevelopment`); **delivery** `Lis:Notifications:*` (`Infrastructure/Notifications`, LIS-VR-002 §7):
  `NotificationOptions` → `NotificationTransports` per channel — SMS to the HTTP gateway (`Sms:{Url,ApiKeyHeader,ApiKey,
  Sender,BodyTemplate,Timeout}`, JSON POST), e-mail to SMTP (`Smtp:{Host,Port,EnableSsl,UserName,Password,From,Timeout}`,
  `System.Net.Mail`), otherwise the pickup directory (`PickupDirectory`: one file `<utc-stamp>-<channel>-<recipient-hash>.txt`
  with channel, recipient, template, language, subject, body); `DeliveringNotificationSender` renders through the template
  source (so an inactive SA-20 template falls back to the built-in text, TPL-07) and absorbs a gateway failure (logged,
  event 7204); with nothing configured `LoggingNotificationSender` stays the default. `ConfigGuard.InspectNotifications`
  (transport rules everywhere; in Production the pickup folder under `E:\lis-data`). Dev API: `appsettings.Development.json`;
  validation API: launch argument; read codes from the folder (`NotificationDeliveryFlowTests` shows how);
  `PlatformUser.Mobile`; `POST /api/v1/auth/recovery/{start,complete}` (always 202 /
  204 or `AUTH-013`). **Tenant settings** — `app.tenant_settings` + `Platform.Domain.Settings.SettingKeys` (26 typed
  descriptors incl. `timeZone`, the laboratory zone without a client record, `SET-001`/`SET-002`), `ITenantSettings.GetAsync(SettingKeys.X)` (loaded once per scope),
  `GET/PUT /api/v1/settings` [settings.view / settings.manage]. Test host: `ApiFactory` disables the outbox loop
  (`Lis:Outbox:Enabled=false`, override per test) and replaces the sender with `RecordingNotificationSender`
  (`factory.Notifications`) unless the test sets `Overrides["Lis:Notifications:PickupDirectory"]` (then the real sender
  writes pickup files). Frontend: `core/api/{auth,audit,settings}.contracts.ts`, `AuthStore.currentBranch/branchIds/
  selectBranch/startPasswordRecovery/completePasswordRecovery`. `dotnet ef` under a slot needs
  `--msbuildprojectextensionspath .local/build/<slot>/obj/Platform.Infrastructure/`.
- Registration (wave 2, slot `reg`; intake `docs/intake/LIS-INT-REG-001_Registration.md`; migration `Registration`): aggregates
  `Visit` (lines, tubes, preparation answers, transfers), `Booking`, `Batch` in `app.visits` …; `VisitRegistered` is the wave-2
  contract (MODULES.md §3) consumed by Samples, Results and Billing policies; `VisitsController` under `/api/v1/registration`
  (`visits`, `visits/check` dry run, lines add/release/cancel, details, discount, documents labels/job-order, settings,
  service-entry, preparations, quote). Frontend `features/registration`: `/registration/new` (CP-06, payment on the page via
  `POST /api/v1/billing/payments`), `/registration/visits` (CP-10), `/registration/visits/:id` (edit), `…/job-order`,
  `/registration/price-query` (CP-12); the page provisions the cash payer lazily (`POST /api/v1/pricing/payers/default`).
  CP-06 layout = desk console (UX option B, chosen 2026-10-11): patient+visit strip, service entry with typeahead
  (`ui/service-entry.component.ts`, also on CP-12), checklist `ui/registration-readiness`, sticky `ui/registration-action-bar`
  (inside the `appSaveShortcut` host), `ui/visit-flags`; a blocked save sets `attempted` (marks fields) and `focusBlocker()`
  jumps via `BLOCKER_TARGETS`. Keep the page stylesheet under the 4 kB component budget (move blocks into `ui/` components).
  Bookings, batches, transfers and the cancellations register are Release 2 (backend only).
- Results (wave 3, slot `res`; intake `docs/intake/LIS-INT-RES-001_Results.md`; migrations `Results`, `ResultLogSequence`):
  `Result` aggregate (`app.results`, states Ordered → Complete → Reviewed → Verified → Printed, cancel/undo/amend, flags,
  panic, delta, formula) with an append-only `app.result_log_entries` (per-result `sequence`), print lists, QC; policies on
  `VisitRegistered`, `VisitLineCancelled`, `SampleCollected`, `PatientsMerged`, `VisitTransferred`, `AnalyserResultReceived`;
  read port `IResultStatusLookup`; `ResultsController` under `/api/v1/results`. Frontend
  `features/results`: `/results/worklist` (CP-18) and `/results/by-accession?accession=` (CP-19); link with
  `RESULTS_LINKS.accession(acc)`. Results by sample, work queue, QC and analyser landing are Release 2 (backend only).
- Laboratory brand (2026-10-12, migration `LaboratoryLogo`): the top bar shows the client laboratory (logo or initials +
  name), the Sky LIS logo stays in the sidebar. Backend: `Domain/Account/LaboratoryLogo` (one per tenant, `Version` on
  every change, removal keeps the file; `LaboratoryLogoFiles` sniffs PNG/JPEG/WebP ≤ 512 KB, ACC-040…045, ACC-403 for the
  platform tenant), `app.laboratory_logos` (RLS), file-store area `account`, `GET/POST/DELETE /api/v1/account/laboratory/logo`
  (read: any member; write: `settings.manage`), `LaboratoryProfile.LogoVersion`. Frontend: `core/brand/laboratory-brand.service.ts`
  (profile + logo blob → object URL, `refresh()`), `shell/lab-brand.component.ts`; the logo is managed on Setup › Parameters
  (`features/organization/ui/laboratory-logo-card`; the client Parameters page is Release 1: a laboratory sees the groups whose
  `ParameterGroup.release` is in scope — Registration, Printing, Samples and results, Security: R1; Subscription: R2 — and
  auto-collect / auto-receive locked On, `editableFrom: 'R2'`, until the sample pages exist) and shown read-only on the profile page. The site CSP allows `img-src blob:`; specs that render the profile page stub `LaboratoryBrandService`.
- Special-right limits (change request 2026-10-13, migration `SpecialRightsExtended`, intake ORG-001 v1.1 A14–A16): `SpecialRights`
  carries `DemographicsEditUpTo` (`DemographicsEditLimit` Ordered < Complete < Reviewed < Verified < Printed; `AllowsDemographicsEditAt`),
  `CanViewBranchCollections` and `ReportDaysLimit` (null = no limit, 0 = today; `EarliestReportDay(today)`); codes ORG-019/ORG-020; owned
  columns `special_*` on `app.roles`, `override_*` on `app.tenant_memberships` with CHECKs declared in the migration (the enum convention
  skips owned types; the EF model declares no default for the two required role columns — with one, the enum's CLR default Ordered is EF's
  "unset" sentinel and the column default Complete would replace a deliberate Ordered; only the migration carries the defaults). Roles that existed before the migration hold Complete / false / no limit (set them in the role editor); provisioned
  administrator roles hold `SpecialRights.All`. Consumers: Patients (identity demographics edit from a visit), Billing (collections
  report), Reporting (work amount report).
- Demographics lock (Patients intake §17, Registration intake §17): `UpdatePatientCommand.VisitId?`; `Patient.IdentityChangesWith` tells an
  identity change (title, names, gender, DOB/age, condition, national id, nationality) from a contact change; `IPatientResultStatusLookup.
  HighestStatusAsync(patientId, visitId?)` (adapter over `Result`, cancelled ignored, a visit without live results falls back to the
  patient-wide answer) → `PAT-032` (403) when beyond `DemographicsEditUpTo`; `GET /api/v1/patients/{id}/demographics-lock?visitId=`
  feeds the shared `app-patient-form` inputs `identityLocked`/`lockHint`; the visits register (CP-10) opens the patient in a drawer
  (row action "Patient", `patients.edit`, `shared/patient/patient-lookup.api.ts`).
- Collections report (Billing intake v1.2 §14, Q20–Q22): `CollectionsReportQuery(FromDay?, ToDay?, Scope User|Branch, GroupBy
  Branch|User|Method)` [billing.view] → `GET /api/v1/billing/collections-report`; `BIL-044` branch scope without
  `CanViewBranchCollections`, `BIL-045` period before `EarliestReportDay`; domain arithmetic in `Domain/Billing/CollectionsReport.cs`,
  port `ICollectionsReportQueries` (one grouped UNION ALL of posted payments and refunds); SPA dialog
  `features/billing/ui/collections-report-dialog.component.ts`, document `ui/documents/collections-report-document.component.ts`.
- Reporting module (slot `rep`; intake `docs/intake/LIS-INT-REP-001_Reporting.md` v1.1 §14b): `WorkAmountReportQuery(FromDay?, ToDay?,
  GroupBy Day|Payer|ContractTier|User|Doctor|ClinicalGroup|Service|Branch, BranchId?)` [reporting.view] and `ExportWorkAmountReportQuery`
  [reporting.export] over `IWorkAmountReader` (live top-level visit lines, `Visit`/`VisitLine` entity types; aggregation in memory,
  span ≤ 366 days) with `IWorkAmountNames`; codes `REP-001` (period before the days limit), `REP-002` (invalid period), `REP-003`
  (branch not allowed: neither the session branch, nor assigned, nor `organization.view`); `ReportsController` under `/api/v1/reports`.
  Frontend `features/reporting` (`/reports/work-amount`, nav group Finance), `core/api/reporting.contracts.ts`, dictionaries
  `reporting.{en,ar}.json`. Results CP-19 (redesign 2026-10-13): with no case open the page is the query desk (header, criteria card folding into a chip bar
  once a query answers, `criteriaCollapsed`, matching list full width); with a case open one sticky `.context-strip` (accession, patient,
  flags, *Details* disclosure, *Criteria* → the form in an `app-dialog`, *Visits (n)* → the list in an `app-drawer`, *New query*)
  replaces them and the unit tabs (`<app-tabs compact />`), sample chips and action buttons share `.unit-bar`; a query with exactly
  one match opens it (`focusGridWhenLoaded` → first editable cell), several matches go to the list (card or side panel).
- Wallet charge run (W2, intake PLT-001 §16 Q11): `WalletChargeJob` (Infrastructure `Jobs/`, hosted by `WalletChargeHostedService`,
  `Lis:Jobs:WalletCharge:{Enabled,PollInterval}`, default on / 1 h; `ApiFactory` turns it off) charges each due Active client's plan fee once
  per period (row-version guarded, no migration) through `WalletCharger`; `POST /api/v1/platform/wallets/charge-run` = SA-05 "Run charges now".
  SMS usage metering is not built (no per-SMS price: business decision Q12).
- Not yet built: MFA/TOTP endpoints, step-up re-authentication, the retention purge (and other scheduled jobs beyond the wallet charge), the OpenAPI
  document and generated client, and the Release-2 modules (analyser integration UI, auto-verification, quality control,
  claims, financial reports; their backends exist where MODULES.md says so and stay hidden under `RELEASE_SCOPE = 'R1'`).

## 6. Working conventions

- Use the `enterprise-app-architect` skill for design and implementation work.
- Keep this file current: every new port, service, database, folder or external
  dependency LIS claims gets added to section 2 in the same change that introduces it.
- Commit only when asked. Never push to a remote that was not created for LIS.
- **Every push is a versioned release recorded in `README.md`** (user rule, 2026-10-10). Before pushing: add one
  bullet per change under `### Unreleased` in README "Release history" and update any README section the change
  affects; then `node scripts/release.mjs <patch|minor|major>` (bumps `VERSION` + frontend package, commits
  `Release vX.Y.Z`, annotated tag); then `git push` (`push.followTags`). Gates: `.githooks/pre-push` →
  `scripts/check-release.mjs` (enable with `git config core.hooksPath .githooks`) and `VersioningTests`. Never `--no-verify`.
