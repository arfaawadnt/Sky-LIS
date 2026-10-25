# Change requests 2026-10-24 — reviewer's features (LIS-CR-008, CR-010, CR-011, CR-012, CR-013)

Source: `docs/review/Sky-LIS_Reviewer-Comments_Response_V1.html` (LIS-RC-001) §4. After the quick wins of LIS-CR-007 the business owner
asked on 2026-10-24 to build the remaining packages **except CR-009** (catalogue simplification, parked): CR-008 addresses and
per-sample collection, CR-010 report designs, CR-011 cash ledger, CR-012 subscription reminders, CR-013 announcements. The decisions
D-2 … D-4 of LIS-RC-001 §5 are built on their **recommendations**, recorded below as assumptions for the business owner to confirm.

Six packages built in parallel by module agents in build slots; the integrator owns the one migration of the wave
(`ReviewFeatures`), routes, navigation, shell, SRS / UAT / README, browser check and release.

## 0. Rules of this wave (read with `docs/build/MODULE-PATTERN.md`)

Same rules as LIS-CR-007 §0 (`docs/build/CR-2026-10-24_Review-Quick-Wins.md`) with these differences:

- **Model changes and the migration**: change the aggregate and its EF configuration; **do not add a migration** — the integrator
  generates the one migration `ReviewFeatures` after every package has landed. Extra SQL (seed rows, backfills, indexes, grants on
  `saas` tables) goes into your module's `src/Platform.Infrastructure/Persistence/<Module>/<Module>.migration.sql` as an idempotent block
  with a dated heading (follow the existing blocks). The database suites (IntegrationTests, FunctionalTests) only pass once the
  migration exists: write the tests now, run the three non-database suites and the frontend gates, and run the database suites when the
  integrator messages you (you will be messaged; fix what fails then).
- **Shared files other agents edit at the same time** (`PlatformAdminCodes.cs`, `Tenant.cs`, `SettingKeys.cs`, `AuthContracts.cs`,
  `platform.{en,ar}.json`, `shared.{en,ar}.json`, `billing.{en,ar}.json`): re-read the file immediately before each edit, make one
  small targeted edit at a time, never rewrite the file, and keep your additions in your own region (named below).
- Never edit: `PermissionCatalog.cs`, `AppDbContext.cs`, `Persistence/Migrations/**`, `deploy/postgres/**`, `app.routes.ts`,
  `shell/**`, `public/i18n/shell.*.json`, `angular.json`, `package.json`, `CLAUDE.md`, `README.md`, test `Support/` folders. Ask for
  routes, nav entries, shell mounting points and labels in the report.
- Compatibility: the installed server runs 0.21.0 with real data; new columns nullable or defaulted; request shapes keep working.
- Report per MODULE-PATTERN §6 plus: files changed, migration needs (tables, columns, seed SQL), nav / route / shell needs with labels
  (en / ar), one README bullet per user-visible change, UAT steps to add or change.

## 1. Packages

| Package | CR | Slot | Modules / areas |
| --- | --- | --- | --- |
| P-ADR Laboratory and branch addresses + locality import | CR-008 (C-04) | `plat` | PlatformAdmin (`Tenant`, `SubscriptionRequest`, locations import), Organization (`Branch`), `features/public` wizard, `features/platform` clients page + governorates drawer, `features/organization` branch editor |
| P-SMP Collected outside per sample | CR-008 (C-02) | `reg` | Registration (`VisitTube`, order contract, CP-06), Samples (collection, labels), Tracker (ext tag) |
| P-RPT Five report designs | CR-010 (C-14) | `res` | Results documents (`features/results/ui/report.document.ts`), Catalogue `ReportTemplate.Design`, Pricing `ContractPricing.ReportDesign`, `SettingKeys.ReportDesign`, template editor preview |
| P-CSH Cash ledger and day close | CR-011 (C-15) | `bill` | Billing (`CashEntry`, ledger query, `CashClosing` to Release 1), `features/billing` |
| P-REM Subscription reminders and grace | CR-012 (C-16) | `fin` | PlatformAdmin (reminder job + log, grace), Security (`SessionPrincipalQuery` → `AuthProfile.SubscriptionNotice`), notification templates, `features/account` banner / modal component, SA dashboard tile |
| P-ANN Announcements | CR-013 (C-17) | `acc` | PlatformAdmin (`Announcement`, SA page, public endpoint), `features/auth` sign-in hero, `features/account` welcome card, client opt-out |

Regions in shared files: P-ADR owns codes `CLI-030…039` / `GEO-020…029` and the `clients.address.*` dictionary keys; P-REM owns
`SUB-001…` (new class `SubscriptionCodes` in `PlatformAdminCodes.cs`'s file or its own file) and `subscription.*` keys; P-ANN owns `ANN-001…`
and `announcements.*` keys; P-RPT owns `SettingKeys.ReportDesign`; P-REM owns `SettingKeys.SubscriptionGraceDays`; P-ANN owns
`SettingKeys.HideAnnouncements`. `AuthContracts.cs`: only P-REM adds a field (`SubscriptionNotice`), only P-ANN adds `Announcements`.

## 2. P-ADR — Laboratory and branch addresses (C-04)

**Today.** `Tenant` and `SubscriptionRequest` hold `CountryId`, `GovernorateId`, free-text `Address`; `Branch` holds `CountryId?`,
`GovernorateId?`. The platform keeps `saas.cities` / `saas.districts` / `saas.areas` under the governorate (LIS-CR-003 A2) with public
lookups `/api/v1/public/cities|districts|areas` and the SA-11 Governorates drawer; the patient file already uses the chain.

**Build.**
- `Tenant.CityId? / DistrictId? / AreaId?`, `SubscriptionRequest` the same, `Branch.CityId? / DistrictId? / AreaId?` (nullable Guids,
  validated through the existing chain rule: a district belongs to the city, an area to the district, the city to the governorate;
  codes in your region). Contracts: client detail / create / update, subscription start, branch detail / create / update — all optional.
- Subscribe wizard (CP-02): after Governorate, cascading selects City (markaz / city) → District → Area (village), optional, loaded from
  the public lookups (reuse `shared/patient`'s pattern of dedicated `computed` params). SA-04 client drawer and CP-36 branch editor: the
  same four fields. The public laboratory card (`GET /api/v1/public/laboratories/{slug}`) and the client list print "Area, District,
  City, Governorate" when filled (`AddressLine` helper in Domain, en / ar).
- **Locality import** (SA-11): `POST /api/v1/platform/governorates/{id}/localities/import` [platform.manage] taking a CSV (UTF-8, header
  `city,district,area` en and ar columns: `city_en,city_ar,district_en,district_ar,area_en,area_ar`, district / area optional) with
  `dryRun`, all-or-nothing, idempotent (existing names matched case-insensitively per parent), answer = created / existing / refused per
  level; SPA: an *Import CSV* action in the locations drawer with the dry-run summary, following the price-lines import (LIS-CR-003 C21).
  Assumption D-2: the official Egyptian list is a content deliverable of National Technology; the importer takes it when it arrives.
- Tests: domain chain rule for tenant and branch; functional round trips (wizard start with city / district / area, client update,
  branch update, import dry run + real run + a refused row); specs for the wizard selects, client drawer, branch editor and import.
- Intakes: PLT (addresses, import), ORG (branch address), ACC (wizard).

## 3. P-SMP — "Collected outside the laboratory" per sample (C-02)

**Today.** `Visit.CollectedOutside` (one flag per visit) set from the CP-06 visit flags; tubes (`VisitTube`) carry no flag; registration
auto-collects / auto-receives on save (parameters `autoCollect`, `autoReceive`).

**Build.**
- `VisitTube.CollectedOutside` (bool, default false). The order contract takes `samples[].collectedOutside` (per tube / sample type) and
  keeps the visit-level `collectedOutside` for one release: when only the old field is sent and true, every tube is marked. The visit
  keeps a **derived** `AnyCollectedOutside` (computed, not stored; `Visit.CollectedOutside` column stays as history, read-only, backfill
  SQL copies it to the visits' tubes in your module's `.migration.sql`).
- CP-06: the visit chip "Sample collected outside the laboratory" becomes a per-sample toggle in the sample list that appears once
  services are entered (one row per tube: sample type, colour, "Collected outside" switch), with a quick action "All outside"; the edit
  page (CP-10 › Edit) shows the same list read-only after save. Keep the page stylesheet under budget (new `ui/sample-flags` component).
- Samples: a tube marked outside is Collected at save with an optional "collected at" the clerk may type (default now) whatever the
  auto-collect parameter says, and still passes the Received gate; the sample label prints "EXT"; the samples dialog (CP-10) and the
  accession page show an "Outside" tag per tube; the Lab Tracker samples cell shows "ext" when any tube is outside (Tracker reader +
  contract `LabTrackerSamples.CollectedOutside` count).
- Tests: domain (tube flag, derived visit flag, old-field compatibility), functional (register with per-sample flags, old body, label
  text, tracker count), specs for the sample list, flags and tracker cell.
- Intakes: REG, SMP, TRK.

## 4. P-RPT — Five report designs (C-14)

**Today.** One result-report layout in `features/results/ui/report.document.ts` (laboratory header + logo, patient block + QR, one table
per unit, flags, signature, footer), fed by the result case view; `ReportTemplate` (CP-54) carries logo, signature, header / footer
text, QR, language per type; printing and the status log are in the Results module.

**Build.**
- `ReportDesign` enum (Domain, shared kernel of Results / Catalogue): `Classic`, `Modern`, `Compact`, `Letterhead`, `Cumulative`.
  Selection order: payer contract override (`ContractPricing.ReportDesign?`) → report template override (`ReportTemplate.Design?`) →
  laboratory default `SettingKeys.ReportDesign` (string, default `classic`, Printing group, Release 1). The result case / print
  document query returns the resolved `design` and, for `Cumulative`, the previous values of each test for the patient (last three,
  with dates) through the existing results history reader.
- SPA: `report.document.ts` becomes a dispatcher over five print components sharing one view model (`ui/documents/designs/*.ts`, each
  under the 4 kB stylesheet budget, A4 and Letter, Arabic RTL): 1 Classic (today), 2 Modern (two-column header, coloured flag bars, a
  range bar per numeric result), 3 Compact (dense, one page where possible), 4 Letterhead (blank top band 45 mm for pre-printed paper,
  no logo), 5 Cumulative (last three results side by side). Setup › Parameters › Printing gets "Report design" (select); the template
  editor (CP-54) and the contract editor (CP-34) get an optional design override; the template editor shows a live preview with sample
  data for the chosen design.
- Tests: domain (selection order), functional (resolved design per override, cumulative history shape), specs for the dispatcher and
  each design (renders the sample case, RTL), parameters / editors.
- Intakes: RES (designs, selection), CTB (template design), PRC (contract design).

## 5. P-CSH — Cash ledger and day close (C-15)

**Today.** Payments register (CP-23), Collections report (by branch / user / method), `CashClosing` aggregate (CP-30, built, Release 2,
scoped by branch + cashier + period, `ExpectedCash` / `CountedCash` / `Difference`). No cash-out entries.

**Build.**
- `CashEntry` aggregate (`app.cash_entries`, RLS): kind `CashOut` (expense: supplier, petty cash, bank deposit) and `CashIn` (opening
  float, other income), amount, method code (cash only for CashOut by default), reason (free text + a Reason of a new kind `CashEntry`
  in the Reasons list if the Reasons kind enum allows it; otherwise free text), reference, note, branch (session), cashier (session user),
  `PostedAtUtc`, `Void` with reason; permission `billing.create` to post, `billing.approve` to void. Codes in `BillingCodes` `BIL-060…`.
- **Cash ledger page** (`/billing/cash-ledger`, Finance, R1): a day or range per branch (session branch; `CanViewBranchCollections`
  for other cashiers): opening balance = the previous close's counted cash (or 0), receipts by method, refunds, voids, cash entries,
  closing balance, running balance per row; rows link to the receipt / entry; *Add cash out* / *Add cash in* dialogs; Excel export
  (`exceljs`, dynamic import) and a printable A4 "Daily cash sheet" (`ui/documents/cash-sheet-document`). Query `CashLedgerQuery(Day /
  From-To, BranchId?, CashierUserId?)` [billing.view] over one UNION ALL reader (payments, refunds, cash entries, closings).
- **Day close**: reuse `CashClosing` (CP-30) for the cashier's close from the ledger page: counted cash, difference with reason, closed
  periods read-only (a cash entry or a payment inside a closed period is refused `BIL-061`). Move CP-30 to Release 1 in this simplified
  form (the integrator flips the nav entry; tell them what the page needs).
- Tests: domain (entry rules, closed period), functional (post / void / ledger / close / refusal), specs for the page, dialogs, export,
  document.
- Intake: BIL.

## 6. P-REM — Subscription reminders and grace (C-16)

**Today.** `Tenant` is Trial (`TrialEndsAtUtc`), Active or Inactive; `Tenant.IsServiceable(now)` already refuses a trial past its end;
the wallet is charged by `WalletChargeJob` (hourly check, monthly fee); a wallet at or below zero pauses metered services. No warning.

**Build.**
- `SubscriptionReminderJob` (Infrastructure `Jobs/`, hosted like `WalletChargeHostedService`, `Lis:Jobs:SubscriptionReminders:{Enabled,
  PollInterval}` default on / 1 h; `ApiFactory` turns it off) runs `SubscriptionReminderRun` once a day per client: events **trial ends
  in 7 / 3 / 1 days, today, ended**; **wallet covers fewer than 7 / 3 / 1 days** of the monthly fee (balance ÷ (fee ÷ 30)), **at or
  below zero**; **grace ends in 3 / 1 days**. Each reminder is recorded in `saas.subscription_reminders` (tenant, kind, due key, sent
  channels, `SentAtUtc`) so none is sent twice. Channels: SMS + e-mail to the manager (templates `subscription-expiry`, `wallet-low`,
  `subscription-grace` in the template source defaults, placeholders laboratory, days, amount, link), through the existing
  `INotificationSender`.
- **Grace**: `SettingKeys.SubscriptionGraceDays` (platform parameter, default 7, 0…60). A tenant is *in grace* when its trial ended (not
  activated) or its wallet has been at or below zero for longer than the plan period; *expired* when the grace is over. `Tenant`
  exposes `SubscriptionState(now, graceDays, walletBalance…)` → `Ok | Warning | Grace | Expired` with the days left. Sign-in of an
  **expired** client is refused for non-administrators (`SUB-001`, 403 at `/auth/sign-in`, reason in `SIGN_IN_FAILED`); the primary /
  tenant administrators still sign in and every `/api` request but auth, the own profile and `/api/v1/account/wallet|subscription` is
  refused `SUB-002` (a gate in the pipeline next to `PasswordChangeGate` — propose it, the integrator wires `PipelineOrder`). Assumption
  D-3.
- **On screen**: `AuthProfile.SubscriptionNotice { level, daysLeft, amountToTopUp, trialEndsAt }` (read in `SessionPrincipalQuery`'s one
  query — extend the query, no second elevated call); SPA component `features/account/ui/subscription-notice` exporting a banner
  (`app-subscription-banner`, dismissable for the session, administrators only) and a sign-in modal (`app-subscription-modal`, shown when
  fewer than 3 days remain or in grace) — the integrator mounts them in the shell; both link to Account › Wallet (CP-66). SA dashboard
  (SA-03) tile "Expiring soon" (trial ending or in grace within 7 days) with its list on Clients (filter *Subscription* = Expiring /
  In grace / Expired). Notification templates (SA-20) list the three new codes.
- Tests: domain (`SubscriptionState`, reminder due keys), application (run: due events, idempotency), functional (profile notice,
  sign-in refusal of an expired non-administrator, administrator gate, dashboard tile, `days` refusals), specs for the banner, modal,
  tile and filter.
- Intake: PLT (reminders, grace, codes), FND (profile field, gate).

## 7. P-ANN — Announcements (C-17)

**Today.** Nothing. The sign-in page (design 5c, `features/auth/ui/auth-frame.component`) has a free hero area; the client dashboard
(`features/account/pages/dashboard.page`) is the landing page.

**Build.**
- `Announcement` aggregate (`saas.announcements`, no tenant, grants in `PlatformAdmin.migration.sql`): title en / ar, body en / ar
  (≤ 600 chars), image (file-store area `announcements`, PNG / JPEG / WebP ≤ 512 KB, sniffed like logos), link URL, placement
  `SignIn | Welcome`, schedule `FromUtc` / `ToUtc?`, audience (country?, plan?, status Trial / Active / any, optional client list),
  `IsActive`, counters `Impressions` / `Clicks` (incremented through two public endpoints, rate-limited by the global chain). Codes
  `ANN-001…`.
- SA page **Announcements** (`/admin/announcements`, group Operations, `platform.manage`; `platform.view` reads): register, editor
  drawer with image upload and preview, activate / deactivate, counters. Public: `GET /api/v1/public/announcements?placement=SignIn&
  laboratory=<slug>` (active, in schedule, audience matches the laboratory of the slug when given; `Cache-Control: public, max-age=300`
  through `SecurityHeadersMiddleware.PublicCacheItem`), `POST /api/v1/public/announcements/{id}/impression|click` (no body).
- Delivery: the laboratory sign-in page replaces the hero slogan with the active SignIn announcement (image + title + body + link) when
  one exists; the client dashboard shows the Welcome announcements as one dismissible card (dismissed for the session only). The session
  profile carries nothing new; the dashboard reads `GET /api/v1/account/announcements` (authenticated, audience resolved from the tenant).
  Opt-out: `SettingKeys.HideAnnouncements` (tenant setting, Subscription group; the Super Admin may set it on SA-04 "Hide announcements
  for this client"; a laboratory cannot change it in Release 1). Assumption D-4: National Technology's own and partner announcements only,
  no in-app strip.
- Tests: domain (schedule, audience), functional (CRUD + permission, public list honours schedule / audience / opt-out / cache header,
  counters), specs for the SA page, the sign-in hero and the welcome card.
- Intakes: PLT (announcements), ACC (sign-in / dashboard placements).

## 8. Integrator checklist

1. Migration `ReviewFeatures` once (all model changes + the modules' `.migration.sql` blocks), `migrations.sql` regenerated, then message
   every agent to run the database suites.
2. Routes / nav: `/billing/cash-ledger` (Finance, R1) and CP-30 cash closing → R1; `/admin/announcements` (Operations); shell mounting of
   `app-subscription-banner` (under the top bar, client portal) and `app-subscription-modal` (app root, after sign-in); `PipelineOrder`
   gate for expired subscriptions; shell labels en / ar.
3. Five suites in `integ`; frontend gates; browser check of each package on the review stack.
4. SRS 1.7 (CP-02, CP-06, CP-13/19 designs, CP-23/CP-30, new CP-70 Cash ledger, SA-03, SA-04, SA-11, new SA-22 Announcements, BR for
   grace), UAT 1.1, README bullets, CLAUDE.md §5, release minor.
