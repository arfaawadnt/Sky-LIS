# Sky LIS — module map and build waves (LIS-BM-001)

This is the decomposition of the Business SRS (LIS-SRS-001: client pages CP-01…CP-68, admin pages SA-01…SA-21) into
bounded contexts, each built as one `Platform.*` module following `docs/build/MODULE-PATTERN.md`. Waves order the
work by dependency; modules inside a wave are built in parallel in separate build slots and test databases.

## 1. Fixed decisions for every module

| Topic | Decision |
| --- | --- |
| Platform tenant | The Super Admin portal is the tenant `TenantIds.Platform` (`00000000-0000-0000-0000-000000000001`). National Technology staff hold memberships and roles in it; the **global master data** (SA-12/13/14) is the catalogue of this tenant; creating a client copies those rows to the new tenant (BR-25). RLS and the query filter work unchanged. |
| Two portals, one SPA | `frontend/` serves both portals. The shell shows the admin navigation when `auth.currentTenant() === PLATFORM_TENANT_ID`, the client navigation otherwise. Admin pages SA-12/13/14 and SA-06/07 **reuse** the client feature components (catalogue, roles, users) under the platform tenant. |
| Branch of the session | `POST /api/v1/auth/select-branch` stores the branch on the refresh session (CP-04); `ICurrentUser.BranchId` exposes it; every branch-stamped aggregate takes `BranchId` from it. |
| Cross-module reads | A module that needs another module's data declares a **read port** in its own Application folder (`I<Thing>Lookup`) and the adapter in `Platform.Infrastructure/Persistence/<Module>/` queries the other module's tables directly (never its repositories or aggregates). Registered by convention. |
| Cross-module writes | Only through domain events and policies (`IDomainEventHandler<TEvent>` in `Platform.Application/<Module>/Policies/`, dispatched by the outbox processor; idempotent on a natural key). Never a command sent to another module. |
| Documents | Each module exposes the **data** of its documents (`GET …/documents/<kind>` returning a typed DTO). Rendering (print CSS, Code 128, QR, PDF via the browser print dialog) is the SPA's `shared/print` kit. |
| Reference numbers | `IReferenceNumbers.NextAsync(kind, prefix, now)` → `PFX-yyyy-nnnnn` per tenant and year. Kinds: `patient` (PAT), `accession` (ACC), `sample` (SMP), `receipt` (RCT), `refund` (RFD), `booking` (BKG), `batch` (BTH), `claim` (CLM), `closing` (CLS), `ticket` (TKT), `print-list` (PRL). Tenant-configurable formats are a later change (OQ-07). |
| Tenant parameters | `ITenantSettings` (foundation) reads `app.tenant_settings` key/value with code defaults; CP-57 edits them. Keys are constants in `Platform.Domain.Settings.SettingKeys`. |
| Audit | `AuditStampInterceptor` stamps actors; `FieldChangeInterceptor` (foundation) writes `audit.field_changes` for every tracked aggregate; domain events go to the outbox and to `audit.audit_trail`. Modules add nothing for auditing except reasons on transitions. |
| Release scope | Decided by the user on 2026-10-10: deliver **Release 1 only**, exactly as the R1 prototype builds define it (prototypes/dist/Sky-LIS-Client-Portal-Prototype-v2.1-Release1.html and Sky-LIS-Super-Admin-Portal-Prototype-v3.1-Release1.html; rules in prototypes/src/app.client.1.js / app.client.4.js `R1` and app.admin.js). Client R1 menu: dashboard; registration, search registrations, patients, payments, price query, worklist, results by accession; setup (roles, users, branches, clinical groups, services, tests, service sequence, sample types, units, drug types, antibiotics, organisms, preparations, comment library, doctors, treasuries, treasury requests, payers, base price lists); profile; plus public site, subscription, login/recovery, branch selection. R1 rules: no Location/Room on registration, payment captured inside registration, samples auto-collected/received on save (no sample pages), cancel services and refund as actions. Admin R1: everything except the integration library. Not built for R1: auto-verification, claims, financial reports, work queue, QC, print reports, results by sample, home/batch/lab-to-lab registration, money transfer, cancellations page. Already-built R2 parts (analyser integration, sample pages, refunds/batch payment/cash closing pages, contract pricing, wallet/support/audit pages, catalogue-depth lists in the client) stay in the code but are hidden: `RELEASE_SCOPE = 'R1'` in shell/nav.registry.ts. |
| Password policy | Decided by the user on 2026-10-09: the pack's policy (02 §6.1) applies everywhere (subscription wizard, users, recovery, resets): minimum length from settings (default 8), maximum 128, no composition rules, must not contain the username. The SRS composition rules (upper, lower, digit, special) are superseded. |
| Releases | Implement R1 and R2 pages alike; mark release in the nav entry (`release: 'R1' \| 'R2'`). |

## 2. Modules

| Slot | Module (`PermissionCatalog`) | SRS pages | Aggregates / main tables | Wave |
| --- | --- | --- | --- | --- |
| `ui` | Shell + UI kit (frontend only) | all | `shared/ui`, `shared/forms`, `shared/print`, `shell/` | 0 |
| `found` | Foundation (`platform`, `identity`, `audit`, `notifications`, `settings`) | CP-03/04, CP-68, SA-08/09 (backend) | branch selection, `identity.otp_challenges` + `INotificationSender`, `audit.field_changes`, `audit.audit_trail`, outbox processor + policies, `app.tenant_settings` + `ITenantSettings`, platform tenant seeding | 0 |
| `cat` | Catalogue A (`catalogue`) | CP-39, CP-40, CP-41, CP-42, CP-43, CP-44, CP-45, CP-55 | ClinicalGroup, SampleType (+conditions), Unit, PatientCondition, Service (+members), Test (+range bands, list values, formula, paragraphs), AdditionalService, service sequence | 1 |
| `cat2` | Catalogue B (`catalogue`) | CP-46, CP-47, CP-48, CP-49, CP-50, CP-51, CP-52, CP-53, CP-54, CP-56, SA-14 | Preparation, CommentTemplate, Doctor, Diagnosis, SendingLab, ShortcutSet, TestRule, Reason, ReportTemplate, DrugType, CommercialName, Antibiotic, Organism, SensitivityValue | 1 |
| `org` | Organization (`organization`, `identity`, `settings`) | CP-36, CP-37, CP-38, CP-57, CP-65 (credentials), CP-04 (page) | Branch, Role (extend: special rights, result rights per group × branch, screen rights = permission keys), tenant users (PlatformUser + TenantMembership: create, lock, reset, overrides, signature), Parameters page, branch-selection page | 1 |
| `plat` | Platform administration (`tenancy`, `platform`, `notifications`, `integration` library) | SA-03, SA-04, SA-05, SA-10, SA-11, SA-15…SA-18, SA-19, SA-20, CP-02 (backend), CP-03 recovery (backend) | Tenant (client: `identity.tenants`), Plan, Wallet (+transactions), Country, Governorate, PlatformParameter, NotificationTemplate, AnalyserModel, PcrKitTemplate, CodeSystem, RuleTemplate, subscription wizard + OTP, admin dashboard | 1 |
| `fin` | Pricing (`pricing`) | CP-32, CP-33, CP-34, CP-12 (price API) | Payer (+contract tiers), BasePriceList (+price lines), ContractPricing (+derived prices, exclusions, alerts), `IPriceLookup` read port for registration | 1 |
| `pat` | Patients (`patients`, `files`) | CP-11 | Patient (number PAT-, demographics, condition, VIP), PatientDocument (files under `Lis:Files:Root`), merge, similar-name search (BR-01) | 1 |
| `reg` | Registration (`registration`) | CP-06, CP-07, CP-08, CP-09, CP-10, CP-12 (page), CP-25, CP-26 | Visit (accession, lines Ordered/Reserved/Cancelled, discount, preparation answers, tubes by BR-02, balance fields), Booking, Batch, transfers, cancellations; raises `VisitRegistered`, `VisitLineCancelled`, `VisitTransferred` | 2 |
| `smp` | Samples (`samples`) | CP-14, CP-15, CP-16, CP-17 | Sample (status Ordered/Collected/Received/Rejected, events, replacement with new barcode BR-18), created by the policy on `VisitRegistered`, auto-collect/receive from settings | 2 |
| `bill` | Billing (`billing`, `treasury`) | CP-23, CP-24, CP-27, CP-30 | Payment (visit, amount, method, reference, treasury, device), Refund, CashClosing, batch payment; `IVisitBalanceLookup` read port | 2 |
| `acc` | Account & onboarding (frontend + `support`, `wallet`) | CP-01, CP-02, CP-03, CP-04, CP-05, CP-65, CP-66, CP-67, CP-68 | public site, subscription wizard UI, recovery UI, branch selection UI, dashboard, profile, wallet view, SupportTicket, audit log UI | 2 |
| `res` | Results (`results`, `quality`) | CP-13, CP-18, CP-19, CP-20, CP-21, CP-22 | Result (per test per visit: value, flags, status Ordered/Complete/Reviewed/Verified/Printed, logs, comments, delta BR-11, ranges BR-08), ReportUnit projection, PrintList, worklist, work queue, QC placeholder; created by the policy on `VisitRegistered` | 3 |
| `rep` | Reporting (`reporting`, `claims`, `dashboard`) | CP-31, CP-35, CP-05 data, SA-03 data | Claim (Draft/Issued), the sixteen financial reports, KPI queries | 3 |
| `int` | Integration (`integration`) | CP-58, CP-59, CP-60, CP-61, CP-62 | Analyser, AnalyserTestMapping, AnalyserSampleCode, SiteAgent (+logs), PcrKit, result intake endpoint for agents | 3 |
| `aut` | Automation (`automation`) | CP-63, CP-64 | VerificationSettings per group, MedicalRule, simulation, release policy on result events | 3 |

## 3. Event contracts shared across waves

Defined before wave 2 so parallel modules agree (fields may be added, never removed or renamed):

The records live in `src/Platform.Domain/Registration/RegistrationEvents.cs` (already created): `VisitRegistered` (VisitId, Accession, BranchId, PatientId, PayerId?, ContractTierId?, Urgent, RegisteredByUserId, Tubes[RegisteredTube], Lines[RegisteredLine], OccurredAtUtc), `VisitLineCancelled` (VisitId, Accession, LineId, ServiceId, TubeId?, ActorUserId, Reason), `VisitTransferred` (VisitId, Accession, from/to payer and tier, ActorUserId). `VisitLinesAdded` (VisitId, Accession, BranchId, PatientId, ActorUserId, Tubes, Lines): lines added to a saved visit or Reserved lines released to Ordered; Samples and Results handle it exactly like `VisitRegistered`. The outbox stamps the tenant.

Samples reacts to `VisitRegistered` (one Sample per tube, idempotent on `TubeId`) and to `VisitLineCancelled`
(cancel the tube when no live line remains). Results reacts to `VisitRegistered` (one Result per test of every
Ordered line, idempotent on `(LineId, TestId)`) and to `VisitLineCancelled`. Billing reacts to nothing at
registration: the SPA saves the visit and then records the captured payment through the Billing API (BR-26).

Integration raises `AnalyserResultReceived` (AnalyserId, AnalyserCode, SampleNumber, TestId, Value, Flags, Unit, StopFlags, MeasuredAtUtc) for every accepted analyser or PCR result; the Results module (wave 3) lands it as a Complete result with the analyser as source (WF-07), refusing samples that are not Received (BR-03), and the Automation module evaluates the stop flags. Site agents authenticate as machine credentials (02 §6.3: client id + secret exchanged at `POST /api/v1/auth/token`), each owned by a service user whose role holds the credential's scopes.

## 4. Build waves

| Wave | Slots | Gate to the next wave |
| --- | --- | --- |
| 0 | `ui`, `found` | shell + kit merged; foundation migration generated; all suites green |
| 1 | `cat`, `cat2`, `org`, `plat`, `fin`, `pat` | wave migration generated; DB tests of each module green; routes and nav merged |
| 2 | `reg`, `smp`, `bill`, `acc` | same |
| 3 | `res`, `rep`, `int`, `aut` | same; browser walk-through of WF-02, WF-06, WF-09 |

Per wave: agents write code + unit/architecture tests + frontend and report; the integrator generates **one**
migration for the wave (`dotnet ef migrations add Wave<N>_<Modules>` + `node scripts/add-rls-to-migration.mjs`),
regenerates `deploy/postgres/migrations.sql`, registers routes and nav entries, then asks each agent to run its
database tests in its slot and fix what fails.

## 5. Wave 2 table contracts (same-wave raw-SQL reads)

Modules of wave 2 read each other's tables only through these agreed names and columns (snake_case, schema `app`,
every table keyed `(tenant_id, id)` with FORCE RLS). Owners may add columns, never rename or drop these.

| Owner | Table | Columns other modules may read |
| --- | --- | --- |
| Registration | `app.visits` | `id`, `tenant_id`, `accession`, `patient_id`, `branch_id`, `payer_id`, `contract_tier_id`, `batch_id`, `status` ('Open' \| 'Completed'), `registered_at_utc`, `registered_by_user_id`, `urgent`, `total_list_price`, `discount_total`, `patient_share_total`, `payer_share_total` (net of discount, Ordered + Reserved lines only; Cancelled lines excluded) |
| Registration | `app.visit_lines` | `id`, `tenant_id`, `visit_id`, `service_id`, `tube_id`, `status` ('Ordered' \| 'Reserved' \| 'Cancelled'), `list_price`, `patient_share`, `payer_share`, `discount`, `expected_result_at_utc` |
| Registration | `app.batches` | `id`, `tenant_id`, `number`, `kind` ('Group' \| 'LabToLab'), `payer_id`, `contract_tier_id`, `sending_lab_id` |
| Samples | `app.samples` | `id`, `tenant_id`, `tube_id`, `sample_number`, `visit_id`, `clinical_group_id`, `sample_type_id`, `status` ('Ordered' \| 'Collected' \| 'Received' \| 'Rejected' \| 'Cancelled' \| 'Replaced'), `collected_at_utc`, `received_at_utc` |
| Billing | `app.payments` | `id`, `tenant_id`, `visit_id`, `receipt_number`, `amount`, `method` ('Cash' \| 'Card' \| 'Pos' \| 'Transfer'), `status` ('Posted' \| 'Voided'), `paid_at_utc`, `treasury_id`, `cashier_user_id` |
| Billing | `app.refunds` | `id`, `tenant_id`, `visit_id`, `number`, `amount`, `method`, `refunded_at_utc`, `treasury_id` |
| Results | `app.results` | `id`, `tenant_id`, `visit_id`, `accession`, `line_id`, `service_id`, `test_id`, `tube_id`, `clinical_group_id`, `patient_id`, `branch_id`, `report_unit_key`, `status` ('Ordered' | 'Complete' | 'Reviewed' | 'Verified' | 'Printed' | 'Cancelled'), `numeric_value`, `text_value`, `flag` ('Normal' | 'Low' | 'High' | 'CriticalLow' | 'CriticalHigh' | 'Abnormal' | null), `is_panic`, `delta_flag`, `source` ('Manual' | 'Analyser'), `analyser_id`, `entered_at_utc`, `reviewed_at_utc`, `verified_at_utc`, `verified_by_user_id`, `printed_at_utc`, `expected_at_utc` |

Balance (BR-14, single definition, owned by Billing): `remaining = visits.patient_share_total − Σ posted payments + Σ refunds`;
negative remaining = refund due. Registration shows it through `IVisitBalanceLookup` (namespace
`Platform.Application.Registration`), which Billing declares and implements; Registration only consumes it.

Results do not exist before wave 3: Registration declares `IResultStatusLookup.HasVerifiedResultsAsync(lineIds)` with a
NoOp adapter marked `[FallbackAdapter]` (nothing verified); the Results agent ships the real adapter and deletes the fallback.

## 6. Progress

| Date | Wave | Status |
| --- | --- | --- |
| 2026-10-09 | 0 `found` | Done: migration `Foundation` (branch on session, audit ledger, outbox processor, OTP recovery, tenant settings); suites 77/61/14/22/21 in the default slot. Intake LIS-INT-FND-001. |
| 2026-10-09 | 0 `ui` | Done: UI kit (`shared/ui`, `shared/forms`, `shared/print`, README), two-portal shell, treasury pages on the kit; 68 frontend tests. Browser check: client and admin portals, Arabic RTL, phone cards (fixed: card action cell width, top-bar overlap under 420 px). Open: the language switch is not saved to the user profile (for the `acc` profile page, CP-65); no Excel export in the table yet. |
| 2026-10-09 | 2–3 (early) | Started ahead of the wave-1 gate against documented contracts: smp, bill, then acc (account and onboarding, public site at /welcome) and int (analyser integration + machine credentials). Route stubs registered for registration, samples, billing, account (client) and /welcome (public). |
| 2026-10-09 | 1 | Started: cat, cat2, org, plat, fin, pat. Route stubs registered in `app.routes.ts` (client /catalogue, /organization, /pricing, /patients; admin /admin/catalogue, /admin/organization, /admin/*). |
| 2026-10-09 | 1 `pat` | Done (agent report): CP-11 register + patient file + picker; tables app.patients, app.patient_documents; IPatientLookup, IFileStore/LocalFileStore; Domain/Architecture/Application and frontend specs green; DB suites written, waiting for the wave-1 migration. Integrator to-do: move the patient picker to shared/, add Merged to STATUS_TONES, policies on PatientsMerged in later waves, check the 204-null mutation() issue in the treasury page. |
| 2026-10-10 | 1 `cat2` | Done (agent report): CP-46…CP-54, CP-56, SA-14; 14 tables; IOrderRulesLookup, IReportTemplateLookup, IDoctorLookup, ICatalogueBCloner (run after Catalogue A's cloner, elevated). Five lists are hand-written pages because the kit has no multi-select field. |
| 2026-10-10 | 1 `org` | Done (agent report): CP-36…CP-38, CP-57, SA-06/07; app.branches, app.role_result_rights, role/membership/user extensions; IBranchLookup, IUserDirectory, IUserPrivileges (special and result rights), provisioning ports implemented. Integrator applied: privileges middleware now passes the whole principal; credential-field map accepts patterns (user create and reset); seeder marks the primary administrator; sign-in refuses an expired account (AUTH-014); profile carries mustChangePassword. |
| 2026-10-10 | 1 `cat` | Done (agent report): CP-39…CP-45, CP-55 (+ SA-12); app.clinical_groups, sample_types (+ sample_conditions), measurement_units, patient_conditions, services (+ service_members), tests (+ range bands, list values, paragraphs); ICatalogueLookup with ResolveRangeAsync, IFormulaEvaluator, ICatalogueACloner (wired by the platform MasterDataCloner before Catalogue B). 16 nav entries registered. Pending: the platform module's TenantProvisioning.cs elevation must be allow-listed (architecture gate). |
| 2026-10-10 | 1 `fin` | Done (agent report): CP-32…CP-34 + quote API; IPriceLookup, IPayerLookup; lazy cash payer. |
| 2026-10-10 | 2 `smp` | Done (agent report): CP-14…CP-17; app.samples (+ generation), app.sample_events; three policies; ISampleStatusLookup. |
| 2026-10-10 | 2 `reg` | Done: backend (visits, lines, tubes, preparation answers, transfers, bookings, batches; `VisitsController`) and the Release-1 frontend: CP-06 `/registration/new` (walk-in, payment captured on the page through Billing, BR-26), edit `/registration/visits/:visitId`, CP-10 `/registration/visits` (samples / payments / documents / results / cancel services, CSV), job order `/registration/visits/:visitId/job-order`, CP-12 price query `/registration/price-query`; dictionaries 347 keys; 53 specs. CP-07/08/09/25/26 are Release 2 (backend only). The page provisions the cash payer lazily (`POST pricing/payers/default`) so a new laboratory can register at once. |
| 2026-10-10 | 3 `res` | Done: backend (`app.results`, `app.result_log_entries` with `sequence`, print lists, QC; `ResultsController`) and the Release-1 frontend: CP-18 worklist `/results/worklist` (filters, BR-13 colouring, CSV, bench list print) and CP-19 `/results/by-accession?accession=` (entry per test type, flags, panic, delta acknowledgement, review, verify, undo, amend, comments, notes, history, preview, print); dictionaries 297 keys; 22 specs. CP-20/21/22, analyser landing UI and auto-verification are Release 2. Other pages link with `RESULTS_LINKS.accession(acc)`. |
| 2026-10-10 | nav | 36 entries registered for patients, samples, pricing, organization and catalogue B (client and admin); treasury 204 fix; Merged status tone. |

Integrator to-do list closed 2026-10-10: `Merged` tone, treasury 204 handling, countries/governorates from the public lookups (every form), job-order route, nav entries of every module, CLAUDE.md §5 summaries, kit `multiSelect` field, patient picker moved to `shared/patient` (one copy, used by Patients and Registration), Registration's `[FallbackAdapter]` for `IResultStatusLookup` deleted (Results ships the real adapter), the top-bar language switch saves the preference (CP-65) and sign-in / start-up open in the saved language. Dropped: a filtered-export hook in MasterDataConfig (no Release-1 page needs it; the pages that export do it themselves).

### Integration log (2026-10-10)

- Migration `Modules` generated from the whole tree (71 tables, RLS on every tenant table, eight module SQL files). From now on model changes get **incremental** migrations (`ModuleFixes<n>`), so slot databases never need a reset; regenerate `deploy/postgres/migrations.sql` after each. `ModuleFixes1`: sample event sequence (timeline order) + platform wallet child keys.
- Script fix: `add-module-sql-to-migration.mjs` and `add-rls-to-migration.mjs` insert text literally (a `String.replace` turned `$$` into `$`). `new-slot-database.sh <slot> --reset` drops and recreates a slot database.
- Cross-cutting fixes: signing keys get their own crypto provider cache (shared cache across hosts signed with disposed keys: random 500s on sign-in); credential envelopes are consumed once per request (several sealed fields per request); inactive laboratories cannot sign in (ITenantStatusLookup in SessionPrincipalQuery and MembershipDirectory); TenantProvisioning.cs allow-listed for elevation (one transaction + BR-25 catalogue copy).
- First full database run (integrator slot): integration 68/91, functional 56/70 → 63/70 after the signing fix. Remaining failures dispatched to their owning agents; billing's visit-dependent tests wait for Registration.
- Frontend: account guards (`branchGuard`, `passwordChangeGuard`) on the shell route; account admin routes under /admin; nav entries for billing, account, platform (+ dashboard as client home); status tones InProgress, Resolved, Posted, Voided, RefundDue, Merged.

### Integration log (2026-10-10, afternoon)

- Full run in the integrator slot: Domain 714, Architecture 85, Application 213, Integration 108, Functional 77; frontend
  297 specs, lint and boundaries clean, production build green.
- Fixed: the `Registration` migration's Down step now drops `fk_visit_accounts_visits` (added by its Up SQL) before
  `app.visits`, so the round trip reverts; `ReportingModuleTests` S-D3 writes its rows in its own month (S-A3 counts the
  whole platform and met them when xunit ran S-D3 first).
- New gate: `MigrationScriptTests.The_deploy_script_records_every_migration` (the committed idempotent script had missed
  `ResultLogSequence` without any test noticing).
- Dev database `lis` migrated to `ResultLogSequence` (it was still at `Foundation`); `seed-demo-lab` added to DevTools and
  run (tenant, branch MAIN, users lab.admin / lab.admin.manager, catalogue K/NA/GLU/HGB, cash price list, treasury
  MAIN-CASH); treasury access of the browser device requested and approved through the UI (TRQ-2026-00001).
- The `backend-api` launch configuration runs with `--no-build`: the API started from binaries of the previous day and its
  outbox could not resolve the wave-1 event types. Build `src/Platform.WebApi` (no slot) before starting it.
- Dashboard (CP-05) trimmed to the R1 prototype: the sample tiles and the Samples quick action are R2; quick actions now
  registration, search registrations, results by accession, worklist, payments, price query. The release switch moved to
  `core/release.ts` (re-exported by `shell/nav.registry.ts`) so features can gate their own items.
- Registration (`reg`) and Results (`res`) frontends finished by module agents (rows above); nav entries registered
  (reception: registration, search registrations, price query; laboratory: worklist, results by accession).
- Browser walk-through in the dev database (lab.admin): WF-02 new patient → K + GLU on the cash contract → 210 EGP cash
  on MAIN-CASH → saved as ACC-2026-00001 (visit 201, payment 201, labels fetched and sent to print) → listed in search
  registrations as Settled / Open; WF-06 results created by the outbox policies with the sample received → values entered
  (K 6.8 flagged PANIC HH) → saved → reviewed → verified → report preview; worklist lists both tests. Arabic RTL checked
  on the registration page. Remaining: the native print dialog blocks the automated browser, so printing was not clicked.
- Fixed on the way: the patient and branch forms read `/api/v1/platform/countries|governorates` (403 for a laboratory);
  they now read the anonymous `/api/v1/public/*` lists and `ListPublicGovernoratesQuery` accepts no country (every
  active governorate).

### Wave 4 — Release-1 integration and go-live readiness (2026-10-10)

Every Release-1 module is built; Release 2 (int, aut, QC, claims, financial reports) stays parked. Wave 4 is the build
plan's P6 for Release 1: the remaining workflow walk-throughs, the defects they surface, and the deployment package.

- WF-09 walked through in the dev database (lab.admin): treasury access (TRQ-2026-00001), full payment at registration
  (RCT-2026-00001), partial payment at registration (100 of 150, RCT-2026-00002) then the remainder on the Payments
  page (RCT-2026-00003), cancellation of the service with a free-text reason (balance −150, status "Refund due"), refund
  of the balance with a catalogue reason from the Payments page's "Refund the balance" action (refund receipt). Cash
  closing (CP-30) is Release 2.
- WF-12 walked through (platform.admin): SA-03 dashboard (served patients, tests, registrations per client), SA-04 Add
  client → "Walk-through Laboratory" provisioned with its branch and administrator (wt.admin), SA-05, SA-19, SA-20 render.
- Fixed: a sign-in honoured a return URL of the other portal (a platform administrator signed in after a laboratory user
  signed out and landed on the client dashboard, 403); each portal now returns only to its own routes. The platform
  administrator seeder resets the password of an existing user (it only re-asserted the role before). DevTools
  `seed-reasons` (cancellation, refund, discount, rejection reasons; also part of `seed-demo-lab`) because the money
  flows need catalogue reasons that a hand-seeded laboratory lacked.
- Deployment package (scripts under `deploy/windows`, runbook under `docs/deploy`, gate `DeploymentPackageTests`) built by
  the packaging agent; see its row below when merged.
- Backup and restore drill done on the private dev instance with the package scripts (dump verified, restored into
  `lis_drill`); found and fixed: `restore-database.ps1` prompted for confirmation on every real run (ConfirmImpact High),
  which a non-interactive host cannot answer. UAT scripts per persona written: `docs/uat/Release-1_UAT_Scripts.md` (78 scripts, 45 critical, every R1 page covered;
  three scripts need the test lane's notification gateway).
- Still open for go-live (plan P6): load test, penetration test, backup and recovery drill on a real host, data migration
  from LDM (needs source data), UAT scripts per persona, pilot.

## 7. Auto-verification contract (Results ↔ Automation)

Results declares, in `Platform.Application.Results`, the port the Automation module implements (Results ships a
`[FallbackAdapter]` that always holds with reason "auto-verification not configured"):

```csharp
public interface IAutoVerificationEvaluator
{
    Task<AutoVerificationDecision> EvaluateAsync(AutoVerificationInput input, CancellationToken cancellationToken);
}

public sealed record AutoVerificationInput(
    Guid ResultId, Guid VisitId, string ReportUnitKey, Guid TestId, Guid ClinicalGroupId, Guid BranchId,
    Guid PatientId, string Sex, int AgeDays, Guid? PatientConditionId,
    decimal? NumericValue, string? TextValue, string? Flag, bool IsPanic, bool DeltaFlag,
    decimal? RangeLow, decimal? RangeHigh, decimal? ReportableLow, decimal? ReportableHigh,
    string Source, Guid? AnalyserId, IReadOnlyList<string> AnalyserFlags, IReadOnlyList<string> StopFlags,
    decimal? PreviousNumericValue, DateTimeOffset? PreviousVerifiedAtUtc,
    bool HasComment, bool AllUnitResultsComplete);

public sealed record AutoVerificationDecision(bool Release, IReadOnlyList<string> HoldReasons, string? Recommendation);
```

Results calls it whenever a result becomes Complete (manual save or analyser landing). On `Release` it verifies the
result as the system actor "Auto-verification" (flag `auto_verified`, the decision recorded in the result log); on hold
it keeps the result Complete and records the hold reasons (shown on the result and in the work queue). Rights and
ReviewBeforeVerify do not apply to the system actor; everything else (value present, Received gate, delta) does.
- Database tests green in their own slots: cat 6+2, cat2 7+7, org 8+6, plat 9+5, fin 6+2, smp 5+3, bill 9+2, found outbox 2 (full integration suite 90/91 in slot found, the remaining failure since fixed by org), int 5 + 9 (machine credentials + integration flows). Test fixtures now ignore PendingModelChangesWarning when migrating (MigrationRoundTripTests stays the gate).
- int done: machine credentials (02 §6.3: `identity.machine_credentials`, `POST /api/v1/auth/token` with an enveloped client secret, refresh in the body, service user per credential), analysers, mappings, sample codes, site agents, PCR kits, agent API; `AnalyserResultReceived` carries IntakeId (idempotency) and overwrite switches. 5 nav entries.
- Started wave 3: `rep` (claims, sixteen financial reports, dashboard result KPIs, SA-03 activity) and `aut` (verification settings, medical rules, simulation, the §7 evaluator). Results asked to add the §7 hook.
