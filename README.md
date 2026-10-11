# Sky LIS

<!-- version:start -->**Version 0.3.9** · released 2026-10-11 · [release history](#release-history)<!-- version:end -->

Sky LIS is National Technology's cloud, multi-tenant **Laboratory Information System**. One web application serves
two portals:

- **Client Portal**: the laboratory's daily work (registration, patients, payments, worklist, results) and its own
  set-up (roles, users, branches, catalogue, doctors, payers, price lists, treasuries).
- **Super Admin Portal**: National Technology's operation of the platform (clients and subscriptions, plans and
  wallets, global master data, notification templates, platform parameters).

Sky LIS succeeds **LDM** (Laboratory Data Manager), National Technology's existing Oracle Forms LIS. LDM's business
rules are documented in [`docs/knowledge/ldm`](docs/knowledge/ldm), but Sky LIS is designed on its own.

## Contents

- [Status](#status)
- [Technology](#technology)
- [Repository layout](#repository-layout)
- [Getting started](#getting-started)
- [Documentation](#documentation)
- [Deployment](#deployment)
- [Versioning and releases](#versioning-and-releases)
- [Release history](#release-history)

## Status

Work is under way on **Release 1**, whose scope is defined by the release-1 prototype builds in
[`prototypes/dist`](prototypes/dist) (`…-v2.1-Release1.html` for the client, `…-v3.1-Release1.html` for the admin).
Some Release 2 pages already exist in the code. They stay hidden while `RELEASE_SCOPE = 'R1'` is set in
`frontend/src/app/shell/nav.registry.ts`.

Modules in the code (backend `src/Platform.*/<Module>`, frontend `frontend/src/app/features/<module>`):

| Area | Modules |
| --- | --- |
| Platform foundation | Identity and sign-in (credential envelope, ES256 tokens, refresh sessions, OTP recovery), tenancy and row-level security, roles and permissions, branch of the session, audit trail (hash-chained), outbox, notifications, tenant settings |
| Platform administration | Clients (tenants) and subscription, plans, wallets, countries, platform parameters, notification templates, admin dashboard |
| Laboratory set-up | Organization (branches, roles, users), Catalogue (clinical groups, services, tests, sample types, units, comments, doctors, antibiotics, organisms and the other lists), Pricing (payers, contracts, base price lists), Treasury and treasury access requests |
| Laboratory operations | Patients, Registration (visits, accessions, tubes), Samples, Billing (payments, refunds, cash closing), Results (worklist, review, verification, print lists), Integration (analysers, site agents) |

## Technology

| Layer | Stack |
| --- | --- |
| Backend | .NET 9 (SDK pinned in `global.json`), ASP.NET Core, EF Core with Npgsql, MediatR 12.5 (CQRS), Clean Architecture + DDD |
| Database | PostgreSQL 17: database `lis`, row-level security per tenant, owner role `lis_owner` (migrations) and runtime role `lis_app` |
| Frontend | Angular 22 (zoneless, signals), TypeScript 6 strict, Vitest, angular-eslint; Arabic and English (RTL/LTR) |
| Quality gates | Architecture tests (layer rules, domain hardening, request policies, migrations, versioning), unit, integration and functional tests against a real PostgreSQL |

## Repository layout

| Path | Contents |
| --- | --- |
| `src/Platform.SharedKernel` | Primitives shared by every layer (entities, results, clock) |
| `src/Platform.Domain` | Aggregates, state machines, domain events, permission catalogue, one folder per module |
| `src/Platform.Contracts` | Request and response contracts of the HTTP API |
| `src/Platform.Application` | Commands, queries, policies and read ports (MediatR handlers), one folder per module |
| `src/Platform.Infrastructure` | EF Core persistence, migrations, row-level security, security services, outbox, audit |
| `src/Platform.WebApi` | ASP.NET Core host: middleware pipeline, controllers (`/api/v1/...`), health and JWKS endpoints |
| `tests/` | `Platform.ArchitectureTests`, `Platform.Domain.UnitTests`, `Platform.Application.UnitTests`, `Platform.IntegrationTests`, `Platform.WebApi.FunctionalTests` |
| `tools/Platform.DevTools` | Development seeding: `seed-user`, `grant-role`, `seed-treasury`, `seed-platform-admin`, `seed-demo-lab`, `seed-reasons` |
| `frontend/` | Angular workspace `platform-web` (both portals in one single-page app) |
| `deploy/` | PostgreSQL roles, schemas and idempotent migration script, Docker Compose variant, secrets guide (`SECRETS.md`); `windows/`: publish, service, IIS site, backup/restore and smoke-test scripts, `web.config` and production configuration templates |
| `scripts/` | Development scripts (secrets, build slots, release) |
| `docs/` | Requirements, business SRS, build plan, module map, intake specifications, domain knowledge base |
| `prototypes/` | Interactive UI prototypes (sources in `src/`, generated pages in `dist/`) |
| `prompt/` | Architecture pack: ratified decisions, blueprint, security specification, pitfalls |

## Getting started

### Prerequisites

- .NET SDK 9.0.3xx, Node 24 and npm 11 (`frontend/.nvmrc`), git
- PostgreSQL 17, listening on `127.0.0.1:5433` for development

### 1. Enable the release check in your clone

```bash
git config core.hooksPath .githooks
git config push.followTags true
```

### 2. Database

Copy `deploy/postgres/.env.example` to `deploy/postgres/.env` (git-ignored) and set three random passwords. Then:

- **Docker**: run `docker compose up -d` in `deploy/postgres`. The init script creates the roles, the `lis` database
  and its schemas.
- **Local PostgreSQL 17**: as a superuser, run `deploy/postgres/roles.sql`
  (`psql -v owner_pw=… -v app_pw=… -f roles.sql`), then `deploy/postgres/schema.sql` on the `lis` database.

Apply the migrations as `lis_owner`, either with `dotnet ef database update --project src/Platform.Infrastructure
--startup-project src/Platform.Infrastructure` (connection string in `LIS_DESIGN_CONNECTION`) or by running the
idempotent script `deploy/postgres/migrations.sql`.

### 3. Secrets

Configuration is read only under the `Lis` root (environment form `Lis__…`). No secret lives in `appsettings*.json`.
For development:

```bash
node scripts/new-dev-secrets.mjs
```

This fills the WebApi user-secrets store (signing key, subject encryption key, master key, connection string). Key
reference and rotation runbooks are in [`deploy/SECRETS.md`](deploy/SECRETS.md).

### 4. Build and test

```bash
dotnet build Platform.sln -c Release
```

```bash
dotnet test Platform.sln -c Release
```

The integration and functional suites run against a real PostgreSQL. They need `LIS_TEST_OWNER_CONNECTION` and
`LIS_TEST_APP_CONNECTION` (owner and app role connection strings); without them they are skipped with a reason. The
test suites truncate the database they use.

### 5. Run

API on `http://127.0.0.1:5100` (`/health/live`, `/health/ready`, `/api/v1/...`):

```bash
dotnet run --project src/Platform.WebApi -c Release
```

Frontend on `http://127.0.0.1:4300`, proxying `/api`, `/health` and `/.well-known` to the API:

```bash
npm --prefix frontend ci
```

```bash
npm --prefix frontend start
```

Frontend checks: `npm run build`, `npm run lint` and `npm run test:ci` in `frontend/`.

A first administrator (connection string in `LIS_DEVTOOLS_CONNECTION`):

```bash
dotnet run --project tools/Platform.DevTools -- seed-platform-admin <username> <password>
```

A demo laboratory for a browser walk-through (branch, catalogue with prices, treasury, a role with every permission, users
`<username>` and `<username>.manager`; request treasury access as the first and approve it as the second):

```bash
dotnet run --project tools/Platform.DevTools -- seed-demo-lab <username> <password>
```

## Documentation

| Document | Source |
| --- | --- |
| Business SRS (LIS-SRS-001): pages, workflows, business rules | [`docs/srs`](docs/srs) |
| Requirements specification (LIS-RS-001) | [`docs/requirements`](docs/requirements) |
| Build phases plan | [`docs/plan`](docs/plan) |
| Module map and build waves, module pattern | [`docs/build`](docs/build) |
| Intake specifications per module | [`docs/intake`](docs/intake) |
| Old and new prototype comparison | [`docs/prototypes`](docs/prototypes) |
| Domain knowledge: LDM and the Sky LIS prototypes | [`docs/knowledge`](docs/knowledge) |
| Architecture pack (decisions, blueprint, security, pitfalls) | [`prompt`](prompt) |

The HTML documents are generated from `src/*.data.js` files with `node <folder>/src/build.js`; edit the data, not the
HTML.

## Deployment

Release 1 runs on Windows Server: the API as the Windows service `NTLIS` (Kestrel on `127.0.0.1:5100`, virtual
service account, install root `C:\lis-app`), the SPA on the IIS site `lis-ui`, which reverse-proxies `/api`, `/health`
and `/.well-known` to the service on the same origin (URL Rewrite + ARR), and PostgreSQL 17 (`lis`, `lis_owner`,
`lis_app`). The package and the scripts live in [`deploy/windows`](deploy/windows):

| Script | Purpose |
| --- | --- |
| `publish.ps1` | builds `publish/<version>/{api,tools,ui}` + `manifest.json` from `VERSION` (build box) |
| `install-api-service.ps1` | installs or updates `NTLIS` (`-Test`: `NTLIS_TEST` on 5180), validates `appsettings.Production.json`, health probe, `-Rollback` |
| `install-ui-site.ps1` | installs or updates `lis-ui` (`-Test`: `lis-ui-test` on 8081) with `web.config` (SPA fallback, proxy, headers, caching), `-Rollback` |
| `backup-database.ps1`, `restore-database.ps1` | `pg_dump -Fc` into `E:\lis-backups` with retention; restore into a named `lis*` database for drills |
| `smoke-test.ps1` | health, JWKS, SPA and proxy checks after a deployment |

Every script is Windows PowerShell 5.1, idempotent and supports `-WhatIf`. The configuration template
`appsettings.Production.template.json` lists every `Lis:*` key with a placeholder; the architecture test
`DeploymentPackageTests` keeps it aligned with the keys the host reads and keeps the package inside the LIS reserved
names. The step-by-step procedure (first installation, upgrade, rollback, configuration reference, logs,
backup/restore drill, checklists) is the runbook
[`docs/deploy/Release-1_Deployment_Runbook.md`](docs/deploy/Release-1_Deployment_Runbook.md).

## Versioning and releases

Sky LIS follows [semantic versioning](https://semver.org) (`MAJOR.MINOR.PATCH`). The [`VERSION`](VERSION) file at the
repository root is the single source: every .NET assembly takes its version from it (`Directory.Build.props`), and the
frontend package (`frontend/package.json`) carries the same number.

| Change | Bump |
| --- | --- |
| New feature, module, page, endpoint or database migration | MINOR |
| Fix, documentation, tooling, refactoring without a behaviour change | PATCH |
| Release 1 go-live | 1.0.0 |
| After 1.0.0: a breaking API or database change | MAJOR |

**Every push to GitHub is a release, and every release is recorded in this README.**

1. Commit your changes.
2. Under [`### Unreleased`](#unreleased) below, add one bullet per change: what changed and what it means for users
   or developers. Also update any other section of this README that the change affects (layout, setup, modules,
   documents).
3. Cut the release. This moves the bullets under a dated `### vX.Y.Z` heading, bumps `VERSION` and the frontend
   package, commits `Release vX.Y.Z` and creates the annotated tag `vX.Y.Z`:

   ```bash
   node scripts/release.mjs patch
   ```

   Use `minor` or `major` instead of `patch` as the table above says; `--dry-run` previews.
4. Push the commit together with its tag:

   ```bash
   git push
   ```

The pre-push hook (`.githooks/pre-push` → `scripts/check-release.mjs`) refuses a push whose branch tip is not the
tagged release named in `VERSION`, whose README lacks that release's entry, whose version is not greater than the one
on the remote, or whose tag is missing from the push. The architecture test `VersioningTests` keeps `VERSION`, the
assemblies, the frontend package and this README aligned. Never bypass the hook with `--no-verify`.

## Release history

### Unreleased

_Nothing yet._

### v0.3.9 — 2026-10-11

- **Validation report** `docs/validation/2026-10-11/Sky-LIS_Validation_Report.html` (+ PDF, LIS-VR-001): full validation of
  v0.3.8. 1,589 automated tests passed; 469 manual cases on both portals (457 executed, 403 passed); 56 database
  verifications after UI saves; backend cycle 36 / 36 steps with 320 database assertions and 5 / 5 on the installed site;
  49 findings (2 S1, 5 S2, 21 S3, 21 S4) in 14 fix groups. Detailed results per area in the same folder.
- **Backend-cycle script** `tests/validation/backend-cycle.mjs`: provisions a client and drives a complete laboratory cycle
  through the API, asserting the database after every request, plus negative and isolation checks; reruns against any
  base URL (`--deployed --insecure-tls` for the installed site).
- **Validation stack**: launch configurations `validation-api` (127.0.0.1:5102) and `validation-spa` (4300, proxy
  `frontend/proxy.validation.json`) run beside the installed service on the same database.

### v0.3.8 — 2026-10-10

- **Installation on WIN-9S2KO4FA64L**: the site also answers on `https://192.168.80.38:5443` (binding without host name for
  the LAN, firewall rule, origin added to the service configuration).

### v0.3.7 — 2026-10-10

- **Remaining points after Release 1** `docs/plan/Sky-LIS_Release-1_Remaining-Points.html` (+ PDF, LIS-RP-001): every open
  item with its module, purpose and enhancement phase (decisions, pilot prerequisites, hardening, acceptance evidence,
  Release 2).

### v0.3.6 — 2026-10-10

- **Installation on WIN-9S2KO4FA64L upgraded to 0.3.5** (service `NTLIS` and site `lis-ui`; configuration kept; smoke
  test 20 / 20); the development API no longer serves the site.

### v0.3.5 — 2026-10-10

- **UAT dry run, Super Admin personas** (operations admin, master data reviewer, super admin; 10 critical scripts, results
  under `docs/uat/results/`): a user of a deactivated laboratory is refused with `CLI-020`; a laboratory that never set a
  parameter inherits the platform value (password minimum length); platform, organization and catalogue list pages re-read
  on every visit instead of showing the previous session's data; 204 answers of the platform API no longer toast a
  refusal; a platform session can no longer open client pages (every client feature route carries the tenant guard);
  stale admin-home text removed. UAT scripts corrected to v0.4.

### v0.3.4 — 2026-10-10

- **UAT dry run, laboratory personas** (cashier, accounts, samples, technician, unit head; 19 critical scripts, results
  under `docs/uat/results/`): the cash desk re-reads the device's treasury access per user and session instead of once per
  browser session, the refund reasons reload per user, and the result-printing refusal text covers printing. The Cashier
  role gains `catalogue.view` (refund reasons are a catalogue list). UAT scripts corrected to v0.3.

### v0.3.3 — 2026-10-10

- **Installed on WIN-9S2KO4FA64L** (production lane, self-signed certificate): service `NTLIS` from package 0.3.2, IIS
  site `lis-ui` at `https://WIN-9S2KO4FA64L`, the private PostgreSQL instance registered as the service `LIS_POSTGRES`;
  smoke test 20 / 20. Record: `docs/deploy/Installations.md`.
- **Smoke test**: `-SkipCertificateCheck` works (the certificate callback is a compiled delegate; a script block failed on
  the validation thread).

### v0.3.2 — 2026-10-10

- **UAT dry run** (laboratory administrator and reception, 15 critical scripts, results under `docs/uat/results/`): seven
  frontend defects fixed: refusal messages of the branches page, discount cap above 100 % no longer clamped, treasury form
  picks a branch by name, registration without an approved treasury saves unpaid instead of blocking, treasury-access rows
  show the decision time, dry-run refusals of a service line are shown, a sign-out racing a 401 no longer loops back to
  the sign-in page. UAT scripts corrected to v0.2.

### v0.3.1 — 2026-10-10

- **UAT scripts** `docs/uat/Release-1_UAT_Scripts.md` (LIS-UAT-001): 78 acceptance scripts for ten personas with negative
  tests, Arabic checks, results sheet, acceptance criteria and coverage matrices; `docs/uat/README.md` explains a session.
- **Backup and restore drill**: `restore-database.ps1` runs unattended (confirm impact lowered; the production target stays
  behind `-AllowProductionTarget`); drill performed on the private dev instance: dump of `lis` verified and restored into
  `lis_drill` (96 tables, 9 migrations).

### v0.3.0 — 2026-10-10

- **Deployment package** (`deploy/windows`): `publish.ps1` builds `publish/<version>/{api,tools,ui}` with a manifest;
  `install-api-service.ps1` installs the Windows service `NTLIS` / `NTLIS_TEST` from a package into `C:\lis-app`
  (virtual service account, protected `appsettings.Production.json`, health probe, rollback); `install-ui-site.ps1`
  installs the IIS site `lis-ui` / `lis-ui-test` with the `web.config` template (Angular fallback, same-origin reverse
  proxy of `/api`, `/health`, `/.well-known` through URL Rewrite + ARR, security headers, `index.html` no-store, hashed
  bundles immutable); `backup-database.ps1` / `restore-database.ps1` (pg_dump custom format, retention, drill restore);
  `smoke-test.ps1`. All Windows PowerShell 5.1, idempotent, `-WhatIf`.
- **Production configuration template** `deploy/windows/appsettings.Production.template.json`: every `Lis:*` key the host
  reads, secrets as `<SET: …>` placeholders, Kestrel pinned to loopback.
- **Runbook** `docs/deploy/Release-1_Deployment_Runbook.md` (LIS-DEP-001): prerequisites, first installation
  (roles → schema → migrations → secrets → service → site → smoke test → `seed-platform-admin`), upgrade and rollback,
  test lane, configuration reference, logs, backup/restore drill, checklists, open questions.
- **Gate** `DeploymentPackageTests` (architecture suite): the package names no other application, port or generic
  configuration root, stays inside the reserved LIS paths, embeds no secret, and the production template carries every
  configuration key derived from the host source.
- **Service-aware host**: the WebApi calls `builder.Host.UseWindowsService()` (package
  `Microsoft.Extensions.Hosting.WindowsServices`), so it runs under the Service Control Manager as `NTLIS` and logs to
  the Application event log; `install-api-service.ps1` refuses a package built without it.
- **Reserved names** (`CLAUDE.md` §2): install root `C:\lis-app`, service accounts `NT SERVICE\NTLIS[_TEST]`, databases
  `lis_test` / `lis_drill`, `E:\lis-data\postgres.env`, `E:\lis-data\releases\<version>`.
- **Repository layout**: `deploy/` now also holds `windows/` (scripts and templates); `docs/deploy` is new.

### v0.2.2 — 2026-10-10

- **Sign-in**: a return URL of the other portal is ignored, so a platform administrator never lands on a client page.
- **Development**: `seed-reasons` seeds the cancellation, refund, discount and rejection reasons (also part of
  `seed-demo-lab`); `seed-platform-admin` resets the password of an existing administrator.

### v0.2.1 — 2026-10-10

- **Shared patient picker**: the patient lookup and creation form used by Patients and Registration live once in
  `frontend/src/app/shared/patient` (Registration's copy removed).
- **Language**: the top-bar language switch saves the preference on the membership, and the app opens in the saved
  language after sign-in or a page reload.
- **UI kit**: master-data pages accept a `multiSelect` field (a string array of option values).
- **Backend**: Registration's placeholder result-status adapter removed; the Results module's adapter is the only one.

### v0.2.0 — 2026-10-10

- **Registration and Results**: the `Registration` migration reverts cleanly (its Down step drops the billing → visits
  key it adds); the result log carries a per-result `sequence` (migration `ResultLogSequence`); the deploy script gate
  now fails when a migration is missing from `deploy/postgres/migrations.sql`.
- **Registration pages** (Release 1): walk-in registration with payment on the page, search registrations with samples,
  payments, documents, results and cancellation actions, job order, price query. The cash payer is provisioned on first use.
- **Results pages** (Release 1): worklist with turnaround colouring, CSV and bench-list print; results by accession with
  entry per test type, flags, panic and delta handling, review, verification, undo, amend, comments, history, report
  preview and print. Both modules appear in the client menu (Reception, Laboratory).
- **Lookups**: the patient and branch forms read countries and governorates from the anonymous public lists (they were
  refused for laboratory users); the public governorate list can be read unfiltered.
- **Development**: `seed-demo-lab` in `tools/Platform.DevTools` seeds a complete Release-1 laboratory for browser
  walk-throughs; the dashboard shows only the Release-1 tiles and quick actions; the release switch lives in
  `frontend/src/app/core/release.ts`.
- **Versioning and releases**: every push to GitHub is now a semantic-versioned release with a matching git tag.
  `VERSION` is the single version source for the .NET assemblies (`Directory.Build.props`) and the frontend package.
  `scripts/release.mjs` cuts a release (bumps the version, dates the README entry, commits and tags). The pre-push
  hook `.githooks/pre-push` refuses untagged or unrecorded pushes, and the architecture test `VersioningTests` keeps
  the version files and this README aligned.
- **README**: this file is new. It covers the product, status, technology, repository layout, setup, documentation,
  the release process and the release history, and it is updated with every release.
- **Repository**: `CLAUDE.md` records the GitHub remote and the release rules; `.gitattributes` keeps the git hooks
  on LF line endings.

### v0.1.0 — 2026-10-10

- **Initial import** of the Sky LIS workspace to GitHub: the .NET 9 backend (`Platform.sln`: foundation, security
  edge, row-level-security persistence, and the business modules built so far), the Angular frontend for both portals,
  the PostgreSQL deployment scripts, development tools, the test suites, the interactive prototypes, and the
  documentation set (requirements, business SRS, build plan, module map, LDM and Sky LIS knowledge bases,
  architecture pack).
