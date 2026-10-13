# LIS-INT-BIL-001 — Billing: payments, refunds, batch payment and cash closing (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `billing` (Sky LIS Client Portal, pages CP-23 Payments, CP-24 Refunds, CP-27 Batch payment, CP-30 Cash closing; business rules BR-14, BR-16, BR-26, BR-27; workflow WF-09 and the payment steps of WF-02 / WF-04) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), MODULES.md (LIS-BM-001 §1, §3, §5) and the LDM synthesis. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.2 — 2026-10-13 (collections report of the Payments register: change request item 2; 1.1 — 2026-10-10 CP-37 special rights enforced) |
| Slot / wave | `bill`, wave 2 (built in parallel with Registration, Samples and Account) |
| Implemented in | `src/Platform.Domain/Billing`, `src/Platform.Contracts/Billing`, `src/Platform.Application/Billing` (+ `Platform.Application/Registration/IVisitBalanceLookup.cs`), `src/Platform.Infrastructure/Persistence/Billing`, `src/Platform.WebApi/Controllers/Billing`, `frontend/src/app/features/billing`, `frontend/src/app/core/api/billing.contracts.ts` |

## 1. Purpose and policy basis

The laboratory collects the **patient share** of every visit (the payer share is billed later through claims), in full
or in parts, at a cash desk; every collection produces a receipt (LDM manual §4.8, synthesis §4.6). When a visit is
re-priced or a service is cancelled after payment, the patient may have paid more than the visit now costs: the
**negative balance is returned through the refund flow, never above the refundable amount** (BR-14; LDM `Return Money`
§10.8). At the end of a shift the cashier reconciles the drawer: **expected cash = cash payments − cash refunds**, card,
POS and transfer amounts are not in the drawer (BR-16; LDM `Total Daily Income` "الرصيد الفعلي", §10.11). Money is
taken only on a **treasury approved for the user's device** (BR-27, CP-29), so every movement is attributable to a
drawer, a device and a person. Batches registered for a group contract are settled at once; lab-to-lab batches are
billed to the sending laboratory through claims (CP-27; LDM `Batch Payment` §4.8, `Invoice ▸ Lab` §10.13).

The single definition of the balance (MODULES.md §5, owned by Billing):

```text
remaining  = visits.patient_share_total − Σ posted payments + Σ refunds
refundable = max(0, −remaining)
balance status: Paid (remaining = 0) · Unpaid (nothing paid, remaining > 0) · Partial (something paid, remaining > 0) · RefundDue (remaining < 0)
```

## 2. Scope and out of scope

In scope: recording payments on a visit (cash, card, POS, transfer), voiding a payment with a reason, recording refunds
with a catalogue reason, settling a batch (pay one member, pay all), the cash-closing preview and the immutable closing
record, the four documents (payment receipt, visit invoice, refund receipt, cash-closing sheet), the registers and CSV
exports, the `IVisitBalanceLookup` read port that Registration consumes, and the BR-26 contract (the registration page
posts the captured payment through this module's API right after saving the visit; MODULES.md §3: Billing reacts to no
registration event).

Out of scope: money transfer between contracts and line shifts (CP-25, Registration module: it raises
`VisitTransferred`; the balance is recomputed on read), service cancellations (CP-26, Registration), payer claims
(CP-31, Reporting), financial reports (CP-35, Reporting), treasuries and treasury access (CP-28/29, Treasury module),
an ERP interface (LDM is "not an accounting system", synthesis §7), multi-currency (one tenant currency, the
`defaultCurrency` parameter), electronic card terminals.

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Visit account, حساب الزيارة | `VisitAccount` aggregate (`app.visit_accounts`, id = the visit id) — the consistency boundary of a visit's money |
| Payment, سداد / receipt, إيصال | `Payment` entity of the account (`app.payments`), receipt number `RCT-yyyy-nnnnn` |
| Refund, استرداد / return money | `Refund` entity of the account (`app.refunds`), number `RFD-yyyy-nnnnn` |
| Void, إلغاء سداد | transition of a payment Posted → Voided with a reason |
| Cash closing, تقفيل الخزينة / handover sheet | `CashClosing` aggregate (`app.cash_closings`), number `CLS-yyyy-nnnnn` |
| Drawer, treasury | `Treasury` (Treasury module); a payment carries `treasury_id` and the `device_id` the browser presents |
| Patient share / payer share | `visits.patient_share_total` / `payer_share_total` (Registration, net of discount, Ordered + Reserved lines) |
| Method | `Cash`, `Card` (Visa in LDM), `Pos`, `Transfer` |
| Batch | `app.batches` (Registration): kind `Group` (settled here) or `LabToLab` (billed through claims) |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `billing.view` | visit account, payments / refunds / closings registers, documents (receipt, invoice, refund receipt, closing sheet), batch account, own approved treasuries |
| `billing.create` | record a payment (CP-23, BR-26 from the registration page), pay a batch member or the whole batch (CP-27) — together with the special right `CanPay` |
| `billing.approve` | record a refund (CP-24) — together with the special right `CanRefund` |
| `billing.cancel` | void a payment with a reason — together with the special right `CanCancel` |
| `billing.manage` | cash-closing preview and close (CP-30) |
| `billing.export` | CSV export of the payments, refunds and closings registers |
| `billing.edit` | not used by this intake (reserved) |

The special rights of CP-37 (Organization, `IUserPrivileges.SpecialRights`, from the role of the membership, BR-17) narrow
the permissions inside the handlers, checked first and refused with 403: `CanPay` (payments and pay-all, BIL-040),
`CanRefund` (refunds, BIL-041), `CanCancel` (voids, BIL-042), `CanPrintInvoice` (the invoice document, BIL-043),
`CanViewBranchCollections` (the collections report over the whole branch, BIL-044) and `ReportDaysLimit` (how far back the
collections report may start, BIL-045; `0` = today only, empty = no limit). Cashier = any user holding `billing.create` with an approved
treasury on the device; manager = holder of `billing.manage`. Deny by default: every command and query carries one
`[RequirePermissionPolicy]` (RequestPolicyTests).

## 5. Aggregates

- **VisitAccount** (root, `app.visit_accounts`): `Id` = the visit id (one account per visit, opened by the first
  movement; a concurrent second opening fails on the primary key), `PaidTotal`, `RefundedTotal` (running totals of the
  children, so every movement updates the root row and the `xmin` concurrency token serialises two cashiers on the same
  visit: CONCURRENCY-409), `RowVersion`.
  - **Payment** (entity, `app.payments`, FK `(tenant_id, visit_id)` → the account): `ReceiptNumber`, `Amount`, `Method`,
    `Reference`, `TreasuryId`, `DeviceId`, `CashierUserId`, `BranchId` (session branch), `PaidAtUtc`, `BatchId?` (paid
    through pay-all), `Status` Posted/Voided, `VoidReason`, `VoidedByUserId`, `VoidedAtUtc`.
  - **Refund** (entity, `app.refunds`): `Number`, `Amount`, `Method`, `Reference`, `ReasonId` (catalogue reason of kind
    Refund), `Note`, `TreasuryId`, `DeviceId`, `CashierUserId`, `BranchId`, `RefundedAtUtc`.
  - Why one aggregate and not three: the invariant "amount ≤ remaining" (BR-14, BR-26) spans every payment and refund of
    the visit; only a root that owns them can decide it atomically. The table contract of MODULES.md §5 (`app.payments`,
    `app.refunds` and their columns) is unchanged.
- **CashClosing** (root, `app.cash_closings`): `Number`, `TreasuryId`, `BranchId` (the treasury's), `CashierUserId?`
  (null = all cashiers), `PeriodFromUtc`, `PeriodToUtc` (half-open), `Totals` (value object, columns on the same row:
  payments and refunds per method, counts, expected cash, outstanding balance, discounts, visits touched), `CountedCash?`,
  `Difference?`, `Note`, `ClosedByUserId`, `ClosedAtUtc`, `RowVersion`. Immutable once created.
- Value objects: `ClosingTotals`, `VisitBalanceFigures` (the balance formula), `CashierStation` (the BR-27 facts of a
  movement: treasury, its branch, active flag, approval for the user and device, the session branch), `BatchAllocation`
  (pay-all distribution).

## 6. State table

| Record | State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- | --- |
| Payment | Posted | money received, counted in the balance and the drawer | none (only the void transition) | yes, once inside a closed cash closing | no | `billing.view` |
| Payment | Voided | cancelled with a reason; excluded from the balance and the drawer | none | yes | yes | `billing.view` |
| Refund | (recorded) | money returned | none | yes | yes | `billing.view` |
| CashClosing | (closed) | the reconciled drawer for a treasury, cashier scope and period | none | yes | yes | `billing.view` |
| Balance (derived) | Unpaid / Partial / Paid / RefundDue | computed on read from §1 | — | — | — | `billing.view` |

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | — → Posted | Record payment | `billing.create` + `CanPay` (BIL-040) | session branch selected (TENANT-004); visit exists (BIL-404) and is not billed through claims (BIL-015); treasury exists (BIL-409), is active (BIL-021), belongs to the session branch (BIL-022) and is approved for the user on this device (BIL-020, BR-27); device id present (BIL-006); amount > 0 (BIL-001) with at most two decimals (BIL-002) and ≤ remaining (BIL-010, BR-26); reference for every non-cash method (BIL-003) | — | — | — | `PaymentRecorded` | no |
| T2 | Posted → Voided | Void payment | `billing.cancel` + `CanCancel` (BIL-042) | payment exists (BIL-405); Posted (BIL-012); not inside a closed cash closing (BIL-013) | — | — | **required** (BIL-005) | `PaymentVoided` | no |
| T3 | — → recorded | Record refund | `billing.approve` + `CanRefund` (BIL-041) | as T1 for branch, visit, treasury and device; amount > 0 (BIL-001, BIL-002) and ≤ refundable (BIL-011, BR-14); reason is an active catalogue reason of kind Refund (BIL-016); reference for non-cash (BIL-003) | — | — | **required** reason id (BIL-004) + optional note | `RefundRecorded` | no |
| T4 | — → Posted × n | Pay batch (pay all) | `billing.create` + `CanPay` (BIL-040) | batch exists (BIL-406) and is not LabToLab (BIL-015); station as T1; amount > 0 and ≤ the batch remaining (BIL-014); distributed in accession order up to each member's remaining | — | — | — | `PaymentRecorded` per member | no |
| T5 | — → closed | Close cash drawer | `billing.manage` | session branch selected; treasury exists (BIL-409) and belongs to the session branch (BIL-022); from < to (BIL-030); to not in the future (BIL-031); period ≤ 31 days (BIL-033); counted cash ≥ 0 with two decimals (BIL-032); a non-zero difference carries a note (BIL-034); no closed period of the same treasury overlaps for an intersecting cashier scope (BIL-120) | — | — | note when a difference exists | `CashClosingClosed` | no |

Pay one batch member is T1 on the member visit (the batch page calls the payments endpoint).

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-BIL-001 | A payment never exceeds the remaining balance at the moment it is recorded (BR-26) | `VisitAccount.RecordPayment` + `xmin` on the account root (concurrent cashiers → CONCURRENCY-409; concurrent first payments → UNIQUE-409 on the account key) | BIL-010 | "The amount exceeds the remaining balance." |
| INV-BIL-002 | A refund never exceeds the refundable amount (BR-14) | `VisitAccount.RecordRefund` + `xmin` | BIL-011 | "The amount exceeds the refundable balance." |
| INV-BIL-003 | Receipt, refund and closing numbers are unique per tenant and come from the per-tenant counter (P-12) | `IReferenceNumbers` (`receipt`/RCT, `refund`/RFD, `closing`/CLS) + unique indexes `(tenant_id, number)` | UNIQUE-409 | — |
| INV-BIL-004 | Money records are never edited or deleted: a payment changes only Posted → Voided (void columns); refunds and closings never change (BR-15, P-54) | aggregate (no setters) + triggers `app.billing_refuse_change()` / `app.billing_guard_payment()` (`Billing.migration.sql`) | — | "billing rows are immutable" |
| INV-BIL-005 | Closed periods of one treasury never overlap for intersecting cashier scopes (all ⊇ any cashier) | handler check before the aggregate | BIL-120 | "A closed period of this treasury overlaps the requested period." |
| INV-BIL-006 | A payment inside a closed cash closing is frozen (no void); the correction is a refund in the current period | handler resolves the cover, the aggregate refuses | BIL-013 | "The payment is inside a closed cash closing." |
| INV-BIL-007 | Every payment and refund carries the treasury, device, cashier and the session branch (CP-23, P-30) | aggregate (`CashierStation`) | BIL-020…022, TENANT-004 | — |
| INV-BIL-008 | Expected cash = cash payments − cash refunds; card, POS and transfer are not in the drawer (BR-16) | `ClosingTotals` | — | — |
| INV-BIL-009 | Amounts are positive with at most two decimals | aggregate | BIL-001 / BIL-002 | — |

## 9. Segregation-of-duties rules

None mandated by the SRS. WF-09 has the cashier close their own drawer, so the closer may be the cashier. Voids and
refunds are protected by narrowly granted permissions (`billing.cancel`, `billing.approve`; LDM "sensitive rights:
refund/transfer/cancel narrowly granted", synthesis §7.2) and are fully audited (field changes + ledger). A
"voider is not the original cashier" rule is listed as an open question (§16 Q2).

## 10. Approval gates

None. The cash closing is the control point: it freezes the movements of its period (INV-BIL-006) and records who
closed, when, the counted cash and the difference.

| Item | Cash closing (T5) |
| --- | --- |
| Recorded | closer, server time, period, cashier scope, totals snapshot, counted cash, difference, note |
| After | nothing editable; payments of the period cannot be voided |
| Correction path | a refund (money out) or a new payment in the current period; a wrong closing is not reopened (open question Q11) |

## 11. Events and policies

```text
Event:     PaymentRecorded { PaymentId, VisitId, ReceiptNumber, Amount, Method, TreasuryId, CashierUserId, BranchId, BatchId?, TenantId }
Raised by: T1, T4
Reacts:    none yet (Reporting reads the tables; Registration may mark a visit Completed when results are printed and the balance is settled)

Event:     PaymentVoided { PaymentId, VisitId, ReceiptNumber, Amount, ActorUserId, Reason, TenantId }      Raised by: T2
Event:     RefundRecorded { RefundId, VisitId, Number, Amount, Method, ReasonId, TreasuryId, CashierUserId, BranchId, Comment, TenantId }   Raised by: T3
Event:     CashClosingClosed { ClosingId, Number, TreasuryId, CashierUserId?, PeriodFromUtc, PeriodToUtc, ExpectedCash, CountedCash?, Difference?, ClosedByUserId, TenantId }   Raised by: T5
```

Consumed: **none**. The balance is always computed (§1), so no policy keeps a copy in step:

- `VisitRegistered` / `VisitLinesAdded`: the registration page posts the captured payment through `POST /api/v1/billing/payments`
  right after saving the visit (BR-26, MODULES.md §3).
- `VisitTransferred` (CP-25): the new contract re-prices the visit; `patient_share_total` may drop below what was paid,
  so the remaining balance turns **negative = refund due** and the visit appears on the Refunds page (WF-09 step 3).
- `VisitLineCancelled` (CP-26): same effect as a transfer (receivables adjusted, overpayment refundable).

All events go to `ops.outbox_events` and `audit.audit_trail` in the commit of the change (foundation).

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Amounts, methods, references, receipt / refund / closing numbers, totals | Financial, Audited | references are card slip or transfer numbers, never card numbers (the UI says so) |
| Treasury, device id, branch | Operational | the device id is an opaque browser identifier |
| Cashier, voider, closer ids | Actor fields | names joined at read time from `identity.platform_users` |
| Void reason, refund reason id and note, closing note | Audited | reasons required on T2, T3 |
| Frozen states | Voided payments, every refund, every closing, payments inside a closing | triggers refuse in-place edits |
| Retention | with the tenant's financial records (assumed 10 years, §16) | exports: the registers below, CSV, `billing.export`, at most 10,000 rows (BIL-050) |

## 13. Reference data and configuration

| Source | Use |
| --- | --- |
| Treasury module | treasuries (active, branch) and live Approved access requests for (user, treasury, device) — BR-27 |
| Catalogue B | reasons of kind `Refund` (CP-24 "reason mandatory"); the SPA lists them from `GET /api/v1/catalogue/reasons?kind=Refund` |
| Registration (MODULES.md §5) | `app.visits` totals, `app.visit_lines` (invoice), `app.batches` (batch payment) — raw SQL on the agreed columns |
| Tenant parameters | `autoPrintReceipt` (the SPA prints the receipt after a payment when true; read through `/api/v1/settings`), `defaultCurrency` (documents and amounts) |

No new parameter keys.

## 14. Registers, reports and exports

| Register / document | Filters | Columns | Who |
| --- | --- | --- | --- |
| Visit account | visit id or accession | accession, patient, payer, batch; total list price, discount, patient share, payer share, paid, refunded, remaining, refundable, balance status; payments and refunds of the visit | `billing.view` |
| Payments | visit, accession, from/to, treasury, cashier, method, status, page | receipt no., date, accession, amount, method, reference, treasury, cashier, status, frozen, void reason | `billing.view`; CSV `billing.export` |
| Refunds | visit, accession, from/to, treasury, cashier, method, page | number, date, accession, amount, method, reference, reason, note, treasury, cashier | `billing.view`; CSV `billing.export` |
| Batch account | batch id or number | batch no., kind, payer; per member: accession, patient, list price, discount, patient share, payer share, paid, refunded, remaining, status; totals | `billing.view` |
| Cash-closing preview | treasury, cashier (or all), from/to | totals per method, expected cash, outstanding balance, discounts, visits; movements | `billing.manage` |
| Cash closings | treasury, cashier, from/to, page | number, treasury, cashier scope, period, payments, refunds, expected cash, counted, difference, closed by/at | `billing.view`; CSV `billing.export` |
| Payment receipt (A5) | per payment | lab and branch letterhead, receipt no., date, accession, patient, amount, method, reference, treasury, cashier, patient share / paid / remaining as of printing, status (VOID watermark) | `billing.view` |
| Invoice (A4) | per visit (special right `CanPrintInvoice`) | letterhead, visit, patient, payer; billed lines with list price, discount, patient share, payer share; totals; payments; refunds; balance | `billing.view` |
| Refund receipt (A5) | per refund | letterhead, number, date, visit, patient, amount, method, reason, note; **signature boxes** (patient, cashier) | `billing.view` |
| Cash-closing sheet (A4) | per closing | letterhead, number, treasury, cashier scope, period, totals, expected / counted / difference, note, movements; **handover signatures** (cashier, receiver) | `billing.view` |
| Collections (treasury) report (A4; change request 2026-10-13 item 2, LDM "Total Daily Income" §10.11) | `GET /api/v1/billing/collections-report?from&to&scope&groupBy` — whole laboratory-calendar days (`timeZone` resolution of `ILaboratoryTimeZone`; default today), `scope` User (own posted payments and refunds, every branch) or Branch (every cashier of the session branch; TENANT-004 without a branch, **BIL-044** without `CanViewBranchCollections`), `groupBy` Branch / User / Method (default Method); a start before `EarliestReportDay(today)` → **BIL-045**; `to` < `from` → BIL-030 | `CollectionsReportDocument`: letterhead, scope, grouping, period (days and UTC bounds, half-open), cashier (user scope), one row per key with payment / refund counts (voided payments excluded), payments, refunds and net per method (Cash, Card, Pos, Transfer), totals and net; a grand-total row; printed by/at. Printed from the "Print collections" button of the Payments register (dialog: period, scope, grouping, preview table) | `billing.view` |

## 15. Notifications and escalations

None in this version. A non-zero closing difference is recorded with its mandatory note and shown on the sheet;
an escalation rule is a later Notifications intake.

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Billing`, `tests/Platform.IntegrationTests/BillingModuleTests.cs`
and `tests/Platform.WebApi.FunctionalTests/BillingFlowTests.cs`.

```gherkin
Scenario S-T1-ok: T1 Record payment — partial then full
  Given a visit with a patient share of 300 and an approved treasury on the cashier's device in the session branch
  When the cashier pays 100 in cash, then 200 by card with reference "SLIP-1"
  Then two Posted payments RCT-yyyy-nnnnn exist, the account shows paid 300, remaining 0, status Paid, and PaymentRecorded was raised twice

Scenario S-T1-over: T1 refused above the remaining balance (INV-BIL-001)
  Given remaining 50
  When the cashier pays 60
  Then the attempt is refused with BIL-010 (409) and nothing is recorded

Scenario S-T1-ref: non-cash without reference → BIL-003 (422)
Scenario S-T1-amount: zero, negative or three-decimal amount → BIL-001 / BIL-002 (422)
Scenario S-T1-station: treasury inactive → BIL-021 (409); of another branch → BIL-022 (403); not approved for the user and device (or approval revoked) → BIL-020 (403)
Scenario S-T1-claims: visit of a LabToLab batch → BIL-015 (409)
Scenario S-T1-branch: no session branch → TENANT-004 (403)
Scenario S-T1-race: two cashiers pay the last 100 of a visit at once → one succeeds, the other gets CONCURRENCY-409 (or UNIQUE-409 on a first payment)

Scenario S-T2-ok: T2 Void payment
  Given a Posted payment of 100 outside any closing
  When a user holding billing.cancel voids it with reason "wrong visit"
  Then it is Voided with the reason, voider and time; the account's paid total drops by 100; PaymentVoided is raised

Scenario S-T2-state: voiding a Voided payment → BIL-012 (409)
Scenario S-T2-reason: void without reason → VALIDATION-400 at the API, BIL-005 in the aggregate
Scenario S-T2-frozen: void of a payment inside a closed cash closing → BIL-013 (409)

Scenario S-T3-ok: T3 Record refund
  Given a visit paid 300 whose patient share dropped to 200 after a transfer (remaining −100, refundable 100)
  When a user holding billing.approve refunds 100 in cash with reason R (kind Refund)
  Then a refund RFD-yyyy-nnnnn is recorded, remaining is 0 and RefundRecorded is raised

Scenario S-T3-over: refund above the refundable amount → BIL-011 (409); refund on a visit with nothing refundable → BIL-011
Scenario S-T3-reason: missing reason → BIL-004; unknown or non-Refund reason → BIL-016 (422)

Scenario S-T4-ok: T4 Pay all
  Given a Group batch with members A (remaining 100), B (0), C (150) in accession order
  When the cashier pays all 200
  Then A receives 100 and C 100, two receipts are issued, B is untouched
Scenario S-T4-over: pay all above the batch remaining → BIL-014 (409)
Scenario S-T4-claims: pay all on a LabToLab batch → BIL-015 (409)

Scenario S-T5-ok: T5 Close the drawer
  Given cash payments 500, card 200, cash refunds 50 on treasury T for cashier C today
  When the manager previews and closes with counted cash 450
  Then the closing CLS-yyyy-nnnnn records expected cash 450, difference 0, and CashClosingClosed is raised
Scenario S-T5-diff: counted 440 without a note → BIL-034 (422); with a note → recorded with difference −10
Scenario S-T5-overlap: a second closing overlapping the same treasury for the same cashier (or for all cashiers) → BIL-120 (409)
Scenario S-T5-period: from ≥ to → BIL-030; to in the future → BIL-031; longer than 31 days → BIL-033

Scenario S-CR-own: Collections report, default
  Given a cashier who took 100 cash and 200 by card today
  When the cashier prints the collections report without parameters
  Then the report covers today (laboratory day, 00:00–23:59:59.999 local) for the cashier's own movements grouped by method: Cash 100, Card 200, total 300, 2 payments, 0 refunds
Scenario S-CR-branch: scope Branch without CanViewBranchCollections → 403 BIL-044; with it → one row per cashier of the session branch (grouping User) or per branch / method
Scenario S-CR-limit: a role with ReportDaysLimit 0 asks for a period starting yesterday → 403 BIL-045; today → 200 (empty rows when nothing was taken)
Scenario S-CR-shape: scope or groupBy outside the enum names, or to before from → VALIDATION-400
Scenario S-CR-cells: the grouping query counts posted payments only, refunds by their method, per (branch, cashier, method); another tenant sees nothing

Scenario S-AUTHZ: any command without the permission → 403 AUTHZ-001 before the handler runs
Scenario S-RIGHTS: a user holding the permission but not the CP-37 special right → 403 BIL-040 (pay), BIL-041 (refund), BIL-042 (void), BIL-043 (invoice); nothing is recorded and no number is drawn
Scenario S-TENANT: accounts, payments, refunds and closings of tenant X are invisible to tenant Y (query filter + FORCE RLS)
Scenario S-IMMUTABLE: an UPDATE of a refund or a closing, or of a voided payment, is refused by the trigger
```

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | Which rights record payments, refunds and voids? | Permission and special right together: `billing.create` + `CanPay`, `billing.approve` + `CanRefund`, `billing.cancel` + `CanCancel`; the invoice needs `CanPrintInvoice` (BIL-040…043, 403). The SPA cannot see special rights (the profile carries permission keys only), so it shows the refusal message. |
| Q2 | Must a void be done by someone other than the cashier who took the payment? | No SoD; `billing.cancel` is granted narrowly and every void is audited with its reason. |
| Q3 | Does BR-27 (approved treasury per device) apply to card, POS and transfer as well as cash? | Yes: CP-29 says "a payment can only be taken on an approved treasury/device pair"; every movement carries a treasury and a device. |
| Q4 | Can a visit registered in branch A be paid in branch B? | Yes; the movement is stamped with the session branch, and the treasury must belong to the session branch. |
| Q5 | Do non-cash refunds need a reference? | Yes, like payments (BR-26). |
| Q6 | Is the refund receipt always printed? | Yes (for the patient's signature, LDM §10.8); the payment receipt follows `autoPrintReceipt`. |
| Q7 | What are "outstanding balances" and "discounts" on the closing? | Σ positive remaining and Σ visit discount of the **visits touched in the period** (a payment or refund on the treasury within the cashier scope), as of closing time. |
| Q8 | Must a closing difference be explained? | Yes: a non-zero difference needs a note (BIL-034). |
| Q9 | Longest closing period? | 31 days (BIL-033); periods are half-open `[from, to)`. |
| Q10 | May two closings overlap? | No, for the same treasury when the cashier scopes intersect (a specific cashier and "all cashiers" intersect). |
| Q11 | Can a closing be reopened? | No. Corrections are new movements in the current period. |
| Q12 | Pay all with more than the batch owes? | Refused (BIL-014); the form defaults to the batch remaining. |
| Q13 | Can a member visit of a lab-to-lab batch be paid individually? | No (BIL-015): billed to the sending lab through claims. |
| Q14 | Which balance does a reprinted receipt show? | The figures as of printing (labelled so). |
| Q15 | Money precision | Two decimals, `numeric(14,2)`, one tenant currency (`defaultCurrency`). |
| Q16 | A payment committed in the very instant a closing of the same period is being saved | Not counted in that closing but frozen by it; closings are taken at the end of the shift with `to` = now. |
| Q17 | Retention | With the tenant's financial records (10 years). |
| Q18 | Cashiers without `settings.view` cannot read `autoPrintReceipt` through `/api/v1/settings` | The SPA falls back to the code default (`true`); integrator to decide whether an authenticated read of print parameters is exposed. |
| Q19 | Digits of amounts and dates (validation v0.5.0 F-09, 2026-10-12) | **Decision.** On screen an Arabic page writes amounts, counts and dates in Arabic-Indic digits like the rest of the portal (`formatMoney`, `formatDateTime`, `formatCount`); the receipt, refund receipt, invoice and closing sheet keep Latin digits in both languages (`formatPrintMoney`, `formatPrintDateTime`), and the CSV exports are written by the server in Latin digits. Counted texts take their Arabic forms ("زيارتان", "٧ إيصالات", "١٢ إيصالًا"; F-11). |
| Q20 | Collections report (change request 2026-10-13 item 2): does the member's `ReportDaysLimit` apply to this report only or to every report? | Assumed: it is the member's limit for **every** report; the collections report is its first consumer (BIL-045 when the period starts before `EarliestReportDay(today)`), and the Reporting module should apply the same check. Today is the laboratory's calendar day (`ILaboratoryTimeZone`: client country zone → `timeZone` parameter → Africa/Cairo), not the browser's; the SPA bounds the date inputs with the browser's today and the server decides. |
| Q21 | Which branch does the "own payments" scope cover? | Assumed: every branch the user took money in (the movements carry their branch, so grouping by Branch shows the split); the "whole branch" scope is the session branch only. The letterhead names the session branch when one is selected. |
| Q22 | What does "amount per collection method" mean when refunds exist? | Assumed: the report shows, per method, the posted payments, the refunds and the net (payments − refunds), plus the counts; voided payments are excluded from amounts and counts (they are not collections). Method rows are ordered Cash, Card, Pos, Transfer; cashier and branch rows by name. |

Integration dependencies (wave 1/2 modules built in parallel; placeholders marked `[FallbackAdapter]`):

| Port (Billing) | Placeholder now | Real adapter |
| --- | --- | --- |
| `IRefundReasonLookup` | accepts any non-empty reason id, no names | reads `app.reasons` (Catalogue B) — kind Refund, active |
| `IBillingDirectory` | neutral letterhead, no patient / service / payer names | tenant name (`identity.tenants`), branch name (`app.branches`), patients (`app.patients`), services (`app.services`), payers (Pricing) |
| `IVisitBalanceLookup` (declared in `Platform.Application.Registration`) | — (Billing ships the real adapter) | consumed by Registration |
