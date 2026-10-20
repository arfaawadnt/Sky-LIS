# Change request 2026-10-20 — Payments back in the menu, treasury removed from the system (LIS-CR-004)

Decision of the business owner (2026-10-20, after the first use of 0.15.2): *Payments* (with the collections report inside it) returns to
the sidebar, and the **treasury concept is removed from the system**: no cash drawers, no device-bound access requests, no approval,
no BR-27. Money keeps its cashier, branch, method and reference; cash closing is per cashier and branch (LDM "Total Daily Income per
user"). History is kept: nothing is deleted from the database; the treasury tables stay as orphans, old payments keep their treasury id.

The work is split into three packages built in parallel (two SPA, one API) and integrated by the integrator (migration
`TreasuryRemoved`, permission catalogue, nav, routes, shell dictionary, deletion of the treasury feature and the device service, docs,
browser check, release, installation). Read `docs/build/MODULE-PATTERN.md` §2 and the rules of `CR-2026-10-19_Prototype-Gaps.md` §0
(slots, forbidden files, reports); they apply unchanged except where this document says otherwise.

## 0. The target shape (every package builds to this)

**Money movement facts** (payments, refunds, batch payments): `VisitId`, `Amount`, `Method` (kind), `MethodCode?`, `Reference?`,
cashier = the session user, branch = the session branch (`BranchScope.Require`), posted time. **No** `TreasuryId`, **no** `DeviceId`
in any request, command, contract or SPA form. The reference rule stays as built on 2026-10-19 (payment-method definition, else
"every kind but cash").

**Cash closing** (`CashClosing`, CP-30 in Release 2 backend + the client page `/billing/cash-closing` where it exists): scope =
**branch of the session** + **cashier** (`CashierUserId?`: one cashier, or every cashier of the branch for `billing.manage`) +
period. `TreasuryId` becomes nullable and is no longer read or written (new closings: null). Preview and close keep every other rule
(period valid, no overlap for the same scope, counted cash and difference, BR-16 frozen period, closing number). Replace the
"treasury of the session branch" rules (`BIL-022`-style treasury codes) by "branch of the session".

**Dashboard** *cash of the day*: by the payment's / refund's `branch_id` (no join on `app.treasuries`).

**Collections report**: unchanged (by method / cashier / branch); drop any treasury name column if it carries one.

**Permissions**: the group `treasury` (`treasury.view/create/edit/manage…`) disappears from `PermissionCatalog` (integrator) and is
purged from `app.roles.permissions` by the migration SQL (integrator). Billing keeps `billing.*`.

**Removed**: the Treasury module (`Platform.Domain/Treasury`, `Application/Treasury`, `Contracts/Treasury`,
`Infrastructure/Persistence/Treasury` — entity configurations and repositories —, `Controllers/Treasury`, its tests), the endpoints
`/api/v1/treasuries`, `/api/v1/treasury-access-requests`, `GET /api/v1/billing/my-treasuries`, the `treasury-request` notification
template code (defaults and the SA-20 code list; existing template rows may stay), the SOD-TRS-001 exception code path (the
membership fact `IsPrimaryAdministrator` **stays** — Organization uses it), the SPA feature `features/treasury`, `core/device`
(the device id served treasury approval only), `core/api/treasury.contracts.ts` (its `CreatedResource` type moves to
`core/api/created-resource.ts` — integrator; import it from there), the nav entries and dictionaries of the two pages.

**Kept in the database**: `app.treasuries`, `app.treasury_access_requests` (orphan tables, audit history), the columns
`treasury_id` / `device_id` on payments, refunds and cash closings — all **nullable** from now on, foreign keys to `app.treasuries`
dropped, CHECKs that require them dropped. The migration is the integrator's; packages change the aggregates and the EF
configurations only (nullable properties, no FK / navigation to a treasury).

## 1. Packages

| Package | Scope | Slot | Owns |
| --- | --- | --- | --- |
| P-API Billing without treasury | the whole backend | `bill` | `src/Platform.{Domain,Contracts,Application,Infrastructure,WebApi}/**` except `PermissionCatalog.cs`, `AppDbContext.cs`, `Persistence/Migrations/**`; `tools/Platform.DevTools`; `tests/**` except `Support/`; `tests/validation/backend-cycle.mjs`; intakes BIL-001, TRS-001 (withdrawal section), REG-001 §17/§18 backend lines |
| P-SPA-BIL Billing pages | `frontend/src/app/features/billing/**`, `core/api/billing.contracts.ts`, `public/i18n/billing.{en,ar}.json` | — | the cash desk, payments, refunds, batch payment, cash closing, payment methods pages and their specs / fixtures / documents |
| P-SPA-REG Registration and account | `frontend/src/app/features/registration/**`, `features/account/**`, `public/i18n/registration.{en,ar}.json`, `account.{en,ar}.json` | — | the registration payment block, the accession page's payments card, the dashboard quick actions / KPI labels |

Integrator: `PermissionCatalog.cs`, `AppDbContext.cs` (if a `DbSet` of the module must go), migration `TreasuryRemoved` + SQL,
`shell/**`, `app.routes.ts`, `public/i18n/shell.*.json`, deletion of `features/treasury`, `core/device`, `core/api/treasury.contracts.ts`
(+ `created-resource.ts`), `core/api/auth.contracts.ts` / `audit.contracts.ts` mentions, `docs/srs`, `README.md`, `CLAUDE.md`,
`docs/build/MODULES.md`, `docs/plan/*DS-001*`, UAT scripts, `.claude/launch.json`.

## 2. P-API — Billing without treasury

- **Domain `Billing`**: `Payment`, `Refund`: `TreasuryId` → `Guid?`, `DeviceId` → `string?`, never set by new postings; remove
  `CashierStation` and the BR-27 guards (`TreasuryInactive`, `TreasuryOfAnotherBranch`, `TreasuryNotApproved`, `TreasuryNotFound` and
  any other treasury code — delete the constants; renumbering of other codes is not needed). `CashClosing`: `TreasuryId` → `Guid?`,
  scope rule = session branch + cashier, `CheckClose` / `Close` / `RequireScope` without treasury parameters; the closing number
  and events keep their shape minus the treasury id (`CashClosingClosed.TreasuryId` removed; `PaymentPosted` / `RefundPosted` lose
  `TreasuryId`). Events are consumed by policies in Billing / Reporting / Account only — fix the consumers.
- **Application `Billing`**: `RecordPaymentCommand(VisitId, Amount, Method, Reference, MethodCode?)`, `RecordRefundCommand(VisitId,
  Amount, Method, Reference, ReasonId, Note, MethodCode?)`, `PayBatchCommand(...)` likewise; handlers resolve cashier and branch from
  `ICurrentUser` (`BranchScope.Require`), no treasury port (`IBillingPorts`: delete the treasury lookup and `my-treasuries` query);
  `PreviewCashClosingQuery(CashierUserId?, FromUtc, ToUtc)` and `CloseCashCommand(...)` on the session branch; register / account
  queries and list items lose the treasury columns (`treasuryCode`, `treasuryName*`); `CollectionsReport` unchanged except a treasury
  column, if any. `BillingSupport`: drop the station resolution.
- **Contracts `Billing`**: `RecordPaymentRequest(VisitId, Amount, Method, Reference, MethodCode?)`, `RecordRefundRequest(VisitId,
  Amount, Method, Reference, ReasonId, Note, MethodCode?)`, `PayBatchRequest(Amount, Method, Reference, MethodCode?)`,
  `CashClosingPreviewRequest(CashierUserId?, FromUtc, ToUtc)` (check the current name), items without treasury fields; delete
  `BillingTreasuryItem`. **Bodies that still send `treasuryId` / `deviceId` are accepted and ignored** (System.Text.Json ignores
  unknown members by default — verify the host does not use `UnmappedMemberHandling.Disallow`), so an old browser tab survives the
  swap.
- **Infrastructure**: EF configurations (nullable columns, no FK to treasuries, no treasury navigation), `BillingReadAdapters` /
  repositories without treasury joins, `BranchDashboardReader` cash of the day by `branch_id`, `DefaultNotifications` and
  `NotificationTemplate` codes without `treasury-request`, `InfrastructureServiceRegistration` without the module. Delete
  `Persistence/Treasury/**`. Add the SQL block for the integrator in `Persistence/Billing/Billing.migration.sql` (dated heading,
  idempotent): nothing is expected beyond what EF generates, but if a trigger function (`app.billing_guard_payment`) lists
  `treasury_id` / `device_id` it must keep accepting nulls — review it.
- **WebApi**: delete `Controllers/Treasury/**`; Billing controllers without `my-treasuries`; `PipelineOrder` untouched.
- **Delete** `Platform.Domain/Treasury`, `Application/Treasury` (commands, queries, policies), `Contracts/Treasury`, and every test
  of the module (`Domain.UnitTests/Treasury`, `Application.UnitTests/Treasury`, `IntegrationTests/Treasury*`,
  `FunctionalTests/Treasury*`); the architecture gates must stay green (`RequestPolicyTests`, `DomainEventPolicyTests`,
  `TransitionAtomicityTests` …). `ICurrentUser.IsPrimaryAdministrator` and the session principal stay.
- **DevTools**: remove `seed-treasury`; `seed-demo-lab` seeds no treasury and no treasury permission; `grant-role` unchanged.
- **Validation script** `tests/validation/backend-cycle.mjs`: remove the treasury steps (access request, approval) and the treasury
  fields of the payment; the cycle must still post a payment and a refund.
- **Tests to add**: domain — a payment / refund / closing without treasury; application — handlers take cashier and branch from the
  session; functional — `POST /api/v1/billing/payments` with the new shape (and with an extra `treasuryId` in the body, ignored),
  cash closing preview and close per cashier on the session branch, `GET /api/v1/treasuries` → 404, `my-treasuries` → 404.
- **Intakes**: `LIS-INT-TRS-001_Treasury-Access.md` — add a dated **Withdrawn** section at the top (why, what replaced it, data
  kept) and bump the version; `LIS-INT-BIL-001_Billing.md` — dated section "Treasury removed" (facts of a movement, closing scope,
  codes removed, endpoints removed, compatibility) and version bump; `LIS-INT-REG-001_Registration.md` — one dated note that the
  payment on CP-06 no longer needs a treasury (the SPA side is P-SPA-REG's; keep to the backend facts).
- **Migration**: do not generate it. Report the exact list of model changes (tables, columns made nullable, FKs / CHECKs to drop,
  roles SQL) so the integrator generates `TreasuryRemoved` and appends the SQL. The database suites run only after it exists: run
  them when the integrator tells you (you will be messaged); until then report them as not run.

## 3. P-SPA-BIL — Billing pages

- `core/api/billing.contracts.ts`: requests without `treasuryId` / `deviceId`, items without treasury fields, `BillingTreasuryItem`
  and `BILLING_API.myTreasuries` deleted; `CashClosingPreviewRequest` per cashier.
- `state/cash-desk.facade.ts`: no treasuries resource, no `approved` / `noTreasury` / `treasuryId`; no `DeviceIdentityService`
  (the service is deleted by the integrator — do not import it). Payments, refunds, batch payment facades and pages: no treasury
  select, no "request treasury access" pointer, the forms post the new shape; `movement-form.component.ts` without the treasury
  field. Cash closing page and facade: scope = cashier (self, or any cashier of the branch for `billing.manage`) + period; closing
  sheet document without the treasury line; receipt / refund receipt / invoice documents without the treasury name; register tables
  and CSV without the treasury columns; `testing/billing.fixtures.ts` updated. Dictionaries: delete `billing.field.treasury`,
  `billing.*.noTreasury*`, `billing.*.requestAccess*` and the like; add what the cashier scope needs (English and Arabic).
- Specs for every changed page / facade / document; `npx ng lint`, `node scripts/check-boundaries.mjs`, `npx ng test --watch=false
  --include 'src/app/features/billing/**/*.spec.ts'`, then the full `ng test` at the end (other packages' specs may still be moving:
  report foreign failures by file).

## 4. P-SPA-REG — Registration and account

- `state/registration.facade.ts`: delete the `treasuries` resource, `hasTreasury`, `treasuryId` in `PaymentState`, the
  `DeviceIdentityService` import; the payment panel is always offered when there is something to pay; `recordPayment` posts the new
  shape (`visitId`, `amount`, `method`, `methodCode`, `reference`). `pages/registration.page.html` / `.ts`: remove the "no treasury"
  warning and its link; `ui/registration-readiness` and `ui/registration-action-bar` if they mention the treasury. The accession page's
  payments card (`ui/accession-payments.component.ts`): no treasury column; its *Take payment* link stays. `data/registration.api.ts`:
  delete `myTreasuries`-style reads. Dictionaries: delete `registration.payment.noTreasury`, `registration.accession.requestAccess`
  and similar (English and Arabic).
- `features/account`: dashboard quick action *Payments* stays; any treasury wording in KPI labels / hints goes; `data/account.api.ts`
  imports `CreatedResource` from `core/api/created-resource.ts` (the integrator creates it — if it is not there yet when you build,
  create that one-line file yourself with `export interface CreatedResource { readonly id: string; }` matching the current
  declaration in `treasury.contracts.ts`, and say so in the report).
- Specs updated / added; same commands as P-SPA-BIL with `--include 'src/app/features/registration/**/*.spec.ts'` and
  `'src/app/features/account/**/*.spec.ts'`.

## 5. Integrator checklist

1. `PermissionCatalog.cs`: remove the `treasury` group (and the `TreasuryAccess`-style keys if separate); gate `PermissionCatalogTests`.
2. Delete `frontend/src/app/features/treasury`, `core/device`, `core/api/treasury.contracts.ts`; create `core/api/created-resource.ts`;
   fix every import; `app.routes.ts` without the treasury route; `shell/nav.registry.ts`: Payments `menu` restored, the two treasury
   entries deleted; `shell.*.json` keys deleted; `auth.contracts.ts` / `audit.contracts.ts` treasury mentions.
3. After P-API lands: migration `TreasuryRemoved` in the `integrator` slot (expected: `alter column … drop not null` on
   `treasury_id` / `device_id` of payments, refunds, cash closings; drop FKs to `app.treasuries`; drop treasury CHECKs; EF will also emit
   `DropTable` for the orphan tables — **remove those operations** from `Up()` and `Down()` so the history stays); append
   `UPDATE app.roles SET permissions = array(SELECT p FROM unnest(permissions) p WHERE p NOT LIKE 'treasury.%')` (idempotent) plus any
   module SQL block; regenerate `deploy/postgres/migrations.sql`.
4. Every suite in the integrator slot; frontend lint, boundaries, full specs, build; browser check (registration with payment, Payments
   in the sidebar, collections report, cash closing, no treasury anywhere); README bullets; CLAUDE.md §5; MODULES.md; SRS (BR-27,
   CP-28, CP-29 withdrawn, WF-09 without the treasury step; bump version); UAT scripts (treasury scripts withdrawn); LIS-DS-001 (D-2
   reversed, C11 row); release (minor), install.
