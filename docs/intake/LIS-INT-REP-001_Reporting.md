# LIS-INT-REP-001 — Reporting: dashboard result figures and platform activity (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `reporting` / `dashboard` (`PermissionCatalog.Reporting`, `PermissionCatalog.Dashboard`), build slot `rep`, wave 3 (MODULES.md §2) |
| Release scope | **Release 1 only** (decided by the user on 2026-10-10, scope of the R1 prototype builds `*-v2.1-Release1.html` / `*-v3.1-Release1.html`): the result KPIs of the branch dashboard (CP-05) and the client activity of the Super Admin dashboard (SA-03). Claims (CP-31), the sixteen financial reports (CP-35) and the claim/report pages of WF-10 are **Release 2** and are not built (§2). |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), MODULES.md (§1, §5) and the LDM synthesis. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.0 — 2026-10-10 |
| Implemented in | `src/Platform.Infrastructure/Persistence/Reporting/` (`ClientActivityReader.cs`, `Reporting.migration.sql`); the Account module's dashboard reader `src/Platform.Infrastructure/Persistence/Account/BranchDashboardReader.cs` (result figures only), `src/Platform.Application/Account/IAccountPorts.cs` (`BranchDayFigures`, three optional figures) and `src/Platform.Application/Account/Queries/DashboardQueries.cs` (pass-through), by agreement with the integrator |

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

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `dashboard.view` | CP-05 (Account module's `GET /api/v1/dashboard/branch`): the three figures travel in the existing response |
| `platform.view` | SA-03 (platform module's `GET /api/v1/platform/dashboard`), platform tenant only (PLT-403 otherwise) |
| `reporting.view`, `reporting.export`, `claims.*` | catalogued, **unused in Release 1** (Release 2 pages) |

No new request, endpoint or permission: the figures are computed by adapters behind ports the Account and platform
modules already declare. Deny by default stays with those modules' requests.

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

No new setting key.

## 14. Registers, reports and exports

| Figure | Definition (SQL) | Where |
| --- | --- | --- |
| Pending results (CP-05) | `count(*)` of `app.results r JOIN app.visits v` with `r.branch_id = @branch`, `r.status IN ('Ordered','Complete','Reviewed')`, `v.registered_at_utc ∈ [day start, day end)` | `BranchDashboardReader.PendingResultsSql` |
| Unverified backlog (CP-05) | `r.status IN ('Complete','Reviewed')`, `v.registered_at_utc < day end` | `BranchDashboardReader.UnverifiedBacklogSql` |
| Delayed tests (CP-05) | open results with `expected_at_utc` and `as_of ≥ start + (expected − start) × delayLatePercent / 100`, where `start = coalesce(latest live collection of the tube, registration)` and `as_of = min(now, day end)` | `BranchDashboardReader.DelayedTestsSql` |
| Served patients, tests, registrations (SA-03) | per tenant: `count(DISTINCT v.patient_id)`, `Σ results of the visit with status ≠ 'Cancelled'`, `count(*)` of visits with `registered_at_utc ∈ [from, to)` | `saas.client_activity_counts(timestamptz, timestamptz)` → `ClientActivityReader` |
| Registrations by client (SA-03) | the per-tenant `registrations`, clients without registrations omitted, highest first (the platform handler keeps the top 10 and joins names) | `ClientActivityReader.Snapshot` |

No export in Release 1.

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
```

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

Integration dependencies:

| Port | Owner | This module's adapter | Replaces |
| --- | --- | --- | --- |
| `IClientActivityReader` (`Platform.Application.PlatformAdmin`) | platform | `Persistence/Reporting/ClientActivityReader` | `ZeroClientActivityReader` (`[FallbackAdapter]`, may be deleted by the integrator) |
| `IBranchDashboardReader` (`Platform.Application.Account`) | account | the existing `Persistence/Account/BranchDashboardReader`, three statements added | — |
| `app.results` (MODULES.md §5) | results | read by raw SQL on the agreed columns | — |
