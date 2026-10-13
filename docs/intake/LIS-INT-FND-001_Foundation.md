# LIS-INT-FND-001 — Platform foundation (wave 0, lean intakes per capability, Document 03 §11.1)

| Item | Value |
| --- | --- |
| Slot / modules | `found` — `platform`, `identity`, `audit`, `notifications`, `settings` (MODULES.md §2) |
| SRS pages | CP-03 (login & password recovery with OTP), CP-04 (branch selection), CP-57 / SA-19 (parameters), CP-68 / SA-09 (audit log), cross-cutting "Audit & notifications" |
| Status | Derived by the development team from the Business SRS and the architecture pack (01 §4.5–4.8, §7; 02 §4–§7; 03 §7–§8). **For review by the business owner**; assumptions in §8. |
| Version | 1.1 — 2026-10-12 (§6 `SET-003` unknown time zone; §6a lockout rule SEC-LOCK-01/02 and assumption A9, after validation LIS-VR-002; §6b idle timeout SEC-IDLE-01…05 and A10, validation F-01) · 1.0 — 2026-10-09 |
| Implemented in | `src/Platform.Domain/{Identity,Settings}`, `src/Platform.Application/{Abstractions,Identity,Audit,Settings}`, `src/Platform.Infrastructure/{Persistence/Audit,Persistence/Settings,Persistence/Outbox,Persistence/Identity,Notifications,Security,Audit}`, `src/Platform.WebApi/Controllers/{Identity,Audit,Settings}`, `tools/Platform.DevTools`, `frontend/src/app/core/{auth,api}` |
| Migration | `20261009100252_Foundation` (`deploy/postgres/migrations.sql` regenerated) |

## 1. Platform tenant

1. **Need.** National Technology staff administer the platform from the Super Admin portal; they are members of the platform tenant `TenantIds.Platform` (`00000000-0000-0000-0000-000000000001`) and hold every permission of the catalogue through the role "Platform Administrator". RLS, the query filter and sign-in work unchanged for this tenant (MODULES.md §1).
2. **Records.** `PlatformUser`, `TenantMembership` (platform tenant), `Role` "Platform Administrator" (keys = `PermissionCatalog.Keys`, brought up to date on every seed). No new life cycle.
3. **Who.** Seeding is an operator action (`seed-platform-admin <username> <password>` in `tools/Platform.DevTools`, backed by `Platform.Application.Identity.PlatformAdministratorSeeder`, idempotent). Signing in needs no permission; everything afterwards is decided by the role.
4. **Gates.** None.
5. **Events.** The existing `PlatformUserRegistered`, `TenantMembershipCreated`, `RoleCreated` / `RoleChanged`.

## 2. Branch of the session (CP-04)

1. **Need.** After sign-in a user works in exactly one of the branches assigned to their membership; every branch-stamped transaction of the session carries that branch and the choice survives token refresh. A membership with a single branch needs no selection step.
2. **Records.** `RefreshSession.BranchId` (nullable; inherited by every successor of the rotation family; `identity.refresh_sessions.branch_id`). `TenantMembership.BranchIds` is the assignment (unchanged).
3. **Who.** Any authenticated actor for their own session (`[RequireAuthenticatedActor]`): `POST /api/v1/auth/select-branch {branchId}` → the refreshed `AuthProfile`. The branch must be one of the membership's `BranchIds`, else `403 TENANT-005 BranchNotAssigned`. Modules call `BranchScope.Require(ICurrentUser)` and get `403 TENANT-004 BranchRequired` while no branch is selected. `ICurrentUser.BranchId`, `SessionPrincipal.BranchId`, `AuthProfile.BranchId/BranchIds`, `GET /auth/me` expose it.
4. **Gates.** None.
5. **Events.** `RefreshSessionBranchSelected(SessionId, UserId, TenantId, BranchId)` (session aggregate; not ledgered, the security event `SESSION_CONTEXT_CHANGED` with detail `branch:<id>` is).

## 3. Audit (CP-68 / SA-09, 01 §4.8, 03 §8)

1. **Need.** Every change to a tracked record is visible with user, time and before/after; every domain event is kept in a tamper-evident, per-tenant ledger; authentication and configuration events are kept with the request context. Nothing is ever edited or erased (BR-15).
2. **Records.**
   - `audit.field_changes` (tenant_id, id, entity_type, entity_id, action Created|Modified|Deleted, actor_user_id, actor_name, at_utc, correlation_id, changes jsonb `[{field,before,after}]`, reason) — written by `FieldChangeInterceptor` in the same transaction for every aggregate, entity and `IFieldChangeTracked` row (tenant settings); never for sessions, keys, nonces, one-time codes, counters, outbox or audit rows; skipping the row version, the audit stamps, key columns and credential material; owned values folded as `Navigation.Property`; the reason copied from a `Reason`/`DecisionReason`/`Comment` property of the event the transition raised.
   - `audit.audit_trail` (tenant_id, sequence, event_id, event_type, aggregate_type, aggregate_id, actor_user_id, at_utc, correlation_id, payload jsonb, prev_hash, entry_hash) — appended by `AuditTrailInterceptor` from the events of tenant aggregates, per tenant under `pg_advisory_xact_lock(hashtext('audit_chain:' || tenant_id))`, SHA-256 chained over the stored columns (time truncated to microseconds, payload in jsonb's canonical text, computed by the engine before the insert).
   - `audit.security_events` (id, tenant_id nullable, name, severity, actor_user_id, client_ip, user_agent_hash, correlation_id, detail, at_utc) — appended by `SecurityEventLedger` (which also writes the structured log) as a single statement on the scope's connection, so a refusal path keeps its row.
   - All three: append-only trigger (`audit.refuse_change()` on UPDATE/DELETE, every role), `lis_app` holds SELECT/INSERT only (schema.sql), FORCE RLS; `security_events` has a hand-written policy (reads by tenant; a pre-authentication scope may insert for any tenant, an authenticated scope only for its own or none).
3. **Who.** `audit.view`: `GET /api/v1/audit/entries?from&to&userId&entityType&entityId&page&pageSize` (unified, newest first, exact paging by merging both tables) and `GET /api/v1/audit/verify-chain` → `{ok, checked, firstBrokenSequence}`.
4. **Gates.** None (read-only).
5. **Events.** None raised; every event of every module is consumed. Interceptor order (gated): TenantConnection → TenantStamp → AuditStamp → FieldChange → AuditTrail → Outbox (the trail reads the events while they are still on the aggregates; the outbox drains them last).

## 4. Outbox processor (01 §7)

1. **Need.** Cross-module reactions are policies dispatched after the commit that raised the event, with retries, back-off and a dead letter; several replicas share the queue without a leader.
2. **Records.** `ops.outbox_events` already carried `claimed_until_utc`, `attempts`, `next_attempt_at_utc`, `processed_at_utc`, `dead_lettered_at_utc`, `trace_parent` and the partial index `ix_outbox_events_pending`; nothing was added.
3. **Who.** `OutboxProcessor` (Infrastructure, hosted by `OutboxHostedService` in the WebApi; `Lis:Outbox:Enabled` default true, `PollInterval` 2 s, `BatchSize` 50, `Lease` 30 s, `MaxAttempts` 10): claims with `FOR UPDATE SKIP LOCKED` under the lease, resolves the type through `DomainEventRegistry` (every `IDomainEvent` of Platform.Domain by full name), deserialises with System.Text.Json, opens a scope bound to the event's tenant (`ForTenant`, no elevation), dispatches to every `IDomainEventHandler<TEvent>`, marks processed; a failure counts an attempt and reschedules at `2 s · 2^attempt` (max 1 h); the tenth dead-letters. Policies: `sealed class XPolicy : IDomainEventHandler<TEvent>` under `Platform.Application/<Module>/Policies/`, registered by `AddApplication` (every closed implementation, scoped), idempotent on a natural key (gate `DomainEventPolicyTests`).
4. **Gates.** None.
5. **Events.** Consumes all.

## 5. Notifications and one-time codes (CP-02 backend, CP-03, SA-20)

1. **Need.** The platform sends one-time codes and messages through templates; a user who forgot their password proves possession of their registered mobile with a code and sets a new password; credentials change only after the code.
2. **Records.** `PlatformUser.Mobile` (normalised digits with optional `+`, unique among users that have one, `identity.platform_users.mobile`); `identity.otp_challenges` (id, purpose, target_hash, code_hash — both HMAC-SHA256 under purpose-bound subkeys of the master key —, issued/expires (5 min), attempts (max 5), consumed_at). Ports: `INotificationSender`, `INotificationTemplateSource` (defaults in code: `otp`, `password-recovery`, `portal-url`, `treasury-request`, `panic-value`, en/ar), `IOtpService` (`IssueAsync` / `VerifyAsync`). Defaults registered after the convention with `TryAdd`, so a module adapter (templates, SMS gateway) wins.
3. **Who.** Anonymous, rate-limited like sign-in (`credential-account` on the identifier, `credential-client` on the address): `POST /api/v1/auth/recovery/start {usernameOrMobile, language}` → always 202; `POST /api/v1/auth/recovery/complete {usernameOrMobile, code, newPassword (credential envelope)}` → 204, or `401 AUTH-013 OtpRejected` for every refusal. Completion replaces the hash, rotates the security stamp, clears the lockout and revokes every live session of the user. Security events `PASSWORD_RECOVERY_STARTED` / `_COMPLETED` / `_FAILED(reason)`.
4. **Gates.** None.
5. **Events.** `PlatformUserPasswordRecovered(UserId)`; `RefreshSessionRevoked(PasswordChanged)`.

## 6. Tenant parameters (CP-57; SA-19 defaults)

1. **Need.** Tenant behaviour switches read by every module at runtime and edited by the laboratory administrator; a key the tenant never set reads as the platform default.
2. **Records.** `app.tenant_settings` (tenant_id, key, value text, updated_at_utc, updated_by_user_id; RLS; field-audited). Catalogue `Platform.Domain.Settings.SettingKeys` — 25 typed descriptors (bool/int/string with range or closed set): `mandatoryMobile`, `mandatoryDoctor`, `mandatoryNationalId`, `similarNameWarning`, `discountCapPercent` 20, `accessionFormat` `ACC-{yyyy}-{nnnnn}`, `autoPrintLabels`, `autoPrintJobOrder`, `autoPrintReceipt`, `printUnverified`, `labelCopies` 1, `resultLanguage` en, `autoCollect`, `autoReceive`, `newBarcodeOnReject`, `reviewBeforeVerify`, `delayWarnPercent` 80, `delayLatePercent` 100, `sessionTimeoutMinutes` 30, `otpLength` 6, `passwordMinLength` 8, `trialDays` 14, `defaultCurrency` EGP, `defaultLanguage` en, `masterDataUpdatePolicy` propose.
3. **Who.** `settings.view`: `GET /api/v1/settings`; `settings.manage`: `PUT /api/v1/settings {settings:[{key,value}]}` (validates every key and value first: `422 SET-001` unknown key, `422 SET-002` invalid value, `422 SET-003` a time-zone name — non-blank, at most 64 characters — this host does not know, e.g. `Mars/Olympus`, so the field reads "This time zone is not known."; returns the effective list). The key `timeZone` (IANA, default `Africa/Cairo`) was added by round 2 R2-01; the catalogue holds 26 keys. Modules read through `ITenantSettings.GetAsync(SettingKeys.X)` (loaded once per scope).
4. **Gates.** None.
5. **Events.** None (field changes are the history).

## 6a. Password checks and lockout (02 §5.1, §6.1)

Rules (security; the tests named are the proof):

| Rule | Statement | Proof |
| --- | --- | --- |
| SEC-LOCK-01 | A checked, wrong password at sign-in increments `identity.platform_users.failed_password_attempts`; at the threshold (`SessionPolicy.LockoutThreshold`, default 5, `Lis:Security:Session:LockoutThreshold`) the account locks for `LockoutDuration` (default 30 min), then `AUTH-003`; the lockout ends by itself; a correct password resets the counter. An unknown account, an inactive or already locked account costs no attempt. | `AuthFlowTests.Five_wrong_passwords_lock_the_account_and_the_right_one_is_then_refused` |
| SEC-LOCK-02 | **A wrong *current* password on Change password (CP-65 / SA-21, `PUT /api/v1/account/password`) is a credential check like sign-in**: it feeds the same counter toward the same threshold and lockout (`ACC-010` below the threshold, `ACC-011` at it and while locked; sign-in then answers `AUTH-003`). Decided 2026-10-12 (assumption A9): a stolen session must not be able to guess the current password without limit. A malformed *new* password is refused before the check and costs no attempt. | `AccountFlowTests.S_A2_wrong_current_passwords_share_the_sign_in_counter_and_lock_the_account_as_sign_in_does`, `S_A2_five_wrong_current_passwords_lock_the_account` |
| SEC-LOCK-03 | No refusal says how many attempts remain (sign-in answers `AUTH-002` for every wrong password, so a guesser learns nothing); the profile page therefore states only the refusal (`ACC-010`) and the lock (`ACC-011`). | — |

## 6b. Idle timeout of the session (validation F-01, setting `sessionTimeoutMinutes`)

Rules (security; the tests named are the proof):

| Rule | Statement | Proof |
| --- | --- | --- |
| SEC-IDLE-01 | **The server is the authority.** A refresh of a live **browser** session whose last activity is older than the idle timeout of its tenant revokes the session (reason `Expired`: an idle end is a tenant-shortened sliding expiry; a new `Idle` member would need a migration for the enum CHECK, P-09), writes `SESSION_REVOKED` (info, detail `idle_timeout`; no reuse alarm, the family is untouched) and is refused `401 AUTH-017` (`AuthProblemCodes.SessionIdleTimeout`, "Your session ended after inactivity"). A later refresh with the same cookie is the ordinary `AUTH-012` of an ended session (R2-06). Machine sessions have no idle timeout. No migration. | `SessionIdleTimeoutFlowTests.An_idle_session_past_the_tenant_timeout_is_refused_with_AUTH_017_and_revoked_without_the_reuse_alarm` |
| SEC-IDLE-02 | **Last activity** = `RefreshSession.LastSeenAtUtc`: the sign-in or the rotation that created the live session, i.e. the last refresh (`RefreshSession.IsIdleAt`). Data requests do not touch the session row. | `RefreshSessionTests.A_session_is_idle_once_nothing_renewed_it_for_longer_than_the_timeout_and_a_rotation_starts_the_count_again_F_01`, `SessionIdleTimeoutFlowTests.A_session_renewed_inside_the_timeout_goes_on_however_long_it_lasts` |
| SEC-IDLE-03 | **The timeout** = the tenant's valid `sessionTimeoutMinutes` (1–720), else the platform tenant's, else 30 (`SettingDescriptor<T>.Resolve`, the rule of §6); for platform administrators the platform tenant's own value. It is read in the one elevated session query (`SessionPrincipalQuery`, two scalar subqueries on `app.tenant_settings`), so a change applies within the 10 s principal cache, and it reaches the SPA as `AuthProfile.sessionTimeoutMinutes` (sign-in, refresh, branch selection). | `SessionIdleTimeoutFlowTests.The_timeout_is_the_laboratory_value_else_the_platform_value_else_thirty_minutes`, `SettingKeysTests.Resolve_takes_the_tenant_value_else_the_inherited_one_else_the_default_F_01` |
| SEC-IDLE-04 | **An access token already issued stays valid until it expires** (15 minutes at most): tokens are not re-checked against the timeout; the next refresh is. | — |
| SEC-IDLE-05 | **Client** (`core/auth/idle-watcher.ts`, mounted by `provideIdleWatch()` in `app.config.ts`; dialog `IdleWarningComponent` in the application root). Activity = mouse, keyboard, touch, wheel, scroll or the tab becoming visible, shared by every tab through the session channel (`activity`, `renewed`). While the user is active the watcher renews the session with one refresh once the last renewal (any tab) is older than a quarter of the timeout (15 s … 1 min), so an active user is never idle on the server. The end is counted from the earlier of the last activity and the last renewal; a minute before it (half the timeout under two minutes) the dialog "Your session will end in N s — Stay signed in / Sign out" shows; movement in the same tab does not close it, *Stay* refreshes, activity in another tab closes it. At the end: sign-out and a full page load of `/login?reason=idle` ("Your session ended after inactivity") in every tab; a refresh refused with `AUTH-017` lands there too. | `idle-watcher.spec.ts` |

## 7. Tests

Domain: `SessionBranchTests`, `PlatformUserRecoveryTests`, `SettingKeysTests`. Architecture: interceptor order, savers allow-list, audit-table shape, append-only triggers, `DomainEventPolicyTests` (+ self-test). Integration: `AuditLedgerTests` (same-transaction writes, chain verification, tampering by update and by deletion, append-only for `lis_app` and the trigger for the owner, security events with request context and tenant visibility, unified paging), `OutboxProcessorTests` (delivery in tenant scope, back-off, dead letter), `TenantSettingsTests`. Functional: `SessionIdleTimeoutFlowTests` (§6b), `FoundationFlowTests` (platform principal, branch selection through refresh and refusal, auto-selected single branch, recovery end to end incl. plaintext and replay refusals, five wrong codes, audit endpoints with 403, settings endpoints with 422/403).

## 8. Assumptions and open questions

| # | Assumption built on | Question for the business owner |
| --- | --- | --- |
| A1 | An unrestricted membership (empty `BranchIds`) cannot select a branch through the foundation; the organization module (wave 1 `org`) either assigns branches explicitly or supplies the tenant's branch list for CP-04. | Does "unrestricted" mean "all branches of the tenant" for CP-04? |
| A2 | Recovery is by **mobile** only (SRS CP-03: "username or mobile for recovery"); e-mail recovery is not offered. | Confirm; e-mail would reuse the same `IOtpService` with `NotificationChannel.Email`. |
| A3 | Password policy for recovery: minimum 8 characters (configurable `Lis:Security:Session:PasswordMinLength`), maximum 128, must not contain the username; **no composition rules** (02 §6.1), although CP-02 lists upper/lower/digit/special for the subscription wizard. | Which policy applies platform-wide? |
| A4 | One-time codes: 6 digits, 5 minutes, 5 attempts, newest code supersedes the previous one (`Lis:Security:Session:Otp*`); the tenant keys `otpLength`/`passwordMinLength` are stored but not yet read by the identity flows, which run before a tenant is known. | Should tenant values override the platform values for tenant users? |
| A5 | Field changes of global aggregates (users) are recorded only when a tenant scope is resolved; sign-in counters and lockouts are covered by security events, never by field history (no credential material in the audit). | — |
| A6 | Events of global aggregates (users, sessions) are not in the per-tenant hash chain; security events are their ledger. | — |
| A7 | Audit export (CP-68 "export", `audit.export`) is left to the `acc` module's page; the API offers paging only. | Export format? |
| A8 | The audit log of SA-09 is the same endpoint under the platform tenant; platform administrators do not see other tenants' audit rows (RLS). A cross-tenant platform view would need an elevated read port. | Is a cross-tenant audit view required for SA-09? |
| A9 | **Decided 2026-10-12** (validation LIS-VR-002, open point "failed attempts on change password"; the security spec did not say): a wrong current password on Change password counts toward the sign-in lockout (SEC-LOCK-02). | Confirm; the alternative (a separate, unlimited counter) would let a stolen session guess the password. |
| A10 | Idle timeout (§6b, validation F-01): the idle end reuses the revocation reason `Expired` (no migration); the client renews an active session at most once a minute, so the effective idle period is between the timeout minus that interval and the timeout; the warning needs an explicit answer (movement in the same tab does not close it). | Confirm; should an idle end lock the screen (re-enter the password in place) instead of signing out? |
