# Change request 2026-10-25 — reviewer's second round (LIS-CR-014)

Source: the business owner's notes of 2026-10-25 after using 0.22.0 — three subscribe-portal defects, one session defect and five Lab
Tracker requests (quoted in §1). Four packages built in parallel by module agents in build slots; the integrator owns the one migration
of the wave (`ListPreferences`), routes, navigation, shell, SRS / UAT / README, browser check and release.

## 0. Rules of this wave

Same as LIS-CR-008 §0 (`docs/build/CR-2026-10-24_Review-Features.md`): edit your module's files only; **no migration from you** (model +
EF configuration; the integrator generates `ListPreferences`); never edit `PermissionCatalog.cs`, `AppDbContext.cs`,
`Persistence/Migrations/**`, `deploy/postgres/**`, `app.routes.ts`, `shell/**`, `public/i18n/shell.*.json`, `angular.json`,
`package.json`, `CLAUDE.md`, `README.md`, test `Support/` folders; re-read shared files right before each targeted edit; run the
three non-database suites and the frontend gates, write the database tests and run them when the integrator messages you (P-SUB and
P-SES have no model change: run every suite now). Intake sections, per-suite counts, files changed, README bullets, UAT steps in the report.

## 1. The notes and the packages

| # | Note (business owner, 2026-10-25) | Package |
| --- | --- | --- |
| S1 | "When I tried to subscribe using the same mobile number, the application blocked me at the step *create account*, not in the mobile number field (if it is the blocker!)" | P-SUB |
| S2 | "I don't know what is the blocker, is it the mobile number or the user name!!" | P-SUB |
| S3 | "I can't go back to previous tabs to edit the data of the subscription (laboratory details and verify code …)" | P-SUB |
| G1 | "If I open two tabs, one for the admin portal and the other for the client portal, both become relevant to each other and the URL changes in the other one." | P-SES |
| T1 | "I need an actions column in the list view of the Lab Tracker as in the Search registrations screen; compare and merge the needs." | P-TRK |
| T2 | "Add three dots at the end of the header row of the list view to choose the optional columns per user, saved for each user as a favourite view." | P-TBL |
| T3 | "Option to view the records by registered service, one service per row, by pressing a button (merging with the worklist)." | P-TRK |
| T4 | "PDF print of the list tables, with an option to download as an Excel sheet; an option to select some records to print." | P-TBL |
| T5 | "When I open any action and land on another screen, I need a shortcut back to the main list view to edit another record, without changing the search criteria." | P-TRK (+ integrator: shell chip) |

| Package | Slot | Files / modules |
| --- | --- | --- |
| P-SUB Subscribe wizard: early checks, field-level refusals, back navigation | `acc` | `Application/PlatformAdmin/Commands/SubscriptionCommands.cs` (+ a check query), public controller, `features/public/**`, `public.{en,ar}.json`, `PublicSubscriptionFlowTests` |
| P-SES One browser, two portals: per-portal refresh cookie and session channel | `fin` | `WebApi/Security/RefreshCookie.cs`, `RateLimiting.cs`, `Controllers/Identity/AuthController.cs`, `core/auth/**` (`AuthStore`, `session-channel.ts`, `idle-watcher.ts`, `refresh-lock.ts`), `core/http/auth.interceptor.ts`, `AuthFlowTests` |
| P-TRK Lab Tracker: actions column, service rows, URL filter, return shortcut | `reg` | Tracker (reader, query, contracts, controller), `features/tracker/**` except the table element's chooser / print attributes, `tracker.{en,ar}.json`, new `shared/ui/return-link/**` |
| P-TBL Shared table: column chooser saved per user, print to PDF, Excel, selection | `bill` | `shared/ui/data-table/**`, new `shared/export/**`, `Domain/Tenancy/TenantMembership.cs` (`ListPreferences`), `Application/Account` preferences command/contract, `AccountController`, `AuthContracts.cs` (`ListPreferences`), `core/auth/auth.store.ts` (one method), `features/registration/pages/visits.page.*` (apply), the `<app-data-table>` element and header toolbar of `features/tracker/pages/tracker.page.html` (apply — targeted edits only) |

## 2. P-SUB — Subscribe wizard (S1, S2, S3)

**Today.** `StartSubscriptionCommand` (step 1) creates the request and sends the code without checking whether the manager mobile
already belongs to a platform user; `CompleteSubscriptionCommand` (step 3) provisions the laboratory and its administrator, where the
mobile uniqueness of `IUserMobileNumbers` (or the username uniqueness) refuses — so the user is blocked at *Create account* with a message
that does not say which field. The wizard has no way back from steps 2–4 to step 1.

**Build.**
- **Early checks** — `POST /api/v1/public/subscriptions/check` (anonymous, rate-limited like `start`) taking the step-1 fields and
  answering per field: `managerMobile` / `mobile` already used by a platform user or by another laboratory (as `start` must refuse too:
  `SUB-011` "This mobile already belongs to an account — sign in or use another number"), laboratory name already taken (`SUB-012`,
  if that rule exists), so the wizard marks the field **before** sending the code. Step 3 checks the username before submitting
  (`GET /api/v1/public/subscriptions/{id}/username-available?username=` → `SUB-013` taken) and shows the refusal on the username field.
- **Field-level refusals** — every refusal of `start`, `verify`, `complete` carries `fields` in its problem details (the existing
  `VALIDATION-400` shape, or `rules`) naming the field (`managerMobile`, `mobile`, `username`); the SPA renders them on the field
  (`shared/forms/problem-text.ts`) and, for a step-1 field refused at step 3, returns the user to step 1 with the field marked and
  the explanation "Change the mobile, then a new code is sent".
- **Back navigation** — the step rail is clickable for done steps; *Back* on steps 2 and 3 returns to step 1 keeping every typed value;
  leaving step 1 again sends a new code **only when a step-1 value changed** (otherwise the verified code stands and the wizard jumps
  to step 3); a request already verified whose details change starts a new request (the old one expires). Step 4 (ready) has no back.
- Tests: functional (`check` per field, `start` refusing a used mobile with `SUB-011` and `fields`, username check, back-and-change
  starting a new request); specs for the rail, Back, field marking and the step-1 return from a step-3 refusal. Intake ACC.

## 3. P-SES — One browser, two portals (G1)

**Today.** One refresh cookie `__Secure-rt` (Path `/api/v1/auth`) for both portals and one BroadcastChannel `platform-session`: signing
into the Super Admin in a second tab replaces the laboratory session of the first tab, which the channel tells to reload to its sign-in
page (C-01 of LIS-VR-002). On the installed server both portals share the origin, so an operator cannot keep both open.

**Build.** A session **per portal** in one browser:
- `RefreshCookie.Name(context, portal)`: `__Secure-rt` for the laboratory portal and `__Secure-rta` for the Super Admin (insecure
  `rt` / `rta`), same path and attributes. Sign-in writes the cookie of the target (`admin` → `rta`); refresh and sign-out take the
  portal from the request (`?portal=admin|client`, default client) and read / clear that cookie only; the rate limiter's refresh policy
  keys on the cookie it reads. A sign-in still revokes the session of the cookie **of its own portal** that the browser presented.
- SPA: `PortalService` (the URL: `/admin/**` = admin) decides the portal of every auth call (`AuthStore.hydrate`, `refresh`, `signOut`,
  the interceptor's replay); the session channel name, the refresh lock and the idle watcher are keyed by portal
  (`platform-session:admin` / `platform-session:client`), so tabs of one portal still follow each other (sign-out, branch change,
  profile change, idle) and tabs of the other portal never react. `signInUrl` keeps sending each portal to its own sign-in page.
- Tests: functional (two cookies coexist; refresh with `portal=admin` rotates only `rta`; sign-out of one portal leaves the other
  session valid; the old single-cookie bodies keep working), specs for the channel key and the portal-aware store. Intake FND (security
  edge) and a note for `deploy/SECRETS.md` if any runbook mentions the cookie name. Document the cookie pair for the integrator's
  CLAUDE.md entry.

## 4. P-TRK — Lab Tracker: actions, service rows, URL filter, return shortcut (T1, T3, T5)

**Today.** The tracker table (CP-69) has no actions column (only the next-step button and the accession link); rows are visits; the
filter lives in the facade (lost when the user leaves the page); the next-step links open other pages without a way back.

**Build.**
- **T1 Actions column** like CP-10 (`features/registration/pages/visits.page.html` is the model; the tracker cannot import the
  registration feature, so its actions navigate): inline *Details* (`/registration/accession?accession=`), *Edit*
  (`/registration/visits/:id`, `registration.edit`), *Payments* icon (`/billing/payments?visitId=`, `billing.view`), *Documents* icon
  (the accession page's documents card: `/registration/accession?accession=&tab=documents` — add the `tab` query to the accession page
  if it has none), then the shared `app-row-menu`: *Patient* (`/patients/:patientId`, `patients.view`), *Samples* (accession page samples
  card), *Results* (`/results/by-accession?accession=`, `results.view`), *Cancel services* (accession page, `registration.cancel`). Keep
  the next-step button. Every navigation carries `returnTo` (below).
- **T3 Service rows** — a *Visits | Services* toggle beside the Table / Board toggle: `GET /api/v1/tracker/visits?rows=services`
  answers one row per live top-level line (service code / name, its unit's result status, sample status, next step and time left of
  that service, the visit's accession / patient / payment), counters unchanged (visit-based), sort keys + `service`; reader = the
  existing SQL joined to `app.visit_lines` + per-line result aggregates; contract `LabTrackerServiceItem`; the board stays visit-based.
- **T5 Return shortcut** — the whole filter (day / from / to, view, stage, attention, search, rows, page, sort) lives in the URL query
  (`?day=…&stage=…`), read on open and written on every change (replace state), so a return restores the criteria; every action
  and next-step link adds `returnTo=<tracker url>`; new shared `shared/ui/return-link/return-link.component.ts` (`app-return-link`):
  reads `returnTo` from the current route, accepts only in-app paths (starts with `/`, no `//`, no scheme), renders a chip "Back to
  Lab Tracker" (label from the path's nav entry: pass `[label]`), and navigates back; the integrator mounts it in the shell's top bar
  (tell them the selector and inputs) so every target page shows it without changes. Also keep `returnTo` through the accession page's
  own links where cheap.
- Tests: functional (`rows=services` shape and sort, counters unchanged), domain (service-level stage / next step), specs (actions by
  permission, services toggle, URL round trip, return chip). Intake TRK.

## 5. P-TBL — Shared table: column chooser per user, print, Excel, selection (T2, T4)

**Today.** `shared/ui/data-table` has `selection="single|multi"` with `[(selected)]`, sorting, templates and the row-actions menu; no
column chooser; printing exists only as per-module documents; Excel only in Pricing (`features/pricing/data/excel.ts`, `exceljs`).

**Build.**
- **T2 Column chooser** — `TableColumn.optional?: true` (hideable; the first column and action column never) and a new input
  `[listKey]="'tracker'"` on `app-data-table`: a "⋯" button at the end of the header row opens a popover (reuse the `app-row-menu`
  pattern: keyboard, Escape, outside click) listing the optional columns with checkboxes and *Reset*; hidden columns are not rendered
  (cells and header). Persistence per user: `TenantMembership.ListPreferences` (JSON text column, nullable: `{ "<listKey>": { "hidden":
  ["col", …] } }`, ≤ 4 KB, TEN-015 when larger or malformed), `PUT /api/v1/account/preferences/lists/{listKey}` [any member] body
  `{ hidden: [] }`, `AuthProfile.ListPreferences` (read in `SessionPrincipalQuery`'s one query like `ResultsDensity`; evict the principal
  on update), `AuthStore.updateListPreferences(listKey, hidden)` (optimistic, signal), the table reads its hidden set from the store when
  `listKey` is given. The platform tenant's memberships work the same.
- **T4 Print and Excel** — new `shared/export/table-export.ts` + `shared/ui/data-table/table-print.component.ts`: a toolbar slot
  (`[exportable]="true"` → *Print* and *Excel* buttons at the end of the table toolbar, or `appTableTools` template) that prints the
  **visible columns** of the **selected rows** (multi selection) or of every loaded row when nothing is selected, as an A4 landscape
  print document (laboratory name, list title, filter summary passed as `[printSubtitle]`, date, page numbers via CSS counters,
  Arabic RTL) through the browser's print dialog (PDF = the browser's "Save as PDF"), and downloads the same rows as `.xlsx`
  (`await import('exceljs')`, sheet named after the list, header row bold, dates as dates, numbers as numbers). Cells render through the
  column's `value` / cell text; `TableColumn.exportValue?` overrides.
- **Apply** to the Search registrations table (CP-10: `listKey: 'visits'`, optional columns Branch / Payer / Patient share / Remaining,
  print subtitle = the active filters) and to the Lab Tracker table (`listKey: 'tracker'`; optional Registration / Samples / Results /
  Payment / Time left; edit only the `<app-data-table …>` element's attributes and the toolbar area of `tracker.page.html` plus the
  column definitions in `tracker.page.ts` — P-TRK edits the rest of those files at the same time: targeted edits, re-read first).
- Tests: domain (ListPreferences limits), functional (PUT + profile round trip, TEN-015), specs (chooser hides columns and persists
  through the store, print document renders visible columns of selected rows, Excel builds the sheet, CP-10 and tracker hooks). Intake
  FND (preferences) and REG (CP-10 export). Migration need: one nullable column `app.tenant_memberships.list_preferences` (text / jsonb).

## 6. Integrator checklist

1. Migration `ListPreferences`; `migrations.sql`; message P-TBL to run the database suites.
2. Mount `app-return-link` in the shell top bar; shell labels; CLAUDE.md §5 entries (cookie pair, session channel per portal, list
   preferences, tracker service rows, return link); SRS 1.8 (CP-02, CP-69, CP-10, CP-03 cross-cutting sessions), UAT 1.2, README.
3. Five suites in `integ`, frontend gates, browser check: subscribe with a used mobile (field marked at step 1), Back between steps,
   admin + client tabs side by side, tracker actions / services / chooser / print / Excel / return chip, CP-10 chooser.
4. Release minor; install on request.
