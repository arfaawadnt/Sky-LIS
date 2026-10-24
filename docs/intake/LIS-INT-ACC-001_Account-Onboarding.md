# LIS-INT-ACC-001 — Account & onboarding (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Slot / modules | `acc` — permission modules `support` (tickets), `wallet` (tenant view of the platform wallet), `dashboard` (CP-05), `audit` (CP-68 / SA-09 page and export) |
| SRS pages | CP-01 Public website, CP-02 Subscription wizard (UI), CP-03 Login & password recovery (UI), CP-04 Branch selection (UI), CP-05 Dashboard, CP-65 Profile, CP-66 Wallet (UI), CP-67 Support, CP-68 Audit log; SA-09 Audit log, SA-21 Profile; the platform support desk (SA side of CP-67); workflow WF-01 (onboarding, steps 1 and 3) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), the prototypes and the architecture pack. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.2 — 2026-10-24 (§18 change request LIS-CR-007 P-FRM: two-part mobiles in the wizard, keep-address strip on the sign-in pages) · 1.1 — 2026-10-20 (§17 change request LIS-CR-005: the wizard's result and SMS carry the laboratory address `{base}/lab/{slug}`; CP-65 shows it) · 1.0 — 2026-10-09 |
| Implemented in | `src/Platform.Domain/Account`, `src/Platform.Contracts/Account`, `src/Platform.Application/Account`, `src/Platform.Infrastructure/Persistence/Account` (+ `Account.migration.sql`), `src/Platform.WebApi/Controllers/Account`, `frontend/src/app/features/{public,account}`, `frontend/src/app/features/auth` (recovery, links), `frontend/src/app/features/home` (dashboard redirect), `frontend/src/app/core/api/account.contracts.ts`, `frontend/public/i18n/{public,account}.{en,ar}.json` |

> **Preferences (2026-10-14).** Beside the language (A1), the member's row density of the results grid — `resultsDensity`
> `comfortable` | `compact`, null = default — is stored on the membership of the session's tenant
> (`TenantMembership.PreferredResultsDensity`, migration `MembershipResultsDensity`, CHECK domain, code TEN-014) through the same
> `PUT /api/v1/account/preferences` (each value given is saved; at least one is required) and returned in `AuthProfile.resultsDensity`.

## 1. Purpose and policy basis

A laboratory finds Sky LIS on the public site, subscribes on its own (WF-01 step 1), signs in, binds the session to a
branch and lands on the branch's day at a glance. Every signed-in user manages their own account (language, password),
the laboratory administrator sees the laboratory's subscription details, wallet, support tickets and audit trail; the
platform staff answer the support tickets of every laboratory and read the platform's own audit trail. Policy basis:
SRS CP-01…CP-05, CP-65…CP-68, SA-09, SA-21, WF-01, BR-13 (delays), BR-15 (nothing erased); architecture pack D-01…D-04
(sessions, opaque tokens, credential envelope, deny by default), 02 §6.1 (password policy, decided by the user on
2026-10-09 for every credential: minimum from the parameters, default 8, maximum 128, no composition rules, must not
contain the username), 01 §4.4 (RLS) and MODULES.md §1/§5.

## 2. Scope and out of scope

In scope:

- **Frontend** of the public site (CP-01) and the four-step subscription wizard (CP-02) over the platform module's
  anonymous API; password recovery (CP-03) over the foundation's `AuthStore`; branch selection (CP-04) over the
  foundation's `select-branch` and the organization module's selectable-branches API, with `branchGuard`; the
  dashboard (CP-05); the profile (CP-65 / SA-21); the wallet view (CP-66) over the platform module's tenant wallet
  API; support (CP-67) for laboratories and the platform desk; the audit log (CP-68 / SA-09) over the foundation's
  audit API with CSV export and chain verification.
- **Backend** of: the `SupportTicket` aggregate and its desk; self-service preferences (language) and password change;
  the branch dashboard query (raw SQL over the wave-2 tables, tolerant of their absence); the own-profile query
  (user, membership, laboratory record); the audit actors list and the CSV export.

Out of scope (owned elsewhere or later): the subscription, wallet, countries and governorates endpoints (platform
module `plat`); the selectable-branches endpoint (`org`); recovery and branch selection endpoints (foundation); result
KPIs, the delayed-tests table and the precise delay rule BR-13 (Results/Reporting, wave 3, extend the same response);
editing the user's display name, mobile and e-mail (the organization module's user administration, CP-38; a self-service
change of the recovery mobile needs its own OTP proof, §16 Q4); "remember me" (no web storage, D-01: the refresh cookie
already survives a browser restart for its sliding lifetime); notifications to the laboratory when the desk answers
(a policy on `SupportTicketReplied`, later, through `INotificationSender`).

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Public website, landing page (الموقع العام) | `/welcome` (anonymous) |
| Subscription, onboarding wizard (الاشتراك) | `/welcome/subscribe`; the platform module's `SubscriptionRequest` |
| Branch of the session (فرع الجلسة) | `RefreshSession.BranchId` (foundation), `AuthStore.currentBranch()` |
| Dashboard, day at a glance (لوحة المتابعة) | `BranchDashboard` (`GET /api/v1/dashboard/branch`) |
| Profile (الملف الشخصي) | `MyAccountProfile` (`GET /api/v1/account/profile`) |
| Ticket, support request (تذكرة دعم) | `SupportTicket` aggregate (`saas.support_tickets`), number `TKT-yyyy-nnnnn` |
| Reply (رد) | `SupportTicketReply` entity (`saas.support_ticket_replies`) |
| Support desk (مكتب الدعم) | the platform tenant's view of every laboratory's tickets (`/admin/support`) |
| Audit log (سجل التدقيق) | foundation's unified `audit.field_changes` + `audit.audit_trail` |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `support.view` | list and read the laboratory's tickets with their replies |
| `support.create` | open a ticket, reply, reopen a resolved ticket, close a ticket (laboratory tenants only) |
| `support.manage` | the platform desk: list and read every laboratory's tickets, reply as the platform, start, resolve, close (platform tenant only) |
| `wallet.view` / `wallet.create` | read the laboratory's wallet / request a top-up (platform module's API; this module only renders it) |
| `dashboard.view` | the branch dashboard |
| `audit.view` | the audit log, the actors list, chain verification |
| `audit.export` | the CSV export of the audit log |
| any authenticated actor | own profile, language preference, password change (`[RequireAuthenticatedActor]`) |

Actors: anonymous visitor / laboratory manager (CP-01, CP-02, CP-03), every signed-in user (CP-04, CP-05, CP-65),
laboratory administrator (CP-66, CP-67, CP-68), platform staff (SA-09, SA-21, support desk). Deny by default.

## 5. Aggregates

- **SupportTicket** (root, platform-level table `saas.support_tickets`, explicit `TenantId` = the laboratory): `Number`
  (`TKT-yyyy-nnnnn`, per laboratory and year from `IReferenceNumbers` kind `ticket`), `Subject` (1–200), `Description`
  (1–4000), `Priority` (Low | Normal | High), `Status` (Open | InProgress | Resolved | Closed), `OpenedByUserId` +
  `OpenedByName`, `OpenedAtUtc`, `LaboratoryNameEn/Ar` (snapshot at opening for the desk), `LastActivityAtUtc`,
  `ResolvedAtUtc`, `ClosedAtUtc`, `ClosedByUserId`, `RowVersion` (xmin).
  - **SupportTicketReply** (owned entity, `saas.support_ticket_replies`): `Id`, `AuthorUserId`, `AuthorName`, `Text`
    (1–4000), `AtUtc`, `FromPlatform`. At most 500 replies per ticket.
- **Storage decision** (asked by MODULES.md for this module): a `saas` table with an explicit `tenant_id`, not an RLS
  tenant table. The desk reads and writes every laboratory's tickets; with an `app` table that would need either an
  elevated read (the elevation allow-list holds only `SessionPrincipalQuery.cs`, gate
  `Elevation_happens_only_at_allow_listed_sites…`) or a second permissive policy on a tenant table (weakening RLS, which
  is forbidden), and the tenant stamp would refuse the desk's writes (`TENANT-403`). Isolation is therefore enforced
  twice: (1) the repository scopes every read to the current tenant unless the current tenant is the platform tenant
  (one place, `SupportTicketRepository.Scoped()`, also behind `GetForUpdateAsync`), (2) a hand-written RLS policy
  `support_desk_scope` on both tables in `Account.migration.sql` (`tenant_id = current tenant OR current tenant =
  platform tenant OR bypass`), so even a handler bug cannot leak a ticket across laboratories.
- No other aggregate. Preferences use the foundation's `TenantMembership.SetPreferredLanguage`; the password change uses
  `PlatformUser.ChangePassword` / `RecordFailedPasswordCheck` and `RefreshSession.Revoke`.

## 6. State table (SupportTicket)

| State | Meaning | Editable | Terminal | Visible to |
| --- | --- | --- | --- | --- |
| Open | raised, nobody at the platform has taken it yet | replies | no | the laboratory (`support.view`), the desk |
| InProgress | the desk is working on it | replies | no | same |
| Resolved | the desk considers it answered | replies (a laboratory reply reopens it) | no | same |
| Closed | finished; kept for the record (BR-15) | nothing | yes | same |

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | Reason | Events |
| --- | --- | --- | --- | --- | --- | --- |
| S1 | — → Open | Open ticket | laboratory (`support.create`) | not the platform tenant (SUP-020); subject 1–200 (SUP-001), description 1–4000 (SUP-002), priority (validator) | — | `SupportTicketOpened` |
| S2 | Open/InProgress → same; Resolved → Open | Reply (laboratory) | `support.create` | not Closed (SUP-010); text 1–4000 (SUP-003); ≤ 500 replies (SUP-012) | — | `SupportTicketReplied` (+ `SupportTicketReopened` from Resolved) |
| S3 | Open → InProgress; InProgress/Resolved → same | Reply (desk) | platform tenant (SUP-403) + `support.manage` | not Closed (SUP-010); text; ≤ 500 replies | — | `SupportTicketReplied` (+ `SupportTicketStarted` from Open) |
| S4 | Open → InProgress | Start | desk (`support.manage`) | state Open (SUP-011) | — | `SupportTicketStarted` |
| S5 | Open/InProgress → Resolved | Resolve | desk (`support.manage`) | state Open or InProgress (SUP-011) | optional note (stored as a desk reply) | `SupportTicketResolved` |
| S6 | Resolved → Open | Reopen | laboratory (`support.create`) | state Resolved (SUP-011) | — | `SupportTicketReopened` |
| S7 | Open/InProgress/Resolved → Closed | Close | laboratory (`support.create`) or desk (`support.manage`) | not Closed (SUP-010) | — | `SupportTicketClosed` |
| A1 | — | Change language | any authenticated actor | `en` or `ar` (validator; TEN-0xx from the membership) | — | field change on the membership (audited) |
| A2 | — | Change password | any authenticated actor | not locked (ACC-011); current password correct (ACC-010, counts toward the lockout); new password policy (ACC-012 length from `passwordMinLength`, never below the platform floor; ACC-013 contains the username; ACC-014 same as the current) | — | `PlatformUserPasswordChanged`; other sessions `RefreshSessionRevoked(PasswordChanged)` |

## 8. Invariants

| Id | Statement | Enforced where | Code |
| --- | --- | --- | --- |
| INV-SUP-001 | A ticket belongs to exactly one laboratory, never to the platform tenant | aggregate factory | SUP-020 |
| INV-SUP-002 | A ticket number is unique per laboratory | unique index `(tenant_id, number)`; per-tenant counter | UNIQUE-409 |
| INV-SUP-003 | A closed ticket changes no more | aggregate state guards | SUP-010 |
| INV-SUP-004 | A laboratory sees only its own tickets; only the platform tenant sees all | repository scope + RLS policy `support_desk_scope` | 404 (SUP-404) |
| INV-SUP-005 | At most 500 replies per ticket | aggregate | SUP-012 |
| INV-ACC-001 | A password change needs the current password; wrong attempts count toward the lockout of sign-in (security rule SEC-LOCK-02 of LIS-INT-FND-001 §6a, decided 2026-10-12) | handler (hasher + `RecordFailedPasswordCheck`) | ACC-010 / ACC-011 |
| INV-ACC-002 | A changed password ends every other session of the user; the current session stays | handler (`ListLiveForUserAsync` minus the current session) | — |

## 9. Segregation-of-duties rules

None. A ticket has no approval; the desk and the laboratory are different tenants by construction.

## 10. Approval gates

None.

## 11. Events and policies

```text
SupportTicketOpened   { TicketId, TenantId, Number, Priority, OpenedByUserId }          S1
SupportTicketReplied  { TicketId, TenantId, Number, ReplyId, FromPlatform, AuthorUserId } S2, S3
SupportTicketStarted  { TicketId, TenantId, Number, ActorUserId }                         S3, S4
SupportTicketResolved { TicketId, TenantId, Number, ActorUserId }                         S5
SupportTicketReopened { TicketId, TenantId, Number, ActorUserId }                         S2, S6
SupportTicketClosed   { TicketId, TenantId, Number, ActorUserId, ByPlatform }             S7
```

Policies: none in this module yet. Planned: "when `SupportTicketReplied` from the platform, then notify the
laboratory administrator" and "when `SupportTicketOpened`, then notify operations" (templates `support-reply`,
`support-opened`, to be added to the platform's notification templates). Events go to the outbox; a ticket is not a
tenant aggregate, so its events are not in the per-tenant hash chain (its field changes are audited).

Security events (02 §7 ledger): `PASSWORD_CHANGED`, `PASSWORD_CHANGE_FAILED` (detail `wrong_password` / `locked`),
`AUDIT_EXPORTED` (detail: row count).

## 12. Data classification and retention

| Data | Class | Retention |
| --- | --- | --- |
| Support tickets and replies | Business, may contain personal data typed by users | with the client contract (assumed 5 years after closing); never deleted by users (BR-15) |
| Preferences | Personal | with the membership |
| Audit export files | Confidential | not stored by the server (generated per request) |
| Dashboard figures | Derived | not stored |

## 13. Reference data and configuration

- Settings read: `passwordMinLength` (tenant; the platform tenant's value for SA-21), `defaultCurrency` (dashboard money).
- Configuration: `Lis:Platform:PortalDomain` (portal URL of the laboratory, default `skylis.app`, the platform module's key),
  `Lis:Security:Session:PasswordMinLength` (platform floor), lockout threshold and duration (`SessionPolicy`).
- Reference numbers: kind `ticket`, prefix `TKT`.

## 14. Registers, reports and exports

| Register | Filters | Columns | Permission / export |
| --- | --- | --- | --- |
| Support tickets (CP-67) | status, priority, search (number, subject), mine | number, subject, priority, status, opened by, opened, last activity | `support.view` |
| Support desk (admin) | laboratory, status, priority, search (number, subject, laboratory) | number, laboratory, subject, priority, status, opened, last activity | `support.manage` |
| Audit log (CP-68 / SA-09) | period, user, entity type, entity id | time, user, action, entity, details | `audit.view`; CSV (`audit.export`, newest 5 000 rows, UTF-8 with BOM, formula-safe cells) |
| Dashboard (CP-05) | date (local day of the browser, `utcOffsetMinutes`) | registrations, collected today (net of refunds), samples received, pending results, unverified backlog, delayed tests, rejected samples, wallet; 7-day registrations; delayed tests | `dashboard.view`; wallet only with `wallet.view` |

**Dashboard response contract (stable; the Reporting agent extends it, never renames):** `GET /api/v1/dashboard/branch?date=yyyy-MM-dd&utcOffsetMinutes=n`
→ `{ branchId, date, fromUtc, toUtc, currencyCode, kpis: { registrations, collectedToday, samplesReceived, pendingResults,
unverifiedBacklog, delayedTests, rejectedSamples, walletBalance, walletCurrency }, registrationsByDay: [{ date, count }],
delayedTests: [{ visitId, accession, patientName, testName, expectedAtUtc, delayMinutes, status }] }`. A `null` figure
means "source not available yet" (table absent → PostgreSQL 42P01, or not built: results), never zero. Definitions:
registrations = visits of the session branch registered in the day; collected = posted payments − refunds of the day on
the branch's treasuries (else the visit's branch); samples received = samples of the branch's visits received in the
day; rejected = samples in status Rejected of the branch's visits registered in the day; wallet = the laboratory's
wallet balance (`saas.wallets`). Pending results, unverified backlog, delayed tests: `null` / empty until Results.

## 15. Notifications and escalations

None sent by this module yet (§11 planned policies). The wizard's and recovery's one-time codes are sent by the
platform and foundation modules.

## 16. Acceptance scenarios, open questions and assumptions

Scenarios (tests named after them):

- S-S1 a laboratory user opens a ticket → Open, number `TKT-2026-00001`; the platform tenant cannot open one (SUP-020).
- S-S2 the laboratory replies; a reply on a Resolved ticket reopens it; a closed ticket refuses replies (SUP-010).
- S-S3 the desk replies to an Open ticket → InProgress; the reply is marked from the platform.
- S-S4/S5 the desk starts and resolves; resolving a Closed ticket is refused (SUP-010), starting an InProgress one (SUP-011).
- S-S6 the laboratory reopens a resolved ticket; reopening an Open one is refused (SUP-011).
- S-S7 either side closes; closing twice is refused (SUP-010).
- S-ISO laboratory B cannot see or load laboratory A's ticket (repository and RLS); the platform tenant sees both.
- S-A1 the language is saved on the membership and returned by the next refresh profile.
- S-A2 a wrong current password is refused (ACC-010) and counted; five wrong attempts lock (ACC-011); a correct change
  revokes the other sessions and keeps the current one; a password containing the username (ACC-013), too short
  (ACC-012) or unchanged (ACC-014) is refused; a plaintext password is refused (CRED-005).
- S-D1 the dashboard answers with nulls when the wave-2 tables do not exist, and with counts for the session branch only
  when they do; without a selected branch it is refused (TENANT-004).
- S-AU1 the audit export returns CSV with a header row for `audit.export` and 403 without it.

| # | Question | Assumed answer built on |
| --- | --- | --- |
| Q1 | SRS CP-65 says "update credentials via OTP"; the brief says current password + new password. | Current password (re-authentication) + new password through the envelope; no OTP. An OTP step can be added before the change with the foundation's `IOtpService` (purpose `password-change`). |
| Q2 | Which minimum length applies to a self-service change? | The tenant's `passwordMinLength` (CP-57; the platform tenant's for staff), never below the platform floor `Lis:Security:Session:PasswordMinLength`. |
| Q3 | "Collected today" (prototype: payments of the day) — gross or net of refunds, by visit branch or treasury branch? | Net of refunds, on the treasuries of the session branch (the cash-closing view), falling back to the visit's branch when a payment has no treasury. |
| Q4 | May users edit their display name, mobile and e-mail on CP-65? | No: read-only on the profile; the administrator edits them (CP-38). A self-service mobile change would need an OTP to the new number. |
| Q5 | Rejected samples "today" — the samples table has no rejection time in the wave-2 contract. | Samples in status Rejected among the branch's visits registered that day, until Samples exposes `rejected_at_utc`. |
| Q6 | Dashboard day boundaries | The browser's local day (`utcOffsetMinutes`), until the tenant time zone (country) is exposed. |
| Q7 | Who sees a laboratory's tickets? | Every member with `support.view` (not only the opener); "mine" is a filter. |
| Q8 | Ticket attachments (screenshots)? | Not in this release (no shared file service for saas rows); text only. |
| Q9 | SA-09: a cross-tenant audit view for the platform? | No (foundation A8): the platform staff see the platform tenant's own trail. |
| Q10 | The wizard's password minimum is the platform parameter; the anonymous page cannot read it. | The page states the default (8) and maps `CLI-040` to "too short"; the platform module may expose the value with the start response. |
| Q11 | Laboratory details on CP-65 (portal URL, plan, country) | Read by `LaboratoryProfileLookup` (this module's read port) from the platform's mapped `saas.tenants`/`plans`/`countries` (wave 1) with the portal domain of `IPortalAddressOptions`; no client record or no table yet (42P01) → the page shows the tenant id and a "not available" note. |
| Q12 | The password change carries two credential fields sealed under one challenge (02 §3.3 "one challenge per request, the same nonce on every field"); the foundation's `CredentialTransportFilter` consumes the nonce once per field, so the second field is refused (CRED-003). | The endpoint follows the specification; the integrator fixes the filter to consume the nonce once per request (report). Until then the change fails with CRED-003. |
| Q13 | Must-change at next sign-in (CP-38): where does the user land? | `passwordChangeGuard` sends the user to the profile (`?mustChange=1`) until the password is changed; it reads the own profile once per session. Exposing `mustChangePassword` on `AuthProfile` would save that call. |

## 17. Change request 2026-10-20 — laboratory address in the wizard result (LIS-CR-005, package P-API)

- The subscription wizard's result (`SubscriptionCompletedResponse.portalUrl`) and its `portal-url` SMS carry the laboratory's
  sign-in address `{Lis:Platform:PortalBaseUrl}/lab/{slug}` (previously `https://{slug}.skylis.app`). The SPA's "Go to sign in"
  opens `/lab/{slug}?username=…` (package P-SPA).
- CP-65 (profile, laboratory card) shows the same address (`LaboratoryProfile.portalUrl`).
- No new endpoint, code or setting in this module; the base URL and its ConfigGuard rule are PLT-001 §18.

## 18. Change request 2026-10-24 — mobiles in two parts and "keep this address" (LIS-CR-007 §3, package P-FRM, reviewer points C-05 and C-01)

Source: `docs/build/CR-2026-10-24_Review-Quick-Wins.md` §3 and the review response LIS-RC-001 points C-05 and C-01. Built in
slot `pat`; front end only (no server change, no new code, no new endpoint).

| Item | Decision |
| --- | --- |
| CP-02 mobiles (C-05) | The laboratory mobile and the manager mobile use the shared `app-mobile-input` (`shared/forms/mobile-input`, see LIS-INT-PAT-001 §19): the dial code of the country chosen in the wizard is shown read-only beside the number, a typed trunk 0 is dropped, pasted `+20…` / `0020…` forms are read, and the request carries the E.164 form (`+201012345678`), which the server already accepts (GEO-010 / GEO-014). `countryMobileValidator` is unchanged, so a wrong length still names the country's digit count (LIS-VR-002 C-08); the hint reads "{length} digits after {dial}, without the leading 0". The same control is used by the Super Admin clients form (SA-04) and the user editor (CP-38). |
| CP-03 keep the address (C-01) | Under the card of a found laboratory (`/lab/<slug>`) a compact strip reads "Keep this address: Ctrl+D (⌘+D on Apple devices) bookmarks it" with the address, *Copy address* (`navigator.clipboard`, toast "Copied"; a refused copy says so) and *Hide*. *Hide* is a signal of the page: it lasts until the page is left and is never stored. Not shown on the Super Admin sign-in nor on "address not found". Keys `auth.keepAddress.*`; component `features/auth/ui/keep-address.component.ts`; the frame has a slot under the card (`authAfterCard`). |
| Find your laboratory (C-01) | Once the typed text has the shape of an address, a hint under *Continue* names the full address it opens (`<origin>/lab/<slug>`) with the bookmark keys and *Copy address*. The page still does not look the laboratory up (the laboratory page answers whether it exists). |
| No device storage | Neither page writes anything on the device (no `localStorage`, `sessionStorage`, IndexedDB or cookie; security rule of the SPA). The "recent laboratories on this device" idea of LIS-RC-001 is **not built**: it would need device storage. |

| # | Question | Assumption built on |
| --- | --- | --- |
| Q-K1 | Should the strip come back after *Hide* on the next visit? | Yes: nothing is stored, so every visit of the sign-in page shows it again until the user hides it. |
| Q-K2 | May the sign-in pages remember the last laboratory to skip the find page? | No (no device storage); the bookmark is the way back. To be revisited only with a security decision on device storage. |
