# LIS-INT-PLT-001 — Platform administration (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Slot / modules | `plat` — permission modules `tenancy` (clients, plans, wallets), `platform` (dashboard, sessions), `notifications` (templates), `integration` (library), `wallet` (tenant-facing wallet), `settings` (SA-19 over the foundation's settings API) |
| SRS pages | SA-03 Dashboard, SA-04 Clients, SA-05 Subscriptions & wallets, SA-08 Sessions, SA-10 Countries, SA-11 Governorates, SA-15 Analyser models, SA-16 PCR kits, SA-17 Code systems, SA-18 Rule templates, SA-19 Platform parameters, SA-20 Notification templates; CP-02 Subscription wizard (backend only), CP-66 Wallet (API only); workflows WF-01 (onboarding), WF-12 (platform operations); rule BR-25 |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001) and the architecture pack. **For review by the business owner**; assumptions in §16. |
| Version | 1.4 — 2026-10-24 (§19 change request LIS-CR-007 A-01: silent clients on SA-03 and SA-04) · 1.3 — 2026-10-20 (§18 change request LIS-CR-005: portal base URL `Lis:Platform:PortalBaseUrl`, addresses `{base}/lab/{slug}`, public laboratory card and logo, `PLT-404`) · 1.2 — 2026-10-19 (change request LIS-CR-003: P-A1 client login logs, SA-08b; P-A2 location hierarchy under a governorate, SA-11b, §17) |
| Implemented in | `src/Platform.Domain/PlatformAdmin`, `src/Platform.Contracts/PlatformAdmin`, `src/Platform.Application/PlatformAdmin`, `src/Platform.Infrastructure/Persistence/PlatformAdmin` (+ `PlatformAdmin.migration.sql`), `src/Platform.WebApi/Controllers/PlatformAdmin`, `frontend/src/app/features/platform`, `frontend/src/app/core/api/{platform,public-subscription}.contracts.ts`, `frontend/public/i18n/platform.{en,ar}.json` |
| Namespace | `PlatformAdmin`, not `Platform`: a namespace `Platform.Application.Platform` would capture every `Platform.*` qualified name written inside `Platform.Application.*` (C# resolves `Platform` to the nearest enclosing member first), breaking other modules' code. |

## 1. Purpose and policy basis

National Technology operates Sky LIS as a multi-tenant subscription product. The Super Admin portal (the platform
tenant `TenantIds.Platform`, MODULES.md §1) manages the laboratories (clients = tenants), their plans and prepaid
wallets, the reference data that validates a laboratory's details (countries, governorates), the notification texts,
the integration library laboratories pick from, the platform default parameters and the live administrator sessions.
A laboratory can also subscribe on its own through the public wizard (CP-02), which provisions the same way as a
manual creation (WF-01). Policy basis: SRS SA-03…SA-20, CP-02, CP-66, WF-01, WF-12, BR-25; architecture pack D-03
(credential envelope), 02 §5 (rate limits), 02 §6.1 (password policy), 01 §4 (tenancy and RLS).

## 2. Scope and out of scope

In scope: every page listed above (backend and admin portal pages), the public subscription API, the tenant-facing
wallet API, the read port that blocks sign-in of inactive clients, the provisioning ports (default branch, laboratory
administrator, master-data copy), seed data (5 countries, 27 Egyptian governorates, 3 plans, 5 notification templates).

Out of scope (owned elsewhere or later): the public site and wizard pages (`acc`, wave 2, from
`core/api/public-subscription.contracts.ts`); the CP-66 wallet page (`acc`); the global catalogue SA-12/13/14 (catalogue
agents, reused components); roles and admin users SA-06/07 (`org`); the audit log SA-09 (foundation + `acc`); the
SMS usage metering (no per-SMS price exists in the plan, the tenant or the settings: business decision, §16 Q12); the sign-in
refusal itself (wired by the identity owner through `ITenantStatusLookup`); per-period patient/test/registration
figures (registration and reporting modules fill `IClientActivityReader`).

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Client, laboratory, tenant (عميل / مختبر) | `Tenant` aggregate (`saas.tenants`); its id is the tenant id of every row of the laboratory |
| Portal URL | `https://{PortalSlug}.{Lis:Platform:PortalDomain}` (default domain `skylis.app`) |
| Source | `Subscription` (wizard) or `Manual` (SA-04) |
| Plan (باقة) | `Plan` (`saas.plans`) |
| Wallet (محفظة), top-up, charge, usage | `Wallet` (`saas.wallets`) + `WalletTransaction` (`saas.wallet_transactions`) + `WalletTopUpRequest` (`saas.wallet_top_up_requests`) |
| Country, governorate, district | `Country` (`saas.countries`), `Governorate` (`saas.governorates`) |
| Notification template | `NotificationTemplate` (`saas.notification_templates`), served to senders through `INotificationTemplateSource` |
| Analyser model, PCR kit, code system, rule template | `AnalyserModel`, `PcrKitTemplate`, `CodeSystem` (+ `CodeSystemItem`), `RuleTemplate` (`saas.*`) |
| Subscription request | `SubscriptionRequest` (`saas.subscription_requests`), the wizard's pending state |
| Platform parameters | the platform tenant's rows of `app.tenant_settings` (foundation), edited on SA-19 |

## 4. Actors and permissions

Every admin request additionally requires the caller's tenant to be the platform tenant (`PLT-403`, see §9): a
client role holding `tenancy.*` never reaches these endpoints.

| Permission key | Grants |
| --- | --- |
| `platform.view` | SA-03 dashboard; SA-08 sessions list |
| `platform.manage` | SA-08 terminate a session |
| `tenancy.view` | clients list/detail, plans, wallets and their transactions |
| `tenancy.create` | provision a client manually (SA-04 "Add") |
| `tenancy.edit` | edit a client's details and plan |
| `tenancy.manage` | activate/deactivate a client, reset its administrator's password, plans (create, edit, activate), countries, governorates, wallet top-up and decline of top-up requests |
| `notifications.view` / `notifications.manage` | SA-20 list / create, edit, activate |
| `integration.view` / `integration.manage` | SA-15…SA-18 list / create, edit, activate, import codes |
| `settings.view` / `settings.manage` | SA-19 (foundation endpoint under the platform tenant) |
| `wallet.view` / `wallet.create` | tenant-facing: read own wallet and transactions / request a top-up (CP-66) |
| anonymous | the subscription wizard and its country/governorate lists (rate-limited) |

Countries and governorates are edited with `tenancy.manage` (reference data of client provisioning).

## 5. Aggregates

| Root (table) | Children / values | References by id |
| --- | --- | --- |
| `Tenant` (`saas.tenants`) | `ClientProfile` value (name en/ar, address, mobiles E.164, licence, manager, e-mail) | `CountryId`, `GovernorateId`, `PlanId`, `AdministratorUserId` (identity) |
| `Plan` (`saas.plans`) | `PlanTerms` value | — |
| `Wallet` (`saas.wallets`, one per tenant) | `WalletTransaction` (entity, history never loaded with the root), `WalletTopUpRequest` (entity) | `TenantId` |
| `Country` (`saas.countries`) | — | — |
| `Governorate` (`saas.governorates`) | — | `CountryId` |
| `NotificationTemplate` (`saas.notification_templates`) | — | — |
| `AnalyserModel`, `PcrKitTemplate`, `CodeSystem`, `RuleTemplate` (`saas.*`) | `CodeSystemItem` rows (`saas.code_system_items`, key `(code_system_id, code)`, not field-audited) | `PcrKitTemplate.AnalyserModelId` |
| `SubscriptionRequest` (`saas.subscription_requests`) | — | `CountryId`, `GovernorateId`, `TenantId` once completed |

All tables are platform-level: schema `saas`, no `tenant_id`, no RLS; `lis_app` receives `USAGE` on the schema and
`SELECT, INSERT, UPDATE` (plus `DELETE` on countries and governorates) through `PlatformAdmin.migration.sql`.

## 6. State tables

Client (`Tenant.Status`):

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Trial | subscribed through the wizard, trial running until `TrialEndsAtUtc` (platform `trialDays`, default 14); users may sign in until the end date | all details, plan | no | no | `tenancy.view` |
| Active | in service; users may sign in | all details, plan | no | no | `tenancy.view` |
| Inactive | blocked: users refused at sign-in, live sessions revoked | all details, plan | no | no (reactivation) | `tenancy.view` |

Subscription request: Pending (code sent) → Verified (code accepted) → Completed (tenant provisioned); every
transition within one hour of the start. Top-up request: Pending → Completed (topped up) | Declined.
Plans, countries, governorates, templates and library rows: active / inactive only.

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P1 | — → Trial/Active | Provision (manual SA-04 / wizard CP-02) | `tenancy.create` / anonymous after S2 | country active (GEO-011), governorate active (GEO-012) and in the country (GEO-013), mobiles of the country's length (GEO-010), plan active (PLN-010), slug valid (CLI-001) and free (CLI-100), username valid (CLI-042) and free (CLI-101), password policy (CLI-040, CLI-041) | — | — | — | `TenantProvisioned` (+ membership/branch events of `org`) | no |
| P2 | Trial/Inactive → Active | Activate | `tenancy.manage` | not already Active (CLI-010) | — | — | — | `TenantActivated` | no |
| P3 | Trial/Active → Inactive | Deactivate | `tenancy.manage` | not already Inactive (CLI-011) | — | — | **required** (CLI-021) | `TenantDeactivated` (+ `RefreshSessionRevoked(TenantSuspended)` per live session) | no |
| P4 | any | Edit details | `tenancy.edit` | as P1 for place and mobiles | — | — | — | — | no |
| P5 | any | Change plan | `tenancy.edit` | plan active (PLN-010) | — | — | — | `TenantPlanChanged` | no |
| P6 | any | Reset administrator password | `tenancy.manage` | administrator recorded (CLI-030), password policy (CLI-040/041); the new password travels in the credential envelope | — | — | — | `PlatformUserPasswordRecovered`, `RefreshSessionRevoked(PasswordChanged)` | no |
| W1 | wallet | Top up | `tenancy.manage` | amount > 0 (WAL-001), reference (WAL-002); optional pending request (WAL-011) | — | — | — | `WalletToppedUp` | no |
| W2 | wallet | Monthly charge | system (job `Lis:Jobs:WalletCharge`) or SA-05 "Run charges now" (`tenancy.manage`) | client Active, next charge due, plan currency = wallet currency (§16 Q11) | — | — | — | `WalletDebited(Subscription)` | yes |
| W3 | wallet | Usage debit | system (later) | balance covers the amount (WAL-010) | — | — | — | `WalletDebited(Usage)` | yes |
| W4 | — → Pending | Request top-up (CP-66) | `wallet.create` (client tenant) | amount > 0 (WAL-001) | — | — | — | `WalletTopUpRequested` | no |
| W5 | Pending → Declined | Decline top-up request | `tenancy.manage` | Pending (WAL-011) | — | — | — | — | no |
| S1 | — → Pending | Start subscription | anonymous | as P1 for country, governorate and mobiles | — | — | — | `SubscriptionRequested`; code sent (purpose `subscription`) | no |
| S2 | Pending → Verified | Verify code | anonymous | alive (SUB-001), Pending (SUB-002), code accepted (SUB-010: wrong, expired, or fifth attempt) | — | — | — | `SubscriptionVerified` | no |
| S3 | Verified → Completed | Complete | anonymous | alive (SUB-001), Verified (SUB-003), then P1 | — | — | — | `SubscriptionCompleted`, `TenantProvisioned` | no |
| M1 | — → active | Create plan / country / governorate / template / library row | `tenancy.manage` / `notifications.manage` / `integration.manage` | shape and natural-key uniqueness (PLN-100, GEO-100/101, NTF-100, LIB-100…103) | — | — | — | — (field history) | no |
| M2 | active ↔ inactive | Deactivate / reactivate | as M1 | — | — | — | — | — | no |
| M3 | country → deleted | Delete country | `tenancy.manage` | no governorate or client references it (GEO-110) | — | — | — | — | no |
| I1 | code system | Import codes (CSV ≤ 20 MB, upsert by code) | `integration.manage` | file shape (LIB-010), size (LIB-011), each code/display (LIB-004) | — | — | — | `CodeSystemImported` | no |
| X1 | live → revoked | Terminate admin session (SA-08) | `platform.manage` | session of the platform tenant (SES-404), not the caller's own (PLT-010) | — | — | — | `RefreshSessionRevoked(Admin)` | no |

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-PLT-001 | A portal slug is 3–40 of `a-z 0-9 -`, not reserved, unique | `Tenant.CleanSlug` + handler + unique index | CLI-001 / CLI-100 | "The portal address is not valid." / "This portal address is taken." |
| INV-PLT-002 | A client's governorate belongs to its country; both active | `Tenant.Provision/UpdateProfile` | GEO-011/012/013 | |
| INV-PLT-003 | A client's users may sign in only while Active or within the trial | `Tenant.IsUsableAt` → `ITenantStatusLookup` | CLI-020 | "Your laboratory's subscription is inactive." |
| INV-PLT-004 | Mobiles are stored E.164 and have the country's length | `Country.NormaliseMobile` | GEO-010 | |
| INV-PLT-010 | A referenced country is not deleted | handler (governorates, clients, requests) | GEO-110 | |
| INV-PLT-011 | ISO code unique; governorate English name unique in its country | handler + unique indexes | GEO-100 / GEO-101 | |
| INV-PLT-020 | Plan code unique | handler + unique index | PLN-100 | |
| INV-PLT-030 | One wallet per client; balance = sum of movements | unique index `ux_wallets_tenant_id`; every movement goes through the root and stores the balance after it | — | |
| INV-PLT-031 | Usage never takes the balance below zero | `Wallet.DebitUsage` | WAL-010 | |
| INV-PLT-040 | Template code unique; only known placeholders; e-mail needs subjects | aggregate + handler + unique index | NTF-100 / NTF-003 / NTF-004 | |
| INV-PLT-050…054 | Library natural keys unique (vendor+model, kit name, name+version, rule name); kit JSON parses | handler + unique indexes; `PcrKitTemplate.Json` | LIB-100…103 / LIB-003 | |
| INV-PLT-060 | A wizard request completes once, within one hour, after the code | `SubscriptionRequest` | SUB-001/002/003 | |
| INV-PLT-070 | Platform administration only from the platform tenant | `PlatformAdminScope.Require` in every admin handler (gate `PlatformAdminGateTests`) | PLT-403 | "Platform administration is available only in the Super Admin portal." |

## 9. Segregation-of-duties rules

No SoD rule between two people in this version (single-actor administration). One scope rule replaces it:

```text
PLT-403  Platform administration only from the platform tenant.
  On every command and query of SA-03…SA-20: the current tenant must be TenantIds.Platform.
  Refusal: "Platform administration is available only in the Super Admin portal." (403)
  Configurable: no.
PLT-010  A platform administrator does not terminate their own session on SA-08 (sign out instead).
```

## 10. Approval gates

None.

## 11. Events and policies

```text
TenantProvisioned { TenantId, PortalSlug, Source, PlanId }     P1   Reacts: none (provisioning is synchronous; the portal SMS is sent after the commit)
TenantActivated { TenantId }                                   P2   Reacts: none
TenantDeactivated { TenantId, Reason }                         P3   Reacts: none (sessions are revoked in the same commit)
TenantPlanChanged { TenantId, FromPlanId, ToPlanId }           P5   Reacts: none yet (limits are read at use)
WalletToppedUp { WalletId, TenantId, TransactionId, Amount, Reference }   W1
WalletDebited { WalletId, TenantId, TransactionId, Kind, Amount, BalanceAfter }   W2/W3   Reacts: later — "when at or below zero, pause metered services"
WalletTopUpRequested { WalletId, TenantId, RequestId, Amount }  W4   Reacts: later — notify operations
SubscriptionRequested / SubscriptionVerified / SubscriptionCompleted { SubscriptionId (, TenantId) }   S1/S2/S3
CodeSystemImported { CodeSystemId, Name, Version, Inserted, Updated }   I1
```

Security events (02 §7 ledger) written by this module: `TENANT_PROVISIONED`, `TENANT_ACTIVATED`, `TENANT_DEACTIVATED`,
`CLIENT_ADMIN_PASSWORD_RESET`, `SESSION_TERMINATED_BY_ADMIN`, `SUBSCRIPTION_STARTED`, `SUBSCRIPTION_VERIFIED`,
`SUBSCRIPTION_VERIFY_FAILED`, `SUBSCRIPTION_COMPLETED` (details are categories and ids, never codes, mobiles or passwords).

**Provisioning transaction (WF-01).** `ITenantProvisioningScope` (`Persistence/PlatformAdmin/TenantProvisioning.cs`) binds
the scope to the new tenant (`ForTenant(newTenantId)`), opens ONE database transaction while elevated (the connection is
stamped with the RLS bypass when it opens, so the platform tenant's catalogue is readable), then drops the elevation
for the work: `saas.tenants` row, wallet, `IDefaultBranchProvisioner` and `ITenantAdministratorProvisioner` (org,
stage-only, query filter bound to the new tenant), `IMasterDataCloner.StageAsync` (catalogue A stages its copies,
elevated around the call), the wizard request (Completed), the handler's single flush, then
`IMasterDataCloner.ApplyAsync` (catalogue B's set-based SQL, elevated, remapping by code onto A's flushed rows), and the
scope commits; any exception rolls the whole tenant back. After the commit: the `portal-url` SMS to the manager's
mobile and the security event. `TenantProvisioning.cs` is the allow-listed elevation site of provisioning
(WebApiHardeningTests).

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Client name, address, licence, plan, slug, status | Operational, audited (field history under the platform tenant; the provisioning commit under the new tenant) | |
| Mobiles, manager name, e-mail | Personal data (contact) | never logged; the security events carry no mobile |
| Administrator password | Credential | envelope only (D-03); hashed by Argon2id; never stored in a platform table |
| Wallet balance and transactions | Financial | retention with financial records (assumed 10 years) |
| Subscription requests | Personal data, short-lived | to be purged 30 days after expiry by the retention job (not built yet) |
| One-time codes | Credential | foundation `identity.otp_challenges` (HMAC only) |

## 13. Reference data and configuration

Seed (`PlatformAdmin.migration.sql`, idempotent on natural keys): countries EG, SA, AE, KW, JO (dial codes +20, +966,
+971, +965, +962; currencies EGP, SAR, AED, KWD, JOD; mobile lengths 10, 9, 9, 8, 9; time zones Africa/Cairo,
Asia/Riyadh, Asia/Dubai, Asia/Kuwait, Asia/Amman; language ar); the 27 governorates of Egypt; plans STARTER (1,500 EGP,
1 branch, 5 users, 500 SMS, 1 analyser), STANDARD (3,500 EGP, 3, 20, 2,000, 5), ENTERPRISE (9,000 EGP, 20, 200,
10,000, 50); templates `otp`, `password-recovery`, `portal-url`, `treasury-request`, `panic-value` (SMS, en/ar).

Configuration: `Lis:Platform:PortalDomain` (default `skylis.app`); `Lis:Jobs:WalletCharge:{Enabled, PollInterval}` (default
`true`, one hour) for the monthly charge (W2, §16 "Monthly charge run"). Platform parameters read from the platform
tenant's settings: `trialDays` (14), `passwordMinLength` (8). The wizard's default plan is the active plan with the
lowest monthly fee (ties by code).

Password policy (decision of the user, 2026-10-09, superseding the SRS composition rules of CP-02): the architecture
pack 02 §6.1 applies everywhere, including the wizard — minimum length from settings (`passwordMinLength`, default 8),
maximum 128, no composition rules, must not contain the username. The wizard's "match" check is done in the browser.

## 14. Registers, reports and exports

| Register | Filters | Columns | Who / export |
| --- | --- | --- | --- |
| Clients (SA-04) | search (name, slug, manager, mobile), source, plan, status, page | name en/ar + portal URL, country · governorate, manager + mobile, plan, wallet balance, branches, users, created, source, status | `tenancy.view`; CSV in the browser |
| Plans (SA-05) | active | code, name, fee + currency, branches, users, SMS quota, analysers, clients on the plan | `tenancy.view` |
| Wallet (SA-05) | client; transactions by kind, page | balance, currency, next charge, monthly fee; pending top-up requests; transactions (date, kind, amount, balance after, reference, note, user) | `tenancy.view`; CSV |
| Sessions (SA-08) | search (user) | user, username, IP, since, last seen, status, current | `platform.view` |
| Countries / governorates / templates / library | search, active | as the SRS data lists | view permissions; CSV |
| Dashboard (SA-03) | period (today, week, month, year) | clients total/active/inactive/trial, admin users, served patients, tests, registrations (0 until the registration module), wallets at or below zero, subscription vs manual, plan distribution, registrations by client, recent clients | `platform.view` |

## 15. Notifications and escalations

| When | Who | Channel / template |
| --- | --- | --- |
| S1 start, resend | manager mobile | SMS `otp` (purpose `subscription`, 6 digits, 5 minutes, 5 attempts) |
| P1 provisioned | manager mobile | SMS `portal-url` ({lab}, {url}, {username}) |
| P6 password reset | administrator mobile | SMS `portal-url` (no password in the message; the platform administrator hands the password over) |
| any sender | — | the active template of the code from `saas.notification_templates`, aliases `{lab}`→`{labName}`, `{acc}`→`{accession}`; built-in text when missing or inactive |

## 16. Acceptance scenarios, open questions and assumptions

Tests: `tests/Platform.Domain.UnitTests/PlatformAdmin/*`, `tests/Platform.Application.UnitTests/PlatformAdmin/*`,
`tests/Platform.ArchitectureTests/PlatformAdminGateTests.cs`, `tests/Platform.IntegrationTests/PlatformAdmin*.cs`,
`tests/Platform.WebApi.FunctionalTests/PlatformAdmin*.cs`.

```gherkin
Scenario S-P1-sub: a laboratory subscribes
  Given an active country EG with mobile length 10 and an active governorate of EG
  When the manager starts the wizard with mobile 01012345678, verifies the code sent to +201012345678 and completes with a free username and a valid password
  Then a Trial client with source Subscription, a wallet, the default branch, the administrator and the master-data copy exist, and the portal-url SMS went to +201012345678

Scenario S-P1-mobile: a wrong mobile length is refused (GEO-010) and nothing is stored
Scenario S-S2-code: a wrong code is refused (SUB-010); the fifth wrong code closes the challenge
Scenario S-S3-order: complete before verify is refused (SUB-003); complete twice is refused (SUB-003)
Scenario S-S1-expiry: after one hour every step is refused (SUB-001)
Scenario S-P1-manual: a platform administrator creates a client with an explicit slug → Active, source Manual; a taken slug → CLI-100
Scenario S-P3: deactivation needs a reason (CLI-021), revokes the client's live sessions; a second deactivation → CLI-011; activation → Active; a second activation → CLI-010
Scenario S-PLT-403: a client user whose role holds tenancy.view lists clients → 403 PLT-403
Scenario S-W1: a top-up of 500 with a reference → balance +500, transaction TopUp with balance after; amount 0 → WAL-001
Scenario S-W3: a usage debit larger than the balance → WAL-010, nothing changes
Scenario S-W2: an Active client whose next charge is due is charged its plan's fee once (Subscription, reference SUB-yyyy-MM-PLAN), the next charge moves one month; a second run in the period charges nothing; a balance short of the fee goes below zero, the client stays Active and metered usage then refuses WAL-010; Trial / Inactive clients are skipped; a currency mismatch fails without a debit
Scenario S-W4: a laboratory requests a top-up (wallet.create) → Pending; the platform tops up against it → Completed
Scenario S-M3: deleting a country that has governorates → GEO-110
Scenario S-NTF: a template with {unknown} → NTF-003; {lab} is served as {labName}
Scenario S-I1: importing "code,display" CSV twice with one changed display → second import inserts 0, updates 1
Scenario S-X1: terminating another administrator's session revokes it; terminating one's own → PLT-010
```

Open questions (the assumed answer is what is built):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | Does an expired trial block sign-in? | Yes: Trial past `TrialEndsAtUtc` reads as not active (CLI-020) until a platform administrator activates the client. |
| Q2 | What is the wizard's plan? | The active plan with the lowest monthly fee; the platform changes it on SA-04. |
| Q3 | Administrator password reset (SA-04): the pack's 02 §6.1 describes a one-time 24 h link that forces a change; the identity model has no link or forced-change flag yet. | The platform administrator sets a new password through the envelope; sessions are revoked; the laboratory receives the portal address by SMS. A link + forced change replace this when identity supports them. |
| Q4 | A tenant without a client record (seeded by the development tools) | Treated as active by `ITenantStatusLookup`. |
| Q5 | Plan currency vs wallet currency | Plans carry their own currency; the wallet takes the country's currency; conversion for the monthly charge is decided with the charge job (OQ-02). |
| Q6 | Are clients deleted? | Never; deactivated. Countries may be deleted while unreferenced (SA-10); governorates, plans, templates and library rows are deactivated. |
| Q7 | Who edits countries and governorates? | `tenancy.manage`. |
| Q8 | Branch and user counts on SA-04 cross tenants; how are they read without elevation? | A `SECURITY DEFINER` function `saas.client_usage_counts(uuid[])` owned by `lis_owner` returns only counts and only when the calling session's tenant is the platform tenant. |
| Q9 | Retention of subscription requests | 30 days after expiry (purge job later). |
| Q10 | Should the wizard confirm the password on the server? | No: one credential per request (the envelope consumes one nonce per request); the browser checks the confirmation. |
| Q11 | Monthly charge (W2) where the intake is silent | Built 2026-10-12 (validation LIS-VR-002 §7, C-8). Charged: **Active** clients only (Trial — expired or not — and Inactive are skipped and stay due; once activated, the missed periods are charged **one per run**, oldest first — whether reactivation should reset the date instead is for the business owner). Amount: the plan's monthly fee as it is at the run. Insufficient balance: the domain rule applies (the subscription may take the balance below zero, the portal keeps working, CP-66; metered usage pauses at zero, WAL-010); the client is **not** suspended or marked, `WalletDebited` carries the balance after it and the SA-03 dashboard counts the wallet at or below zero. Plan currency ≠ wallet currency (Q5, OQ-02): **not charged**, `WALLET_CHARGE_FAILED` written to the security ledger every run until resolved. Idempotency: per tenant and period by the due-date check and the date move in the same commit, guarded by the wallet's row version (no period key, no migration). |
| Q12 | SMS usage metering (W3) | **Not built — business decision.** Plans carry an SMS quota (`SmsQuota`) but no per-SMS price exists in the plan, the client record or the platform parameters, so nothing can be debited per message. Decide: price per SMS (per plan or platform parameter), whether SMS within the quota is free, and whether a refused debit (WAL-010) blocks the SMS. `Wallet.DebitUsage` and the "SMS used" figure (count of Usage movements) are ready. |

### Monthly charge run (built 2026-10-12)

`src/Platform.Application/PlatformAdmin/Jobs/WalletChargeRun.cs` (`WalletCharger`: one client, one commit; `RunWalletChargesCommand`
[`tenancy.manage`, platform tenant]), `src/Platform.Infrastructure/Jobs/WalletChargeJob.cs` (the pass: lists the due wallets,
charges each in its own scope bound to the platform tenant — no elevation, the saas tables carry no RLS — retries a lost
row-version race, serialised in-process), `src/Platform.WebApi/Startup/WalletChargeHostedService.cs`,
`POST /api/v1/platform/wallets/charge-run` → `WalletChargeRunSummary` (charged, skipped, failed, at or below zero, one row per
due wallet). SA-05 "Run charges now" button. Tests: `tests/Platform.IntegrationTests/WalletChargeRunTests.cs`,
`PlatformAdminFlowTests.Run_charges_now_…`, `frontend/…/subscriptions.page.spec.ts` ("Run charges now (W2)").

### Change request 2026-10-19 — Client login logs (LIS-CR-003 §2, package P-A1, SA-08b)

**What changes.** A history grid of the client users' browser sessions beside the live list of SA-08: one row per
rotation family of `identity.refresh_sessions` with client, user, login and logout (UTC), IP address, device (Computer /
Mobile / Tablet / Unknown), OS, browser, duration (minutes) and status; filters by client, user (username or display
name), login window, logout window, device, duration bounds and status; CSV export. No model change, no migration: the
facts were already stored by the security edge (02 §4.3).

**Grouping rules** (`ClientLoginLogReader`, one SQL statement, composed with EF for the filters, the order and the page):
login = the first member's `IssuedAtUtc`; last seen = the latest `LastSeenAtUtc` of any member; logout and status =
the revocation with reason Logout (**SignedOut**), else the first revocation for another reason (**Revoked**, reason
shown: Admin, PasswordChanged, ReuseDetected, Expired = idle timeout, TenantSuspended), else the latest sliding
`ExpiresAtUtc` when it is past (**Expired**), else **Active** (logout empty); duration = (logout ?? last seen) − login in
whole minutes, never negative. The address and the user agent are the first member's (the sign-in request). Machine
sessions (`SessionKind.Machine`) and the platform tenant's own sessions are excluded; the platform tenant is listed only
when the client filter names it. A tenant without a client record (development seeding) shows "No client record".

**User agent.** `UserAgentParser` (`Platform.Application/PlatformAdmin/`, pure, unit-tested) reads device class, OS
family + version and browser family + major version from the stored string: Chrome, Edge, Firefox, Safari, Opera,
Samsung Internet, Internet Explorer on Windows, macOS, iOS, Android, ChromeOS, Linux; anything else is "Unknown".
Client hints are not stored, so Windows 11 reads as Windows 10. The device cannot be classified by the database, so a
device filter is applied after the read on the server-sorted families (capped at 10 000 like the export) and paged in memory.

**Requests** (`Queries/ClientLoginLogQueries.cs`, both `[platform.view]` + `IPlatformAdministrationRequest`):
`ListClientLoginLogsQuery(tenantId?, search?, loginFromUtc?, loginToUtc?, logoutFromUtc?, logoutToUtc?, device?,
minMinutes?, maxMinutes?, status?, page?, pageSize?, sort?, direction?)` → `Paged<ClientLoginLogItem>`;
`ExportClientLoginLogsQuery(same filters, language?, sort?, direction?)` → CSV (UTF-8 with BOM, RFC 4180, formula-safe
cells, headers in the language, at most 10 000 rows, times `yyyy-MM-dd HH:mm:ss` UTC). Shape (VALIDATION-400): search ≤ 100,
windows ordered, minutes ≥ 0 and min ≤ max, device and status among the known names, page bounds, sort key of
`PlatformAdminSortKeys.ClientLoginLogs` = `login`, `logout`, `client` (English name), `clientAr`, `user`, `duration`,
`status` (default login desc). No new domain codes: refusals are PLT-403 (client tenant), AUTHZ-001 (no key) and VALIDATION-400.

**Endpoints.** `GET /api/v1/platform/login-logs` (paged, sorted) and `GET /api/v1/platform/login-logs/export`
(`text/csv; charset=utf-8`, attachment `client-login-logs-<utc stamp>.csv`), `PlatformLoginLogsController`.

**Frontend.** `features/platform/pages/login-logs.page.*` (route `/admin/login-logs`, facade `LoginLogsFacade`): a filter
card (client select from the clients register, user search, two UTC date-time windows, device, status, duration bounds;
applied on submit, a reversed window or crossed minutes is refused in the browser), a server-sorted and server-paged table,
"Export CSV" downloading the server's file, empty state. Every time on the page is UTC (`ui/utc-time.ts`), as the prototype
labels them. Dictionary keys `platform.loginLogs.*` (English and Arabic). Nav entry (integrator): admin · administration ·
`/admin/login-logs` · `platform.view` · `shell.nav.loginLogs` · "Client login logs" / "سجل دخول العملاء".

**Tests.** `Platform.Application.UnitTests/PlatformAdmin/ClientLoginLogTests.cs` (parser, validators, mapping, CSV),
`Platform.IntegrationTests/ClientLoginLogReaderTests.cs` (grouping, statuses, durations, filters, every sort key, paging),
`Platform.WebApi.FunctionalTests/ClientLoginLogsFlowTests.cs` (sign-in → sign-out = one SignedOut row with duration and
device, filters, sort keys, CSV, 400 on a reversed window, 403 without `platform.view` or from a client tenant),
`login-logs.facade.spec.ts`, `login-logs.page.spec.ts`.

| # | Question | Assumed answer |
| --- | --- | --- |
| Q13 | Which address and agent does a row show when a family rotated across addresses? | The first member's (the sign-in); a later refresh from another address is not shown (the live list SA-08 shows the current one). |
| Q14 | Are the times shown in UTC or in the viewer's zone? | UTC on the page, the filters and the CSV, as the prototype's column names say; the client's own time zone (`timeZone` setting) is not applied. |
| Q15 | Is a session ended by the idle timeout (`RevocationReason.Expired`) "Expired" or "Revoked"? | Revoked, reason "Idle timeout": it is an explicit revocation; "Expired" is reserved for a session nobody touched until its sliding lifetime ran out. |
| Q16 | How far back does the history go? | As far as `identity.refresh_sessions` is kept; no retention purge exists yet (CLAUDE.md §5 "not yet built"), so the list grows until one does. |
| Q17 | Should a device filter page on the server? | Not possible without storing the parsed device: it is applied after the read, capped at 10 000 families of the other filters; a "truncated" flag is not surfaced on the page. |

## 17. Change request 2026-10-19 — location hierarchy under a governorate (LIS-CR-003 §3, package P-A2, SA-11b "Locations")

Source: `docs/build/CR-2026-10-19_Prototype-Gaps.md` §3 (prototype difference item A2). Built in slot `pat` beside the
Patients change (intake LIS-INT-PAT-001 §18); the tables arrive with the wave migration `PrototypeGaps`.

**What changes.**

| Item | Decision |
| --- | --- |
| Aggregates | `City` (governorate id), `District` (city id), `Area` (district id) in `Platform.Domain/PlatformAdmin/Locations.cs`: `LocalizedText Name` (both languages required, ≤ 100), `IsActive`; `Rename`, `Deactivate`, `Reactivate`; the parent is fixed once created. No domain events (reference data, like countries and governorates). |
| Tables | `saas.cities`, `saas.districts`, `saas.areas` (no tenant), `name_en` / `name_ar`, `is_active`, `xmin` concurrency. `PlatformAdmin.migration.sql` block 5 grants `SELECT, INSERT, UPDATE` to `lis_app` (no deletes: rows are deactivated) and the per-parent case-insensitive unique English names `ux_cities_governorate_name`, `ux_districts_city_name`, `ux_areas_district_name`. No seed rows: the business owner enters the cities of each governorate. |
| Rules | Parent must be active to create under it (`GEO-015`); English name unique within the parent, case-insensitive (`GEO-102` city, `GEO-103` district, `GEO-104` area; checked in the handler, backed by the index → `UNIQUE-409` on a race); deactivating a parent with active children is refused (`GEO-112`); not found `GEO-406` city, `GEO-407` district, `GEO-408` area (`GEO-405` governorate); name shape `GEO-006` / `VALIDATION-400`. Reactivating a child under an inactive parent is refused with `GEO-015`. |
| Commands / queries | `Create{City,District,Area}Command`, `Rename…Command`, `Set…ActiveCommand` [`tenancy.manage`]; `List{Cities,Districts,Areas}Query(parentId, search?, isActive?)` [`tenancy.view`], ordered by English name; all `IPlatformAdministrationRequest` (platform tenant only, `PLT-403`). Public `Public{Cities,Districts,Areas}Query(parentId?)`: active rows, id + names, every row when unfiltered. |
| Governorates page (SA-11b) | Row action *Locations* (`tenancy.manage`) opens a drawer (`features/platform/ui/locations-drawer.component.ts`, facade `state/locations.facade.ts`, column `ui/location-column.component.ts`) with three linked columns — cities of the governorate → districts of the selected city → areas of the selected district — each with a server search, *Show inactive*, *Add* / *Rename* through an inline bilingual editor and *Activate* / *Deactivate*; the level a refusal addressed shows it. Dictionary keys `platform.locations.*` (English and Arabic). |
| Consumers | Patients (the patient form's linked selects and the chain rule, intake LIS-INT-PAT-001 §18) through the public lookups. The client (tenant) address of the subscription wizard and SA-04 **stays free text** in this change. |

**Endpoints.** `GET/POST /api/v1/platform/governorates/{id}/cities`, `PUT /api/v1/platform/cities/{id}`,
`POST /api/v1/platform/cities/{id}/deactivate|reactivate`, `GET/POST /api/v1/platform/cities/{id}/districts`,
`PUT /api/v1/platform/districts/{id}` (+ the activate pair), `GET/POST /api/v1/platform/districts/{id}/areas`,
`PUT /api/v1/platform/areas/{id}` (+ the pair) — `PlatformLocationsController` in `PlatformReferenceDataControllers.cs`; list
queries take `search` and `isActive`. Anonymous pick lists beside `GET /api/v1/public/governorates`:
`GET /api/v1/public/cities?governorateId=`, `GET /api/v1/public/districts?cityId=`, `GET /api/v1/public/areas?districtId=`
(`PublicReferenceController`).

**Tests.** `Platform.Domain.UnitTests/PlatformAdmin/LocationTests.cs` (factory, rename, activate pair, name rules),
`Platform.IntegrationTests/LocationsModuleTests.cs` (unique indexes → `UNIQUE-409`, list projections, public lookups),
`Platform.WebApi.FunctionalTests/LocationsFlowTests.cs` (an administrator builds the hierarchy and every rule answers with its code;
a viewer lists but does not change; a client tenant is fenced out; the anonymous pick lists answer the asked parent's active rows),
`locations.facade.spec.ts`, `locations-drawer.component.spec.ts`.

```gherkin
Scenario S-GEO-tree: Cairo → "Nasr City" → "7th district" → "Area 1" created in turn; each list shows its rows; the public lookups answer them
Scenario S-GEO-dup: a second "nasr city" under Cairo → GEO-102 (409), nothing created
Scenario S-GEO-parent: a district under a deactivated city → GEO-015 (409)
Scenario S-GEO-children: deactivating a city with an active district → GEO-112 (409); after the district is deactivated → 204
Scenario S-GEO-view: tenancy.view lists; create / rename / deactivate → 403; a client tenant → PLT-403
```

| # | Question | Assumed answer |
| --- | --- | --- |
| Q18 | Who enters the hierarchy, and is any of it seeded? | The platform administrator from SA-11b; nothing is seeded (the LDM manual carries no city list), so every governorate starts without cities and the laboratories keep the free-text city until it is filled. |
| Q19 | Are deletes allowed? | No: like countries with governorates, rows are deactivated; an unreferenced row could be deleted later if the business owner asks (the countries' delete rule, SA-10). |
| Q20 | Is the unique name per parent on the English name only? | Yes (as governorates within a country): the Arabic spelling may vary. |
| Q21 | Does the client (tenant) address use the hierarchy? | Not in this change (LIS-CR-003 §3): the wizard and SA-04 keep free text; a later change may add the selects once the hierarchy is filled. |

## 18. Change request 2026-10-20 — laboratory addresses and the public card (LIS-CR-005, package P-API)

- **Portal address** of a laboratory = `{Lis:Platform:PortalBaseUrl}/lab/{slug}` (`Tenant.PortalUrl(baseUrl)`, `Tenant.PortalUrlOf`),
  on SA-04 list and detail, the dashboard's recent clients, the provisioning response and its `portal-url` SMS, the wizard result,
  the SA-04 password-reset SMS and CP-65. `Lis:Platform:PortalDomain`, `Tenant.DefaultPortalDomain` and `Tenant.BuildPortalUrl` are
  removed. The `portal-url` template text is unchanged (`{url}`).
- **Setting** `Lis:Platform:PortalBaseUrl` (ConfigGuard `InspectPortalBaseUrl`, enforced at start-up): an absolute http(s) URL
  without user information, path, query, fragment or trailing slash; in Production required and `https`; elsewhere optional
  (default `http://127.0.0.1:4300`, the development SPA) and `http` allowed.
- **Public laboratory card** (anonymous, global rate limit chain, `GetPublicLaboratoryQuery` [AllowUnauthenticated]):
  `GET /api/v1/public/laboratories/{slug}` → `200 { slug, nameEn, nameAr, logoVersion }` for a usable client laboratory (Active,
  or Trial before its end — the statuses sign-in lets through); `404 PLT-404` otherwise (unknown, inactive, trial ended, the platform
  tenant, malformed address). `GET /api/v1/public/laboratories/{slug}/logo` → the active logo's bytes with its content type,
  `Cache-Control: public, max-age=300`, `ETag: "logo-v{version}"` (the only response exempt from the global `no-store`:
  `SecurityHeadersMiddleware.PublicCacheItem`); `404 PLT-404` when the card would be 404 or there is no logo. Port
  `IPublicLaboratoryDirectory` (Infrastructure `PublicLaboratoryDirectory`): the client record from `saas.tenants` (no RLS), the logo
  row in the laboratory's own tenant scope (`ForTenant`, no elevation); the file is opened in the same scope.
- **New code** `PLT-404` (laboratory address not found, 404).
- **Elevation**: none added (the reads use the platform table and a tenant scope, not `Elevate()`).
- **Tooling**: `ClientRecordRegistrar` (Application, not an endpoint) + `Tenant.RegisterExisting` create the client record of an
  existing tenant (Active, Manual, no event, no wallet).

## 19. Change request 2026-10-24 — silent clients (LIS-CR-007 A-01, package P-PLT)

Source: the reviewer's comments response LIS-RC-001, point A-01 ("show the clients that stopped using the system so we can call
them"); brief `docs/build/CR-2026-10-24_Review-Quick-Wins.md` §6.

- **Rule** (`Platform.Domain.PlatformAdmin.ClientSilence`, pure): last activity of a client = the later of its users' last sign-in
  and its last registration; a client is **silent** for a window of N days when that activity is strictly before `now − N days`, or
  when it has none and the client was created before that instant. Inactive clients are never silent. N is 1…90 (default 3; the
  Clients page offers 3 / 7 / 14 / 30).
- **Last sign-in** = the latest `identity.refresh_sessions.issued_at_utc` of a **browser** session of the tenant (a sign-in or a
  renewal; machine sessions of site agents do not count). **Last registration** = the latest `app.visits.registered_at_utc` of the
  client. **Administrator** shown = the client record's manager name and mobile (E.164) — provisioning creates the laboratory
  administrator from them, and the Super Admin keeps them current on SA-04.
- **Reads** (port `IClientLastActivityReader`, adapter `Persistence/PlatformAdmin/ClientLastActivityReader.cs`, no elevation, no
  migration): the Trial and Active clients and their last browser sign-in from the platform tables (`saas.tenants`,
  `identity.refresh_sessions`: no RLS) in two queries; the last registration of a client in that client's own tenant scope
  (`ForTenant`, the pattern of the public laboratory card), one indexed `max` per client (`ix_visits_tenant_id_registered_at_utc`),
  asked only for the clients whose sign-ins did not already clear them (`SilentClientFinder`). The brief mentioned the dashboard's
  `SECURITY DEFINER` counters; a new function would have needed a migration, which this package may not add — the `ForTenant` read
  gives the same answer without one. If the number of candidates grows large, a `saas.client_last_registrations()` function in
  the pattern of `saas.client_activity_counts` is the drop-in replacement (open question Q22).
- **Endpoints** (`platform.view`, platform tenant only: PLT-403):
  - `GET /api/v1/platform/clients/silent?days=3` → `SilentClientList { days, asOfUtc, trial, items[] }`, items
    `{ id, nameEn, nameAr, portalUrl, status, createdAtUtc, lastSignInAtUtc, lastRegistrationAtUtc, lastActivityAtUtc, silentDays,
    administratorName, administratorMobile }`, longest silence first (no activity at all first), never paged;
  - `GET /api/v1/platform/clients/silent/export?days=3&language=en|ar` → CSV (UTF-8 with BOM, RFC 4180, cells escaped against formula
    injection, times UTC `yyyy-MM-dd HH:mm`), file `silent-clients-{days}d-{yyyy-MM-dd-HHmm}.csv`;
  - `GET /api/v1/platform/dashboard?period=…&silentDays=3` → `PlatformDashboard` gains `silentClients`, `silentTrialClients`,
    `silentDays` (independent of the period; `silentDays` optional, default 3).
  - `days` / `silentDays` outside 1…90 → `VALIDATION-400`. No new problem code.
- **SPA** (`features/platform`): SA-03 tile "Silent for 3+ days" (red when > 0, the trial ones in the subtitle) linking to
  `/admin/clients?silent=3`; SA-04 toolbar select *Activity* (Any / Silent 3+ / 7+ / 14+ / 30+ days, shown with `platform.view`) that
  swaps the register for the silent table (`ui/silent-clients.component.ts`, facade `state/silent-clients.facade.ts`): client and
  portal address, status, administrator with a `tel:` link, last sign-in, last registration, last activity, days silent, created; the
  page's search box and status select narrow it in the browser; *Export CSV* in the header exports the server's CSV of the window;
  a row opens the client drawer. The window is kept in the address (`?silent=`), no storage.
- **Tests**: domain `ClientSilenceTests` (S-SIL-1…8), application `SilentClientFinderTests`, integration
  `ClientLastActivityReaderTests`, functional `SilentClientsFlowTests` (sign-in today → not silent; last sign-in four days old →
  silent for 3, not for 7; inactive excluded; dashboard counts; CSV; `days=0`/`91` and `silentDays=0` → 400; 403 without
  `platform.view` and for a laboratory user); SPA specs `clients-silent.page.spec.ts`, `dashboard.page.spec.ts`,
  `silent-clients.facade.spec.ts`.

| # | Question | Assumed answer |
| --- | --- | --- |
| Q22 | Is one indexed read per candidate client acceptable for the dashboard? | Yes for the current client count (tens): only clients without a sign-in inside the window are read. A `SECURITY DEFINER` function can replace it in a later migration without changing the port. |
| Q23 | Does a token renewal count as a sign-in? | Yes: an open session renews every 15 minutes while someone works, so the latest issue is the best "last seen" the platform tables hold without reading the laboratory's data. |
| Q24 | Who is "the administrator" to call? | The manager on the client record (name and mobile), which provisioning used for the laboratory administrator and the Super Admin maintains on SA-04; the administrator user's own profile may differ later. |
| Q25 | Are trial clients included? | Yes, counted separately in the tile's subtitle (a silent trial is the most important call). |
