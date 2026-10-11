# Validation 2026-10-11 · Tester A · Client portal operations (Release 1)

Build under test: **v0.3.8** (SPA `http://127.0.0.1:4300` → validation API `127.0.0.1:5102`, database `lis` on the
private PostgreSQL 5433). Laboratory: demo laboratory (tenant `01a12494-7ca8-75a8-873d-645d12599db7`, branch MAIN,
treasury MAIN-CASH). Personas: `uat.reception`, `uat.cashier`, `uat.tech`, `uat.tech.chem`, `uat.head`,
`lab.admin.manager`. Tester: automated browser session in the Browser pane. Mouse clicks did not reach a background
tab, so clicks and input events were dispatched from the page context. This runs the same Angular handlers as a user's
click. Test and report only: no application code, dictionary, script or migration was changed. All data created
carries the prefix **V11**.

Method notes (they affect how the verdicts read):

- **Printing** ran without a native dialog. A page-context stub replaced `print()` on the print kit's hidden iframe
  and captured the rendered document text: labels, job order, receipts, invoice, refund receipt, quote, worklist and
  laboratory report. The document content is verified; the physical print is not.
- **Downloads** were intercepted in the page: CSV exports and the patient document download. The `Blob` was captured
  and nothing was written to disk. `window.open` (the document *View* action) was intercepted too.
- **Forced requests** were not executed. A forced request calls an API that the page does not offer, and no bearer
  token was placed in the page. When the page *offered* an action outside the user's rights, the action was executed
  and the server's refusal recorded.
- Synthetic `Enter` key events do not trigger the browser's implicit form submission. Where a page relies on that
  (the Results query `Enter`), the verdict is "inconclusive" and the equivalent F8 or button path was tested.
- Every save was followed by a read-only database query as `lis_owner`. The queries and their results are in §4.

## 1. Summary

| Cases | Executed | Passed | Failed | Blocked | Not tested |
| --- | --- | --- | --- | --- | --- |
| 133 | 128 | 118 | 10 | 1 | 4 |

The 133 cases are those in §3; "Executed" = passed + failed. One more case (the Results query `Enter` key) is inconclusive and not counted. PRF-01
and SES-03 passed; parts of them were blocked (laboratory block, recovery completion), see §7.

Defects: **1 × S1, 0 × S2, 7 × S3, 7 × S4** (§5). Some S3/S4 defects were found inside passing cases (A-10…A-15).

Outbox: every event of the run was processed and none was dead-lettered. At the end of the run, `ops.outbox_events`
holds 0 rows for the tenant with `processed_at_utc IS NULL` or `dead_lettered_at_utc` set.

Headline: the daily client workflows work end to end. They cover registration, collection, result entry, review,
verification, printing, amendment, cancellation, refund and treasury access. Every save lands in the right tables with
the right tenant, status and reference numbers. The audit-trail events and field changes are written, and the outbox
events are processed.

The serious finding is **A-01**. After a sign-out and sign-in in the same tab, the Results pages and the worklist keep
the previous user's data and rights. A chemistry-only technician saw haematology values from the previous user's
session. The exposure was read only: the server refused the write.

## 2. Coverage (page × function → cases)

| Page | Functions | Cases |
| --- | --- | --- |
| Dashboard CP-05 | tiles, date picker, 7-day chart, quick actions, delayed table, recent activity, Arabic | DSH-01…07 |
| Registration CP-06 | patient picker search / create, title→gender, age↔DOB, mobile, similar name, contract, doctor, flags, service entry by code / shortcut / F2, duplicate, gender, Reserved, tubes, preparation, discount reason & cap, payment methods / partial / reference / overpay, Save button & Ctrl+S, saved-dialog documents, unpaid save, Clear, New registration, repeat order, profile service, Arabic | REG-01…25 |
| Edit visit | open, release reserved, add service, details, discount | EDT-01…05 |
| Search registrations CP-10 | classifier, date range, payer, status, branch, paging, CSV, Edit, Samples dialog, Payments, Documents, Results, Cancel services, Arabic | SRC-01…14 |
| Job order page | content, print | JOB-01 |
| Price query CP-12 | quote, contract switch, gender, print, continue, nothing saved | PRC-01…06 |
| Patients CP-11 | list, search, filters, edit, file, visits, documents, new visit, VIP, upload / download | PAT-01…08 |
| Worklist CP-18 | counters, filters, status boxes, search, delay colours, CSV, print, user change, Arabic | WRK-01…08 |
| Results by accession CP-19 | query / F8, numeric, invalid value, flags, panic, formula, list, text, comment library, note, history, review, undo, verify, delta, preview, print, amend, rights, user change, Arabic | RES-01…21 |
| Payments CP-23 | partial collection, validations, methods + reference, void, reprint, auto-print, invoice, register tab + CSV, Arabic | PAY-01…09 |
| Refund (from Payments) | link, limits, reason, partial refunds, receipt, form state | RFD-01…05 |
| Treasury access CP-29 / Treasuries | request, duplicate, cancel, reject, approve, revoke, SoD, filters, roles, list view, Arabic | TRS-01…11 |
| Profile CP-65 | details, language, password policy, change and restore | PRF-01…05 |
| Session | branch selection, sign-out, recovery to OTP | SES-01…03 |
| Permissions | reception, cashier, technician, unit head; direct URL | PRM-01…05 |

## 3. Test cases

Verdicts: PASS, FAIL (defect id), BLOCKED, NT (not tested). "DB-nn" points to the verification in §4.

| Id | Steps (persona) | Expected (source) | Observed | Verdict |
| --- | --- | --- | --- | --- |
| DSH-01 | Dashboard, today (reception) | branch-day figures (CP-05) | Registrations 0→2 over the run, Collected 200→410, Pending 8→7, Backlog 2→9, Delayed 0; every figure equals the SQL of `BranchDashboardReader` re-run on the database (net collections = posted payments − refunds) | PASS |
| DSH-02 | Day = 2026-10-10 | figures of that day | 16 registrations, EGP 2,436.00 (= 2,736 posted − 300 refunds), pending 14 (12 Ordered + 2 Complete), backlog 2 | PASS (A-12) |
| DSH-03 | 7-day chart | 7 local days | 04…10 Oct and 05…11 Oct windows and weekday labels correct | PASS |
| DSH-04 | Quick actions | only permitted pages | reception: 6 links; tech.chem: Results by accession and Worklist only; head has no `dashboard.view` and lands on `/` | PASS |
| DSH-05 | Recent activity | audit-backed list | reception: "The audit log permission is needed…"; manager: latest audit rows | PASS (A-12) |
| DSH-06 | Delayed tests table | BR-13 late share | nothing late (all open results < 100 %), "No delayed tests." | PASS |
| DSH-07 | Arabic | rtl, Arabic labels | rtl, Arabic tiles and nav | PASS (A-12) |
| REG-01 | New patient → Create with an empty form | required fields refused | "This field is required." + "Enter a date of birth or an age."; nothing sent | PASS |
| REG-02 | Title Mrs / Mr | title proposes gender (CP-06) | Female / Male | PASS |
| REG-03 | Years 35 → DOB; DOB 1980-02-15 → age | age ↔ DOB | 1991-10-11; 46 y 7 m 26 d | PASS |
| REG-04 | Mobile under Egypt (10): 01011110001 / 1011110001; under Any: 12-34 | country length | 11 digits refused, 10 accepted; 4 digits refused | PASS (open point 10 of the earlier run stands) |
| REG-05 | Create "V11 Walk-in One" (Mr, DOB) and "V11 Walk-in Two" (Mrs, 30 y, mobile, NID, e-mail, Giza, Dokki, Egypt) | file created, number assigned | PAT-2026-00006, PAT-2026-00007; database rows equal the form | PASS · DB-01 |
| REG-06 | Type "v11 walk-in one" in New patient | similar-name warning (BR-01, REG-002) | "Similar patient files exist…" with PAT-2026-00006 "Similar name", `Use this file` | PASS |
| REG-07 | Picker search "V11" / "V11 Walk-in Two" | existing file found | PAT-2026-00006 / -00007 listed and selectable | PASS |
| REG-08 | Service entry K, GLU + Enter | line priced from the contract | K 120, GLU 90, tube C Serum, result expected tomorrow | PASS |
| REG-09 | Entry "4" (shortcut number) | service by shortcut | resolves to HGB (already present → "Already on this visit: HGB.") | PASS |
| REG-10 | F2 → picker, type "h" / "haemo", pick HGB | picker adds the service | HGB added on tube H EDTA; an empty search shows "No service matches." | PASS (A-13) |
| REG-11 | K a second time | duplicate refused | toast "Already on this visit: K.", no second line | PASS |
| REG-12 | HCG for a male patient | REG-015 | "HCG: The service is restricted to the other gender." + save blocker; removed with `Remove HCG` | PASS |
| REG-13 | Tick Reserved on HGB, save | BR-05: billed, no tube or result | HGB Reserved, 150 billed, no tube, no result row | PASS · DB-02 |
| REG-14 | Fasting hours? = 12 h | answer stored and printed | stored as "12 h"; printed on the job order | PASS · DB-02 |
| REG-15 | Discount 15 % without reason (cap 10 %) | reason mandatory, cap | "Choose the discount reason." + "The discount exceeds your limit of 10 %."; save blocked | PASS |
| REG-16 | 10 % + "Staff and relatives" | discount spread over the lines | total 36.00; line discounts 12 / 9 / 15 | PASS · DB-02 |
| REG-17 | Card without reference; paid 400 > due | BR-26 | "Enter the transaction reference." / "The amount paid cannot exceed the amount due." | PASS |
| REG-18 | Card 200 with ref V11-CARD-001, Urgent, treatment and insurance ids, Ctrl+S | visit and payment saved | ACC-2026-00017, "1 tube(s) were issued.", "200.00 received — receipt RCT-2026-00022" | PASS · DB-02/03 |
| REG-19 | Saved dialog: Job order, Sample labels, Receipt | documents | job order (patient, Urgent, lines, tube, preparation, balance), label (urgent "!", tube, services), receipt (amount, method + ref, remaining) | PASS (A-10) |
| REG-20 | V11 Walk-in Two: RENAL, BLDGRP, HCG, K, paid 0 | unpaid save allowed | ACC-2026-00018, "2 tube(s)", `Collect on the Payments page`, no payment row | PASS · DB-04 |
| REG-21 | New registration; K + treatment no.; Clear | form reset | lines and fields empty | PASS |
| REG-22 | RENAL again for PAT-2026-00007 | BR-12 repeat window | warning panel + "Repeat order" dialog; `Register anyway` → ACC-2026-00019 | PASS · DB-10 |
| REG-23 | RENAL profile | members CREA + EGFR | results CREA (Numeric) and EGFR (Formula) | PASS · DB-04 |
| REG-24 | Tubes | BR-02 merge | C Serum shared by K / GLU / NA (SMP-2026-00019); H EDTA separate | PASS |
| REG-25 | Arabic | rtl | rtl, Arabic labels | PASS |
| EDT-01 | `Open the visit` | edit page | "Visit ACC-2026-00017", lines with their tubes | PASS |
| EDT-02 | `Release` on HGB | tube and results issued | toast "HGB was released…", SMP-2026-00020, HGB result Ordered, `released_at_utc` set | PASS · DB-05 |
| EDT-03 | Add NA | joins the existing tube | NA on SMP-2026-00019 | PASS · DB-05 |
| EDT-04 | Doctor none, treatment V11-T2, Biohazard on | details saved and audited | database and `field_changes` before / after | PASS · DB-05 |
| EDT-05 | Discount → Amount 30 | redistributed | 112.50 / 84.37 / 140.63 / 112.50 = 450.00 | PASS · DB-05 |
| SRC-01 | Search ACC-2026-00017, "17", PAT-2026-00005, "sara", "mona hassan", 01001234568, "V11" | classifier | 1 / 1 / 1 / 1 / 7 / 7 / 1 visits, each the right one | PASS |
| SRC-02 | All dates / 2026-10-10 | date range | 17 / 16 rows (= database) | PASS |
| SRC-03 | Payer AXA; Status Completed / Open | filters | 0 / 0 / 17 (database: all 17 Open, all Cash) | PASS |
| SRC-04 | This branch only off | branch filter | 17 (single-branch data) | PASS |
| SRC-05 | Paging | several pages | 17 rows < smallest page size 20 | NT |
| SRC-06 | Export CSV | file | `registrations-2026-10-11.csv`, BOM, 16 columns, values equal the row | PASS (A-15) |
| SRC-07 | Samples dialog, `Label`, `Print all labels` | tubes and labels | 2 tubes Received with times; labels rendered | PASS |
| SRC-08 | Documents dialog | labels, job order, receipts, report | all five entries work; `Open the job order` → job-order page | PASS |
| SRC-09 | Payments action | Payments with the visit | `/billing/payments?visitId=…`, account shown | PASS |
| SRC-10 | Results action | Results by accession | `/results/by-accession?accession=ACC-2026-00017` | PASS |
| SRC-11 | Edit action | visit page | opens (as EDT-01) | PASS |
| SRC-12 | Cancel K on ACC-2026-00018 | reason mandatory, BR-15 | confirm disabled until a reason; "The services were cancelled."; struck-through chip; reason "Patient declined — V11 cancel test" | PASS · DB-06 |
| SRC-13 | Cancel HCG of a paid visit (manager) | negative balance | remaining −250.00, "Refund due" on Payments | PASS · DB-08 |
| SRC-14 | Arabic | rtl | rtl, Arabic labels | PASS |
| JOB-01 | Job-order page | CP-06 job order | header, urgent + biohazard, 4 lines, 2 tubes, preparation, balance | PASS (A-10) |
| PRC-01 | Quote K, GLU, RENAL (Cash) | prices | 120 / 90 / 150, total 360, preparation "Fasting hours?" | PASS |
| PRC-02 | Contract AXA Gold | contract prices | "Not priced under this contract" on all three | PASS |
| PRC-03 | Gender Male + HCG | refused | banner "HCG: The service is restricted to the other gender." | PASS (A-11) |
| PRC-04 | Print quote | document | quote with the total and "for information only" | PASS |
| PRC-05 | Continue to registration | services carried over | `/registration/new?services=…`, three lines, Cash | PASS |
| PRC-06 | Nothing saved | no visit | visit count unchanged by the quote (19 = 16 + the 3 registrations of this run) | PASS |
| PAT-01 | Patients list | CP-11 columns | no., name, gender, age, mobile, NID, condition, VIP (for `viewConfidential`), status; **no visit count and no last visit** | FAIL A-09 |
| PAT-02 | Search by no. / name / mobile / NID / "منى"; gender and status filters | search and filters | all correct | PASS |
| PAT-03 | Edit PAT-2026-00006: bad mobile and e-mail refused, then mobile, e-mail, comment | saved and audited | "Patient file PAT-2026-00006 was saved."; database and field changes | PASS · DB-07 |
| PAT-04 | Open file: File, Visits | file and history | identity, contact, record; visit ACC-2026-00017 | PASS |
| PAT-05 | Documents tab as reception (no `files.view`) | hidden or a permission notice | tab offered; "The documents could not be loaded." (403 AUTHZ-001) | FAIL A-03 |
| PAT-06 | New visit from the file | registration with the patient | `/registration?patientId=…`, patient preselected | PASS |
| PAT-07 | VIP (manager) | VIP only for `viewConfidential` | checkbox "VIP patient (confidential)", VIP column and badge; absent for reception | PASS · DB-07 |
| PAT-08 | Upload `.txt`, then PNG + note; Download; View (manager) | PDF / JPEG / PNG up to 10 MB | "Only PDF, JPEG or PNG files can be attached."; PNG attached; the download returns the same 106 bytes | PASS · DB-07 |
| WRK-01 | Worklist default (tech) | counters | after `Refresh`: Requests 2 / Tests 7 / Samples 4 / Delayed 0, matching the rows | PASS |
| WRK-02 | Group HEMA, test K, sample Rejected / Received, payer AXA, status Complete, Delayed only, collected 10/10 | filters | 2 / 1 / 0 / 7 / 0 / 0 / 0 / 30 rows, all consistent with the database | PASS |
| WRK-03 | Search ACC-2026-00018 / PAT-2026-00007 / SMP-2026-00022 / "V11" | search | 3 / 3 / 1 rows; a name finds nothing | PASS (A-14) |
| WRK-04 | Delay colours | BR-13 ≥ 80 % / ≥ 100 % | rows at 83–84 % carry `delay-warn`; rows at 0–1 % carry none | PASS |
| WRK-05 | Export CSV, Print worklist | file and document | 18-column CSV ("7 rows were exported."), worklist printed | PASS |
| WRK-06 | `Results` row action | opens the case | not clicked from the worklist (the same navigation was tested in SRC-10) | NT |
| WRK-07 | Sign out tech, sign in tech.chem in the same tab, open Worklist | only own rights, fresh data | the previous user's list incl. 2 Haematology rows; CHEM only after `Refresh` | FAIL A-01 |
| WRK-08 | Arabic | rtl | rtl, Arabic labels | PASS |
| RES-01 | Query by accession with F8; a row opens the visit | CP-19 | F8 lists the visit and a click opens it; `Enter` inconclusive (synthetic key) | PASS |
| RES-02 | K 6.2, NA "abc", GLU 250, Save | numeric values and flags | "2 results saved." / "1 results were refused"; NA "The value must be a number."; K H (3.5–5.0), GLU H (70–110); NA 140 saved afterwards | PASS · DB-09 |
| RES-03 | HGB 5.0 | panic | badge PANIC, flag LL, row `flag-panic`, `is_panic` true, `ResultPanicDetected` | PASS · DB-09 |
| RES-04 | CREA 1.2 | formula | eGFR = 142 (ROUND(175 × 1.2^−1.154)) computed on save | PASS · DB-09 |
| RES-05 | BLDGRP list | BR-19 placeholder | default "—" with `Save` disabled; AB saved | PASS · DB-09 |
| RES-06 | Text result | text entry | the demo catalogue has no text test | NT |
| RES-07 | Comment from the library (HAEM) and internal note (tech) | comment prints, note never prints | both saved; the comment is on the report, the note is absent from the preview | PASS · DB-09 |
| RES-08 | History drawer | logs | result log 3, status log 3, previous results 0 | PASS |
| RES-09 | Review (tech) | Complete → Reviewed | "3 results reviewed." | PASS |
| RES-10 | Undo with reason (head) | reason required | "A reason is required." then "3 results undone." → Complete; the reason is in the status log | PASS · DB-09 |
| RES-11 | Verify (head) | signature | "3 results verified (electronic signature)." | PASS |
| RES-12 | ACC-2026-00019: CREA 2.0, then Verify | BR-11 | Δ 66.7 % badge + `Acknowledge`; "Delta check…" dialog; `Acknowledge and verify` → Verified | PASS · DB-10 |
| RES-13 | Preview of unverified results (tech) | watermark | "PREVIEW — NOT A REPORT", "PRELIMINARY — NOT YET VERIFIED"; comment shown, note hidden | PASS |
| RES-14 | Print unverified results (tech) | BR-09 | "Only verified results print (BR-09)." | PASS |
| RES-15 | Print verified results (head) | printed + status log | "marked printed and sent to the printer"; status Printed, copy 1 | PASS · DB-09 |
| RES-16 | Amend K 6.2 → 5.8 (head) | reason required, back to Complete | reason required; "returned to Complete"; re-verified; `amendment_count` 1 | PASS · DB-09 |
| RES-17 | Technician's buttons | no Verify / Undo / Amend | Save, Review, Preview, Print report only | PASS |
| RES-18 | Reception on a case | read only | value inputs disabled, but `Report comment` / `Internal note` open an editable dialog; Save → "You do not hold the permission for this action." | FAIL A-02 |
| RES-19 | Sign out reception, sign in tech in the same tab | tech can enter values | inputs disabled and no Save until a full reload | FAIL A-01 |
| RES-20 | tech.chem on the stale HEMA tab: HGB 5.1, Save | server refuses | "You have no right to enter results of this clinical group in this branch."; nothing written | PASS |
| RES-21 | Arabic | rtl | rtl, Arabic labels | PASS |
| PAY-01 | ACC-2026-00018: cash 200 (cashier) | partial payment | RCT-2026-00023, Partly paid, remaining 260 | PASS · DB-08 |
| PAY-02 | Amounts 500, 0, 12.345; card without reference | BR-26 | "at most 460", "at least 0.01", "wrong format", Reference "This field is required." | PASS |
| PAY-03 | Card 100 (V11-SLIP-77), Transfer 160 (V11-TRF-88) | methods with reference | RCT-2026-00024 / -00025; Paid; "Nothing is due on this visit." | PASS · DB-08 |
| PAY-04 | Void RCT-2026-00025 | reason mandatory | "A reason is required." then "Receipt RCT-2026-00025 voided."; remaining 160 | PASS · DB-08 |
| PAY-05 | POS 160 (V11-POS-99) | settles the visit | RCT-2026-00026, Paid | PASS · DB-08 |
| PAY-06 | Reprint; tenant `autoPrintReceipt` = false | reprint offered; no automatic print | reprint works, but **every payment printed a receipt automatically** | FAIL A-05 |
| PAY-07 | Print invoice | invoice | services, payments, refunds and balance | PASS |
| PAY-08 | Register tab: Voided, POS, today, accession; CSV | register | correct rows; CSV with the void columns | PASS |
| PAY-09 | Arabic | rtl | rtl, Arabic labels | PASS |
| RFD-01 | `Refund the balance` on ACC-2026-00018 | refund form (CP-24 via CP-23) | `/billing/refunds?visitId=…`, Refundable 250.00 | PASS |
| RFD-02 | Amount 300; no reason | BR-14, reason mandatory | "at most 250"; "This field is required." | PASS |
| RFD-03 | 200 cash, "Service cancelled" + note; then 50 card V11-RFDCARD | partial refunds | RFD-2026-00003 / -00004; refund receipt with signature lines | PASS · DB-08 |
| RFD-04 | Second refund without typing a note | empty note | RFD-2026-00004 saved with the first refund's note "V11 HCG cancelled" | FAIL A-06 |
| RFD-05 | After the full refund | nothing refundable | "Nothing is refundable on this visit." | PASS |
| TRS-01 | Request without a treasury; then NORTH-CASH + label (reception) | treasury required, request Pending | "This field is required."; TRQ-2026-00007 Pending | PASS · DB-11 |
| TRS-02 | Same request again | TRS-012 | "A pending or approved request already exists for this treasury on this device." | PASS |
| TRS-03 | Cancel own request | Cancelled | dialog (reason optional) → "Request cancelled." | PASS · DB-11 |
| TRS-04 | Manager rejects TRQ-2026-00008 | reason mandatory | "A reason is required." then Rejected | PASS · DB-11 |
| TRS-05 | Manager approves TRQ-2026-00009 (cashier) | Approved | Approved, "Shift Manager · 11 Oct 2026, 10:45" (no comment field: D-05 still open) | PASS · DB-11 |
| TRS-06 | Revoke TRQ-2026-00009 | reason mandatory | Revoked | PASS · DB-11 |
| TRS-07 | Manager's own request TRQ-2026-00010 | SoD | only `Cancel request` offered; cancelled (forced approve not executed) | PASS |
| TRS-08 | Filters Pending / My requests only | filters | correct rows | PASS |
| TRS-09 | Cashier on other users' rows | no decision buttons | no Approve / Reject | PASS |
| TRS-10 | Treasuries list as reception or cashier | branch as `CODE · name` | branch column shows the raw GUID | FAIL A-04 |
| TRS-11 | Arabic | rtl | rtl, Arabic labels | PASS |
| PRF-01 | Profile details | CP-65 | account block correct; laboratory block reads "subscription record … not available" (the demo laboratory has no `saas.tenants` row) | PASS (laboratory block BLOCKED, data) |
| PRF-02 | Save language Arabic, then English | stored and applied | `preferred_language` ar → en; the toast showed the raw key `account.profile.languageSaved` | FAIL A-07 |
| PRF-03 | Policy checklist (too short, contains username, same as current, mismatch) | ORG-020 / ORG-021 and account rules | each rule unticks live; a mismatch is not sent | PASS |
| PRF-04 | Wrong current password | refused | "The current password is not correct."; `PASSWORD_CHANGE_FAILED` | PASS |
| PRF-05 | tech.chem: change, sign in with the new password, restore | changed and restored | "Your password was changed." twice; sign-in with the new password works; original password restored | PASS · DB-12 |
| SES-01 | Branch selection | CP-04 | every persona has exactly one branch; `/account/select-branch` redirects to the dashboard | BLOCKED |
| SES-02 | Sign out | session ends | `/login`, `SESSION_REVOKED` | PASS |
| SES-03 | Recovery: empty field, user without mobile, reception, bad code | up to the OTP step | "This field is required."; the same neutral message for both users; a challenge is created only for the user with a mobile (`no_mobile` logged); "The value has the wrong format." for "12ab" | PASS (completion BLOCKED: gateway) |
| PRM-01 | Reception | R1 nav, no verification or setup | nav = reception + laboratory (view) + finance (view) + catalogue lists + profile; no Branches / Roles / Users | PASS |
| PRM-02 | Cashier | no create / edit / cancel | nav without Registration; rows offer Payments and Documents only | PASS |
| PRM-03 | Technician | results only | nav: dashboard, worklist, results, catalogue lists, profile | PASS |
| PRM-04 | Unit head: nav; `/billing/payments` typed in the address bar | page refused | nav correct; the Payments page opens, and the server answers "You do not hold the permission for this action." | FAIL A-08 |
| PRM-05 | Forced requests | AUTHZ-001 / SoD codes | not executed (no token in the page) | NT |

## 4. Database verification

All queries ran as `lis_owner` (read only) against `lis`. Every row listed carries tenant
`01a12494-7ca8-75a8-873d-645d12599db7` (checked with `tenant_id = '<tenant>'` predicates).

| Ref | Case | Table(s) | Query (abridged) | Result |
| --- | --- | --- | --- | --- |
| DB-01 | REG-05 | app.patients | `select number,title,full_name,gender,date_of_birth,age_years,mobile,national_id,nationality,email,governorate,city,is_vip from app.patients where number in ('PAT-2026-00006','PAT-2026-00007')` | `PAT-2026-00006 · Mr · V11 Walk-in One · Male · 1980-02-15`; `PAT-2026-00007 · Mrs · V11 Walk-in Two · Female · age 30 · 01011110002 · 29601010100022 · EG · v11.two@example.test · Giza · Dokki`; audit `PatientRegistered` ×2 |
| DB-02 | REG-13…18 | app.visits, visit_lines, visit_tubes, samples, results, visit_preparation_answers | `… where accession='ACC-2026-00017'` | visit Open, branch MAIN, V11-T1 / V11-INS, urgent, DR01, total 360, discount 36 Percent 10 "Staff and relatives", share 324, revision 1; lines K 108 / GLU 81 Ordered with tube, HGB 135 **Reserved, no tube**; tube SMP-2026-00019 C Serum; sample Received (2 services); results K, GLU Ordered; answer "Fasting hours? = 12 h" |
| DB-03 | REG-18 | app.payments, visit_accounts, audit.audit_trail, audit.field_changes, ops.outbox_events | `… where visit_id='01a129dc-ea4e-7926-90ce-6d1ff6dc7ef2'` | RCT-2026-00022 · 200.00 · Card · V11-CARD-001 · Posted · MAIN-CASH · device a0ab790c-… · branch MAIN; account paid 200; trail seq 266–270: `VisitRegistered`, `VisitDiscountChanged`, `PaymentRecorded`, `SampleCollected`, `SampleReceived`; field changes Created for Visit, VisitLine ×3, VisitTube, VisitAccount, Payment, VisitPreparationAnswer, Sample, Result ×2; 5 outbox events processed (attempts 0), none dead-lettered |
| DB-04 | REG-20/23 | visits, visit_lines, results, payments | `… accession='ACC-2026-00018'` | Open, 580 / 580, no payment; no `visit_accounts` row until the first payment; lines RENAL / BLDGRP / HCG / K Ordered on SMP-2026-00021 (C) and -00022 (H); results BLDGRP RestrictedList, CREA Numeric, EGFR Formula, HCG, K |
| DB-05 | EDT-02…05 | visits, visit_lines, samples, results, audit.field_changes, audit.audit_trail | `… id='01a129dc-…'` | revision 5; V11-T2, doctor null, biohazard true; total 480, discount 30 Amount, share 450; HGB `released_at_utc` set on SMP-2026-00020; NA on SMP-2026-00019 (3 services); 4 results Ordered; field changes: Revision 1→2 (release), totals (NA), Biohazard / ReferringDoctorId / TreatmentNo, DiscountKind Percent→Amount with the reason; trail `VisitLinesAdded` ×2, `VisitDiscountChanged` |
| DB-06 | SRC-12 | visit_lines, results, audit_trail, outbox | `… visit_id='01a129e2-e5b1-734f-92c3-9eea48f6ba40'` | K Cancelled "Patient declined — V11 cancel test", cancelled by / at set; result K Cancelled with the same reason; share 460; trail `VisitLineCancelled`; outbox `VisitLineCancelled` + `ResultCancelled` processed |
| DB-07 | PAT-03/07/08 | patients, patient_documents, field_changes, audit_trail | `… number='PAT-2026-00006'` / `'PAT-2026-00007'` | PAT-6 mobile 01011110001, e-mail, comment, modified by UAT Reception Clerk; field changes Comments / Email / Mobile null→value; trail `PatientUpdated`; PAT-7 `is_vip` true; document `V11-referral.png`, image/png, 106 B, sha256 91b6e61c…, note, active, storage key `<tenant>/patients/<id>/<doc>`; field change `PatientDocument Created`; outbox `PatientDocumentAttached` processed |
| DB-08 | PAY / RFD | payments, refunds, visit_accounts, visit_lines, audit_trail, outbox | `… visit_id='01a129e2-…'` | RCT-23 200 Cash; RCT-24 100 Card V11-SLIP-77; RCT-25 160 Transfer V11-TRF-88 **Voided** "V11 wrong method" (voided by set); RCT-26 160 Pos V11-POS-99; all on MAIN-CASH, by uat.cashier, device a0ab790c. Account paid 460, refunded 250; HCG Cancelled "Ordered by mistake". RFD-2026-00003 200 Cash and RFD-2026-00004 50 Card V11-RFDCARD, reason "Service cancelled", note "V11 HCG cancelled" on **both** (A-06). Trail `PaymentRecorded` ×4, `PaymentVoided`, `VisitLineCancelled`, `RefundRecorded` ×2; outbox all processed |
| DB-09 | RES | results, result_log_entries, audit_trail | `… accession in ('ACC-2026-00017','ACC-2026-00018')` | K 5.8 Verified, print 1, amendment 1, HAEM comment text, internal note; GLU 250 High Printed; NA 140 Normal Printed; HGB 5.0 CriticalLow `is_panic` Verified; CREA 1.20 High, EGFR 142.00, BLDGRP "AB" Verified by UAT Unit Head. K log: value — → 6.2 (tech), Complete → Reviewed, Comment, Note, Reviewed → Complete "V11 undo review test", Verified, Printed (copy 1), value 6.2 → 5.8 "V11 transcription error", Printed → Complete, Verified. Trail `ResultEntered`, `ResultPanicDetected`, `ResultReviewed` ×3, `ResultUndone` ×3, `ResultVerified` ×8, `ResultPrinted` ×3, `ResultAmended`, `ResultCancelled` ×2. tech.chem's refused HGB 5.1 left no row |
| DB-10 | REG-22 / RES-12 | visits, payments, results | `… accession='ACC-2026-00019'` | RCT-2026-00027 150 Cash; CREA 2.00 Verified, `delta_flag` true, 66.7 %, previous 1.2, acknowledged by / at set; EGFR 79.00 Verified |
| DB-11 | TRS | app.treasury_access_requests, audit_trail | `… number in ('TRQ-2026-00007' … '-00010')` | 7 Cancelled; 8 Rejected (reason "V11 wrong branch for this user"); 9 Approved, then Revoked ("V11 revoke test"); 10 Cancelled; all NORTH-CASH / branch NORTH / device a0ab790c; trail Requested / Cancelled / Rejected / Approved / Revoked; outbox processed |
| DB-12 | PRF-02/05, SES | app.tenant_memberships, identity.platform_users, audit.security_events, identity.otp_challenges | `preferred_language`, `password_changed_at_utc`, events of the last minutes | reception `preferred_language` ar (10:39), then en; tech.chem `password_changed_at_utc` 10:54, then 10:54:50, `failed_password_attempts` 0; events `PASSWORD_CHANGE_FAILED`, `PASSWORD_CHANGED`, `SESSION_REVOKED` (logout), `PASSWORD_RECOVERY_FAILED` (`no_mobile`), `PASSWORD_RECOVERY_STARTED`; one `password-recovery` OTP challenge (5 min, 0 attempts, not consumed) |
| DB-13 | all | ops.outbox_events | `select type, count(*), count(processed_at_utc), count(dead_lettered_at_utc) … where tenant_id=<tenant> and occurred_at_utc > now() - interval '40 min' group by type` | 25 event types, every event processed, 0 dead letters; at the end of the run 0 unprocessed rows for the tenant |

## 5. Defect log

| Id | Sev | Case | Steps | Expected | Observed | Evidence | Suspected root cause |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A-01 | **S1** (security: data beyond the user's result rights; also blocks entry until a reload) | WRK-07, RES-19 | 1. As `uat.tech`, open Worklist and Results by accession ACC-2026-00017 (CHEM + HEMA). 2. Sign out; sign in as `uat.tech.chem` (CHEM only) in the same tab. 3. Open Worklist, then Results by accession. Reverse direction: reception → tech. | each user sees only data their result rights allow; rights and data are read again at sign-in (CP-18 / CP-19, BR-17) | the worklist shows the previous user's rows, including Haemoglobin and Blood group. Results shows the "Haematology Complete" tab with HGB **5.0 PANIC**, and the stale tab even offers Save (the server refuses). In the reverse direction, tech inherits reception's read-only rights (inputs disabled) until a full reload | WRK-07 / RES-19 / RES-20 transcripts; `Refresh` or a reload fixes it | `frontend/src/app/features/results/results.routes.ts` provides `WorklistFacade` (l.15) and `ResultsCaseFacade` (l.20) at route level. Route injectors live for the app's lifetime, and the `resource()`s in `state/worklist.facade.ts` and `state/results-case.facade.ts` are keyed on filter / accession, not on the user or session. Same family as D-08, which was fixed only in `billing/state/cash-desk.facade.ts`. Every feature facade that holds tenant data needs a reset keyed on the user / session |
| A-02 | S3 | RES-18 | Reception (`results.view` only) opens a case and clicks `Report comment (prints)` or `Internal note` | actions hidden for users without edit rights (CP-19) | editable dialog with the comment library and Save; Save → "You do not hold the permission for this action." | RES-18 transcript | `frontend/src/app/features/results/ui/result-grid.component.ts` l.149–150: the comment and note buttons are not gated by `canEdit()` (the value input is, l.235) |
| A-03 | S3 | PAT-05 | Reception opens Patients › file › Documents | tab hidden, or a permission notice, when the user lacks `files.view` | tab offered; error "The documents could not be loaded."; `GET /api/v1/patients/{id}/documents?includeInactive=true` → 403 `AUTHZ-001` | network log | `features/patients/pages/patient-file.page.html` renders the Documents tab without `*appCan` on `files.view`, while `ListPatientDocumentsQuery` requires `files.view` (`Platform.Application/Patients/Queries/PatientDocumentQueries.cs` l.13). Also, `state/patient-file.facade.ts` l.21 turns "Show deactivated documents" on by default (S4) |
| A-04 | S3 | TRS-10 | Reception or cashier opens Finance › Treasuries | branch shown as `CODE · name` (the D-03 fix) | raw branch GUID; `/api/v1/organization/branches` → 403 ×5 | network log | `features/treasury/pages/treasuries.page.ts` l.56–64 resolves names only through `TreasuryApi.activeBranches()` (`data/treasury.api.ts` l.37, which needs `organization.view`) and falls back to the id. Use `/organization/branches/selectable`, or return the branch code and name in the treasury list contract |
| A-05 | S3 | PAY-06 | Tenant `autoPrintReceipt` = false; the cashier records a payment | no automatic receipt (CP-23: "receipt auto-printed per parameter") | a receipt print job starts after every payment (captured RCT-23, -24, -25); the registration page, by contrast, does not print | print capture; `GET /api/v1/settings` → 403 | `features/billing/data/billing.api.ts` l.61–77 reads the parameter through `/api/v1/settings` (needs `settings.view`) and returns **true** on any failure (`state/cash-desk.facade.ts` l.60). A cashier never holds `settings.view`, so the parameter is ignored. Expose the print parameters to the cash desk, e.g. in the billing contract |
| A-06 | S3 | RFD-04 | Refund 200 with a note, then a second refund without typing a note | every movement is entered from a fresh form | RFD-2026-00004 stored the first refund's note; method, reason and reference also keep their previous values (the payment form is the same component) | DB-08 | `features/billing/ui/movement-form.component.ts` (l.131–198): after a successful submit only `amount` is recomputed by the effect; `note`, `reason`, `method` and `reference` are not reset |
| A-07 | S4 | PRF-02, PAY-01 | Profile: choose العربية, `Save language`; open Payments for the first time | translated messages | toast shows the raw key `account.profile.languageSaved`; toast `billing.loading` on the first Payments load | captured toasts | `features/account/pages/profile.page.ts` l.100 builds the toast text right after the language switch, before `account.ar.json` has loaded; `billing.loading` renders before the billing dictionary arrives (`pages/payments.page.html` l.19) |
| A-08 | S3 | PRM-04 | Unit head (no `billing.view`) types `/billing/payments` in the address bar | route refused or redirected | the page opens and the API refuses the lookup; the R2 routes (`/billing/refunds`, `batch-payment`, `cash-closing`) also open by URL | PRM-04 transcript | `frontend/src/app/app.routes.ts` guards the features with `tenantGuard` only; there is no route-level permission or release guard derived from `NAV_REGISTRY` |
| A-09 | S3 | PAT-01 | Patients list | CP-11 data includes "VIP, visit count, last visit" | no visit-count and no last-visit columns | PAT-01 | `features/patients/pages/patients.page.html` columns; the `PatientListItem` contract carries no visit figures |
| A-10 | S4 | REG-19, JOB-01, PAY-07 | Any printed document | bilingual letterhead with the Arabic branch name | the Arabic laboratory name is followed by "Main Branch" in English, although `name_ar` = الفرع الرئيسي | captured documents | `features/registration/ui/registration-documents.ts` l.221 / 318 / 386 pick one branch name by UI language, and the letterhead prints it under both laboratory names |
| A-11 | S4 | PRC-03 | Price query, gender Male, add HCG | the line says why it is refused | the banner gives the reason, but the line reads "Not priced yet." | PRC-03 | pending-line row in `features/registration/pages/price-query.page.html` (the D-06 fix was applied to the registration page only) |
| A-12 | S4 | DSH-02/05/07 | Dashboard | consistent captions, digits and names | tiles say "today" for a past day; chart values and the day summary use Latin digits while the tiles use Arabic-Indic digits; Recent activity shows technical names ("ResultLogEntry", "ResultVerified"); page title "لوحة المتابعة" vs nav "لوحة المعلومات" | DSH transcripts | `features/account/pages/dashboard.page.*`, `public/i18n/account.ar.json` |
| A-13 | S4 | REG-10 | F2 with an empty search | "F2 browses the catalogue" | "No service matches." until a term is typed | REG-10 | service picker in `features/registration` |
| A-14 | S4 | WRK-03 | Worklist search by patient name | name search (CP-18 "Search") | a name finds nothing; accession, patient no. and sample no. work | WRK-03 | the worklist `search` filter matches identifiers only (`ResultRepository.WorklistAsync`) |
| A-15 | S4 | SRC-06 | Export the registrations CSV | local date / time as in the grid | the `Date` column is raw UTC ISO (`2026-10-11T07:28:26.129944+00:00`); the payments CSV at least labels its column "(UTC)" | CSV capture | `features/registration/pages/visits.page.ts` l.207 writes `registeredAtUtc` unformatted |

Known open points seen again (not new defects): D-05 (treasury-access rows show no decision reason or approval
comment, and the approve dialog has no comment field); open point 10 of the laboratory run (Egypt mobile length 10 vs
the trunk 0); the plural wording "1 results were refused" / "1 visits were exported".

## 6. Data created (dev database `lis`, demo laboratory, invented data)

- Patients **PAT-2026-00006** "V11 Walk-in One" (mobile 01011110001, e-mail, comment) and **PAT-2026-00007**
  "V11 Walk-in Two" (VIP, document `V11-referral.png`).
- **ACC-2026-00017** (PAT-6): K, GLU, HGB (released) and NA; urgent, biohazard, V11-T2; discount 30 EGP;
  RCT-2026-00022 card 200; remaining 250. CHEM printed (copy 1); K amended 6.2 → 5.8 and re-verified, with the HAEM
  comment and an internal note. HGB 5.0 panic, verified. Samples SMP-2026-00019 and -00020.
- **ACC-2026-00018** (PAT-7): RENAL and BLDGRP verified; K and HCG cancelled. Payments RCT-2026-00023…-00026
  (-00025 voided). Refunds RFD-2026-00003 (200 cash) and RFD-2026-00004 (50 card). Samples SMP-2026-00021 and -00022.
- **ACC-2026-00019** (PAT-7): RENAL; CREA 2.00 with the delta acknowledged, verified; RCT-2026-00027 150 cash.
  Sample SMP-2026-00023.
- Treasury requests TRQ-2026-00007 (Cancelled), -00008 (Rejected), -00009 (Approved, then Revoked) and -00010
  (Cancelled).
- One `password-recovery` OTP challenge for `uat.reception` (expired unused).
- Account settings changed and restored: `uat.reception` language ar → en; `uat.tech.chem` password changed and
  restored to the original.
- No role, branch assignment, tenant parameter or shared record was changed or deactivated.

## 7. Not tested or blocked, and why

| Case | Status | Reason |
| --- | --- | --- |
| SES-01 Branch selection | BLOCKED | no persona has two branches, and the assignments of shared accounts may not be changed |
| SES-03 recovery completion | BLOCKED (case passed up to the OTP step) | no notification gateway on this lane |
| PRF-01 laboratory block | BLOCKED, data (case passed for the account block) | the demo laboratory has no `saas.tenants` subscription record |
| SRC-05 paging | NT | 17 visits < the smallest page size (20) |
| RES-06 text result | NT | the demo catalogue has no text-type test (creating catalogue data is outside this scope) |
| WRK-06 worklist `Results` action | NT | time; the same navigation was verified from Search registrations (SRC-10) |
| PRM-05 forced requests | NT | no bearer token is placed in the page; refusals were verified only where the page offered the action (RES-18, RES-20, PRM-04) |
| Results query `Enter` key | inconclusive | synthetic key events do not trigger implicit form submission; F8 was verified |
| Physical printing, native dialogs | outside what the automation can do | documents verified through captured print frames |
| Audit hash-chain verification (`/api/v1/audit/verify-chain`) | NT | needs an authenticated API call from outside the page; trail rows and sequences were read directly |

Environment note: during the run, another tester's automation navigated this tester's first tab (`tab-3`) to
`http://localhost:4300/login`. Testing continued in a new tab on `127.0.0.1`; no data was affected.
