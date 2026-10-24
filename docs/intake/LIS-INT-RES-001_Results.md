# LIS-INT-RES-001 — Results and quality control (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `results` (Sky LIS Client Portal: CP-18 Worklist, CP-19 Results by accession, CP-20 Results by sample, CP-21 Work queue, CP-13 Print reports) and `quality` (CP-22 Quality control, placeholder scope); workflows WF-06, WF-07 (landing analyser results); business rules BR-03, BR-04, BR-08, BR-09, BR-10, BR-11, BR-13, BR-19, BR-20; status models "Test / result" and "Work-queue colour states" |
| Slot / wave | `res`, wave 3 (MODULES.md §2) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001) and the LDM knowledge base (§2.3, §3.3–§3.7, §5, §6, §8, §12). **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.2 — 2026-10-24 (reference range before the first save, LIS-CR-007 P-RES C-08, §17); 1.1 — 2026-10-12 (validation v0.5.0: VIP on the opened case and report, RES-056, Q19; one "late" definition, Q21; report language, digits and counted wording, Q22) |
| Implemented in | `src/Platform.Domain/{Results,Quality}`, `src/Platform.Contracts/{Results,Quality}`, `src/Platform.Application/{Results,Quality}` (+ `Results/Policies`), `src/Platform.Infrastructure/Persistence/{Results,Quality}`, `src/Platform.WebApi/Controllers/{Results,Quality}`, `frontend/src/app/features/results`, `frontend/src/app/core/api/results.contracts.ts`, `frontend/public/i18n/results.{en,ar}.json` |

> **Results page follow-ups (2026-10-14).** CP-19 with a visit open: previous / next visit from the context strip (the neighbour in
> the matching list across its pages, else the accession number ± 1 looked up first), unsaved values confirmed away before leaving a
> visit, the keyboard and colour hints behind the *i* button of the unit bar, a compact row density saved as the member's preference
> (`PUT /api/v1/account/preferences` with `resultsDensity`, intake ACC-001), and the strip pinned on narrow screens.

## 1. Purpose and policy basis

A **result** is the medical track of one test of one visit (LDM §2.3 "test track": Ordered → Complete → Review →
Verification → Printed). It is the clinical core of the laboratory: the value a technician or an analyser records,
flagged against the reference range that applies to this patient (BR-08: sex, age, patient condition; LDM §2.5, §3.3),
compared with the patient's previous result (delta check, BR-11; LDM §5.6), reviewed, then **verified** — the
electronic signature of the responsible person (BR-09; LDM §5.5 "Verify → Verified (enables printing)") — and printed
on the laboratory report of its **report unit** (LDM §2.6: "one printable report: tests grouped by clinical group; each
mega profile is its own report"). No value may be recorded before the tube is Received (BR-03, the gate of LDM §2.3),
nothing is ever erased (every value change and every status change is kept with user, time and source; LDM §5.3
"RL"/"SL"), and only verified results print (BR-09, LDM §5.7) unless the laboratory switches `printUnverified` on.

Quality control (CP-22) is a placeholder in the SRS ("scope to be confirmed"): this intake delivers control materials
with levels and targets, control runs, the Levey-Jennings chart and the Westgard flags 1-2s, 1-3s, 2-2s and R-4s.

## 2. Scope and out of scope

In scope:

- Creation of one result per test of every Ordered line when a visit is registered or lines are added (policy on the
  Registration events, MODULES.md §3), with the report unit, the tube, the expected result time (BR-13) and the payer.
- Result entry by test type (numeric with decimals and reportable limits, non-numeric text, optional and restricted
  lists, formula computed automatically, formatted text paragraphs, culture with organisms and sensitivities), flags,
  panic detection, delta check with acknowledgement; review, verify, undo, amend (modify verified), comments and notes.
- Landing analyser and PCR results (WF-07, policy on `AnalyserResultReceived`), with the Received gate and the
  analyser's overwrite switches; refusals written to the result log.
- Cancellation of a line's results (policy on `VisitLineCancelled`), re-pointing results of merged patients
  (`PatientsMerged`), re-timing the turnaround clock at collection (`SampleCollected`), following payer transfers
  (`VisitTransferred`).
- Worklist (CP-18), results by accession (CP-19), results by sample (CP-20), work queue (CP-21), print reports with
  print lists (CP-13), quality control (CP-22); documents: laboratory report per report unit, abbreviated worksheet,
  worklist (bench list); CSV export of the lists.
- Read ports for other modules: `IResultStatusLookup` (declared by Registration; real adapter here). The dashboard
  (CP-05) reads its result figures with its own SQL over `app.results` (Reporting); the unused `IResultKpiLookup` was
  removed on 2026-10-12 (validation v0.5.0 F-15, Q21).

Out of scope (later intakes or other modules): auto-verification (Automation, CP-63/64; consumes this module's
events), result attachments and voice notes (LDM §3.7, CP-19 "attachments": needs the Files capability per result),
SMS/e-mail of reports and the web results portal, the "search by test result" statistics screen (LDM §5.1), analyser
specific reference ranges (LDM §3.3 "analyser range → test setup range"; the analyser mapping carries them, landing
uses the test's range in this version), QC import from instruments and QC blocking of patient results.

## 3. Glossary

| Business word (SRS / LDM) | Platform word |
| --- | --- |
| Test result, line of the results grid | `Result` aggregate (`app.results`), one per (visit line, test) |
| Report, printable record, report unit (tab of CP-19) | `ReportUnitKey`: `{visit}-{clinical group}` (32-hex ids), or `{visit}-{line}-{service}` for a service printed alone (LDM mega "own report sheet") |
| Profile / package banner | the result's `ServiceId` (the member service that owns the test) inside the line's `LineServiceId` |
| Test status Ordered / Complete / Review / Verification / Printed | `ResultStatus` Ordered / Complete / Reviewed / Verified / Printed (+ Cancelled) |
| Printed flag (`P`), print count | `PrintCount` > 0 (independent of the status when `printUnverified` is on) |
| Reference range, N.C. range, panic, reportable (LDM §3.4) | `ResolvedRange` (low, high, text, panic low/high, reportable low/high), resolved at entry (BR-08) |
| Flag H / L / HH / LL / abnormal | `ResultFlag` High / Low / CriticalHigh / CriticalLow / Abnormal (Normal) |
| Panic value | `IsPanic` + event `ResultPanicDetected` |
| Delta check (§5.6) | `DeltaFlag`, `DeltaPercent`, `PreviousResultId`, acknowledgement by/at |
| RL — result log; SL — status log | `ResultLogEntry` rows (`app.result_log_entries`), kinds Value / Status / … |
| Comment (prints), Comments (internal, never prints) | `ReportComment`, `InternalNote` |
| Verify = e-signature | `VerifiedByUserId`, `VerifiedAtUtc`, `VerifiedByName` (name as of the action) |
| Print list, print list no. | `PrintList` aggregate (`app.print_lists`, number `PRL-yyyy-nnnnn`) |
| Abbreviated report (worksheet) | document `worksheet` |
| QC material, level, target, SD, run | `QcMaterial` (+ levels), `QcRun` |

## 4. Actors and permissions

| Permission key | Grants | Typical actor |
| --- | --- | --- |
| `results.view` | every read: worklist, visit search, results of a visit / sample, logs, previous results, work queue, print lists, previews and documents, comment library and microbiology lists for entry | everyone in the laboratory and reception |
| `results.create` | enter / save values (manual entry, culture, formatted text), report comment, internal note | technician |
| `results.edit` | amend (modify) a Verified or Printed result — together with the special right *modify verified result* | unit head |
| `results.approve` | review, verify (one and bulk), acknowledge a delta flag | unit head, pathologist, microbiologist |
| `results.cancel` | undo review / undo verify (reason required) | unit head |
| `results.export` | print (mark printed): one report unit or a print list; CSV exports (affordance) | reception, technician |
| `results.manage` | print lists: create, add report units or accessions, remove units | reception |
| `quality.view` | QC materials, runs, Levey-Jennings data | technician, unit head |
| `quality.create` | record a QC run | technician |
| `quality.edit` | edit a material (names, lot, expiry, levels) | unit head |
| `quality.manage` | create, deactivate, reactivate materials | unit head |

**Result rights** (Organization, CP-37; LDM §8 `S R V E P` per medical group and branch) decide inside the aggregate
for the result's clinical group and the **branch of the visit** (§16 Q3): *see* (results of groups the user cannot see
are not returned), *edit* (enter, amend), *review*, *verify* (and undo verify), *print*. Special rights: *modify
verified result* (amend), *preview without verify* (preview with missing values). A command needs a branch selected
for the session (`BranchScope.Require`, TENANT-004); policies take the actor from the event.

## 5. Aggregates

- **Result** (root, tenant-scoped, `app.results`): `VisitId`, `Accession`, `LineId`, `LineServiceId` (the ordered
  service), `ServiceId` (the member service that owns the test), `TestId`, `TestType`, `IsHiddenHelper`, `TubeId?`,
  `ClinicalGroupId`, `ReportUnitKey`, `PatientId`, `BranchId` (visit branch), `PayerId?`, `Urgent`, `ServiceSequence`,
  `MemberOrder`, `RegisteredAtUtc`, `ClockStartUtc`, `ExpectedAtUtc` (BR-13), `Status`, value (`NumericValue?`,
  `TextValue?` as displayed, `FormattedReport?` paragraphs, `CultureReport?` organisms with sensitivities),
  `Unit?`, `Decimals`, `Range` (owned `ResolvedRange?`), `Flag?`, `IsPanic`, delta (`DeltaFlag`, `DeltaPercent?`,
  `PreviousResultId?`, `PreviousValue?`, `DeltaAcknowledgedByUserId?`, `DeltaAcknowledgedAtUtc?`), source (`Source`,
  `AnalyserId?`, `AnalyserCode?`, `AnalyserFlags?`, `MeasuredAtUtc?`), actors and times (`EnteredBy/At`,
  `ReviewedBy/At`, `VerifiedBy/At`, `VerifiedByName`, `FirstPrintedAtUtc`, `LastPrintedAtUtc`, `PrintCount`,
  `LastPrintListNumber?`, `AmendmentCount`, `CancelledAtUtc`, `CancellationReason`), `ReportComment?`, `InternalNote?`,
  `RowVersion` (xmin).
- **ResultLogEntry** (child entity of Result, `app.result_log_entries`, composite FK `(tenant_id, result_id)`): `Kind`
  (Ordered, Value, Status, Refused, DeltaAcknowledged, Comment, Note, PatientMoved), `FromStatus?`, `ToStatus?`,
  `ValueBefore?`, `ValueAfter?`, `Source?`, `AnalyserId?`, `IntakeId?` (idempotency of analyser landing), `AtUtc`,
  `ByUserId?`, `ByName?`, `Detail?` (reason, refusal code and message). Appended by the root only, never changed
  (append-only trigger, BR-15).
- **PrintList** (root, `app.print_lists`): `Number` (`PRL-yyyy-nnnnn`, `IReferenceNumbers` kind `print-list`),
  `Status` (Open, Printed), `BranchId`, `PrintedAtUtc?`, `PrintedByUserId?`, entries (owned, `app.print_list_entries`:
  `ReportUnitKey`, `VisitId`, `Accession`, `ClinicalGroupId`, `AddedAtUtc`, `Position`).
- **QcMaterial** (root, `app.qc_materials`): `Code` (unique), `Name` (LocalizedText), `TestId`, `AnalyserId?`, `Lot?`,
  `ExpiresOn?`, `IsActive`, levels (owned, `app.qc_material_levels`: `Level` 1–3, `Name`, `TargetMean`, `TargetSd` > 0).
- **QcRun** (root, `app.qc_runs`): `MaterialId`, `TestId`, `Level`, `Value`, `MeasuredAtUtc`, `TargetMean`/`TargetSd`
  (as of the run), `ZScore`, Westgard flags (`Rule12s`, `Rule13s`, `Rule22s`, `RuleR4s`), `Outcome` (Accepted,
  Warning, Rejected), `Comment?`, `RecordedByUserId`.

Value objects: `ResolvedRange`, `FormattedReport` (paragraphs: title, text), `CultureReport` (`NoGrowth`, isolates:
organism id, code and names as of entry, quantity, comment, sensitivities: antibiotic id, code, abbreviation, names,
interpretation code, MIC), `ResultTestRules` (type, decimals, unit, list values — handed in by the handler from the
catalogue read port), `ResultActor` (user, name, the five rights and two special rights for this result).

Other aggregates are referenced by id only: Visit, visit line and tube (Registration), Sample (Samples), Patient,
test, service, clinical group, report template, organism, antibiotic, sensitivity value (Catalogue), analyser
(Integration), branch, payer.

## 6. State table

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Ordered | test ordered, no value yet | value (T2), comment, note | no | no | `results.view` + see right |
| Complete | a value is recorded (manual, analyser or formula) | value (T2), comment, note, delta acknowledgement | no | no | idem |
| Reviewed | first-pass check done | value (T2 returns to Complete), comment, note, delta acknowledgement | no | no | idem |
| Verified | signed by the verifier (BR-09); printable | note only; value and comment through Amend (T6) or Undo (T5) | yes (value) | no | idem |
| Printed | verified and printed at least once | note only; reprint; Amend (T6) | yes (value) | no | idem |
| Cancelled | the visit line was cancelled | none | yes | yes | idem |

Report unit status (BR-04, status model "Test / result"): the lowest status of its non-cancelled results; a profile
banner (results of one member service) shows the lowest status of its members. The printed flag of a unit is "any
result printed". Work-queue colour states (fixed meaning, colours per installation): Incomplete, Completed, Reviewed,
Verified × Printed / Not printed (+ "Reserved & Not printed", never produced: Reserved lines have no results, §16 Q9).

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | — → Ordered | Order | policy P1 (`VisitRegistered`, `VisitLinesAdded`) | ids present (RES-001); idempotent on (LineId, TestId); Reserved lines and Additional services get none | — | — | — | — (field history + log row) | yes |
| T2 | Ordered / Complete / Reviewed → Complete | Enter / save value | `results.create` + edit right (RES-050) | not Cancelled/Verified/Printed (RES-011); sample Received (RES-010, BR-03); value by type: required (RES-002), number (RES-003), formula not typed (RES-004), placeholder is no value (RES-005, BR-19), restricted list (RES-006), paragraphs (RES-007), culture (RES-008), lengths (RES-009); culture references known (RES-040…042) | — | — | — | `ResultEntered`; `ResultPanicDetected` when a panic appears | no |
| T2a | Ordered / Complete / Reviewed → Complete | Land analyser value | policy P3 (`AnalyserResultReceived`) | as T2, but a refusal writes a `Refused` log row instead of failing; Reviewed only with `OverwriteReviewed`, Verified/Printed only with `OverwriteVerified` (verification removed, logged); idempotent on `IntakeId` | — | — | — | `ResultEntered` (+ panic) | yes |
| T2b | Ordered / Complete / Reviewed / Verified / Printed → Complete | Compute formula | handler of T2/T2a/T6 after an input changed | all referenced inputs have a value (else unchanged); a Verified formula is reopened (signature removed, logged) | — | — | — | `ResultEntered` | yes |
| T3 | Complete → Reviewed | Review (one, bulk) | `results.approve` + review right (RES-051) | state Complete (RES-012) | — | — | — | `ResultReviewed` | no |
| T4 | Complete / Reviewed → Verified | Verify (one, bulk) | `results.approve` + verify right (RES-052) | a value (RES-014); delta acknowledged (RES-015, BR-11); Reviewed first when `reviewBeforeVerify` (RES-016); state (RES-013) | — | **signature** (§10) | — | `ResultVerified` | no |
| T5 | Verified → Reviewed (if reviewed) or Complete; Reviewed → Complete | Undo verify / undo review | `results.cancel` + verify right (undo verify) or review right (undo review) | state Reviewed or Verified (RES-017); Printed is amended, not undone (RES-018) | — | — | **required** (RES-019) | `ResultUndone` | no |
| T6 | Verified / Printed → Complete | Amend (modify verified result) | `results.edit` + edit right + special right *modify verified result* (RES-055) | state (RES-023); value rules of T2; sample Received (RES-010) | — | — | **required** (RES-019) | `ResultAmended` (+ panic) | no |
| T7 | (Complete / Reviewed / Verified) | Acknowledge delta | `results.approve` + review or verify right (RES-051) | a delta flag not yet acknowledged (RES-020) | — | — | optional comment | `ResultDeltaAcknowledged` | no |
| T8 | Verified → Printed; Printed → Printed; (Complete / Reviewed: printed flag only, `printUnverified` on) | Print report unit (one unit, or a print list) | `results.export` + print right (RES-053); a VIP patient's unit also needs the See-VIP special right (RES-056, F-06) | every non-cancelled result of the unit Verified or Printed (RES-021, BR-09) unless `printUnverified`; no missing value | — | — | — | `ResultPrinted` | no |
| T9 | any but Cancelled → Cancelled | Cancel | policy P2 (`VisitLineCancelled`) | idempotent | — | — | the line's reason | `ResultCancelled` | yes |
| T10 | (any but Cancelled) | Report comment | `results.create` + edit right | not Verified / Printed (RES-024: undo or amend first); ≤ 2 000 (RES-025) | — | — | — | — (log row) | no |
| T11 | (any but Cancelled) | Internal note | `results.create` | ≤ 2 000 (RES-025); never printed | — | — | — | — (log row) | no |
| T12 | (any) | Move to the surviving patient | policy P4 (`PatientsMerged`) | idempotent | — | — | the merge reason | — (log row) | yes |
| T13 | (Ordered / Complete / Reviewed) | Re-time the turnaround clock | policy P5 (`SampleCollected`) | not yet Verified | — | — | — | — | yes |
| T14 | (any) | Follow the payer | policy P6 (`VisitTransferred`) | — | — | — | — | — | yes |
| PL1 | — → Open | Create print list | `results.manage` | branch selected | — | — | — | — | no |
| PL2 | Open | Add report units / accessions; remove units | `results.manage` | list Open (RES-030); units exist and are visible (RES-405); a unit is listed once | — | — | — | — | no |
| PL3 | Open → Printed | Print the list | `results.export` | not empty (RES-031); every unit printable (T8 rules, RES-021); the list number is stamped on every result | — | — | — | `ResultPrinted` per result | no |
| Q1 | — → Active | Define QC material | `quality.manage` | code (QC-001), unique (QC-100), levels 1–3 with SD > 0 (QC-002, QC-003) | — | — | — | — | no |
| Q2 | Active | Edit material | `quality.edit` | levels (QC-002, QC-003) | — | — | — | — | no |
| Q3 | Active ⇄ Inactive | Deactivate / reactivate | `quality.manage` | idempotent | — | — | — | — | no |
| Q4 | — → Accepted / Warning / Rejected | Record QC run | `quality.create` | material active (QC-005), level defined (QC-004), value (QC-006); Westgard over the previous runs of the material and level | — | — | optional comment | `QcRunRecorded` (rejected runs: `QcRunRejected`) | no |

Bulk review / verify (CP-21): T3 / T4 for every listed result; each refusal is reported per result with its code, the
others commit together (one transaction); at most 500 results per call.

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-RES-001 | One result per (visit line, test) | policy idempotency + unique index `(tenant_id, line_id, test_id)` | UNIQUE-409 | — |
| INV-RES-002 | A value is recorded only on a Received tube | aggregate (T2, T2a, T6) with the gate answered by `ISampleStatusLookup` | RES-010 | "The sample is not received yet: results cannot be entered (BR-03)." |
| INV-RES-003 | A restricted list accepts listed values only; a placeholder is never a value | aggregate | RES-006 / RES-005 | "The value is not in the test's list." / "A placeholder is not a result (BR-19)." |
| INV-RES-004 | A numeric value is stored with the test's decimals; outside the reportable limits it reads "< low" / "> high" | aggregate | — | — |
| INV-RES-005 | The range, flag and panic of a value are those resolved at its entry for the patient's sex, age and condition (BR-08) | handler resolves, aggregate flags | — | — |
| INV-RES-006 | Verified implies a value, the verifier, the time and the verifier's name as of the action | aggregate (T4) | RES-014 | "A result without a value cannot be verified." |
| INV-RES-007 | A delta-flagged result is verified only after the flag is acknowledged | aggregate (T4) | RES-015 | "Acknowledge the delta check before verifying (BR-11)." |
| INV-RES-008 | Only Verified results print unless `printUnverified` is on (BR-09) | aggregate (T8) | RES-021 | "Only verified results print." |
| INV-RES-009 | A Verified or Printed value changes only through Amend with the special right, and returns to Complete | aggregate (T2 refuses, T6) | RES-011 / RES-055 | — |
| INV-RES-010 | Every value change and status change appends exactly one log row; log rows are never changed | aggregate + append-only trigger on `app.result_log_entries` | — | — |
| INV-RES-011 | Hidden helper tests never print (BR-20) | report document projection | — | — |
| INV-RES-012 | A print list is printed once; a printed list is frozen | aggregate | RES-030 | "The print list is already printed." |
| INV-RES-013 | A print list number is unique per tenant and never reused | unique index + reference counter | UNIQUE-409 | — |
| INV-QC-001 | A QC material code is unique per tenant; levels 1–3 are distinct with SD > 0 | handler + unique index; aggregate | QC-100 / QC-002 / QC-003 | — |

## 9. Segregation-of-duties rules

None fixed by the SRS or LDM: LDM lets the same chemist review and verify ("Verify may skip review", §5.5) and the
rights are per group and branch. The record keeps every anchor (`EnteredByUserId`, `ReviewedByUserId`,
`VerifiedByUserId`), so a rule such as `SOD-RES-001` "the reviewer may not verify" or "the person who entered may not
verify" can be added inside `Result.Verify` without a data change (§16 Q6).

## 10. Approval gates

| Item | Content |
| --- | --- |
| Transition | T4 Verify |
| Permission | `results.approve` + result right *verify* for the result's clinical group and the visit branch |
| Rule | a value; delta acknowledged; Reviewed first when the tenant parameter `reviewBeforeVerify` is on (default off) |
| Recorded | verifier id, server time, verifier's display name as of the action (the electronic signature printed on the report) |
| After the gate | value and report comment frozen; internal note editable; print (T8), undo (T5), amend (T6) possible |
| Correction path | Undo (T5, reason) returns to Reviewed or Complete; Amend (T6, special right, reason) returns a Verified or Printed result to Complete with a new value; the previous value and signature stay in the result log; a printed report carries the amendment count |
| Rejection | not applicable (an unverified result simply stays Complete or Reviewed) |
| Step-up | none in this version (§16 Q7) |
| Display | verifier name and time on the grid, the status log and the report |

## 11. Events and policies

```text
Event:    ResultEntered { ResultId, VisitId, Accession, TestId, ClinicalGroupId, ReportUnitKey, Source, Flag?, IsPanic, DeltaFlag, ActorUserId? }
Raised by: T2, T2a, T2b, T6
Reacts:   Automation — auto-verification rules evaluate the result (CP-63/64)   [Automation intake]

Event:    ResultPanicDetected { ResultId, VisitId, Accession, PatientId, TestId, BranchId, Value, Flag }
Raised by: T2, T2a, T2b, T6 when the new value is a panic value and the previous one was not
Reacts:   Notifications — "panic-value" template to the unit (foundation template)   [later]

Event:    ResultReviewed / ResultVerified { ResultId, VisitId, Accession, TestId, ClinicalGroupId, ReportUnitKey, ActorUserId }
Event:    ResultUndone { ResultId, VisitId, Accession, FromStatus, ToStatus, ActorUserId, Reason }
Event:    ResultAmended { ResultId, VisitId, Accession, TestId, ActorUserId, Reason }
Event:    ResultDeltaAcknowledged { ResultId, VisitId, Accession, TestId, ActorUserId, Comment? }
Event:    ResultPrinted { ResultId, VisitId, Accession, ReportUnitKey, PrintCount, PrintListNumber?, ActorUserId }
          Reacts: Registration — visit Completed when every unit is printed and the balance is settled (status model "Visit")   [Registration]
Event:    ResultCancelled { ResultId, VisitId, Accession, LineId, TestId, ActorUserId, Reason }
Event:    QcRunRecorded { RunId, MaterialId, TestId, Level, Outcome, ActorUserId } / QcRunRejected { RunId, MaterialId, TestId, Level, Rules }

Policy P1: when VisitRegistered or VisitLinesAdded, then create one Result per test of every Ordered line (the line's
           test ids, or the service expanded through the catalogue: profiles, cultures, text reports, packages of
           services; Additional services have none), idempotent on (LineId, TestId); report unit by clinical group, or
           its own unit for a print-alone service; tube from the event; expected time from app.visit_lines
           (expected_result_at_utc) or the registration time + the test's duration counting test days (BR-13).
Policy P2: when VisitLineCancelled, then cancel the line's results (idempotent; the reason is the line's).
Policy P3: when AnalyserResultReceived, then land the value on the sample's result of TestId (tube of the sample
           number) as Complete with the analyser as source, ranges and flags applied, idempotent on IntakeId; refusals
           (sample not Received, Reviewed/Verified without overwrite, value not acceptable, cancelled) are written to
           the result log; formulas depending on the test are recomputed.
Policy P4: when PatientsMerged, then re-point the merged file's results to the survivor (idempotent, logged).
Policy P5: when SampleCollected, then restart the turnaround clock of the tube's open results at the collection time
           (BR-13: expected = collection + duration counting test days).
Policy P6: when VisitTransferred, then the visit's results follow the new payer (work-queue and worklist filters).
```

Events go to `ops.outbox_events` in the same commit; the audit reason of a transition comes from the event's
`Reason` / `Comment`.

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Value, text, paragraphs, culture, flag, panic, delta, range | **Health data (PII context)**, Audited | printed on the report; every change in the result log |
| Patient id (number, names, age, sex joined at read time) | PII | never copied into the result |
| Report comment | Health data, Audited | prints |
| Internal note | Confidential (laboratory staff), Audited | never prints (LDM §5.4) |
| Verifier id, name, time | Audited | the electronic signature (BR-09) |
| Statuses, times, actors, sources, analyser id and flags | Operational, Audited | |
| Frozen states | Verified and Printed (value), Cancelled | |
| Retention | with the patient's laboratory record (assumed 10 years, §16 Q12) | CSV export of lists: `results.view` (affordance `results.export`) |

## 13. Reference data and configuration

Tenant parameters (`ITenantSettings`, edited on CP-57): `reviewBeforeVerify` (default off, OQ-05), `printUnverified`
(default off, BR-09 exception), `resultLanguage` (en / ar, report language), `delayWarnPercent` (80) and
`delayLatePercent` (100) (BR-13 colours). Reference data: tests (type, unit, decimals, ranges by sex/age/condition,
list values, formula, paragraphs, hidden helper, print reference, delta % and window, duration and test days),
services (members, print-alone, report name, sequence), clinical groups (report template), report templates, comment
library, organisms, antibiotics, sensitivity values (Catalogue A and B read ports and tables), patients (sex, date of
birth or age, condition), samples (status, number, collection), users (names), branches, payers, analysers and their
test mappings (`app.analysers`, `app.analyser_test_mappings`, raw SQL, same wave), visits and lines (`app.visits`,
`app.visit_lines`, raw SQL per MODULES.md §5 until Registration's `IVisitLookup` exists).

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Worklist (CP-18), one row per test | collection date range (`from`/`to`, local day by `utcOffsetMinutes`), clinical group, test, analyser (tests mapped on it), sample status, payer, status boxes (multi), delayed only, search (accession / sample number / patient number prefix) | patient no., accession, sample no., patient name, age/sex, test, test status, sample status, collection, expected result, delay, consumed %, colour (ok / warn / late / done) | `results.view` + see right |
| Counters | as the worklist | requests (visits), tests, samples | idem |
| Visit search (CP-19 query panel) | accession, patient no., patient name (`%` wildcard), test status, sample status, from analyser, not printed, not verified, delta, registration date range | accession, patient, registered at, branch, units with status | idem |
| Results of a visit / of a sample (CP-19 / CP-20) | accession, visit id or sample number | case header; one tab per report unit: samples, status, results grid (test, value, unit, range, flag, delta, sample status, status, printed, source, actors), comment, note | idem |
| Result log / status log / previous results | result id | value changes (before, after, source, analyser, who, when), status changes, refusals; the patient's previous verified results of the test | idem |
| Work queue (CP-21), one row per report unit | registration date range, clinical group, payer, from analyser, to analyser, normal / abnormal, received only, status boxes exact (Complete, Reviewed, Verified, Printed) and Not-X (BR-10), printed / not printed, search | accession, patient, unit, status, printed, colour state, results count, abnormal, panic, delta | idem |
| Print reports (CP-13) | verified-unprinted units of the work queue; print lists (status) | list number, units, printed at | `results.view`; actions `results.manage` / `results.export` |
| QC (CP-22) | material, level, date range | runs: date, value, z-score, flags, outcome; Levey-Jennings points with mean ± 1/2/3 SD | `quality.view` |

Documents (typed DTOs, rendered by `shared/print`): **laboratory report** per report unit (template from the clinical
group's report template, lab and branch letterhead, patient and visit header, results with units, ranges and flags by
profile banner, culture organisms and sensitivity table, formatted paragraphs, comments, verifier name and time, QR
with the accession, preliminary marker, print list number, reprint count; hidden helpers excluded) — preview
(`results.view`) and print (after T8); **abbreviated worksheet** (multi-patient, "copy for file use only", empty result
boxes); **worklist** (bench list of the current filter, at most 2 000 rows). CSV export on the worklist, the work queue
and the visit search (client side, the rows of the current filter, at most 5 000).

API: `/api/v1/results` — `GET ?accession|visitId|sampleNumber` (case with report units) · `GET /visits` (search) ·
`PUT /{id}` (value) · `PUT /values` (several values) · `POST /{id}/review|verify|undo|amend|acknowledge-delta` ·
`POST /review`, `POST /verify` (bulk, result ids) · `PUT /{id}/comment` · `PUT /{id}/note` · `GET /{id}/log` ·
`GET /{id}/previous` · `GET /worklist` · `GET /worklist/documents/worklist` · `GET /work-queue` ·
`GET /report-units/{key}/documents/report?preview=` · `POST /report-units/{key}/print` ·
`POST /documents/worksheet` · `GET /lookups/comment-templates` · `GET /lookups/microbiology`;
`/api/v1/results/print-lists` — `GET`, `GET /{id}`, `POST`, `POST /{id}/units`, `POST /{id}/accessions`,
`POST /{id}/remove-units`, `POST /{id}/print`, `GET /{id}/documents`; `/api/v1/quality` — `GET/POST /materials`,
`PUT /materials/{id}`, `POST /materials/{id}/deactivate|reactivate`, `GET/POST /runs`, `GET /materials/{id}/chart`.

## 15. Notifications and escalations

A panic value raises `ResultPanicDetected` (the foundation's `panic-value` template is the hook for an SMS/e-mail
policy of the Notifications capability; not sent by this module). Late tests are visible on the worklist (colour, Delayed counter and filter) and
counted by the dashboard (CP-05), all with the predicate of Q21. Rejected QC runs raise `QcRunRejected`.

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/{Results,Quality}`, `tests/Platform.Application.UnitTests/Results`,
`tests/Platform.IntegrationTests/ResultsModuleTests.cs` and `tests/Platform.WebApi.FunctionalTests/ResultsFlowTests.cs`.

```gherkin
Scenario R-T1-ok / R-T1-idem / R-T1-reserved: T1 Order
  VisitRegistered with an Ordered line of a two-test profile → two Ordered results in the group's report unit;
  handled twice → still two; a Reserved line → none; an Additional service → none; a print-alone service → its own unit

Scenario R-T2-ok: T2 Enter numeric
  Given a Received sample and a numeric test with decimals 1 and a band 3.5–5.0 (panic 2.5 / 6.5)
  When 6.84 is entered → value 6.8, flag CriticalHigh, panic, status Complete, a Value log row, ResultEntered + ResultPanicDetected
Scenario R-T2-gate: the sample is Collected → RES-010 and nothing changes (BR-03)
Scenario R-T2-right: the user has no edit right on the group → RES-050
Scenario R-T2-list: a restricted list refuses a value not in the list (RES-006) and a placeholder (RES-005); an optional list keeps free text unflagged
Scenario R-T2-formula: typing a formula test → RES-004; entering its inputs computes it (T2b)
Scenario R-T2-reportable: 0.04 with a reportable low 0.1 reads "< 0.1"
Scenario R-T2-delta: previous verified 100, delta 20 %, new 130 → DeltaFlag, verify refused RES-015 until acknowledged (T7)
Scenario R-T2-culture / R-T2-text: a culture needs No growth or an isolate (RES-008); formatted text needs a paragraph (RES-007)
Scenario R-T3-ok / R-T3-state: Complete → Reviewed; Ordered → RES-012
Scenario R-T4-ok: Complete → Verified with verifier name and time (signature)
Scenario R-T4-novalue / R-T4-review: Ordered → RES-014; reviewBeforeVerify on and Complete → RES-016
Scenario R-T5-ok / R-T5-reason / R-T5-printed: Verified (reviewed) → Reviewed; no reason → RES-019; Printed → RES-018
Scenario R-T6-ok / R-T6-right: Verified → Complete with the new value and the amendment logged; without the special right → RES-055
Scenario R-T8-ok / R-T8-unverified: a verified unit prints (Printed, count 1, ResultPrinted); a unit with a Complete result → RES-021 unless printUnverified
Scenario R-PREVIEW: a unit with a missing value → RES-022 unless the user holds "preview without verify"
Scenario R-T9: VisitLineCancelled → the line's results Cancelled, twice → no change
Scenario R-T2a: an analyser value lands Complete with the analyser source; on a Collected sample a Refused log row is written;
  on a Reviewed result without OverwriteReviewed a Refused row; with it the result returns to Complete; the same IntakeId twice → once
Scenario R-T12: PatientsMerged → the merged file's results belong to the survivor
Scenario R-BR04: a unit with Verified and Complete results reads Complete; a profile banner reads the lowest member status
Scenario R-BR10: Not Verified = Ordered + Complete + Reviewed; Not Reviewed = Ordered + Complete; Not Complete = Ordered
Scenario R-BR13: duration 24 h, test days Mon–Fri, collected Friday 10:00 → expected Monday 10:00; 80 % consumed → warn; past → late
Scenario R-PL: a print list gets PRL-yyyy-nnnnn, takes verified units, prints once (RES-030 afterwards), stamps its number
Scenario R-AUTHZ / R-BRANCH / R-TENANT: without results.approve a verify is 403 AUTHZ-001; without a branch 403 TENANT-004;
  results of tenant X are invisible to tenant Y; groups without the see right are not returned
Scenario QC-1…QC-4: a material with two levels; a run within ±2 SD is Accepted; > 2 SD Warning (1-2s); > 3 SD Rejected (1-3s);
  two consecutive > +2 SD Rejected (2-2s); consecutive runs 4 SD apart Rejected (R-4s)
```

Open questions (the assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 (OQ-04) | Is the Received gate kept when the laboratory auto-receives? | Yes: the gate is always enforced; with `autoReceive` on, samples are Received at registration, so entry is open at once. |
| Q2 (OQ-05) | Review / verify order | The tenant parameter `reviewBeforeVerify` (default off: verify may skip review, LDM §5.5). |
| Q3 | Which branch do result rights refer to? | The branch of the visit (where the results are produced for the patient), not the session branch. |
| Q4 | Delta window when the test has a delta % but no window | No limit: the latest previous verified result of the same test of the patient in another visit. |
| Q5 | Does a delta flag block verification? | It must be acknowledged first (CP-19); acknowledgement does not change the value (BR-11 "does not block" = it never refuses the value itself). |
| Q6 | SoD between entry, review and verification | None (§9). |
| Q7 | Step-up (fresh password) for verification | Not in this version. |
| Q8 | Printing unverified results with `printUnverified` on | The printed flag is set but the status stays Complete / Reviewed (the colour states "Incomplete & Printed", "Completed & Printed", "Reviewed & Printed" exist for this case); the report carries a "preliminary" marker. |
| Q9 | Reserved & Not printed colour | Never produced: Reserved lines create no results until released (BR-05); the colour is in the legend. |
| Q10 | Formula recomputation on a verified formula | Reopened to Complete with the new value (signature removed, logged); it needs a new verification. |
| Q11 | Report comment scope | Per result (prints under the test); the report shows the comments of the unit in order. |
| Q12 | Retention | With the patient's laboratory record (10 years). |
| Q13 | Turnaround day boundaries | UTC days (no tenant time-zone parameter yet); the worklist date filter uses the caller's offset. |
| Q14 | Analyser-specific ranges (LDM §3.3) | Not applied in this version: the test's resolved range is used for every source. |
| Q15 | Panic list values | A list value marked critical is a panic (flag Abnormal); a non-normal list value is Abnormal. |
| Q16 | Culture flag | Growth (any isolate) reads Abnormal, No growth reads Normal; no panic. |
| Q17 | QC scope (CP-22 "scope to be confirmed") | Materials with up to three levels and targets, manual runs, Levey-Jennings, Westgard 1-2s (warning), 1-3s, 2-2s, R-4s (rejection) on consecutive runs of the same material and level; no blocking of patient results. |
| Q18 | Visit header data (doctor, payer, registration time) | Raw SQL on `app.visits` (MODULES.md §5) and optional columns when present; replaced by Registration's `IVisitLookup` when it exists. |
| Q19 | VIP patients on the worklist (CP-18) and in the matching visits of Results by accession (CP-19) | **Same rule as PAT Q8 / REG Q12 — assumption, awaiting the business owner (validation 2026-10-11; CP-19 added 2026-10-12).** Without the See-VIP special right the worklist, its bench list and its export, and the CP-19 matching-visits list show "VIP patient" with no Arabic name (`WorklistItem.PatientMasked`, `ResultVisitListItem.PatientMasked`), and a name search does not find the file; the accession, the sample number (worklist) and the patient number still do. **Opened case and report (validation v0.5.0 F-06, 2026-10-12; assumption, awaiting the business owner):** without the right, the CP-19 case view and the on-screen report preview mask the identity too: "VIP patient", no Arabic name, no date of birth (the case carries no mobile or national id); the patient number, sex and age stay, because the bench needs them (ranges, formulas). The masking is done by the server (`PatientHeaderView.IsMasked`, `ReportPatientBlock.IsMasked`, `ResultVip`). Printing a VIP patient's report (`POST …/print` and the official copy `GET …/documents/report?preview=false`) needs the See-VIP right: **RES-056** (403) otherwise, checked after the unit's visibility (RES-405 first). The page keeps Print visible but disabled, with the reason. Registration and the patient file are unchanged: the desk needs the identity (PAT Q8, REG Q12). |
| Q20 | Digits of dates and times | **Decision 2026-10-12 (validation R3-02).** On screen, dates and times use the digits of the language (Arabic-Indic in Arabic) like the rest of the portal (`result-format.ts` `formatDateTime`); printed reports, the bench list and exported files keep Latin digits (`formatPrintDateTime`). |
| Q21 | One definition of "late" for the worklist (validation v0.5.0 F-15, 2026-10-12) | **Decision.** A test is late when its consumed share of the turnaround reaches the tenant's `delayLatePercent`: consumed ≥ late ⇔ now ≥ start + (expected − start) × late / 100 (`TurnaroundClock.IsLate`). The row colour, the *Delayed* counter and *Delayed only* (server SQL), and the dashboard's delayed tests read the same predicate; the shown share is rounded down so "2 %" and a red row always agree. Before, the counter and the filter used the expected time (100 %) whatever the threshold. |
| Q22 | Report language, digits and counted wording (validation v0.5.0 F-09…F-12, 2026-10-12) | **Decision.** The laboratory report follows its own language as a whole (`resultLanguage`, or the template's language): captions, frame language and direction come from that language's dictionary (`DocumentTextsLoader`), whatever the screen's language. On screen an Arabic page writes every number (ages, counts, percentages, amounts, dates) in Arabic-Indic digits; printed documents and CSV exports keep Latin digits. Counted sentences take their grammatical form (`I18nService.tc`: Arabic dual, 3–10 plural, 11+ singular; English one / other). |

Assumptions about other modules (same-wave raw-SQL reads, MODULES.md §5): `app.visits(id, tenant_id, accession,
patient_id, branch_id, payer_id, registered_at_utc, urgent[, doctor_id])`, `app.visit_lines(id, tenant_id, visit_id,
expected_result_at_utc, status)`, `app.analysers(id, tenant_id, code, name_en, name_ar)`,
`app.analyser_test_mappings(tenant_id, analyser_id, test_id, is_active)`. Each read checks that the table and columns
exist and falls back to empty answers when they do not.

## 17. Change request 2026-10-24 — Reference range before the first save (LIS-CR-007, package P-RES, reviewer point C-08, CP-19)

**Today.** A result stores its range (BR-08) when a value is first entered (`ResultEntryEngine`), so an Ordered line showed an empty
*Reference range* cell until the first save: the technician could not compare the value being typed with the expected band.

**Change.**

- `GET /api/v1/results?accession=|visitId=|sampleNumber=` (`GetResultCaseHandler`): a result with **no value and no stored range** is
  served with the band BR-08 picks today for the visit's patient (`ResultViews.ProvisionalRange` → `ResultCatalogue.Range` →
  `ReferenceRangeResolver`, the call entry makes; the patient profile is `PatientProfile.From(patient, today UTC)` like entry), and the row
  carries `rangeProvisional: true` (`ResultRowView.RangeProvisional`, optional, default false; mirrored in `results.contracts.ts`).
- Once a value exists the stored range wins, also when it is empty and also after the catalogue band or the patient file changes
  (what was flagged is never rewritten on screen). A test without a matching band shows no range, as before.
- The grid (`app-result-grid`) shows a provisional range greyed and in italics, with the tooltip `results.grid.rangeProvisional`
  ("Expected range for this patient; confirmed when the result is saved" / «المعدل المتوقع لهذا المريض؛ يُثبَّت عند حفظ النتيجة»).
- The worklist (CP-18) shows no range column, so it is unchanged. Reports print only entered results, so they are unchanged.
- No new code, no new endpoint, no table change.

Tests: Application `ResultsUseCaseTests.C08_*` (provisional before entry, stored after, no band → no range); functional
`ResultsFlowTests.C08_an_ordered_line_shows_the_patient_s_range_before_entry_and_the_stored_range_after` (band changed after entry: the
entered visit keeps 3.5 – 5.0, a new visit shows the new band provisionally); spec `result-grid.component.spec.ts`.

| # | Question | Assumed answer |
| --- | --- | --- |
| Q23 | Should a result whose value was cleared (or a cancelled line) show the provisional range again? | A result with a stored range keeps it; a result without a value and without a stored range shows the provisional one, whatever its status. |
| Q24 | Should the provisional range follow a VIP-masked case? | Yes: masking hides identity only; sex and age stay (Q19), so the range is resolved the same way. |
