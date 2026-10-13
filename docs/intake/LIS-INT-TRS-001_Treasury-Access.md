# LIS-INT-TRS-001 — Treasury access (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `treasury` (Sky LIS Client Portal, pages CP-28 Treasuries, CP-29 Treasury access; business rule BR-27) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001) because the TASK 5 intake slot was left blank. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.1 — 2026-10-12 (validation v0.5.0 D-05, O-4, O-5, O-10; see the revision history at the end) |
| Implemented in | `src/Platform.Domain/Treasury`, `src/Platform.Application/Treasury`, `src/Platform.Infrastructure/Persistence/Treasury`, `src/Platform.WebApi/Controllers/Treasury`, `frontend/src/app/features/treasury` |

## 1. Purpose and policy basis

A cash transaction is accepted only on a **treasury** (cash drawer) that the cashier is approved to use **from the
device they are working on** (BR-27). A cashier therefore asks for access to a treasury from a device; a supervisor
who is not the requester decides; an approval can later be revoked. The record of request, decision and revocation
is the control that the finance function audits.

## 2. Scope and out of scope

In scope: the treasury catalogue of a tenant (code, bilingual name, branch, active flag) and the life cycle of a
treasury access request (request → approve / reject / cancel, approve → revoke). The approval is the fact that
other modules consult.

Out of scope (later intakes): cash sessions, shifts and the payment itself (the Payments module reacts to the events
of this module); device fingerprinting beyond the browser-minted device id (one per user and browser profile, Q1); notification channels.

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Treasury, cash drawer, الخزينة | `Treasury` aggregate (`app.treasuries`) |
| Access request, طلب صلاحية الخزينة | `TreasuryAccessRequest` aggregate (`app.treasury_access_requests`) |
| Device | `DeviceId` (opaque id ≤ 128 chars minted once per browser profile) + optional `DeviceLabel` (≤ 100) |
| Request number | `TRQ-yyyy-nnnnn`, per tenant and year (`IReferenceNumbers`, `app.reference_counters`) |
| Supervisor / shift manager | any user holding `treasury.approve` in the tenant |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `treasury.view` | list treasuries; list and read access requests of the tenant (read-only access) |
| `treasury.create` | request access for oneself from the current device |
| `treasury.approve` | approve, reject (pending) and revoke (approved) requests of other users |
| `treasury.cancel` | cancel one's own pending request |
| `treasury.manage` | create, rename, deactivate and reactivate treasuries |

Permissions are granted through tenant roles (`Role` aggregate, `app.roles`; a membership carries one role).
Deny by default: a command without a `[RequirePermissionPolicy]` fails the architecture gate.

## 5. Aggregates

- **Treasury** (root): `Code` (≤ 16, upper-case `[A-Z0-9-_]`, unique per tenant), `Name` (`LocalizedText` en/ar),
  `BranchId` (reference by id), `IsActive`, `RowVersion` (xmin).
- **TreasuryAccessRequest** (root): `Number`, `TreasuryId` + `BranchId` (reference), `RequestedByUserId` (actor
  anchor), `DeviceId`, `DeviceLabel`, `Status`, `RequestedAtUtc`, `Approval` (value object: approver id, time,
  comment), `DecidedByUserId`, `DecidedAtUtc`, `DecisionReason`, `RowVersion`. No children.

## 6. State table

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Pending | awaiting a decision | none (a request is immutable once submitted) | no | no | `treasury.view` |
| Approved | the device may use the treasury | none | yes | no (revocable) | `treasury.view` |
| Rejected | refused with a reason | none | yes | yes | `treasury.view` |
| Revoked | approval withdrawn with a reason | none | yes | yes | `treasury.view` |
| Cancelled | withdrawn by the requester | none | yes | yes | `treasury.view` |

A request is **live** while Pending or Approved.

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | — → Pending | Request | `treasury.create` | treasury exists (TRS-404) and is active (TRS-010); the treasury's branch is one of the requester's branches, or the requester works in every branch (TRS-015, Q3); device id present (TRS-011); no live request for the same user, treasury and device (TRS-012) | — | — | — | `TreasuryAccessRequested` | no |
| T2 | Pending → Approved | Approve | `treasury.approve` | state Pending (TRS-013) | SOD-TRS-001 | G1 | optional comment | `TreasuryAccessApproved` | no |
| T3 | Pending → Rejected | Reject | `treasury.approve` | state Pending (TRS-013) | SOD-TRS-001 | — | **required** (TRS-020) | `TreasuryAccessRejected` | no |
| T4 | Approved → Revoked | Revoke | `treasury.approve` | state Approved (TRS-014) | — | — | **required** (TRS-021) | `TreasuryAccessRevoked` | no |
| T5 | Pending → Cancelled | Cancel | `treasury.cancel` | state Pending (TRS-013); actor is the requester (TRS-030) | — | — | optional | `TreasuryAccessCancelled` | no |
| T6 | — → Treasury active | Create treasury | `treasury.manage` | code valid (TRS-001), unique in tenant (TRS-100); name (TRS-002) and branch (TRS-003) present | — | — | — | `TreasuryCreated` | no |
| T7 | Treasury | Rename | `treasury.manage` | name present (TRS-002) | — | — | — | `TreasuryUpdated` (only when the name changed) | no |
| T8 | active → inactive | Deactivate | `treasury.manage` | — | — | — | — | `TreasuryDeactivated` | no |
| T9 | inactive → active | Reactivate | `treasury.manage` | — | — | — | — | `TreasuryReactivated` | no |

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-TRS-001 | A treasury code is 1–16 characters of `A-Z 0-9 - _` and unique within the tenant | aggregate (`Treasury.CleanCode`) + handler check + unique index `(tenant_id, code)` | TRS-001 / TRS-100 | "The treasury code is invalid." / "A treasury with this code already exists." |
| INV-TRS-002 | At most one live request per (user, treasury, device) | handler check + partial unique index `ux_treasury_access_requests_live` (`status IN ('Pending','Approved')`) | TRS-012 | "A pending or approved request already exists for this treasury on this device." |
| INV-TRS-003 | Only a Pending request is decided or cancelled; only an Approved request is revoked | aggregate (state guards) | TRS-013 / TRS-014 | "Only a pending request can be decided." / "Only an approved request can be revoked." |
| INV-TRS-004 | Reject and revoke carry a non-empty reason | aggregate | TRS-020 / TRS-021 | "A rejection/revocation needs a reason." |
| INV-TRS-005 | An inactive treasury accepts no new request | aggregate (`Request` factory) | TRS-010 | "The treasury is inactive." |
| INV-TRS-006 | Only the requester cancels | aggregate (`Cancel`) | TRS-030 | "Only the requester can cancel a request." |
| INV-TRS-008 | A user requests access only to a treasury of a branch they may work in (an empty branch assignment = every branch) — **assumption, Q3** | aggregate guard `TreasuryAccessRequest.EnsureRequesterWorksIn` with the branches of the session principal (handler) | TRS-015 (422; the client shows it under the treasury field) | "This treasury belongs to a branch you are not assigned to." |
| INV-TRS-007 | A request number is unique per tenant | unique index `(tenant_id, number)`; numbers come from a per-tenant, per-year counter under `INSERT … ON CONFLICT DO UPDATE` | — | — |

## 9. Segregation-of-duties rules

```text
SOD-TRS-001  Approver is not the requester.
  On transitions T2, T3: the acting user must not be the user recorded as RequestedByUserId.
  Refusal: "You cannot decide on your own request."
  Configurable: no.
```

The rule lives inside `TreasuryAccessRequest.Approve/Reject` (`EnsureActorIsNot(RequestedByUserId, …)`), not in a
validator or handler. The client hides the Approve/Reject buttons on the user's own rows; the server still refuses
(403, code SOD-TRS-001).

## 10. Approval gates

| Item | G1 — Approve access (T2) |
| --- | --- |
| Permission | `treasury.approve` |
| Rule | none (single approval by any holder of the permission who is not the requester) |
| Recorded | approver user id, server time, optional comment (`Approval` value object) |
| After the gate | nothing is editable; T4 Revoke remains possible |
| Correction path | revoke (T4, reason) and request again (T1); the revoked record stays in force as history |
| Rejection | T3, reason required; the record is terminal (Rejected), a new request is needed |
| Step-up | none (assumed; see §16) |
| Display | approver's display name as of the action, timestamp and comment on the record and in the list |

## 11. Events and policies

```text
Event:    TreasuryAccessRequested { RequestId, Number, TreasuryId, RequestedByUserId, DeviceId, TenantId }
Raised by: T1
Reacts:   Notifications — "when requested, then notify holders of treasury.approve" (idempotent on RequestId)   [later intake]
Timing:   within one outbox cycle

Event:    TreasuryAccessApproved { RequestId, Number, TreasuryId, RequestedByUserId, DeviceId, ApprovedByUserId, TenantId }
Raised by: T2
Reacts:   Payments — "when approved, then the device may open a cash session on the treasury" (idempotent on RequestId)   [later intake]
Timing:   within one outbox cycle

Event:    TreasuryAccessRejected { …, RejectedByUserId, Reason }      Raised by: T3   Reacts: Notifications (requester)
Event:    TreasuryAccessRevoked  { …, RevokedByUserId, DeviceId, Reason }   Raised by: T4   Reacts: Payments — "when revoked, then close the device's open cash session on that treasury"
Event:    TreasuryAccessCancelled { RequestId, Number, TreasuryId, RequestedByUserId, TenantId }   Raised by: T5   Reacts: none
Event:    TreasuryCreated / TreasuryUpdated / TreasuryDeactivated / TreasuryReactivated { TreasuryId, Code, TenantId }   Raised by: T6 / T7 / T8 / T9   Reacts: none yet
          (TreasuryUpdated since validation v0.5.0 O-5: a rename is an event of the audit trail, beside its field changes)
```

All events are written to `ops.outbox_events` in the same commit as the state change; cross-module reactions are
policies, never direct calls.

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Treasury code, names, branch, active flag | Operational, Audited | |
| Request number, treasury, device id, device label, requested at | Operational | device id is not PII; it is excluded from nothing but never logged with the user's name |
| RequestedByUserId, approver / decider ids | Actor fields (SoD anchors) | display names are joined at read time from the membership, never copied |
| Status, decision time, reason, approval comment | Audited | reasons required on T3/T4 |
| Frozen states | Approved, Rejected, Revoked, Cancelled | |
| Retention | keep with the tenant's financial records (assumed 10 years; §16) | export: list filters below, CSV, requires `treasury.view` |

## 13. Reference data and configuration

Treasuries are tenant reference data maintained with `treasury.manage`. No tenant settings in this version
(no approval expiry, no step-up, no configurable SoD).

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Treasuries | active flag, page | code, name en/ar, branch, active | `treasury.view` |
| Access requests | status, treasury, "mine only", page | number, treasury (code + name), requested by, device (label or id), status, requested at, decided by, decided at, **approval comment and decision reason** (validation v0.5.0 D-05; on the requester's and the approver's rows) | `treasury.view` |
| Access requests CSV | the filters of the register (status, "mine only") | number, treasury, requested by, device label, device id, status, requested at, decided by, decided at, approval comment, decision reason | `treasury.export` |
| Request detail | — | all of the above + approval (approver, time, comment), decision reason | `treasury.view` |

## 15. Notifications and escalations

None in this version; the events in §11 are the hooks for the Notifications module intake.

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Treasury`, `tests/Platform.IntegrationTests/TreasuryModuleTests.cs`
and `tests/Platform.WebApi.FunctionalTests/TreasuryAccessFlowTests.cs`.

```gherkin
Scenario S-T1-ok: T1 Request — happy path
  Given an active treasury and a user holding treasury.create with no live request on this device
  When the user requests access from device D with label L
  Then a Pending request numbered TRQ-yyyy-nnnnn is recorded and TreasuryAccessRequested is raised

Scenario S-T1-inactive: T1 Request — refused by INV-TRS-005
  Given an inactive treasury
  When the user attempts to request access
  Then the attempt is refused with TRS-010 and nothing is recorded

Scenario S-T1-live: T1 Request — refused by INV-TRS-002
  Given a Pending or Approved request of the same user on the same treasury and device
  When the user requests again
  Then the attempt is refused with TRS-012 (409) and nothing is recorded

Scenario S-T2-ok: T2 Approve — happy path
  Given a Pending request of user A and a user B holding treasury.approve
  When B approves with an optional comment
  Then the request is Approved, the approval records B, the time and the comment, and TreasuryAccessApproved is raised

Scenario S-T2-sod: T2 Approve — refused by SOD-TRS-001
  Given a Pending request of user A who also holds treasury.approve
  When A attempts to approve
  Then the attempt is refused with SOD-TRS-001 (403) and the request stays Pending

Scenario S-T2-state: T2 Approve — refused by INV-TRS-003
  Given an Approved request
  When an approver approves again
  Then the attempt is refused with TRS-013 (409)

Scenario S-T3-reason: T3 Reject — refused by INV-TRS-004
  Given a Pending request
  When an approver rejects without a reason
  Then the attempt is refused with TRS-020 (the API refuses the empty shape first with VALIDATION-400)

Scenario S-T4-ok: T4 Revoke — happy path
  Given an Approved request
  When an approver revokes with a reason
  Then the request is Revoked, the decider and reason are recorded and TreasuryAccessRevoked is raised

Scenario S-T5-ok / S-T5-other: T5 Cancel — by the requester / refused for another user
  Given a Pending request of user A
  When A cancels (optionally with a reason)  →  the request is Cancelled and TreasuryAccessCancelled is raised
  When user C (not A) attempts to cancel     →  refused with TRS-030 (403)

Scenario S-AUTHZ: any command without the permission
  Given a user whose role lacks the permission
  When the user calls the endpoint
  Then the API answers 403 AUTHZ-001 before the handler runs

Scenario S-T1-branch: a treasury outside the requester's branches (Q3 assumption, validation v0.5.0 O-4)
  Given a user assigned to branch NORTH only and an active treasury of branch EAST
  When the user requests access to it
  Then the attempt is refused with TRS-015 (422) and nothing is recorded; a user of every branch may request it

Scenario S-O10: two people on one browser
  Given user A approved on treasury T from device D
  When user B, on the same browser, opens the cash desk
  Then B presents B's own device id, T is not usable for B, and B's own request needs its own approval
Scenario S-TENANT: isolation
  Given treasuries and requests of tenant X
  When a user of tenant Y lists or reads them
  Then nothing of tenant X is returned (query filter) and the database policy refuses the rows (FORCE RLS)
```

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | How is the "device" identified? The SRS cited by the prototypes is not in the repo. | An opaque id minted once per **user and browser profile** and stored locally (one `localStorage` entry `lis.device-id` holding a map user id → device id; the only web-storage use of the SPA). The server binds the approval to (user, treasury, device), so two people sharing a browser each need their own approval, and the approval binds the person on that device, not the browser (validation v0.5.0 O-10; until v0.7.2 one id per browser profile, which the first user of the browser keeps). Hardware fingerprinting is out of scope. |
| Q2 | Does an approval expire? | No; it is revoked explicitly (T4). |
| Q3 | Must the approver belong to the treasury's branch? May a user ask for access to a treasury of any branch? | Approver: no; any holder of `treasury.approve` in the tenant. Requester (**assumption awaiting the business owner**, validation v0.5.0 O-4): only to the treasuries of the branches assigned to their membership; a user assigned to every branch may ask for any. The request picker offers only those treasuries and the server refuses the others with TRS-015. |
| Q4 | Does approval need a fresh password proof (step-up)? | No. |
| Q5 | May a cashier hold approvals on several treasuries from one device? | Yes; the uniqueness is per (user, treasury, device). |
| Q6 | Retention of request history | With the tenant's financial records (10 years). |
| Q7 | Who is notified, and how? | Nobody yet; events are raised for the Notifications intake. |
| Q8 | Does the payment screen check the approval at payment time? | Yes, by the Payments module through a policy/query on live approvals (BR-27); not part of this intake. |

## Revision history

| Version | Date | Change |
| --- | --- | --- |
| 1.0 | 2026-10-09 | First intake, derived from the Business SRS. |
| 1.1 | 2026-10-12 | Validation v0.5.0: approval comment and decision reason on the request rows and in the new CSV export (D-05); requests limited to the requester's branches, TRS-015, INV-TRS-008 (O-4, Q3 assumption); `TreasuryUpdated` on a rename (O-5); one device id per user and browser profile (O-10, Q1). |
