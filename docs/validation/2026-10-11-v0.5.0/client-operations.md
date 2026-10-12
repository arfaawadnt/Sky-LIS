# Validation 2026-10-11 · Tester A · Client portal operations (Release 1) · v0.5.0

Build under test: **v0.5.0** (commit `7a2f6ea`). Commit `db4cc55` redesigned the registration page CP-06 as a desk
console; the backend is unchanged since v0.4.1. The SPA `http://127.0.0.1:4300` proxies to the validation API
`127.0.0.1:5102`; the database is `lis` on the private PostgreSQL 5433.

- Laboratory: demo laboratory (tenant `01a12494-7ca8-75a8-873d-645d12599db7`, branch MAIN, treasury MAIN-CASH).
- Personas: `uat.reception`, `uat.cashier`, `uat.accounts`, `uat.tech`, `uat.tech.chem`, `uat.head`,
  `lab.admin.manager`.
- Tester: automated browser session in the Browser pane. It used its own tabs `tab-7` and `tab-9`, on `127.0.0.1`
  only, and closed both at the end.
- This run tested and reported only. No application code, dictionary, test, script or migration was changed.
- All data created carries the prefix **V14**.

Environment history (it affects how some verdicts read):

- **22:41–23:05 (local), dev server.** Port 4300 served the Angular dev server on the working tree while the v0.5.0
  registration redesign was being written. The page reloaded on every save of a source file (22:43, 22:46, …).
  - Run in this window: Dashboard, Registration (first pass), the visit editor, Search registrations, Price query,
    Patients (reception), Worklist, Treasury (reception) and Profile.
  - Only the registration page was changing. Every other page ran v0.4.1 code, which is identical to v0.5.0.
- **From about 23:05, pinned build.** Port 4300 serves the pinned v0.5.0 production build (`main-EJ4AJUIM.js`). The
  session cookie survived the swap.
  - Run on the pinned build: Payments, Refunds, treasury decisions, Patients (manager), Results, every persona, and
    recovery.
  - The registration page got a **full second pass**: new patient, service entry, suggestions, picker, checklist, bar,
    blocked save, discount, payment, Ctrl+S, repeat order, documents, unpaid save, visit editor, Clear and Arabic.
  - The registration verdicts below are those of the pinned build.

Method notes:

- **Input.** Clicks and input were dispatched from the page context, which runs the same Angular handlers as a user's
  click. A few steps used real pointer and keyboard input on the fronted tab (`ref` clicks, typing into the service
  entry).
- **Printing** ran without a native dialog. The page's print-kit iframe had its `print()` replaced so that the rendered
  text was captured: labels, job order, receipts, invoice, refund receipts, quote, worklist and report.
- **Downloads** (CSV, patient document) and `window.open` (the document *View* action) were intercepted in the page.
  Nothing was written to disk.
- **Forced requests** were not executed: no bearer token was placed in the page.
- Every save was followed by a read-only query as `lis_owner` (§4).

## 1. Summary

| Cases | Executed | Passed | Failed | Blocked | Not tested |
| --- | --- | --- | --- | --- | --- |
| 152 | 146 | 139 | 7 | 1 | 5 |

"Executed" means passed + failed. Some S4 defects were found inside passing cases; their verdict reads
"PASS (A-nn)".

Defects: **0 × S1, 1 × S2, 3 × S3, 7 × S4** (§5).

Outbox: the run raised 94 events of 26 types (22:44–23:26). All were processed and none was dead-lettered. At the
end, `ops.outbox_events` holds no unprocessed or dead-lettered row for the tenant. The window wrote audit-trail
sequences 375–468.

Headline: the daily client workflows work end to end on v0.5.0.

- They cover registration, the visit editor, search, documents, cancellation, payment, void, refund, result entry,
  review, undo, verification, printing, amendment, treasury access and the session rules.
- Every save landed in the right tables, with the right tenant, status, numbers, audit trail, field changes and
  processed outbox events.
- All 15 defects of the v0.3.8 run stay fixed, and so do the 5 findings of the re-test rounds (R3-01…R3-05).

The new desk-console registration page meets the CP-06 rules: BR-01, BR-02, BR-05, BR-12, BR-26, the discount cap and
reason, and the gender restriction. Its checklist, sticky bar, suggestions and picker keys work.

The main findings:

- **A-01 (S2): a saved registration comes back as a new draft.**
  1. After a save, the clerk chooses "Open the visit" or "Collect on the Payments page". The saved draft stays in the
     registration page.
  2. The next visit of Registration (sidebar, or "New registration" in the visit editor) shows the same patient,
     services, discount, card amount and transaction reference, marked "Ready to save".
  3. One Ctrl+S would register a duplicate visit and a duplicate payment.

  The cause predates v0.5.0.
- **A-02 (S3): the delta check is skipped when the order of entry and verification changes.** The delta check (BR-11)
  is computed only when a value is entered. Here the previous result was verified after the new value had been
  entered, and the 66.7 % change verified without a delta flag.
- **A-03 (S3): the VIP mask leaks.** A VIP patient is masked in Search registrations, but the same user sees the full
  name, mobile and national ID in the Patients list.
- **A-04 (S3): two tabs can sign each other out.** When two tabs of the same browser refresh at the same moment, they
  trip the refresh-token reuse detection, and the user is signed out everywhere.

## 2. Coverage (page × function → cases)

| Page | Functions | Cases |
| --- | --- | --- |
| Dashboard CP-05 | tiles (vs SQL), day picker, 7-day chart, quick actions, delayed table with due time, recent activity, Arabic, future day | DSH-01…08 |
| Registration CP-06 (v0.5.0 desk console) | new patient (required, title→gender, age↔DOB, mobile country, similar name), picker search, contract default, doctor, flags as toggles, service entry by code / shortcut / suggestions (↑↓ Enter Esc) / F2 picker (↑↓), duplicate, gender rule, Reserved, preparation, discount fold / reason / cap, payment methods / reference / overpay / partial / unpaid, checklist, sticky bar, jump to field, blocked save marks and focuses, Ctrl+S, repeat order, saved dialog and documents, Clear, state after save, Arabic | REG-01…30 |
| Visit editor | open, release, add, details, discount amount, read-only view, summary with refunds | EDT-01…07 |
| Search registrations CP-10 | classifier, dates, payer, status, branch, paging, CSV, samples dialog, documents, payments, results, edit, cancel (reception, manager), VIP masking, row actions per persona, Arabic | SRC-01…16 |
| Job order | page and print | JOB-01 |
| Price query CP-12 | quote, contract switch, gender, print, continue, nothing saved | PRC-01…06 |
| Patients CP-11 | list with visit columns, search, filters, edit, file / visits, documents with / without `files.view`, new visit, VIP, upload / download / view, Arabic | PAT-01…09 |
| Worklist CP-18 | counters, filters, name search, colours, CSV / print, row action, user change, age column, Arabic | WRK-01…09 |
| Results by accession CP-19 | F8 query, numeric / invalid, flags, panic, formula, list, text, comment library, note, history, review, undo, verify, delta, preview, BR-09, print, amend, rights per persona, first load, Arabic | RES-01…21 |
| Payments CP-23 | partial, validations, methods, void, settle, auto-print off, reprint (also voided), invoice, register filters + CSV, accounts persona, manager without treasury, Arabic | PAY-01…11 |
| Refunds (action-only) | link, limits, reason, partial refunds, receipt, form reset, nothing refundable, CSV | RFD-01…06 |
| Treasury access CP-29 / Treasuries | request, duplicate, cancel own, reject, approve, revoke, SoD, filters, cashier view, list branch + export, Arabic | TRS-01…11 |
| Profile CP-65 | details (no tenant id), language save, password checklist, wrong current password, change and restore | PRF-01…05 |
| Session | branch choice, sign-out broadcast, recovery to the code step, refused URL notice and landing, return URL, two-tab refresh | SES-01…06 |
| Permissions | reception, cashier, technician (+ chemistry), unit head, accounts, forced requests | PRM-01…06 |

## 3. Test cases

Verdicts: PASS, FAIL (defect id), BLOCKED, NT. "DB-nn" points to §4. "(dev)" marks a step run on the working tree
before the build was pinned. The page code was then identical to v0.5.0, except Registration, which was re-run on the
pinned build.

| Id | Steps (persona) | Expected (source) | Observed | Verdict |
| --- | --- | --- | --- | --- |
| DSH-01 | Dashboard today (reception) (dev) | branch-day figures (CP-05) | Registrations 6, Collected EGP 800.00, Pending 2, Backlog 2, Delayed 14. The `BranchDashboardReader` SQL re-run gives 6 / 1,140 − 340 / 2 / 2 / 14 | PASS · DB-13 |
| DSH-02 | Day 2026-10-10 | figures of that day | 16 / EGP 2,436.00 / 14 / 2 / 0, equal to SQL; captions read "on 10 Oct 2026" | PASS · DB-13 |
| DSH-03 | 7-day chart | 7 local days | windows 5…11 Oct and 4…10 Oct, weekday labels right | PASS |
| DSH-04 | Quick actions | permitted pages | reception: New registration, Search registrations, Results by accession, Worklist, Payments, Price query. Head and accounts have no dashboard and land on Worklist / Search registrations | PASS |
| DSH-05 | Recent activity | audit-backed | reception: "The audit log permission is needed…". Manager: readable rows ("Treasury access requested · UAT Cashier", "Payment voided") | PASS |
| DSH-06 | Delayed tests table | BR-13 rows with due time | 14 rows, equal to the tile; columns Accession, Patient, Test, **Due**, Delay, Status (R3-03 fixed) | PASS |
| DSH-07 | Arabic | rtl, Arabic, one digit set | rtl, Arabic labels and digits, no raw key | PASS |
| DSH-08 | Day 2026-10-20 (future), then clear the field | future refused or capped; empty → today | 20 Oct accepted (no `max`) and shown with "Delayed 14" as of now; clearing the field keeps 20 Oct | FAIL A-10 |
| REG-01 | New patient → Create with an empty form | required fields refused | "This field is required." (and "Enter a date of birth or an age." on the dev pass); nothing sent | PASS |
| REG-02 | Title Mrs / Mr / Miss | gender proposed | Female / Male / Female | PASS |
| REG-03 | Years 35 → DOB; DOB 1980-02-15 → age (dev) | age ↔ DOB | 1991-10-11; 46 y 7 m 26 d | PASS |
| REG-04 | Mobile under Egypt (10) / Any (dev) | country length | under Egypt, 01011140001 refused, 1011140001 accepted, 12-34 refused; under Any, 11 digits accepted | PASS (open point 10 stands) |
| REG-05 | Create V14 Walk-in One (dev) and V14 Walk-in Three (pinned) | file and number | PAT-2026-00009 (all fields), PAT-2026-00011 (Miss, 25 y) | PASS · DB-01 |
| REG-06 | Type "v11 walk-in one" in New patient | similar-name warning (BR-01) | "Similar patient files exist…" with PAT-2026-00006 "Similar name" and `Use this file` | PASS |
| REG-07 | Picker: search PAT-2026-00009, then type "V14 Walk-in Two" | only matches of the current text | the list is right after ~0.3 s. During the debounce the previous term's result stays clickable; a fast click picked PAT-2026-00009 | PASS (A-09) |
| REG-08 | Entry K / GLU + Enter | priced from the contract | K 120, GLU 90, tube C Serum, expected +24 h | PASS |
| REG-09 | Entry "4" (dev) | shortcut number | HGB added | PASS |
| REG-10 | Type "gl", "re", ↓, Esc, then "ren" ↓ Enter (pinned) | suggestions while typing (db4cc55) | "gl" → GLU, HGB; "re" → CREA, RENAL; ↓ selects (`aria-activedescendant`); Esc closes; "ren" ↓ Enter adds RENAL; one character suggests nothing | PASS |
| REG-11 | F2 with an empty box, ↓ ↓, pick (pinned) | browse + keys | the catalogue is listed at once; ↓ focuses K, then NA; NA added | PASS |
| REG-12 | GLU twice | duplicate refused | "Already on this visit: GLU." | PASS |
| REG-13 | HCG for a male patient | REG-015 | line "HCG: The service is restricted to the other gender.", an open checklist item, `Remove HCG` | PASS |
| REG-14 | Reserved on NA (pinned) and HGB (dev) | BR-05 | billed, no tube, no result row | PASS · DB-02 |
| REG-15 | Fasting hours? 8 h / 12 h | stored, printed | stored, and printed on the job order | PASS · DB-02 |
| REG-16 | Discount 15 % without a reason, then 5 % + Management approval | reason mandatory, cap 10 % | 15 %: "Choose the discount reason." and "The discount exceeds your limit of 10 %.". 5 %: 18.00, spread 7.50 / 4.50 / 6.00 | PASS · DB-02 |
| REG-17 | Card without a reference; 500 > due | BR-26 | "Enter the transaction reference." / "The amount paid cannot exceed the amount due." | PASS |
| REG-18 | Card 100 ref V14-CARD-050, Ctrl+S (pinned); card 200 (dev) | visit + payment | ACC-2026-00026, "1 tube(s)", "100.00 received — receipt RCT-2026-00037"; ACC-2026-00023 / RCT-2026-00031 | PASS · DB-02/03 |
| REG-19 | Saved dialog: Job order, Sample labels, Receipt | documents | job order (Biohazard, lines, tube, preparation, balance), label, receipt (Card · reference, remaining); bilingual letterhead | PASS |
| REG-20 | V14 Walk-in Three: HCG + BLDGRP, paid 0 (pinned) | unpaid save | ACC-2026-00027, "2 tube(s)", "Collect on the Payments page", no payment, no automatic print | PASS · DB-04 |
| REG-21 | K + treatment no.; Clear | form reset | lines, texts, flags and discount empty | PASS |
| REG-22 | RENAL for a patient whose CREA was ordered (pinned) | BR-12 | bar notice "RENAL repeated within its interval — you will confirm on save"; "Repeat order" dialog → `Register anyway` | PASS (A-11) · DB-02 |
| REG-23 | RENAL profile | members | CREA (Numeric) + EGFR (Formula) | PASS |
| REG-24 | Tubes | BR-02 | one C Serum tube for RENAL / GLU / K / NA; H EDTA separate | PASS |
| REG-25 | Checklist "Ready to save" | one line per requirement; open lines jump | "1 of 3 done" → "5 of 5 done"; open lines are buttons; "Add at least one service." focuses `#reg-service-entry` | PASS |
| REG-26 | Save with the patient and services missing | all missing fields marked, the first focused | toast "2 items need attention…"; the patient row is marked `invalid` and focused; the services card is not marked | PASS (A-08) |
| REG-27 | Flags Urgent / Biohazard | toggles | `aria-pressed` false → true; saved and printed | PASS |
| REG-28 | Discount fold | folded until needed | folded at start; opens on a click or on a discount blocker | PASS |
| REG-29 | Save → `Open the visit` (or `Collect on the Payments page`) → `New registration` or sidebar Registration | an empty registration | the saved draft returns: same patient, lines, treatment no., Biohazard, discount 5 % + reason, Card 100 with reference V14-CARD-050, "5 of 5 done" | FAIL A-01 |
| REG-30 | Arabic (pinned) | rtl, Arabic, one digit set | rtl, Arabic labels, amounts in Arabic-Indic digits. The checklist ("اكتمل 1 من 3"), the cap ("حدّك 10٪") and the bar ("مطلوب استكمال 2") use Latin digits | PASS (A-07) |
| EDT-01 | Open the visit | editor | "Visit ACC-2026-00026", lines with tubes, the edit checklist | PASS |
| EDT-02 | Release NA / HGB | tube issued | "NA was released: the tube is issued and the results are expected."; `released_at_utc` set | PASS · DB-05 |
| EDT-03 | Add K / CREA | joins the C tube | on SMP-2026-00033 / -00028 | PASS · DB-05 |
| EDT-04 | Urgent, doctor DR01, insurance V14-INS-50; (dev) Biohazard, doctor none, V14-T2 | saved and audited | "The changes were saved."; field changes with before / after | PASS · DB-05 |
| EDT-05 | Discount Amount 30 (dev) | redistributed | 113.57 / 85.18 / 141.96 / 113.57 / 75.72 = 530.00 | PASS · DB-05 |
| EDT-06 | Cashier opens `/registration/visits/<id>` | read only | "You can view this visit but not change it."; every control `:disabled`, no Save (R3-02 fixed) | PASS |
| EDT-07 | ACC-2026-00020 (refunded) | the summary nets refunds | Paid 210.00, Refunded −90.00, Remaining 0.00 (R3-01 fixed) | PASS |
| SRC-01 | ACC-2026-00023, "23", PAT-2026-00010, "walk-in two", 01011140002, "V14" | classifier | 1 / 1 / 2 / 2 / 2 / 3 visits, the right ones | PASS |
| SRC-02 | All dates; "mona hassan" | date range | 25 visits; 7 for Mona Hassan (equal to the DB) | PASS · DB-13 |
| SRC-03 | Payer AXA; status Completed / Open | filters | 0 / 0 / 25 (DB: 25 Open, all Cash) | PASS · DB-13 |
| SRC-04 | This branch only off | branch filter | 25 | PASS |
| SRC-05 | Page size 20 | paging | "Showing 1–20 of 25"; the next page shows 21–25 | PASS |
| SRC-06 | Export CSV | file | `registrations-2026-10-11.csv`, local dates, toast | PASS |
| SRC-07 | Samples dialog, Print all labels | tubes, labels | 2 tubes Received; labels without the cancelled HCG | PASS |
| SRC-08 | Documents dialog | 5 entries | labels, job-order print, job-order page, receipts link, results report | PASS |
| SRC-09 | Payments action | Payments of the visit | `/billing/payments?visitId=…` | PASS |
| SRC-10 | Results action | Results by accession | `/results/by-accession?accession=…` | PASS |
| SRC-11 | Edit action | editor | opens | PASS |
| SRC-12 | Reception cancels HCG on ACC-2026-00024 | reason mandatory | the button stays disabled until a reason is chosen; "The services were cancelled."; struck-through chip | PASS · DB-06 |
| SRC-13 | Manager cancels BLDGRP on the paid ACC-2026-00024 | negative balance | Payments shows "Refund due", Remaining −60.00 and `Refund the balance` | PASS · DB-06 |
| SRC-14 | VIP PAT-2026-00010 as reception: Search registrations, then Patients | VIP masked for users without the right (REG Q12) | Search registrations: "VIP patient". Patients list: full name, mobile and national ID | FAIL A-03 |
| SRC-15 | Arabic | rtl | rtl, Arabic, no raw key | PASS |
| SRC-16 | Row actions for cashier and accounts | no edit / cancel | Payments and Documents only; accounts has no Export | PASS |
| JOB-01 | Job-order page | CP-06 job order | header, Urgent + Biohazard, 5 lines, 2 tubes, preparation, balance; Print | PASS |
| PRC-01 | Quote K, GLU, RENAL | prices | 120 / 90 / 150, total 360, preparation "Fasting hours?" | PASS |
| PRC-02 | Contract AXA Gold | contract prices | "Not priced under this contract" | PASS |
| PRC-03 | Gender Male + HCG | refused, with the reason | the line reads the gender reason (A-11 of v0.3.8 fixed) | PASS |
| PRC-04 | Print quote | document | quote, total, "for information only" | PASS |
| PRC-05 | Continue to registration | services carried over | `/registration/new?services=…`, 3 lines, Cash | PASS |
| PRC-06 | Nothing saved | no visit | visit count 25 before and after | PASS |
| PAT-01 | List (reception, manager) | CP-11 columns | Visits and Last visit present; VIP column for the manager only | PASS |
| PAT-02 | Search by no. / name / mobile / NID / Arabic; gender, status | search | all correct | PASS |
| PAT-03 | Edit PAT-2026-00009: bad mobile / e-mail refused, then saved | saved and audited | "Patient file PAT-2026-00009 was saved." | PASS · DB-07 |
| PAT-04 | File, Visits | history | identity, contact, record; ACC-2026-00023 | PASS |
| PAT-05 | Documents as reception | hidden without `files.view` | tabs File / Visits only | PASS |
| PAT-06 | New visit from the file | patient preselected | `/registration?patientId=…` | PASS |
| PAT-07 | VIP (manager) | only with `viewConfidential` | "VIP patient (confidential)", VIP column | PASS · DB-07 |
| PAT-08 | Upload a .txt, then a PNG + note; Download; View (manager) | PDF / JPEG / PNG only | "Only PDF, JPEG or PNG files can be attached."; "V14-referral.png was attached."; download and view return the same 70 bytes | PASS · DB-07 |
| PAT-09 | Arabic | rtl | rtl, Arabic | PASS |
| WRK-01 | Default view (reception, tech) | counters | 9 / 24 / 14 / 0 (reception) and 9 / 23 / 13 / 0 (tech), equal to the rows | PASS |
| WRK-02 | Group Haematology | filter | only Haemoglobin / Blood group rows | PASS |
| WRK-03 | Search by name / accession / sample / patient no. / Arabic | search | 11 / 5 / 1 / 6 / 5 rows | PASS |
| WRK-04 | Delay colours | BR-13 | every row of the window was at 0–2 %; colours not checked | NT |
| WRK-05 | CSV (tech), Print (reception) | file, document | `worklist-2026-10-11-2026-10-11.csv` + toast; reception has no Export (no `results.export`); worklist printed | PASS |
| WRK-06 | Row action `Results` | opens the case | `/results/by-accession?accession=ACC-2026-00024` | PASS |
| WRK-07 | Sign out tech, sign in tech.chem in the same tab | own rights only | 19 tests, all Clinical Chemistry, no Haematology row (A-01 of v0.3.8 fixed) | PASS |
| WRK-08 | Age / sex of patients registered by age | age shown | "F" only for PAT-2026-00010 (30 y) and PAT-2026-00007; the age appears only with a date of birth | FAIL A-05 |
| WRK-09 | Arabic | rtl | rtl, Arabic | PASS |
| RES-01 | Query ACC-2026-00023 with F8; open from the list | CP-19 | "Matching visits (1)"; the row opens the visit | PASS |
| RES-02 | K 6.2, NA "abc", GLU 250, CREA 1.2, Save; then NA 140 | values, flags | "3 results saved." / "1 results were refused…"; K H, GLU H; NA saved afterwards | PASS · DB-09 |
| RES-03 | HGB 5.0 | panic | PANIC badge, LL, `flag-panic`, `is_panic` | PASS · DB-09 |
| RES-04 | CREA 1.2 / 2.0 in RENAL | formula | eGFR 142 / 79, computed on save | PASS (observation) |
| RES-05 | Restricted list | BR-19 | not run: the BLDGRP lines of this run were cancelled or not entered | NT |
| RES-06 | Text result | text entry | the demo catalogue has no text test | NT |
| RES-07 | Comment from the library (HAEM) + internal note | the comment prints, the note never | both saved; the preview shows the comment, not the note | PASS · DB-09 |
| RES-08 | History | logs | result log 3, status log 2, previous 0 | PASS |
| RES-09 | Review (tech) | Complete → Reviewed | "4 results reviewed." | PASS |
| RES-10 | Undo (head) | reason required | "A reason is required.", then "4 results undone." | PASS · DB-09 |
| RES-11 | Verify CHEM and HEMA (head) | signature | "4 results verified (electronic signature)." / "1 …" | PASS |
| RES-12 | CREA 1.2 (ACC-24) and 2.0 (ACC-25) entered; ACC-24 verified, then ACC-25 | BR-11: a delta flag to acknowledge | ACC-25 verified at once: no Δ, no dialog; `delta_flag` false, no previous result | FAIL A-02 |
| RES-13 | Preview unverified results (tech) | watermark | "PREVIEW — NOT A REPORT", "PRELIMINARY — NOT YET VERIFIED"; comment shown, note hidden | PASS |
| RES-14 | Print unverified results (tech) | BR-09 | "Only verified results print (BR-09)." | PASS |
| RES-15 | Print verified CHEM (head) | printed | "…marked printed and sent to the printer."; tab Printed; report with the signature line | PASS · DB-09 |
| RES-16 | Amend K 6.2 → 5.8 (head) | reason, back to Complete | "This field is required.", then "The result was amended…"; re-verified | PASS · DB-09 |
| RES-17 | Technician's buttons | no Verify / Undo / Amend | Save, Review, Preview, Print report, comment, note | PASS |
| RES-18 | Reception on a case | read only | Preview and History only; no comment, note or Save | PASS |
| RES-19 | tech.chem on ACC-2026-00023 after tech | CHEM only | "1 report units are outside your result rights"; one tab | PASS |
| RES-20 | First visit of Results in a page load (reception, pinned build) | translated status | the status region shows `results.loading` before "Loading…" | FAIL A-06 |
| RES-21 | Arabic | rtl | rtl, Arabic | PASS |
| PAY-01 | ACC-2026-00025: cash 100 (cashier) | partial payment | RCT-2026-00033; the form resets to amount 170, Cash, no reference | PASS · DB-08 |
| PAY-02 | Amounts 500, 0, 12.345, empty; card without a reference | BR-26 | "at most 270", "at least 0.01", "wrong format", "required"; reference required | PASS |
| PAY-03 | Card 60 V14-SLIP-1, Transfer 50 V14-TRF-1 | methods | RCT-2026-00034 / -00035 | PASS · DB-08 |
| PAY-04 | Void RCT-2026-00035 | reason mandatory | "A reason is required.", then "Receipt RCT-2026-00035 voided."; remaining 110 | PASS · DB-08 |
| PAY-05 | POS 110 V14-POS-1 | settles the visit | RCT-2026-00036, Paid | PASS · DB-08 |
| PAY-06 | `autoPrintReceipt` false; Reprint | no automatic print | no print job after the 5 payments; Reprint prints; the voided receipt reprints with "VOID · V14 wrong method" | PASS |
| PAY-07 | Print invoice | invoice | services, payments, refunds, balance | PASS |
| PAY-08 | Register: Voided, POS, accession; CSV | register | correct rows; `payments-2026-10-11.csv` with local `Paid at (Asia/Riyadh)` and a toast | PASS |
| PAY-09 | Accounts on Payments | view and print only | no Take a payment, no Void; Reprint and Print invoice offered | PASS |
| PAY-10 | Manager without an approved treasury | guidance | "No treasury is approved for you on this device. Request treasury access" | PASS |
| PAY-11 | Arabic | rtl | rtl, Arabic | PASS |
| RFD-01 | `Refund the balance` on ACC-2026-00024 (cashier) | refund page | `/billing/refunds?visitId=…`, Refundable 60.00, form prefilled | PASS |
| RFD-02 | Amount 300; no reason | BR-14 | "at most 60"; reason required | PASS |
| RFD-03 | 40 Card V14-RFD-CARD + note; then 20 Cash | partial refunds | RFD-2026-00007 / -00008; signature receipts | PASS · DB-08 |
| RFD-04 | Form after the first refund | a fresh form | amount 20, Cash; reference, reason and note empty; RFD-8 stored without a note (A-06 of v0.3.8 fixed) | PASS · DB-08 |
| RFD-05 | After the full refund | nothing refundable | "Nothing is refundable on this visit." | PASS |
| RFD-06 | Refunds CSV | file | `refunds-2026-10-11.csv`, local times, Arabic reason, toast | PASS |
| TRS-01 | Request without a treasury, then NORTH-CASH + label (reception) | Pending | "This field is required."; TRQ-2026-00012 Pending | PASS · DB-10 |
| TRS-02 | The same request again | TRS-012 | "A pending or approved request already exists for this treasury on this device." | PASS |
| TRS-03 | Cancel own request | Cancelled | TRQ-2026-00015 Cancelled (reason optional) | PASS · DB-10 |
| TRS-04 | Manager rejects TRQ-12 | reason mandatory | "A reason is required.", then "Request rejected." | PASS · DB-10 |
| TRS-05 | Manager approves TRQ-13 (cashier) | Approved | "Request approved." | PASS · DB-10 |
| TRS-06 | Revoke TRQ-13 | reason mandatory | "Access revoked." | PASS · DB-10 |
| TRS-07 | Manager's own TRQ-14 | SoD | only `Cancel request` offered; cancelled | PASS |
| TRS-08 | Filters Rejected / My requests only | filters | 12, 8, 3 / 14, 10 | PASS |
| TRS-09 | Cashier on other users' rows | no decisions | none offered | PASS |
| TRS-10 | Treasuries (reception) | `CODE · name`, no Export | `MAIN · Main Branch`, `NORTH · North Branch`; Refresh only | PASS |
| TRS-11 | Arabic | rtl | rtl, Arabic | PASS |
| PRF-01 | Profile details | no technical ids | account block; the laboratory block reads "details are not available yet" without the tenant id (R3-04 fixed) | PASS |
| PRF-02 | Save Arabic, then English (reception) | stored; translated toast | "تم حفظ اللغة." / "Your language was saved."; `preferred_language` en at the end | PASS · DB-12 |
| PRF-03 | Checklist: short, contains the username, same as current, mismatch (tech.chem) | live rules | each rule unticks; a mismatch is not sent | PASS |
| PRF-04 | Wrong current password | refused | "The current password is not correct." | PASS |
| PRF-05 | Change and restore | changed | not repeated this run (passed on v0.3.8) | NT |
| SES-01 | Branch selection | CP-04 | every persona has one branch; the assignments may not be changed | BLOCKED |
| SES-02 | Sign out in tab-7 with tab-9 open | the session ends and the other tab follows | tab-7 and tab-9 both on `/login` (no return URL); `SESSION_REVOKED` | PASS · DB-11 |
| SES-03 | Recovery: empty field, uat.reception, code "12ab", Back | up to the code step | "This field is required."; neutral text, 6-digit code step, resend timer; "The value has the wrong format."; Back returns | PASS (completion: no gateway) |
| SES-04 | Refused URLs (reception, head) | notice + permitted landing | "This page is not available for your account." each time; landing on Dashboard / Worklist | PASS |
| SES-05 | Full load of `/billing/payments?visitId=…` while signed out, then sign in | return URL | lands on the payments of the visit | PASS |
| SES-06 | Two tabs of 127.0.0.1 reloaded together (dev-server reload at 22:43:11) | both tabs keep the session | 3 × `REFRESH_REUSE_DETECTED`; the whole family is revoked (`ReuseDetected`); both tabs on `/login` | FAIL A-04 |
| PRM-01 | Reception | R1 nav | reception + laboratory (view) + finance + catalogue + profile; no Setup | PASS |
| PRM-02 | Cashier | no registration | nav without Registration, Patients or Worklist; rows offer Payments + Documents | PASS |
| PRM-03 | Technician / chemistry | results only | Dashboard, Worklist, Results, catalogue, Profile | PASS |
| PRM-04 | Unit head | results, no dashboard | lands on Worklist; billing, registration and patients refused with the notice | PASS |
| PRM-05 | Accounts | search, finance | lands on Search registrations; no Export, no edit | PASS |
| PRM-06 | Forced requests | AUTHZ codes | not executed (no token in the page) | NT |

## 4. Database verification

All queries ran as `lis_owner` (read only). Every row listed carries tenant `01a12494-7ca8-75a8-873d-645d12599db7`.

| Ref | Case | Table(s) | Query (abridged) | Result |
| --- | --- | --- | --- | --- |
| DB-01 | REG-05 | app.patients, audit.*, ops.outbox_events | `select tenant_id, number, title, full_name, name_ar, gender, date_of_birth, age_years, mobile, national_id, nationality, email, governorate_id, city, address, comments … where number in ('PAT-2026-00009','PAT-2026-00011')` | PAT-9: Mr · V14 Walk-in One · في14 زائر واحد · Male · 1980-02-15 · 01011140001 · 28002150100141 · EG · e-mail · Giza · Dokki · address · comment. PAT-11: Miss · V14 Walk-in Three · Female · 25 y. Trail `PatientRegistered` (seq 375); field change Created; outbox processed |
| DB-02 | REG-14…18, 22 | app.visits, visit_lines, visit_tubes, samples, results, visit_preparation_answers | `… where accession in ('ACC-2026-00023','ACC-2026-00026')` | ACC-26: Open, V14-T50, Biohazard, 360 − 18 (Percent 5, "Management approval") = 342; RENAL 142.50 / GLU 85.50 Ordered with a tube, NA 114.00 **Reserved, no tube**; answer "8 h"; results GLU, CREA, EGFR Ordered. ACC-23: urgent, V14-T1, DR01, 10 % "Staff and relatives"; HGB Reserved without a tube; SMP-2026-00028 Received (3 services); answer "12 h" |
| DB-03 | REG-18 | app.payments, visit_accounts, audit_trail, outbox | `… where visit_id in (ACC-23, ACC-26)` | RCT-2026-00031 200 Card V14-CARD-001; RCT-2026-00037 100 Card V14-CARD-050; both Posted, MAIN-CASH, uat.reception, device a0ab790c, branch MAIN. Trail `VisitRegistered`, `VisitDiscountChanged`, `PaymentRecorded`, `SampleCollected`, `SampleReceived`; outbox 5 / 5 processed |
| DB-04 | REG-20 | visits, payments, visit_accounts, visit_lines, visit_tubes | `… accession in ('ACC-2026-00024','ACC-2026-00027')` | ACC-27: 310.00, no payment, no account row, HCG / BLDGRP Ordered, 2 tubes. ACC-24: RENAL, BLDGRP, HCG on SMP-2026-00029 / -00030 |
| DB-05 | EDT-02…05 | visits, visit_lines, field_changes, audit_trail | `… id in (ACC-23, ACC-26)` | ACC-23: revision 5, V14-T2, doctor null, Biohazard, 560 − 30 (Amount) = 530; HGB `released_at_utc` on SMP-2026-00032; CREA on SMP-2026-00028. Field changes: Revision, totals, Biohazard / ReferringDoctorId / TreatmentNo, DiscountKind Percent → Amount with the reason. Trail `VisitLinesAdded` ×2, `VisitDiscountChanged`. ACC-26: revision 4; Urgent false → true, ReferringDoctorId null → DR01, InsuranceId null → V14-INS-50; NA released, K added on SMP-2026-00033 |
| DB-06 | SRC-12/13 | visit_lines, results, samples, audit_trail, outbox | `… visit_id = ACC-24` | HCG Cancelled "Patient declined — V14 cancel test" (by / at set); result Cancelled with the same reason. BLDGRP Cancelled "Ordered by mistake"; sample SMP-2026-00030 cancelled (`SampleCancelled`). Share 150. Trail `VisitLineCancelled` ×2, `ResultCancelled` ×2; outbox processed |
| DB-07 | PAT-03/07/08 | patients, patient_documents, field_changes | `… id in (PAT-9, PAT-10)` | PAT-9: mobile 01011140009, e-mail, comment "V14 edited comment", modified by UAT Reception Clerk; field changes Comments / Email / Mobile before → after; trail `PatientUpdated`. PAT-10: `is_vip` true (field change IsVip false → true by Shift Manager). Document V14-referral.png, image/png, 70 B, sha256 6b7fa434f92a…, note, active, storage key `<tenant>/patients/<patient>/<doc>`; `PatientDocumentAttached` processed |
| DB-08 | PAY / RFD | payments, refunds, visit_accounts, audit_trail, outbox | `… receipt_number >= 'RCT-2026-00031'`, `… number >= 'RFD-2026-00007'` | RCT-32 210 Cash (ACC-24). For ACC-25: RCT-33 100 Cash; RCT-34 60 Card V14-SLIP-1; RCT-35 50 Transfer V14-TRF-1 **Voided** "V14 wrong method" (voided by set); RCT-36 110 Pos V14-POS-1. All on MAIN-CASH, by uat.cashier, device a0ab790c. Accounts: ACC-24 paid 210 / refunded 60; ACC-25 paid 270. RFD-2026-00007 40 Card V14-RFD-CARD, note "V14 first refund note"; RFD-2026-00008 20 Cash, reference and note **empty**. Trail `PaymentRecorded` ×5, `PaymentVoided`, `RefundRecorded` ×2; outbox processed |
| DB-09 | RES | results, result_log_entries, audit_trail | `… accession in ('ACC-2026-00023','-00024','-00025')` | ACC-23: K 5.8 High Verified, print 1, amendment 1, HAEM comment, internal note; GLU 250 High, NA 140, CREA 1.20 Printed (copy 1); HGB 5.0 CriticalLow `is_panic` Verified; all verified by UAT Unit Head. ACC-24: CREA 1.2 High and EGFR 142 Verified; BLDGRP / HCG Cancelled. ACC-25: CREA 2.0 High, EGFR 79, K 4.5 Verified, **`delta_flag` false, no previous result** (A-02). K log 1–12: Ordered; value — → 6.2 (tech); Ordered → Complete; Comment; Note; Complete → Reviewed; Reviewed → Complete "V14 undo review test"; Verified; Printed "copy 1"; value 6.2 → 5.8 "V14 transcription error"; Printed → Complete; Verified. Trail `ResultEntered` ×11, `ResultPanicDetected`, `ResultReviewed` ×4, `ResultUndone` ×4, `ResultVerified` ×11, `ResultPrinted` ×4, `ResultAmended` |
| DB-10 | TRS | app.treasury_access_requests, audit_trail | `… number between 'TRQ-2026-00012' and 'TRQ-2026-00015'` | 12 Rejected "V14 wrong branch for this user"; 13 Approved (approved by set), then Revoked "V14 revoke test"; 14 Cancelled (manager); 15 Cancelled (reception). All NORTH-CASH, device a0ab790c. Trail Requested ×4, Rejected, Approved, Revoked, Cancelled ×2; outbox processed |
| DB-11 | SES-02/03/06 | identity.refresh_sessions, audit.security_events | sessions of uat.reception since 22:40; events since 22:40 | The 22:42:29 family rotated at 22:42:48 and 22:43:11.731; then all three rows were `is_revoked` with `ReuseDetected` at 22:43:11.734. `REFRESH_REUSE_DETECTED` ×3 (22:43:11.78 / .82 / 12.02). Later families rotate normally. `SESSION_REVOKED` on each sign-out; `PASSWORD_RECOVERY_STARTED`; `PASSWORD_CHANGE_FAILED` (tech.chem) |
| DB-12 | PRF-02 | app.tenant_memberships | `preferred_language` of uat.reception and uat.tech.chem | both `en` at the end (reception went ar → en twice: profile and topbar switch) |
| DB-13 | DSH-01/02, SRC-02/03 | visits, payments, refunds, results | the `BranchDashboardReader` statements with the day bounds; `group by status / payer` | today 6 / 1,140.00 − 340.00 / 2 / 2 / 14; 10 Oct 16 / 2,436.00 / 14 / 2; 25 visits, all Open and Cash; 7 visits of Mona Hassan |
| DB-14 | all | ops.outbox_events | `select count(distinct type), count(*), count(processed_at_utc), count(dead_lettered_at_utc) … occurred_at_utc > '2026-10-11 22:44+03'` | 26 types, 94 events, 94 processed, 0 dead letters; at the end, no unprocessed row for the tenant |

## 5. Defect log

| Id | Sev | Case | Steps | Expected | Observed | Evidence | Suspected root cause |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A-01 | **S2** (duplicate visit and payment risk) | REG-29 | 1. Reception registers PAT-2026-00009 with RENAL, GLU, NA (Reserved), discount 5 % and Card 100 ref V14-CARD-050 → ACC-2026-00026. 2. In the saved dialog, choose `Open the visit` (or, after an unpaid save, `Collect on the Payments page`). 3. Click `New registration` in the editor, or Registration in the sidebar | an empty registration form | The saved draft comes back: same patient, lines, treatment no. V14-T50, Biohazard, discount 5 % + reason, Card 100 with reference V14-CARD-050, and the checklist reads "5 of 5 done". The page keeps it until `Clear`. Saving would create a second visit and a second payment with the same reference. Only the BR-12 repeat dialog stands in the way, and only when a repeat rule applies | REG-29 transcript (both paths). The draft was cleared with `Clear` and not saved | `features/registration/pages/registration.page.ts`: `openVisit()` and `goToPayments()` close the dialog and navigate without `facade.reset()`, unlike `newRegistration()`. `RegistrationFacade` is provided at route level and outlives the page. The page calls `facade.open(id)` only when the route's visit id differs from the facade's, and for `/registration/new` both are null. Not a v0.5.0 regression: v0.4.1 has the same code |
| A-02 | S3 (clinical check bypassed) | RES-12 | 1. The technician enters CREA 1.2 on ACC-2026-00024 and CREA 2.0 on ACC-2026-00025 (same patient). 2. The unit head verifies ACC-24, then ACC-25 | BR-11 / INV-RES-007: a 66.7 % change is flagged and must be acknowledged before verification | ACC-25 is verified at once: no Δ badge, no dialog; `delta_flag` false, `previous_result_id` null | DB-09 | `Platform.Application/Results/ResultEntryEngine.cs` l.137–139 computes the delta only when the value is entered, against `PreviousVerifiedAsync`, and the previous result was not verified yet. The verify transition (T4) does not re-run the check against results verified since |
| A-03 | S3 (confidentiality) | SRC-14 | Reception (no See-VIP right) opens Search registrations, then Patients, for VIP PAT-2026-00010 (also PAT-2026-00007) | VIP name and mobile masked for users without the right (REG intake Q12, §321) | Search registrations shows "VIP patient". The Patients list, the patient file and the registration picker show the full name, mobile and national ID. The next menu entry defeats the mask | SRC-14 transcript | The intakes disagree: `docs/intake/LIS-INT-PAT-001` Q8 hides only the flag, while `LIS-INT-REG-001` Q12 masks name and mobile. The list projection in `Platform.Application/Patients/Queries/PatientQueries.cs` applies no mask. This needs a business decision first, then one rule across CP-10, CP-11, the picker and the worklist |
| A-04 | S3 (availability) | SES-06 | Two tabs of the same host hold the session, and both reload at the same moment. Here the dev server reloaded both at 22:43:11; the same happens when a browser restores its tabs, or with "reload all" | both tabs keep the session | Both tabs refresh with the same cookie, and the second presentation is treated as reuse. The whole family is revoked and both tabs land on `/login` | DB-11 (`REFRESH_REUSE_DETECTED` ×3, `ReuseDetected`) | `core/auth/auth.store.ts`: `refresh()` is single-flight per tab only, with no cross-tab lock (Web Locks or the existing `SESSION_CHANNEL`). `Platform.Application/Identity/Commands/RefreshSessionCommand.cs` l.54–65 treats any rotated token as reuse, with no grace window for the immediate successor. Timing-dependent: two later synchronized reloads did not race |
| A-05 | S4 | WRK-08 | Worklist with patients registered by age (no date of birth) | "30y · F" | "F" only (PAT-2026-00010, PAT-2026-00007). The results header, the labels and the job order show the age | WRK-08 | `Platform.Infrastructure/Persistence/Results/ResultRepository.cs` l.225 projects `DateOfBirth` only; the worklist age ignores `age_years` / `age_recorded_on` |
| A-06 | S4 | RES-20 | First visit of Results by accession after a page load (e.g. Search registrations › Results) | translated loading text | the status region shows `results.loading` before "Loading…" (pinned build) | `__seen` capture | `features/results/pages/results-by-accession.page.html` l.98 / l.241 render `i18n.t('results.loading')` before the `results` dictionary is loaded. The billing case (v0.3.8 A-07) was fixed; this one was not |
| A-07 | S4 | REG-30 | Registration in Arabic | one digit set (A-12 of v0.3.8) | Amounts use Arabic-Indic digits. The checklist ("اكتمل 1 من 3", "الخدمات المسعّرة: 1"), the cap ("حدّك 10٪") and the bar ("مطلوب استكمال 2") use Latin digits | REG-30 transcript | `ui/registration-readiness.component.ts`, `ui/registration-action-bar.component.ts` and the cap label pass raw numbers to `i18n.tf` without the localized number format |
| A-08 | S4 | REG-26 | Save with no service | every missing field marked (db4cc55: "A blocked save marks all missing fields") | the patient row is marked; the services card and the entry are not | REG-26 | `pages/registration.page.html` sets `[class.invalid]` for patient, contract, doctor, discount and payment, but for neither the `lines` nor the `issues` blocker (`#reg-services`) |
| A-09 | S4 | REG-07 | Picker: search one patient, then type another name and click the first result within ~0.3 s | only matches of the typed text | During the debounce the previous term's results stay listed and clickable. A fast click selected PAT-2026-00009 while "V14 Walk-in Two" was in the box | snapshot series 0–1.6 s | The picker in `shared/patient` keeps the last result list while the new query is debounced. Clear or disable the list as soon as the text changes |
| A-10 | S4 | DSH-08 | Dashboard day = 2026-10-20; then clear the date | future days refused; empty → today | The future day is accepted, with figures "as of now" (Delayed 14). Clearing the field keeps 20 Oct | DSH-08 | `features/account/pages/dashboard.page.*`: no `max` on the date input and no fallback for an empty value |
| A-11 | S4 | REG-22 | Register RENAL for a patient whose **CREA** (a RENAL member) was ordered a minute earlier as a single test | the message names what repeats | "RENAL: this test was ordered for the patient on … (ACC-2026-00023)". RENAL was never ordered; the repeated test is Creatinine | REG-22 | The REG-201 alert text uses the service code for a test-level frequency hit (alert builder of the registration dry run) |

Known open points seen again (not new defects):

- D-05: treasury-access rows show no decision reason or approval comment, and the approve dialog has no comment field.
- Open point 10: the Egypt mobile rule counts 10 digits, without the trunk 0.
- Plural wording: "1 results were refused" / "1 results verified".

Observations (not counted):

- **eGFR formula.** The demo catalogue's eGFR formula is `ROUND(175 × CREA^−1.154)`, without the age and sex factors
  of MDRD. It gives 142 for a 30-year-old woman. This is catalogue data, not code, but it should be checked before
  go-live.
- **Labels.** Sample labels carry the urgent mark ("!") but no biohazard mark for a biohazard visit.
- **Failed-attempt counter.** A wrong *current* password in Profile › Change password increments
  `failed_password_attempts` (tech.chem: 1). Whether this should count toward lock-out is not decided; the security
  spec should say so.
- **Matching visits.** Results by accession reads "Matching visits (0)" while it shows a visit opened from a link.
- **Branch name.** The new-registration header shows no branch name: v0.5.0 removed the "Branch of your session"
  placeholder. The edit header shows the branch.

## 6. Previous defects (v0.3.8 run and re-test rounds)

| Old id | Subject | Status in v0.5.0 | Case |
| --- | --- | --- | --- |
| A-01 (S1) | previous user's data after sign-out / sign-in | still fixed | WRK-07, RES-19 |
| A-02 | reception comment / note dialogs | still fixed | RES-18 |
| A-03 | documents tab without `files.view` | still fixed | PAT-05 |
| A-04 | treasury branch GUID | still fixed | TRS-10 |
| A-05 | receipt auto-printed with the parameter off | still fixed | PAY-06, REG-20 |
| A-06 | refund form kept the previous note | still fixed | RFD-04 |
| A-07 | raw keys in toasts (profile, billing) | still fixed; **the same pattern now in Results** (new A-06) | PRF-02, RES-20 |
| A-08 | refused URLs opened | still fixed | SES-04, PRM-04 |
| A-09 | patients list without visit columns | still fixed | PAT-01 |
| A-10 | letterhead language | still fixed | REG-19, PAY-07 |
| A-11 | price-query line reason | still fixed | PRC-03 |
| A-12 | dashboard captions / digits | still fixed on the dashboard; **Latin digits in the new registration checklist** (new A-07) | DSH-07, REG-30 |
| A-13 | F2 empty search | still fixed (↑↓ added) | REG-11 |
| A-14 | worklist name search | still fixed | WRK-03 |
| A-15 | registrations CSV dates | still fixed | SRC-06 |
| R1-01 / R1-02 / R1-04 / R1-05 | refund link, delayed table, landing, export keys | still fixed | RFD-01, DSH-06, SES-04, TRS-10 |
| R3-01 | editor summary ignored refunds | still fixed | EDT-07 |
| R3-02 | editor editable for viewers | still fixed | EDT-06 |
| R3-03 | delayed table without due time | still fixed | DSH-06 |
| R3-04 | tenant id on the profile | still fixed | PRF-01 |
| R3-05 | export toasts and UTC file names | still fixed for payments, refunds, registrations and worklist | PAY-08, RFD-06 |

## 7. Data created (dev database `lis`, demo laboratory, invented data)

- Patients:
  - **PAT-2026-00009** "V14 Walk-in One" (mobile 01011140009, e-mail, comment).
  - **PAT-2026-00010** "V14 Walk-in Two" (VIP, document `V14-referral.png`).
  - **PAT-2026-00011** "V14 Walk-in Three".
- **ACC-2026-00023** (PAT-9):
  - Services K, GLU, HGB (released), NA and CREA; urgent, biohazard, V14-T2, discount 30 EGP; RCT-2026-00031 card 200.
  - CHEM printed (copy 1). K amended 6.2 → 5.8 and re-verified, with the HAEM comment and an internal note.
  - HGB 5.0 panic, verified. Samples SMP-2026-00028 and -00032.
- **ACC-2026-00024** (PAT-10):
  - RENAL verified (CREA 1.2, eGFR 142); HCG and BLDGRP cancelled.
  - RCT-2026-00032 cash 210; refunds RFD-2026-00007 (40 card) and -00008 (20 cash).
  - Samples SMP-2026-00029 and -00030 (cancelled).
- **ACC-2026-00025** (PAT-10):
  - RENAL + K verified; CREA 2.0 verified without a delta (A-02).
  - Payments RCT-2026-00033…-00036 (-00035 voided). Sample SMP-2026-00031.
- **ACC-2026-00026** (PAT-9, pinned build): RENAL, GLU, NA (released) and K; urgent, biohazard, DR01, V14-T50,
  V14-INS-50, 5 % discount; RCT-2026-00037 card 100. Sample SMP-2026-00033.
- **ACC-2026-00027** (PAT-11, pinned build): HCG and BLDGRP, unpaid.
- Treasury requests TRQ-2026-00012 (Rejected), -00013 (Approved, then Revoked), -00014 (Cancelled) and -00015
  (Cancelled).
- One `password-recovery` OTP challenge for uat.reception, left to expire.
- Settings changed and restored: uat.reception language ar → en (twice).
- No password, role, branch assignment, tenant parameter or shared record was changed or deactivated.
- uat.tech.chem's failed-attempt counter stands at 1, from the wrong current password in PRF-04. It resets at the
  next password change, or as the account policy says.

## 8. Not tested or blocked, and why

| Case | Status | Reason |
| --- | --- | --- |
| SES-01 branch selection | BLOCKED | every persona of the demo laboratory has one branch; shared assignments may not be changed |
| SES-03 recovery completion | passed up to the code step | no notification gateway on this lane |
| PRF-05 password change and restore | NT | passed on v0.3.8; not repeated, to keep the shared accounts untouched |
| WRK-04 delay colours | NT | every row of today's window was at 0–2 % of its turnaround |
| RES-05 restricted list | NT | the BLDGRP lines of this run were cancelled or not entered |
| RES-06 text result | NT | the demo catalogue has no text test |
| PRM-06 forced requests | NT | no bearer token is placed in the page; refusals were checked where pages offer the action |
| Results query `Enter` key | not run | synthetic keys do not submit forms; F8 verified |
| Physical printing, native dialogs | outside the automation | documents verified through captured print frames |

Environment notes:

- Before the build was pinned, the dev-server reloads cost two forms in progress (nothing was saved half-way). They
  also produced the A-04 evidence.
- Other testers' tabs (`tab-3` on 127.0.0.1, `tab-4` / `tab-6` on localhost) were never opened or used by this run.
  By design, a sign-in on 127.0.0.1 is shared by every tab of that host.
