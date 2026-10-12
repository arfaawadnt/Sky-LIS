# Re-test 2026-10-12 · R-CLIENT · Client portal fixes of the v0.5.0 campaign

Build under test: the **pinned fix build** served by `tests/validation/serve-ui.mjs` from `.local/validation/ui`
(bundle `main-7IZ4YQ6H.js`, copied 2026-10-12 01:42). It holds the working tree on top of `v0.5.3` (commit `5ddba06`) with
the uncommitted fixes of this round. The SPA `http://127.0.0.1:4300` proxies to the validation API `127.0.0.1:5102`, which
runs the fixed code. Database `lis` on the private PostgreSQL `127.0.0.1:5433`, read only as `lis_owner`.

- Laboratories: demo laboratory (tenant `01a12494-7ca8-75a8-873d-645d12599db7`, branch MAIN, treasury MAIN-CASH) and the
  Walk-through Laboratory (tenant `01a124d7-5e87-7d30-af34-12248755e2f1`).
- Personas: `lab.admin` (every permission), `uat.reception` (reduced persona; no See-VIP), `uat.tech` (no `billing.view`),
  `wt.admin`, and two users created for this run, `v15_must` and `v15.export`.
- Tester: automated browser session in the Browser pane, own tabs `tab-11`, `tab-13` (closed during the run, see §8) and
  `tab-15`, host `127.0.0.1` only. Other testers' tabs (`tab-3` on 127.0.0.1, `tab-10` / `tab-12` on localhost) were never
  used.
- Time: 2026-10-12 01:44 to 02:50 (UTC+3).
- This run tested and reported only. No application code, dictionary, test, script or migration was changed. Test data
  carries the prefix **V15**; the two new passwords are only in the git-ignored `.local/dev-credentials.md`.

Method notes:

- **Input.** The Browser pane was hidden, so clicks and input were dispatched from the page context (`element.click()`,
  `input` / `change` / `blur` events, `keydown` for Enter, Esc and F10). This runs the same Angular handlers as a user.
- **Two-tab reloads** (A-04) used a `BroadcastChannel` message from one tab: the other tab reloaded on receipt and the
  sender reloaded itself in the same task. The navigation timings of the two tabs differ by 1.4 ms.
- **Transient text** (A-06) was captured with a `MutationObserver` installed before the client-side navigation; it
  records every text node added and every raw `feature.key` that appears, even for one frame.
- **Downloads** (CSV) were intercepted in the page (`URL.createObjectURL`, anchor `download`); nothing was written to disk.
  **Prints** were captured by replacing the print-kit iframe's `print()`.
- **No forced requests.** No bearer token was placed in the page. Server refusals were read from the network log.
- **Database.** Every save was checked with a read-only query as `lis_owner` (session `default_transaction_read_only`).

## 1. Summary

| Items re-tested | FIXED | PARTLY | NOT FIXED |
| --- | --- | --- | --- |
| 24 | 23 | 1 | 0 |

| Id | Subject | Verdict |
| --- | --- | --- |
| A-01 (S2) | saved registration came back as a draft | **FIXED** (residue R-01, S4) |
| A-02 | delta check skipped when the order of entry and verification changes | **FIXED** |
| A-03 | VIP mask leaks | **PARTLY** (R-06, S3; R-05, S4) |
| A-04 (= C-02) | two tabs sign each other out | **FIXED** |
| A-05 | worklist age for a patient registered by age | **FIXED** |
| A-06 | raw dictionary key on the first visit | **FIXED** |
| A-07 | Arabic digits in the registration checklist, bar and amounts | **FIXED** (residue R-02, S4) |
| A-08 | blocked save does not mark the services card | **FIXED** |
| A-09 | stale picker results clickable during the debounce | **FIXED** |
| A-10 | dashboard accepts a future day; clearing keeps it | **FIXED** |
| A-11 | repeat-order message names the profile, not the test | **FIXED** |
| O-9 | money on a technician's dashboard | **FIXED** |
| Results wording | "1 results saved" | **FIXED** |
| B-01 | ORG-017 error survives a corrected country; silent blocked save | **FIXED** |
| B-02 | Add user without a branch creates an every-branch user | **FIXED** |
| B-03 | sort covers only the loaded page | **FIXED** (observation R-09, S4) |
| B-04 (= C-07) | tab stays on `?mustChange=1` after the forced change | **FIXED** |
| B-05 | hidden filter in the price grid | **FIXED** |
| B-06 | copy dialog proposes a taken code | **FIXED** |
| B-07 | base price lists: year filter and CSV export | **FIXED** |
| B-08 | treasury search by Arabic name | **FIXED** |
| B-09 | shell shows the English role name in Arabic | **FIXED** (observation R-12, S4) |
| O-1 | activating a price list asks no confirmation | **FIXED** |
| `_` search | literal underscore in Users, Branches, Payers, Treasuries | **FIXED** |

Regression smoke: 26 client pages for `lab.admin` and 23 for `uat.reception`, each in English and Arabic (98 page runs).
Every page opened and loaded its list. No raw dictionary key appeared and the console showed no script error. The
end-to-end cycle passed: registration with a card payment, result entry, review, verification, service cancellation and
refund.

New findings: **12 × R-nn: 0 × S1, 0 × S2, 1 × S3, 11 × S4** (§5). Outbox during the run: demo laboratory 64 events, Walk-through 11, all
processed, none dead-lettered, no retry. Audit trail: demo sequences 479…542 and Walk-through 307…317, with no gap (the
tenant maxima equal the row counts).

## 2. Findings re-tested

### A-01 (S2) · saved registration comes back as a draft · FIXED

Every exit of the "Registration saved" dialog was tried, then Registration was opened again (sidebar or "New
registration"). The page was empty each time: no patient, "No services yet", checklist "1 of 3 done".

| Save | Exit of the saved dialog | Then | Result |
| --- | --- | --- | --- |
| ACC-2026-00030, CREA, cash 80 (F10) | Open the visit | editor › New registration | empty |
| ACC-2026-00031, RENAL, unpaid | Collect on the Payments page | sidebar Registration | empty |
| ACC-2026-00032, GLU, unpaid | Open the visit | sidebar Registration | empty |
| ACC-2026-00033, NA, unpaid | × (Close) | stays on the page; then Search registrations › Registration | services, patient and payment cleared |
| ACC-2026-00034, HGB, unpaid | Esc | stays on the page | services, patient and payment cleared |
| ACC-2026-00035, CREA + K, cash 200 | New registration | – | empty |
| ACC-2026-00038 (uat.reception), K, unpaid | Job order (printed), then Open the visit | sidebar Registration | empty |

DB: nine visits ACC-2026-00030…00038, one per save, no duplicate and no duplicate payment (RCT-2026-00038 / -00039 /
-00040 only).

Residue: after × or Esc the patient picker still lists the previous search result while its box is empty. One click on it
re-selects the previous patient (R-01, S4). The services, discount and payment are cleared, so no duplicate visit is
pre-filled.

### A-02 · delta check · FIXED

Two orders were tried, each on a new V15 patient (CREA delta limit 20 %):

1. **As in the defect** (PAT-2026-00013). CREA 1.0 entered on ACC-2026-00030 and CREA 2.0 on ACC-2026-00031, both before
   any verification. ACC-30 was then reviewed and verified. When ACC-31 was **reviewed** the row showed **"Δ 100%"** and
   **Acknowledge**. **Verify** opened the "Delta check" dialog: "Creatinine: 2.00 (Δ 100%, previous 1)". Cancel left the
   result Reviewed; "Acknowledge and verify" verified it.
2. **Reviewed before the older one was verified** (PAT-2026-00014). CREA 1.0 on ACC-2026-00035 and 2.5 on ACC-2026-00036.
   ACC-36 was reviewed first, with no Δ (nothing verified yet). Then ACC-35 was reviewed and verified. The first **Verify**
   on ACC-36 was refused by the server: "Acknowledge the delta check before verifying (BR-11)." and "1 result was refused;
   see the code on the row." The row then showed "Δ 150%" and Acknowledge. The second Verify opened the dialog, and
   "Acknowledge and verify" verified it.

DB (`app.results`): ACC-31 CREA `delta_flag` t, `delta_percent` 100.0, `previous_result_id` → ACC-30, previous value 1,
acknowledged by lab.admin. ACC-36 CREA `delta_flag` t, 150.0, previous → ACC-35, acknowledged by lab.admin. Trail
`ResultDeltaAcknowledged` ×2.

Observation: in order 2 the user meets a refusal before the dialog, because the page shows no Δ until the server re-checks
at verification. The rule holds; the first click is wasted.

### A-03 · VIP mask · PARTLY

Personas: `uat.reception` (role Reception, no See-VIP right), `v15.export` (new role "V15 Patient Export":
`patients.view`, `patients.export`, `dashboard.view`; no See-VIP), and `lab.admin` (with the right). VIP patients:
PAT-2026-00010 and PAT-2026-00007.

| Place | Without the right | With the right (lab.admin) |
| --- | --- | --- |
| Patients list | "VIP patient"; mobile, national ID "—"; no VIP column | name, mobile, national ID, VIP badge |
| Export CSV (`v15.export`) | `PAT-2026-00010,Mrs,VIP patient,,Female,1996-10-11,yes,30,,,,,,,Active,,2026-10-11`; mobile, national ID and e-mail empty; no VIP column | full row, `VIP` column `yes` |
| Search by "V14 Walk-in Two", "walk-in two", mobile 01011140002, 1011140002, 13-digit partial national ID, "VIP", prefix "PAT-2026-0001" | not found (the prefix lists PAT-11…14 but not PAT-10) | found |
| Search by full number PAT-2026-00010 / full national ID 29601010100022 | found, shown as "VIP patient" | found |
| Registration picker | number / full national ID → "PAT-2026-00010 VIP patient Female · 30 y"; name and mobile → "No patient file matches" | name, mobile, VIP badge |
| Similar-patient warning (New patient: name, mobile, national ID of a VIP) | **no warning at all** (the non-VIP check works: "V11 Walk-in One" → PAT-2026-00006 "Similar name"; mobile 01011140009 → PAT-2026-00009 "Same mobile") | "PAT-2026-00010 V14 Walk-in Two … Same mobile" |
| Opening the file (reception) | real identity shown: "PAT-2026-00010 · V14 Walk-in Two", mobile 01011140002 (operational rule) | same |

Not fixed in full:

- The similar-patient warning omits a VIP file entirely instead of listing a masked "VIP patient" entry. A clerk who
  types the exact mobile of a VIP patient gets no warning and can create a duplicate file (R-06, S3).
- Sorting the Patients list by Name places the masked rows at their real names' position ("V11 Walk-in One", "VIP
  patient", "V12 Retest One"…), which hints at the hidden name (R-05, S4).

### A-04 (= C-02) · two tabs sign each other out · FIXED

- **Simultaneous reloads.** lab.admin was signed in on `tab-11` and `tab-13`, and both tabs reloaded at the same moment
  five times (01:46:09 … 01:46:44). Both stayed signed in each time.
- **DB, simultaneous reloads.** `identity.refresh_sessions` of family `01a12d23…` shows one rotation per tab and per reload,
  30–80 ms apart and serialized (01:46:09.787 → .863, 01:46:32.571 → .609, …). No row was revoked.
- **Idle, then reload.** `v15.export` was signed in on `tab-11` and `tab-15`. Both tabs were left idle from 02:29:34 to
  02:46:47 (17 min, past the 15-minute access token), then reloaded together; their navigation start times differ by
  1.5 ms. Both came back signed in ("V15 Patient Export" in the sidebar). DB: the token issued at 02:29:33.949 rotated at
  02:46:47.389, and its successor at 02:46:47.503. The two refreshes were serialized 114 ms apart, and no row was revoked.
- **Security events.** `audit.security_events` holds **no `REFRESH_REUSE_DETECTED`** for lab.admin, uat.reception,
  uat.tech, wt.admin, v15_must or v15.export. The only two such events of the period (01:49:45) belong to `v15.user` of the
  platform tenant, another re-tester's account on localhost.

### A-05 · worklist age · FIXED

V15 Age Only (PAT-2026-00014) was registered by age only (52 years; DB `date_of_birth` null, `age_years` 52). The
worklist shows "52y · F". The other by-age patients now show their age too: PAT-2026-00010 "30y · F" and PAT-2026-00008
"40y · M".

### A-06 · raw keys on first visit · FIXED

A full page load was made on a page of another feature (Profile or Treasury access). The first client-side visit then went
to Results by accession (plain, and with `?accession=ACC-2026-00026`), Patients and Dashboard. The `MutationObserver`
recorded **no** raw key on any of them. The status region read "Loading…" from its first frame.

### A-07 · Arabic digits · FIXED

In Arabic the registration page uses Arabic-Indic digits throughout:

- checklist "اكتمل ٤ من ٤", "الخدمات المسعّرة: ١", "اكتمل ١ من ٣";
- bar "مطلوب استكمال ٢:";
- cap "الخصم · حدّك ١٠٠٪";
- amounts "١٢٠٫٠٠ EGP", line prices, and the expected time "١٣‏/١٠‏/٢٠٢٦، ٢:٠٢ ص".

Residue: the patient card and picker read "أنثى · 52 سنة" in Latin digits (R-02, S4).

### A-08 · blocked save marks the services card · FIXED

Save with no patient and no service: toast "2 items need attention before saving; they are listed at the bottom of the
page."; `#reg-patient` and `#reg-services` carry `invalid`, the service entry is `aria-invalid`, and the patient search
has the focus.

### A-09 · stale picker results · FIXED

Search PAT-2026-00009, then type "V14 Walk-in Two". At 0, 50 and 150 ms the previous result is still listed but its button
is `disabled` (list `aria-busy="true"`). A click on it 60 ms after the change did nothing. From 300 ms the list is empty,
and from 600 ms it shows PAT-2026-00010.

### A-10 · dashboard day · FIXED

The day input carries `max="2026-10-12"`. Setting 2026-10-20 snaps back to 12 Oct ("today" captions). Clearing the field
from 11 Oct returns to 12 Oct. A past day (11 Oct) works: "12 on 11 Oct 2026", collected EGP 1,520.00.

### A-11 · repeat-order message · FIXED

RENAL ordered for PAT-2026-00013, whose CREA had been ordered as a single test a minute earlier (ACC-2026-00030):

- bar notice and "Repeat order" dialog: "**CREA (RENAL)**: this test was ordered for the patient on 12/10/2026, 01:52
  (ACC-2026-00030); the repeat window is 30 days. Confirm to register it again."
- For a service that is itself the test: "CREA: this test was ordered…".

### O-9 · money on a technician's dashboard · FIXED

- `uat.tech` (no `billing.view`): the dashboard has no "Collected" tile. The API answer carries `"collectedToday":null`.
  `v15.export` has no tile either.
- `uat.reception` and `lab.admin` (with `billing.view`) see "Collected today EGP 370.00". This is 80 + 200 + 240 − the
  refund of 150, equal to the payments and refunds of the day.

### Results wording · FIXED

"1 result saved.", "1 result reviewed.", "1 result verified (electronic signature).", "2 results saved.", "2 results
verified (electronic signature).", "1 result was refused; see the code on the row."

### B-01 · Branches ORG-017 and blocked-save notice · FIXED

Walk-through Laboratory, Add branch V15_EAST:

1. With Jordan + "Cairo — Egypt", the server answers 422 ORG-017. The toast reads "The governorate must belong to the
   chosen country.", Governorate is `aria-invalid="true"` and the form is `ng-invalid`.
2. Changing **Country** to Egypt makes Governorate `aria-invalid="false"` and the form `ng-valid`.
3. Save: 201, "Branch saved.".

Blocked save (empty form):

| Page | Notice | Marked | Focus | Request sent |
| --- | --- | --- | --- | --- |
| Branches | "The record was not saved: correct the marked fields first." | Code, Name EN, Name AR | Code | none |
| Units | same notice | Unit | Unit | none |
| Payers & contracts | same notice | Name EN, Name AR, Search key | Name (English) | none |
| Doctors (catalogue B page, for comparison) | field messages only, no notice | Code, Name EN, Name AR | Code | none |

### B-02 · Add user with no branch · FIXED

Walk-through Laboratory, Add user `v15_must`, with "Every active branch" unticked and no branch ticked. Create user
answered "Choose at least one branch, or every branch." and **no request was sent**. DB: no `v15_must` row. With MAIN
ticked: 201 "User created."; DB `branch_ids` = {MAIN}.

### B-03 · sort covers the whole list · FIXED

Every header click now requests the server order (`&sort=…&direction=…`):

| List | Rows | Check | Result |
| --- | --- | --- | --- |
| Units (Walk-through, All) | 22 | page size 20, page 2, then Unit descending | page 1 = V14 µmol/L, V14 PG-17…PG-01, V14 mg/dL, V11 U/L; page 2 = V11 mmol/L, V11 g/dL; equal to `order by lower(name) desc` |
| Worklist (demo, from 1 Oct, all states) | 56 | page size 20, Test descending | page 1 = hCG, eGFR ×5, Sodium ×6, Potassium ×8; page 2 = Potassium ×5, Haemoglobin ×5, Glucose ×10; equal to the DB group counts in `name_en desc` |
| Tests | 8 | Name ×2 | `sort=nameEn&direction=desc`; equal to the DB order |
| Services | 8 | Name ×2 | `sort=nameEn` asc / desc |
| Payers & contracts | 2 | Payer ×2 | `sort=nameEn` asc / desc |
| Treasuries | 2 | Name ×2 | `sort=nameEn` asc / desc |
| Patients | 12 | Name ×2 | `sort=name` asc / desc |
| Roles & privileges | 7 | Role ×2 | `sort=nameEn` asc / desc |
| Users | 9 | Name ×2 | `sort=nameEn` asc / desc |
| Branches | 2 | Name ×2 | `sort=nameEn` asc / desc |

Observation R-09 (S4): the lists do not sort names the same way. Units sort case-insensitively. Tests, Services and the
Worklist use byte order, so "hCG" and "eGFR" come after "Sodium" in a descending sort. The database collation is `C`.

### B-04 (= C-07) · forced password change · FIXED

1. `v15_must` (created with "Must change the password", Walk-through) signs in and lands on
   `/account/profile?mustChange=1` with "Your administrator asked you to choose a new password before you continue."
2. Update password: "Your password was changed." The tab moves on at once to `/account/dashboard`. Neither the query
   parameter nor the banner remains.

DB: `must_change_password` f, `password_changed_at_utc` 02:26:30, `PASSWORD_CHANGED`.

### B-05 · price grid filter · FIXED

V15-PL25 grid, "Add a service" → V11-GLU (marked "already listed"):

1. Toast "The service is already on the list."; the **search box shows "V11-GLU"** and the grid "Showing 1–2 of 2"
   (V11-GLU, V11-GLUP).
2. Refresh keeps the box and the rows.
3. Clearing the box brings back all five lines.

### B-06 · copy dialog code · FIXED

Copy list on V11-PL26 proposes **V11-PL27-2** (V11-PL27 exists, inactive). Typing V11-PL27 and pressing Create copy: 409,
"A base price list with this code already exists." under Code (`aria-invalid="true"`). The copy V15-PL25 (2025, ×1, round
0.01) was then created inactive.

### B-07 · base price lists filter and export · FIXED

- The list page has a **Year** select (All years / 2029 / 2027 / 2026) and **Export CSV**.
- Status All: four lists. Year 2029: V14-PL29 only. Year 2026: V11-PL26 and V14-PL26.
- The export under Year 2026 + All gives `base-price-lists-2026-10-12.csv` with header `Code,Name (English),Name
  (Arabic),Year,Active lines,Lines,Derived contracts,Copied from,Active` and the same two rows as the screen (5/5 and
  9/9 lines, V14-PL26 copied from V11-PL26, Active Yes).

### B-08 · treasury search · FIXED

English UI, Walk-through Laboratory:

| Search | Found |
| --- | --- |
| "الشرق" (Arabic name) | V14-EAST-CASH |
| "خزينة الاختبار" | V15_TR |
| "east", "Underscore" (English names) | V14-EAST-CASH, V15_TR |
| "EAST-C", "MAIN-CA" (part of a code) | V14-EAST-CASH; V11-MAIN-CASH and V14-MAIN-CASH |

The search is now sent to the server (`/api/v1/treasuries?search=…`).

### B-09 · Arabic role name in the shell · FIXED

In Arabic the sidebar and the top-bar user button read "مدير المختبر" for lab.admin and "الاستقبال" for uat.reception.

Observation R-12 (S4): the user's own name stays in English ("UAT Reception Clerk", "Laboratory Administrator"), although
the memberships hold Arabic names ("موظف استقبال", "مدير المختبر").

### O-1 · price-list activation confirm · FIXED

On V15-PL25, Activate opens "Activate list — Activate V15-PL25? Apply will use it, and if it is the most recent active list
it prices cash patients from now on.".

- Cancel: the list stays Inactive.
- Activate again, then confirm: "List activated.", status Active.

The list was deactivated afterwards with a confirm ("List deactivated."). Year 2025 was chosen so that the cash default of
the laboratory never changed.

### `_` search · FIXED

Each list was given one V15 record whose code or name holds "_". A probe whose "_" would act as a wildcard had to find
nothing.

| List | Literal match found | Wildcard probe → nothing |
| --- | --- | --- |
| Branches | "V15_", "5_e", "_" → V15_EAST | "a_n" (would match "Main") |
| Payers & contracts | "V15_", "5_u", "5_U" → V15_Under Payer | "a_e" (would match "Payer"), "r_u" |
| Treasuries | "V15_", "5_t" → V15_TR | "a_n", "A_N" |
| Users | "v15_", "5_m", "_must" → V15 Must Change | "4_r" (would match "v14.reception") |

## 3. Regression smoke

Each page was opened from the sidebar. On each, the list was loaded, one row action and one editor were opened and then
closed, and the page was scanned for raw keys. The console was read at the end of each pass.

| Page | lab.admin EN | lab.admin AR | uat.reception EN | uat.reception AR |
| --- | --- | --- | --- | --- |
| Dashboard | OK (14 delayed rows; New registration) | OK | OK | OK |
| Registration | OK (full flows, §2) | OK (A-07) | OK (save + documents) | OK |
| Search registrations | OK (Edit → visit editor) | OK | OK | OK |
| Patients | OK (Open → file; New patient drawer) | OK | OK (VIP masked) | OK |
| Price query | OK | OK | OK | OK |
| Worklist | OK (Results → by accession) | OK | OK | OK |
| Results by accession | OK (entry / review / verify) | OK | OK | OK |
| Treasuries | OK (Edit / Add) | OK | OK (R-07) | OK (R-07) |
| Treasury access | OK | OK | OK | OK |
| Payers & contracts | OK (Edit / New) | OK | OK (R-07) | OK (R-07) |
| Base price lists | OK (Edit / New list) | OK | OK (Open price grid) | OK |
| Payments | OK (visit account, refund link) | OK | OK | OK |
| Branches | OK (Edit / Add) | OK | – (no right) | – |
| Roles & privileges | OK (Open / Add role) | OK | – | – |
| Users | OK (Open / Add user) | OK | – | – |
| Preparations | OK | OK | OK (R-07) | OK (R-07) |
| Comment library | OK | OK | OK (R-07) | OK (R-07) |
| Doctors | OK | OK | OK (R-07, R-08) | OK (R-08) |
| Microbiology | OK (Add drug type) | OK | OK | OK |
| Clinical groups | OK | OK | OK (R-07) | OK (R-07) |
| Services | OK (row → editor; Add) | OK | OK (row → read-only editor) | OK |
| Tests | OK (row → editor; Add) | OK | OK | OK |
| Service sequence | OK | OK | OK | OK |
| Sample types | OK | OK | OK (R-07) | OK (R-07) |
| Units of measurement | OK | OK | OK (R-07) | OK (R-07) |
| Profile | OK | OK | OK | OK |

Console:

- lab.admin, both languages: no error.
- uat.reception: one 401, from the silent refresh before sign-in. Three 403: two from `GET /api/v1/organization/branches`
  on Doctors (R-08), one from the deliberate Save in R-07.
- No script error in either pass.

Other checks:

- **Sidebar collapse.** At 1280 px the first top-bar button reads "Collapse menu", toggles `.shell.collapsed` and becomes
  "Expand menu". The sidebar foot has no collapse button.
- **F10.** F10 saved registrations (ACC-30, ACC-37, ACC-38) from the service entry and from the document.
- **Egyptian national ID.** 29005150101231 filled date of birth 1990-05-15, gender Male and governorate Cairo, with the
  hint "Filled from the national ID: date of birth, gender, governorate." The patient form shows the four groups Identity,
  Sex and age, Date of birth or age, Contact. DB PAT-2026-00013: 1990-05-15, Male, governorate set, national ID stored.
- **Top bar.** The laboratory's own name is not in the top bar, as announced.

### End-to-end (lab.admin, demo laboratory)

1. Registration: PAT-2026-00014 V15 Age Only, Cash — Default, GLU + HGB, **Card 240** with reference V15-CARD-001, saved
   with F10. Result: ACC-2026-00037, "2 tube(s)", "240.00 received — receipt RCT-2026-00040".
2. Results: GLU 95, Save "1 result saved.", Review, Verify "1 result verified (electronic signature).".
3. Search registrations › Cancel services: HGB, reason "Patient declined", note "V15 E2E refund". "The services were
   cancelled."; Remaining −150.00.
4. Payments › **Refund the balance**: `/billing/refunds?visitId=…`, "Refundable: 150.00 EGP", reason "Service cancelled",
   Cash. Result: RFD-2026-00009 150.00.

DB: ACC-37 lines GLU Ordered / HGB Cancelled; RCT-2026-00040 240.00 Card V15-CARD-001; RFD-2026-00009 150.00 Cash,
treasury MAIN-CASH, device `a0ab790c…`, branch MAIN. Trail `VisitRegistered`, `PaymentRecorded`,
`VisitLineCancelled`, `ResultCancelled`, `SampleCancelled`, `RefundRecorded`; outbox processed.

## 4. Database verification (abridged)

| Ref | Item | Query (abridged) | Result |
| --- | --- | --- | --- |
| DB-01 | A-01 | visits `accession >= 'ACC-2026-00030'` with lines and payments | 9 visits, one per save; payments RCT-38 (80 Cash, ACC-30), RCT-39 (200 Cash, ACC-35), RCT-40 (240 Card V15-CARD-001, ACC-37); no duplicate |
| DB-02 | A-02 | `app.results` of ACC-30/31/35/36 | as §2 A-02 (`delta_flag`, `delta_percent`, `previous_result_id`, acknowledging user) |
| DB-03 | A-04 | `identity.refresh_sessions` of lab.admin since 01:44; `audit.security_events` since 01:40 | serialized rotations, nothing revoked; no `REFRESH_REUSE_DETECTED` for the users of this run |
| DB-04 | A-05 | `app.patients` PAT-13/14 | PAT-14 `date_of_birth` null, `age_years` 52, `age_recorded_on` 2026-10-11 (R-11); PAT-13 DOB 1990-05-15, NID, governorate |
| DB-05 | B-02 / B-04 | `identity.platform_users` + `app.tenant_memberships` where `username like 'v15%'` | no row after the refused create; then `v15_must` {MAIN}, `must_change_password` f after the change |
| DB-06 | B-03 | `measurement_units` / `results` ordered | equal to the screen order (§2 B-03) |
| DB-07 | clean-up | branches, treasuries, payers, price lists `like 'V15%'` | V15_EAST, V15_TR, V15_UND, V15-PL25 all `is_active` f |
| DB-08 | languages | `preferred_language` | lab.admin, uat.reception, uat.tech, wt.admin, v15_must, v15.export: `en` at the end |
| DB-09 | all | `ops.outbox_events` since 01:44 | demo 64 / 64 processed / 0 dead / max attempts 0; Walk-through 11 / 11 / 0 / 0 |
| DB-10 | all | `audit.audit_trail` since 01:44 | demo 479…542, Walk-through 307…317; tenant maxima equal the row counts (no gap) |

## 5. New findings

| Id | Sev | Where | Steps | Expected | Observed | Suspected cause |
| --- | --- | --- | --- | --- | --- | --- |
| R-01 | S4 | Registration (A-01 residue) | Save a registration; close the saved dialog with × or Esc | the page fully reset | services, payment and checklist reset; the patient picker still lists the last search result under an empty box, and one click re-selects that patient | the reset clears the facade but not the picker's own result list (`shared/patient` picker keeps `results` until the next search) |
| R-02 | S4 | Registration, Arabic (A-07 residue) | Choose a patient in Arabic | one digit set | patient card and picker "أنثى · 52 سنة" (Latin digits) while the rest of the page uses Arabic-Indic | the age label of the patient card / picker is not formatted with the localized number format |
| R-03 | S4 | every paged list, Arabic | Open any list in Arabic | one digit set | paginator "عرض 1–14 من 14" in Latin digits next to Arabic-Indic amounts | `shared/ui/paginator` interpolates raw numbers |
| R-04 | S4 | Results by accession, Arabic | Open the page | one name for the page | sidebar "النتائج برقم الاستلام", page title "النتائج برقم الطلب" | two dictionary entries (shell nav vs `results.*` title) |
| R-05 | S4 | Patients, user without See-VIP | Sort by Name | masked rows give no hint of the name | "VIP patient" rows sit at their real names' place (between "V11 Walk-in One" and "V12 Retest One") | server sorts on the real name before masking; sort masked rows apart or by number |
| R-06 | **S3** | New patient (BR-01), user without See-VIP | Type the exact mobile (or name) of a VIP patient in New patient | the similar-patient warning lists a masked "VIP patient" entry, so a duplicate file is avoided | no warning at all; a duplicate file of a VIP patient can be created. With the right the warning lists PAT-2026-00010 "Same mobile". The check sends only name and mobile (`/patients/similar?name=…&mobile=…`), not the national ID | `Platform.Application/Patients/Queries/PatientQueries.cs` `FindSimilarPatientsHandler` l.112–113 drops every identity-masked row (`.Where(row => !PatientProjections.IsIdentityMasked(row, canSeeVip))`) instead of returning it masked; a deliberate filter that needs a business decision (privacy vs duplicate files) |
| R-07 | S4 | master-data lists, viewer | uat.reception clicks a row on Treasuries, Payers, Preparations, Comments, Doctors, Clinical groups, Sample types or Units | read-only view or nothing | the **Edit** drawer opens with editable fields and an enabled Save; Save → 403, "You do not hold the permission for this action." (nothing changed) | `master-data-page.component.html` l.30 `(rowActivate)="openEdit($event)"` with no permission check (unchanged since the first commit) |
| R-08 | S4 | Doctors, reception | Open Doctors | no failed request | `GET /api/v1/organization/branches?pageSize=200` → 403 (console error) on each visit; the branch column still reads "كل الفروع" for DR01 | the page loads the branch list for its column / picker without `organization.view`; use a lookup open to `catalogue.view` |
| R-09 | S4 | sorting (B-03) | Sort Tests / Services / Worklist and Units by name | one ordering rule | Units case-insensitive; Tests, Services, Worklist byte order ("hCG", "eGFR" after "Sodium" when descending); DB collation `C` | sort expressions differ per query (`lower()` on units only) |
| R-10 | S4 | Dashboard | 7-day chart, a day with one registration | "1 registration" | "12 Oct 2026: 1 registrations" (EN) | plural not applied to the chart label |
| R-11 | S4 | New patient by age | Create a patient by age at 01:57 local on 12 Oct | the age anchored on the laboratory's day | `age_recorded_on` 2026-10-11 (UTC date), the estimated DOB shows 1974-10-11 | the age anchor uses the UTC date, not the laboratory time zone (UTC+3) |
| R-12 | S4 | shell, Arabic (B-09 neighbour) | Switch to Arabic | the user's Arabic name | the role is Arabic, the user's name stays English ("UAT Reception Clerk" although `full_name_ar` = "موظف استقبال") | the shell shows the profile `displayName` only |

Observations (not counted):

- Results by accession still reads "Matching visits (0)" for a visit opened from a link (seen in v0.5.0).
- The registration grid shows the expected time as "13/10/2026, 01:52". The other pages use "12 Oct 2026, 00:24".
- The delta dialog shows the previous value as "previous 1" (not "1.00").
- In the Cancel services dialog the GLU line of ACC-37 (verified result) could be ticked; the dialog text says such
  services cannot be cancelled. The refusal was not tried.
- The patients export without See-VIP keeps the city, nationality and age of a VIP patient. Only the name, mobile, national
  ID and e-mail are blank. This is within the stated rule.

## 6. Data created (dev database `lis`, invented data)

Demo laboratory:

- **Patients:**
  - PAT-2026-00013 "V15 Delta Patient", created from national ID 29005150101231;
  - PAT-2026-00014 "V15 Age Only", 52 years.
- **Visits ACC-2026-00030…00038:**
  - ACC-30: CREA, cash 80, RCT-2026-00038; CREA 1.0 verified.
  - ACC-31: RENAL; CREA 2.0 with Δ acknowledged, verified.
  - ACC-32, ACC-33, ACC-34: GLU, NA and HGB, unpaid; nothing entered.
  - ACC-35: CREA + K, cash 200, RCT-2026-00039; verified.
  - ACC-36: CREA 2.5 with Δ acknowledged, verified.
  - ACC-37: GLU verified; HGB cancelled; card 240 RCT-2026-00040; refund RFD-2026-00009 150 cash.
  - ACC-38: K, unpaid, by uat.reception.
- **Role** "V15 Patient Export" (`patients.view`, `patients.export`, `dashboard.view`).
- **User** `v15.export` (MAIN, no must-change).

Walk-through Laboratory:

- **Branch** V15_EAST "V15 East Branch" (Egypt / Cairo). Deactivated at the end.
- **Payer** "V15_Under Payer" (key V15_UND, Credit). Deactivated.
- **Treasury** V15_TR "V15 Underscore desk" (MAIN). Deactivated.
- **Price list** V15-PL25 (2025, copy ×1 of V11-PL26). Activated, then deactivated.
- **User** `v15_must` (V14 Reception, MAIN). Its password was changed at first sign-in.

Shared records:

- Languages switched for the Arabic passes and restored: lab.admin ar → en, uat.reception ar → en.
- No password, role or branch of an existing account was changed.
- The two new passwords are in `.local/dev-credentials.md`.

## 7. Not tested, and why

| Item | Reason |
| --- | --- |
| A-03 worklist masking for VIP | no VIP result in today's worklist window; the worklist mask was not re-checked |
| B-02 server refusal (ORG-018) | the page refuses before sending; a forced request would need a token in the page |
| Physical printing, native dialogs | captured through the print-kit iframe; see §8 for the refund receipt |
| Cancel a service with a verified result | not attempted (observation in §5) |
| Desktop / narrow switch on resize | the `narrow` state follows the width at load only in this hidden pane (tested after a reload at 1280 px) |

## 8. Environment notes

- The Browser pane was hidden. A tab that had never been displayed stayed blank after sign-in (no animation frame). Two
  fixes made the tabs usable: opening the pane once through `preview_start`, and a fresh navigation.
- `tab-13` hung on the refund: the refund receipt's automatic print opened a native print dialog in the hidden pane. The
  refund itself was recorded (RFD-2026-00009). The tab was closed and `tab-15` opened. After that, prints were captured by
  replacing the iframe's `print()`.
- The cookie session is shared by every tab of 127.0.0.1, so persona switches signed the other own tab out too.
- The two `REFRESH_REUSE_DETECTED` events of the period belong to `v15.user` (platform tenant, localhost), another
  re-tester's account. They are not part of this run.
