# Final browser round · 2026-10-12 · tester F-CLIENT · client portal (LIS-VR-002)

Scope:

- the fixes for the open points of LIS-VR-002 on the client portal (report §6, §7 and §10, README "Unreleased");
- every client-portal case that earlier rounds skipped or were blocked from (report §7).

This run only tested and reported. No application code, dictionary, test, script or migration was changed. Data was changed
only through the application pages, where a step allowed it. Every shared setting that was changed was restored (§6).

New findings continue the numbering of `retest-final-admin.md` (F-01…F-04), so ids are unique in this round: **F-05…F-17**.

## Environment

- **Site:** `http://127.0.0.1:4300`, the pinned build `.local/validation/ui`.
  - `PINNED.txt`: "open-points df3bdac+working-tree built 2026-10-12T12:33:08Z".
  - It proxies to the validation API `127.0.0.1:5102` (health `ready`).
- **Database:** `lis` on `127.0.0.1:5433`, read only as `lis_owner` (session `default_transaction_read_only = on`).
- **Laboratories:**
  - demo laboratory `01a12494-…` (no client record);
  - Walk-through Laboratory `01a124d7-…`;
  - V11 API Lab 261012123322 `01a1301a-…`, used for the patient-condition case (§2, B-5).
- **Accounts:**
  - `lab.admin`, `uat.reception`, `uat.tech`, `wt.admin`, `v14.reception` and `v11api.admin.261012123322`;
  - the V19 accounts created in this run (§7).
  - Passwords are only in the git-ignored `.local/dev-credentials.md`.
- **Browser:** one own tab (`tab-5`) on host `127.0.0.1`, signed out and closed at the end. The other tabs (`seed`,
  `tab-1` … `tab-4`) were not used.
- **Time:** 2026-10-12, 15:35–16:42 local (12:35–13:42 UTC).

Method notes (as in rounds 3 and 4):

- **Input.** The Browser pane was hidden. Clicks and input were dispatched from the page context: `element.click()`,
  `input` / `change` / `blur` events, and `keydown` for Enter, Escape, F2, F8, F10, Ctrl+K and Ctrl+S. These run the same
  Angular handlers as a user.
- **Router navigation.** After each full page load, the page context set `Document.prototype.startViewTransition = undefined`.
- **Printing** ran without a native dialog, as in `client-operations.md`. The `print()` of the print-kit iframe was replaced, so
  the rendered frame (text, `lang`, `dir`) was captured. Downloads (CSV) were captured from the Blob, and nothing was written
  to disk.
- **Network evidence** came from the tab's network log and `PerformanceResourceTiming.responseStatus`. Console errors came
  from the tab's console log; markers bracket each smoke run.
- **Node client.** It was used only for forced requests, the audit API and two failed sign-ins (A-10).
  - It is a scratch file in the session scratchpad and is never committed.
  - It seals the password as the SPA does (compact JWE, RSA-OAEP-256 + A256GCM).
  - It sends `Origin`, `Sec-Fetch-Site: same-origin` and `X-Requested-With: Platform`; its User-Agent is
    `F-CLIENT-node/1.0`.
  - No bearer token was placed in the page.

## 1. Summary

| Items | PASS | PARTLY | FAIL | New findings |
| --- | --- | --- | --- | --- |
| **23** (A-1…A-10, B-1…B-13) | **19** | **3** | **1** | **13** (2 × S3, 11 × S4) |

| Id | Subject | Verdict |
| --- | --- | --- |
| A-1 | Treasury access: comment and reason on rows and CSV, branch-limited picker, `TRS-015`, device bound to the person | **PASS** (one-time migration effect: F-16) |
| A-2 | Audit trail: payer, tier, base price list and treasury edits are events | **PASS** |
| A-3 | Catalogue: `CAT-061`, Release-2 modules hidden and kept, no Report template field, doctors' branch codes | **PARTLY**: everything passes, but a role save that keeps Release-2 keys could only be shown indirectly (F-07) |
| A-4 | Blocked-save notice on the user, test, service and patient editors | **PASS** |
| A-5 | Add user: duplicate username, e-mail, mobile and employee code under their fields | **PASS** |
| A-6 | Registration: `REG-109`, `REG-206`, BIO label, tube wording 1/2/many in both languages, expected time, branch in header | **PASS** |
| A-7 | Results: VIP masked on the worklist and the accession list, no "0" before a query, digits | **PASS** (new: F-06 opened visit, F-09 mixed digits) |
| A-8 | eGFR with `@AGE` / `@FEMALE` | **PASS**: 65 (F 30 y) and 81 (M 45 y), the correct MDRD values (see §2) |
| A-9 | Egyptian mobile with the trunk 0 | **PASS** with the country Egypt chosen (F-08 for the default "Any") |
| A-10 | Lock-out from wrong current passwords on Change password | **PASS** |
| B-1 | Branch selection at sign-in and change of branch (CP-04) | **PARTLY**: works, but no screen offers "change branch" (F-05) |
| B-2 | Session timeout and screen lock | **FAIL**: nothing happens (the client-portal case of F-01) |
| B-3 | Delay colours | **PASS**, shown with the delay thresholds; the editor cannot set a 1–2 minute turnaround (F-15) |
| B-4 | Restricted-list and text results | **PASS** |
| B-5 | Patient condition shown to a patients reader without `catalogue.view` | **PASS** (in V11 API Lab; the demo labs have no conditions) |
| B-6 | Cancel a service with a verified result | **PASS**: refused, rule recorded |
| B-7 | Paging on Branches and Users | **PASS** |
| B-8 | Reject a treasury request with a reason; a blank reason is refused | **PASS** |
| B-9 | Keyboard: F10, Ctrl+S, F2, Ctrl+K, F8, Escape | **PASS** |
| B-10 | Printing: receipt, job order, labels, result report, invoice, in English and Arabic | **PARTLY**: the Arabic job order prints Arabic-Indic digits (F-10); the Arabic report mixes languages (F-12) |
| B-11 | Export CSV: branches, roles, users, treasuries (and treasury access) | **PASS** |
| B-12 | Forced API requests | **PASS** (note F-17) |
| B-13 | Smoke of every client menu page: `lab.admin` in English and Arabic, `uat.reception` in English | **PASS**: no console error from the pages |

Not passing:

- **B-2** (no idle timeout; S3, same as F-01 of the admin round);
- **B-1** (F-05, S3);
- **A-3** (F-07);
- **B-10** (F-10, F-12).

## 2. Items

### A-1 Treasury access — PASS

| Step | Result | Evidence |
| --- | --- | --- |
| Users | `v19c.cash1` (A) and `v19c.cash2` (B) were created through Setup › Users: role Reception, branch MAIN only | §7 |
| Picker | A and B (MAIN only), and `lab.admin` (MAIN only), see only "MAIN-CASH · Main cashier". The hint reads "Only the treasuries of your branches are offered." `v19c.two` (MAIN + NORTH) sees MAIN-CASH and NORTH-CASH | screen |
| Device per person | On the same browser: A's id is `3fc47c21-…`, B's is `cd29020b-…`. `localStorage["lis.device-id"]` is a map of user id to device id | screen, storage |
| Requests | A requests TRQ-2026-00017 ("V19 desk A"); B requests TRQ-2026-00018 ("V19 desk B") | DB `app.treasury_access_requests` |
| Approve with comment | `lab.admin` approved TRQ-17 with "V19 approved for desk A". Row: "Approved · Laboratory Administrator · 12 Oct 2026, 15:45 · Comment: V19 approved for desk A" | DB `approval_comment`, `approved_at_utc` 15:45:27 |
| Reject with reason | An empty reason gives "A reason is required." and the dialog stays open; three spaces give the same. "V19 rejected: not this desk" is accepted, and the row shows "Reason: …" | DB `decision_reason`, status Rejected |
| Requester view | Signed in as A, both rows show the comment and the reason, as in the approver view | screen |
| CSV | `treasury-access-requests-2026-10-12.csv` has the columns `…, Approval comment, Decision reason`. TRQ-17 has the comment and TRQ-18 has the reason | captured blob |
| A takes payments | A registered V19 Pay Alpha with GLU and paid 90.00 on MAIN-CASH (F10 save) | DB RCT-2026-00047, device `3fc47c21`, cashier `v19c.cash1` |
| B on the same browser | Registration says "No treasury is approved for you on this device in this branch…". Payments shows "No treasury is approved for you on this device. Request treasury access". The link opens Treasury access, and B's new request TRQ-2026-00019 was later cancelled by B | screen, DB |
| Forced: B pays with A's device id | `POST /billing/payments` with device `3fc47c21-…` returns **403 BIL-020**; with B's own id also **403 BIL-020** | Node |
| Forced: TRS-015 | `v19c.cash1` and `lab.admin` (MAIN only) request NORTH-CASH: **422 TRS-015** "This treasury belongs to a branch you are not assigned to." | Node |

### A-2 Audit trail — PASS

The client audit page (`/account/audit`) is Release 2 and hidden. The check used the database and `GET /api/v1/audit/entries`
as `lab.admin`.

| Edit (each made, then undone, through the page) | `audit.audit_trail` (demo laboratory) |
| --- | --- |
| Payer AXA Insurance renamed "AXA Insurance V19", then back | seq 629, 630 `Pricing.PayerUpdated` |
| Tier Silver renamed "Silver V19", then back | seq 631, 632 `Pricing.ContractTierRenamed` |
| Base price list CASH-2026 renamed "Cash prices 2026 V19", then back | seq 633, 634 `Pricing.BasePriceListUpdated` |
| Treasury NORTH-CASH renamed "North cashier desk V19", then back | seq 635, 636 `Treasury.TreasuryUpdated` |

- The API lists each edit as an `Event` entry next to its `FieldChange` entry.
- `GET /audit/verify-chain` returns `{"ok":true,"checked":636}`.

### A-3 Catalogue — PARTLY

- **`CAT-061`.**
  - BLDGRP, value "A", ticked Normal and Critical: the row shows "A value is Normal or Critical, not both." and the notice
    "The record was not saved…". No request is sent.
  - Forced `PUT …/list-values` returns **400** with `errors["values[0].IsCritical"]` and `rules: [{rule: "CAT-061"}]`.
  - Not saved (the page was left).
- **Release-2 modules hidden, held keys kept.**
  - *User overrides*, `uat.short` (95 added keys, 25 of them Release 2): the matrix shows only Release-1 modules (70 of 70).
    It shows "This user also has 25 override(s) on modules of a later release; they are not shown and are kept when you save."
  - Unticking *Reports and exports: Export* and saving gave 94 added keys, with the **25 Release-2 keys kept**. Ticking it
    again gave 95. Events: seq 637, 638.
  - *Role editor*, Walk-through *Laboratory Administrator* (95 keys, 25 Release 2): the matrix shows 70 of 70 and "This role
    also holds 25 permission(s) of modules of a later release…".
  - A save that removes a key is refused: "The main administrator's role cannot lose permissions." The result-rights save kept
    95 keys with 25 Release 2 (DB).
  - Demo *Laboratory Administrator* (101 keys): the save is refused with "Platform permissions cannot be granted in a
    laboratory." (F-07). The refusal shows that the editor sends the hidden keys back, but a role save that keeps Release-2
    keys could not be shown directly. No other role holds Release-2 keys, and none can be given one through the page.
- **Clinical group** form: Code, Unit code, Name EN/AR, Report name, Display order. There is no Report template field.
- **Doctors** as `v14.reception` (Walk-through, branches MAIN and V14-EAST): the branch column shows codes ("V11-NORTH MAIN",
  "V14-EAST MAIN", "V14-EAST", "All branches"). No ids, and no refused call.

### A-4 Blocked-save notices — PASS

An empty save shows "The record was not saved: correct the marked fields first." and marks the fields:

- **user**: 6 fields; the focus moves to Username;
- **test**: Code, Clinical group, Name EN/AR;
- **service**: Code, Short name, Name EN/AR, Clinical group, Sample type;
- **patient**: Name, plus "Enter a date of birth or an age."

### A-5 Duplicates on Add user — PASS

The server reports one duplicate per save, each under its own field:

- "This username is taken." (`uat.reception`);
- "Another account uses this e-mail address." (`lab.admin@lis.local`);
- "Another account uses this mobile number.", for `01019190001`, which matches the stored `+201019190001`, and for the legacy
  stored `01000000011`;
- "This employee code is taken." (`LAB-ADMIN`).

Changing a field clears its error. Nothing was created for the duplicate attempts.

### A-6 Registration — PASS

| Check | Result |
| --- | --- |
| REG-206 | CREA, then RENAL (EGFR + CREA): the visit is accepted with the warning "RENAL repeats tests already on this visit: CREA. Check that the repeat is intended." Saved as ACC-2026-00047 |
| REG-109 | RENAL, then CREA: the CREA line reads "CREA: Every test of this service is already on the visit: CREA (RENAL)." and the bar says "Some services were refused…". It names the test and the profile it came through. CREA was removed and the visit saved as ACC-2026-00048 |
| BIO | ACC-48 with *Biohazard* on: the label frame has `<b class="biohazard">BIO</b>`; the Arabic job order shows "خطر بيولوجي" |
| Tubes, English | "1 tube was issued." (ACC-45), "2 tubes were issued." (ACC-49), "4 tubes were issued." (Walk-through ACC-2026-00003) |
| Tubes, Arabic | "صدرت عينة واحدة." (WT ACC-04), "صدرت عينتان." (WT ACC-05, ACC-06), "صدرت ٣ عينات." (WT ACC-07) |
| Expected time | English "13 Oct 2026, 15:58"; Arabic "١٢‏/١٠‏/٢٠٢٦، ١٠:١٩ م" |
| Branch in header | "Type to find a service… F10 saves. · Main Branch" ("· North Branch" after the change of branch, "· Walk-through Laboratory") |

### A-7 Results — PASS

As `uat.tech` (results.view, no See-VIP):

- **Accession list.** PAT-2026-00010 lists ACC-24 and ACC-25 as "VIP patient · PAT-2026-00010". ACC-2026-00018 finds its
  visit, masked. A name search for "V14 Walk-in" does not return the VIP file.
- **Worklist** (window from 10 Oct): a search by ACC-2026-00024 or PAT-2026-00010 finds rows named "VIP patient". The name
  "V14 Walk-in Two" finds nothing. The CSV and the printed worklist also say "VIP patient".
- **Before a query** the list reads "Matching visits / Run a query…", with no count.
- **Arabic.** On-screen dates on the worklist and the accession list use Arabic-Indic digits ("١١‏/١٠‏/٢٠٢٦، ١٠:٣٤ ص"). The
  report preview, and the report frames printed in §B-10, keep Latin digits ("11/10/2026, 10:34").
- **Opening the visit** shows the real name (F-06). Ages and percentages stay in Latin digits (F-09).

### A-8 eGFR — PASS

- **Formula.** The demo EGFR test was edited in the test editor (allowed). The formula became
  `ROUND(175 * [CREA] ^ -1.154 * @AGE ^ -0.203 * IF(@FEMALE, 0.742, 1), 0)` and the decimals 2 → 0.
  - "Check formula" answered "Valid: … — reads CREA."
  - The unit "mL/min/1.73 m²" is not offered: the laboratory's units are g/dL, IU/L, mg/dL and mmol/L. The unit stays empty.
- **Audit.**
  - `audit.audit_trail` seq 639 `LabTestChanged` and seq 640 `LabTestFormulaChanged` (with the expression);
  - `audit.field_changes`: FormulaExpression before/after, Revision 1 → 2, Decimals 2 → 0.
- **Results** with CREA 1.0 (`app.results`):
  - V19 eGFR Female, 30 y, ACC-2026-00047: EGFR **65**;
  - V19 eGFR Male, 45 y, ACC-2026-00048: EGFR **81**.
- **The brief expected 53 and 65.**
  - The formula gives 175 × 30^-0.203 × 0.742 = 65.10 → 65 and 175 × 45^-0.203 = 80.80 → 81.
  - 53 matches neither case, so the application is right and the expectation was wrong.

### A-9 Egyptian mobile — PASS

On the Patients editor with the mobile country **Egypt (10)**, whose hint reads "10 digits, with or without the leading 0…":

- PAT-2026-00023 typed `01019199921` is stored as `01019199921`.
- Editing it to the 10-digit `1019199921` saves without a change: the row is unchanged and `modified_at` stays empty.
- PAT-2026-00024 typed `1019199924` is stored as `01019199924`.
- The Patients search finds each file typed either way.

With the default country "Any", the number is stored as typed (F-08).

### A-10 Lock-out on Change password — PASS (`v19c.lock`)

| Step | Counter (`failed_password_attempts`) |
| --- | --- |
| Signed in in the browser (session kept) | 0 |
| Node: 2 sign-ins with a wrong password: 401 `AUTH-002` | 2 |
| Profile › Change password, wrong current password × 2: 403 "The current password is not correct." | 4 |
| Third wrong current password: 403 "Too many wrong passwords: the account is locked for a while." | 5, `is_locked_out` true until 16:37:47 |
| Node: sign-in with the **correct** password while locked | 401 `AUTH-003` |

Security events, in order: `SIGN_IN_FAILED` × 2, `PASSWORD_CHANGE_FAILED` × 3, `USER_LOCKED_OUT`.

### B-1 Branch selection (CP-04) — PARTLY

- `v19c.two` (MAIN + NORTH) signs in and lands on "Choose your branch". MAIN was chosen; `POST /auth/select-branch` answered
  200 and the dashboard shows "MAIN · Main Branch".
- `identity.refresh_sessions` session `01a1303a-7cd3-7366-…` has `branch_id` MAIN.
- **No screen offers a change of branch** (F-05):
  - the user menu holds the user's name, the language and Sign out only;
  - the profile and the dashboard have no branch action.
- Opening `/account/select-branch?change=1&returnUrl=/registration/new` by URL marks MAIN "Current". Choosing NORTH opens
  Registration with "· North Branch".
- The DB shows the **same session row** with `branch_id` NORTH (modified 16:08:59).
- `wt.admin` (every branch) and `v14.reception` also got the branch choice at sign-in.

### B-2 Session timeout and screen lock — FAIL (the client case of F-01)

- `wt.admin` set Setup › Parameters › *Idle lock (minutes)* to 1 (Walk-through, 16:10:18) and reloaded the page.
- After 1 min 37 s and again after 3 min 34 s of no input, nothing had happened: no lock, no warning, no sign-out.
- The page then saved a registration at once (ACC-2026-00003, 16:14), so the session was still usable.
- The value was restored with *Use default* (30; see F-13).
- The pinned build has no idle code. The working tree now holds an untracked `frontend/src/app/core/auth/idle-watcher.ts`
  (16:28, "validation F-01"), which was neither tested nor part of the pinned build.

### B-3 Delay colours — PASS

- **Turnaround.** The test and service editors take whole hours (`duration_hours`, `turnaround_hours` are integers), so a
  1–2 minute turnaround cannot be set.
- **Thresholds instead.** On the Walk-through Laboratory, *Delay warning* was set to 1 % and *Late threshold* to 2 %. The
  legend reads "≥ 1% of the turnaround / ≥ 2% (late)". The worklist row classes of ACC-2026-00003 (6 h tests, registered
  16:14:28) then changed:

| Time | V14 Creatinine, V11 Glucose (6 h) | V11 Blood group, HBs (24 h) |
| --- | --- | --- |
| 16:14 (registration) | no colour, 0 % | no colour, 0 % |
| 16:18:04 | `delay-warn`, 1 % | none, 0 % |
| 16:20:52 | `delay-late`, 2 % | none, 0 % |

- Meanwhile the *Delayed* counter stayed 0 and *Delayed only* listed nothing (F-15).
- **Rights.** The Walk-through *Laboratory Administrator* role had no result rights, so its worklist was empty. Result rights
  (see/review/verify/edit/print on V11-CHEM, V11-HEMA, V14-BIO, V14-SERO) were added for the test and removed afterwards.
- **Restored.** Both thresholds were set back to "Use default" (80 / 100).

### B-4 Restricted-list and text results — PASS (Walk-through)

- **Restricted list.** V14 HBs antigen (ACC-2026-00003) is a select offering only "—", "Negative" and "Positive".
  - "Positive" was saved with F10: "1 result saved.", flag **PANIC A**.
  - DB: `text_value` Positive, flag Abnormal, `is_panic` true.
- **Text.** V11 Interpretation (FormattedText in profile V11-GLUP, ACC-2026-00004) opens "Formatted text result" with the two
  template paragraphs.
  - Both texts were replaced and saved.
  - DB: `formatted_report` holds both paragraphs. Glucose 5.4 and the formula 97 were saved in the same F10.

### B-5 Patient condition — PASS

- **Why another laboratory.** The demo and Walk-through laboratories hold no patient condition, and the client
  "Patient conditions" page is Release 2. The case was run in **V11 API Lab 261012123322**, which has "V11 Pregnant".
- **Set-up**, as its administrator:
  - role *V19 Patient Reader* (Patient files: View, Dashboards: View);
  - user `v19c.reader`;
  - patient V19 Condition Patient (F, 31 y, PAT-2026-00002) with the condition V11 Pregnant.
- **As `v19c.reader`** (no `catalogue.view`):
  - the Patients list has a *Condition* column showing "V11 Pregnant";
  - the file shows "Patient condition V11 Pregnant";
  - every call answered 200, including `GET /patients/conditions`.

### B-6 Cancel a service with a verified result — PASS (rule recorded)

- ACC-2026-00048: RENAL (CREA + EGFR) was verified by `lab.admin`, with an electronic signature.
- The cancel dialog states "Services with verified results cannot be cancelled." Yet the RENAL line is still selectable.
- Cancel with a reason answers **409** "The service has verified results and cannot be cancelled."
- The line and its results are unchanged.

### B-7 Paging — PASS

| List | Rows | With Rows = 20 | Page 2 | Requests |
| --- | --- | --- | --- | --- |
| Users (demo) | 21, after 11 V19 users | "Showing 1–20 of 21", buttons 1 and 2 | "21–21 of 21" (v19c.two) | `?page=2&pageSize=20`; Previous and Last work |
| Branches (Walk-through, status All) | 21, after 17 V19 branches | "Showing 1–20 of 21" | V19-B17 | paged in the browser, from one `pageSize=50` read |

The smallest page size offered is 20. The V19 branches were deactivated after the test.

### B-8 Reject with a reason — PASS

- See A-1: an empty or blank reason is refused in the dialog.
- The server refuses `reason: "   "` with **400 VALIDATION-400**.

### B-9 Keyboard shortcuts — PASS

| Key | Result |
| --- | --- |
| F10 | Saved a registration (ACC-45, `defaultPrevented`) and a results unit. On the dashboard, with no form, it is still prevented |
| Ctrl+S | Saved patient PAT-23 (comment "V19 saved with Ctrl+S", DB). On the dashboard it is prevented and reaches no browser action |
| Ctrl+K | Moves the focus to "Go to page… (Ctrl+K)" (prevented) |
| F2 | In the service entry, opens "Choose a service" (prevented) |
| Escape | Closes that dialog |
| F8 | Runs the query on Results by accession |
| Enter / Shift+Enter | Move between results, as the grid's hint says |

### B-10 Printing — PARTLY

| Document | English frame | Arabic frame |
| --- | --- | --- |
| Labels | `en/ltr`, Latin; "C ACC-2026-00047 12/10/2026, 15:58 … F · 30Y"; BIO on ACC-48 | `ar/rtl`, Latin digits |
| Job order | `en/ltr`, Latin; lines, sample, expected time, "Printed by" | `ar/rtl`, but **dates and amounts in Arabic-Indic digits** ("١٢‏/١٠‏/٢٠٢٦"; "١٥٠٫٠٠ EGP"), while age and visit number stay Latin (F-10) |
| Receipt | `en/ltr` (RCT-2026-00048) | `ar/rtl`, Latin digits (reprint RCT-48) |
| Invoice | — | `ar/rtl`, Latin digits (ACC-47) |
| Result report | ACC-48 after verification: `en/ltr`, "eGFR 81", "Verified by · electronic signature" | Captions in Arabic inside an **`en/ltr` frame** (report language English); Latin digits; "إعادة طباعة (طُبعت 2 نسخة)" (F-12, F-11) |

### B-11 Export CSV (`lab.admin`) — PASS

- `branches-2026-10-12.csv`: 2 rows;
- `roles-…`: 8 rows;
- `users-…`: 21 rows;
- `treasuries-…`: 2 rows;
- `treasury-access-requests-…`: 16 rows, later 18 with the new columns.

Each file has translated headers, a UTF-8 BOM and CRLF.

### B-12 Forced API requests — PASS

| Request (Node client) | Answer |
| --- | --- |
| `lab.admin` approves its own TRQ-2026-00016 / rejects it | **403 SOD-TRS-001** "The requester cannot approve (reject) their own request." |
| `lab.admin` reads the Walk-through patient / visit by id | **404 PAT-404** / **404 REG-404** (control: own visit 200) |
| Reception (`v19c.cash1`) `PUT /settings`, `POST /organization/branches`, `GET /settings` | **403 AUTHZ-001** (with a well-formed body; see F-17) |
| Reception approves TRQ-19 | **403 AUTHZ-001** |
| Must-change user `v19c.must` (temporary password): `GET /settings`, `GET /patients` | **403 AUTH-016**; `/auth/me` 200 |
| Payment on another user's approved device | **403 BIL-020** (A-1) |
| Treasury of another branch | **422 TRS-015** (A-1) |

### B-13 Smoke — PASS

Each page's title and refused calls (≥ 400) were checked, and the console was read between markers:

- **`lab.admin`, English:** all 27 menu entries. Titles are correct, there are no raw keys and no call ≥ 400.
- **`lab.admin`, Arabic:** the same 27 pages, all `rtl`, with Arabic titles. The only Latin words are data (names), key names
  ("Enter", "Ctrl") and "Africa/Cairo".
- **`uat.reception`, English:** 24 entries, with no Setup › Branches, Roles, Users or Parameters. No call ≥ 400.
- **Console:** no error from any page during the three runs.
  - At each sign-in the console logs the expected 401 of the `/login` start-up refresh;
  - it also logs a 404 for `GET /account/laboratory/logo`, because the demo laboratory has no logo (observation, §4).

## 3. Not tested, and why

| Item | Reason |
| --- | --- |
| A role save that keeps Release-2 keys, shown in the database | The only roles holding Release-2 keys are the two laboratory administrators. The demo one cannot be saved (F-07). The Walk-through one is the main administrator's role, which refuses losing a key and has nothing left to add. No role can be given a Release-2 key through the page. The client side is shown (message, keys sent back), and the user-override half passes. |
| A 1–2 minute turnaround | The editors take whole hours; the colours were shown with the thresholds instead (B-3). |
| eGFR unit mL/min/1.73 m² | Not in the laboratory's unit list; creating a unit was not part of the allowed change. |
| Patient condition on `v15.export` | The demo laboratory has no condition (and the client page is Release 2); run in V11 API Lab with an equivalent reader (B-5). |
| The idle-watcher in the working tree | Not in the pinned build under test (B-2). |

## 4. New findings

| Id | Sev | Finding | Evidence | Suspected cause |
| --- | --- | --- | --- | --- |
| **F-05** | **S3** | A user with several branches cannot change branch from any screen (CP-04 "change branch"). The user menu has only the language and Sign out. The page works when its URL is typed. | B-1 | `changeBranchUrl()` in `account.guards.ts` is documented "for the shell's user menu" but nothing calls it; `shell-topbar.component.html` has no entry. |
| **F-06** | **S3** | On Results by accession, a user without See-VIP sees the list masked, but opening the visit shows the real name ("V11 Walk-in Two · VIP"). The report preview shows it too. | A-7, `uat.tech`, ACC-2026-00018 | The visit/case DTO of the results page and the report document skip the VIP mask that the list and worklist queries apply. The business owner should say whether the printed report may carry the name. |
| F-07 | S4 | The demo laboratory's *Laboratory Administrator* role cannot be saved in the role editor: "Platform permissions cannot be granted in a laboratory." The matrix reads "76 of 70 permissions granted". | A-3, role `01a12494-905c-…` (101 keys incl. `platform.view/manage`, `tenancy.*`) | The seed (`seed-demo-lab`) gave the role platform-only keys. The editor keeps every key it does not show and sends them back; the counter counts them. Roles of provisioned laboratories (Walk-through: 70 of 70) are not affected. |
| F-08 | S4 | The mobile country defaults to "Any". A 10-digit Egyptian number typed without choosing Egypt is stored without the 0 (PAT-2026-00020 `1019199912`), and a search for `01019199912` finds nothing. | A-9 | As specified in PAT Q14 ("Any" keeps the number as typed), but the default makes it the common case. Suggest the laboratory's country as the default. |
| F-09 | S4 | Mixed digits on Arabic screens. Ages ("30س", "52 سنة" vs "٥٢ سنة"), the worklist percentage ("2%") and the Payments page dates and amounts ("12‏/10‏/2026، 3:58 م", "230.00") are Latin, while other dates are Arabic-Indic. | A-7, B-10 | The digit rule (dates in the language's digits) was applied per date pipe; the age, percent and Payments formatters were not switched. |
| F-10 | S4 | The Arabic **job order** prints dates and amounts in Arabic-Indic digits, against the rule that printed documents keep Latin digits. Labels, receipt, invoice and report keep Latin. | B-10 (demo ACC-48, Walk-through ACC-04) | The job-order document uses the on-screen date/number formatters. |
| F-11 | S4 | Arabic count wording: "٣ نتيجة" should be "٣ نتائج", "٢ وحدة تقرير" should be "وحدتا تقرير", "طُبعت 2 نسخة" should be "نسختان". The repeat-order dialog says "فترة التكرار 30 يوماً" with Latin digits. "(s)" plurals remain ("This role has 2 member(s).", see F-04). | A-7, B-10, A-6 | Dictionary entries without dual/plural forms. |
| F-12 | S4 | A result report printed from the Arabic interface has Arabic captions inside an `en`/`ltr` frame. The laboratory's report language is English, so the document mixes languages and direction. | B-10 (ACC-48 reprint) | The frame takes `lang`/`dir` from the report-language parameter; the captions come from the interface dictionary. |
| F-13 | S4 | *Use default* on Setup › Parameters stores the default value as an explicit override (row kept; the button still shows). The laboratory no longer follows a later platform default. | §6 (`sessionTimeoutMinutes`, `delayWarnPercent`, `delayLatePercent` now rows 30 / 80 / 100); also seen in the admin round | `resetToDefault()` sets the inherited value; there is no "remove override" request. |
| F-14 | S4 | After a refused role save, reopening the role editor still shows the old refusal ("The main administrator's role cannot lose permissions."). | B-3 restore, Walk-through role | The route-provided facade keeps the mutation problem across visits. |
| F-15 | S4 | The worklist *Delayed* counter and *Delayed only* filter use the expected time (100 %), while row colours use the laboratory's late threshold. With 2 % configured, two red "late" rows showed *Delayed 0*, and *Delayed only* was empty. | B-3 | Two definitions of "late"; harmless with the default 100 %. |
| F-16 | S4 | One-time effect of the per-person device id (O-10). On a browser used before the upgrade, only the first user who opens a cash page inherits the old id (here `lab.admin`). Other users' approvals on that browser no longer match. `uat.reception`'s TRQ-2026-00002 (`a0ab790c`) now answers `my-treasuries` = [] for its new id `4720d7e4`; they must request again. | A-1; Node `GET /billing/my-treasuries` | By design of the migration; needs a go-live/UAT note. The credentials file note on `uat.reception`'s approval is now stale. |
| F-17 | S4 | Validation runs before authorization. A reception user posting a malformed body to a manage endpoint gets **400 VALIDATION-400** with the field rules, not 403. | B-12 (`PUT /settings`, `POST /organization/branches` with incomplete bodies) | MediatR behavior order (validation before authorization) discloses request rules to unauthorized callers; low impact. |

Observations (no finding):

- **Logo 404.** `GET /api/v1/account/laboratory/logo` answers 404 for a laboratory without a logo, which logs a console error
  at every sign-in.
- **Cancel dialog.** The dialog lets a service with verified results be ticked; only the server refuses it (B-6).
- **Treasury access.** A reception user sees every treasury-access request of the laboratory; *My requests only* filters them.
- **Synthetic clicks.** Very fast scripted clicks on the result-rights matrix (no pause between boxes) saved only the last
  change. With 250 ms between clicks every box was saved. This is a test artefact, not a finding.

## 5. Database checks

| Check | Result |
| --- | --- |
| Treasury requests (A-1) | TRQ-17 Approved, comment, device `3fc47c21`; TRQ-18 Rejected, reason, device `cd29020b`; TRQ-19 Cancelled ("V19 test cleanup"); `branch_id` MAIN |
| Payments | RCT-47 `v19c.cash1`/`3fc47c21`; RCT-48, 49, 50 `lab.admin`/`a0ab790c` |
| Audit (A-2, A-8, A-3) | seq 629–640 as listed; field changes for EGFR; chain verified to 636 |
| Users | 11 V19 users in the demo laboratory and `v19c.reader` in V11 API Lab, with the chosen role, branches and must-change flags |
| Privileges | `uat.short` added keys 95 → 94 (25 Release 2 kept) → 95; demo admin role 101 keys unchanged; Walk-through admin role 95 keys (25 Release 2), result rights 0 → 4 → 0 |
| Patients (A-9) | Mobiles `01019199921`, `01019199924` (Egypt) and `1019199912` ("Any") |
| Results (A-8, B-4) | EGFR 65 and 81 (decimals 0); HBs Positive, panic; V11-NOTE two paragraphs; GLU 5.4 / formula 97 |
| ACC-48 | Results Verified, printed twice; RENAL line still live after the refused cancel |
| Sessions (B-1) | `v19c.two` session branch MAIN → NORTH (same row) |
| Lock-out (A-10) | Counter 2 → 4 → 5, locked until 16:37:47; events as listed |
| Settings | Walk-through `sessionTimeoutMinutes` 1 → 30; `delayWarnPercent` 1 → 80; `delayLatePercent` 2 → 100 |

## 6. Settings changed and restored

| Setting / record | Change | Restored |
| --- | --- | --- |
| Walk-through `sessionTimeoutMinutes` | default (30, no row) → 1 (16:10:18) | *Use default* 16:14:54. Stored as a row of 30 (F-13) |
| Walk-through `delayWarnPercent` / `delayLatePercent` | default → 1 / 2 (16:14:54) | *Use default* 16:22. Rows of 80 / 100 (F-13) |
| Walk-through *Laboratory Administrator* result rights | none → 4 groups, full rights (16:17) | removed (0 rules) |
| `uat.short` privileges | − reporting.export | + reporting.export (95 keys again) |
| Payer AXA Insurance, tier Silver, CASH-2026, NORTH-CASH names | V19 suffix | original names. Audit events remain, as intended |
| Language of `uat.tech`, `wt.admin`, `lab.admin` | العربية for the Arabic checks | English again (DB `preferred_language` en) |
| Demo test EGFR | formula with `@AGE` / `@FEMALE`, decimals 0 | **not restored**, as asked (allowed change) |
| BLDGRP list value A | Normal + Critical (refused, not saved) | nothing saved |

## 7. Data created (prefix V19, invented)

| Record | Where |
| --- | --- |
| Users `v19c.cash1`, `v19c.cash2` (Reception, MAIN), `v19c.two` (Reception, MAIN + NORTH), `v19c.lock` (Reception; locked by A-10, the lock has expired), `v19c.must` (Reception, must change, never changed), `v19c.pg1`…`v19c.pg6` (Technician, paging) | demo laboratory |
| Treasury requests TRQ-2026-00017 (Approved), 00018 (Rejected), 00019 (Cancelled) | demo laboratory |
| Patients PAT-2026-00019…00024 (V19 Pay Alpha, Pay Beta, eGFR Female, eGFR Male, Mobile Eleven, Mobile Ten) | demo laboratory |
| Visits ACC-2026-00045…00049 (RCT-2026-00047…00050); ACC-48 verified and printed | demo laboratory |
| Branches V19-B01…V19-B17 (created, then deactivated) | Walk-through Laboratory |
| Patient PAT-2026-00003 "V19 Tubes Many", visits ACC-2026-00003…00007 (unpaid), results on ACC-03/04 | Walk-through Laboratory |
| Role *V19 Patient Reader*, user `v19c.reader`, patient PAT-2026-00002 "V19 Condition Patient" (V11 Pregnant) | V11 API Lab 261012123322 |

The passwords of the V19 accounts are in the git-ignored `.local/dev-credentials.md` only. No notification was sent by this
run. The browser's `lis.device-id` map now holds per-user ids for the users who signed in (F-16).
