# LIS-INT-ORG-001 — Organization: branches, roles and privileges, users, parameters (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Slot / modules | `org` — permission modules `organization` (branches, roles), `identity` (users and credentials), `settings` (parameters) |
| SRS pages | CP-36 Branches, CP-37 Roles & privileges, CP-38 Users, CP-57 Parameters; the same Roles/Users pages serve SA-06 Roles and SA-07 Admin users under the platform tenant; business rule BR-17; workflows WF-01 (onboarding, steps 2–3) and WF-11 (privileges administration) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), the LDM knowledge base (manual ch. 12) and the architecture pack (02 §4, §6.1). **For review by the business owner**; assumptions in §16. |
| Version | 1.1 — 2026-10-13 (three special rights of the change request of 2026-10-13: demographics edit limit, branch collections view, report days limit) |
| Implemented in | `src/Platform.Domain/{Organization,Tenancy}`, `src/Platform.Domain/Identity/PlatformUser.cs` (administration methods), `src/Platform.Contracts/Organization`, `src/Platform.Application/{Organization,Tenancy}`, `src/Platform.Application/Identity/Commands/SelectBranchCommand.cs`, `src/Platform.Infrastructure/Persistence/{Organization,Tenancy}`, `src/Platform.Infrastructure/Security/SessionPrincipalQuery.cs`, `src/Platform.WebApi/Controllers/Organization`, `frontend/src/app/features/organization`, `frontend/src/app/core/api/organization.contracts.ts` |

## 1. Purpose and policy basis

A laboratory works in one or more **branches**; every transaction of a session is stamped with the branch the user
chose at sign-in (CP-04). Staff get access through **privilege groups** first and **users** second (LDM manual
§12.1): a role carries the screen rights (here: catalogued permission keys `module.action`, the LDM `Q I U D` per
screen of §12.3), the **special rights** (particular actions: discount with a cap, payment, refund, transfer,
cancellation, modifying a verified result, previewing without verification, printing invoices, seeing VIP cases —
§12.3 "Special Rights"; since 1.1 also three **limits**: the result status up to which a member may still change a
patient's identity demographics, whether the collections report may cover the whole branch, and how many days back a
member may report) and the **result rights** per clinical group and branch (`S R V E P`: see, review, verify,
edit, print — §12.4). A user joined to a role inherits everything (§12.6 step 3). BR-17 (§12.8): changing a role
changes every member at once; changing one user (an **override**) touches that user only, is marked *modified*, and
**Restore** returns the user to the role. The main administrator of a tenant is protected (CP-38, SA-07).
**Parameters** (CP-57) are the tenant behaviour switches every module reads at runtime (foundation `ITenantSettings`).

## 2. Scope and out of scope

In scope: the branch catalogue (CP-36: code, bilingual name, country, governorate, phone, e-mail, currency, active
flag; deactivation instead of deletion); roles (CP-37/SA-06: bilingual name, description, active flag, permission
keys, special rights, result rights per clinical group × branch); tenant users (CP-38/SA-07: platform user +
tenant membership — create with an initial password, edit, lock/unlock, administrator password reset with
must-change, password expiry date, role and branch assignment, designation, shifts, privilege overrides with
restore); the effective privileges of a session (`SessionPrincipal`, `IUserPrivileges`); the branch-selection
rule for unrestricted memberships (resolves foundation question FND A1); the Parameters page over the foundation
settings API; provisioning of a new tenant's MAIN branch and primary administrator for the platform module.

Out of scope (with reason): role **locations** and **print results for ranks** (LDM §12.5; hospital features, no
location model in release 1 — SRS release-1 scope "no Location/Room"); the **signature image upload** (CP-38; the
data model carries `SignatureFileId`, the upload needs the shared file service that does not exist yet — see §16);
the self-service password change and the forced change at next sign-in (CP-65, agent `acc`, and the sign-in flow of
the foundation: this module stores the flag and exposes it); deleting users or roles (BR-15: nothing is erased —
deactivation instead); SA-06 platform special rights "impersonate client admin" (no impersonation in the pack).

## 3. Glossary

| Business word (SRS / LDM) | Platform word |
| --- | --- |
| Branch | `Branch` aggregate (`app.branches`) |
| Privilege group, role (Groups And Privileges) | `Role` aggregate (`app.roles`) |
| Screen right `Q/I/U/D` per screen | permission key `module.action` from `PermissionCatalog` (`view`, `create`, `edit`, `approve`, `cancel`, `export`, `manage`, `viewConfidential`) |
| Special right (Discount, Payment, Refund …) | `SpecialRights` value object on the role (and optionally overridden per user) |
| Demographics editable up to (status) | `SpecialRights.DemographicsEditUpTo` (`DemographicsEditLimit`: Ordered < Complete < Reviewed < Verified < Printed), consumed by Patients (`AllowsDemographicsEditAt`) |
| Treasury report of the whole branch | `SpecialRights.CanViewBranchCollections`, consumed by Billing (collections report, scope Branch) |
| Report time limit (days) | `SpecialRights.ReportDaysLimit` (null = no limit, 0 = today only, ≤ 3650), consumed by Reporting and Billing through `EarliestReportDay(today)` |
| Result rights `S R V E P` per medical group and branch | `RoleResultRight` rows (`app.role_result_rights`): clinical group × branch (or all branches) × five flags |
| System user | `PlatformUser` (global credentials, `identity.platform_users`) + `TenantMembership` (tenant profile, `app.tenant_memberships`) |
| Modified / Restore | membership overrides (`AddedPermissionKeys`, `RemovedPermissionKeys`, `SpecialRightsOverride`), `IsModified`, `RestoreToRole` |
| Locked / UnLocked | administrator lock of the platform user (`IsLockedOut` with no end) |
| Password Expire + Expire Date | `PlatformUser.PasswordExpiresAtUtc`: after it the user cannot sign in or keep a session until an administrator extends it or resets the password |
| Main administrator | `TenantMembership.IsPrimaryAdministrator` |
| Parameters | tenant settings (`app.tenant_settings`, `SettingKeys`) |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `organization.view` | list and read branches and roles; read the permission catalogue |
| `organization.create` | create branches and roles |
| `organization.edit` | edit branches; rename/describe roles; set a role's permissions, special rights and result rights |
| `organization.manage` | deactivate/reactivate branches; activate/deactivate roles |
| `identity.view` | list and read the tenant's users with their effective privileges |
| `identity.create` | create a user (platform user + membership) with an initial password |
| `identity.edit` | edit a user's profile (names, e-mail, mobile, designation, shifts, language, expiry, must-change) and branch assignment |
| `identity.manage` | lock, unlock, reset password, deactivate/reactivate, assign the role, set overrides, restore to role |
| `settings.view` / `settings.manage` | read / save parameters (foundation endpoints, CP-57) |
| any authenticated actor | `GET /organization/branches/selectable` (CP-04 list), `GET /organization/me/privileges` (own effective privileges) |

Lab Administrator is a business persona: the provisioned role "Laboratory Administrator" holds every non-platform key.
Platform-only modules (`platform`, `tenancy`) cannot be granted in a client tenant (ORG-033).

## 5. Aggregates

- **Branch** (root, `app.branches`): `Code` (≤ 16, `A-Z 0-9 - _`, unique per tenant, immutable), `Name`
  (`LocalizedText`), `CountryId?`, `GovernorateId?` (platform lists `saas.countries` / `saas.governorates`, owned by
  module `plat`; referenced by id, no FK), `Phone?`, `Email?`, `Currency` (ISO 4217 shape, 3 letters), `IsActive`,
  `RowVersion`.
- **Role** (root, `app.roles`): `Name` (`LocalizedText`, unique per tenant on English, case-insensitive), `Description?`
  (≤ 500), `PermissionKeys` (text[]), `SpecialRights` (owned value, columns on the role), `ResultRights` (owned
  collection `RoleResultRight`, own table keyed `(tenant_id, id)`, FK `(tenant_id, role_id)`), `IsActive`, `RowVersion`.
- **TenantMembership** (root, `app.tenant_memberships`, existing; extended): `RoleId?`, `BranchIds` (empty = every
  active branch of the tenant), `Designation?` (≤ 100), `Shifts` (≤ 10 labels of ≤ 40), `SignatureFileId?`,
  `IsPrimaryAdministrator`, overrides `AddedPermissionKeys`, `RemovedPermissionKeys`, `SpecialRightsOverride?`
  (owned value); `IsModified` is derived.
- **PlatformUser** (root, `identity.platform_users`, existing; extended): `MustChangePassword`, `PasswordExpiresAtUtc?`;
  administrator methods `CreateWithPassword` (= `Register`), `ResetPasswordByAdministrator`, `LockByAdministrator`,
  `Unlock`, `SetPasswordExpiry`, `RequirePasswordChange`, `ChangeEmail`.
- Value objects: `SpecialRights` (`DiscountCapPercent?` — `null` = the tenant default `discountCapPercent`, `0` = no
  discount, 1–100 = the cap — and eight flags), `ResultRight` (read shape: group, branch?, five flags).
- `EffectivePrivileges` (domain service): permission keys = (role ∪ added) − removed, filtered by the catalogue (and
  by platform-only modules outside the platform tenant); special rights = override ?? role ?? none; result rights =
  role's.

## 6. State table

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Branch active | selectable at sign-in, usable for transactions | name, country, governorate, phone, e-mail, currency | no | no | `organization.view` |
| Branch inactive | kept for history; not selectable | same | no | no | `organization.view` |
| Role active | members inherit its privileges | all | no | no | `organization.view` |
| Role inactive | grants nothing; cannot be assigned | all | no | no | `organization.view` |
| User active | may sign in (unless locked or expired) | profile, role, branches, overrides | no | no | `identity.view` |
| User locked | sign-in refused, sessions revoked | profile | no | no | `identity.view` |
| User expired | `PasswordExpiresAtUtc` ≤ now: sign-in refused, sessions end | profile, expiry | no | no | `identity.view` |
| Membership inactive | the user no longer works in this tenant; sessions in it revoked | profile | no | no | `identity.view` |

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| B1 | — → Branch active | Create branch | `organization.create` | code valid (ORG-003), unique (ORG-100); name; currency (ORG-009); contact (ORG-008); governorate needs country (ORG-008) | — | — | — | `BranchCreated` | no |
| B2 | Branch | Update | `organization.edit` | as B1 except code (immutable) | — | — | — | — (field audit) | no |
| B3 | active → inactive | Deactivate | `organization.manage` | not the last active branch (ORG-110) | — | — | — | `BranchDeactivated` | no |
| B4 | inactive → active | Reactivate | `organization.manage` | — | — | — | — | `BranchReactivated` | no |
| B5 | — → Branch active | Provision MAIN | system (platform provisioning) | idempotent on code MAIN | — | — | — | `BranchCreated` | yes |
| R1 | — → Role active | Create role | `organization.create` | name unique (ORG-101); ≥ 1 permission (ORG-005); keys catalogued (ORG-001); no platform key outside the platform tenant (ORG-033); no key the actor lacks (ORG-030) | — | — | — | `RoleCreated` | no |
| R2 | Role | Rename / describe | `organization.edit` | name unique (ORG-101); description ≤ 500 (ORG-004) | — | — | — | — | no |
| R3 | Role | Set permissions | `organization.edit` | as R1; an active role keeps ≥ 1 (ORG-005); the role of the primary administrator loses none (ORG-014) | — | — | — | `RoleChanged(permissions)` | no |
| R4 | Role | Set special rights | `organization.edit` | cap null or 0–100 (ORG-006); demographics edit limit a result status name (ORG-020); report days limit null or 0–3650 (ORG-019) | — | — | — | `RoleChanged(special-rights)` | no |
| R5 | Role | Set result rights | `organization.edit` | group present, no duplicate (group, branch) (ORG-007), ≤ 2 000 rows | — | — | — | `RoleChanged(result-rights)` | no |
| R6 | active → inactive | Deactivate | `organization.manage` | no member (ORG-011) | — | — | — | `RoleChanged(deactivated)` | no |
| R7 | inactive → active | Activate | `organization.manage` | ≥ 1 permission (ORG-005) | — | — | — | `RoleChanged(activated)` | no |
| U1 | — → User active | Create user | `identity.create` | username 4–30 `a-z 0-9 . _` (ORG-023), unique (ORG-102); e-mail unique (ORG-103); mobile unique (ORG-104); employee code unique (ORG-105) — the user editor shows ORG-102…105 under their own field, not in a banner (LIS-VR-002 O-6); password policy (ORG-020/021); role active (ORG-010) and grantable (ORG-030); branches exist and are active (ORG-012); expiry in the future (ORG-022) | — | — | — | `PlatformUserRegistered`, `TenantMembershipCreated`, `TenantMembershipRoleChanged` | no |
| U2 | User | Edit profile | `identity.edit` | e-mail/mobile unique; credential data of a user who belongs to other tenants is not changed (ORG-031) | — | — | — | — (field audit) | no |
| U3 | User | Assign branches | `identity.edit` | branches exist and are active (ORG-012) | SOD-ORG-001 | — | — | — (principal evicted) | no |
| U4 | User | Assign role | `identity.manage` | role active (ORG-010), grantable (ORG-030); not the primary administrator (TEN-012) | SOD-ORG-001 | — | — | `TenantMembershipRoleChanged` | no |
| U5 | User | Set overrides | `identity.manage` | keys catalogued (ORG-001), no key both added and removed (TEN-013), added keys grantable (ORG-030), none removed from the primary administrator (TEN-012); not in the platform tenant (ORG-032) | SOD-ORG-001 | — | — | `TenantMembershipPrivilegesOverridden` | no |
| U6 | modified → inherited | Restore to role | `identity.manage` | — | SOD-ORG-001 | — | — | `TenantMembershipRestoredToRole` | no |
| U7 | active → locked | Lock | `identity.manage` | not the primary administrator (TEN-012); not a user of other tenants (ORG-031) | SOD-ORG-001 | — | — | `PlatformUserLockedByAdministrator`, `RefreshSessionRevoked(Admin)` | no |
| U8 | locked → active | Unlock | `identity.manage` | ORG-031 | SOD-ORG-001 | — | — | `PlatformUserUnlocked` | no |
| U9 | User | Reset password | `identity.manage` | password policy (ORG-020/021); not the primary administrator (TEN-012, recovery by mobile instead); ORG-031 | SOD-ORG-001 | — | — | `PlatformUserPasswordReset`, `RefreshSessionRevoked(PasswordChanged)` | no |
| U10 | active → inactive | Deactivate membership | `identity.manage` | not the primary administrator (TEN-012) | SOD-ORG-001 | — | — | `TenantMembershipDeactivated`, `RefreshSessionRevoked(Admin)` | no |
| U11 | inactive → active | Reactivate membership | `identity.manage` | — | SOD-ORG-001 | — | — | `TenantMembershipReactivated` | no |
| U12 | — → primary admin | Provision administrator | system (platform provisioning) | idempotent on the tenant's primary membership | — | — | — | `PlatformUserRegistered`, `RoleCreated`, `TenantMembershipCreated`, `TenantMembershipRoleChanged` | yes |
| S1 | session → branch | Select branch (CP-04) | authenticated actor | assigned branch, or any **active** branch of the tenant when the membership is unrestricted (TENANT-005) | — | — | — | `RefreshSessionBranchSelected` | no |

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-ORG-001 | A branch code is 1–16 characters of `A-Z 0-9 - _`, unique in the tenant, never changes | aggregate + handler + unique index `(tenant_id, code)` | ORG-003 / ORG-100 | "The branch code is invalid." / "A branch with this code already exists." |
| INV-ORG-002 | A tenant keeps at least one active branch | handler (count) | ORG-110 | "The last active branch cannot be deactivated." |
| INV-ORG-003 | A branch is never deleted (BR-15) | no delete endpoint; FK `ON DELETE RESTRICT` from future referencing tables | — | — |
| INV-ORG-004 | An active role holds at least one permission ("active role needs a screen") | aggregate | ORG-005 | "An active role needs at least one permission." |
| INV-ORG-005 | A role with members cannot be deactivated | handler (count) passed to the aggregate | ORG-011 | "The role still has members." |
| INV-ORG-006 | Role names are unique per tenant (English, case-insensitive) | handler + unique index `ux_roles_tenant_name_en` (module SQL) | ORG-101 | "A role with this name already exists." |
| INV-ORG-007 | Permission keys are catalogued; platform-only keys only in the platform tenant | aggregate (catalogue) + handler (tenant) + principal query (filter at read) | ORG-001 / ORG-033 | — |
| INV-ORG-008 | One result right per (clinical group, branch or all branches) in a role; any of review/verify/edit/print implies see | aggregate (normalisation) + unique index `NULLS NOT DISTINCT` | ORG-007 | "Duplicate result right." |
| INV-ORG-009 | Discount cap is null (tenant default), or 0–100 | value object | ORG-006 | — |
| INV-ORG-010 | The primary administrator cannot be deactivated, locked, demoted (role change, removed overrides) or have their password reset by another administrator; the role they hold loses no permission | aggregate (membership) + handler (role) | TEN-012 / ORG-014 | "The main administrator is protected." |
| INV-ORG-011 | An administrator cannot grant (role keys, role assignment, added overrides) a permission they do not hold themselves | handler (`IUserPrivileges`) | ORG-030 | "You cannot grant a permission you do not hold." |
| INV-ORG-012 | A key cannot be both added and removed by overrides | aggregate (membership) | TEN-013 | — |
| INV-ORG-013 | Usernames are 4–30 characters `a-z 0-9 . _`, unique platform-wide | handler + domain normalisation + unique index | ORG-023 / ORG-102 | — |
| INV-ORG-014 | Passwords: length between the tenant's `passwordMinLength` (default 8) and 128, no composition rules, must not contain the username (decision of the user, 2026-10-09) | `PasswordRules` (application) | ORG-020 / ORG-021 | — |
| INV-ORG-015 | Credentials and contact data of a user who is an active member of another tenant are changed only there or by the user (prevents a cross-tenant takeover) | handler (`IMembershipDirectory`) | ORG-031 | "This user belongs to other organisations; their credentials cannot be changed here." |
| INV-ORG-016 | No per-user override in the platform tenant (SA-07 "privileges inherited from the role only") | handler | ORG-032 | — |
| INV-ORG-018 | The demographics edit limit is one of the result statuses Ordered, Complete, Reviewed, Verified, Printed; a role created before 1.1 holds Complete (migration default), the provisioned administrator role Printed, a member without a role Ordered (`SpecialRights.None`) | value object + CHECK `ck_roles_special_demographics_edit_up_to_domain` / `ck_tenant_memberships_override_demographics_edit_up_to_domain` | ORG-020 | "The demographics edit limit must be one of the result statuses." |
| INV-ORG-019 | The report days limit is empty (no limit) or 0–3650 days; a role created before 1.1 holds no limit, a member without a role 0 | value object + CHECK `ck_roles_special_report_days_limit_range` / `ck_tenant_memberships_override_report_days_limit_range` | ORG-019 | "The report days limit must be between 0 and 3650 days." |
| INV-ORG-017 | Branch assignment references existing active branches of the tenant; an empty assignment means all active branches. The create and assign requests say which they mean with `allBranches` (validation v0.5.0 B-02): `false` with no branch, or `true` with branches, is refused ORG-018 (field `branchIds`); `null` keeps the older contract | handler (`IBranchLookup`, `OrganizationGuards.EnsureBranchChoice`) | ORG-012, ORG-018 | — |

## 9. Segregation-of-duties rules

```text
SOD-ORG-001  An administrator does not administer their own access.
  On U3–U11: the acting user must not be the target user.
  Refusal: "You cannot change your own access; ask another administrator."
  Configurable: no.
```

The rule is checked by the user-administration handlers before the aggregate is called (the target is a platform
user id, the actor is `ICurrentUser.UserId`); the client hides the actions on the actor's own row.

## 10. Approval gates

None. Privilege changes take effect immediately (BR-17); they are recorded by the field audit, the audit trail
(events) and the security events.

## 11. Events and policies

```text
Event:    BranchCreated { BranchId, Code } / BranchDeactivated { BranchId, Code } / BranchReactivated { BranchId, Code }
Raised by: B1/B5, B3, B4
Reacts:   none yet (treasuries and other branch-stamped modules read the active flag at transaction time)

Event:    RoleCreated { RoleId } / RoleChanged { RoleId, Aspect }
Raised by: R1 / R3–R7
Reacts:   the handler evicts the cached principal of every live session of the role's members in the tenant
          (`IPrincipalCacheInvalidator`), so the next request resolves the new privileges (02 §4.4, BR-17).
Timing:   immediately in this process; other replicas within the 10 s cache lifetime.

Event:    TenantMembershipRoleChanged / TenantMembershipPrivilegesOverridden / TenantMembershipRestoredToRole
Raised by: U4 / U5 / U6
Reacts:   eviction of the member's sessions (same mechanism).

Event:    PlatformUserLockedByAdministrator { UserId } / PlatformUserPasswordReset { UserId, MustChange }
Raised by: U7 / U9
Reacts:   every live session of the user is revoked in the same transaction (02 §4.1 "admin deactivation", "password change").
```

## 12. Data classification and retention

| Data | Class | Notes |
| --- | --- | --- |
| Branch master data | internal | kept forever (BR-15) |
| Role and privileges | internal, security-relevant | every change in `audit.field_changes` and `audit.audit_trail` |
| User names, mobile, e-mail | personal data | visible to `identity.view` only; never in tokens (D-02) |
| Password hash | credential | Argon2id; never returned, never in the field audit (skipped property) |
| Temporary password typed by the administrator | credential | travels in the credential envelope (D-03, `[CredentialField]`), never logged or stored in plaintext |

### 12a. Credentials in transit

The initial password (U1) and the temporary password (U9) are `[CredentialField]` properties (`CreateUserRequest.Password`,
`ResetUserPasswordRequest.TemporaryPassword`): the SPA seals them in the credential envelope (D-03) and the edge
filter opens them before the handler runs; a plaintext value is refused with `CRED-005`. They are hashed with
Argon2id at once and never logged, stored or returned.

## 13. Reference data and configuration

Tenant settings read by this module: `passwordMinLength` (default 8, range 8–64), `discountCapPercent` (default for
roles whose cap is null; consumed by Registration/Billing). Platform lists read by the pages: countries and
governorates (`/api/v1/platform/countries`, `/api/v1/platform/governorates?countryId=`), clinical groups
(`/api/v1/catalogue/clinical-groups`). The Parameters page (CP-57) groups the 25 foundation keys into Registration,
Printing, Samples and results, Security and Subscription with typed inputs (switch, bounded number, closed list, text).

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Branches | search (code, names), active | code, name, phone, e-mail, currency, status | `organization.view` |
| Roles | search (names), active | name, description, permissions, members, status | `organization.view` |
| Users | search (username, names, mobile, e-mail), role, active | username, name, role, branches, designation, status (active/locked/expired/inactive), modified flag, primary-admin flag | `identity.view` |
| Permission catalogue | — | module, display name, actions | `organization.view` |

No export in this release (the data table has no Excel export yet).

## 15. Notifications and escalations

None in this module. Security events: `MEMBERSHIP_CHANGED` and `PRIVILEGE_MATRIX_CHANGED` are covered by the
audit trail of the domain events above; `USER_LOCKED_OUT` stays the automatic lockout's.

## 16. Acceptance scenarios, open questions and assumptions

Acceptance scenarios (domain unit tests are named after them; functional tests cover the HTTP paths):

| Id | Scenario |
| --- | --- |
| AS-B1 | Create a branch with a valid code; the code is upper-cased; a duplicate code is ORG-100. |
| AS-B2 | Deactivate a branch; deactivating the last active branch is ORG-110; reactivation is idempotent. |
| AS-B3 | A governorate without a country, a malformed e-mail or currency is refused (ORG-008/009). |
| AS-R1 | Create a role with permissions; zero permissions is ORG-005; an unknown key is ORG-001. |
| AS-R2 | Set special rights; a cap of 101 is ORG-006; an unknown demographics edit limit is ORG-020; a report days limit of 3651 is ORG-019; the short constructor and a client that omits the three limits keep the migration defaults (Complete, false, no limit). |
| AS-R3 | Set result rights; duplicates are ORG-007; "verify" implies "see". |
| AS-R4 | Deactivate a role with members is ORG-011; without members succeeds; activating a role without permissions is ORG-005. |
| AS-R5 | Changing a role's permissions changes the next request of every member (BR-17). |
| AS-U1 | Create a user; a 3-character username is ORG-023; a password shorter than the minimum is ORG-020; a password containing the username is ORG-021. |
| AS-U2 | Lock a user: sign-in refused and live sessions revoked; unlock restores sign-in. |
| AS-U3 | Reset a password: the old password fails, the temporary one works, `MustChangePassword` is set, sessions are revoked. |
| AS-U4 | Overrides: an added key appears only for that user, `IsModified` is true; restore clears it. |
| AS-U5 | The primary administrator cannot be locked, deactivated, demoted or reset by another administrator (TEN-012). |
| AS-U6 | An administrator cannot change their own access (SOD-ORG-001) nor grant a permission they lack (ORG-030). |
| AS-S1 | An unrestricted membership selects any active branch; an inactive or foreign branch is TENANT-005; a tenant with one active branch auto-selects it at sign-in; the selectable list shows the active branches. |

Decision recorded: **password policy** (user, 2026-10-09) — the architecture pack 02 §6.1 applies everywhere:
minimum length from the tenant setting `passwordMinLength` (default 8), maximum 128, no composition rules, must not
contain the username. The composition rules of CP-02 are superseded.

| # | Assumption built on | Question for the business owner |
| --- | --- | --- |
| A1 | A membership with no branches means "all active branches of the tenant" (resolves FND A1); explicitly assigned branches are kept as assigned. | — (resolved by the coordinator) |
| A2 | `DiscountCapPercent = null` means "use the tenant parameter `discountCapPercent`", `0` means no discount; new roles start with 0 (least privilege). | Is "no discount" the right default for a new role? |
| A3 | Result rights: any of review/verify/edit/print implies see; a row for "all branches" (branch empty) and rows for specific branches combine (any match grants). | Confirm the combination rule. |
| A4 | The privilege matrix uses the platform's catalogue actions (view/create/edit/approve/cancel/export/manage/viewConfidential) per module rather than LDM's per-screen Q/I/U/D. | Is per-module granularity acceptable for release 1? |
| A5 | E-mail is required for a user (the platform user needs one, unique); recovery is by mobile (FND A2). | Can lab staff exist without an e-mail? |
| A6 | Password expiry (`PasswordExpiresAtUtc`) is an access end date (LDM §12.6 step 5): after it the user cannot sign in and live sessions end. The pack's "max age 180 days" (forced periodic change) is not implemented. | Is a periodic change required? |
| A7 | Lock is platform-wide (credentials are global, D-06); therefore lock, unlock, password reset, e-mail and mobile changes are refused (ORG-031) for users who are active members of other tenants. | Should a tenant be able to bar such a user only from its own tenant (membership deactivation does that today)? |
| A8 | The signature image (CP-38) is stored as a file id; uploading it waits for a shared file service. | Which service stores staff signatures? |
| A9 | Shifts are free labels (≤ 10 per user); LDM takes them from another screen. | Is a shift catalogue needed? |
| A10 | SA-06 platform special rights (reset client password, activate client, expire passwords) are permission keys of `tenancy`/`identity`; impersonation is not offered. | Confirm. |
| A11 | No per-user overrides in the platform tenant (SA-07). | — |
| A12 | The provisioned role "Laboratory Administrator" holds every permission except the platform-only modules, every special right with a cap of 100, and no result rights (clinical groups are copied later). | Should the administrator role get result rights on every group after the catalogue copy? |
| A13 | The forced change at next sign-in is enforced by the sign-in flow (foundation) and the change-password page (`acc`); this module sets and exposes the flag (`SessionPrincipal.MustChangePassword`). | — |
| A14 | Change request 2026-10-13 item 1: the right "edit demographics according to test status" is a per-role (overridable per user) **status limit**: identity demographics of a patient may be changed from a visit while the highest status of that visit's results is at most the limit; every role that existed before 1.1 received `Complete` from the migration so that nothing changes for a reviewed or verified visit (an administrator raises it in the role editor); the administrator role of a laboratory provisioned after 1.1 holds `Printed` (`SpecialRights.All`). | Is `Complete` the right default for existing roles, and should the limit also apply from the patient file (there: the highest status over every visit of the patient)? |
| A15 | Item 2: "treasury report for the current user or the branch as per the rights" is the boolean right `CanViewBranchCollections` (off for every role that existed before 1.1, on for the administrator role of a laboratory provisioned after 1.1); without it the collections report covers the user's own collections only. | Confirm. |
| A16 | Item 3: "user right for time limits in days" is `ReportDaysLimit` per role / user: the earliest day a report may start is today minus the limit (0 = today only; empty = no limit, which existing roles received). The same limit bounds the collections report of item 2. | Should existing roles start with a limit (e.g. 30 days) instead of none? |

## 17. Change request 2026-10-24 — branch address (LIS-CR-008 C-04, package P-ADR)

`Branch` gains `CityId?`, `DistrictId?`, `AreaId?` (`app.branches.city_id / district_id / area_id`, uuid null, referenced by id: no FK
across the schema boundary; migration `ReviewFeatures` of the integrator, no extra SQL). B1 Create and B2 Update take the optional ids
(`CreateBranchRequest`, `UpdateBranchRequest` and the commands, trailing and defaulted: older bodies keep working and clear the
locality on update); `BranchListItem` returns them. The handler reads the rows through `IBranchGeography.LocalityRowsAsync` and
resolves them with `OrganizationGuards.ResolveLocalityAsync` → `Locality.Resolve` (platform domain); `Branch.Locate` refuses a locality
on a branch without a governorate or of another governorate. Codes: GEO-020 (city not in the branch's governorate, or no governorate),
GEO-021, GEO-022: field refusals (422) that the SPA puts under City, District and Area.

CP-36 editor: three optional cascading selects after Governorate (City / markaz → District → Area / village), loaded from the public
lookups for the chosen parent (master-data page `FieldSchema.dependsOn` + `dependentOptions`, added by LIS-CR-008); changing a level
clears the levels below. Keys `organization.branches.field.cityId|districtId|areaId`, `organization.branches.hint.locality`,
`organization.branches.error.GEO-020…022`.

Acceptance: `AddressesFlowTests.A_branch_takes_a_locality_of_its_governorate_only` (create with city and district, GEO-020 without a
governorate, GEO-022 for an area without its district, update to an area, update without the fields clears); unit tests
`BranchGeographyRulesTests` (Application) and `LocalityTests.S_LCY_20` (Domain); SPA `branches.page.spec` and the master-data
cascading spec.
