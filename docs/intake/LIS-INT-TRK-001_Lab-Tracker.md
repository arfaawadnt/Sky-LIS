# LIS-INT-TRK-001 — Lab Tracker (change request LIS-CR-006)

Version 1.1 · 2026-10-24 (LIS-CR-008 P-SMP: "ext" on the samples cell, §18) · 1.0 · 2026-10-22 · package P-API (backend). Source: `docs/build/CR-2026-10-22_Lab-Tracker.md` §0/§2, decision of the business
owner of 2026-10-22. Read with the Registration (REG), Samples (SMP), Results (RES) and Billing (BIL) intakes.

## 1. Purpose

One page that follows every visit of a laboratory day through **registration → samples → results → payment**: where each visit is
(stage), what it needs next and from which module (next step), how much turnaround time is left, and what needs attention. The page has
two layouts (pipeline table, stage lanes) over the same query. It is a read model only: no aggregate, no table, no event, no migration.

## 2. Scope and permissions

- Query `GetLabTrackerQuery` [`registration.view`] → `GET /api/v1/tracker/visits`. No new permission key.
- Sections of a row: `samples` only with `samples.view`, `results` only with `results.view`, `payment` only with `billing.view` (null
  otherwise). The stage, next step, time left and flags are computed from everything regardless of those permissions.
- Results are counted over the results the user may see (result rights per clinical group and branch, the worklist's rule); `hidden`
  counts the others.
- VIP rule A-03 (as the visits register and the worklist): without See-VIP a VIP row reads "VIP patient" (`patientMasked` true, no Arabic
  name) and a name or mobile search does not find it; accession, sample number and patient number still do.

## 3. Day and branch

- `day` = laboratory-calendar day in the tenant time zone (`ILaboratoryTimeZone`: client record country, else `timeZone`); default today.
  A visit belongs to the day its `registered_at_utc` falls in (half-open window of the local day).
- `branchId` = the session branch by default (`TENANT-004` without one); any branch of the laboratory may be asked for, as the visits
  register allows.

## 6. Rules (pure functions, `Platform.Domain/Tracker/VisitProgress.cs`)

The results read (R) are the visible live results of the visit; when the user sees none of them, every live result of the visit (so a
desk without result rights still follows the visit). Live = not Cancelled; live samples = not Cancelled / Replaced; lines = top-level
(services and packages; package members ignored).

| Stage (precedence) | Rule |
| --- | --- |
| `Cancelled` | no live line |
| `AwaitingSample` | a live sample is Ordered or Rejected; or no result and no sample exists yet (just registered) |
| `InLab` | a result of R is Ordered, or Complete while `reviewBeforeVerify` is on; or no result yet but samples exist |
| `AwaitingVerification` | a result of R is Reviewed, or Complete without review |
| `Ready` | every result of R signed, one not printed |
| `Delivered` | every result of R printed (delivered = printed) |

Next step: AwaitingSample → `RecollectSample` when a sample is rejected, else `CollectSample`; InLab → `EnterResults` (an Ordered result or
none yet) else `ReviewResults`; AwaitingVerification → `VerifyResults`; Ready → `TakePayment` when the patient still owes, else
`PrintReport`; Delivered → `TakePayment` when owed, else `None`; Cancelled → `None`. "Owes" = remaining > 0 (BR-14: patient share −
posted payments + refunds) and the visit is not billed through claims (lab-to-lab batch).

Time left: minutes from now to the earliest expected time of R's unsigned results (rounded down, negative = late); delay state = the worst
`TurnaroundClock` colour of those results (`delayWarnPercent` / `delayLatePercent`), `Done` when every result is signed, none without results.

Attention: `Delayed` = delay state Late; `Panic` = a visible unsigned result flagged panic; `Rejected` = a live rejected sample (not
replaced); `ReadyUnpaid` = Ready or Delivered and owes. Views: `Reception` = AwaitingSample or owes (cancelled excluded); `Verification`
= AwaitingVerification; `FrontDesk` = Ready.

## 14. Query and endpoint

`GET /api/v1/tracker/visits?day=yyyy-MM-dd&branchId=&stage=&attention=&view=&search=&page=&pageSize=&sort=&direction=`

- `stage` / `attention` / `view` filter the rows; `counters` cover the whole filtered day (day, branch, search) before them.
- `search` (≤ 60): accession, sample number, patient number, Latin / Arabic name contain the text (ILIKE, escaped); the mobile contains the
  digits when the text is a number of at least five digits (trunk 0 dropped as the visits register does).
- Sort keys (allow-list `TrackerSortKeys.Visits`): `timeLeft` (default: urgent first, minutes left ascending, no clock last; `desc` = most
  time left first), `registeredAt`, `accession`, `patient` (shown name, masked rows by the mask), `stage` (workflow order, Cancelled last);
  ties by accession then id. VALIDATION-400 on any other key, direction, enum name, page size above 200 or an invalid day.
- Response `LabTrackerPage { items, page, pageSize, totalCount, counters, day, branchId }`; `LabTrackerItem` and `LabTrackerCounters` as in
  `Platform.Contracts/Tracker/TrackerContracts.cs`.
- Implementation: one SQL statement (`Persistence/Tracker/LabTrackerReader.cs`) returns every visit of the day with grouped per-visit
  aggregates (tenant- and branch-bounded, RLS, no elevation); the handler applies the rules, counts, filters, orders and pages in memory
  (one definition of the rules, counters over the whole day). Measured: 502 visits read in 67–117 ms on the dev instance. Existing
  indexes suffice; no migration.

## 15. Codes

No new code. `TENANT-004` (no session branch), `AUTHZ-001` (403) without `registration.view`, `VALIDATION-400` on shape.

## 16. Scenarios (tests)

- Domain `VisitProgressRulesTests` TRK-S01…S16 (stages, next steps, payment flag, time left, panic, visibility fallback).
- Integration `TrackerModuleTests` S-T1…S-T5 (aggregates, result rights per branch, search incl. VIP and literal `_`, tenant isolation,
  500 visits timing).
- Functional `TrackerFlowTests` S-T6…S-T8 (the day through every stage, filters vs counters, sort, search, day / branch; null sections and
  VIP mask; 403, TENANT-004, VALIDATION-400) and the sort-key gate.

## 17. Open questions (assumed answers, the business owner may change them)

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | A user who sees none of a visit's results: what stage? | Derived from all its live results (the desk follows the visit; values stay hidden). |
| Q2 | "Unacknowledged panic": no acknowledgement exists for a panic. | A visible panic result not yet verified (verification acknowledges it). |
| Q3 | "Ready but unpaid" with a refund due? | Not flagged: only Unpaid / Partial (remaining > 0); a lab-to-lab batch visit (claims) is never flagged. |
| Q4 | Delivered without a hand-over record | Delivered = every result printed. |
| Q5 | Samples with auto-collect / auto-receive on | Shown as recorded (they read Received). |
| Q6 | May a user look at another branch? | Yes, as the visits register (any branch of the laboratory); the default is the session branch. |
| Q7 | `payment.paid` | Net of refunds (posted payments − refunds), so share − paid = remaining. |
| Q8 | Very large days | Read whole into memory (≈ 0.2 ms per visit); a day above a few thousand visits may need SQL paging later. |

## 18. Change request 2026-10-24 (LIS-CR-008, package P-SMP, reviewer point C-02) — "ext" on the samples cell

`LabTrackerSamples.CollectedOutside` counts the live samples (Cancelled and Replaced excluded) collected outside the laboratory
(`app.samples.collected_outside`, one more aggregate of the reader's `sample_counts`, appended as the last column of the statement);
`VisitSampleCounts.CollectedOutside` / `AnyCollectedOutside` in `VisitProgress.cs`. The stage, next step and flags do not change: an
outside sample is followed like any other. The SPA shows an "ext" pill (`tracker.samples.ext`, title `tracker.samples.extTitle`) beside
the samples pill in the table and on the board card when the count is above zero; present only with `samples.view`, like the section.

## 19. Change request 2026-10-25 (LIS-CR-014, package P-TRK, reviewer points T1, T3, T5) — actions, service rows, filter in the URL, return shortcut

**T1 — actions column (like CP-10, LIS-CR-007 C-06).** Every table row (visit or service) ends with `app-tracker-row-actions`
(`features/tracker/ui/row-actions.component.ts`): *Details* (`/registration/accession?accession=`), *Edit* (`/registration/visits/:id`,
`registration.edit`), the *Payments* icon (`/billing/payments?visitId=`, `billing.view`) and the *Documents* icon
(`/registration/accession?accession=&tab=documents`), then the shared `app-row-menu`: *Patient* (`/patients/:patientId`, `patients.view`),
*Samples* (`…&tab=samples`, `samples.view`), *Results* (`/results/by-accession?accession=`, `results.view`), *Cancel services*
(`…&tab=cancel`, `registration.cancel`). The tracker cannot import the registration feature, so every action is a router link (the
targets do the work; `*appCan` is affordance only). The next-step button stays. The accession page (CP-10b) gained `?tab=`:
`documents` / `samples` scroll to that card (ids `accession-documents`, `accession-samples`), `cancel` opens the cancel-services dialog
with no line preselected, once per visit and tab after the visit is shown.

**T3 — *Visits | Services* rows.** `GET /api/v1/tracker/visits?rows=services` (case-insensitive; `visits` or absent = one row per visit;
anything else VALIDATION-400) answers `LabTrackerPage.Rows = "Services"`, `Items = []` and `Services` = one `LabTrackerServiceItem` per
live top-level line (kind Service or Package, status not Cancelled): the service code / names / kind, the visit's identity, patient
(VIP masked as A-03), payer and registration, and the line's **own** stage, next step, minutes left, delay state, samples and results;
`VisitStage` is the visit's and `Payment` the visit's balance. Reader: `ILabTrackerReader.ReadServicesAsync` = the same `day_visits` CTE
(search included) + `line_family` (every live line with its top-level line: package members fold into their package), the line's tubes
(its lines' `tube_id` ∪ its results' `tube_id`), live samples of those tubes (a tube shared by two lines counts for both), and the
per-line result aggregates with the same result-rights visibility. Rules: `VisitProgressRules.OfService(ServiceProgressFacts)` = the visit
rules over the line as a visit of one live line (never Cancelled); TakePayment / ReadyUnpaid follow the visit's balance. The stage,
attention and view filters apply to the line's own progress; `TotalCount` counts service rows; **the counters stay the visits'**.
Sort keys `TrackerSortKeys.Services` = the visit keys + `service` (code); `service` with visit rows is VALIDATION-400. Tie-break:
accession, line sequence, line id. The board stays visit-based (the toggle shows with the table only).

**T5 — filter in the URL, return shortcut.** The table's whole state is the URL query (`features/tracker/state/tracker-url.ts`): `day` |
`from`+`to`, `stage`, `attention`, `view`, `search`, `rows=services`, `page`, `size`, `sort` (table column key) + `dir=desc`,
`layout=board`; only non-default values are written. On open a URL naming any of these keys wins (a return, a shared link, back /
forward); a URL without them (the menu) keeps the facade's state and is rewritten from it; every change is written with
`replaceUrl`. Malformed values are dropped (default) and the URL rewritten clean. Every row action, the accession link, the next-step
link and the board card link carry `returnTo=<the tracker URL>`. New shared `shared/ui/return-link/return-link.component.ts`
(`app-return-link`, input `label`): reads `returnTo` from the current URL, accepts only in-app paths (`safeReturnTo`: starts with one `/`,
no `//`, no `\`, no scheme, no control or white-space character, ≤ 2000 chars), renders the chip "Back to {label}" (default "Back to the
list") and navigates there; hidden when absent or equal to the page shown. Mounted once by the integrator in the shell top bar. The
accession page keeps `returnTo` on its own navigation (search, previous / next, edit).

Assumptions (Q9–Q11, the business owner may change them): **Q9** a service row's payment is the visit's balance (payments are taken per
visit); **Q10** a service row is filtered by its own stage, not the visit's (the visit's stage shows under the service when it differs);
**Q11** opening the tracker from the menu keeps the last filter of the session (the facade's), a link with a filter replaces it.

Tests: domain `TRK_T3a/T3b`; functional `Service_rows_follow_each_line_on_its_own_while_the_counters_stay_the_visits`; specs
`tracker-url.spec.ts`, `return-link.component.spec.ts`, facade (service rows), page (actions by permission, services toggle, URL round
trip), accession page (`?tab=cancel`, `returnTo` kept).
