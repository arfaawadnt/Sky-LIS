# LIS-INT-REP-001 — Reporting: dashboard result figures and platform activity (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `reporting` / `dashboard` (`PermissionCatalog.Reporting`, `PermissionCatalog.Dashboard`), build slot `rep`, wave 3 (MODULES.md §2) |
| Release scope | **Release 1 only** (decided by the user on 2026-10-10, scope of the R1 prototype builds `*-v2.1-Release1.html` / `*-v3.1-Release1.html`): the result KPIs of the branch dashboard (CP-05), the client activity of the Super Admin dashboard (SA-03) and, since the change request of 2026-10-13 (item 3), the **work amount report** (§14b). Claims (CP-31), the sixteen financial reports (CP-35) and the claim/report pages of WF-10 are **Release 2** and are not built (§2). |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), MODULES.md (§1, §5) and the LDM synthesis. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.1 — 2026-10-13 (work amount report, change request item 3) |
| Implemented in | `src/Platform.Infrastructure/Persistence/Reporting/` (`ClientActivityReader.cs`, `Reporting.migration.sql`, `WorkAmountReader.cs`: reader, names and letterhead adapters); the Account module's dashboard reader `src/Platform.Infrastructure/Persistence/Account/BranchDashboardReader.cs` (result figures only), `src/Platform.Application/Account/IAccountPorts.cs` (`BranchDayFigures`, three optional figures) and `src/Platform.Application/Account/Queries/DashboardQueries.cs` (pass-through), by agreement with the integrator. Work amount report: `src/Platform.Domain/Reporting/` (`ReportingCodes`, `WorkAmountGrouping`, `ReportPeriod`), `src/Platform.Contracts/Reporting/ReportingContracts.cs`, `src/Platform.Application/Reporting/` (`IWorkAmountReader`, `IWorkAmountNames`, `IReportLetterhead`, `WorkAmountAggregator`, `Queries/WorkAmountReportQueries.cs`), `src/Platform.WebApi/Controllers/Reporting/ReportsController.cs`, `frontend/src/app/features/reporting/` (route `/reports/work-amount`), `frontend/src/app/core/api/reporting.contracts.ts`, `frontend/public/i18n/reporting.{en,ar}.json` |

## 1. Purpose and policy basis

The branch dashboard (SRS CP-05) shows "the branch's day at a glance": among its KPIs **pending results**, **unverified
backlog** and **delayed tests**, whose delays "derive from test durations (BR-13)". The Super Admin dashboard (SA-03)
shows, per period, the **served patients, tests and registrations** of the clients. Both are read-only figures computed
from the operational records other modules own; this module owns no aggregate in Release 1.

- **Turnaround clock** (BR-13; LDM synthesis §6 "Worklist and TAT monitoring", §3.4 Duration / Test Days): the expected
  result moment is the start (collection, else registration) plus the test's duration counted on its test days; the
  share of the duration consumed is compared with the tenant thresholds (`delayWarnPercent` 80, `delayLatePercent` 100;
  CP-57). LDM colours rows at 70/80/90 % "before lateness" (§6); the dashboard counts the tests that are **late**.
- **Tenant isolation** (02 §4, D-02): a laboratory never sees another laboratory's figures; the platform tenant sees
  aggregate counts of every client and nothing else (no patient, visit or result rows).

## 2. Scope and out of scope

In scope (Release 1):

- CP-05: pending results, unverified backlog and delayed tests of the session branch for the selected local day, in the
  existing response contract of the Account module (`BranchDashboardKpis`), from `app.results` (Results, MODULES.md §5).
- SA-03: served patients, tests and registrations per period across the clients, and registrations per client, through
  the platform module's `IClientActivityReader` port (replacing its `[FallbackAdapter]` `ZeroClientActivityReader`).
- **Work amount report** (change request 2026-10-13, item 3; §14b): the income of the visits registered in a period of
  laboratory days — cash (patient shares) and credit (payer shares), cancelled services excluded — grouped by day, payer,
  contract tier, registering user, referring doctor, clinical group, service or branch, with a grand total; a screen
  (`/reports/work-amount`, Finance group), an A4-landscape print and a CSV export; bounded per member by the CP-37
  special right **report days limit** (`SpecialRights.ReportDaysLimit`, Organization).

Out of scope:

- **Release 2 (not built, parked by decision of 2026-10-10)**: Claims CP-31 (Claim aggregate Draft/Issued/Cancelled,
  layouts, `CLM-` numbers, claim document and CSV), Financial reports CP-35 (patients' income, payment methods,
  contract type, monthly statistics, patient statement, doctor income with share, clients income, service income
  analysis, outstanding balances, discounts, cancellations, transfers, total per day, price-list hand-out, cost vs
  income, turnaround compliance), the claims and reports pages and their navigation entries.
- The delayed-tests **table** of CP-05 (`BranchDashboard.DelayedTests`), "recent activity", and the worklist colours
  (Results module, CP-18).
- Charts per period on SA-03 beyond the existing response (the platform module owns the page).

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Pending results | results (`app.results`) of the visits registered in the day, status Ordered, Complete or Reviewed |
| Unverified backlog | results holding a value that waits for verification: status Complete or Reviewed, visits registered up to the end of the day |
| Delayed test | an open result (Ordered, Complete, Reviewed) whose consumed share of the turnaround reached `delayLatePercent` |
| Turnaround start | the tube's collection time (`app.samples.collected_at_utc`, live samples), else the visit's registration time |
| Expected result time | `app.results.expected_at_utc` (stored by Results from BR-13) |
| Served patients | distinct patients of the visits registered in the period, per client |
| Tests (SA-03) | results of those visits that are not cancelled |
| Registrations | visits registered in the period (`app.visits.registered_at_utc`) |
| Live service line | a visit line (`app.visit_lines`) whose status is Ordered or Reserved (BR-05: a Reserved service is billed); a Cancelled line is never counted |
| Top-level line | a Service or Package line; a PackageMember line carries no amounts of its own (the package line does) and is not counted as a service |
| Gross | Σ `list_price` of the live top-level lines (the contract list prices, BR-06) |
| Discount | Σ `discount` of those lines (the visit discount allocated to its lines, Registration intake §5) |
| Cash income | Σ `patient_share` of those lines — the patient's part **net of the discount** (Registration stores it net) |
| Credit income | Σ `payer_share` of those lines — the payer's part, never discounted |
| Net | cash income + credit income (= gross − discount) |
| Report day | a laboratory-calendar day (`ILaboratoryTimeZone`; UTC when no zone is known), inclusive bounds; "today" is the laboratory's today |
| Report days limit | CP-37 special right `ReportDaysLimit`: how many days back the member may report (0 = today only, empty = no limit); `SpecialRights.EarliestReportDay(today)` |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `dashboard.view` | CP-05 (Account module's `GET /api/v1/dashboard/branch`): the three figures travel in the existing response |
| `platform.view` | SA-03 (platform module's `GET /api/v1/platform/dashboard`), platform tenant only (PLT-403 otherwise) |
| `reporting.view` | the work amount report (`GET /api/v1/reports/work-amount`, the page `/reports/work-amount`, the print) |
| `reporting.export` | its CSV (`GET /api/v1/reports/work-amount/export`); the button is shown with `*appCan`, the server decides |
| `organization.view` | lets a reporter pick a branch that is not among the membership's assigned branches (§8 INV-REP-007) |
| CP-37 special right `reportDaysLimit` | bounds the period's start for every member (INV-REP-006); set on the role or as a user override (Organization) |
| `claims.*` | catalogued, **unused in Release 1** (Release 2 pages) |

The dashboard figures need no request of their own (adapters behind ports the Account and platform modules declare). The
work amount report is the module's first request pair (`WorkAmountReportQuery`, `ExportWorkAmountReportQuery`), gated as
above; deny by default.

## 5. Aggregates

None in Release 1. Read models only:

- `BranchDayFigures` (Account port) gains three optional figures `PendingResults`, `UnverifiedBacklog`, `DelayedTests`
  (additive; the response contract `BranchDashboardKpis` is unchanged).
- `ClientActivitySnapshot` (platform port) is filled from the rows of `saas.client_activity_counts`.

## 6. State table

Not applicable (no aggregate). The figures are derived on every read; nothing is stored.

## 7. Transition table

Not applicable (no command). Queries: CP-05 branch dashboard (Account), SA-03 platform dashboard (platform).

## 8. Invariants

| Id | Statement | Enforced where |
| --- | --- | --- |
| INV-REP-001 | A laboratory's figures are computed from its own rows only | RLS on every table read (connection opened through EF with the tenant stamp) + `tenant_id` joins + `branch_id = @branch` |
| INV-REP-002 | The cross-tenant activity is readable only by a session of the platform tenant and returns counts only | `saas.client_activity_counts` (SECURITY DEFINER, owner `lis_owner`): raises 42501 unless `app.current_tenant` is the platform tenant; returns `(tenant_id, served_patients, tests, registrations)`; `REVOKE … FROM PUBLIC`, `GRANT EXECUTE … TO lis_app`; gate `ReportingGateTests` |
| INV-REP-003 | No elevation in C# for the activity figures | `ClientActivityReader` takes only `AppDbContext`; gate `ReportingGateTests` |
| INV-REP-004 | A figure whose source table or column is not migrated yet is "not available" (`null`), never 0 | 42P01 / 42703 caught per statement (CP-05); function answers no row while `app.visits` is absent, 42883 caught (SA-03 shows zeros, as the fallback did) |
| INV-REP-005 | The result figures read only the agreed `app.results` columns of MODULES.md §5 | gate `ReportingGateTests.The_result_figures_read_only_the_agreed_results_columns_and_the_turnaround_threshold` |
| INV-REP-006 | A work amount report never starts before the member's earliest allowed day (`SpecialRights.EarliestReportDay(today)`, today in the laboratory's calendar) | `ReportPeriod.RequireWithinLimit` → **REP-001** (403) in both handlers; the page hints the earliest day and bounds the date inputs (`min`) |
| INV-REP-007 | A work amount report covers one branch: the session branch (TENANT-004 without one), or another branch only when the member holds `organization.view` or the membership is assigned to it (no assignment = every branch, as CP-04 reads it) | `WorkAmountReportComposer.ResolveBranchAsync` → **REP-003** (403) |
| INV-REP-008 | Cancelled services never count; a visit whose services were all cancelled contributes nothing; a package counts once (its member lines carry no amounts and are not read) | `WorkAmountReader` predicate (`status <> 'Cancelled' AND kind <> 'PackageMember'`); integration S-W9 |
| INV-REP-009 | A period is at most `ReportingCodes.MaxReportSpanDays` (366) days and ends no earlier than it starts | `ReportPeriod.Window` → **REP-002** (400); the validators refuse a reversed pair as VALIDATION-400 first |
| INV-REP-010 | The report of a laboratory is computed from its own rows only | tenant query filter on `Visit` and `VisitLine` + RLS; no elevation (`WorkAmountReader` takes only `AppDbContext`); integration S-W10 |

## 9. Segregation-of-duties rules

None (read-only figures).

## 10. Approval gates

None.

## 11. Events and policies

Consumed: none. Raised: none. The figures are read on demand from `app.visits`, `app.samples` and `app.results`
(MODULES.md §5); no projection is kept, so nothing has to follow `VisitRegistered`, result or sample events.

## 12. Data classification and retention

| Data | Class | Note |
| --- | --- | --- |
| CP-05 counts | Operational aggregate | branch-scoped, tenant-scoped; no patient data leaves the query |
| SA-03 counts per client | Commercial aggregate | counts per tenant id; names joined by the platform module from `saas.tenants` |
| Retention | nothing stored | computed per request |

## 13. Reference data and configuration

| Source | Key | Default | Use |
| --- | --- | --- | --- |
| Tenant setting (CP-57) | `delayLatePercent` | 100 | delayed ⇔ consumed share ≥ this percentage (BR-13) |
| Tenant setting (CP-57) | `delayWarnPercent` | 80 | not counted on the dashboard (worklist colour only, Results CP-18) |
| Results (`app.results`, §5) | `branch_id`, `status`, `visit_id`, `tube_id`, `expected_at_utc` | — | the three CP-05 figures |
| Registration (`app.visits`, §5) | `registered_at_utc`, `patient_id`, `tenant_id` | — | day window (CP-05), period and served patients (SA-03) |
| Samples (`app.samples`, §5) | `tube_id`, `collected_at_utc`, `status` | — | turnaround start |
| Registration (`app.visits`, `app.visit_lines`, §5; entity types `Visit`, `VisitLine`) | `branch_id`, `registered_at_utc`, `registered_by_user_id`, `payer_id`, `contract_tier_id`, `referring_doctor_id`; line `status`, `kind`, `sequence`, `service_id`, `service_code`, `service_name_en/ar`, `list_price`, `patient_share`, `payer_share`, `discount` | — | work amount report (§14b) |
| Laboratory time zone | `ILaboratoryTimeZone` (client record country → tenant parameter `timeZone` → platform default) | Africa/Cairo | report days of the work amount report |
| Organization (CP-37) | `SpecialRights.ReportDaysLimit` (role `special_report_days_limit`, user override `override_report_days_limit`) | empty = no limit | INV-REP-006 |
| Read ports for captions | `IBranchLookup`, `IUserDirectory` (Organization), `IPayerLookup` (Pricing: payers, tiers), `IDoctorLookup` (Catalogue B), `ICatalogueLookup` (services → clinical group) | — | `IWorkAmountNames` adapter; an id the ports no longer know is captioned with the id itself |

No new setting key.

## 14. Registers, reports and exports

| Figure | Definition (SQL) | Where |
| --- | --- | --- |
| Pending results (CP-05) | `count(*)` of `app.results r JOIN app.visits v` with `r.branch_id = @branch`, `r.status IN ('Ordered','Complete','Reviewed')`, `v.registered_at_utc ∈ [day start, day end)` | `BranchDashboardReader.PendingResultsSql` |
| Unverified backlog (CP-05) | `r.status IN ('Complete','Reviewed')`, `v.registered_at_utc < day end` | `BranchDashboardReader.UnverifiedBacklogSql` |
| Delayed tests (CP-05) | open results with `expected_at_utc` and `as_of ≥ start + (expected − start) × delayLatePercent / 100`, where `start = coalesce(latest live collection of the tube, registration)` and `as_of = min(now, day end)` | `BranchDashboardReader.DelayedTestsSql` |
| Served patients, tests, registrations (SA-03) | per tenant: `count(DISTINCT v.patient_id)`, `Σ results of the visit with status ≠ 'Cancelled'`, `count(*)` of visits with `registered_at_utc ∈ [from, to)` | `saas.client_activity_counts(timestamptz, timestamptz)` → `ClientActivityReader` |
| Registrations by client (SA-03) | the per-tenant `registrations`, clients without registrations omitted, highest first (the platform handler keeps the top 10 and joins names) | `ClientActivityReader.Snapshot` |

### 14b. Work amount report (change request 2026-10-13, item 3)

"Add a work amount report showing the income of the accessions registered today, cash and credit, without cancelled
visits; a screen with many grouping types for showing the amounts; a user right for a time limit in days for each user."

| Element | Definition | Where |
| --- | --- | --- |
| Endpoint | `GET /api/v1/reports/work-amount?from&to&groupBy&branchId` [`reporting.view`] → `WorkAmountReport` (period echoed after defaults, grouping, branch, time zone, letterhead, rows, totals, generated by/at); the same DTO prints | `ReportsController`, `WorkAmountReportQuery` |
| Export | `GET /api/v1/reports/work-amount/export` (same parameters) [`reporting.export`] → `text/csv; charset=utf-8`, UTF-8 BOM, RFC 4180, formula-leading cells prefixed with an apostrophe, numbers invariant with two decimals, English headers `Group, Group (Arabic), Code, Visits, Services, Gross, Discount, Cash income, Credit income, Net`, the rows then the `Total` line; file `work-amount-<from>-<to>-<grouping>.csv` | `ExportWorkAmountReportQuery`, `WorkAmountCsv` |
| Period | `from`…`to` laboratory-calendar days, inclusive; default today (one given bound = a one-day period); window `[from 00:00, to + 1 day 00:00)` in the laboratory's zone; `from ≥ EarliestReportDay(today)` else REP-001; `from ≤ to` and span ≤ 366 days else REP-002 | `ReportPeriod` |
| Branch | `branchId` null = session branch; another branch per INV-REP-007 else REP-003 | `WorkAmountReportComposer` |
| Source rows | `app.visits v JOIN app.visit_lines l ON l.visit_id = v.id` with `v.branch_id = @branch`, `v.registered_at_utc ∈ [from, to)`, `l.status <> 'Cancelled'`, `l.kind <> 'PackageMember'`, ordered by registration, visit, line sequence; one row per live top-level line with the visit's keys | `WorkAmountReader` (EF over the Registration entity types, tenant filter + RLS) |
| Groupings (`groupBy`) | `Day` (laboratory day of the registration, ascending; default), `Payer`, `ContractTier` ("payer · tier"), `User` (registered by), `Doctor` (referring doctor; "No referring doctor" / "بدون طبيب محوِّل" for none), `ClinicalGroup` (of the service; "Not grouped" / "بدون مجموعة" when the service has none), `Service` (code + snapshot names), `Branch` (one row: the report covers one branch) | `WorkAmountGrouping`, `WorkAmountReportComposer.RowsAsync` |
| Figures per row | `visits` = distinct visits in the group; `services` = live top-level lines; `gross` = Σ list price; `discount` = Σ discount; `cashIncome` = Σ patient share; `creditIncome` = Σ payer share; `net` = cash + credit | `WorkAmountAggregator.Rows` |
| Totals | the same figures over every line; `visits` distinct across the whole report (a visit split over several groups counts once) | `WorkAmountAggregator.Total` |
| Order | by English label, then Arabic label (case-insensitive), then key; `Day` by key (ISO date) | `WorkAmountAggregator.Rows` |
| Captions | bilingual (`labelEn`/`labelAr`) with the code when the group has one (branch, doctor, clinical group, service); an unknown id is captioned with the id, never dropped | `IWorkAmountNames` |
| Screen | `/reports/work-amount` (Finance group, `reporting.view`): from/to (default today; the member's earliest day as `min` and a hint), group-by select, branch select when the member may select more than one branch (`GET /api/v1/organization/branches/selectable`), the table with a totals row, Print (A4 landscape through the print kit, `app-document-frame`), Export CSV (`*appCan="'reporting.export'"`) | `features/reporting` |
| Codes | **REP-001** `ReportPeriodBeyondLimit` (403), **REP-002** `ReportPeriodInvalid` (400), **REP-003** `ReportBranchNotAllowed` (403); plus TENANT-004 (no session branch) and VALIDATION-400 (unknown grouping, reversed pair) | `ReportingCodes` |

## 15. Notifications and escalations

None. (A late test is shown; an escalation when the share passes `delayLatePercent` is a later Notifications intake.)

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Application.UnitTests/Reporting/DashboardActivityTests.cs`,
`tests/Platform.IntegrationTests/ReportingModuleTests.cs`, `tests/Platform.WebApi.FunctionalTests/ReportingFlowTests.cs`
and `tests/Platform.ArchitectureTests/ReportingGateTests.cs`.

```gherkin
Scenario S-D1: the reader's result figures reach the dashboard KPIs unchanged
Scenario S-D2: before app.results exists the three figures are null ("not available")
Scenario S-D3: a branch with results Ordered (due 3 h after registration), Complete (due in 3 days), Reviewed (due 23 h),
               Verified and Cancelled today, a Complete result of yesterday and a result of another branch
  When the day's figures are read at the end of the day with delayLatePercent 100
  Then pending = 3, unverified backlog = 3, delayed = 2
Scenario S-D4: the branch dashboard of an empty branch answers 0 for the three figures once Results is migrated
Scenario S-A1: activity rows add up across clients; registrations per client are listed highest first, idle clients omitted
Scenario S-A2: no activity → the empty snapshot; counts above int range are clamped
Scenario S-A3: two clients with 3 and 1 visits in the period (2 distinct patients in the first, one visit outside the
               period, results Verified, Ordered and Cancelled) → registrations 4, served patients 3, tests 2,
               by client [(A, 3), (B, 1)] for a platform session
Scenario S-A4: a laboratory session calling the activity function is refused with 42501; the function is SECURITY DEFINER
Scenario S-A5: the Super Admin dashboard (period year) counts the registrations and patients of a client
Scenario S-A6: a laboratory user holding platform.view is refused (PLT-403) on the platform dashboard

Scenario S-W1: at 22:30 UTC in a +03:00 laboratory the default period is the 14th (the laboratory's today) and the
               window is [13th 21:00Z, 14th 21:00Z); the letterhead names the laboratory and the branch
Scenario S-W2: a member whose role may report on today only (limit 0) is refused a period starting yesterday (REP-001, 403);
               today stays available; a member without a limit may report any past day
Scenario S-W3: a period ending before it starts, or longer than 366 days, is REP-002; one bound names a one-day period
Scenario S-W4: another branch needs organization.view or the membership's assignment (REP-003 otherwise); the session
               branch never needs a right; without a session branch the module refuses (TENANT-004)
Scenario S-W5: grouping by payer: a visit with two lines counts once in its payer's row; cash = Σ patient shares,
               credit = Σ payer shares, net = cash + credit; the total counts 3 visits and 4 services; rows by label
Scenario S-W6: grouping by day uses the laboratory calendar (22:10 UTC on the 13th is the 14th at +03) and orders the days
Scenario S-W7: doctor, clinical group, contract tier, user, service and branch rows carry bilingual captions and codes;
               "No referring doctor" / "Not grouped" rows gather the rest; an unknown id is captioned with the id
Scenario S-W8: the CSV is UTF-8 with BOM, quotes a label with a comma and quotes, prefixes a leading "=" with an apostrophe,
               writes amounts with two decimals and ends with the Total line
Scenario S-W9 (engine): the reader returns the live top-level lines of the branch in the window only — a cancelled line,
               a package member, a visit whose services were all cancelled, another branch and the day after the window
               never appear; the window end is exclusive
Scenario S-W10 (engine): another tenant never sees the branch's lines; its own visit in a branch of the same id is its own
Scenario S-W11 (engine): the lines fold into payer and day rows with a grand total (3 visits, 3 services, 600 gross,
               10 discount, 270 cash, 320 credit, 590 net)
Scenario S-W12 (API): a reporter with reporting.view reads today's income of the session branch grouped by payer with
               the totals; by day the single row is keyed by the report's own day; an unknown grouping is 400
Scenario S-W13 (API): a role limited to today (special_report_days_limit = 0) gets 403 REP-001 for a period starting
               yesterday and 200 for today
Scenario S-W14 (API): the CSV is 403 without reporting.export and, with it, a text/csv file with BOM, header, row and total
```

Scenario ids S-W1…S-W8 map to `tests/Platform.Application.UnitTests/Reporting/WorkAmountReportTests.cs`, S-W9…S-W11 to
`tests/Platform.IntegrationTests/WorkAmountReportModuleTests.cs`, S-W12…S-W14 to
`tests/Platform.WebApi.FunctionalTests/WorkAmountReportFlowTests.cs`; the page and facade specs are
`frontend/src/app/features/reporting/**/*.spec.ts`.

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | What is a "pending result" on CP-05? | A result of a visit registered in the selected day that is not verified yet (Ordered, Complete or Reviewed). |
| Q2 | Is the unverified backlog limited to the day? | No: every result with a value waiting for verification of visits registered up to the end of the selected day (a backlog). |
| Q3 | Which threshold makes a test "delayed"? | `delayLatePercent` (default 100 = past the expected time). `delayWarnPercent` colours the worklist (Results) and is not counted. |
| Q4 | When does the turnaround clock start? | At the tube's collection when collected, else at registration (BR-13; the start Results uses). A result without a tube starts at registration. |
| Q5 | Delayed as of when for a past day? | As of the end of that day (`min(now, day end)`), with today's statuses (history of statuses is not reconstructed). |
| Q6 | Are delayed tests limited to the day's registrations? | No: every open result of the branch registered up to the end of the day. |
| Q7 | What counts as a "test" on SA-03? | A result (one per test per visit) that is not cancelled; before Results is migrated tests are 0. |
| Q8 | What is a "served patient"? | A distinct patient file with at least one visit registered in the period, per client; the total is the sum over clients (files are per laboratory). |
| Q9 | Which period boundaries does SA-03 use? | The platform module's (UTC calendar day, ISO week, month, year), unchanged. |
| Q10 | May the platform read the clients' rows for these figures? | Only counts, through the SECURITY DEFINER function of the platform tenant (pattern of `saas.client_usage_counts`, PLT intake Q8); never by elevation in C#. |
| Q11 | Claims and financial reports | Release 2 (decision of 2026-10-10); this intake will be extended (CP-31, CP-35, WF-10, BR-13 compliance report) when Release 2 starts. |
| Q12 | Is the discount already netted in the shares the work amount report sums? | **Yes.** Registration (intake LIS-INT-REG-001 §5, "Discount"): the visit discount is allocated to the top-level lines and `line.PatientShare = gross patient share − line.Discount`; the payer share is never discounted. So cash income is the patient's part after discount, credit income the payer's part, and gross − discount = cash + credit. The discount column is informative. |
| Q13 | "Income of the accessions registered today" — booked or collected? | **Booked** income: the shares of the services registered in the period, whatever has been paid (payments are the Billing collections report, item 2). A Reserved service is billed (BR-05) and counted; a cancelled service is not, and a visit whose services were all cancelled contributes nothing ("without cancelled visits"). |
| Q14 | How is a package counted? | Once: the package line carries the amounts; its member lines carry none and are not read. Under the clinical-group grouping the package falls in its own service's clinical group (or "Not grouped"). |
| Q15 | Which day is "today", and which day does a visit belong to? | The laboratory's calendar (`ILaboratoryTimeZone`: client record country, else the tenant parameter `timeZone`, else Africa/Cairo); a visit belongs to the laboratory day of its `registered_at_utc`. The page proposes the browser's today; the server echoes the laboratory's period and the page follows it. |
| Q16 | Who may report on another branch? | The session branch needs nothing. Another branch: a member holding `organization.view`, or whose membership is assigned to that branch (no assignment = every branch, as the CP-04 branch list reads it). The page offers the picker only when more than one branch is selectable. |
| Q17 | Does the days limit apply to the whole period or to its start? | To its start: `from ≥ today − limit`. A limit of 0 means today only; empty means no limit. The end is capped by nothing but the 366-day span (REP-002). The same right bounds the Billing collections report (item 2). |
| Q18 | Which currency? | The amounts are summed as stored (one currency per laboratory, `defaultCurrency`); the report shows no currency symbol. A multi-currency laboratory is out of scope. |

Integration dependencies:

| Port | Owner | This module's adapter | Replaces |
| --- | --- | --- | --- |
| `IClientActivityReader` (`Platform.Application.PlatformAdmin`) | platform | `Persistence/Reporting/ClientActivityReader` | `ZeroClientActivityReader` (`[FallbackAdapter]`, may be deleted by the integrator) |
| `IBranchDashboardReader` (`Platform.Application.Account`) | account | the existing `Persistence/Account/BranchDashboardReader`, three statements added | — |
| `app.results` (MODULES.md §5) | results | read by raw SQL on the agreed columns | — |
| `Visit`, `VisitLine` entity types over `app.visits`, `app.visit_lines` (MODULES.md §5; an earlier wave) | registration | `Persistence/Reporting/WorkAmountReader` (`IWorkAmountReader`) | — |
| `IBranchLookup`, `IUserDirectory` (Organization), `IPayerLookup` (Pricing), `IDoctorLookup` (Catalogue B), `ICatalogueLookup` (Catalogue A) | owning modules | `Persistence/Reporting/WorkAmountNames` (`IWorkAmountNames`) | — |
| `saas.tenants` names, `Branch` (Organization) | platform, organization | `Persistence/Reporting/ReportLetterhead` (`IReportLetterhead`) | — |
| `ILaboratoryTimeZone` (Patients), `IUserPrivileges.SpecialRights.ReportDaysLimit`, `ISessionPrincipalQuery` (branch assignment) | patients, security edge | consumed by `WorkAmountReportComposer` | — |
