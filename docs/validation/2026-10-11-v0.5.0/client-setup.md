# Validation 2026-10-11 (v0.5.0) · Tester B · Client portal set-up (Release 1)

Build under test: **v0.5.0** (pinned production build). SPA `http://localhost:4300` (one tab created for this run, `tab-10`;
`127.0.0.1` and other tabs were not used) → validation API `127.0.0.1:5102`. Database `lis` on the private PostgreSQL
`127.0.0.1:5433`, read only as `lis_owner`. Laboratory: **Walk-through Laboratory** (tenant
`01a124d7-5e87-7d30-af34-12248755e2f1`), administrator `wt.admin`. At the start the tenant held the V11 set-up of the previous
campaign: 2 branches, 4 roles, 4 users, 3 clinical groups, 7 tests, 5 services, 3 payers, 2 price lists, 2 treasuries,
1 visit, audit trail sequence 1…150. Every record created in this run carries the prefix **V14**.
Time: 2026-10-11 23:05 to 2026-10-12 00:10 (UTC+3).

Test and report only: no application code, dictionary, test, script or migration was changed. The new test passwords were
written only to the git-ignored `.local/dev-credentials.md`.

References:

- SRS `docs/srs/src/srs.data.js` (CP-28/29, CP-32/33, CP-36…CP-48, CP-56, BR-xx);
- intakes `LIS-INT-{ORG,CAT,CTB,PRC,TRS,REG}-001`;
- `frontend/src/app/shell/nav.registry.ts`;
- the previous campaign `docs/validation/2026-10-11/client-setup.md` (v0.3.8, B-01…B-14) and its re-tests
  `retest-client.md` and `retest-client-round2.md`.

Method notes:

- **Clicks and input.** The Browser pane was hidden, so clicks and input were dispatched from the page context
  (`element.click()`, `input` / `change` / `blur` events). This runs the same Angular handlers as a user. Checkboxes were
  ticked with a 150 ms pause between ticks.
- **Navigation between editors** (B-05 re-test) used the browser history: `history.back()` / `forward()`, and a
  `pushState` + `popstate` pair to put two editor URLs next to each other. This is what the browser does on back/forward.
- **CSV downloads** were intercepted in the page (`URL.createObjectURL`, anchor `download`). The bytes were read to check
  the BOM. Nothing was written to disk.
- **No forced requests.** No token was placed in the page. Refusals were read from the page and from the network log.
- **Database checks.** Every save was followed by a read-only query as `lis_owner` (§4).
- **Harness slip, corrected.** In TS-04 my own helper filled the first band row instead of a new one. The Male band of
  V14-CREA was saved as 30–60 Y / 70–110 for about one minute. I restored it through the page (62–106, 18–150 Y) and
  checked the database. The extra `LabTestRangesChanged` events in §4 come from this.
- **Working tree changed during the run.** Someone else edited about 90 files of the working tree during the run
  (uncommitted fixes, for example `profile.page.ts`, `UserCommands.cs`, a new migration `GrantExportPermissions`). The SPA
  under test is the pinned v0.5.0 build, so the results hold for v0.5.0. The root-cause references in §5 were read in the
  working tree. All files cited there are unchanged from v0.5.0, except `profile.page.ts` (B-04), which is quoted from the
  `v0.5.0` tag.
- **One tab, several users.** Users were switched by signing out and in within `tab-10`. All users share the browser's
  device id `3757857f-…` (localhost origin).

## 1. Summary

| Cases | Executed | Passed | Failed | Blocked | Not tested |
| --- | --- | --- | --- | --- | --- |
| 213 | 210 | 201 | 9 | 0 | 3 |

"Executed" = passed + failed. The 9 failed cases map to **9 new defects** (B-01…B-09; B-01 also marks BRN-11 as PASS with a note): **0 × S1, 0 × S2, 3 × S3,
6 × S4** (§5). All 14 defects of the previous campaign stay fixed (§6). Of the earlier observations, the Release-2
"Contract pricing" action is gone (resolved); price-list activation now shows a toast but still asks no confirmation (O-1).

Outbox and audit for the tenant during the run:

- 156 events written, 156 processed, 0 dead-lettered, maximum 0 retries;
- audit trail 151…306 with no gap (306 entries in all, chain contiguous);
- no password, hash or security stamp in `audit.field_changes` or in the outbox payloads (DB-20).

**Headline.** The full set-up can be built and changed through the R1 pages, and the result drives operations end to end:

- `v14.reception` is a new user with a new role. It signed in for the first time, changed its password, chose a branch and
  was approved on a new treasury by another user. It then registered walk-in visit **ACC-2026-00002**:
  - the price came from the new list V14-PL26 (package V14-PKG, 300.75);
  - the doctor was branch-filtered (V14-DR02 is not offered on MAIN);
  - the new preparation questions were asked (diuretics option list, smoker Yes/No for a male patient);
  - the tubes were split by the new groups and sample types: B · V14 Li-heparin plasma · Room temperature, and
    S · V11 Serum;
  - the cash payment of 300.75 was posted on the new treasury V14-MAIN-CASH (RCT-2026-00002).
- `v14.tech` is a new technician. It entered and reviewed the results of the new group V14-BIO:
  - creatinine 130 µmol/L was flagged **H** against the new male band 62–106;
  - the new formula computed 1.47 mg/dL;
  - the new formatted-text test was filled from its paragraph templates;
  - the V14 Serology result stayed read-only, as the role's see-only right requires.

The business rules checked hold in the pages and in the database: codes, uniqueness, ORG-006/015/016/017/021/102/103/104/110,
CAT-011/021/029/053/054/058/060/063/070/071/100, PRC-012/020/021/100/103, TRS-012, SoD, BR-17, BR-19, REG-100.

The new findings are smaller than last time:

- **B-01 (S3).** After a refused governorate (ORG-017), correcting the *country* does not clear the error. Save then does
  nothing and shows nothing.
- **B-02 (S3).** On *Add user*, unticking "Every active branch" and ticking no branch creates a user with access to
  **every** branch. The editor refuses the same state.
- **B-05 (S3).** In the price grid, picking a service that is already on the list leaves a hidden filter. The grid shows
  "1 of 1" with an empty filter box, and Refresh keeps it.

## 2. Coverage (page × function → cases)

The R1 client set-up routes come from `nav.registry.ts`:

- Setup group: Branches, Roles & privileges, Users, Preparations, Comment library, Doctors, Microbiology, Clinical groups,
  Services, Tests, Service sequence, Sample types, Units of measurement.
- Finance group: Treasuries, Treasury access, Payers & contracts, Base price lists.

R2 routes (parameters, contract pricing, diagnoses, sending labs and the rest) were not tested.

| Page (SRS) | Functions | Cases |
| --- | --- | --- |
| Branches CP-36 | required, code format, currency default and VALIDATION-400, e-mail ORG-016, phone ORG-015, governorate/country ORG-017 (create and edit), duplicate ORG-100, edit, deactivate, last active ORG-110, reactivate, status filter, search EN/AR, sort, export with filter, Arabic | BRN-01…19 |
| Roles & privileges CP-37 | required, permission needed, new export keys, All row, module filter, special rights (cap ORG-006, pay, cancel), result rights per group and branch, review⇒see, rename, revoke all, discard, (de)activate, members rule, change confirm with members, BR-17, search, status, sort, export, Arabic | RL-01…17 |
| Users CP-38 | required, formats, password rules, end date, duplicates, branch choice on create / edit, role kept (B-14), privileges override / restore, reset password, lock / unlock + sign-in, (de)activate, own account, filters, search, export with branch codes, must-change gate, branch selection, Arabic, paging | US-01…24 |
| Clinical groups CP-39 | required, formats, create, duplicate, edit, (de)activate, export with Active filter, search EN/AR | CG-01…09 |
| Sample types CP-43 + conditions | required, create, duplicate, conditions (duplicate CAT-011, remove row, deactivate), inactive condition not offered, edit, (de)activate, search, export | ST-01…07 |
| Units CP-44 | required, create, duplicate ignoring case, case-only rename, paging over 20 rows, sort across pages, search across pages, deactivate, export | UN-01…09 |
| Tests CP-41 | numeric + bands + panic + timing, partial refusal kept per tab (CAT-070 / 054 / 053), restricted list CAT-058 / 060, formula (CAT-063, unknown, self, picker, normalise), formatted text, type change CAT-071, back/forward between editors, filters, search EN/AR, filtered export, (de)activate, read-only view | TS-01…17 |
| Services CP-40 | required, shortcut uniqueness, CAT-021, test / profile / package, CAT-029, duplicate, back/forward between editors, members and in-sum flag, (de)activate, search (shortcut, alias, Arabic, short name), type filter, export, read-only view | SV-01…14 |
| Service sequence CP-42 | duplicate, zero / decimal, swap in one save, discard, search, export, read-only view | SQ-01…07 |
| Preparations CP-46 | required, option list CAT-210, linked services picker, Yes/No + gender, duplicate, edit options, (de)activate, search EN/AR, answer-type filter, export (BOM) | PR-01…09 |
| Comment library CP-47 | required, create with / without group, duplicate, edit, (de)activate, search, export | CM-01…07 |
| Doctors CP-48 | share 0–100, branches picker, duplicate, edit branches, (de)activate, search name / Arabic / phone, export with codes, branch filter at registration | DR-01…08 |
| Microbiology CP-56 | drug types, commercial names, antibiotics, organisms (de/reactivate, export), sensitivity values (order ≥ 0, duplicate) | MB-01…05 |
| Payers & contracts CP-32 | required, portal username, key normalisation, PRC-100, non-default Cash payer, edit, tiers (add, required, PRC-103, rename, rename to existing, deactivate), no R2 action, Cash rules PRC-020 / 021, (de)activate, search, export, active tiers at ordering | PY-01…16 |
| Base price lists CP-33 | required / year, copy (code proposal, PRC-012, ×1.1 round 0.5, ×1), add services, price validation, duplicate add, save, activate, grid export, Apply, edit, deactivate confirm, search / year, list export, cash price | PL-01…16 |
| Treasuries CP-28 | required, create with branch, duplicate, edit (code / branch read-only), deactivate → picker, reactivate → picker, search EN/AR, export with `treasury.export`, viewer | TR-01…09 |
| Treasury access CP-29 | required, own request (SoD), cancel, request, TRS-012, approve by another user, reject, Arabic | TA-01…08 |
| Read-only and guard | guard after a role change, export by permission | RO-01…02 |
| Proof (CP-12, CP-06, CP-18, CP-19) | price query, registration on the redesigned page, duplicate package member, worklist, result entry / formula / text, see-only group, review | PF-01…07 |
| Arabic | every set-up page, field messages in Arabic, shell | AR-01…03 |

## 3. Test cases

Verdicts: PASS, FAIL (defect id), NT (not tested). "DB-nn" points to §4. Persona `wt.admin` on MAIN unless stated.

| Id | Steps | Expected (source) | Observed | Verdict |
| --- | --- | --- | --- | --- |
| BRN-01 | Add branch, clear Currency, Save | required fields marked (CP-36) | "This field is required." on Code, Name EN, Name AR, Currency | PASS |
| BRN-02 | Open Add branch | currency starts at EGP (old B-01) | Currency = EGP | PASS |
| BRN-03 | Code `v14 east!` | upper-cased, format refused | `V14 EAST!`, "Use upper-case letters, digits, '-', '_' or '.', starting with a letter or digit." | PASS |
| BRN-04 | Currency `EG`, Save | VALIDATION-400 under the field | 400; "Exactly 3 characters." under Currency | PASS |
| BRN-05 | E-mail `not-an-email` | ORG-016 under the field (old B-03) | 422; "The e-mail address is not valid." under E-mail | PASS |
| BRN-06 | Phone `09-ab` | ORG-015 under the field | 422; "The phone number must have at least 5 digits and at most 20 characters…" under Phone | PASS |
| BRN-07 | Country Jordan + governorate Cairo — Egypt | ORG-017 (old B-02) | 422 ORG-017; "The governorate must belong to the chosen country." under Governorate | PASS |
| BRN-08 | After BRN-07, change **Country** to Egypt (the governorate's country), Save | saved | nothing is sent; no message; form stays `ng-invalid` with the stale server error on Governorate; saved only after the governorate is picked again | FAIL B-01 |
| BRN-09 | V14-EAST: names, Egypt / Cairo, phone, e-mail, EGP | "Branch saved.", Active | as expected | PASS · DB-01 |
| BRN-10 | Add `v14-east` again | ORG-100 | "A branch with this code already exists." | PASS |
| BRN-11 | Edit V14-EAST: code; country V11 Land; then Egypt + name + phone | code read-only; ORG-017; saved | code disabled; ORG-017 shown; same block as BRN-08 until Governorate was re-picked (Giza); then "Branch saved." | PASS (B-01) · DB-01 |
| BRN-12 | Deactivate V14-EAST | confirm, Inactive | "Users can no longer select this branch at sign-in…" → "Branch deactivated." | PASS · DB-02 |
| BRN-13 | Deactivate V11-NORTH, then MAIN | ORG-110 for the last active branch | "The last active branch cannot be deactivated."; V11-NORTH reactivated at once | PASS · DB-02 |
| BRN-14 | Status Inactive / All; Reactivate V14-EAST | filter and reactivation | 2 inactive rows; "Branch reactivated." | PASS · DB-02 |
| BRN-15 | Search `east`, `الشرق`, `zzz` | code and both names | 1 / 1 / "Nothing to show." (phone is not a search field) | PASS |
| BRN-16 | Code header twice | sort | ascending, then descending | PASS |
| BRN-17 | Export CSV with Inactive, then All | rows of the filter, localized (old B-09 / B-10) | `branches-2026-10-11.csv`: header only, then 3 rows with country / governorate names and Yes / No | PASS |
| BRN-18 | Arabic: list, empty save, `EG`, bad e-mail, `ab` phone, Jordan + Cairo, duplicate | rtl; Arabic field messages | "هذا الحقل مطلوب."، "3 أحرف بالضبط."، "عنوان البريد الإلكتروني غير صالح."، phone text, "يجب أن تتبع المحافظة الدولة المختارة."، "يوجد فرع بهذا الرمز." | PASS |
| BRN-19 | Paging | several pages | 3 rows < 20 | NT |
| RL-01 | Add role: Create empty; names without permission | required; ORG-005 hint | 2 fields required; "Grant at least one permission before creating the role." | PASS |
| RL-02 | Screen rights of a new role | export keys of v0.4.0 offered | 95 keys; "Branches and roles: Export" and "Treasuries and treasury access: Export" present; Special / Result rights tabs disabled until the role exists | PASS |
| RL-03 | V14 Reception: 16 keys incl. `treasury.export`; Create | "Role created." | as expected | PASS · DB-03 |
| RL-04 | "All" of Quality control on / off; filter `treas` | count 16 → 20 → 16; one row | as expected | PASS |
| RL-05 | Special rights: Custom cap 101, Save | ORG-006 | "The discount cap must be between 0 and 100." (no request) | PASS |
| RL-06 | Cap 20, Take payments, Cancel services, Save | saved with toast (old B-06) | "Special rights saved." ×3 | PASS · DB-03 |
| RL-07 | V14 Technician: 7 keys (results view / create / edit / approve …) | created | "Role created." | PASS · DB-03 |
| RL-08 | Result rights: MAIN → V14 Biochemistry Review / Edit / Print; untick See; tick again; Save. Every branch → V14 Serology See. V14 Reception: See on both groups | review ⇒ see; per branch | ticking Review ticks See; unticking See clears the row; "Result rights saved." ×3; "1 rule(s)", then "2 rule(s) configured" | PASS · DB-04 |
| RL-09 | V14 Temp role: rename | saved | "Saved."; heading renamed | PASS · DB-03 |
| RL-10 | Revoke all, Save; Discard changes | refused; restored | "An active role needs at least one permission."; back to "1 of 95" | PASS |
| RL-11 | Deactivate / Activate in the editor; Deactivate from the list | confirm + toasts | "Deactivated." / "Activated." / "Deactivated." | PASS · DB-03 |
| RL-12 | V11 Reception (members) | Deactivate disabled (ORG-011) | disabled; "A role with members cannot be deactivated." | PASS |
| RL-13 | V14 Reception (2 members): untick `pricing.view`, Save | confirm for all members | "This role has 2 member(s). The change applies to all of them. Continue?" → "Screen rights saved." | PASS · DB-03 |
| RL-14 | Sign in as `v14.reception` | BR-17: the change reaches the member | Payers & contracts and Base price lists missing from Finance; `/pricing/payers` refused by the guard (back to the dashboard); key restored afterwards (16 keys) | PASS |
| RL-15 | Search `tech`, `استقبال`, `zz`; status Inactive; sort | filters | correct rows; "No role matches the filter."; ascending / descending | PASS |
| RL-16 | Export with Inactive | rows of the filter | 2 rows (V11 / V14 Temp role), Active = No | PASS |
| RL-17 | Arabic list | rtl | rtl, Arabic names | PASS |
| US-01 | Add user → Create empty | required | 6 fields required | PASS |
| US-02 | `abc`, `Ab1!xyz`, `bad-mail`, mobile `12` | formats (ORG-023 / ORG-020) | "The value has the wrong format.", "At least 8 characters.", "Enter a valid e-mail address.", "Enter a valid mobile number." | PASS |
| US-03 | Confirm differs | refused | "The two passwords differ." (no request) | PASS |
| US-04 | Password `Xv14.reception#9` | ORG-021 | "The password must not contain the username." | PASS |
| US-05 | Access ends 2020-01-01 | ORG-022 | "The end date must be in the future." | PASS |
| US-06 | E-mail of v11.reception; mobile of v11.reception; username `v11.reception` | ORG-103 / 104 / 102 | "Another account uses this e-mail address." / "…this mobile number." / "This username is taken." (as a banner, not under the field) | PASS |
| US-07 | Untick "Every active branch of the laboratory", tick no branch, role V14 Reception, Create | refused, as the editor refuses it (US-09) | "User created."; `branch_ids` empty = **every** branch | FAIL B-02 · DB-05 |
| US-08 | v14.reception › Role & branches: MAIN + V14-EAST, Save | saved | "Branches saved." | PASS · DB-05 |
| US-09 | Untick every branch in the editor, Save | refused | "Choose at least one branch, or every branch." | PASS |
| US-10 | Choose V14 Technician (unsaved), press the branches Save, Cancel the confirm | role kept (old B-14) | "Change the role…" confirm; "The role was not assigned; the user keeps the previous role." + "Branches saved."; select back on V14 Reception | PASS |
| US-11 | v14.tech: employee code V14-T01, designation, shifts, MAIN, no must-change | created | "User created." | PASS · DB-05 |
| US-12 | v14.temp: every branch, language Arabic | created | "User created."; language `ar` stored | PASS · DB-05 |
| US-13 | v14.temp › Privileges: + `registration.export`, − `results.view`, Save; Restore to role | modified, then inherits | "Privileges saved for this user."; "Modified + registration.export − results.view"; confirm → "The user inherits the role again." | PASS · DB-05 |
| US-14 | Reset password: `short`; `xv14.temp-9911`; valid | ORG-020; ORG-021; reset, dialog closes (old B-06) | "At least 8 characters."; "The password must not contain the username."; "Password reset; the user's sessions were ended." and the dialog closed | PASS · DB-06 |
| US-15 | Lock v14.temp; sign out; sign in as v14.temp | AUTH-015, clean URL (old B-11) | "User locked."; "Your account was locked by an administrator. Contact your administrator to unlock it."; address `/login` | PASS · DB-06 |
| US-16 | Unlock | Active | "User unlocked." | PASS · DB-06 |
| US-17 | Deactivate / Activate v14.temp | status | "Deactivated." / "Activated." | PASS · DB-06 |
| US-18 | Open wt.admin | own account protected | "This is your own account…"; role select and Save disabled; "The main administrator keeps the administrator role." | PASS |
| US-19 | Role V14 Reception; status Locked; search `v14.t`, `01005551402`, `فني`, e-mail | filters | correct rows each time | PASS |
| US-20 | Export with role filter | branch codes (R1-06 residue) | `MAIN; V14-EAST` and `All`; status and flags translated | PASS |
| US-21 | First sign-in of v14.reception; open `/catalogue/tests`; change password | gate, then released (C-07) | redirected back to `/account/profile?mustChange=1`; "Your password was changed."; the page then **stays** on `?mustChange=1` with "Your administrator asked you to choose a new password before you continue." until the user navigates | FAIL B-04 |
| US-22 | v14.reception (2 branches) opens a page | branch selection (CP-04) | "Choose your branch": MAIN, V14-EAST | PASS · DB-06 |
| US-23 | Arabic: list, empty create, duplicate e-mail | Arabic | rtl; 6 × "هذا الحقل مطلوب."; "يوجد حساب آخر بهذا البريد الإلكتروني." | PASS |
| US-24 | Paging | several pages | 7 rows | NT |
| CG-01 | Add clinical group → Save empty | required | Code, Unit code, Names; order 0 by default | PASS |
| CG-02 | `v14 bio!`, unit `b b`, order −1 | formats (CAT-001 / 003 / 008) | format message on both; "The value must be at least 0." | PASS |
| CG-03 | V14-BIO (B, report name, 1); V14-SERO (S, 3) | created | "Saved." ×2 | PASS · DB-07 |
| CG-04 | `v14-bio` again | CAT-100 | "A clinical group with this code already exists." | PASS |
| CG-05 | Edit V14-SERO: code; report name, order 4 | code read-only; saved | disabled; "Saved." | PASS · DB-07 |
| CG-06 | Deactivate / Activate V14-SERO | confirm, status | "Deactivated." / "Activated." | PASS |
| CG-07 | Export with Active while V14-SERO inactive | only listed rows (old B-09) | 4 rows, V14-SERO absent, Yes / No | PASS |
| CG-08 | Search `bio`, `الأمصال`, `zz` | search | V14-BIO / V14-SERO / nothing | PASS |
| CG-09 | Arabic | rtl | rtl | PASS |
| ST-01 | Add sample type → Save empty | required | 4 fields | PASS |
| ST-02 | V14-PLS "Plasma"; again V14-PLS | created; duplicate refused | "Saved."; "A sample type with this code already exists." | PASS · DB-07 |
| ST-03 | Conditions: ROOM, COLD, `room` in one save | CAT-011 | "A condition code is used twice in this sample type…"; rows kept; after removing the 3rd row "The conditions were saved." | PASS · DB-07 |
| ST-04 | Reopen; untick COLD Active; Save | deactivated; codes read-only | codes disabled; saved; list shows 1 condition | PASS · DB-07 |
| ST-05 | Test editor, sample type V14-PLS | only active conditions (old B-08) | "ROOM · V14 Room temperature" only (also in the service editor) | PASS |
| ST-06 | Edit short name; Deactivate; Activate | saved, status | "Saved.", "Deactivated.", "Activated." | PASS · DB-07 |
| ST-07 | Search `heparin`, `بلازما`; Export | search, file | 1 / 1; CSV with Conditions count | PASS |
| UN-01 | Add unit → Save empty | required | "This field is required." | PASS |
| UN-02 | `V14 µmol/L`, `V14 mg/dL` | created | "Saved." ×2 | PASS · DB-07 |
| UN-03 | `v14 µMOL/l` | unique ignoring case | "A unit with this name already exists." | PASS |
| UN-04 | Rename `V14 mg/dL` → `V14 MG/DL` → back | own name in another case allowed | "Saved." ×2 | PASS |
| UN-05 | 17 more units (V14 PG-01…17), filter All, page size 20, Next page | paging | "Showing 1–20 of 22", then "21–22 of 22" | PASS |
| UN-06 | On page 2, sort Unit descending | the whole list sorted | page 2 shows `V14 µmol/L, V14 PG-17`: only the 2 loaded rows were reordered | FAIL B-03 |
| UN-07 | Search `PG-1` | across pages | "Showing 1–8 of 8" | PASS |
| UN-08 | Deactivate the 17 PG units | confirm each | 17 × Inactive; Active filter back to 4 rows | PASS · DB-07 |
| UN-09 | Export | file, BOM | `units-2026-10-11.csv`, BOM present | PASS |
| TS-01 | Add test → Save empty | required | Code, Clinical group, Names | PASS |
| TS-02 | V14-CREA Numeric (V14-BIO, µmol/L, 0 dec., V14-PLS / ROOM, LOINC 2160-0, Jaffe): bands Male 18–150 Y 62–106 (panic 20 / 800, rep. 5 / 3000), Female 44–80, Any 0–18 30–90; timing 4 / **minimum 10** | refused section marked, input kept (old B-04) | "The test was created, but part of its configuration was refused — correct the tab marked "!"…"; tab "Timing !" with the CAT-070 rule; 4 / 10 kept; 3 bands stored | PASS · DB-08 |
| TS-03 | Timing 6 / 2 | saved | "Saved." | PASS · DB-08 |
| TS-04 | Add Male 30–60 (overlap) + delta 20 % | CAT-054, bands kept | "Part of the changes was saved…"; "Reference ranges !" "Two bands of the same sex and condition cover the same ages."; 4 rows kept; delta saved | PASS · DB-08 |
| TS-05 | Remove the 4th band; Female panic low 50 (inside 44–80) | CAT-053 | "The changes were refused…"; limits rule shown; value kept; panic 20 → "Saved." | PASS · DB-08 |
| TS-06 | V14-HBS Restricted list: Negative (normal), Positive (critical, #CC0000, Default), "—"; Placeholder unticked | CAT-058 (BR-19) | created; "List values !" "Only a placeholder can be the default…"; 3 values kept; with "—" default + placeholder → "Saved." | PASS · DB-08 |
| TS-07 | "—" also Critical | CAT-060 | "A placeholder is never critical." | PASS |
| TS-08 | Positive both Normal and Critical | (no rule in the intake) | saved (O-2) | PASS |
| TS-09 | V14-CREAMG Formula: `[V14-CREA] /`; `[NOPE] * 2`; picker inserts `[V14-CREA]`; `round([V14-CREA] / 88.4, 2)` | CAT-063 once with position (old B-13); unknown; normalised | "(position 13) — The formula ends too early."; "Unknown or inactive test code(s): [NOPE]."; "Valid: ROUND([V14-CREA] / 88.4, 2) — reads V14-CREA."; "The test was created." | PASS · DB-08 |
| TS-10 | `[V14-CREAMG] * 2` on itself | self refused | "A formula cannot read its own test." | PASS |
| TS-11 | V14-COMM Formatted text, 2 paragraphs with templates | created | "Paragraphs 2" | PASS · DB-08 |
| TS-12 | V14-COMM type → Restricted list, Save | CAT-071 | "General !" "Remove the configuration of the current type … before changing the test type." | PASS |
| TS-13 | Editors V14-COMM ↔ V14-CREA through history (back / forward) | page follows the address (old B-05) | heading, code and tabs follow the address each time | PASS |
| TS-14 | Type Formula; group V14-BIO; search `creatinine`, `كرياتينين`, `hbs` | filters | GLUMG + CREAMG; 3 tests; 2; 2; 1 | PASS |
| TS-15 | Export with group V14-BIO | filtered, localized | 3 rows; type, group, unit, sample names | PASS |
| TS-16 | V14-COMM Deactivate / Activate | confirm, status | "Deactivated." / "Activated." | PASS · DB-08 |
| TS-17 | v14.reception opens V14-CREA | read-only (old B-12) | 67 / 67 inputs disabled; no Save; Add band disabled | PASS |
| SV-01 | Add service → Save empty | required | 6 fields | PASS |
| SV-02 | V14-CREA with shortcut 1101 | shortcut unique | "This shortcut number is used by another service." | PASS |
| SV-03 | Shortcut 1401, members V14-CREA + V14-CREAMG | CAT-021 | "The members do not fit the service type…"; nothing created | PASS |
| SV-04 | Remove CREAMG; Save (aliases `CREAT, V14KIDNEY`, precaution, ROOM, TAT 6) | created | "The service was created."; condition list ROOM only | PASS · DB-09 |
| SV-05 | V14-HBS (1402, V14-SERO, V11 Serum) | created | as expected | PASS · DB-09 |
| SV-06 | V14-RFT Profile without sample type; then V14-PLS / ROOM | CAT-029 | "This field is required."; then created with 3 members | PASS · DB-09 |
| SV-07 | V14-PKG Package (no sample fields) with V14-RFT + V14-HBS | services as members | created; members of kind Service | PASS · DB-09 |
| SV-08 | `v14-crea` again | duplicate | "A service with this code already exists." | PASS |
| SV-09 | V14-PKG → V14-RFT editor through history; back / forward; Deactivate (cancel) | follows the address (old B-05) | V14-RFT / V14-PKG / V14-RFT shown; confirm names V14-RFT | PASS |
| SV-10 | V14-RFT: "In sum" on CREAMG; precaution edited | saved | "Saved." | PASS · DB-09 |
| SV-11 | V14-HBS Deactivate / Activate | confirm, status | "Deactivated." / "Activated." | PASS · DB-09 |
| SV-12 | Search `1402`, `v14kidney`, `الكلى`, `HBsAg`; type Package | search, filter | HBS / CREA / RFT + PKG / HBS; V11-PKG + V14-PKG | PASS |
| SV-13 | Export with type Package | filtered file | 2 rows, 13 columns | PASS |
| SV-14 | v14.reception opens V14-RFT | read-only | 28 / 29 inputs disabled (the member search stays enabled, harmless) | PASS |
| SQ-01 | V14-PKG 8, V14-RFT 8 | CAT-105 | "Some sequences are used twice…"; Save disabled | PASS |
| SQ-02 | 0, then 2.5 | CAT-031 | Save disabled | PASS |
| SQ-03 | RFT 9 / PKG 8, Save | one save | "2 sequence(s) saved." | PASS · DB-09 |
| SQ-04 | HBS 99, Discard changes | restored | 7 | PASS |
| SQ-05 | Search `kidney` | search | 2 rows | PASS |
| SQ-06 | Export | file | Sequence, Code, Names, Type, Active | PASS |
| SQ-07 | v14.reception | read-only | 9 / 9 inputs disabled; only Refresh | PASS |
| PR-01 | Add preparation → Save empty | required | 3 fields | PASS |
| PR-02 | Option list `None`; `None / None` | CAT-210 | "An option list needs at least two different options." (no request) | PASS |
| PR-03 | V14-MEDS options None / Daily / Occasionally, linked V14-CREA + V14-RFT (picker) | saved | "Preparation saved."; chips | PASS · DB-10 |
| PR-04 | V14-SMOKE Yes / No, Male, all services | saved | "Preparation saved." | PASS · DB-10 |
| PR-05 | `v14-meds` again | duplicate | "A record with this code already exists in this list." | PASS |
| PR-06 | Edit V14-MEDS: code; Occasionally → Weekly | read-only; saved | disabled; "None · Daily · Weekly" | PASS · DB-10 |
| PR-07 | Deactivate / Reactivate V14-SMOKE | status | "Deactivated." / "Reactivated." | PASS |
| PR-08 | Search `smoke`, `مدرات`, `diuretics`; answer type Yes / No | search, filter | correct rows | PASS |
| PR-09 | Export with Yes / No filter | localized, BOM | "Yes / No", "Male", "All services"; BOM present | PASS |
| CM-01 | Add comment → Save empty | required | 3 fields | PASS |
| CM-02 | V14LIP (later scoped to V14-BIO), V14ALL (all groups) | saved | "Comment saved." | PASS · DB-10 |
| CM-03 | `v14lip` again | duplicate | "A record with this code already exists in this list." | PASS |
| CM-04 | Edit V14ALL text (short name read-only); V14LIP group → V14-BIO | saved | "Comment saved."; group shown | PASS · DB-10 |
| CM-05 | Deactivate / Activate V14ALL | status | "Deactivated." / "Activated." | PASS |
| CM-06 | Search `lipaemic` | search | V14LIP | PASS |
| CM-07 | Export | file | 6 columns, group EN / AR | PASS |
| DR-01 | V14-DR01 share 150 | 0–100 (CAT-220) | "The value must be at most 100." | PASS |
| DR-02 | V14-DR01: phone, names, specialty, occupation, share 12.5, branches V14-EAST + MAIN | saved | "Doctor saved." | PASS · DB-10 |
| DR-03 | `v14-dr01` again; V14-DR02 without branches | duplicate; all branches | refusal text; "Doctor saved." | PASS |
| DR-04 | Edit V14-DR02: + V14-EAST | saved | "V14-EAST" | PASS · DB-10 |
| DR-05 | Deactivate / Reactivate V14-DR02 | status | "Deactivated." / "Reactivated." | PASS |
| DR-06 | Search `karim`, `هالة`, `01005551499` | search | DR01 / DR02 / DR01 | PASS |
| DR-07 | Export | branch codes (old B-09) | `V14-EAST \| MAIN`, `All branches` | PASS |
| DR-08 | Registration on MAIN (v14.reception) | only doctors of the branch | V11-DR01, V11-DR02, V14-DR01; V14-DR02 (V14-EAST only) absent | PASS |
| MB-01 | Drug types: empty; "V14 Cephalosporins"; `v14 CEPHALOSPORINS` | required; unique name | 2 fields; "Drug type saved."; "A record with this name already exists in this list." | PASS · DB-10 |
| MB-02 | Commercial name V14 Zinnat → V14 Cephalosporins | saved | "Commercial name saved." | PASS · DB-10 |
| MB-03 | Antibiotic V14-CXM / CXM / V14 Zinnat | saved | "Antibiotic saved." | PASS · DB-10 |
| MB-04 | Organism V14-KPN: add, deactivate, All, activate, export | each works | as expected; CSV 2 rows | PASS · DB-10 |
| MB-05 | Sensitivity V14I order −3, then 3; `v14i` again | order ≥ 0; duplicate | "The value must be at least 0."; saved; duplicate refused | PASS · DB-10 |
| PY-01 | New payer → Save empty | required | 3 fields | PASS |
| PY-02 | Portal enabled, no web username | required when enabled | "This field is required." | PASS |
| PY-03 | V14 MedCare Insurance, key ` v14med `, Insurance, portal `v14.medcare.portal` | trimmed upper-case key | "Payer saved."; V14MED | PASS · DB-11 |
| PY-04 | Another payer with `v14med` | PRC-100 | "Another payer uses this search key." | PASS |
| PY-05 | Same form, key V14CASH2, type Cash | a non-default Cash payer is allowed (PRC intake Q19) | "Payer saved." | PASS · DB-11 |
| PY-06 | Edit it → "V14 Staff cash scheme" | saved | "Payer saved." | PASS · DB-11 |
| PY-07 | Tiers: empty add; V14 Gold; V14 Silver; `v14 gold` | required; PRC-103 | 2 fields; "Tier added." ×2; "The payer already has a tier with this name." | PASS · DB-11 |
| PY-08 | Rename V14 Silver → V14 Silver Plus | toast, stale error cleared (old B-06) | "Tier renamed."; the earlier PRC-103 text is gone | PASS · DB-11 |
| PY-09 | Rename V14 Silver Plus → `v14 GOLD` | PRC-103 | refusal shown; cancelled | PASS |
| PY-10 | Deactivate tier V14 Silver Plus | confirm, Inactive | "…will quote nothing until it is reactivated" → "Tier deactivated." | PASS · DB-11 |
| PY-11 | Row and tier actions | no R2 contract pricing (old O-6) | payer: Edit, Deactivate, Contract tiers; tier: Rename, Deactivate only | PASS |
| PY-12 | Cash: Deactivate; tiers; Edit type → Credit | PRC-020 / PRC-021 | "The default cash payer cannot be deactivated or change type." ×2; Default tier offers Rename only | PASS |
| PY-13 | Deactivate / Reactivate V14 Staff cash scheme | status | "Payer deactivated." / "Payer reactivated." | PASS · DB-11 |
| PY-14 | Search `medcare`, `ميدكير`, `v14med` | search | 1 row each | PASS |
| PY-15 | Export with search `v14` (old B-10) | file | 2 rows; tiers "V14 Gold \| V14 Silver Plus (inactive)" | PASS |
| PY-16 | Price query / registration contract list | active tiers only | V14 MedCare — V14 Gold; Silver Plus absent; payer without tiers absent | PASS |
| PL-01 | New list → Save empty; year 1999 | required; ≥ 2000 | 3 fields; "The value must be at least 2000." | PASS |
| PL-02 | Copy V11-PL26: accept the proposed code | a free code is proposed | proposal `V11-PL27`, which exists (inactive) → "A base price list with this code already exists." | FAIL B-06 |
| PL-03 | Copy, Add amount −500 | PRC-012 | "The copy would produce a price below 0 or above the maximum." | PASS |
| PL-04 | Copy ×1.1, round 0.5 → V14-PL29 (2029) | inactive copy, rounded | "List V14-PL29 created (inactive)."; 66 / 82.5 / 121 / 165 / 253.5 | PASS · DB-12 |
| PL-05 | Copy ×1, round 0.01 → V14-PL26 (2026) | identical prices | 5 lines copied | PASS · DB-12 |
| PL-06 | Add service V14-CREA, V14-HBS, V14-RFT, V14-PKG | lines at 0 | "… added — enter its price." ×4 | PASS |
| PL-07 | CREA −5, HBS 100.555; Save prices | refused by the page (PRC-006) | "Correct the highlighted prices first."; nothing sent | PASS |
| PL-08 | Pick V14-CREA again in "Add a service" | refused, list unchanged | "The service is already on the list."; the grid then shows only V14-CREA ("Showing 1–1 of 1") with an **empty** filter box; Refresh keeps it; typing in the filter and clearing it restores 9 lines | FAIL B-05 |
| PL-09 | 85 / 120 / 210 / 300.75; Save prices | saved | "Prices saved: 4 added, 0 changed." | PASS · DB-12 |
| PL-10 | Activate V14-PL26 | Active, prices cash | "List activated."; "Prices cash patients" (no confirm: O-1) | PASS · DB-12 |
| PL-11 | Grid Export CSV | file | `v14-pl26-prices-2026-10-11.csv`, 9 lines | PASS |
| PL-12 | Apply to contracts | confirm, toast | "Recompute the 0 contract tier(s)…" → "0 contract tier(s) applied, 0 price(s) changed." | PASS |
| PL-13 | Edit V14-PL29 name (code read-only) | saved | "List saved." | PASS · DB-12 |
| PL-14 | Deactivate V14-PL26 (cancelled) | confirm | "Apply will skip this list…" | PASS |
| PL-15 | Search `copy`, `PL29`, `v14 cash`, `نقدية V14`, `2029`; look for an export and a year filter | search code / name, year filter, export (PRC intake §screens, SRS "Grids & editors") | code and names found; `2029` finds nothing; no year filter; no Export on the lists page | FAIL B-07 |
| PL-16 | Price query: 1401, V14-HBS, V14-RFT under Cash; then V14 MedCare — V14 Gold | cash default list = highest year, latest created (Pricing Q2) | 85.00 / 120.00 / 210.00 (total 415.00); preparations listed; Gold: "Not priced under this contract" | PASS |
| TR-01 | Add treasury → Save empty | required | 4 fields | PASS |
| TR-02 | `v14-main-cash` on MAIN; V14-EAST-CASH on V14-EAST | upper-cased; branch `CODE · name` | rows with branch names | PASS · DB-13 |
| TR-03 | V14-MAIN-CASH again | TRS-100 | "A treasury with this code already exists." | PASS |
| TR-04 | Edit V14-EAST-CASH | code and branch read-only | both disabled; "Saved." | PASS · DB-13 |
| TR-05 | Deactivate V14-EAST-CASH, open Treasury access | not offered (TRS-010) | absent from the picker | PASS |
| TR-06 | Reactivate, open Treasury access (no reload) | offered (old B-07) | offered; also offered to v14.reception after a sign-out / sign-in | PASS · DB-13 |
| TR-07 | Search `east`, `الشرق`, `V14-MAIN` | code and both names | `east` and `V14-MAIN` found; `الشرق` (Arabic name "خزينة الشرق V14") → "Nothing to show." | FAIL B-08 |
| TR-08 | Export (wt.admin; v14.reception with `treasury.export` only) | with `treasury.export` | both download; 4 rows with branch names | PASS |
| TR-09 | v14.reception (no `treasury.manage`) | no write actions | no Add / Edit / Deactivate | PASS |
| TA-01 | Request access without a treasury | required | "This field is required." | PASS |
| TA-02 | wt.admin requests V14-EAST-CASH ("V14 admin desk") | own row: Cancel only (SOD-TRS-001) | TRQ-2026-00004 Pending, action `Cancel request` only | PASS · DB-13 |
| TA-03 | Cancel it | Cancelled | reason optional; "Request cancelled." | PASS · DB-13 |
| TA-04 | v14.reception requests V14-MAIN-CASH | Pending | TRQ-2026-00005, "Access requested." | PASS · DB-13 |
| TA-05 | Same request again | TRS-012 | "A pending or approved request already exists for this treasury on this device." | PASS |
| TA-06 | wt.admin approves TRQ-2026-00005 | another user approves | confirm → "Request approved."; `Revoke` offered | PASS · DB-13 |
| TA-07 | Reject with reason | reason mandatory | no pending request of another user was left | NT |
| TA-08 | Arabic | rtl | rtl, Arabic labels | PASS |
| RO-01 | v14.reception types `/pricing/payers` (after RL-13) and `/organization/users` | guard refuses | back to the dashboard both times | PASS |
| RO-02 | v14.reception on Treasuries and Tests | export follows the export keys | Treasuries: Export CSV (has `treasury.export`); Tests: no export (no `catalogue.export`) | PASS |
| PF-01 | wt.admin price query (PL-16) | new prices and preparations | as PL-16 | PASS |
| PF-02 | v14.reception on MAIN, redesigned registration: New patient "V14 Setup Walk-in" (Mr, Male, 45 y, Egypt mobile); Cash — Default; V14-DR01; service `1404` (V14-PKG); answers Daily / No; cash 300.75 on V14-MAIN-CASH; Save registration | the visit uses the new set-up | PAT-2026-00002. Members shown under the package. Tubes `B · V14 Lithium heparin plasma · V14 Room temperature` and `S · V11 Serum`. Checklist "4 of 4 done". Discount limit "20 %" (the role cap). "Registration saved ACC-2026-00002 … 2 tube(s) were issued. 300.75 received — receipt RCT-2026-00002." | PASS · DB-14 |
| PF-03 | Before saving: add `1401` (V14-CREA) and `V14-HBS` | REG-100 for a package member | V14-HBS: "The service is already on this visit."; V14-CREA (a test inside V14-RFT inside the package) was accepted (O-3); both lines removed before saving | PASS |
| PF-04 | v14.tech › Worklist | the visit's tests | 4 tests (CREA, CREAMG, COMM, HBS), sample Received, expected time from the test duration | PASS |
| PF-05 | Results by accession ACC-2026-00002: CREA 130; Text entry on V14-COMM (template paragraphs + one sentence); Save | flag against the male band, formula computed, text stored | "1 results saved.", "2 results saved."; CREA 130 **H**, range 62 – 106; CREAMG ƒ 1.47 | PASS · DB-15 |
| PF-06 | V14 Serology tab | see-only right | the HBS select is disabled; no Save / Review | PASS |
| PF-07 | Review on V14 Biochemistry | MAIN review right | 3 results Reviewed | PASS · DB-15 |
| AR-01 | Switch to العربية (saved preference); open all 17 set-up pages | rtl, Arabic labels | rtl and Arabic headings on all 17; no English UI words on the lists | PASS |
| AR-02 | Field messages in Arabic | per field (VALIDATION-400 `rules`) | see BRN-18 and US-23 | PASS |
| AR-03 | Shell in Arabic | role in Arabic | the sidebar shows "Laboratory Administrator" (also "V14 Technician" for v14.tech) while the users list shows "مدير المختبر" | FAIL B-09 |

## 4. Database verification

All queries ran as `lis_owner`, read only, against `lis`. `T` = `01a124d7-5e87-7d30-af34-12248755e2f1`. Every row listed
carries `tenant_id = T`. The queries are abridged; joins resolve ids to codes.

| Ref | Case | Table(s) | Query (abridged) | Result |
| --- | --- | --- | --- | --- |
| DB-01 | BRN-09/11 | app.branches, saas.countries, saas.governorates | `select b.code,b.name_en,b.name_ar,c.name_en,g.name_en,g.country_id=c.id,b.phone,b.email,b.currency,b.is_active,b.tenant_id=T …` | V14-EAST · "V14 East Branch (edited)" · فرع الشرق V14 · Egypt · Giza · `t` · 0225550333 · v14.east@example.test · EGP · active · `t`. The refused pairs (Jordan / Cairo, V11 Land / Cairo) were never stored |
| DB-02 | BRN-09…14 | audit.field_changes, audit.audit_trail, ops.outbox_events | `… where entity_id = <V14-EAST>` / `aggregate_id = …` | Field changes: Created (all fields), then Modified (country / governorate / name / phone, IsActive). Trail seq 151 `BranchCreated`; `BranchDeactivated` ×2 and `BranchReactivated` ×2 (V14-EAST and V11-NORTH in BRN-13). All processed. V11-NORTH ends Active |
| DB-03 | RL | app.roles | `select name_en,name_ar,cardinality(permission_keys),special_discount_cap_percent,special_can_pay,special_can_cancel,is_active,members …` | V14 Reception: 16 keys (billing.create/view, catalogue.view, dashboard.view, patients.create/edit/view, pricing.view, registration.create/edit/view, results.view, samples.view, treasury.create/export/view), cap 20, pay `t`, cancel `t`, 2 members. V14 Technician: 7 keys, 1 member. V14 Temp role (renamed): 1 key, inactive. Field changes cap 0→10→20, PermissionKeys without / with pricing.view (RL-13/14). Trail `RoleCreated` ×3, `RoleChanged` ×11; security events `PRIVILEGE_MATRIX_CHANGED` ×8 |
| DB-04 | RL-08 | app.role_result_rights | joined to groups and branches | V14 Reception: V14-BIO ALL see; V14-SERO ALL see. V14 Technician: V14-BIO **MAIN** see / review / edit / print (no verify); V14-SERO ALL see |
| DB-05 | US | identity.platform_users, app.tenant_memberships | `select u.username,u.email,u.mobile,u.must_change_password,u.is_locked_out,left(password_hash,10),m.employee_code,m.full_name_en,m.is_active,branch codes,cardinality(branch_ids),r.name_en,m.preferred_language,m.designation,m.shifts,m.added_permission_keys,m.removed_permission_keys …` | v14.reception: `$argon2id$`, MAIN,V14-EAST (2), V14 Reception, en. Right after US-07 the row had `branch_ids = {}` (every branch). v14.tech: V14-T01, MAIN (1), V14 Technician, "Biochemistry technician", {Morning,Night}, no must-change. v14.temp: every branch, V14 Reception, `ar`; during US-13 `{registration.export}` / `{results.view}`, after the restore `{}` / `{}`. Trail `TenantMembershipCreated` ×3, `…RoleChanged` ×3, `…PrivilegesOverridden`, `…RestoredToRole`, `…Deactivated`, `…Reactivated` |
| DB-06 | US-14…22 | audit.security_events | `select name,severity,count(*) … where at_utc > 23:05` | `MEMBERSHIP_CHANGED` info ×7 and **warning ×2** (password reset, lock). `SIGN_IN_FAILED` warning ×1 (locked v14.temp). `PASSWORD_CHANGED` ×1 (v14.reception). `USER_DEACTIVATED` ×1. `SESSION_CONTEXT_CHANGED` (branch) ×6; `SIGN_IN_SUCCEEDED` ×7; `SESSION_REVOKED` per sign-out |
| DB-07 | CG, ST, UN | app.clinical_groups, app.sample_types, app.sample_conditions, app.measurement_units | per table | V14-BIO B "V14 Clinical Biochemistry" 1 `t`; V14-SERO S "V14 Serology Unit" 4 `t`. V14-PLS "Li-Hep" `t`, conditions ROOM `t`, COLD `f`. Units: V14 mg/dL `t`, V14 µmol/L `t`, V14 PG-01…17 `f`. Trail `ClinicalGroupCreated` ×2, `…Changed`, `…Deactivated`, `…Reactivated`; `SampleTypeCreated`, `SampleConditionsChanged` ×2; `MeasurementUnitCreated` ×19, `…Changed` ×2, `…Deactivated` ×17 |
| DB-08 | TS | app.tests, app.test_range_bands, app.test_list_values, app.test_paragraphs | `select t.code,t.type,g.code,u.name,t.decimals,st.code,sc.code,t.methodology,t.loinc,t.duration_hours,t.min_duration_hours,t.frequency_check_days,t.frequency_action,t.delta_percent,t.delta_window_days,t.formula_expression,t.formula_references,t.is_active …` | V14-CREA Numeric V14-BIO µmol/L 0 dec. V14-PLS / ROOM, Jaffe, 2160-0, 6 / 2 h, 7 d Alert, 20 %, 60 d. Bands: Male 18–150 Y 62–106 (panic 20 / 800, rep. 5 / 3000); Female 18–150 44–80 (same); Any 0–18 30–90 (panic 15 / 500, "V14 paediatric"). V14-HBS RestrictedList: Negative normal; Positive critical #CC0000; "—" default + placeholder. V14-CREAMG `ROUND([V14-CREA] / 88.4, 2)` refs {V14-CREA}. V14-COMM 2 paragraphs. Trail `LabTestCreated` ×4, `…RangesChanged` ×4, `…TimingChanged` ×3, `…ListValuesChanged` ×2, `…FormulaChanged`, `…ParagraphsChanged`, `…Deactivated`, `…Reactivated` |
| DB-09 | SV, SQ | app.services, app.service_members | `select s.sequence,s.code,s.short_name,s.shortcut_no,s.type,g.code,st.code,sc.code,s.turnaround_hours,s.aliases,s.precaution,s.is_active …`; members joined | 6 V14-CREA 1401 Test V14-BIO V14-PLS/ROOM 6 h {CREAT,V14KIDNEY}; 7 V14-HBS 1402 Test V14-SERO V11-SER; 9 V14-RFT 1403 Profile V14-PLS/ROOM 8 h "(edited)"; 8 V14-PKG 1404 Package, no sample. Members: RFT = CREA 1, CREAMG 2 (in_sum `t`), COMM 3; PKG = Service RFT 1, Service HBS 2. Trail `ServiceCreated` ×4, `…Changed`, `…MembersChanged`, `…Deactivated`, `…Reactivated`, `ServiceResequenced` ×2 |
| DB-10 | PR, CM, DR, MB | app.preparations, app.comment_templates, app.doctors, app.drug_types, app.commercial_names, app.antibiotics, app.organisms, app.sensitivity_values | per table, ids resolved | V14-MEDS OptionList {None,Daily,Weekly} Any → V14-CREA,V14-RFT; V14-SMOKE YesNo Male, all. V14LIP → V14-BIO; V14ALL no group, "(edited)". V14-DR01 12.50 % MAIN,V14-EAST; V14-DR02 0.00 % V14-EAST. V14 Cephalosporins ← V14 Zinnat ← V14-CXM (CXM); V14-KPN; V14I order 3. All active, with matching Created / Deactivated / Reactivated trail events |
| DB-11 | PY | app.payers, app.contract_tiers | `select p.name_en,p.search_key,p.type,p.web_portal_enabled,p.web_username,p.is_active,p.is_default,tiers …` | V14 MedCare Insurance V14MED Insurance portal `v14.medcare.portal`, tiers V14 Gold `t`, V14 Silver Plus `f`. V14 Staff cash scheme V14CASH2 Cash, not default, no tiers. Trail `PayerCreated` ×2, `ContractTierAdded` ×2, `ContractTierDeactivated`, `PayerDeactivated`, `PayerReactivated`; the payer edit and the tier rename are in field changes only (O-5) |
| DB-12 | PL | app.base_price_lists, app.base_price_lines | `select l.code,l.name_en,l.year,l.is_active,copied_from,string_agg(code=price) …` | V14-PL26 2026 **active**, copied from V11-PL26: V11-BG 60, V11-GLU 75, V11-GLUP 150, V11-HGB 110, V11-PKG 230.50, V14-CREA 85, V14-HBS 120, V14-PKG 300.75, V14-RFT 210. V14-PL29 2029 inactive "(x1.1, edited)": 66 / 82.50 / 165 / 121 / 253.50. Trail `BasePriceListCreated` ×2, `…PricesChanged`, `…Activated` |
| DB-13 | TR, TA | app.treasuries, app.treasury_access_requests | `select r.number,t.code,b.code,requester,r.device_id,r.device_label,r.status,approver,decider,r.decision_reason …` | V14-MAIN-CASH (MAIN) `t`; V14-EAST-CASH "V14 East cashier desk" (V14-EAST) `t`. TRQ-2026-00004 wt.admin V14-EAST-CASH Cancelled (decided by wt.admin, no reason). TRQ-2026-00005 v14.reception V14-MAIN-CASH **Approved** by wt.admin. Both on device `3757857f-…`. Trail `TreasuryCreated` ×2, `…Deactivated`, `…Reactivated`, `TreasuryAccessRequested` ×2, `…Cancelled`, `…Approved` |
| DB-14 | PF-02 | app.visits, app.visit_lines, app.visit_tubes, app.payments, app.patients, app.visit_preparation_answers | `… where v.accession='ACC-2026-00002' and v.tenant_id=T` | ACC-2026-00002 · PAT-2026-00002 "V14 Setup Walk-in" Male 45 · MAIN · Cash / Default · V14-DR01 · Open · 300.75 / patient 300.75 / payer 0 / discount 0 · by v14.reception. Lines: V14-PKG Package 300.75; V14-RFT PackageMember 0 (3 tests, SMP-2026-00003, +8 h); V14-HBS PackageMember 0 (SMP-2026-00004, +24 h). Tubes: SMP-2026-00003 B V14 Biochemistry, V14 Lithium heparin plasma, V14 Room temperature; SMP-2026-00004 S V14 Serology, V11 Serum. RCT-2026-00002 300.75 Cash V14-MAIN-CASH, device 3757857f, v14.reception, MAIN, Posted. Answers V14-MEDS "Daily", V14-SMOKE "No". Trail `PatientRegistered`, `VisitRegistered`, `PaymentRecorded`, `SampleCollected` ×2, `SampleReceived` ×2 |
| DB-15 | PF-05/07 | app.results | `select t.code,r.status,r.numeric_value,r.unit,r.decimals,r.range_low,r.range_high,r.range_panic_low,r.range_panic_high,r.flag,r.formatted_report,entered_by,reviewed_by …` | V14-CREA Reviewed 130 V14 µmol/L, 0 dec., range 62–106, panic 20 / 800, flag **High**. V14-CREAMG Reviewed 1.47 V14 mg/dL. V14-COMM Reviewed, paragraphs Interpretation (edited) + Recommendation. All entered and reviewed by v14.tech. V14-HBS Ordered. Trail `ResultEntered` ×3, `ResultReviewed` ×3 |
| DB-16 | all | ops.outbox_events | `select count(*),count(processed_at_utc),count(dead_lettered_at_utc),max(attempts) … where tenant_id=T and occurred_at_utc > '2026-10-11 23:05+03'` | 156 / 156 / 0 / 0 |
| DB-17 | all | audit.audit_trail | `count(*),min,max,count(distinct sequence)`; gap check with `lag()` | 156 rows, sequence 151…306; whole tenant 306 rows, max 306, 0 gaps |
| DB-18 | all | audit.field_changes | `select entity_type,action,count(*) … group by 1,2` | e.g. Branch Created 1 / Modified 5; LabTest 4 / 13; RangeBand Created 12 / Deleted 9; ServiceMember Created 10 / Deleted 3; PriceLine Created 14; PlatformUser Created 3 / Modified 4; TenantMembership Created 3 / Modified 7; Visit, VisitLine ×3, VisitTube ×2, Payment, VisitPreparationAnswer ×2, Result 4 / 10 |
| DB-19 | US-12, AR | app.tenant_memberships | `preferred_language` of wt.admin, v14.* | wt.admin `en` (switched to `ar` for AR-01 and restored); v14.temp `ar`; others `en` |
| DB-20 | US, PF | audit.field_changes, ops.outbox_events | `count(*) where changes::text ilike '%PasswordHash%' or '%argon2%' or '%SecurityStamp%' or the V14 passwords` | 0 and 0: no secret in the audit or the outbox |

## 5. Defect log

| Id | Sev | Case | Steps | Expected | Observed | Evidence | Suspected root cause |
| --- | --- | --- | --- | --- | --- | --- | --- |
| B-01 | S3 | BRN-08, BRN-11 | Branches › Add (or Edit): choose a country and a governorate of another country, Save (ORG-017 under Governorate). Correct the **country** to the governorate's country, Save | the corrected pair is saved | nothing is sent and nothing is shown. The form stays `ng-invalid` because Governorate keeps the server error. The error text is the same sentence as the field's hint ("The governorate must belong to the chosen country."), so the user cannot tell it is an error. It saves only after the governorate is picked again (even the same value) | network: 1 × 422 ORG-017, then no request; form class `ng-touched ng-dirty ng-invalid` | `features/organization/pages/branches.page.ts` l.24 maps ORG-017 (a two-field rule) to `governorateId` only. `problemToFormErrors` (`shared/forms/problem-to-form-errors.ts`) sets a `server` error that clears only when that control's value changes. `MasterDataPageComponent.save()` (`shared/ui/master-data-page/master-data-page.component.ts` l.222–228) returns silently when `markAllTouched(form)` is false. Fix idea: clear the governorate's server error when the country changes, or put the ORG-017 message on both fields |
| B-02 | S3 | US-07 | Users › Add user: untick "Every active branch of the laboratory", tick no branch, fill the rest, Create user | refused with "Choose at least one branch, or every branch.", as the editor does (US-09) | "User created."; the membership is stored with `branch_ids = {}`, which means **every active branch** (ORG intake INV-ORG-017). The user receives wider access than the administrator chose | DB-05 (right after US-07) | `features/organization/pages/user-editor.page.ts` l.304 sends `branchIds: this.allBranches() ? [] : [...this.branchChoice()]` on create without the check that `saveBranches()` makes at l.375–376 (`!allBranches() && branchChoice().size === 0` → `organization.users.pickBranch`) |
| B-03 | S4 | UN-06 | Units, filter All (22 rows), page size 20, page 2, sort Unit descending | the whole list is sorted (server order) | only the rows already loaded are reordered: page 2 shows `V14 µmol/L, V14 PG-17` instead of the last two rows of the descending list. This was observation O-3 in v0.3.8; with lists now longer than one page it gives a wrong order | UN-05/06 transcript | `shared/ui/data-table` sorts the current page on the client (data-table spec). The list adapters (`master-data-page`) send no sort parameter to the server |
| B-04 | S4 | US-21 | First sign-in of a user with must-change; change the password on `/account/profile?mustChange=1` | the tab moves on to the portal home (branch choice, then the landing page), as it did in v0.3.8 (US-14 there) | "Your password was changed.", but the page stays on `?mustChange=1` and still reads "Your administrator asked you to choose a new password before you continue." The user must open a menu entry. The next page then asks for the branch as expected | US-21 transcript | In v0.5.0 (`git show v0.5.0:frontend/src/app/features/account/pages/profile.page.ts`) `changePassword()` only shows the toast and resets the form; nothing leaves `?mustChange=1`, and `mustChangeNow` (l.69) stays true while the query parameter is present. Note: the working tree (uncommitted, edited by someone else during this run) already adds a navigation to `portal.homePath()` for the Super Admin finding C-07. Re-test this case on the client portal once that change is built |
| B-05 | S3 | PL-08 | Base price lists › a price grid › "Add a service": pick a service that is already on the list | a refusal toast; the grid unchanged | toast "The service is already on the list.", then the grid shows only that line ("Showing 1–1 of 1") while the filter box is empty. Refresh keeps the hidden filter. The other lines seem gone until the user types in the filter box and clears it | PL-08 transcript; "Active / lines 9 active of 9" beside "Showing 1–1 of 1" | `features/pricing/pages/price-list-editor.page.ts` l.140 `this.facade.search.set(service.code …)` on a duplicate. The search box in `price-list-editor.page.html` l.77 is bound only through `(searchChange)`, not `[(value)]`, so the facade filter and the visible box disagree |
| B-06 | S4 | PL-02 | Base price lists › Copy list on V11-PL26 | a free code is proposed | `V11-PL27` is proposed, which already exists (inactive, so hidden by the default filter) → "A base price list with this code already exists." | PL-02 transcript | `features/pricing/ui/copy-list-dialog.component.ts` `nextCode()` (l.163–175) derives the code from the source code and year without checking the existing lists |
| B-07 | S4 | PL-15 | Base price lists (list page) | search by code and name, a **year** filter (PRC intake §screens "search (code, name), year, active flag"), Excel / CSV export (SRS "Grids & editors") | no year filter and no Export CSV on the lists page (the price grid has one); searching `2029` finds nothing | page actions | `features/pricing/pages/base-price-lists.page.ts`: master-data config with search and status only, no export button (the other finance and organization lists gained one with old B-10) |
| B-08 | S4 | TR-07 | Treasuries: search `الشرق` in the English UI (Arabic name "خزينة الشرق V14") | names in both languages are searchable, as on the other lists | "Nothing to show." The search also covers only the loaded page | TR-07 transcript | `features/treasury/pages/treasuries.page.ts` l.61–62: `searchMode: 'client'` because `ListTreasuriesQuery` has no text search. It matches the visible text only (code, localized name, branch label) |
| B-09 | S4 | AR-03 | Switch to Arabic | the shell shows the role in Arabic | "Laboratory Administrator" / "V14 Technician" in the sidebar, while the lists show "مدير المختبر" / "فني V14" | AR-03 | `shell/shell.component.ts` l.68 and `shell/shell-topbar.component.ts` l.41 show `auth.profile().roleName`, a single (English) string of the auth profile |

## 6. Previous defects (v0.3.8 campaign)

| Old id | Subject | This run | Status |
| --- | --- | --- | --- |
| B-01 | Currency empty on Add branch | BRN-02 | still fixed |
| B-02 | Governorate of another country saved | BRN-07, BRN-11 (ORG-017 on create and edit) | still fixed (new usability defect B-01 next to it) |
| B-03 | ORG-008 as one banner | BRN-05/06 (ORG-016 / ORG-015 under their fields, EN and AR) | still fixed |
| B-04 | Test editor lost sections on a partial refusal | TS-02/04/05/06/07 | still fixed |
| B-05 | Editors kept the previous record after back / forward | TS-13, SV-09 | still fixed |
| B-06 | 204 answers treated as failures | RL-06/08/11, US-08/13/14/15/16/17, PY-08/10 | still fixed |
| B-07 | Treasury picker cached for the app's life | TR-05/06, TA-04 | still fixed |
| B-08 | Inactive sample condition offered | ST-05 | still fixed |
| B-09 | Export ignored the filter; mixed formats | BRN-17, CG-07, RL-16, PR-09, DR-07, US-20 (branch codes now in the users export too) | still fixed |
| B-10 | No export on Branches / Roles / Users / Payers / Treasuries | BRN-17, RL-16, US-20, PY-15, TR-08 | still fixed (the Base price lists page still lacks one: B-07) |
| B-11 | Lock message and `returnUrl=%2Flogin` | US-15, every sign-out | still fixed |
| B-12 | Viewers could type in read-only editors | TS-17, SV-14, SQ-07 | still fixed |
| B-13 | Formula message repeated the position | TS-09 | still fixed |
| B-14 | Branches Save reset an unsaved role | US-10 | still fixed |
| O-3 / O-4 / O-6 | Client-side sort; activate without confirm; R2 contract-pricing action | UN-06; PL-10; PY-11 | O-3 is now defect B-03. O-4 persists (activation now shows a toast, still no confirm). O-6 is resolved |

## 7. Observations and open points (not counted as defects)

- **O-1 Activating a price list.** It still has no confirmation (UAT-FIN-02 step 5 says "Activate, confirm"). Activating a
  list can change the cash prices of the whole laboratory.
- **O-2 Normal and Critical on one list value.** A restricted-list value can be both Normal and Critical (TS-08). The CAT
  intake has no rule against it. Ask the business owner.
- **O-3 Test-level duplicates at registration.** REG-100 refuses a service that is already on the visit or inside a
  package. A **test** inside a profile inside a package can still be ordered again as its own service: V14-CREA next to
  V14-PKG ⊃ V14-RFT ⊃ V14-CREA, charged 85 more. The SRS actor table says reception "Refuses duplicates". Confirm whether
  this means services only.
- **O-4 Treasury access across branches.** The request picker offers every active treasury of the laboratory. Example:
  V11-NORTH-CASH is offered to v14.reception, whose branches are MAIN and V14-EAST. TRS intake Q3 only covers the
  approver.
- **O-5 Edits without trail events.** Payer edits, tier renames and treasury / price-list name edits still write
  `audit.field_changes` only, without an `audit.audit_trail` event. Creates and status changes write both.
- **O-6 Duplicate username / e-mail / mobile on Add user.** They appear in a banner, not under the field. The text names
  the field, so this is acceptable.
- **O-7 Release-2 modules in the privilege matrix.** The matrix lists Payer claims, Analysers, QC, Auto-verification,
  Support tickets and Wallet. Their keys can be granted although the pages are not in Release 1.
- **O-8 Clinical group "Report template".** The field is shown with no option (report templates are Release 2).
- **O-9 Money on a technician's dashboard.** The dashboard of v14.tech shows "Collected today EGP 620.75" although the
  role has no `billing.view`. This belongs to tester A's area.
- **O-10 Shared device id.** One device id per browser profile: treasury approvals follow the browser, not the person
  (unchanged).
- **O-11 Cash default list changed.** V14-PL26 (2026, created later) is now the cash default list, ahead of V11-PL26 (also
  2026). Its V11 prices are identical copies, so V11 quotes did not change. Testers of this laboratory should know which
  list prices cash patients now.

## 8. Data created (dev database `lis`, Walk-through Laboratory, invented data)

- **Branch:** V14-EAST "V14 East Branch (edited)", Egypt / Giza, Active.
- **Roles:**
  - V14 Reception (16 keys, cap 20 %, pay, cancel; see on V14-BIO and V14-SERO);
  - V14 Technician (7 keys; V14-BIO S/R/E/P on MAIN, V14-SERO see);
  - V14 Temp role (renamed), inactive.
- **Users** (passwords only in `.local/dev-credentials.md`):
  - v14.reception (V14 Reception, MAIN + V14-EAST);
  - v14.tech (V14 Technician, MAIN);
  - v14.temp (V14 Reception, every branch, Arabic, temporary password, must change).
- **Catalogue A:**
  - clinical groups V14-BIO (B) and V14-SERO (S);
  - sample type V14-PLS (conditions ROOM, COLD inactive);
  - units V14 µmol/L, V14 mg/dL, V14 PG-01…17 (inactive, paging test);
  - tests V14-CREA, V14-HBS, V14-CREAMG, V14-COMM;
  - services V14-CREA (1401), V14-HBS (1402), V14-RFT (1403), V14-PKG (1404), sequence 6–9.
- **Catalogue B:**
  - preparations V14-MEDS and V14-SMOKE;
  - comments V14LIP and V14ALL;
  - doctors V14-DR01 and V14-DR02;
  - microbiology: V14 Cephalosporins, V14 Zinnat, V14-CXM, V14-KPN, V14I.
- **Pricing:**
  - payers V14 MedCare Insurance (tiers V14 Gold, V14 Silver Plus inactive) and V14 Staff cash scheme;
  - price lists V14-PL26 (active, cash default) and V14-PL29 (inactive).
- **Treasury:** V14-MAIN-CASH and V14-EAST-CASH; TRQ-2026-00004 (cancelled) and TRQ-2026-00005 (approved, v14.reception,
  device `3757857f-…`).
- **Operations:** patient PAT-2026-00002, visit ACC-2026-00002, samples SMP-2026-00003/00004, receipt RCT-2026-00002 (300.75),
  results V14-CREA / V14-CREAMG / V14-COMM reviewed.
- **Shared records changed and restored:**
  - V11-NORTH: deactivated for BRN-13, reactivated at once;
  - wt.admin language: Arabic for AR-01, back to English;
  - V14 Reception `pricing.view`: removed for BR-17, restored.
  - No other V11 record was changed.

## 9. Not tested and why

- **BRN-19, US-24 (paging on Branches and Users).** These lists hold fewer than 20 rows. Paging was tested on Units (UN-05).
- **TA-07 (reject with a reason).** No pending request of another user was left after TA-06. It would need one more
  sign-in cycle of a second requester. It was tested in the v0.3.8 campaign.
- **Arabic field messages on the catalogue and pricing drawers.** Labels and lists were checked in Arabic on all 17 pages
  (AR-01). Field-level Arabic messages were checked on Branches and Users only.
- **Forced requests** (for example approving one's own treasury request through the API) were not executed: no token was
  placed in the page.
- **Keyboard shortcuts** (Ctrl+S, F2, Ctrl+K) were not exercised. Saves used the buttons.
