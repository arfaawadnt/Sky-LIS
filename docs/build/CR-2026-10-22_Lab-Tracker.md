# Change request 2026-10-22 — Lab Tracker: one page that follows every visit (LIS-CR-006)

Decision of the business owner (2026-10-22): build the **Lab Tracker** — one page that follows every visit of the day through
**registration → samples → results → payment**, in the two layouts chosen on the design canvas (private claude.ai artifact
"Sky LIS Lab Tracker"): **A · Pipeline board** (table view) and **B · Stage lanes** (board view), switched by a *Table | Board*
toggle over the same data and filters. Assumed answers to the open decisions (business owner may change them): samples are
shown as recorded (with auto-collect / auto-receive on they read *received*); **delivered = printed** (no hand-over record yet);
a verified visit that is not fully paid is **flagged** ("Ready but unpaid"), never blocked; the existing Worklist stays and is
reached from the tracker's *Bench* view. Read with `docs/build/MODULE-PATTERN.md` §2 and `CR-2026-10-19_Prototype-Gaps.md` §0.

## 0. Target shape

**Stages of a visit** (derived on the server, one per visit, in this order of precedence):

| Stage | Rule |
| --- | --- |
| `Cancelled` | the visit has no live (not cancelled) line |
| `AwaitingSample` | a live sample of the visit is Ordered or Rejected (not replaced) |
| `InLab` | otherwise, some visible live result is Ordered or Complete |
| `AwaitingVerification` | otherwise, some visible live result is Reviewed (or Complete when review-before-verify is off) |
| `Ready` | every visible live result is Verified and at least one is not Printed |
| `Delivered` | every visible live result is Printed |

**Next step** (code + the module that does it): `CollectSample` / `RecollectSample` (samples), `EnterResults`, `ReviewResults`,
`VerifyResults` (results), `TakePayment` (billing; when the visit has a remaining patient share and no earlier step is open, or when
Ready and unpaid), `PrintReport`, `None`. **Time left**: the earliest `ExpectedAtUtc` of the visit's unsigned visible results against
the turnaround clock (`TurnaroundClock`, `delayWarnPercent` / `delayLatePercent`): minutes left (negative = late) and `DelayState`.

**Query** `GetLabTrackerQuery(Day?, BranchId?, From?, To?, Stage?, Attention?, View?, Search?, Page, PageSize, Sort, Direction)`
[`registration.view`] → `LabTrackerPage { items, page, pageSize, totalCount, counters }`:
- `Day` = laboratory day in the tenant time zone (default today; the visit's `RegisteredAtUtc` falls in it); `BranchId` = session branch by
  default (same branch rule as the visits register). **Follow-up 2026-10-22**: `From` / `To` (laboratory wall-clock, both or neither,
  `From ≤ To`, at most 31 days) replace the day with the window `[From, To + 1 min)`; the page echoes `fromUtc` / `toUtc`.
- `Attention`: `Delayed` (DelayState Late), `Panic` (a visible result with an unacknowledged panic), `Rejected` (a rejected sample not
  replaced), `ReadyUnpaid` (stage Ready or Delivered and balance not Paid).
- `View` presets: `Reception` (AwaitingSample or remaining patient share), `Verification` (AwaitingVerification), `FrontDesk` (Ready).
- `Search`: accession, sample number, patient number, name, mobile (escape-aware ILike).
- Sort keys (allow-list, `ListSorting`): `timeLeft` (default: urgent first, then minutes left ascending, nulls last), `registeredAt`,
  `accession`, `patient`, `stage`.
- `counters`: per stage (all six) and per attention flag, for the whole filtered day **before** the stage / attention filters, so the
  strip and the chips always show the day.
- **Item** `LabTrackerItem { visitId, accession, patientId, patientNumber, patientName (VIP masked like the visits register), gender,
  ageText, urgent, registeredAtUtc, registeredByName, payerName, tierName, branchId, stage, nextStep, minutesLeft?, delayState?,
  registration { lines, cancelledLines }, samples? { total, ordered, collected, received, rejected, firstRejectionReason? },
  results? { total, ordered, complete, reviewed, verified, printed, hidden, panic, delta, firstPanicTest? },
  payment? { status, patientShare, paid, remaining } }` — `samples` only with `samples.view`, `results` only with `results.view`, `payment`
  only with `billing.view` (null otherwise; the stage and next step are still computed on the server from everything). Results are
  counted over the results the user may see (result rights per clinical group and branch, exactly the worklist's rule,
  `WorklistQueries.cs`); `hidden` counts the others.
- One SQL read through a read port in Infrastructure (`ILabTrackerReader`, `Persistence/Tracker/`), tenant-scoped (RLS; no elevation),
  paging after ordering; the per-visit aggregates as grouped sub-selects. Performance target: 500 visits of a day in < 300 ms on the
  dev instance; add an index in the module SQL only if needed (report it).

**Endpoint** `GET /api/v1/tracker/visits` (query string = the query's fields) in `Controllers/Tracker/TrackerController.cs`.
No migration is expected (report if one is needed). No new permission key.

## 1. Packages

| Package | Slot | Owns |
| --- | --- | --- |
| P-API | `rep` | `src/Platform.{Domain?,Contracts,Application,Infrastructure,WebApi}/…/Tracker/**` (new module folders), `Platform.Contracts/Tracker`, tests (unit for the stage / next-step / time-left rules as a pure function in `Platform.Domain/Tracker/VisitProgress.cs` or Application; integration for the reader; functional for the endpoint: shape, 403 without `registration.view`, null sections without the module permissions, result-rights hiding, VIP masking, filters, counters, sort keys added to the sort gate), intake `docs/intake/LIS-INT-TRK-001_Lab-Tracker.md` (new, short: purpose, rules above, endpoint, codes, open questions with the assumed answers) |
| P-SPA | — | `frontend/src/app/features/tracker/**` (new feature), `core/api/tracker.contracts.ts`, `public/i18n/tracker.{en,ar}.json` |

Integrator: nav entry (client · reception · first entry · `/tracker` · `registration.view` · `shell.nav.labTracker` · "Lab Tracker" /
"متابعة سير العمل" · R1), `app.routes.ts` (`tracker` lazy route with `tenantGuard` beside the other client features), shell dictionaries,
SRS (new page CP-69 Lab Tracker), UAT script, README, CLAUDE.md, release.

## 2. P-API

As §0. Put the pure rules (stage, next step, time left from counts + clock) in one testable place; the reader returns counts and
timestamps, the handler applies the rules, masks, filters the sections by permission. Reuse existing helpers: `BranchScope`, tenant
time zone (`SettingKeys.TimeZone` / client record), `ResultRights`, VIP masking of the visits register, `TurnaroundClock`, `ListSorting`.
Commands: `source scripts/slot-env.sh rep`, reset the slot database before the database suites, run all five suites one at a time.
Never edit `PermissionCatalog.cs`, `AppDbContext.cs` (report DbSet needs; a read port over existing entities should need none),
`Persistence/Migrations/**`, `deploy/**`, the frontend, README, CLAUDE.md, `Support/` (report lines). Do not commit.

## 3. P-SPA

Feature `features/tracker`: `data/tracker.api.ts`, `state/tracker.facade.ts` (`resource()` keyed on the filter signal; **polling every
30 s** while the document is visible — `visibilitychange`, paused when hidden, reload on return; a "Live · updated n s ago" label;
no `subscribe()` in components), `pages/tracker.page.{ts,html,css}` (route `''` of `TRACKER_ROUTES`; export it for the integrator), small
`ui/` components to keep each stylesheet under 4 kB:
- **Header**: title, branch chip, live label, search (scan-friendly: Enter submits, the field keeps focus), period picker (today · yesterday · a day · last 7 days · from / to).
- **View tabs**: All · Reception · Bench (a link to `/results/worklist`) · Verification · Front desk — with counts where the counters
  give them.
- **Pipeline strip** (layout A): six stage tiles (Registered = total, Awaiting sample, In the lab, Awaiting verification, Ready,
  Delivered), each a button that filters by stage (toggle; `aria-pressed`).
- **Needs attention** chips: Delayed, Panic, Rejected, Ready but unpaid — filter buttons with counts.
- **Table | Board** segmented toggle (`aria-pressed`), remembered for the session in a signal (no web storage).
- **Table** (layout A): columns Visit (accession link, Urgent, patient · sex · age), 1 Registration (time, payer), 2 Samples, 3 Results
  (+ Panic), 4 Payment, Next step (a button/link to the page that does it: CollectSample → `/registration/accession?visitId=`,
  Enter/Review/VerifyResults → `/results/by-accession?accession=`, TakePayment → `/billing/payments?visitId=`, PrintReport →
  `/results/by-accession?accession=`), Time left (pill). Row edge colour by delay state; status = icon + text + colour (never colour alone);
  server paging and sorting (`sortMode: 'server'`).
- **Board** (layout B): five lanes (Awaiting sample, In the lab, Awaiting verification, Ready, Delivered) built from the current page
  filtered per lane — simplest correct approach: one request per lane with `stage=` and `pageSize=20` while the board is shown (say how you
  did it); each card: accession, time-left pill, patient, one-line detail, payment pill, flag pill (Panic / Rejected / Collect first), a
  four-segment progress bar (registration, samples, results, payment) with an `aria-label`; "+ n more" switches to the table filtered by
  that stage. Lanes scroll horizontally on narrow screens.
- Sections the API returns as null show "—" with a tooltip "Not in your rights".
- Arabic RTL with logical properties; dictionaries complete in both languages; counted forms; F10 not used here.
- Specs: facade (filters → query string, polling pauses when hidden, live label), page (strip/chips filter, toggle, next-step links,
  null sections), board lanes.
Commands as usual from `/e/LIS/frontend`; never start servers; do not commit. Never edit `app.routes.ts`, `shell/**`, `shell.*.json`,
other features.

## 4. Integrator checklist

Nav, routes, shell dictionaries; gates (five suites + frontend); browser check on the review stack (both layouts, filters, polling,
Arabic, phone width); SRS 1.4 (CP-69), UAT script `UAT-REG-16` (Lab Tracker), README, CLAUDE.md; release (minor). Installation only
when the user asks.
