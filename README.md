# Sky LIS

<!-- version:start -->**Version 0.20.1** · released 2026-10-22 · [release history](#release-history)<!-- version:end -->

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
| Platform administration | Clients (tenants) and subscription, plans, wallets, countries, governorates with their cities / districts / areas, platform parameters, notification templates, sessions and client login logs, admin dashboard |
| Laboratory set-up | Organization (branches, roles, users), Catalogue (clinical groups, services, tests, sample types, units, comments, doctors, antibiotics, organisms and the other lists), Pricing (payers with contacts, contracts, base price lists with Excel import / bulk adjust / print, service prices), payment methods, Treasury and treasury access requests |
| Laboratory operations | Patients, Registration (visits, accessions, tubes, accession details), Samples, Billing (payments, refunds, cash closing), Results (worklist, review, verification, print lists), Integration (analysers, site agents) |

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
| `tools/Platform.DevTools` | Development seeding: `seed-user`, `grant-role`, `seed-platform-admin`, `seed-demo-lab`, `seed-reasons` |
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

A demo laboratory for a browser walk-through (branch, catalogue with prices, payment methods, a role with every permission,
users `<username>` and `<username>.manager`):

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

### v0.20.1 — 2026-10-22

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.20.0 on 2026-10-22 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP, the tracker page, the ranged endpoint and the period dictionary keys checked through IIS. Documentation only; the installed 0.20.0 build is the same code as this release.

### v0.20.0 — 2026-10-22

- **Lab Tracker period filter (from / to).** The tracker's day picker becomes a period picker: *Today* (the laboratory's own day,
  the default), *Yesterday*, *A day…*, *Last 7 days* or *From / to…* — two date-time inputs in the laboratory's wall-clock,
  inclusive to the minute, at most 31 days. `GET /api/v1/tracker/visits` takes `from` and `to` (`yyyy-MM-ddTHH:mm`, together; they
  replace `day`; `VALIDATION-400` for one without the other, an inverted or a longer range) and the page echoes the UTC window it read
  (`fromUtc` / `toUtc`). Counters, strip and chips cover the whole period. Specs: `period-picker.component.spec.ts`,
  `TrackerFlowTests`.

### v0.19.1 — 2026-10-22

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.19.0 on 2026-10-22 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP, the Lab Tracker page and endpoint checked through IIS. Documentation only; the installed 0.19.0 build is the same code as this release.

### v0.19.0 — 2026-10-22

Change request LIS-CR-006 (2026-10-22, `docs/build/CR-2026-10-22_Lab-Tracker.md`): the **Lab Tracker**, chosen from the design canvas as
layouts A (pipeline board) + B (stage lanes). No migration.

- **Lab Tracker (client, CP-69, `/tracker`, first entry of Reception).** One page follows every visit of the day through
  registration → samples → results → payment. A pipeline strip (Registered, Awaiting sample, In the lab, Awaiting verification, Ready,
  Delivered) and *Needs attention* chips (Delayed, Panic, Rejected, Ready but unpaid) filter the list; desk views Reception / Bench
  (→ Worklist) / Verification / Front desk; day picker and a scan-friendly search. **Table** view: one row per visit with the four stage
  cells, the next step as a link to the page that does it (accession details, results by accession, payments), and a time-left pill on
  the turnaround clock, server-sorted and paged. **Board** view: five stage lanes with progress cards. The page refreshes every 30 s
  while visible ("Live · updated n s ago"). Sections outside the user's module rights read "—"; results follow the result rights; VIP
  names are masked. Rules: delivered = printed; a verified visit not fully paid is flagged, never blocked.
- **API.** `GET /api/v1/tracker/visits` [registration.view]: stage, next step, time left and attention flags per visit with day-wide
  counters; filters `day`, `branchId`, `stage`, `attention`, `view`, `search`; sort keys `timeLeft`, `registeredAt`, `accession`,
  `patient`, `stage`. One SQL read with row-level security (502 visits in about 0.1 s on the dev instance). Rules in
  `Platform.Domain/Tracker/VisitProgress.cs`.
- **Documents.** SRS 1.4 (CP-69), UAT 0.8 (UAT-REG-16), intake LIS-INT-TRK-001.

### v0.18.1 — 2026-10-21

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.18.0 on 2026-10-21 (`docs/deploy/Installations.md`): configuration changed (`Lis:Platform:PortalBaseUrl` = `https://192.168.80.38:5443` replaces `PortalDomain`), the demo laboratory registered at `demo-lab`, clean-tag build, verified backup, nothing pending, smoke tests 20 / 20 by name and 20 / 20 by IP, the new addresses checked through IIS. Documentation only; the installed 0.18.0 build is the same code as this release.

### v0.18.0 — 2026-10-20

Change request LIS-CR-005 (2026-10-20, `docs/build/CR-2026-10-20_Portal-Addresses.md`): the Super Admin and the laboratories no longer share
one sign-in; every laboratory has its own address. No migration.

- **Laboratory address.** Each laboratory signs in at `<base>/lab/<slug>` (its name and logo on the card) and only its own members can
  sign in there; anyone else is refused exactly like a wrong password (`AUTH-002`, BR-28). An unknown or inactive address says
  "This laboratory address was not found". Password recovery has its own address per context and returns to it.
- **Super Admin address.** Platform staff sign in at `<base>/admin/login`; a laboratory user there is refused.
- **Find your laboratory.** `<base>/login` and an anonymous `<base>/` ask only for the laboratory address and continue to it.
- **Back to the right door.** Sign-out, the idle timeout and a lost session return to the session's own address; the subscription wizard's
  *Sign in* opens the new laboratory's address with the administrator's username filled in.
- **API.** `POST /api/v1/auth/sign-in` takes `laboratory` (a slug) or `admin: true` — exactly one; the `tenant` field and the laboratory
  choice step are removed. The session profile carries `portalSlug`. New anonymous `GET /api/v1/public/laboratories/{slug}` and
  `/{slug}/logo` (`PLT-404` otherwise). The sign-in rate limit is keyed on the username alone.
- **Setting.** `Lis:Platform:PortalBaseUrl` (absolute URL; `https` and required in Production) replaces `Lis:Platform:PortalDomain`; every
  portal address the system prints (provisioning SMS, subscription result, SA-04, dashboard, CP-65) is `{base}/lab/{slug}`.
- **DevTools.** `register-tenant-client <tenantId> <slug> <nameEn> <nameAr>` gives a tenant seeded without a client record its record and
  address; `seed-demo-lab` registers `demo-lab`.
- **Documents.** SRS 1.3 (CP-03, SA-01, BR-28), UAT 0.7, intakes FND-001 1.2, PLT-001 1.3, ACC-001 1.1, runbook.

### v0.17.1 — 2026-10-20

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.17.0 on 2026-10-20 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP. Documentation only; the installed 0.17.0 build is the same code as this release.

### v0.17.0 — 2026-10-20

- **New sign-in page (design 5c "light premium", chosen 2026-10-20).** The login screen is redesigned: the brand light ground (soft blue and violet light, a fine dot grid, the brand gradient line on top), the Sky LIS logo large at the top, a welcome headline ("Welcome back to the bench." / "مرحبًا بعودتك إلى المختبر.") beside a white sign-in card, and a footer with the public site and *Subscribe now*. Everything the page did is kept: language switch, show / hide password, forgot password, the laboratory choice for multi-laboratory users, the idle-session notice, the sealed-password note. Fully mirrored in Arabic, single column on a phone. Uses the app's own fonts (the site policy allows only self-hosted fonts).

### v0.16.1 — 2026-10-20

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.16.0 on 2026-10-20 (`docs/deploy/Installations.md`): clean-tag build, migration `TreasuryRemoved` applied (17 of 17), verified backups before the migration and before the swap, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP. Documentation only; the installed 0.16.0 build is the same code as this release.

### v0.16.0 — 2026-10-20

Change request LIS-CR-004 (2026-10-20, `docs/build/CR-2026-10-20_Treasury-Removal.md`): business owner decision after the first use
of 0.15.2 — *Payments* back in the menu and **the treasury removed from the system**. Migration `TreasuryRemoved`.

- **Payments is back in the sidebar** (Finance › Payments), with the collections report (*Print collections*) inside it.
- **No treasury anywhere.** Payments, refunds and batch payments are recorded for the signed-in cashier on the session branch:
  no treasury to choose, no device, no access request, no approval (BR-27 withdrawn). The registration payment panel takes the money
  at once; typing 0 saves the visit unpaid. Receipts, refund receipts, closing sheets, the registers and their CSV files no longer
  show a treasury or a device. An old browser tab that still sends `treasuryId` / `deviceId` keeps working (the fields are ignored).
- **Cash closing (Release 2) per cashier and branch:** preview, close and the cashier list read the session branch; one cashier or
  all cashiers of the branch over a period. The dashboard's *cash of the day* counts payments and refunds by their own branch.
- **Removed:** the Treasuries and Treasury access pages, `/api/v1/treasuries`, `/api/v1/treasury-access-requests`,
  `/api/v1/billing/my-treasuries` and `/api/v1/billing/treasuries` (404), the `treasury` permission group (purged from every role
  and user override by the migration), the `treasury-request` notification code, DevTools `seed-treasury`, and the SPA device
  identity (the SPA now uses no browser storage at all). Codes BIL-006, BIL-020, BIL-021, BIL-022, BIL-409 are retired.
- **History kept:** `app.treasuries` and `app.treasury_access_requests` stay in the database; the `treasury_id` / `device_id` columns
  of payments, refunds and cash closings become nullable without foreign keys; old payments keep their values.
- **Documents:** Business SRS 1.2 (CP-28, CP-29 and BR-27 withdrawn, WF-09 without the treasury step), UAT scripts 0.6 (six treasury
  scripts withdrawn, CSH-08 and REG-09 rewritten), intakes TRS-001 1.2 (withdrawn), BIL-001 1.4, REG-001 1.5; the module pattern now
  points at the payment-methods slice as its reference.

### v0.15.3 — 2026-10-20

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.15.2 on 2026-10-20 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP. Documentation only; the installed 0.15.2 build is the same code as this release.

### v0.15.2 — 2026-10-20

- **Accession details: no *Cancel* on a verified line.** A line — or a package through any of its members — with a verified or printed result shows "Verified — cannot be cancelled" instead of the *Cancel services* button (the server already refused the cancellation with `REG-106`; the page no longer offers it). Lines whose results are still in entry, review or without a result keep the button. Reported on 2026-10-20 on the installed 0.15.0.

### v0.15.1 — 2026-10-19

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.15.0 on 2026-10-19 (`docs/deploy/Installations.md`): clean-tag build, migration `PrototypeGaps` applied (16 of 16), verified backups before the migration and before the swap, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP, new public lookup and served bundle verified through IIS. Documentation only; the installed 0.15.0 build is the same code as this release.

### v0.15.0 — 2026-10-19

Change request LIS-CR-003 (2026-10-19, `docs/build/CR-2026-10-19_Prototype-Gaps.md`): the *Not built* / *Partial* items of the
client v3 / admin v4.1 prototype difference, migration `PrototypeGaps`.

- **Client login logs (Super Admin, SA-08b).** Administration › *Client login logs* lists the client users' browser sessions —
  one row per session family with client, user, login and logout (UTC), IP address, device, OS, browser, duration and status
  (Active / Signed out / Revoked with its reason / Expired) — with filters (client, user, login and logout windows, device,
  duration bounds, status), server sorting and paging, and a CSV export (`GET /api/v1/platform/login-logs[/export]`,
  `platform.view`). No new data: the facts were already kept by the session store.
- **Accession details (client, CP-10b).** Reception › *Accession details* (`/registration/accession?accession=` or `?visitId=`)
  shows one visit in one place: a search strip (accession, patient number, mobile or name), a sticky header with the patient
  drawer, flags, status, branch, payer and doctor, previous / next accession, and cards for the ordered tests (with *Cancel*),
  samples (*Label*, *Collect*), results (status per unit, *Enter / Open results*), payments (account figures, receipts, *Take
  payment*, *Refund*, *Receipt*) and documents. Reached from the visits register (*Details*) and from the registration success
  panel. Every card asks only with its module's permission; a refusal leaves the card empty.
- **Menu change (C11).** Treasuries, Treasury access and Payments leave the sidebar (still in scope and guarded, reached from the
  pages that need them): the registration page's *no treasury* warning links to Treasury access, Treasury access and Treasuries link
  to each other, the accession page's Payments card links to the payments register, and the dashboard quick action *Payments* stays.
- **Service sequence reordering (client, CP-42b).** On Setup › Service sequence the rows move by drag and drop, ↑ / ↓ buttons,
  Alt+↑ / Alt+↓ or a typed position; a clinical-group filter stands beside the type filter; the visible rows keep the set of numbers
  they already occupy, so a reorder inside a filter never touches the rows outside it; *Save order* / *Discard* with a dirty strip,
  F10 saves, leaving the page asks first. The server refuses one number for two listed services (`CAT-033`).
- **Locations (Super Admin, SA-11b).** On Governorates a *Locations* action opens the governorate's cities, districts and areas —
  three linked columns with search, add, rename and activate / deactivate (`tenancy.manage`; tables `saas.cities`, `saas.districts`,
  `saas.areas`, endpoints under `/api/v1/platform/governorates/{id}/cities` … and the public lookups `/api/v1/public/cities|districts|areas`).
  No rows are seeded: the business owner enters the cities of each governorate.
- **Patient form: pick lists and a second phone (C3 / A2).** When the governorate has cities, the city, district and area are picked
  from the platform's lists (free text stays otherwise); a second phone sits beside the mobile and feeds the similar-patient check.
  The file, the register, the picker and the CSV export show them (`app.patients.city_id/district_id/area_id/second_phone`).
- **Payment methods (client set-up, CP-30b).** Setup › *Payment methods*: the laboratory defines its own methods (code, names, kind,
  "requires transaction reference", order), seeded with Cash, Card, POS and Bank transfer for every laboratory (existing ones by the
  migration, new ones on provisioning); deactivate instead of delete, at least one active cash method (`BIL-052`); CSV export.
  The cash desk and the registration payment panel offer the methods by name and ask for a reference only when the chosen method
  requires it; receipts, invoices and the payment / refund registers name the method. API: `/api/v1/billing/payment-methods`
  (`options`, `export`, `defaults`, update, deactivate / reactivate); payments, refunds and batch payments accept an optional
  `methodCode` (`app.payments.method_code`, `app.refunds.method_code`).
- **Payer contacts (C19).** Payers carry two phones, WhatsApp and e-mail (payers page *Contact* block and CSV export); a malformed value
  is refused (`PRC-017` / `PRC-018`).
- **Price list tools (C21).** The base price list editor gains *Download Excel* (the lines with an empty "New price" column), *Upload
  Excel* (every row checked first, then imported all-or-nothing, `POST …/base-price-lists/{id}/import`), *Bulk adjust* (percentage or
  amount with rounding, previewed then applied, negative results held at 0, `POST …/{id}/adjust`) and *Print*.
- **Service prices (client, C22).** Finance › *Service prices*: every price of one catalogue service across the base price lists
  (editable inline) and the contract tiers (`GET /api/v1/pricing/service-prices/{serviceId}`, `pricing.view`).
- **Dependencies.** The SPA takes `exceljs` 4.4.0 (loaded on demand for the Excel tools).

### v0.14.1 — 2026-10-14

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.14.0 on 2026-10-14 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP; the demo laboratory's administrator flagged as primary administrator so the new self-approval applies there. Documentation only; the installed 0.14.0 build is the same code as this release.

### v0.14.0 — 2026-10-14

- **The laboratory administrator approves their own treasury access request.** On Treasury access, the primary
  administrator of the laboratory (the account that subscribed or was provisioned as the main administrator) now sees
  *Approve* on their own pending request and may approve it — a one-person desk needs no second approver. Everyone else
  keeps SOD-TRS-001 (the server refuses a self-approval with 403), and nobody can reject their own request (withdraw it
  with *Cancel*). The session profile carries `isPrimaryAdministrator`.

### v0.13.2 — 2026-10-14

- **Installation record.** WIN-9S2KO4FA64L upgraded on 2026-10-14 to 0.13.0 (configuration changed:
  `Lis:Notifications:RevealCodes = true` while no SMS gateway exists) and, two minutes later, to 0.13.1
  (`docs/deploy/Installations.md`): clean-tag builds, verified backups, nothing pending, smoke tests 20 / 20 by name
  and by IP each time; the rollback folders now hold 0.13.0. The runbook's configuration table dates the setting to
  0.13.0. Documentation only; the installed 0.13.1 build is the same code as this release.

### v0.13.1 — 2026-10-14

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.12.0 on 2026-10-14 (`docs/deploy/Installations.md`): clean-tag
  build, verified backup, nothing pending in the migrations, configuration unchanged, smoke tests 20 / 20 by name and
  20 / 20 by IP. The first start was refused by the start-up guard because of a stray machine-wide variable
  `Auth__CredentialEnvelope__Mode` set earlier that day; the administrator removed it (API down about eight minutes).
  The runbook's upgrade steps now say to check the machine environment for `*CredentialEnvelope*` first.
  Documentation only; the installed 0.12.0 build is the same code as this release.

### v0.13.0 — 2026-10-14

- **Subscription wizard shows the verification code (interim).** Until an SMS gateway is connected, the public
  *Subscribe* page shows the one-time code sent to the manager's mobile right on the verification step (the person still
  types it). Controlled by the host setting `Lis:Notifications:RevealCodes` (off by default; the start-up guard refuses
  it once `Lis:Notifications:Sms:Url` is configured, and logs a warning while it is on). The start / resend answer of
  `/api/v1/public/subscriptions` carries `verificationCode` on such a host; password recovery is unchanged.

### v0.12.0 — 2026-10-14

- **Tests: parent services on the test page (CP-41, change request 2026-10-14).** The test editor's general tab lists
  the services that carry the test (code, name, type, "from the service" when the service carries exactly this one
  test) and lets the laboratory add the test to a profile or a culture, or take it out, saved with the test
  (`PUT /api/v1/catalogue/tests/{id}/parent-services`, `catalogue.edit`). Each change rewrites the members of the
  affected service under its own rules, so a test service keeps its single test (`CAT-021`), a package takes no test
  (`CAT-022`) and an inactive test joins nothing (`CAT-027`); one refusal changes nothing. `LabTestDetail` gains
  `parentServices`. Same page on both portals (Setup › Tests and Master data › Tests). Intake CAT-001 v1.1 (T21,
  Q17), SRS 1.1, UAT-ADM-08 step 13.
- **Results by accession: what the result rights hide (change request 2026-10-14).** A report unit whose results
  belong partly to a clinical group outside the user's rights (a print-alone service such as Urine C/S with a chemistry
  member) shows a note with the count and the groups to ask for, and the case header counts those results beside the
  hidden units (`ReportUnitView.hiddenResults` / `hiddenGroups`, `ResultCaseView.hiddenResults`). Found on
  ACC-2026-00052: the culture row was invisible with no explanation.
- **New clinical groups reach the administrators' result rights by themselves.** When a clinical group is created, every
  active role that manages roles (`organization.manage`, held by the provisioned Laboratory Administrator) receives see,
  review, verify, edit and print on it in every branch through the outbox policy `GrantNewClinicalGroupToAdministratorsPolicy`;
  other roles keep their deliberate rights and the platform catalogue takes none. Groups created before this release
  still need the Result rights tab once.
- **Review stack for the built-in browser.** Launch configurations `review-api` (API on 127.0.0.1:5103 from the default
  Release build, pickup folder `.local/mail-pickup/review`) and `review-spa` (4300, `frontend/proxy.review.json`) let a
  change be checked beside the installed service and the other dev stacks.

### v0.11.1 — 2026-10-13

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.11.0 on 2026-10-14 (`docs/deploy/Installations.md`): clean-tag build, verified backup, migration `MembershipResultsDensity` applied from the console before the service swap, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP, sign-in and `me` verified on the new principal query. Documentation only; the installed 0.11.0 build is the same code as this release.

### v0.11.0 — 2026-10-13

- **Results by accession: previous / next visit from the strip.** Two arrow buttons (and Ctrl+Alt+← / →) move to the
  neighbouring visit of the matching list without opening it — across its pages — or, when the open visit is not in a
  list (opened from a link), to the accession one lower or higher, looked up first so a missing one only says so.
  Entered values not yet saved are confirmed away before leaving a visit (also when a row of the list is chosen).
- **Results by accession: smaller details.** The keyboard and colour hints under the grid fold behind the *i* button of
  the unit bar; a *Compact rows* toggle (saved as a preference of the user, so every device follows) fits a long
  profile on one screen; on narrow screens the strip stays pinned and its sub line and flags give way.
- **Preferences endpoint.** `PUT /api/v1/account/preferences` accepts `language` and / or `resultsDensity`
  (`comfortable` | `compact`); the profile carries `resultsDensity`. Migration `MembershipResultsDensity` (one nullable
  column on `app.tenant_memberships`, code `TEN-014`).

### v0.10.1 — 2026-10-13

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.10.0 on 2026-10-14 (`docs/deploy/Installations.md`): clean-tag build, verified backup, no migration in the release, configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP. Documentation only; the installed 0.10.0 build is the same code as this release.

### v0.10.0 — 2026-10-13

- **Results by accession: the results start at the top.** With a visit open, one sticky strip (accession, patient, sex and age,
  VIP / urgent, rights flags, *Details*) replaces the page header, the criteria card and the matching list; *Criteria* opens the
  query form in a dialog, *Visits (n)* opens the matching visits in a side panel, and the unit tabs, sample chips and the
  Save / Review / Verify / Undo / Preview / Print buttons share one row above the grid. A query with exactly one match
  opens it at once with the cursor on the first editable result; several matches go to the list (card or side panel).
  The first result row moves from roughly 470 px to about 110 px below the top bar.

### v0.9.1 — 2026-10-13

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.9.0 on 2026-10-13 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending in the migrations (`SpecialRightsExtended` had been applied from the console beforehand), configuration unchanged, smoke tests 20 / 20 by name and 20 / 20 by IP. Documentation only; the installed 0.9.0 build is the same code as this release.

### v0.9.0 — 2026-10-13

- **Three new special rights (Roles & privileges, per-user overrides).** *Demographics editable up to* (a result status:
  Ordered, Complete, Reviewed, Verified, Printed), *Treasury report of the whole branch* and *Report time limit (days)*
  (empty = no limit, 0 = today only). Roles that already exist receive Complete / off / no limit from the migration
  `SpecialRightsExtended`; an administrator raises them in the role editor. Newly provisioned administrator roles hold
  everything. Codes ORG-019 / ORG-020.
- **Patient demographics from Search registrations.** The visits register gets a *Patient* row action (right
  `patients.edit`) that opens the patient file in a drawer: contact fields are always editable; identity fields (title,
  names, gender, date of birth or age, condition, national id, nationality) stay editable only while the highest result
  status of that visit is within the user's *Demographics editable up to* right — beyond it they are shown read-only with
  the reason, and the server refuses a change with `PAT-032`. The same lock applies on the patient file (highest status
  over every visit of the patient). The change reflects on the patient record at once (names and mobile in the register).
- **Treasury (collections) report on Payments.** *Print collections* on the Search payments tab: period of whole
  laboratory days (default today), scope *Own payments* or *Whole branch* (the latter needs the new branch right,
  `BIL-044`), grouped by payment method (default), cashier or branch, with posted payments, refunds and net per method
  (Cash, Card, POS, Transfer) and totals; printed as an A4 document. A period starting before the user's report time
  limit is refused (`BIL-045`). `GET /api/v1/billing/collections-report`.
- **Work amount report** (new page Finance › *Work amount report*, right `reporting.view`): the booked income of the
  visits registered in a period of laboratory days (default today) — visits, services, gross, discount, cash income
  (patient share), credit income (payer share) and net — without cancelled services, grouped by day, payer, contract
  tier, registering user, referring doctor, clinical group, service or branch, with a totals row, A4 print and CSV
  export (`reporting.export`). A period starting before the user's *Report time limit* is refused (`REP-001`); a
  reversed or longer-than-366-day period `REP-002`; a branch the user may not report on `REP-003`.
  `GET /api/v1/reports/work-amount` (+ `/export`).
- **Results by accession: criteria fold away.** Once a query has answered, the criteria card collapses into a one-line
  bar of chips (accession, patient, statuses, dates, flags) with *Change criteria*, so the matching visits and the case
  get the height; focus moves to the first matching visit; *Change criteria* or *New query* opens the form again with
  the typed values; F8 still reruns the query while folded.
- **Runbook: applying `migrations.sql` from a Windows console.** The generated script starts with a UTF-8 byte-order mark that
  psql skips only in UTF-8 mode; the runbook now sets `PGCLIENTENCODING=UTF8` before `psql -f` (a console on a legacy code page
  otherwise stops at line 6 with `syntax error at or near "ï»¿DO"`, before touching anything).

### v0.8.1 — 2026-10-13

- **Installation record.** WIN-9S2KO4FA64L upgraded to 0.8.0 on 2026-10-13 (`docs/deploy/Installations.md`): clean-tag build, verified backup, nothing pending in the migrations, configuration extended with the notification pickup folder and the wallet charge job, smoke tests 20 / 20 by name and 17 / 17 by IP. Documentation only; the installed 0.8.0 build is the same code as this release.

### v0.8.0 — 2026-10-13

- **Session timeout enforced (validation F-01).** The parameter *Session timeout (minutes)* now ends idle sessions: the
  server refuses a refresh after the session of a laboratory (or of the platform) went unrenewed longer than its timeout
  (`AUTH-017`, session revoked), and both portals warn a minute before the end ("Stay signed in / Sign out"), keep the
  session alive while the user works in any tab, and sign out to the sign-in page with "Your session ended after
  inactivity". No migration.
- **Report template logo, signature and preview (validation F-02).** SA-12 report templates get a row action "Logo,
  signature and preview": a thumbnail of each image, upload / replace / remove for `catalogue.edit` (PNG, JPEG or WebP up
  to 512 KB, decided by the bytes; `CAT-270…272`, `CAT-394`) and a sample result report drawn by the results report
  renderer. The images are Files-module files (`…/report-templates/{id}/images/{logo|signature}`), served only to
  signed-in members of the tenant; a printed result report shows the template logo in its letterhead and the signature on
  a verified report. A client provisioned from the platform catalogue gets its own copy of the images. The template
  create / edit request no longer carries `logoFileId` / `signatureFileId`. No migration.
- **Open points of the validation report closed (LIS-VR-002 §6, §7, §10).** Every point still open after v0.6.0 is fixed
  with a test; where the specifications gave no rule, the chosen rule is recorded in the intake as an assumption awaiting the
  business owner (test-level duplicates, treasury branches, lock-out on a wrong current password, on-screen digits, VIP).
- **Notification delivery.** `Lis:Notifications:*` selects a pickup directory (one file per message; dev
  `E:\LIS\.local\mail-pickup`, production `E:\lis-data\mail-pickup`, protected ACL set by the installer), SMTP e-mail
  and a generic HTTP SMS gateway; inactive templates fall back to the built-in text. The subscription wizard, password
  recovery and the template fallback can now be completed without a gateway.
- **Treasury access.** Approving takes an optional comment; the comment and the decision reason show on the request rows
  and in a new CSV export; a request is limited to treasuries of the user's branches (`TRS-015`); a device approval binds
  the person on that browser (one device id per user); payer, tier, base-price-list and treasury edits are audit-trail events.
- **Catalogue and privileges.** A list value is Normal or Critical, not both (`CAT-061`); Release-2 modules are hidden in
  the role editor and the user overrides (held keys are kept); the report-template field is hidden where report templates are
  not delivered; the dial code is checked as typed; doctors list their branch codes; formulas take the patient's age and sex
  (`@AGE`, `@FEMALE`).
- **Registration and results.** A service whose tests are all already ordered is refused (`REG-109`), one that adds a new
  test is accepted with a warning (`REG-206`); labels carry a BIO mark for a biohazard visit; tube counts read in the
  singular, dual and plural; the new-registration header names the branch; VIP names are masked on the worklist and the
  Results-by-accession list; on-screen dates use the language's digits (printed documents keep Latin digits); Egyptian
  mobiles are accepted with the trunk 0.
- **Wallet charge run.** A hosted job (`Lis:Jobs:WalletCharge:{Enabled,PollInterval}`) and "Run charges now" on SA-05
  (`POST /api/v1/platform/wallets/charge-run`) charge each due, active client its plan's monthly fee once per period
  (guarded by the wallet's row version), move the next charge one month and write a ledger row; trial and inactive clients
  are skipped, a currency mismatch is reported (`WALLET_CHARGE_FAILED`). SMS usage metering waits for a per-SMS price
  (business decision, PLT intake Q12).
- **Branch and roles.** "Change branch" in the user menu for users with several branches (other tabs follow); data
  migration `RemovePlatformKeysFromLaboratoryRoles` removes platform-only keys from laboratory roles and overrides, and
  the demo seed no longer grants them; the privilege counter counts only listed keys; a reopened role editor shows no old
  refusal; "Use default" removes the stored value (`SettingValueItem.Value` null); a caller without the right gets 403
  before any 400 about the body.
- **Results, documents and digits.** An opened VIP case and its preview are masked without See-VIP, and printing a VIP
  report needs the right (`RES-056`); the patient form opens on the laboratory's country and mobile search matches with
  or without the trunk 0; Arabic screens use Arabic-Indic digits everywhere while printed documents (job order included)
  use Latin digits; a report's captions, language and direction follow the report language; the worklist's delayed
  counter and filter follow the late threshold of the row colour.
- **Wording.** One count rule for both languages (`I18nService.tc`: none, one, two, few, many), no "(s)" plurals left
  (a dictionary check enforces it), and the "not saved" notice on every editor including the Super Admin forms.
- **Forms and messages.** Full-page editors (user, test, service, patient) show the "not saved" notice on a blocked save;
  duplicate username, e-mail, mobile and employee code show under their fields; an unknown time zone is `SET-003` with its
  own message; a wrong current password counts toward lock-out like a wrong sign-in (documented rule SEC-LOCK-02).

### v0.7.2 — 2026-10-12

- **Laboratory parameters in Release 1.** Setup › Parameters now shows a laboratory four groups next to its logo:
  Registration, Printing, Samples and results, and Security and general (Subscription stays Release 2). Holders of
  `settings.manage` edit them. "Mark samples Collected" and "Mark samples Received" stay locked On until the sample
  pages of Release 2, because switching either off would block result entry (BR-03, RES-010) with no screen to
  recover. Each parameter group now carries the release a laboratory sees it in (`ParameterGroup.release`), and a
  parameter can be locked until a release (`editableFrom`).

### v0.7.1 — 2026-10-12

- **The laboratory logo moved to Setup › Parameters.** It is one logo for the whole laboratory, a laboratory setting
  rather than a personal one, so it is managed where the laboratory's settings live. Setup › Parameters is now in the
  Release 1 menu (permission `settings.view`), but in a laboratory it shows only the "Laboratory logo" card until
  Release 2; the Super Admin portal keeps its full parameter page. The profile page shows the logo read-only, with a
  "Change it in Setup › Parameters" link for holders of `settings.manage`.
- **Site installer recycles the app pool.** `install-ui-site.ps1` used to only start the `lis-ui` app pool after swapping
  the content folder, which does nothing when the pool is already running. The worker then kept serving the previous
  `web.config`, including its old CSP, until the next recycle; this was found when installing 0.7.0. A running pool is
  now recycled, so a new `web.config` takes effect immediately.

### v0.7.0 — 2026-10-12

- **The top bar shows the client laboratory instead of the Sky LIS brand.** It shows the laboratory's logo, or a badge
  with its initials ("DL" for "Demo Laboratory"; the Arabic article "ال" is skipped), with the laboratory's name in the
  language of the page. The Sky LIS logo stays at the top of the sidebar, and the Super Admin portal shows nothing there.
- **Laboratory logo on the profile page (CP-65).** A holder of `settings.manage` uploads, replaces or removes the
  laboratory's logo; every user of the laboratory sees it in the top bar at once. The logo must be PNG, JPEG or WebP
  (decided by the file's bytes, not its name; SVG is refused because it can carry script), up to 512 KB. Refusal codes:
  ACC-040 empty, ACC-041 too large, ACC-042 wrong type, ACC-045 no logo, ACC-403 platform tenant. Endpoints:
  `GET`/`POST`/`DELETE /api/v1/account/laboratory/logo`; the profile carries `laboratory.logoVersion`.
- **Migration `LaboratoryLogo`:** new table `app.laboratory_logos`, one row per laboratory, with row-level security.
  Images are kept in the file store (area `account`); a replacement or removal never deletes a stored file.
- **Site CSP:** `img-src` now allows `blob:` (`deploy/windows/web.config`), because the logo is fetched with the
  sign-in token and shown through an object URL. A reinstall of the site applies it.

### v0.6.0 — 2026-10-12

- **Validation of v0.5.0: every finding fixed** (report `docs/validation/2026-10-11-v0.5.0/Sky-LIS_Validation_Report.html`,
  LIS-VR-002). 30 distinct defects (1 × S2, 9 × S3, 20 × S4) from three browser testers plus the backend cycle; each fix
  has a test and was re-tested in the browser and in the database.
- **Two tabs no longer sign each other out (C-02/A-04).** Tabs of one browser take turns to refresh (Web Locks), and the
  server accepts the refresh token it has just rotated for a grace window (`Lis:Security:Session:RefreshReuseGrace`,
  default 5 s, at most 30 s): the successor session gets an access token and no new cookie. Older generations and later
  replays still revoke the whole family. New key in the production template.
- **A forced password change is enforced by the server (C-04).** While a user must change the password, every `/api`
  request except the auth endpoints, the own profile, password, preferences and privileges is refused with
  `403 AUTH-016` (new pipeline step `PasswordChangeGate`). The changing tab now leaves the forced-change view (C-07).
- **Export permissions granted to existing roles (C-01).** Migration `GrantExportPermissions` gives
  `organization.export` / `treasury.export` to every role holding the module's view and manage keys and every catalogue
  key to the Platform Administrator role; a gate keeps that role complete.
- **User mobiles in international form (C-03).** Admin and laboratory user mobiles are stored in E.164 by the country
  rules, are unique in every spelling, and password recovery finds the account whichever spelling was stored or typed.
- **Clinical delta check re-run at sign-off (A-02).** Review and verification re-check the delta against the latest
  verified previous result, and verifying a result refreshes the patient's open results of the same test (BR-11).
- **VIP identity masked everywhere for users without the See-VIP right (A-03).** Patient register, export, picker and
  similar-file check mask name, mobile, national ID, e-mail and address; name and mobile search exclude VIP files, the full
  number or national ID still finds them. Recorded as an assumption awaiting the business owner in the PAT and REG intakes.
- **Registration draft cleared after a save (A-01, S2)**, one digit set in Arabic (A-07), the services card marked on a
  blocked save (A-08), stale patient-picker results disabled (A-09) — released earlier in v0.5.1; verified here.
- **Sorting covers the whole list (B-03).** Every paged list sorts on the server before paging (`sort` and
  `direction` query parameters with an allow-list per list, VALIDATION-400 otherwise); lists paged in the browser sort
  before slicing. A functional gate serves every allowed key in both directions.
- **Set-up fixes.** Linked fields clear each other's server error and a blocked master-data save marks the fields and says
  so (B-01); a restricted user without a branch is refused (`ORG-018`, B-02); the price grid filter is always visible
  (B-05); the copy dialog proposes a free code (B-06); base price lists get a year filter and CSV export (B-07); treasury
  search matches code and both names on the server (B-08); the shell shows the role name in Arabic (B-09); activating a
  price list asks for confirmation.
- **Smaller fixes.** Worklist age for patients registered by age (A-05); dictionaries load before Results, Patients and
  Account pages (A-06); the dashboard refuses a future day (`ACC-030`) and returns to today (A-10); the repeat-order
  warning names the repeated test (A-11); collected money only for users with `billing.view`; "1 result saved"
  wording; plan fees refused beyond two decimals (C-05); recovery error cleared (C-06); wizard mobile messages (C-08);
  dial code checked under the field (C-09); "View template" for viewers (C-10); client of an administrator's password
  reset in the audit log (C-11); platform entity hint (C-12); the sessions list sorts by time.
- **Re-test round.** The similar-patient warning keeps VIP files as masked entries, so a duplicate is still warned, and
  masked rows sort after named ones; the patient picker starts empty after a save; ages, the paginator and the dashboard
  chart use the language's digits and singular; one Arabic term for the accession ("رقم الزيارة"); a master-data row opened
  without the edit right is a read-only view; reception reads selectable branches instead of a refused list; text columns
  sort without regard to case; the user's own name follows the language (`displayNameEn/Ar` in the session profile);
  a patient registered by age is dated by the laboratory's local day; Verify refused for a refreshed delta opens the
  acknowledgement dialog; the refresh grace also covers a request that read the clock just before the other tab rotated;
  the wizard's mobile messages follow a language switch.
- **Re-test round 2.** A laboratory without a client record (the demo laboratory) dates registrations and ages by the new
  tenant parameter `timeZone` (IANA, validated; the laboratory's value, else the platform's, else Africa/Cairo) instead of
  the UTC day, and the patient screens show ages for the laboratory's day; patient conditions are read from
  `GET /api/v1/patients/conditions` (`patients.view`), so a patients reader without `catalogue.view` gets no 403, and the
  file's Visits tab needs `registration.view`; signing out sends no request before `/login` loads; the refresh of a session
  that ended by sign-out, administrator or expiry is refused without the reuse alarm (`SESSION_CONTEXT_CHANGED`, info,
  `refresh_after_<reason>`); the patient file's dates, tab counts and the Visits column use the language's digits; the
  Super Admin greeting and the wizard's mobile messages follow a language switch; "1 report unit · 1 result".
- **Re-test round 3.** A form field now re-binds to its control when the control arrives after the first render (a
  `formControlName` inside a control-flow block): such fields showed no validation message at all; a blocked save on
  Platform parameters says so, a refused value appears under its field, and the time zone is limited to 64 characters;
  the patient file's visits show the Arabic branch name, the translated status and Arabic-Indic counts; the matching-visits
  count of Results by accession uses the language's digits (its dates keep the Latin digits of the printed reports).
- **Searches with `_` or `%` match literally.** Every `EF.Functions.ILike` passes its escape character; a new
  architecture gate (`SearchPatternGateTests`) refuses the two-argument form.
- **Validation tooling.** `tests/validation/serve-ui.mjs` and the `validation-ui` launch configuration pin a release
  build for a validation campaign, so working-tree edits cannot reach the code under test.

### v0.5.3 — 2026-10-11

- **The sidebar collapses from the top header.** The ☰ button at the start of the top bar now collapses and expands the
  sidebar on every screen size (on screens under 960 px it still opens the menu as a drawer). The "Collapse menu" button
  at the foot of the sidebar is removed. The button's label and `aria-expanded` follow the state ("Collapse menu" /
  "Expand menu").

### v0.5.2 — 2026-10-11

- **The Egyptian national ID fills the patient's date of birth, gender and governorate.** Typing a 14-digit Egyptian
  ID (century, date of birth, governorate of birth, sequence whose last digit is odd for male and even for female) fills
  those fields when they are empty. An untouched default title follows the gender ("Ms" for a woman). Values the ID
  filled follow a corrected ID; anything the clerk entered is never overwritten, and a contradiction is listed under the
  name row. IDs of other nationalities and passports are ignored, and the check digit is not used to refuse.
  National ID now sits beside the Arabic name so it can be typed first.
- **F10 saves** (registration, results entry, editors and dialogs), the save key of the LDM forms the laboratories know.
  Ctrl+S still works, but neither key reaches the browser any more: Ctrl+S no longer opens "Save page as…" where no
  form listens, and F10 no longer moves the focus to the browser menu. On-screen hints now say F10.
- **Patient form alignment:** the gender switch, date of birth, "or age" and the age boxes share one label line and one
  40 px control line.

### v0.5.1 — 2026-10-11

- **New patient form reorganised (CP-06 / CP-11).** The form now has four labelled groups: Identity (title beside the
  English name, then the Arabic name), Sex and age (a Male / Female switch, then date of birth "or age" in years, months
  and days on one line, then the patient condition), Contact (mobile country beside the mobile, then national ID), and a
  folded "More details" section (nationality, e-mail, governorate, city, address, comments). That section opens by itself
  when it holds data or when one of its fields refuses the save. The layout follows the width of its container, so it
  stacks cleanly in the registration strip, the side panel and the patient file. This also fixes the ragged layout where
  Title and Gender took a whole row each.
- **Fix A-01 (S2): a saved registration no longer comes back as a draft.** The draft is cleared as soon as the visit is
  saved, so "Open the visit", "Collect on the Payments page" or closing the dialog can no longer lead to a duplicate visit
  and payment.
- **Fix A-07: Arabic digits in the registration checklist, action bar, discount cap and counts.**
- **Fix A-08: a blocked save also marks the services card and entry box** when no service was added or one was refused.
- **Fix A-09: the patient picker disables (and dims) the previous search's results** as soon as the text changes, until
  the new search answers.

### v0.5.0 — 2026-10-11

- **Registration page redesigned as a desk console (CP-06, UX option B with option C's checklist).** Patient and visit
  data share one strip, and the visit flags are toggle buttons. Matching services are suggested while typing (↑↓ chooses,
  Enter adds, Esc closes); Enter without a suggestion still resolves shortcut numbers and sets, and ↑↓ now also move
  through the F2 catalogue list. A "Ready to save" checklist sits beside the checkout. The discount is folded until
  needed, and payment moved into the checkout column. A sticky bottom bar lists every open item as a chip that jumps to
  its field, with the amount due and Save (Ctrl+S). A blocked save now marks all missing fields and focuses the first one,
  instead of naming only the first problem. New components: `registration-readiness`, `registration-action-bar`,
  `visit-flags`. Business rules and API calls are unchanged.

### v0.4.1 — 2026-10-11

- **Service installer**: waits until the service process has exited and retries moving the previous build, instead of
  failing when the stopped service still holds its files (seen during the 0.4.0 upgrade).
- **Installation on WIN-9S2KO4FA64L upgraded to 0.4.0** (`docs/deploy/Installations.md`): smoke test 20 / 20 by name and
  17 / 17 by IP, backend-cycle deployed run 5 / 5.

### v0.4.0 — 2026-10-11

- **All 49 findings of the validation report fixed** (LIS-VR-001, 2 S1 · 5 S2 · 21 S3 · 21 S4), each with tests:
  - **Sessions (S1)**: a tab never continues as another user after a refresh; sign-out reloads the page and is broadcast
    to other tabs; every list re-reads on each visit.
  - **Editors and saves (S2)**: the test editor keeps typed sections and shows each refusal; editors follow browser
    back/forward; successful empty answers are confirmed (roles, users, tiers, results, samples, integration).
  - **Guards and rights**: routes refuse pages outside the user's permissions or Release 1; `/account` closed to platform
    sessions; result comment/note, patient documents and treasury branches follow the user's rights.
  - **Data and rules**: governorate must belong to the branch country (`ORG-017`), field codes for branch phone and e-mail
    (`ORG-015/016`), IANA time zones (`GEO-007`), manager mobile (`GEO-014`), top-ups refuse more than two decimals,
    administrator lock has its own code (`AUTH-015`); `BEFORE TRUNCATE` guards on every append-only table (migration
    `AppendOnlyTruncateGuard`).
  - **Features**: patients list visit columns, worklist search by name, exports on branches/roles/users/payers/treasuries,
    filtered and localized catalogue exports, billing print setting (`GET /api/v1/billing/print-settings`), audit Client
    column and filter with client creation in the platform trail, laboratory settings show the effective value and source.
  - **Wording**: localized field-level validation messages, Arabic parameter hints, letterheads per language, dashboard
    wording and digits, distinct Home/Dashboard labels, Release-1 landing text.
- **Fix verification** (report LIS-VR-001 v1.1): two browser re-test rounds with regression smoke of every page for every
  persona; the findings they raised are fixed too: the refund action reachable again (action-only pages carry
  `menu: false` in the navigation registry), sign-in and sign-out act only on a refresh cookie whose secret matches and a
  sign-in ends the browser's previous session, other tabs follow a password change, delayed-tests table with due time,
  read-only views for users without edit rights, refunds netted in the visit summary, local export file names.
- **New permissions** `organization.export` and `treasury.export`: add them to existing laboratory roles in the role
  editor (the platform administrator and newly provisioned laboratory administrators get them automatically).

### v0.3.9 — 2026-10-11

- **Validation report** `docs/validation/2026-10-11/Sky-LIS_Validation_Report.html` (+ PDF, LIS-VR-001): full validation of
  v0.3.8. 1,589 automated tests passed; 469 manual cases on both portals (457 executed, 403 passed); 56 database
  verifications after UI saves; backend cycle 36 / 36 steps with 320 database assertions and 5 / 5 on the installed site;
  49 findings (2 S1, 5 S2, 21 S3, 21 S4) in 14 fix groups. Detailed results per area in the same folder.
- **Backend-cycle script** `tests/validation/backend-cycle.mjs`: provisions a client and drives a complete laboratory cycle
  through the API, asserting the database after every request, plus negative and isolation checks; reruns against any
  base URL (`--deployed --insecure-tls` for the installed site).
- **Validation stack**: launch configurations `validation-api` (127.0.0.1:5102) and `validation-spa` (4300, proxy
  `frontend/proxy.validation.json`) run beside the installed service on the same database. `cat-api` (added
  2026-10-14) serves the `cat` build slot's output (`.local/build/cat/…/Platform.WebApi.dll`, Development
  environment) on the same 5102, for checking a slot build in the browser through `ux-spa` (5110) while another
  session holds 5100 / 4300.

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
