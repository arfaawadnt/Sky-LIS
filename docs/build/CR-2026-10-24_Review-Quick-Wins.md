# Change request 2026-10-24 — reviewer's quick wins (LIS-CR-007)

Source: `docs/review/Sky-LIS_Reviewer-Comments_Response_V1.html` (LIS-RC-001), the answer to the two V1 review documents of Shahed. The
business owner asked on 2026-10-24 to build the "switch on" item and package CR-007 (the user-interface quick wins): C-01, C-05, C-06/C-10,
C-07, C-08, C-09, C-11 and A-01. The work is split into five packages built in parallel by module agents in build slots and integrated by
the integrator (navigation, shell dictionary, SRS / UAT / README, browser check, release).

## 0. Rules of this wave (read with `docs/build/MODULE-PATTERN.md`)

These are change requests on **existing** modules, so an agent edits the existing files of its module (domain, contracts, application,
infrastructure persistence, controllers, Angular feature, `public/i18n/<module>.{en,ar}.json`, its tests, its intake document). Everything
else of MODULE-PATTERN §2 stays: never edit `PermissionCatalog.cs`, `AppDbContext.cs`, `deploy/postgres/**`, `app.routes.ts`, `shell/**`,
`public/i18n/shell.*.json`, `angular.json`, `package.json`, `CLAUDE.md`, `README.md`, test `Support/` folders, or another package's
files. Ask for what you need in the report.

- Work in your slot: `source scripts/slot-env.sh <slot>` (Git Bash, from `E:\LIS`), then `dotnet build Platform.sln -c Release` and
  `dotnet test tests/<Suite> -c Release --no-build -v q`. If a database suite reports "database lis_<slot> does not exist" or a schema
  error, run `bash scripts/new-slot-database.sh <slot> --reset` once. Never start the API or the dev server. Frontend:
  `cd /e/LIS/frontend && npx ng lint && node scripts/check-boundaries.mjs && npx ng test --watch=false` (the tool's working directory
  persists across calls: return to `/e/LIS` afterwards, or use absolute paths). Keep command output small (`-v q`, `| tail`).
- **Migrations**: only package P-MIC changes the model; it adds the migration `ReviewQuickWins` itself (command in CLAUDE.md §5, with
  `--msbuildprojectextensionspath .local/build/cat/obj/Platform.Infrastructure/`) and regenerates `deploy/postgres/migrations.sql`.
  Every other package changes no table.
- **Compatibility**: the installed server runs 0.20.0 with real data. New columns are nullable or defaulted; existing API request
  shapes keep working (new fields optional).
- **Intake**: append a dated change-request section to your module's intake (`docs/intake/LIS-INT-<MOD>-001_*.md`): what changes,
  new codes, new endpoints, open questions with the assumption you built on; bump the version line.
- **Tests**: domain tests for every new rule; functional tests for every new endpoint and every changed answer; frontend specs for
  every new component and changed page. Keep the existing gates green. Rules of the SPA: no `any`, no web storage (no `localStorage`,
  `sessionStorage`, IndexedDB, `document.cookie`), no `subscribe()` in components, 4 kB stylesheet budget per component, Arabic
  dictionaries with counted forms, logical CSS properties (RTL).
- Dictionaries: add keys next to the keys of the page you change, never at the end of a shared dictionary (two packages touch
  `catalogue-b.{en,ar}.json`: P-RES adds `doctors.*` keys beside the existing ones, P-MIC adds `drugTypes.*` / `cultureSpecimens.*`
  keys beside the existing `drugTypes.*` ones). Never rewrite a whole shared file; use targeted edits.
- Report per MODULE-PATTERN §6, plus: the list of files changed, the nav entries and shell dictionary labels (English and Arabic) you
  need, and one README release-history bullet per user-visible change.

## 1. Packages

| Package | Items | Slot | Files / modules touched |
| --- | --- | --- | --- |
| P-TBL Horizontal row actions + payments layout | C-06, C-10, C-07 | `bill` | `shared/ui/data-table/**`, the registers' `appRowActions` templates where needed, `features/billing/pages/payments.page.*` |
| P-FRM Mobile input + bookmark strip | C-05, C-01 | `pat` | new `shared/forms/mobile-input/**`, `features/public` (subscribe wizard), `features/organization` (user editor), `features/platform/pages/clients.page.*`, `shared/patient` (adopt the control), `features/auth` (sign-in pages) |
| P-RES Range before save + proposed doctor code | C-08, C-09 | `res` | Results (`Application/Results/ResultViews.cs`, case query, functional test), Catalogue B doctors (command, handler, `features/catalogue/pages-b` doctor editor) |
| P-MIC Microbiology naming + culture specimens | C-11 | `cat` | Catalogue (SampleType `ForCulture`, migration `ReviewQuickWins`), `features/catalogue` microbiology and sample-type pages, service editor sample-type picker |
| P-PLT Silent clients | A-01 | `plat` | PlatformAdmin dashboard / clients (Application, Infrastructure reader, WebApi), `features/platform` dashboard + clients page |

The integrator switches CP-51 Shortcut sets and CP-52 Test rules to Release 1 in `shell/nav.registry.ts` (A-04 / A-05).

## 2. P-TBL — Horizontal row actions (C-06, C-10) and the payment box first (C-07)

**Today.** `shared/ui/data-table` renders the `appRowActions` template in an Actions column of width 1 % whose `.row-actions` flex row
*wraps*, so pages with a status badge plus two buttons (every master-data list) show three rows, and the visits register (CP-10, eight
actions) shows a tall column.

**Goal.** Every list shows its actions in **one horizontal line**: icon buttons with a tooltip (`title`) and an accessible name; the
status badge first, then the actions; nothing wraps. Pages with more than four actions fold the rest into a *More* menu (a small popover
of the shared `overlay` kind, keyboard reachable: Enter / Space opens, arrows move, Escape closes). The primary action of the page stays
visible. Narrow screens (< 720 px) keep the row and let the cell scroll horizontally.

Implementation guidance: change `data-table.component.{html,css,ts}` so `.row-actions` is `flex-wrap: nowrap` and the column takes the
width of its content; add a shared `app-row-menu` (or extend the table with a `[maxInlineActions]` input defaulting to 4) that renders
overflow actions; convert the visits register's actions to icon buttons + menu; audit the other 26 pages that use `appRowActions` so that
none still wraps (most only need the CSS change). Keep text labels for the first two actions where the page relies on them (e.g.
*Details*, *Edit*); turn the rest into icons with tooltips. Add specs: the table renders actions in one row; the overflow menu opens,
runs an action and closes.

**C-07 Payments (CP-23)** `features/billing/pages/payments.page.*`: order of the page becomes lookup strip → one row with the visit
totals **and the "Take a payment" card** → "Payments of the visit" and refunds below, collapsed to the last three rows with *Show all*.
After a successful find, focus moves to the amount field (scan → amount → F10). Update `payments.page.spec.ts`.

## 3. P-FRM — Mobile input (C-05) and the bookmark strip (C-01)

**C-05.** New shared control `shared/forms/mobile-input/mobile-input.component.ts` (`app-mobile-input`, `ControlValueAccessor`):
two visual parts in one bordered field — the dial code (read-only from the `country` input when given, else a small select over the
public countries list `GET /api/v1/public/countries`, or free text starting with `+`) and the national number; a leading 0 typed in the
national part is removed on blur; pasting `+20…`, `0020…` or `0…` into the national part is recognised; the control's value is the E.164
string (`+201004439855`) or `''`; the hint reads "{n} digits, without the leading 0" when the country's mobile length is known. The
server stays unchanged (E.164 + `IUserMobileNumbers`). Adopt it on: the subscribe wizard (laboratory mobile, manager mobile — keep the
existing `countryMobileValidator` semantics and the C-08 message of LIS-VR-002), the user editor (`features/organization`), the SA
clients page (`features/platform/pages/clients.page.*`), and the shared patient form (replace its country select + number pair; keep
`onIdentityInput` and the demographics-lock behaviour). Specs for the control (typing, paste forms, zero removal, value out/in) and the
adopted forms.

**C-01.** On the laboratory sign-in page (`/lab/<slug>`, `features/auth`) show under the card a compact strip: "Keep this address:
Ctrl+D (⌘+D) bookmarks it" with a *Copy address* button (`navigator.clipboard`, toast "Copied") and a *Hide* that hides it for the
session (a signal in the page, no storage). On the *Find your laboratory* page add the same copy hint under the Continue button once a
laboratory is found. Keys `auth.keepAddress.*` in `auth.{en,ar}.json`. No device storage of any kind (security rule); the "recent
laboratories on this device" idea of LIS-RC-001 is **not** built in this package — say so in the intake.

## 4. P-RES — Reference range before save (C-08) and proposed doctor code (C-09)

**C-08.** `GetResultCaseHandler` / `ResultViews`: a line whose result has no stored `Range` (Ordered, never entered) gets its `range`
resolved from the test's range bands and the patient's sex / age / condition with `ReferenceRangeResolver` (the same call `ResultsSupport`
makes at entry) so the grid shows the expected range before the first save; the stored range wins once a value exists. Mark such rows
`rangeProvisional: true` in the contract (optional field) and render them greyed in the grid (`app-result-grid`). Functional test: an
ordered line of the case answers the range of the patient's band; after entry the stored range is served. Also the worklist's row range
if it shows one.

**C-09.** `CreateDoctorCommand` (`Application/Catalogue/Commands/CatalogueBSetupCommands.cs`): a blank `Code` makes the handler
generate `DR-nnn` from `IReferenceNumbers` (kind `doctor`, prefix `DR`, no year in the format: add an overload or a second method
`NextShortAsync(kind, prefix, width 3)` on the port and its implementation over `app.reference_counters`; existing numbers are
unaffected). A typed code is validated as today (unique, pattern). SPA: the *Add doctor* form shows the Code field with placeholder
"Generated on save (DR-001 …)" and no "required" mark; the Edit form keeps the code read-only as today. Domain test for the format,
functional test for blank → generated, dictionary keys `doctors.codePlaceholder` beside the existing `doctors.*` keys.

## 5. P-MIC — Microbiology naming and culture specimens (C-11)

**Rename.** The *Drug types* tab (CP-56) becomes **Antibiotic classes** (`فئات المضادات الحيوية`) with the subtitle "Groups the
antibiotics of the sensitivity panel (Penicillins, Cephalosporins …)"; the Antibiotic form labels its class field "Class". Dictionary
keys only (`drugTypes.*` values) — routes, API paths and entity names do not change.

**Culture specimens.** `SampleType.ForCulture` (bool, default false; migration `ReviewQuickWins` adds the column with default
false, then sets it true for the sample types whose English name contains "swab", "culture", "urine", "stool", "sputum", "pus", "csf",
"wound" — idempotent SQL in `Persistence/Catalogue/Catalogue.migration.sql` with a dated heading, appended to the migration).
Sample-type editor (CP-43): a switch "Used for cultures". New sixth tab **Culture specimens** on the Microbiology page: a read-only list
of the sample types with `ForCulture` (code, short name, name, status) with a link "Manage on Sample types". The service editor's
sample-type picker lists culture specimens first (then the rest) when the service type is Culture. Tab order: Organisms · Antibiotics ·
Antibiotic classes · Commercial names · Sensitivity values · Culture specimens. Contracts: `SampleTypeDetail.ForCulture`,
`Create/UpdateSampleTypeCommand.ForCulture` (optional, default false). Tests: domain, functional (round trip, filter
`forCulture=true` on the sample-type list), specs for the new tab and the editor switch.

## 6. P-PLT — Silent clients (A-01)

**Goal.** The Super Admin dashboard (SA-03) shows how many clients have had **no activity for N days** (default 3) and a list to call them.

**Definition.** Last activity of a client = the latest of its users' last sign-in (`identity.refresh_sessions.IssuedAtUtc`, tenant =
client) and its last registration (`app.visits.RegisteredAtUtc`, read with the platform's elevated reader like the other dashboard
counters). Silent = last activity older than N days, or no activity at all since the client was created more than N days ago. Inactive
(status) clients are excluded.

**Build.** `PlatformDashboard.SilentClients` (count) with `silentDays` echoed; `GET /api/v1/platform/clients/silent?days=3` →
rows: client, portal address, status (Trial / Active), created, last sign-in, last registration, last activity, administrator name and
mobile (E.164), with CSV export `…/silent/export`; `days` 1…90 (VALIDATION-400 otherwise). Permission `platform.view`. SPA: a dashboard
tile "Silent for 3+ days" (red when > 0, trial clients counted separately in the subtitle) linking to the Clients page opened with the
new *Silent* filter (a `days` select 3 / 7 / 14 / 30) and its table; CSV export button. Dictionary keys in `platform.{en,ar}.json`.
Functional tests: a fresh client with a sign-in today is not silent; one with no activity for four days is; `days=0` refused.

## 7. Integrator checklist (after the packages report)

1. `shell/nav.registry.ts`: CP-51 / CP-52 → `release: 'R1'`; any new entry or label the packages ask for; `shell.{en,ar}.json`.
2. Migration `ReviewQuickWins` present once (P-MIC); `deploy/postgres/migrations.sql` regenerated; MigrationScriptTests green.
3. Five suites in the `integ` slot, frontend lint / boundaries / specs / build.
4. Browser check on the review stack: visits register actions in one line; Payments with the payment card first; a result case showing
   ranges before entry; Add doctor with a generated code; Microbiology tabs; a mobile typed with a leading 0 on Subscribe; the silent
   clients tile on the admin dashboard; shortcut sets and test rules in the client menu.
5. SRS: CP-51 / CP-52 in the Release-1 scope matrix; CP-56 name; CP-43 field; SA-03 tile; CP-23 layout; history row. UAT: steps for the
   changed pages; history row. README bullets; CLAUDE.md §5 entry LIS-CR-007; release minor.
