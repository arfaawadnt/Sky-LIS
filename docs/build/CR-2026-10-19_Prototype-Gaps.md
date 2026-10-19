# Change request 2026-10-19 — prototype v3 / v4.1 gaps (LIS-CR-003)

Source: `docs/knowledge/sky-lis/SkyLIS_Prototypes_v3-v4.1_Difference.md` §2 and `docs/plan/Sky-LIS_Development-Status_and_Proposed-Changes.html`
(LIS-DS-001). The business owner asked on 2026-10-19 to build the items marked *Not built* / *Partial* for the admin portal (A1, A2) and the
client portal (C3, C9, C10, C11, C18, C19, C21, C22). The work is split into six packages built in parallel by module agents in build
slots, integrated by the integrator (one migration `PrototypeGaps`, routes, nav, shell dictionary, README, browser check, release).

## 0. Rules of this wave (read with `docs/build/MODULE-PATTERN.md`)

These are change requests on **existing** modules, so an agent edits the existing files of its module (domain, contracts, application,
infrastructure persistence, controllers, Angular feature, `public/i18n/<module>.{en,ar}.json`, its tests, its intake document). Everything
else of MODULE-PATTERN §2 stays: never edit `PermissionCatalog.cs`, `AppDbContext.cs`, `Persistence/Migrations/**`, `deploy/postgres/**`,
`app.routes.ts`, `shell/**`, `public/i18n/shell.*.json`, `angular.json`, `package.json`, `CLAUDE.md`, `README.md`, test `Support/`
folders, or another module. Ask for what you need in the report.

- Work in your slot: `source scripts/slot-env.sh <slot>` (Git Bash, from `E:\LIS`), then `dotnet build Platform.sln -c Release` and
  `dotnet test tests/<Suite> -c Release --no-build -v q`. Never run `dotnet ef`, never start the API or the dev server. Frontend:
  `cd /e/LIS/frontend && npx ng lint && node scripts/check-boundaries.mjs && npx ng test --watch=false` (the tool's working
  directory persists across calls: return to `/e/LIS` afterwards, or use absolute paths). Keep command output small (`-v q`, `| tail`).
- **Model changes**: change the aggregate and its EF configuration; the integrator generates the one migration of the wave after every
  package has landed. The database suites (IntegrationTests, FunctionalTests) will only pass after that migration exists — write them
  now, run them when the integrator tells you (you will be messaged). Extra SQL (seed rows for existing tenants, grants on `saas`
  tables, indexes EF cannot express) goes into `src/Platform.Infrastructure/Persistence/<Module>/<Module>.migration.sql` as an
  idempotent block with a dated heading; the integrator appends it to the migration.
- **Compatibility**: the installed server runs 0.14.0 with real data. Every new column is nullable or has a default; existing API
  request shapes keep working (new fields optional); existing rows get sensible values from the migration SQL.
- **Intake**: append a dated change-request section to your module's intake (`docs/intake/LIS-INT-<MOD>-001_*.md`): what changes, new
  codes, new endpoints, open questions with the assumption you built on; bump the version line.
- **Tests**: domain tests for every new rule; functional tests for every new endpoint (happy path, 403 without the permission, 400 on
  shape, each new refusal code); frontend specs for every new page and facade. Keep the existing gates green.
- Two agents may touch `frontend/src/app/features/registration/**` (packages C9 and C10): C10 edits only the payment block of
  `registration.page.html`, the payment state of `registration.facade.ts` and the `registration.payment.*` dictionary keys; C9 adds
  new files and appends new methods at the **end** of `data/registration.api.ts` and new keys at the **end** of the dictionaries.
  Never rewrite a whole shared file; use targeted edits.
- Report per MODULE-PATTERN §6, plus: the list of files changed, the migration needs (tables, columns, seed SQL), the nav entries and
  shell dictionary labels (English and Arabic) you need, and one README release-history bullet per user-visible change.

## 1. Packages

| Package | Items | Slot | Modules touched |
| --- | --- | --- | --- |
| P-A1 Client login logs | A1 | `plat` | PlatformAdmin (Application / Infrastructure / WebApi), `features/platform` |
| P-A2 Location hierarchy + patient contacts | A2, C3 | `pat` | PlatformAdmin (cities / districts / areas, public lookups), Patients, `shared/patient`, `features/platform` (governorates page), `features/patients` |
| P-C10 Payment methods | C10 | `bill` | Billing (+ the payment block of the registration page) |
| P-C9 Accession details + menu | C9, C11 | `reg` | Registration (`features/registration`), Treasury pages (links only) |
| P-C18 Sequence reordering | C18 | `cat` | Catalogue A (sequence query + `features/catalogue` sequence page) |
| P-PRC Pricing | C19, C21, C22 | `fin` | Pricing (Payer, BasePriceList, new service-prices query; `features/pricing`) |

## 2. P-A1 — Client login logs (admin, SA-08b)

**Goal.** A history grid of the client users' browser sessions (the prototype's *Client login logs*): per session family one row with
client, user, login date / time (UTC), logout date / time (UTC), IP address, device (Computer / Mobile / Tablet), OS, browser, duration
(minutes), status; filters by client, user (search on username / display name), login window, logout window, device, duration bounds and
status; CSV export. Platform administrators' own sessions are excluded (tenant = platform) unless the filter asks for a client.

**Facts available.** `identity.refresh_sessions` (`RefreshSession`: `FamilyId`, `PlatformUserId`, `TenantId`, `Kind`, `IssuedAtUtc`,
`ExpiresAtUtc`, `RotatedAtUtc`, `SuccessorId`, `IsRevoked`, `RevokedAtUtc`, `RevocationReason` Logout / Admin / PasswordChanged /
ReuseDetected / Expired / TenantSuspended, `LastSeenAtUtc`, `IpAddress`, `UserAgent`), `identity.platform_users` (username, display
name), `saas.tenants` (client name). The existing live list is `ListAdminSessionsQuery` + `PlatformSessionDirectory` (SA-08).

**Design.**
- One row per family: login = min `IssuedAtUtc`; logout = the `RevokedAtUtc` of the member revoked with `Logout` (status *SignedOut*),
  else the `RevokedAtUtc` of a member revoked for another reason (status *Revoked*, with the reason), else the last member's
  `ExpiresAtUtc` when it lies in the past (status *Expired*), else null (status *Active*). Duration = (logout ?? last seen) − login.
- `UserAgentParser` in `Platform.Application/PlatformAdmin/` (pure, unit-tested in Application.UnitTests): device class, OS family and
  version, browser family and version from the stored user-agent string; unknown → "Unknown".
- Query `ListClientLoginLogsQuery(tenantId?, search?, loginFromUtc?, loginToUtc?, logoutFromUtc?, logoutToUtc?, device?, minMinutes?,
  maxMinutes?, status?, page, pageSize, sort, direction)` [`platform.view`] over a read port `IClientLoginLogReader`
  (`Platform.Infrastructure/Persistence/PlatformAdmin/`): group by family in SQL / LINQ, filters pushed down where possible, paging after
  ordering (server-side sort keys per `ListSorting`: login, logout, client, user, duration, status; default login desc). Validation:
  windows ordered, minutes ≥ 0, min ≤ max, page bounds.
- Export: `ExportClientLoginLogsQuery` (same filters, no paging, cap 10 000 rows) returning the CSV rows → `GET
  /api/v1/platform/login-logs/export` as `text/csv` with BOM (see the existing CSV exports for the formula-safe writer).
- Endpoint `GET /api/v1/platform/login-logs` (paged) in `Controllers/PlatformAdmin/`.
- Frontend `features/platform`: page `login-logs.page.ts` (+ facade, api methods, contracts in `core/api/platform.contracts.ts`): filter
  bar (client select from the clients list, user search, two date-time ranges, device, duration min / max, status), server-paged and
  server-sorted table, CSV button, empty state. Route child `login-logs` in `platform.routes.ts`. Dictionary keys under `platform.loginLogs.*`.
- Nav entry (integrator): admin · administration · `/admin/login-logs` · `platform.view` · `shell.nav.loginLogs` · "Client login logs" / "سجل دخول العملاء".

## 3. P-A2 — Location hierarchy (admin, SA-11b) and patient contacts (C3)

**Goal (A2).** Governorate → city → district → area as platform reference data edited from the Governorates page, exposed to the forms
as pick lists. **Goal (C3).** A second phone on the patient.

**Design A2.**
- Three aggregates in `Platform.Domain/PlatformAdmin/`: `City` (governorate id, `LocalizedText Name`, `IsActive`), `District` (city id,
  …), `Area` (district id, …). Names bilingual, ≤ 100, English name unique within the parent (case-insensitive; index in
  `PlatformAdmin.migration.sql` like `ux_governorates_country_name`), codes `GEO-015` (parent inactive), `GEO-102/103/104` (name exists),
  `GEO-112` (deactivating a parent with active children), `GEO-406/407/408` (not found). Tables `saas.cities`, `saas.districts`,
  `saas.areas` (no tenant; grants `SELECT, INSERT, UPDATE` to `lis_app` in the module SQL). No deletes: activate / deactivate like the
  other master data.
- Commands / queries in `Application/PlatformAdmin/` [`tenancy.view` / `tenancy.manage`, as the governorates]: list per parent (active
  filter, search), create, update (rename), deactivate, reactivate. Endpoints under the governorates controller family:
  `GET/POST /api/v1/platform/governorates/{id}/cities`, `PUT /api/v1/platform/cities/{id}`, `POST …/cities/{id}/deactivate|reactivate`,
  `GET/POST /api/v1/platform/cities/{id}/districts`, `PUT …/districts/{id}` (+ activate pair), `GET/POST …/districts/{id}/areas`,
  `PUT …/areas/{id}` (+ pair). Public lookups beside `GET /api/v1/public/governorates`: `cities?governorateId=`, `districts?cityId=`,
  `areas?districtId=` (active rows, id + names).
- Governorates page (`features/platform/pages/governorates.page.ts`): a row action *Locations* opening a drawer with three linked
  columns (cities of the governorate → districts of the selected city → areas of the selected district), each with search, add, rename,
  activate / deactivate; gated by `tenancy.manage`. Dictionary keys `platform.locations.*`.
- Patients: `Patient` gains `CityId?`, `DistrictId?`, `AreaId?` (no FK across schemas, like `GovernorateId`; the handler validates that the
  ids chain to the chosen governorate through a read port of the public lookups; `PAT-0xx` codes). The free-text `City` stays for
  laboratories whose governorate has no cities; when a city is picked the handler stores its English name in `City` too, so lists,
  exports and labels keep working. `PatientDemographics`, the requests, `PatientDetail`, the list item and the CSV export carry the
  three ids and the names. `shared/patient/patient-form.component.*`: after the governorate, the city / district / area selects appear
  when the governorate has active cities (loaded through `patient-lookup.api.ts` from the public lookups); otherwise the free-text city
  input stays. The Egyptian national-id fill keeps setting the governorate only.
- Client (tenant) address stays free text in this change (subscription wizard and SA-04 unchanged); say so in the intake.

**Design C3.** `Patient.SecondPhone` (cleaned like `Mobile` through `CleanMobile`, optional, ≤ 16 digits, no uniqueness, no country
check), carried by `PatientDemographics`, the requests, detail, list item (optional), CSV, the similar-patient search (second phone
matches like the mobile), the patient form (field after the mobile, same country hint) and the visits register's patient drawer.

## 4. P-C10 — Payment methods (client set-up, CP-30b)

**Goal.** A tenant list of payment methods with code, English and Arabic names, kind, *requires reference (transaction no.)* and status,
seeded with Cash, Card, POS and Bank transfer; the cash desk and the registration payment panel offer the active methods and ask for the
reference only when the chosen method requires it.

**Design.**
- Aggregate `PaymentMethodDefinition` in `Platform.Domain/Billing/` → `app.payment_methods` (`(tenant_id, id)`, RLS declared by the
  integrator's migration): `Code` (≤ 20, unique per tenant, upper-case key), `Name` (`LocalizedText`), `Kind` (`PaymentMethod` enum:
  Cash / Card / Pos / Transfer — the money semantics stay on the kind: cash needs the treasury drawer, the collections report groups by
  kind), `RequiresReference`, `IsActive`, `Sequence`. Rules: one active Cash-kind method at least (`BIL-05x` when deactivating the last
  one), code immutable, deactivate instead of delete. Events `PaymentMethodDefined / Updated / Deactivated / Reactivated`.
- Defaults: `PaymentMethodDefinition.Defaults()` = CASH / Cash / نقدي (Cash, no reference), CARD / Card / بطاقة (Card, reference),
  POS / POS / نقطة بيع (Pos, reference), TRANSFER / Bank transfer / تحويل بنكي (Transfer, reference). Seeded (1) for every existing
  tenant by `Billing.migration.sql` (one idempotent INSERT … SELECT over `identity.tenants` / `app.branches` tenant ids, `ON CONFLICT DO
  NOTHING` on `(tenant_id, code)`), (2) for new tenants by a policy `SeedPaymentMethodsPolicy : IDomainEventHandler<TenantProvisioned>`
  (`Application/Billing/Policies/`, idempotent, skips the platform tenant), (3) by `POST /api/v1/billing/payment-methods/defaults`
  [`billing.manage`] for safety (idempotent, as `POST /api/v1/pricing/payers/default`), called by the set-up page when the list is empty.
- `Payment` and `Refund` gain `MethodCode` (nullable string ≤ 20, the definition's code at posting time; legacy rows null → shown as the
  kind). `RecordPaymentCommand` / `RecordRefundCommand` (and the batch payment command) gain optional `MethodCode`: when present the
  handler loads the active definition (`BIL-05x` unknown / inactive), takes `Kind` from it and passes `RequiresReference` to the aggregate
  (`GuardPayment` / `GuardRefund`: the reference is required when the definition says so; without a code the legacy rule "every kind but
  cash" stays). The receipt and refund documents and the payment / refund list items show the method name (code + localized name) when
  known.
- Endpoints `GET/POST /api/v1/billing/payment-methods` [`billing.view` / `billing.manage`], `GET …/{id}`, `PUT …/{id}`, `POST
  …/{id}/deactivate|reactivate`, `GET …/options` (active, ordered: id, code, names, kind, requiresReference) [`billing.view` — the
  registration page needs it too; if the reception role lacks `billing.view`, expose the options under `registration.view` as well
  through a second attribute-free path: check how `my-treasuries` is authorised and follow it].
- Frontend: set-up page `/billing/payment-methods` (`features/billing/pages/payment-methods.page.ts`, master-data page kit: list with
  code, names, kind, requires reference, status; drawer form; deactivate / reactivate; CSV) — nav entry (integrator): client · setup ·
  `billing.view` · `shell.nav.paymentMethods` · "Payment methods" / "طرق الدفع". The cash desk `movement-form.component.ts` and the
  registration payment block take the options list (`CashDeskFacade.methods`, registration facade `methods` resource): the method select
  shows the names, the reference input shows only when `requiresReference`, the request sends `method` (kind) **and** `methodCode`.
  Dictionary keys `billing.paymentMethods.*`, `registration.payment.*` additions.
- Collections report: unchanged (by kind); the detail rows of the payments register show the method code when present.

## 5. P-C9 — Accession details page (client, CP-10b) and the menu change (C11)

**Goal (C9).** One page per accession that shows the visit, its ordered tests, samples, results status and payments, and offers the
actions reception needs without hunting through row actions: find by accession (or by patient name / number / mobile), cancel ordered
tests with a reason, collect a sample when it is not collected yet, open result entry, open payments and refunds, print documents, edit.

**Design.**
- Route `/registration/accession` (query `accession=` or `visitId=`) in `features/registration`: `pages/accession-details.page.*`, facade
  `state/accession.facade.ts` (provided at the route; `refresh()` on every visit of the page), api methods appended to
  `data/registration.api.ts`. Data: `GET /api/v1/registration/visits/{id}` (`VisitDetail`: header, lines, tubes, preparation answers,
  balance), the samples of the visit (the samples dialog's source), the result status per line (the Results module: check
  `core/api/results.contracts.ts` for a per-visit / per-accession status read usable with `results.view`; gate the card with
  `*appCan="'results.view'"`), the visit's payments and refunds (`core/api/billing.contracts.ts`: the payments page's account read;
  gate with `billing.view`). The registration feature calls these endpoints through its own api service; it never imports another
  feature.
- Layout: a search strip (one box: accession / patient number / mobile / name; Enter runs `GET /api/v1/registration/visits?search=`;
  exactly one match opens it, several show a compact list to pick from); then a sticky header (accession, patient with the patient
  drawer of CP-10 reused through `shared/patient`, sex / age, VIP / urgent flags, status, branch, registered at / by, payer / tier,
  doctor) and cards: **Ordered tests** (code, name, type, sample type / tube, line status — cancelled rows highlighted and kept —,
  price, patient share, payer share, expected result, result status, row action *Cancel* opening the existing cancel dialog of
  `ui/visit-dialogs.ts` with the row preselected); **Samples** (per tube: number, type, status, collected / received times, *Label*,
  *Collect* when the status is Ordered and the user holds the samples collect right — call the Samples module's collect endpoint with a
  confirm dialog; in Release 1 auto-collect is on, so the button rarely shows); **Results** (status summary per unit, *Enter results* /
  *Open results* → `/results/by-accession?accession=`); **Payments** (balance figures, posted payments and refunds with receipt numbers,
  method, amount, cashier, time; *Take payment* → `/billing/payments?visitId=`, *Refund* → `/billing/refunds?visitId=`, *Receipt* print
  through the existing document endpoints); **Documents** (labels, job order); **Edit registration** → `/registration/visits/{id}`;
  previous / next accession (±1 looked up, as the results page does). Keyboard: Enter in the search box, Esc closes dialogs.
- Keep the page stylesheet within the component budget (move blocks into `ui/` components: `accession-header`, `accession-lines`,
  `accession-payments`, …). Dictionary keys `registration.accession.*`.
- Nav entry (integrator): client · reception · `/registration/accession` · `registration.view` · `shell.nav.accessionDetails` ·
  "Accession details" / "تفاصيل الطلب". The visits register (CP-10) gets a row action *Details* opening the page; the registration
  success panel gets an *Accession details* button.

**C11 (menu).** The integrator sets `menu: false` on `/treasury/treasuries`, `/treasury/access` and `/billing/payments` (action-only:
still in scope and guarded). The agent of this package makes them reachable: the registration page's *no treasury* warning links to
`/treasury/access` (`billing.pay.requestAccess` wording), the Treasury access page gets a *Treasuries* button (`treasury.manage`) to
`/treasury/treasuries` and the Treasuries page a *Treasury access* button back (`features/treasury/pages/*`, links only), and the
accession page's Payments card links to the payments register (*All payments*). The dashboard quick action *Payments* stays.

## 6. P-C18 — Service sequence reordering (client / admin, CP-42b)

**Goal.** On the sequence page: drag a row to a new position, move up / down with buttons, type a position, and reorder inside a
clinical-group filter so that only the visible rows renumber.

**Design.**
- Backend: `ListServiceSequenceQuery` gains `ClinicalGroupId?` (filter) — `ServiceSequenceRow` already carries the group names; add
  `ClinicalGroupId` to the row. `SaveServiceSequenceCommand` stays (full list of `(serviceId, sequence)`); when a filtered subset is
  reordered the page sends only the visible rows with their new sequence numbers, renumbered within the set of sequence numbers those
  rows already occupied (so the other rows keep their numbers and no duplicates arise). Add a domain / handler check that the saved
  numbers are unique among the sent rows (`CAT-0xx`) if missing.
- Frontend (`features/catalogue/*sequence*`): native HTML5 drag-and-drop on the rows (no new dependency), ↑ / ↓ buttons (disabled at the
  ends), the typed position input kept (moves the row, renumbers the rest), a clinical-group select beside the type filter, a *dirty*
  state with *Save order* / *Discard* (confirm away unsaved changes), keyboard: Alt+↑ / Alt+↓ on the focused row. Screen-reader text for
  the drag handles. Specs for the reorder helpers (pure functions in `ui-a/sequence-order.ts`) and the page.

## 7. P-PRC — Pricing: payer contacts (C19), price list tools (C21), service prices (C22)

**C19.** `Payer` gains `Phone1`, `Phone2`, `WhatsApp` (optional, ≤ 32 chars: digits, spaces, `+ - ( )`, cleaned) and `Email` (optional,
≤ 254, basic shape) with code `PRC-0xx` for an invalid value; create / update commands and requests, `PayerListItem` / detail, the payers
page form (a *Contact* block) and its CSV export.

**C21.** Three tools on the base price list editor (`features/pricing/pages/price-list-editor.page.*`):
- *Download Excel*: the SPA builds an `.xlsx` with `exceljs` (installed; always `await import('exceljs')` so it loads on demand) holding
  the list's lines: columns `Code`, `Name`, `Current price`, `New price` (empty), sheet named after the list code.
- *Upload Excel*: the SPA parses the chosen `.xlsx` with `exceljs` (first sheet; header row matched by name, case-insensitive: Code and
  New price / Price), builds rows `{ row, serviceCode, price }` and posts `POST /api/v1/pricing/base-price-lists/{id}/import`
  (`ImportPriceLinesCommand(listId, rows, dryRun)` [`pricing.edit`]); the handler validates every row (unknown service code `PRC-015`-style,
  invalid price, duplicate code, empty) and answers `ImportPriceLinesResult(accepted, errors: [{ row, serviceCode, code, message }])`;
  with any error nothing is applied (all-or-nothing); without errors and `dryRun = false` the lines are upserted through
  `BasePriceList.UpsertLines` (one save). The dialog shows the row errors, then *Apply*.
- *Bulk adjust*: `AdjustBasePriceListCommand(listId, mode Amount | Percent, value, rounding None | Whole | Nearest5 | Nearest10,
  serviceIds?, preview)` [`pricing.edit`]: domain arithmetic in `PriceMath.Adjust` (tests: percent and amount, each rounding, negative
  results clamp to 0 and are reported), answers the rows `{ serviceId, code, name, oldPrice, newPrice }` and, when `preview = false`,
  applies them through `UpsertLines`. Dialog: mode, value, rounding, scope (all lines or the filtered / selected rows), *Preview* table,
  *Apply*.
- *Print*: `ui/documents/price-list-document.component.ts` on `shared/print` (list code, name, year, status, lines code / name / price,
  count), opened from the editor.
- Endpoints `POST …/{id}/import`, `POST …/{id}/adjust`; contracts in `core/api/pricing.contracts.ts`.

**C22.** `GET /api/v1/pricing/service-prices/{serviceId}` [`pricing.view`] → `ServicePricesView(serviceId, code, nameEn, nameAr, type,
basePrices: [{ listId, listCode, listNameEn, listNameAr, year, listActive, price, lineActive }], contractPrices: [{ tierId, payerNameEn/Ar,
tierNameEn/Ar, patientPrice, payerPrice, isExcluded }] from the derived contract prices)`. Page `/pricing/service-prices`
(`features/pricing/pages/service-prices.page.*`): a service picker (the catalogue lookup already used by the pricing pages), the two
tables, inline edit of a base price (one-line `PUT …/base-price-lists/{id}/lines`, `pricing.edit`), a link to each list's editor. Nav
entry (integrator): client · finance · `/pricing/service-prices` · `pricing.view` · `shell.nav.servicePrices` · "Service prices" /
"أسعار الخدمات".

## 8. Integrator checklist (after the packages report)

1. `git status`; read the six reports; apply the requested shared pieces (nav entries, shell dictionary, routes if any, `PermissionCatalog`
   only if asked, `allowedCommonJsDependencies` for exceljs — done).
2. Generate the migration `PrototypeGaps` in the `integrator` slot; append every `<Module>.migration.sql` block of this wave; add RLS
   for `app.payment_methods` (`scripts/add-rls-to-migration.mjs`); regenerate `deploy/postgres/migrations.sql`.
3. Tell the agents to run their database suites; run every suite in the integrator slot; frontend lint, boundaries, specs, build.
4. Browser check of each new page on the review stack; README bullets under *Unreleased*; CLAUDE.md §5 summaries; LIS-DS-001 status
   update; release, install.
