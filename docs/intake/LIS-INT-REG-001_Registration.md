# LIS-INT-REG-001 — Registration: visits, orders, tubes, bookings, batches, transfers and cancellations (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `registration` (`PermissionCatalog.Registration`), build slot `reg`, wave 2 (MODULES.md §2) |
| SRS pages | CP-06 Registration, CP-07 Home reservation, CP-08 Batch registration, CP-09 Lab-to-Lab registration, CP-10 Search registrations, CP-12 Price query (page), CP-25 Money transfer, CP-26 Cancellations |
| Workflows / rules | WF-02 (walk-in), WF-03 (home visit), WF-04 (batch and lab-to-lab, registration steps), WF-09 (transfer and cancellation steps), WF-10 (price query side); BR-01, BR-02, BR-05, BR-06, BR-12, BR-13, BR-15, BR-24, BR-26 |
| Domain reference | LDM synthesis `docs/knowledge/ldm/LDM_Business_Knowledge.md` §2.1 (identifiers), §2.2 (tube generation), §2.3 (Reserved), §2.4 (Rank/Relative), §3.4 (duration, frequency check, test days), §3.8 (panels, test rules, shortcuts, diagnosis), §4.1–§4.9 (registration family), §7.2 (Query, Money Transfer, Delete Patient), §13 (weaknesses) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), MODULES.md and the LDM synthesis. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.5 — 2026-10-24 (change request LIS-CR-008 P-SMP: "collected outside" per sample, §20); 1.4 — 2026-10-20 (CP-10b: no Cancel on a line with a verified or printed result); 1.3 — 2026-10-19 (change request LIS-CR-003 P-C9: Accession details page CP-10b and the C11 menu links, §18); 1.2 — 2026-10-12 (validation LIS-VR-002 open points: test-level duplicates Q23, labels Q13, VIP on the worklist Q12; v0.5.0: VIP on the opened result case Q12, Latin digits on the job order and counted wording Q13, mobile search with the trunk 0) |
| Implemented in | `src/Platform.Domain/Registration`, `src/Platform.Contracts/Registration`, `src/Platform.Application/Registration` (commands, queries, `OrderPlanner`, read ports, policies), `src/Platform.Infrastructure/Persistence/Registration` (+ `Registration.migration.sql`), `src/Platform.WebApi/Controllers/Registration`, `frontend/src/app/features/registration`, `frontend/src/app/core/api/registration.contracts.ts`, `frontend/public/i18n/registration.{en,ar}.json` |
| Codes | `REG-001`…`REG-099` validation and privilege refusals, `REG-100`…`REG-199` conflicts, `REG-201`…`REG-299` advisory warnings (never thrown), `REG-404`/`405`/`406` not found |

## 1. Purpose and policy basis

Registration is where the laboratory accepts work. A **visit** (accession) groups the services ordered for one patient
at one branch on one contract; every later record hangs off it: the tubes and their sample numbers (Samples), the
results per test (Results), the money (Billing), the claims (Reporting). A wrong registration is a wrong tube, a wrong
report or a wrong invoice (LDM §2.4), so the clerk never types prices, tubes or turnaround: they are derived.

- **Identifiers** (BR-01, LDM §2.1): the accession is issued by the system, sequential per tenant and year, never
  reused; every tube gets a sample number that is never reused. Patient numbers belong to the Patients module.
- **Price from the contract** (BR-06, LDM §2.4): payer and contract tier select each service's list price, patient share
  and payer share through Pricing's quote; an unpriced service cannot be ordered on that contract.
- **Tube merge** (BR-02, LDM §2.2): services of the same clinical group, sample type and sample condition share one
  tube; any difference splits them. Driven by the catalogue, never by the clerk.
- **Reserved keeps the bill** (BR-05, LDM §2.3/§4.4): a service whose precaution is unmet is kept on the visit and in the
  bill without a tube; releasing it later issues (or joins) a tube.
- **Frequency check** (BR-12, LDM §3.4): a test ordered again for the same patient inside the test's frequency window
  alerts (the clerk confirms) or is prevented, per the test's setting — across all branches (LDM §13 weakness fixed).
- **Turnaround clock** (BR-13, LDM §3.4/§6): the expected result date counts only the test's working days.
- **Nothing is erased** (BR-15, LDM §7.2 "Delete Patient"): cancellations, transfers and discounts keep the original
  data with reason, user and time; no row of this module is ever deleted.
- **Validated selection only** (BR-24): services, payers, tiers, doctors, diagnoses, reasons and preparations are picked
  from master data and checked on the server.
- **Payment inside registration in Release 1** (BR-26): the registration page captures the payment and posts it to
  the Billing API right after the visit is saved; the payment itself is Billing's record.

## 2. Scope and out of scope

In scope:

- Visit life cycle: register (walk-in, home conversion, batch member, lab-to-lab member), add services, release Reserved
  services, update the visit details, apply/change/clear the discount, cancel services with a reason, contract transfer
  (preview and apply), line share adjustment, completion (method, called by later policies), patient re-pointing after a
  merge (policy on `PatientsMerged`).
- The order planner shared by every entry path and the dry run (`POST …/visits/check`): catalogue resolution, diagnosis
  sets, test rules, prices, gender restriction, duplicate refusal, frequency check, contract alerts, precautions,
  expected dates, tube plan, preparation questions.
- Home reservations (bookings): create, edit, cancel, convert (= register with the booking in the same unit of work).
- Batches: group (one contract, services chosen once) and lab-to-lab (sending lab, services per row, collected-outside
  per row), one visit per member, add members later.
- Documents (typed DTOs printed by the SPA's print kit): labels, job order, price quote.
- Registers: visit search, visit detail, bookings, batches, cancelled services, transfer log; CSV export.
- Read ports exposed: `IVisitLookup` (Billing, Results, Reporting); declared and consumed: `IResultStatusLookup`
  (NoOp fallback until Results ships), `IServiceEntryFinder`, `ISendingLabLookup`, `IRegistrationDirectory`.
- The SPA pages of CP-06, CP-07, CP-08, CP-09, CP-10, CP-12, CP-25, CP-26 and the job-order page linked by Samples.

Out of scope (owner):

- Patient files, similar-name warning, merge (Patients, CP-11); the registration page embeds a copy of its picker.
- Samples, collection, receipt, rejection (Samples): created by its policy on `VisitRegistered` / `VisitLinesAdded`.
- Results (Results, wave 3): created by its policy on the same events; the verified-result check is a port.
- Payments, refunds, receipts, batch payment, balance definition (Billing; BR-14). Registration only reads the balance.
- Claims to payers and sending labs (Reporting, CP-31).
- Location / department / room (R2 hospital fields: SRS release-1 scope), recurring check-ups (LDM `Checkup Days`),
  scan-attach (Patients documents), SMS of results.

## 3. Glossary

| Business word (SRS / LDM) | Platform word |
| --- | --- |
| Visit, order, accession, Acc. No. (رقم الزيارة) | `Visit` aggregate (`app.visits`), `Accession` = `ACC-yyyy-nnnnn` (`IReferenceNumbers` kind `accession`) |
| Ordered service, selected test (الخدمة) | `VisitLine` (`app.visit_lines`), kind `Service`, `Package` or `PackageMember` |
| Panel / package / mega (single price, several services) | a `Package` line carrying the price + one `PackageMember` line per member service (price 0) carrying the tube and the tests |
| Tube, sample, Sample ID (رقم العينة) | `VisitTube` (`app.visit_tubes`), `SampleNumber` = `SMP-yyyy-nnnnn` (kind `sample`) |
| Reserved (محجوز) | `VisitLineStatus.Reserved` (BR-05) |
| Rank / Relative | payer / contract tier (`PayerId`, `ContractTierId`) |
| Patient / Insurance share | `PatientShare` / `PayerShare` |
| Discount, Disc. Per. | visit discount: kind `Amount` or `Percent`, value, reason (catalogue reason of kind Discount) |
| Precaution, preparation question | service `Precaution` (warning) and `PreparationQuestion` answers (`app.visit_preparation_answers`) |
| Test rule `{A}+{B} >> {C}` | companion services added by `IOrderRulesLookup.ServicesAddedByRulesAsync` |
| Diagnosis (ICD) tests | services added by `IOrderRulesLookup.DiagnosisAsync` |
| Shortcut number / shortcut set | `ServiceEntry` resolution (`GET /api/v1/registration/service-entry?text=`) |
| Result date, Duration, Test Days | `ExpectedResultAtUtc`, `TurnaroundCalculator` |
| Done By, collector | `VisitLine.CollectorUserId` (home visits: the phlebotomist) |
| Home reservation, Booking No. | `Booking` aggregate (`app.bookings`), `BKG-yyyy-nnnnn` |
| Batch registration / Lab-to-Lab, Batch No. | `Batch` aggregate (`app.batches`), `BTH-yyyy-nnnnn`, kind `Group` / `LabToLab` |
| Sample collected inside the lab (unticked) | `Visit.CollectedOutside` = true |
| Money transfer: Rank Transfer / Normal Transfer | `TransferContract` / `AdjustLineShares`, logged in `app.visit_transfers` |
| Delete Patient (cancel tests) | `CancelLines` (status Cancelled with reason, actor, time) |
| Query (price quote) | `POST /api/v1/registration/quote` → `PriceQuoteDocument` |
| Sampling details printout | job order document |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `registration.view` | search and read visits, documents (labels, job order), preparation questions, price quote, bookings, batches, cancelled-services list, transfer log |
| `registration.create` | register a visit (incl. booking conversion), the dry run, service entry resolution, create a booking, register a batch and add members |
| `registration.edit` | add services, release Reserved services, update visit details, set or clear the discount, edit a booking |
| `registration.cancel` | cancel services (also needs the special right **Cancel**), cancel a booking |
| `registration.approve` | contract transfer preview and apply, line share adjustment (both also need the special right **Transfer**) |
| `registration.export` | CSV export of visits, bookings and cancelled services |
| `registration.manage` | reserved (no endpoint in this version) |

Special rights (`IUserPrivileges.SpecialRights`, Organization): **discount cap** (`EffectiveDiscountCap(discountCapPercent)`),
**Cancel**, **Transfer**, **See VIP** (VIP patients are masked in the search register without it). Actors: reception
clerk (view, create, edit), cashier (view), accounts officer (view, approve + Transfer right), supervisor (cancel +
Cancel right), phlebotomist (bookings). Deny by default; every command and query carries exactly one policy.

Cross-module permissions used by the pages: `catalogue.view` (service picker F2, reasons, doctors, diagnoses, sending
labs), `pricing.view` (tier picker), `patients.view` / `patients.create` (patient picker), `billing.create` + right
**Pay** (R1 payment panel), `samples.view` (samples dialog).

## 5. Aggregates

All roots are sealed, tenant-scoped (`(tenant_id, id)`, query filter, FORCE RLS), carry `RowVersion` (xmin) and the
audit stamps; children are tenant entities keyed `(tenant_id, id)` with a composite FK to their root and change only
through the root. Every transition of a visit increments `Revision`, so the root row (and its xmin token) changes with
any child change and concurrent edits of one visit serialise (CONCURRENCY-409).

- **Visit** (root, `app.visits`): `Accession` (≤ 20, unique), `PatientId`, `OriginalPatientId?` (first file before a
  merge re-point), `BranchId` (session branch, TENANT-004), `LocationType` (`Walkin | Home | Batch | LabToLab`),
  `TreatmentNo?` (≤ 50), `ReferringDoctorId?`, `PayerId`, `ContractTierId`, `InsuranceId?` (≤ 50), `DiagnosisId?`,
  `BookingId?`, `BatchId?`, `Urgent`, `Biohazard`, `CollectedOutside`, `Status` (`Open | Completed`), `RegisteredAtUtc`,
  `RegisteredByUserId`, `CompletedAtUtc?`, `UtcOffsetMinutes` (the registering desk's offset, −840…840, for test days),
  totals `TotalListPrice`, `DiscountTotal`, `PatientShareTotal`, `PayerShareTotal` (numeric(12,2); Ordered + Reserved
  lines; patient share net of discount), discount (`DiscountKind?` `Amount | Percent`, `DiscountValue`,
  `DiscountReasonId?`, `DiscountReason?` text snapshot, `DiscountByUserId?`, `DiscountAtUtc?`), `Revision`.
  Children:
  - **VisitLine** (`app.visit_lines`): `ServiceId`, `ServiceCode`, `ServiceNameEn/Ar` (snapshot at ordering), `Kind`
    (`Service | Package | PackageMember`), `ParentLineId?` (member → package line), `ServiceType` (catalogue type
    name), `TubeId?`, `Status` (`Ordered | Reserved | Cancelled`), `ListPrice`, `PatientShare` (net of the allocated
    discount), `PayerShare`, `Discount`, `ExpectedResultAtUtc?`, `TestIds` (uuid[], the tests results are created for),
    `CollectorUserId?`, `AddedByRuleId?`, `Origin` (`Manual | Rule | Diagnosis`), `Sequence`, `OrderedAtUtc`,
    `OrderedByUserId`, `ReleasedAtUtc?`, `ReleasedByUserId?`, cancellation (`CancellationReasonId?`,
    `CancellationReason?`, `CancelledByUserId?`, `CancelledAtUtc?`).
  - **VisitTube** (`app.visit_tubes`): id (= the tube id Samples keys on), `SampleNumber` (unique), `ClinicalGroupId`,
    `SampleTypeId`, `SampleConditionId?`, snapshot `UnitCode`, group / sample type / condition names (en/ar),
    `CreatedAtUtc`.
  - **VisitPreparationAnswer** (`app.visit_preparation_answers`): `PreparationId`, question snapshot (en/ar), `Answer`
    (≤ 400), `AnsweredAtUtc`, `AnsweredByUserId`.
  - **VisitTransfer** (`app.visit_transfers`): `Kind` (`Contract | LineShares`), `LineId?`, from/to payer and tier,
    patient and payer share before/after (visit totals for a contract transfer, line values for a share adjustment),
    `ReasonId?`, `Reason`, `ActorUserId`, `AtUtc`.
  Value logic in the domain: `TubePlanner` (BR-02), `TurnaroundCalculator` (BR-13), `DiscountAllocator`
  (proportional allocation with largest-remainder rounding to cents), `VisitOrderItem` (a resolved service to order).
- **Booking** (root, `app.bookings`): `Number` (`BKG-…`, unique), `BranchId`, `PatientId?` or patient data
  (`PatientName` ≤ 200, `Gender?`, `Mobile?` ≤ 20, `AgeYears?`), `Address` (1–500, mandatory), `VisitAtUtc`,
  `PhlebotomistUserId?`, `PayerId`, `ContractTierId`, `ServiceIds` (uuid[] 1–100), `Notes?` (≤ 1000), `Status`
  (`Booked | Converted | Cancelled`), `VisitId?`, `Accession?`, `ConvertedAtUtc?`, `ConvertedByUserId?`,
  `CancellationReason?`, `CancelledByUserId?`, `CancelledAtUtc?`.
- **Batch** (root, `app.batches`): `Number` (`BTH-…`, unique), `Kind` (`Group | LabToLab`), `BranchId`, `PayerId`,
  `ContractTierId`, `SendingLabId?` (LabToLab), `ReferringDoctorId?`, `GroupName?` (≤ 200, Group), `Courier?` (snapshot of
  the sending lab's courier, en/ar), `Notes?`, `RegisteredAtUtc`, `RegisteredByUserId`, `MemberCount`. The members are
  the visits carrying `BatchId` (one visit per member, each with its own accession).

Other aggregates are referenced by id only: Patient (Patients), Service / LabTest / ClinicalGroup / SampleType (Catalogue
A), Doctor / Diagnosis / Reason / Preparation / SendingLab (Catalogue B), Payer / ContractTier (Pricing), Branch / user
(Organization). No foreign keys across modules in this wave (MODULE-PATTERN §3); the handler validates every id.

### Order planning (one algorithm for every entry path and the dry run)

```text
input: patient (gender, file status, mobile, national id) | none (price quote), payer + tier, branch, diagnosis?,
       requested services [(serviceId, reserved)], the visit's live lines (adding to a saved visit), now, utcOffset
1. requested ids in order; a repeated id → REG-100
2. diagnosis → its services are added (origin Diagnosis), skipping ids already present and inactive services
3. test rules: ServicesAddedByRulesAsync(all live ids incl. the visit's) → companions added (origin Rule) at their own
   price; a companion that is inactive, unpriced or gender-restricted is skipped with warning REG-207
4. catalogue: every service and every package member service (ICatalogueLookup); tests of all of them
5. refusals per manual / diagnosis service: unknown REG-012; inactive or not orderable REG-013 (BR-24);
   gender of the service or of one of its tests ≠ patient REG-015; duplicate of a live line (or of a package member)
   REG-100; no price under the contract REG-014 (BR-06); every test of the service already on the visit through another
   service (a profile, a package member, an earlier line of the same request) REG-109, naming each test and the service
   that orders it; a service that adds at least one new test is kept with warning REG-206 naming the repeated tests
   (assumption Q23). Automatic companions (rule, diagnosis) whose tests are all present are skipped silently
6. frequency (BR-12): per test with FrequencyCheckDays, the same test on a non-cancelled line of another visit of the
   patient ordered within the window → Prevent: REG-101 (refused) | Alert: warning REG-201 (Register needs
   confirmFrequencyWarnings = true, else REG-102)
7. warnings: contract alert condition REG-202, precaution REG-203, added by rule REG-204, added by diagnosis REG-205,
   repeated tests REG-206
8. expected result date per line (BR-13): hours = service TurnaroundHours when > 0, else each test's DurationHours;
   counted from now on the test's TestDays only (local days of the desk's offset); line = latest of its tests;
   package = latest of its members; a service without tests and without turnaround has none
9. tube plan (BR-02) and preparation questions (PreparationsForAsync(all service ids, patient gender))
```

### Tube generation (BR-02)

```text
A line needs a tube when it is Ordered, of kind Service or PackageMember, of a sample-carrying type
(Test, Profile, Culture, TextReport) and its service has a clinical group and a sample type.
key = (clinical group, sample type, sample condition | none)
A tube is live while at least one of its lines is Ordered.
For each line needing a tube (in order): join the first live tube of the visit with the same key, else create a new
tube (new id, next SMP number). Reserved, Cancelled, Package and Additional lines carry no tube.
Releasing a Reserved line or adding a line later applies the same rule; a key whose tube has no live line any more
(all its lines cancelled, Samples cancelled the sample) gets a new tube id (Samples intake Q15).
```

### Discount

```text
base   = Σ gross patient share of the top-level (Service, Package) Ordered + Reserved lines
amount = Percent: round(base × value / 100, 2)  |  Amount: min(value, base)   (recomputed on every line change)
cap    = the actor's EffectiveDiscountCap(discountCapPercent); applied percent = amount / base × 100 must be ≤ cap (REG-060)
the amount is allocated to the lines in proportion to their gross patient share, cents by largest remainder;
line.PatientShare = gross − line.Discount; the payer share is never discounted
```

## 6. State table

| Aggregate / state | Meaning | Editable | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Visit **Open** | the visit in progress | details, discount, lines (add, release, cancel), contract and shares | no | no | `registration.view` |
| Visit **Completed** | results reported and settled (set by later policies) | cancellation, contract transfer, line shares (money stays correctable) | details, discount, new lines | no | `registration.view` |
| Line **Ordered** | billed, tube issued (when it needs one), results expected | cancel; shares through the transfer flows | identity, service, snapshot | no | `registration.view` |
| Line **Reserved** | billed, no tube, no results yet (BR-05) | release, cancel | as Ordered | no | `registration.view` |
| Line **Cancelled** | removed from bill, tube and results; kept with reason (BR-15) | none | yes | yes | `registration.view` |
| Booking **Booked** | home visit planned | everything except the number | no | no | `registration.view` |
| Booking **Converted** | the visit exists (visit id, accession) | none | yes | yes | `registration.view` |
| Booking **Cancelled** | withdrawn with a reason | none | yes | yes | `registration.view` |
| Batch (no states) | header of a group of visits | members can be added | header | no | `registration.view` |

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| V1 | — → Open | Register visit | `registration.create` | session branch (TENANT-004); patient exists (REG-001) and Active (REG-002); mandatory mobile (REG-009) / national id (REG-010) / doctor (REG-007) per settings; doctor selectable in the branch (REG-008); payer active (REG-005); tier of the payer and active (REG-006); diagnosis active (REG-011); 1–200 services (REG-003/004); planner refusals REG-012…015, REG-100, REG-101, REG-109; frequency alerts confirmed (REG-102); preparation answers valid (REG-016); discount (REG-017/018/019/039/060); booking Booked (REG-107) and same patient (REG-108) when converting | — | — | discount reason when discounted | `VisitRegistered`, `VisitDiscountChanged` (when discounted), `BookingConverted` | no |
| V2 | Open → Open | Add services | `registration.edit` | visit exists (REG-404); Open (REG-103); planner as V1 against the live lines; answers; frequency confirmation | — | — | — | `VisitLinesAdded` | no |
| V3 | Reserved → Ordered | Release Reserved services | `registration.edit` | Open (REG-103); lines of the visit (REG-024); Reserved (REG-105); not a package member (REG-025) | — | — | — | `VisitLinesAdded` | no |
| V4 | Ordered / Reserved → Cancelled | Cancel services | `registration.cancel` + right Cancel (REG-061) | lines of the visit (REG-024); live (REG-104); not a package member alone (REG-025); no verified result (REG-106, `IResultStatusLookup`); reason required (REG-021), a given reason id of kind Cancellation (REG-037) | — | — | **required** | `VisitLineCancelled` per line (package: the package and each member) | no |
| V5 | Open → Open | Update details | `registration.edit` | Open (REG-103); doctor rules (REG-007/008) | — | — | — | — (field changes audited) | no |
| V6 | Open → Open | Set / clear discount | `registration.edit` | Open (REG-103); reason of kind Discount (REG-017/018); value valid (REG-019); something to discount (REG-039); within the cap (REG-060) | — | — | **required** to set | `VisitDiscountChanged` | no |
| V7 | Open / Completed | Contract transfer (preview = query) | `registration.approve` + right Transfer (REG-062) | new payer/tier valid (REG-005/006); different from the current (REG-038); every live service priced by the new contract (REG-014); reason (REG-022), a given reason id of kind Transfer (REG-037) | — | — | **required** | `VisitTransferred`, `VisitTransferRecorded` | no |
| V8 | Open / Completed | Adjust line shares | `registration.approve` + right Transfer (REG-062) | line of the visit (REG-024), live (REG-104), not a package member (REG-025); both shares ≥ 0 and their sum unchanged (REG-023); reason (REG-022) | — | — | **required** | `VisitLineSharesAdjusted` | no |
| V9 | Open → Completed | Complete | policies of later modules (no endpoint) | Open; idempotent | — | — | — | `VisitCompleted` | yes |
| V10 | any → any | Re-point patient | policy on `PatientsMerged` | the visit belongs to the merged file; idempotent | — | — | the merge reason | `VisitPatientRepointed` | yes |
| B1 | — → Booked | Create booking | `registration.create` | session branch; patient file Active (REG-001/002) or patient name + mobile (REG-028); address (REG-027); visit time not more than one day in the past (REG-029); phlebotomist is a user of the tenant (REG-030); payer/tier (REG-005/006); 1–100 services, active and orderable (REG-012/013) | — | — | — | `BookingCreated` | no |
| B2 | Booked → Booked | Edit booking | `registration.edit` | exists (REG-405); Booked (REG-107); as B1 | — | — | — | — | no |
| B3 | Booked → Cancelled | Cancel booking | `registration.cancel` | Booked (REG-107); reason (REG-021) | — | — | **required** | `BookingCancelled` | no |
| B4 | Booked → Converted | Convert booking | `registration.create` (V1 with `bookingId`) | as V1; the visit is LocationType Home, its lines' collector = the phlebotomist; same unit of work | — | — | — | `BookingConverted` + V1 events | no |
| T1 | — → batch + N visits | Register batch | `registration.create` | Group: payer/tier (REG-034), group name; LabToLab: sending lab active (REG-031) — payer, tier, courier and doctor come from it; 1–200 members (REG-032), no patient twice (REG-033); each member as V1 (refusals carry the row number) | — | — | — | `BatchRegistered`, one `VisitRegistered` per member | no |
| T2 | batch + N | Add batch members | `registration.create` | batch exists (REG-406); as T1 per member | — | — | — | `BatchMembersAdded`, `VisitRegistered` per member | no |

Queries (no state change): visit search / detail, labels, job order, preparations, dry run (`registration.create`),
service entry (`registration.create`), price quote, transfer preview (`registration.approve`), bookings, batches,
cancelled services, transfer log (`registration.view`), exports (`registration.export`).

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-REG-001 | Accession, sample, booking and batch numbers are issued by the per-tenant counter, unique, never changed (BR-01) | `IReferenceNumbers` (P-12) + unique indexes `(tenant_id, accession|sample_number|number)` + trigger `visits_guard` | UNIQUE-409 | — |
| INV-REG-002 | A service appears at most once among the live lines of a visit, package members included | aggregate + planner | REG-100 | "The service is already on this visit." |
| INV-REG-002b | A service is not ordered when every test it orders is already on the visit through another service (assumption Q23; validation O-3) | planner (dry run, register, add services, quote) | REG-109 | "Every test of this service is already on the visit: CREA (PKG › RFT)." |
| INV-REG-003 | Tubes follow BR-02: one live tube per (group, sample type, condition) at a time; only Ordered sample-carrying lines hold a tube; Reserved, Cancelled, Package and Additional lines hold none | aggregate (`TubePlanner`) | — | — |
| INV-REG-004 | Totals = Σ of the Ordered + Reserved lines; patient share net of discount; discount ≤ base | aggregate (recomputed on every change) | REG-019 | "The discount is not valid." |
| INV-REG-005 | A discount has a reason and stays within the actor's cap when applied | aggregate (cap supplied by the handler from the privileges) | REG-017 / REG-060 | "A discount needs a reason." / "The discount exceeds your limit." |
| INV-REG-006 | A cancelled line keeps its service, prices, reason, actor and time; nothing is deleted (BR-15) | aggregate (no remove) + trigger `registration_refuse_delete` on every table of the module | REG-021 | "A cancellation needs a reason." |
| INV-REG-007 | Package members share the status of their package; they are never released or cancelled alone | aggregate | REG-025 | "This service belongs to a package; act on the package." |
| INV-REG-008 | Lines change only while the visit is Open, except cancellation, contract transfer and share adjustment | aggregate state guard | REG-103 | "The visit is completed." |
| INV-REG-009 | A share adjustment keeps the line's gross total (patient + payer) and both shares ≥ 0 | aggregate | REG-023 | "The patient and payer shares must add up to the line total." |
| INV-REG-010 | A booking is converted at most once and only from Booked; a converted booking names its visit | aggregate + `CHECK ck_bookings_conversion` | REG-107 | "The booking is no longer open." |
| INV-REG-011 | Every visit carries the session branch, a payer and a tier; a re-pointed visit keeps its original patient id | aggregate + NOT NULL columns | — | — |

## 9. Segregation-of-duties rules

None fixed. Cancellation and transfer are protected by their permission **and** a special right (Cancel, Transfer),
a mandatory reason and the audit ledger (LDM §7.2 "sensitive rights narrowly granted; one login per person"). Assumption
Q11: no "registrar ≠ canceller" rule.

## 10. Approval gates

None. A contract transfer is itself the act of the holder of `registration.approve` + Transfer right after the payer's
approval arrived off-system (WF-09 step 3); the preview is mandatory in the UI, not a gate.

## 11. Events and policies

```text
Event:    VisitRegistered { VisitId, Accession, BranchId, PatientId, PayerId, ContractTierId, Urgent, RegisteredByUserId,
                            Tubes[TubeId, SampleNumber, ClinicalGroupId, SampleTypeId, SampleConditionId, ServiceIds],
                            Lines[LineId, ServiceId, TubeId, Status Ordered|Reserved, TestIds] }      (shape fixed, MODULES.md §3)
Raised by: V1, B4, T1, T2
Reacts:   Samples P1 (one sample per tube, idempotent on TubeId; auto collect/receive per settings) — one outbox cycle
          Results P1 (one result per test of every Ordered line, idempotent on LineId + TestId) — wave 3
          A Package line carries no tube and no tests; each PackageMember line carries its tube and its tests.

Event:    VisitLinesAdded { VisitId, Accession, BranchId, PatientId, ActorUserId, Tubes (new tubes and existing tubes that
                            gained services, with every live service of the tube), Lines (added or released lines) }
Raised by: V2, V3          Reacts: Samples P1 / T1b, Results P1 (same idempotent rules)

Event:    VisitLineCancelled { VisitId, Accession, LineId, ServiceId, TubeId, ActorUserId, Reason }   (one per line)
Raised by: V4              Reacts: Samples P2 (cancel the tube when no live line remains), Results (cancel the line's results)

Event:    VisitTransferred { VisitId, Accession, From/To payer and tier, ActorUserId }
Raised by: V7              Reacts: none (Billing recomputes the balance on read, BR-14; an overpayment becomes refundable)

New events of this module (reasons feed the audit ledger):
  VisitTransferRecorded { VisitId, Accession, TransferId, From/To payer and tier, PatientShareBefore/After, ActorUserId, Reason }  V7
  VisitLineSharesAdjusted { VisitId, Accession, LineId, ServiceId, Patient/Payer share before and after, ActorUserId, Reason }  V8
  VisitDiscountChanged { VisitId, Accession, Kind?, Value, Amount, ActorUserId, Reason }                                     V1, V6
  VisitCompleted { VisitId, Accession }                                                                                      V9
  VisitPatientRepointed { VisitId, Accession, FromPatientId, ToPatientId, ActorUserId, Reason }                              V10
  BookingCreated { BookingId, Number, BranchId, VisitAtUtc } · BookingCancelled { …, ActorUserId, Reason } · BookingConverted { BookingId, Number, VisitId, Accession, ActorUserId }
  BatchRegistered { BatchId, Number, Kind, MemberCount, ActorUserId } · BatchMembersAdded { BatchId, Number, Added, ActorUserId }

Policy (this module): when PatientsMerged then re-point every visit and every Booked booking of the merged file to the
survivor (OriginalPatientId keeps the first file; idempotent: nothing is left on the merged id after the first run).
Timing: one outbox cycle.
```

Billing reacts to nothing at registration: the SPA saves the visit and then posts the captured payment (BR-26).

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Accession, sample numbers, booking and batch numbers, status, flags, totals | Operational, Audited | numbers immutable |
| Patient id (names and contact read at display time from Patients) | Personal (health context) | names are never copied into visits; the booking's patient data (name, mobile, address) is **PII** |
| Booking address, mobile, notes | **PII**, Audited | visible to `registration.view` |
| Services, tubes, expected dates, preparation answers | **Health data**, Audited | answers may reveal conditions (fasting, medication) |
| Prices, shares, discounts, transfers | Commercial, Audited | discount and transfer reasons required |
| VIP patients | Confidential | masked (name, mobile) in the search register and excluded from name/mobile search without the See-VIP right |
| Retention | life of the tenant's medical records; nothing is deleted (BR-15; trigger refuses DELETE on every table of the module) | |

## 13. Reference data and configuration

| Source | Key / list | Default | Use |
| --- | --- | --- | --- |
| Tenant setting | `mandatoryMobile`, `mandatoryNationalId` | false | V1 refuses a file without mobile / national id (REG-009 / REG-010) |
| Tenant setting | `mandatoryDoctor` | false | V1 / V5 refuse a visit without a referring doctor (REG-007) |
| Tenant setting | `discountCapPercent` | 20 | the cap of a role that defers to the tenant (`EffectiveDiscountCap`) |
| Tenant setting | `labelCopies` | 1 | copies on the labels document |
| Tenant setting | `autoPrintLabels`, `autoPrintJobOrder`, `autoPrintReceipt` | true | read by the SPA after save (`GET /api/v1/registration/settings`) |
| Tenant setting | `accessionFormat` | `ACC-{yyyy}-{nnnnn}` | **not applied yet**: accessions use the fixed `ACC-yyyy-nnnnn` (MODULES.md §1, OQ-07) |
| Tenant setting | `defaultCurrency` | EGP | currency printed on the price quote |
| Catalogue A | services, members, tests (timing, gender), groups, sample types | — | `ICatalogueLookup` |
| Catalogue B | diagnoses, test rules, preparations, shortcut sets, doctors, reasons (Discount, Cancellation, Transfer), sending labs | — | `IOrderRulesLookup`, `IDoctorLookup`, `ISendingLabLookup` |
| Pricing | payers, tiers, quotes | — | `IPayerLookup`, `IPriceLookup` |
| Counters | `accession` ACC, `sample` SMP, `booking` BKG, `batch` BTH | — | `IReferenceNumbers` |

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Visits (CP-10) | accession, patient no., name, mobile, from/to (registration date), branch, payer, status, patient, page | accession, date, branch, patient no. + name (masked VIP), payer / tier, status, services with Ordered / Reserved / Cancelled styling, total, discount, patient share, paid, remaining | `registration.view` |
| Visit detail | — | header, patient, payer / tier, doctor, lines (status, shares, discount, expected date, tube, collector, cancellation), tubes, answers, discount, transfers, balance | `registration.view` |
| Bookings (CP-07) | status, from/to (visit time), phlebotomist, search (number, name, mobile), page | number, visit time, patient, mobile, address, phlebotomist, payer, services, status, accession | `registration.view` |
| Batches (CP-08/09) | kind, from/to, search (number, group, lab), page | number, kind, date, group / sending lab, payer / tier, members, patient share total | `registration.view` |
| Cancelled services (CP-26) | from/to (cancellation time), branch, page | accession, patient, service, shares, reason, by, at | `registration.view` |
| Transfer log (CP-25) | from/to, kind, page | accession, kind, service, from → to contract, shares before → after, reason, by, at | `registration.view` |
| CSV exports | as the register (≤ 10 000 rows, else REG-050) | as the register, both languages, UTF-8 BOM, formula-injection safe | `registration.export` |
| Documents | — | labels (one per live tube × `labelCopies`), job order (CP-06 / CP-15), price quote (CP-12) | `registration.view` |

## 15. Notifications and escalations

None in this version. (Contract alert conditions and precautions are shown to the clerk at ordering, not sent.)

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Registration`, `tests/Platform.Application.UnitTests/Registration`,
`tests/Platform.IntegrationTests/RegistrationModuleTests.cs` and `tests/Platform.WebApi.FunctionalTests/RegistrationFlowTests.cs`.

```gherkin
Scenario S-V1-ok: register a walk-in visit
  Given an Active patient, the cash tier and two Test services of chemistry on serum and one of haematology on whole blood
  When the clerk registers the three services
  Then the visit gets ACC-yyyy-nnnnn, two tubes with SMP numbers (chemistry serum, haematology blood), totals = Σ shares,
       expected dates from the turnaround on test days, and VisitRegistered carries both tubes and the three lines
Scenario S-V1-reserved: a Reserved service is billed without a tube (BR-05); VisitRegistered lists it as Reserved without a tube
Scenario S-V1-package: a package adds one priced Package line and one PackageMember line per member; the members join the tubes of their key
Scenario S-V1-dup: ordering the same service twice, or a service already inside a package of the visit → REG-100 (409)
Scenario S-V1-dup-tests: CREA ordered next to a package holding a profile holding CREA → REG-109 (409) naming "CREA (PKG › RFT)"; RENAL (CREA + UREA) next to CREA → registered with warning REG-206 "CREA"; adding CREA to a saved visit holding the package → REG-109
Scenario S-V1-gender: a female-only service for a male patient → REG-015 (422)
Scenario S-V1-unpriced: a service the contract does not price → REG-014 (422)
Scenario S-V1-inactive: an inactive or not orderable service → REG-013 (422)
Scenario S-V1-freq-alert: a test with FrequencyCheckDays 30 / Alert ordered 10 days ago → the dry run warns REG-201; Register without confirmation → REG-102 (409); with confirmation → registered
Scenario S-V1-freq-prevent: the same with Prevent → REG-101 (409), with or without confirmation
Scenario S-V1-rule: rule {A}+{B} >> {C}: ordering A and B adds C at its own price with origin Rule (warning REG-204)
Scenario S-V1-diagnosis: a diagnosis adds its services (warning REG-205), never twice
Scenario S-V1-discount: 10 % with a Discount reason inside a 20 % cap is allocated to the lines; 30 % → REG-060 (403); no reason → REG-017
Scenario S-V1-mandatory: mandatoryDoctor on and no doctor → REG-007; mandatoryMobile on and the file has no mobile → REG-009
Scenario S-V1-merged: registering on a Merged file → REG-002
Scenario S-V2-join: adding a chemistry serum service to a saved visit joins the existing live tube (no new tube); VisitLinesAdded lists the tube with every live service
Scenario S-V2-newtube: adding a service whose key's tube has only cancelled lines creates a new tube id
Scenario S-V3-release: releasing a Reserved line issues or joins its tube and raises VisitLinesAdded; releasing an Ordered line → REG-105
Scenario S-V4-cancel: cancelling with a reason keeps the line with reason, actor and time, recomputes the totals and raises one VisitLineCancelled per line
Scenario S-V4-right: a user without the Cancel special right → REG-061 (403)
Scenario S-V4-verified: a line with a verified result → REG-106 (409)
Scenario S-V4-package: cancelling a package cancels its members; cancelling a member alone → REG-025
Scenario S-V7-transfer: moving the visit from cash to an insurer tier re-prices the live lines, logs the transfer with reason, raises VisitTransferred; the same tier → REG-038; without the Transfer right → REG-062
Scenario S-V8-shares: shifting 50 from patient to payer on a line keeps the line total; a changed total → REG-023
Scenario S-V10-merge: PatientsMerged re-points the visits of the merged file and keeps the original patient id; a second delivery changes nothing
Scenario S-B1-ok / S-B1-address: a booking without an address → REG-027; converting registers the visit with LocationType Home, collector = phlebotomist, booking Converted; converting twice → REG-107
Scenario S-T1-group: a group batch of three members creates three visits with one batch number; the same patient twice → REG-033
Scenario S-T1-lab: a lab-to-lab batch takes payer, tier and doctor from the sending lab; collected-outside is kept per member
Scenario S-BR13: 24 h from Thursday 10:00 on a Sunday–Thursday test → Sunday 10:00 (Friday and Saturday do not count)
Scenario S-AUTHZ: without registration.create the API answers 403 AUTHZ-001; a shape violation answers 400 VALIDATION-400; without a session branch TENANT-004
Scenario S-TENANT: visits, lines, tubes, bookings and batches of tenant X are invisible to tenant Y (query filter + FORCE RLS)
```

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | Is a payer/tier mandatory on every visit? | Yes. The page defaults to the tenant's cash default tier; the server requires both. |
| Q2 | How are packages (panels / megas) represented on a visit? | One priced `Package` line + one `PackageMember` line (price 0) per member service carrying its tube and tests; members follow the package's status. A member already on the visit is a duplicate (REG-100). |
| Q3 | Which turnaround applies to a line: the service's or the tests'? | The service's `TurnaroundHours` when > 0, else each test's `DurationHours`, counted on that test's test days from registration (release time for a released line), in the local days of the registering desk (the browser's UTC offset, stored on the visit) until a tenant time zone exists. The latest test wins. |
| Q4 | Does the discount touch the payer share? | No: it reduces the patient share only (the cash collected at the desk); allocated to lines in proportion to their gross patient share. |
| Q5 | Is the discount cap checked again when later changes make the applied percent exceed it (e.g. a cancellation)? | No: the cap is checked when a user applies or changes the discount; a later amount discount is clipped to the base, a percent discount is recomputed. |
| Q6 | Are preparation answers mandatory? | No; questions are shown, answers are optional, but every answer must belong to an applicable question and match its type (Yes/No, an option of the list, text ≤ 400). |
| Q7 | Frequency check scope | Same tenant, every branch, the patient's file (merged files' visits are re-pointed to the survivor), non-cancelled lines of other visits; the window is FrequencyCheckDays counted back from now. |
| Q8 | Companion services from test rules that cannot be ordered (inactive, unpriced, wrong gender) | Skipped with warning REG-207; they never block the registration. |
| Q9 | May a visit be cancelled as a whole? | By cancelling all its services (the visit stays, Open, with zero totals and every line Cancelled). |
| Q10 | Which reasons? | Discount: a catalogue reason of kind Discount (id mandatory). Cancellation: a reason of kind Cancellation **or** free text (one of them mandatory). Transfer: a reason of kind Transfer **or** free text (one of them mandatory). |
| Q11 | SoD between registering and cancelling | None; protected by permission + special right + reason + audit. |
| Q12 | VIP visibility | **One rule with PAT intake Q8 — assumption, awaiting the business owner (validation A-03, 2026-10-11).** Without the See-VIP special right VIP patients are masked (name, mobile) in the visit search and excluded from name/mobile search; the same rule now applies to the patient register (CP-11), its export and the patient picker, which also withhold the Arabic name and the national id and find a VIP file only by its full patient number or full national id (so reception can still register the patient). The similar-file check of a new patient warns with a masked entry (validation R-06, 2026-10-12): a VIP file with the typed mobile or a similar name is listed as "VIP patient" with its number, sex, age and match reason, so no duplicate file is created, and the clerk may use it (choosing a VIP file by its number is allowed). The results worklist (CP-18), its bench list and its export, and the matching-visits list of Results by accession (CP-19) follow the same rule (validation 2026-10-11; RES Q19): the name reads "VIP patient" without the right, a name search does not find the file, and the accession, sample number and patient number still do; an opened CP-19 case and its report preview are masked too, and a VIP patient's report prints only with the right (RES-056, validation v0.5.0 F-06, RES Q19). Registration keeps the identity at the desk: the visit, its documents and the patient file show the real name (operational need). A mobile typed with the trunk 0 in the visit search also finds a file stored without it (PAT Q14, F-08). |
| Q13 | Label copies | `labelCopies` per tube (the services' own label counts are informational in this version). Each label carries "!" for an urgent visit and "BIO" (a boxed text mark, readable in every label font) for a biohazard visit (validation 2026-10-11); its date uses Latin digits in both languages, like the printed reports. The job order, the price quote and the registration receipt print their dates and amounts in Latin digits too, whatever the screen language (validation v0.5.0 F-10, 2026-10-12); on screen an Arabic page shows Arabic-Indic digits, and counted messages take their Arabic forms (dual, 3–10 plural, 11+ singular: "صدرت عينتان.", "فترة التكرار ٣٠ يومًا"; F-11). |
| Q14 | Booking without a patient file | Allowed with name + mobile (+ gender, age); the file is chosen or created on the registration page when converting. A booking that names a file converts only on that file (REG-108). |
| Q15 | Batch members who have no file | The SPA creates the files first through the Patients API (patients.create), then submits the batch with patient ids (cross-module writes only through each module's API). |
| Q16 | Lab-to-lab billing | The visits carry the sending lab's payer and tier; claims bill the sending lab (Reporting). No payment panel on CP-09. |
| Q17 | Who completes a visit? | Later policies (results printed + balance settled) call `Complete`; no endpoint in this version. |
| Q18 | Accession format setting | Not applied; `ACC-yyyy-nnnnn` until OQ-07 (tenant-configurable formats) is decided. |
| Q19 | Payment captured on the page (R1) | Posted by the SPA to `POST /api/v1/billing/payments` right after the visit is saved (BR-26). A failed payment leaves the visit saved and shows the error with a link to the Payments page; it is never retried silently. |
| Q20 | Line share adjustment semantics (LDM "Normal Transfer") | Moves value between the patient and the payer share of one line; the line total (gross patient + payer) is unchanged. Changing the actual charge of a line is a contract transfer or a discount, not a share adjustment. |
| Q21 | Can a Completed visit be transferred or have services cancelled? | Yes (money stays correctable for claims and refunds); new services need an Open visit. |
| Q22 | Shortcut numbers and sets at the service entry | Resolved on the server in this order: exact shortcut number, exact service code, exact short name or alias, shortcut set code; otherwise the picker opens with the typed text. |
| Q23 | Does "refuses duplicates" (SRS actor table) cover tests ordered through different services? | **Assumption, awaiting the business owner (validation O-3, 2026-10-11).** Yes at test level: a service whose every test is already on the visit through another service (a profile, a member of a package, a profile inside a package) is refused with REG-109, which names each repeated test and the service that orders it ("CREA (PKG › RFT)"). A service that adds at least one new test is allowed, with warning REG-206 naming the repeated tests; its price is not reduced. Services without tests (additional services) are never refused by this rule. |

## 17. Change request 2026-10-13 item 1 — patient demographics from Search registrations

CP-10 gains the row action **Patient** (`patients.edit`, icon `user`): the patient file opens in a drawer with the shared
patient form prefilled from `GET /api/v1/patients/{id}`. Before the form shows, the drawer reads
`GET /api/v1/patients/{id}/demographics-lock?visitId=<row>`; when `canEditIdentity` is false the identity fields (title,
names, gender, date of birth or age, condition, national id, nationality) are read-only with a hint naming the status a
result of this visit has reached and the user's limit (`DemographicsEditUpTo`, CP-37), while the contact fields stay
editable. Save (button or F10) calls `PUT /api/v1/patients/{id}` with the row's `visitId` and the file's row version; the
server decides (PAT-032 → 403, shown in the drawer); on success the drawer closes, a toast confirms and the register
reloads so the name and mobile columns follow. The rule, the code and the read port are specified in LIS-INT-PAT-001 §17;
Registration adds no table and no endpoint. The lock is judged on the row's visit (a visit without live results is judged
patient-wide); editing from the patient file (CP-11) judges every visit of the patient.

## 18. Change request 2026-10-19 (LIS-CR-003, package P-C9) — Accession details page (CP-10b) and the C11 menu links

Source: `docs/build/CR-2026-10-19_Prototype-Gaps.md` §5; prototype v3 difference C9 and C11. Frontend only: Registration adds no
table, no endpoint and no code; every read already existed and is called through `RegistrationApi` (the feature never imports
another feature).

**What changes.** A new client page **Accession details** at `/registration/accession` (`?accession=` or `?visitId=`; nav entry
client · reception · `registration.view`) shows one visit with the actions reception needs in one place:

- *Search strip* — one box (accession, patient number, mobile or name, classified as CP-10 does) run on Enter or the button, never
  while typing. Exactly one match — or, among several, the one whose accession is exactly the text — opens; several show a
  compact list to pick from; none says so. `GET /api/v1/registration/visits` (all days, all branches, page size 20); the
  register's accession filter is a contains-match, so an exact lookup (previous / next, `?accession=`) narrows the answer client-side.
- *Sticky header* — accession, patient (the CP-10 patient drawer, `patients.edit`, PAT-032 lock judged on this visit), sex and age,
  VIP / urgent / biohazard / collected-outside flags, status, branch, registered at and by, payer and tier, doctor, treatment number,
  batch; *previous / next accession* (number ± 1, looked up before it opens: a missing one only says so); *Edit registration*
  (`registration.edit`) → `/registration/visits/{id}`.
- *Ordered tests* — every line (package members under their package), code, name, type, sample number and tube type, line status
  (cancelled rows highlighted and kept with reason, user and time), list price, patient and payer shares, expected result, the result
  status of the line (from the Results module, see below), *Cancel* on a live top-level line (`registration.cancel`; the existing cancel
  dialog opens with the row preselected, BR-15).
- *Samples* (`samples.view`) — `GET /api/v1/samples?visitId=`: number, unit, type and condition, status, collected / received times,
  rejection reason; *Label* (the registration label of the tube) and *Collect* while the sample is Ordered (`samples.create`, a confirm,
  then `POST /api/v1/samples/scan` with mode `Collect` — the phlebotomy scan of CP-14). With auto-collect on (Release 1) the button
  rarely shows.
- *Results* (`results.view`) — `GET /api/v1/results?visitId=`: the status of every report unit the user may see, its result count,
  panic / delta / printed marks, the count of units and results hidden by the user's result rights; *Enter results* (`results.create`)
  / *Open results* → `/results/by-accession?accession=`.
- *Payments* (`billing.view`) — `GET /api/v1/billing/visits/{id}/account`: patient share, paid, refunded, remaining, refundable and the
  balance status; posted payments and refunds (receipt number, method and reference, amount, cashier and treasury, time; voided
  payments struck through); *Take payment* (`billing.create`) → `/billing/payments?visitId=`, *Refund* (`billing.approve`) →
  `/billing/refunds?visitId=`, *Receipt* prints a payment's receipt through the existing receipt document, *All payments* → the
  payments register (C11). When Billing refuses the account, the visit's own balance (`VisitDetail.balance`) gives the figures.
- *Documents* — labels, job order (print and on screen).
- *Cancel* is offered only while the server would accept it: a line (or a package through any member) with a verified or printed
  result shows "Verified — cannot be cancelled" instead of the button (`AccessionFacade.lockedLines`, REG-106; fix of 2026-10-20 after
  the first use on the installed 0.15.0). The server stays the judge: a cancellation the page offers can still be refused.
- Keyboard: Enter in the search box runs it, Esc closes the match list and the dialogs.

Entry points: the visits register (CP-10) row action *Details*; the registration success panel's *Accession details* button
(`?visitId=`). C11 links: the registration page's *no treasury* warning links to `/treasury/access`; the Treasury access page shows a
*Treasuries* button for `treasury.manage`; the Treasuries page links back to *Treasury access*.

**Rules kept.** VIP identity stays masked for users without See-VIP (the server masks `VisitDetail.patient`; the page shows the
masked label and offers no patient drawer). Every button is affordance (`*appCan`); the server decides. Each card's read is asked
only with the permission its module checks and read tolerantly: a refusal or an absent module is an empty card, never a broken page.
A line's result status is the earliest stage among its live results (a profile is Complete only when every test is); a package shows
the earliest of its members; Cancelled results never hide a live one.

**Open questions and assumptions.**

- Q31 — *Collect* from the desk: assumed allowed for any user holding `samples.create` (the same right as the CP-14 scan). If the
  business wants collection confined to the phlebotomy page, the button is removed (one line).
- Q32 — Previous / next by accession number ± 1 crosses branches and days; a visit the user may not open (another branch without the
  right) answers the server's refusal. Assumed acceptable, as on the results page.
- Q33 — The search opens a single match at once, including a single name match. Assumed wanted (the desk types an accession most of
  the time); the list can be forced by widening the text.

## 19. Change request 2026-10-20 (LIS-CR-004) — payment on CP-06 without a treasury

Backend facts only (the SPA side is package P-SPA-REG): the payment captured on the registration page (BR-26) posts
`POST /api/v1/billing/payments {visitId, amount, method, reference?, methodCode?}`; the cashier is the session user and the branch the
session branch, so no treasury, device id or approved access is needed any more (BR-27 withdrawn, intake LIS-INT-TRS-001). The
accession details page (CP-10b, §18) lists the payments and refunds of the visit without a treasury column; rows posted before the
change keep their treasury id in the database only. `GET /api/v1/billing/my-treasuries` is gone (404).

## 20. Change request 2026-10-24 (LIS-CR-008, package P-SMP, reviewer point C-02) — "collected outside the laboratory" per sample

**Change.** The flag moves from the visit to each tube: `VisitTube.CollectedOutside` (`app.visit_tubes.collected_outside`, boolean,
default false) and `VisitTube.CollectedOutsideAtUtc` (`collected_outside_at_utc`, the collection time the clerk typed, null = the
registration time). The visit keeps a **derived** `Visit.AnyCollectedOutside` (not stored) that the visit detail, the job order, the
visits register row and `IVisitLookup` return as `collectedOutside`. `app.visits.collected_outside` stays as history: since this
change it is written once, at registration, and is true only when the old visit-level field marked every tube.

**Order contract.** `POST /api/v1/registration/visits` takes `samples[]` = `{ tubeKey?, sampleTypeId?, collectedOutside,
collectedAtUtc? }`: an entry names one tube of the dry run (`TubePreview.key`, `group:type:condition`) or, without a key, every tube of
the sample type; only entries with `collectedOutside: true` count. The old visit-level `collectedOutside` is kept for one release: sent
true **without** `samples[]`, it marks every tube (batch members keep using it). `PUT …/visits/{id}/details` still accepts
`collectedOutside` and ignores it (the flag is set at registration only); `VisitDetails` lost the field. The tube event
`RegisteredTube` carries `CollectedOutside` and `CollectedAtUtc` (defaults keep older outbox payloads readable); tubes added later to a
saved visit are collected in the laboratory.

**Codes.** `REG-040` (422) an entry that matches no tube of the order (stale key, a type not ordered, a text that is not a key, a
reserved service, which has no tube) or names neither; `REG-041` (422) a collection time more than 5 minutes ahead (a time inside the
skew is kept as the registration time) or older than 30 days. Shape: at most 200 entries, key of at most 100 characters, key or type
required (VALIDATION-400).

**Documents.** The label of an outside tube prints "EXT" (`SampleLabel.CollectedOutside`) beside "!" and "BIO"; the job order marks the
tube "· EXT" and keeps the visit line "Sample collected outside the laboratory" when any tube is outside.

**CP-06.** The visit chip is gone; once the dry run plans tubes, `ui/sample-flags` lists one row per new tube (unit letter, sample type,
condition) with a "Collected outside the laboratory" switch and, when on, an optional "Collected at" (`datetime-local`, not later than
now; empty = registration time); "All outside" / "None outside" switch every tube (shown with two or more tubes). The edit page
(CP-10 › Edit) shows the saved live tubes read-only: "Outside" with the recorded time, or "Collected in the laboratory". The samples
dialog of CP-10 and the samples card of the accession page (CP-10b) show an "Outside" tag per tube.

**Migration (integrator, `ReviewFeatures`).** Columns `app.visit_tubes.collected_outside` (boolean not null default false),
`app.visit_tubes.collected_outside_at_utc` (timestamptz null), `app.samples.collected_outside` (boolean not null default false); then the
dated block of `Persistence/Registration/Registration.migration.sql` (visit flag to every tube of the visit; tube flag to its
generation-0 sample). The tube guard (`app.visit_tubes_guard`) protects number and key only, so the backfill passes it.

**Open questions (assumed answers).**

| # | Question | Assumed answer |
| --- | --- | --- |
| C02-Q1 | The prototype speaks of a tube "colour"; the catalogue has none. | Rows show the unit letter, the sample type and the condition; a tube colour waits for a catalogue field. |
| C02-Q2 | May the flag change after save (edit page)? | No: it is set at registration; the edit page lists it read-only. A mistake is corrected by cancelling and re-ordering the service. |
| C02-Q3 | How far back may the collection time lie? | 30 days; never in the future (5 minutes of clock skew tolerated). |
| C02-Q4 | Turnaround clock of an outside sample | Starts at its arrival (registration), not at the outside collection time. |
