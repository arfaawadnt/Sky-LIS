# Validation 2026-10-11 · Tester B · Client portal set-up (Release 1)

Build under test: **v0.3.8** (SPA `http://127.0.0.1:4300` → validation API `127.0.0.1:5102`, database `lis` on the
private PostgreSQL 5433). Laboratory: **Walk-through Laboratory** (tenant `01a124d7-5e87-7d30-af34-12248755e2f1`).
At the start it held one branch (MAIN), one role (Laboratory Administrator), one user (`wt.admin`) and nothing else:
no clinical group, sample type, unit, test, service, payer, price list, treasury or catalogue-B record. The "global
master-data copy" was empty for this tenant. The set-up was built from scratch through the client portal as
`wt.admin`. The new personas `v11.reception` and `v11.tech` then ran the proof. Every record created carries the prefix
**V11**.

Test and report only: no application code, dictionary, script or migration was changed.

Method notes:

- **Clicks and input.** The Browser pane was too small for usable screenshots. Clicks and input events were
  dispatched from the page context (`element.click()`, `input` / `change` events). This runs the same Angular
  handlers as a user's click. Role and privilege checkboxes were ticked with pacing (150 ms), following the
  laboratory dry-run lesson.
- **CSV downloads** were intercepted in the page (`URL.createObjectURL` and the anchor `download` click). The CSV text
  was captured and nothing was written to disk.
- **Forced requests were not executed.** No bearer token was placed in the page. Refusals were verified only where
  the page offered the action. The HTTP status and problem code were read from the browser's network log.
- **Database checks.** Every save was followed by a read-only query as `lis_owner`. The queries and their results
  are in §4. Audit rows were checked in `audit.field_changes`, `audit.audit_trail` and `audit.security_events`.
  Outbox rows were checked through `audit.audit_trail.event_id` → `ops.outbox_events`.
- **One tab, several users.** Only one tab was used (`tab-8`, created for this run, on `127.0.0.1` only). The users
  were switched by signing out and in within the same tab. This also exercises the defect A-01 pattern (route-level
  state that survives a sign-out).

## 1. Summary

| Cases | Executed | Passed | Failed | Blocked | Not tested |
| --- | --- | --- | --- | --- | --- |
| 197 | 196 | 176 | 20 | 0 | 1 |

"Executed" = passed + failed. The 20 failed cases map to 14 defects, because several cases share one defect (for
example the missing export on five pages).

Defects: **0 × S1, 2 × S2, 4 × S3, 8 × S4** (§5).

Outbox: the run produced 123 events for the tenant between 11:10 and 11:50. All 123 were processed and none was
dead-lettered. The tenant's audit trail holds 128 entries, sequence 1…128 without gaps. No password, hash or security
stamp appears in `audit.field_changes` or in the outbox payloads (DB-20).

**Headline.** A laboratory can be built from an empty tenant through the R1 set-up pages, and that set-up drives the
operations end to end:

- The new reception user, holding a new role, signed in on a chosen branch, was approved on a new treasury, and
  registered a walk-in visit with new services:
  - prices came from the new base price list;
  - the preparation question came from the new preparation;
  - the referring doctor came from the new doctor list;
  - the tubes were split by the new clinical groups and sample types;
  - the cash payment was posted on the new treasury.
- The new technician then entered and reviewed results in the new chemistry group:
  - the value was flagged against the new male reference band;
  - the new formula test computed;
  - the new text test was filled from its paragraph templates;
  - the haematology results stayed read-only, as the role's result rights require.

The business rules checked (codes, uniqueness, caps, SoD, BR-17, BR-19, CAT-0xx, PRC-0xx, ORG-0xx, TRS-0xx) are
enforced and stored correctly.

The serious findings concern the editors:

- **B-04 (S2).** The test editor throws away the user's range bands or list values whenever one save step is refused.
  It never shows the server's reason.
- **B-05 (S2).** After browser back/forward between two service or test editors, the page keeps showing **and acting
  on** the previous record. A deactivation hit V11-GLUP while the address bar showed V11-PKG.
- **B-06 (S3).** About fifteen organization and pricing actions that succeed (HTTP 204) are treated by the page as
  failures:
  - no confirmation is shown;
  - the reset-password dialog stays open after the reset;
  - a tier rename keeps a stale "already exists" error.
- **B-07 (S3).** The treasury picker on Treasury access is cached for the life of the app and survives a sign-out. A
  reactivated treasury is not offered until a full reload.

## 2. Coverage (page × function → cases)

Client R1 set-up routes come from `frontend/src/app/shell/nav.registry.ts`. Setup group:

- Branches, Roles & privileges, Users;
- Preparations, Comment library, Doctors, Microbiology;
- Clinical groups, Services, Tests, Service sequence, Sample types, Units of measurement.

Finance group: Treasuries, Treasury access, Payers & contracts, Base price lists.

The route files are `organization.routes.ts`, `catalogue-a.routes.ts`, `catalogue-b.routes.ts`, `pricing.routes.ts`
and `treasury.routes.ts`. R2 routes (parameters, contract pricing, diagnoses, sending labs and the rest) were not
tested.

| Page (SRS) | Functions | Cases |
| --- | --- | --- |
| Branches CP-36 | required, code format / upper-case, currency, e-mail, country–governorate, add, duplicate, edit, deactivate, last-active rule, reactivate, status filter, search EN/AR, sort, export, paging, Arabic | BRN-01…17 |
| Clinical groups CP-39 | required, formats, add, duplicate, unit code, edit, (de)activate, CSV, Arabic | CG-01…09 |
| Sample types CP-43 + conditions | required, add, duplicate, conditions add / duplicate / deactivate, inactive condition in editors | ST-01…07 |
| Units CP-44 | required, add, case-insensitive duplicate, edit, deactivate | UN-01…04 |
| Tests CP-41 | numeric + bands + panic + timing, overlap CAT-054, panic order CAT-053, restricted list + BR-19 CAT-058, formula check / insert / normalise / unknown / self, formatted text + paragraphs, type change CAT-071, duplicate, filters, search EN/AR, CSV, (de)activate, editor switching | TS-01…18 |
| Services CP-40 | member count CAT-021, test / profile / package, shortcut uniqueness, aliases, precaution, sample condition, required sample type, duplicate, filters, search (shortcut, alias, Arabic, short name), CSV, edit members, (de)activate, editor switching | SV-01…13 |
| Service sequence CP-42 | swap in one save, duplicate, zero / decimal, discard, search / type filter, CSV | SQ-01…06 |
| Preparations CP-46 | option-list rule CAT-210, linked services picker, Yes/No + gender, duplicate, edit options (code read-only), (de)activate, search EN/AR, answer-type filter, CSV | PR-01…08 |
| Comment library CP-47 | required, add with / without group scope, duplicate, edit, (de)activate, search, CSV | CM-01…06 |
| Doctors CP-48 | share 0–100, branches picker, all branches, duplicate, edit branches, (de)activate, search name / Arabic / phone, CSV | DR-01…08 |
| Microbiology CP-56 | drug types, commercial names, antibiotics, organisms (edit, de/reactivate, CSV), sensitivity values (order ≥ 0), duplicates | MB-01…06 |
| Payers & contracts CP-32 | Cash / Default provisioning, required, portal username, search-key normalisation, tiers add / duplicate / rename / deactivate, Cash rules PRC-020 / PRC-021, duplicate key PRC-100, credit payer, (de)activate, search, export, inactive tier at ordering | PY-01…16 |
| Base price lists CP-33 | required / year, create inactive, picker, price validation, save, activate, grid CSV, copy (negative refused, ×1.1 rounded 0.5), highest-year list prices cash, deactivate, edit, Apply, search | PL-01…14 |
| Roles & privileges CP-37 | create without permission, screen rights, special rights (cap ORG-006, flags), result rights per group and branch, review⇒see, rename, revoke all, (de)activate, members rule, change confirm with members, BR-17, list filters, feedback, Arabic, export | RL-01…17 |
| Users CP-38 | required, formats, password rules, duplicates, branches, role, reset password, lock / unlock + sign-in, must-change, overrides + restore, (de)activate, main administrator, filters, Arabic, export, branch selection | US-01…21 |
| Treasuries CP-28 | required, add per branch, duplicate, edit, deactivate → picker, reactivate, search, picker freshness, export | TR-01…09 |
| Treasury access CP-29 | required, own-request SoD, cancel, request, duplicate TRS-012, approve, reject with reason, Arabic | TA-01…09 |
| Read-only view (reception) | write actions hidden, inputs, direct URL to Users / Roles after a user switch | RO-01…03 |
| Proof (CP-12, CP-06, CP-18, CP-19) | quote, registration with the new set-up, worklist, result entry / formula / text, result rights, review | PF-01…06 |

## 3. Test cases

Verdicts: PASS, FAIL (defect id), NT (not tested). "DB-nn" points to §4. Persona `wt.admin` unless stated.

| Id | Steps | Expected (source) | Observed | Verdict |
| --- | --- | --- | --- | --- |
| BRN-01 | Add branch → Save empty | required fields marked (CP-36) | "This field is required." on Code, Name EN, Name AR, Currency | PASS |
| BRN-02 | Open Add branch | currency starts at EGP (`DEFAULT_CURRENCY`, field hint) | Currency empty; the user must type EGP | FAIL B-01 |
| BRN-03 | Code `v11 north!` | code format refused, upper-cased | shown as `V11 NORTH!`; "Use upper-case letters, digits, '-', '_' or '.', starting with a letter or digit." | PASS |
| BRN-04 | Currency `EG` | VALIDATION-400 field message (UAT-ADM-01 step 3) | "'Currency' must be 3 characters in length. You entered 2 characters." | PASS |
| BRN-05 | E-mail `not-an-email` | refused (ORG-008) | 422 ORG-008 "The e-mail address is not valid."; the page shows "The contact or location data of the branch is invalid." with no field marked | PASS (B-03) |
| BRN-06 | V11-NORTH: names, Egypt, Alexandria, phone, e-mail, EGP | branch Active | "Branch saved."; row Active | PASS · DB-01 |
| BRN-07 | Add `v11-north` again | ORG-100 | "A branch with this code already exists." | PASS |
| BRN-08 | Edit V11-NORTH: code field; country "V11 Land" with governorate Alexandria (Egypt) | code read-only; pair refused (hint "The governorate must belong to the chosen country", ORG-008) | code disabled; **saved** with country V11 Land + Alexandria (`gov_in_country` = f) | FAIL B-02 · DB-01 |
| BRN-09 | Edit: country Egypt, name "(edited)", phone | saved, audited | saved; field changes before / after | PASS · DB-01/02 |
| BRN-10 | Deactivate V11-NORTH | confirm, Inactive | "Users can no longer select this branch at sign-in…" → "Branch deactivated." | PASS · DB-02 |
| BRN-11 | Deactivate MAIN (the last active branch) | ORG-110 | "The last active branch cannot be deactivated."; MAIN stays Active | PASS |
| BRN-12 | Status filter Inactive / All; Reactivate V11-NORTH | filter and reactivation | correct rows; "Branch reactivated." | PASS · DB-02 |
| BRN-13 | Search `north`, `الشمال`, `zzz` | search code and names | 1 / 1 / "Nothing to show." | PASS |
| BRN-14 | Click the Code header twice | sort | ascending, then descending (client-side on the loaded page, see O-3) | PASS |
| BRN-15 | Look for an export | Excel / CSV export (SRS "Grids & editors") | no export button | FAIL B-10 |
| BRN-16 | Paging | several pages | 2 rows < smallest page size 20 | NT |
| BRN-17 | Arabic | rtl, Arabic labels | rtl, Arabic labels and names | PASS |
| CG-01 | Add clinical group → Save empty | required | Code, Unit code, Names marked; display order defaults 0 | PASS |
| CG-02 | Code `v11 chem!`, unit `c c`, order −1 | formats (CAT-001 / CAT-003 / CAT-008) | format message on code and unit; "The value must be at least 0." | PASS |
| CG-03 | V11-CHEM (unit C, report name, order 1), V11-HEMA (H, 2) | Active | "Saved." ×2 | PASS · DB-03 |
| CG-04 | `v11-chem` again | CAT-100 | "A clinical group with this code already exists." | PASS |
| CG-05 | V11-IMM with unit code C (already used by V11-CHEM) | intake: unit code is a label letter, not unique | accepted (see O-2) | PASS |
| CG-06 | Edit V11-HEMA report name | code read-only, saved | code disabled; "Saved." | PASS · DB-03 |
| CG-07 | Deactivate V11-IMM; Inactive filter; Activate | confirm, status | "Deactivated." / "Activated." | PASS · DB-03 |
| CG-08 | Export CSV with filter Active | the listed rows | `clinical-groups-2026-10-11.csv` with BOM and 7 columns, but it also holds the **inactive** V11-IMM | FAIL B-09 |
| CG-09 | Arabic | rtl | rtl; Arabic names | PASS |
| ST-01 | Add sample type → Save empty | required | 4 fields marked | PASS |
| ST-02 | V11-SER (Serum), V11-EDTA | Active | "Saved." ×2 | PASS · DB-04 |
| ST-03 | V11-SER again | duplicate refused | "A sample type with this code already exists." | PASS |
| ST-04 | Conditions of V11-SER: FAST, RAND, `rand` in one save | CAT-011 | "A condition code is used twice in this sample type…"; rows kept; with REFR instead → "The conditions were saved." | PASS · DB-04 |
| ST-05 | Reopen, add RAND again | CAT-011 | same refusal | PASS |
| ST-06 | Remove the new row, untick REFR Active, save | condition deactivated | saved; list shows 2 conditions; REFR `is_active` f | PASS · DB-04 |
| ST-07 | Tests › Add test, sample type V11-SER, Sample condition list | only active conditions | REFR · Refrigerated (inactive) is offered | FAIL B-08 |
| UN-01 | Add unit → Save empty | required | "This field is required." | PASS |
| UN-02 | `V11 mmol/L`, `V11 g/dL` | Active | "Saved." ×2 | PASS · DB-05 |
| UN-03 | `v11 MMOL/l` | unique ignoring case | "A unit with this name already exists." | PASS |
| UN-04 | Add `V11 IU/L`, edit to `V11 U/L`, deactivate | saved, Inactive | "Saved." / "Deactivated."; hidden under Active | PASS · DB-05 |
| TS-01 | Add test → Save empty | required | Code, Clinical group, Names marked; decimals 2, labels 1 by default | PASS |
| TS-02 | V11-GLU numeric (V11-CHEM, mmol/L, 1 dec, V11-SER / FAST, LOINC, methodology, print range) + 3 bands + timing duration 6 / **minimum 10** | timing refused (CAT-070); the user corrects it without losing input | toast "The test was created; part of its configuration was refused — see the highlighted tab."; no tab highlighted and no message. The editor reopened on the saved test with **0 bands** and default timing: bands and timing input lost, the ranges request never sent | FAIL B-04 |
| TS-03 | Re-enter 3 bands + a 4th Male 30–60 (overlaps) + valid timing | CAT-054; bands kept | "Two bands of the same sex and condition cover the same ages." The timing (an earlier step) was saved, the editor reloaded and the grid shows **"No bands"** | FAIL B-04 |
| TS-04 | 4 bands, 4th Any 30–60 with panic low 5 inside 4–6 | CAT-053 | "Limits must read: reportable low ≤ panic low ≤ low ≤ high ≤ panic high ≤ reportable high…"; bands kept (no earlier step) | PASS |
| TS-05 | Remove the 4th band, save | bands and timing stored | "Saved."; tab "Reference ranges 3" | PASS · DB-06 |
| TS-06 | V11-BG restricted list: A, B, AB, O, "—"; tick Default on O (Placeholder auto-ticks), untick Placeholder | CAT-058 (BR-19), shown to the user | 422 CAT-058 in the network log; the page showed only the "created; part refused" toast; the five values were lost | FAIL B-04 |
| TS-07 | Re-enter with "—" Default + Placeholder, colour on A | saved | "Saved."; "List values 5" | PASS · DB-07 |
| TS-08 | V11-HGB numeric (V11-HEMA, g/dL, EDTA) with band 12–17, panic 7 / 20 | created complete | "The test was created." | PASS · DB-06 |
| TS-09 | V11-GLUMG formula: `[V11-GLU] +`, Check formula | CAT-063 | "The formula is not valid. (position 12) — The formula ends too early. (position 12)" | PASS (B-13) |
| TS-10 | Search GLU → Add inserts `[V11-GLU]`; `round([V11-GLU] * 18.016, 0)`; Check; Save | normalised, saved | "Valid: ROUND([V11-GLU] * 18.016, 0) — reads V11-GLU."; created | PASS · DB-07 |
| TS-11 | `[NOPE] * 2`; `[V11-GLUMG] * 2` on GLUMG | unknown / self refused | "…reads a test code that does not exist or is inactive…"; "A formula cannot read its own test." | PASS |
| TS-12 | V11-NOTE formatted text with 2 paragraphs | saved | "The test was created."; "Paragraphs 2" | PASS · DB-07 |
| TS-13 | Change the type to Restricted list on a test that has configuration | CAT-071 | "Remove the configuration of the current type … before changing the test type."; type unchanged. Note: because of B-05 this ran on V11-NOTE (paragraphs), not on the V11-HGB address that was open | PASS · DB-07 |
| TS-14 | New test `v11-glu` | duplicate refused | "A test with this code already exists." | PASS |
| TS-15 | Tests list: type Formula, group V11-HEMA, search `glucose` / `جلوكوز` | filters and search | 1 / 2 / 2 / 2 correct rows | PASS |
| TS-16 | Export CSV | file | `tests-2026-10-11.csv`, 10 columns, 5 rows | PASS |
| TS-17 | V11-NOTE Deactivate → Activate | confirm, status | "Deactivated." / "Activated." | PASS · DB-07 |
| TS-18 | Open V11-GLU, then the V11-HGB editor (history navigation) | V11-HGB shown | the address shows V11-HGB, the page still shows **V11-GLU** | FAIL B-05 |
| SV-01 | V11-GLU type Test with members GLU + GLUMG | CAT-021 | 422 "The members do not fit the service type…"; nothing created | PASS |
| SV-02 | V11-GLU Test: shortcut 1101, aliases `FBS, V11SUGAR`, precaution, V11-SER / FAST, TAT 6, member V11-GLU | created | "The service was created."; sequence 1 | PASS · DB-08 |
| SV-03 | V11-HGB with shortcut 1101 | shortcut unique | "This shortcut number is used by another service."; with 1102 created | PASS · DB-08 |
| SV-04 | V11-BG (1103, HEMA, EDTA, TAT 2) | created | created | PASS · DB-08 |
| SV-05 | V11-GLUP Profile without sample type | refused (CAT-029) | "This field is required."; with V11-SER / FAST created | PASS · DB-08 |
| SV-06 | V11-GLUP members GLU, GLUMG, NOTE | 3 members | "Members 3" | PASS · DB-08 |
| SV-07 | V11-PKG Package (no sample fields) members V11-GLUP + V11-HGB | services as members | created; members of kind Service | PASS · DB-08 |
| SV-08 | `v11-glu` again | duplicate refused | "A service with this code already exists." | PASS |
| SV-09 | Filters Profile / V11-HEMA; search `1103`, `v11sugar`, `باقة`, `hb` | filters, search by shortcut, alias, Arabic, short name | GLUP / HGB+BG / BG / GLU / PKG / HGB | PASS |
| SV-10 | Export CSV | file | 13 columns, 5 rows | PASS |
| SV-11 | V11-GLUP: precaution, tick "In sum" on GLUMG | saved | "Saved."; members rewritten (3 deleted + 3 created), GLUMG in_sum t | PASS · DB-08 |
| SV-12 | Deactivate → Activate in the editor | confirm, status | "Deactivated." / "Activated." (this ran on V11-GLUP, see SV-13) | PASS |
| SV-13 | From the V11-GLUP editor open the V11-PKG editor; Deactivate; browser back / forward | the page shows and acts on V11-PKG | the page kept showing V11-GLUP; the confirm read "Deactivate V11-GLUP…"; the database shows V11-GLUP deactivated and reactivated, V11-PKG untouched. Back / forward keep the stale record | FAIL B-05 · DB-08 |
| SQ-01 | Service sequence: BG 2, HGB 3, one Save | "2 sequence(s) saved." | as expected | PASS · DB-09 |
| SQ-02 | BG = 3 (HGB 3) | duplicate refused (CAT-105) | "Some sequences are used twice…"; Save disabled; the field is marked | PASS |
| SQ-03 | BG = 0, then 2.5 | refused (CAT-031) | Save disabled for both | PASS |
| SQ-04 | Discard changes | restored | 1,2,3,4,5 | PASS |
| SQ-05 | Search `glucose`, type Package | filters | 2 / 1 rows | PASS |
| SQ-06 | Export CSV | file | Sequence, Code, Names, Type, Active | PASS |
| PR-01 | V11-FAST Option list with one option | CAT-210 | "An option list needs at least two different options." | PASS |
| PR-02 | Options `8 h / 12 h / 14 h`, linked V11-GLU + V11-GLUP (picker) | saved | "Preparation saved."; chips on the row | PASS · DB-10 |
| PR-03 | V11-PREG Yes / No, Female, all services | saved | saved; "All services" | PASS · DB-10 |
| PR-04 | `v11-fast` again | duplicate refused | "A record with this code already exists in this list." | PASS |
| PR-05 | Edit V11-FAST: code; option 14 h → 16 h | code read-only; saved | disabled; "8 h · 12 h · 16 h" | PASS · DB-10 |
| PR-06 | Deactivate / Reactivate V11-PREG | status | "Reactivated." | PASS |
| PR-07 | Search `pregnant`, `حامل`, `fast`; answer type Yes / No | search, filter | correct rows | PASS |
| PR-08 | Export CSV | file | 8 columns; answer type and gender as raw enums (`OptionList`, `YesNo`, `Any`) | PASS (B-09) |
| CM-01 | Add comment → Save empty | required | 3 fields marked | PASS |
| CM-02 | V11HAEM scoped to V11-CHEM; V11GEN for all groups | saved | "Comment saved." ×2; "All groups" | PASS · DB-10 |
| CM-03 | `v11haem` again | duplicate refused | "A record with this code already exists in this list." | PASS |
| CM-04 | Edit V11GEN text (short name read-only); Deactivate; Activate | saved, status | as expected | PASS · DB-10 |
| CM-05 | Search `haemolysed` | search | V11HAEM | PASS |
| CM-06 | Export CSV | file | 6 columns, group EN / AR | PASS |
| DR-01 | V11-DR01 share 150 | 0–100 (CAT-220) | "The value must be at most 100." | PASS |
| DR-02 | V11-DR01: phone, names, specialty, occupation, share 10, branch V11-NORTH | saved | "Doctor saved." | PASS · DB-10 |
| DR-03 | V11-DR02, no branch | all branches | "All branches" | PASS · DB-10 |
| DR-04 | `v11-dr01` again | duplicate refused | "A record with this code already exists in this list." | PASS |
| DR-05 | Edit V11-DR01: add MAIN | saved | "V11-NORTH MAIN" | PASS · DB-10 |
| DR-06 | Deactivate / Reactivate V11-DR02 | status | "Deactivated." / "Reactivated." | PASS |
| DR-07 | Search `salem`, `منى`, `0100555` | search | DR02 / DR02 / DR01 | PASS |
| DR-08 | Export CSV | file | 9 columns; Branches column holds the count "2", not the codes | PASS (B-09) |
| MB-01 | Drug types: empty save; "V11 Penicillins"; `v11 PENICILLINS` | required, saved, unique name | required ×2; saved; "A record with this name already exists in this list." | PASS · DB-10 |
| MB-02 | Commercial name "V11 Amoxil" under V11 Penicillins | saved | saved, drug type shown | PASS · DB-10 |
| MB-03 | Antibiotic V11-AMX / AMX / V11 Amoxil; `v11-amx` again | saved; duplicate refused | saved; "A record with this code already exists in this list." | PASS · DB-10 |
| MB-04 | Organism V11-ECOLI: add, edit name, deactivate, All filter, activate, CSV | each works | as expected | PASS · DB-10 |
| MB-05 | Sensitivity values V11S order 1; V11R order −2, then 2 | order ≥ 0 | "The value must be at least 0."; saved with 2 | PASS · DB-10 |
| MB-06 | Arabic (drug types, antibiotics, sensitivity values) | rtl | rtl, Arabic tabs | PASS |
| PY-01 | Open Payers & contracts | Cash / Default provisioned on first open (UAT-FIN-01 step 1) | Cash · CASH · Default, Active | PASS · DB-11 |
| PY-02 | New payer → Save empty | required | Name EN, Name AR and Search key marked | PASS |
| PY-03 | Portal enabled, no web username | VALIDATION-400 | "'Web Username' must not be empty." | PASS |
| PY-04 | V11 Nile Insurance, key ` v11nile `, Insurance, portal `v11.nile.portal` | trimmed and upper-cased key | "Payer saved."; key V11NILE | PASS · DB-11 |
| PY-05 | Tiers V11 Gold, V11 Silver | added | "Tier added." ×2 | PASS · DB-11 |
| PY-06 | Tier `v11 gold` | PRC-103 | "The payer already has a tier with this name." | PASS |
| PY-07 | Rename V11 Silver → V11 Silver Plus | renamed, confirmed, form reset | PUT 204 and the name changed, but no toast; the dialog stays in rename mode and shows the stale "The payer already has a tier with this name." | FAIL B-06 · DB-11 |
| PY-08 | Deactivate tier V11 Silver Plus | confirm, Inactive | confirm "…will quote nothing until it is reactivated"; Inactive; no toast | PASS (B-06) · DB-11 |
| PY-09 | Cash › Deactivate | PRC-020 | "The default cash payer cannot be deactivated or change type." | PASS |
| PY-10 | Cash › Contract tiers | Default has no Deactivate tier (PRC-021) | Rename tier and Contract pricing only | PASS |
| PY-11 | New payer with key V11NILE | PRC-100 | "Another payer uses this search key." | PASS |
| PY-12 | V11 Delta Corporate (Credit, V11DELTA) + tier V11 Staff; Deactivate; filter All; Reactivate | saved, status | "Payer saved.", "Tier added.", "Payer deactivated.", "Payer reactivated." | PASS · DB-11 |
| PY-13 | Edit Cash, Type → Credit | PRC-020 | same refusal | PASS |
| PY-14 | Search `nile`, `النيل` | search | 1 row each | PASS |
| PY-15 | Export | Excel / CSV export | none | FAIL B-10 |
| PY-16 | Price query / registration payer list | only active tiers | Cash — Default, V11 Delta — V11 Staff, V11 Nile — V11 Gold (Silver Plus inactive, absent) | PASS |
| PL-01 | New list → Save empty; year 1999 | required; year ≥ 2000 | 3 fields marked; "The value must be at least 2000." | PASS |
| PL-02 | V11-PL26, 2026 | created inactive, grid opens | "List saved."; "Status Inactive … never prices cash patients" | PASS · DB-12 |
| PL-03 | Add a service: GLU, HGB, BG, GLUP, PKG | lines at 0 | "… added — enter its price." ×5 | PASS |
| PL-04 | GLU −5, HGB 100.555; Save prices | refused by the page (PRC-006) | "Correct the highlighted prices first."; nothing sent | PASS |
| PL-05 | 75 / 110 / 60 / 150 / 230.50 | saved | "Prices saved: 5 added, 0 changed." | PASS · DB-12 |
| PL-06 | Activate | Active, prices cash | Active, "Prices cash patients" (no confirm and no toast, O-4) | PASS · DB-12 |
| PL-07 | Export CSV of the grid | file | `v11-pl26-prices-2026-10-11.csv`, 5 lines | PASS |
| PL-08 | Copy list, Add amount −500, V11-PL29 | PRC-012 | "The copy would produce a price below 0 or above the maximum." | PASS |
| PL-09 | Copy ×1.1, round 0.5, V11-PL27 2027 | inactive copy with rounded prices | "List V11-PL27 created (inactive)."; 82.5 / 121 / 66 / 165 / 253.5 (230.5 × 1.1 = 253.55 → 253.5) | PASS · DB-12 |
| PL-10 | Activate PL27; Price query V11-GLU, 1102 under Cash, then V11 Nile Gold | highest-year active list prices cash (Pricing Q2) | 82.50 / 121.00; Nile: "Not priced under this contract" (no contract pricing in R1) | PASS |
| PL-11 | Deactivate PL27 from the list | confirm, Inactive | "Apply will skip this list…" → "List deactivated." | PASS · DB-12 |
| PL-12 | Edit list PL26 name (code read-only) | saved | "List saved." | PASS · DB-12 |
| PL-13 | Apply to contracts on PL26 | R1: nothing derived | "Recompute the 0 contract tier(s)…" → "0 contract tier(s) applied, 0 price(s) changed." | PASS |
| PL-14 | Search `2027` with filter Active; filter All | search, filter | nothing (PL27 inactive) / both | PASS |
| RL-01 | Add role: Create role empty, then names without permission | required; "Grant at least one permission…" (ORG-005) | as expected | PASS |
| RL-02 | V11 Reception with 17 keys (paced ticks) | created | "Role created."; "17 of 93 permissions granted" | PASS · DB-13 |
| RL-03 | Special rights: Custom cap 101 | ORG-006 | "The discount cap must be between 0 and 100." | PASS |
| RL-04 | Cap 15, Take payments, Cancel services; Save | saved, confirmed | stored (cap 15, can_pay, can_cancel); **no** "Special rights saved." toast | PASS (B-06) · DB-13 |
| RL-05 | Result rights, every branch: See on V11-CHEM and V11-HEMA | saved | 2 rules stored; no toast | PASS (B-06) · DB-13 |
| RL-06 | V11 Technician: 6 keys; MAIN: CHEM see / review / edit / print; every branch: HEMA see | branch-specific rights | stored with branch MAIN and branch null | PASS · DB-13 |
| RL-07 | Tick Review on V11-IMM; untick See | review presupposes see (INV-ORG-008) | See ticked automatically; unticking See clears Review | PASS |
| RL-08 | Rename the temp role | saved | heading "V11 Temp role (renamed)" | PASS · DB-13 |
| RL-09 | Revoke all, Save | an active role needs a permission | "An active role needs at least one permission." | PASS |
| RL-10 | Deactivate / Activate the temp role (editor), Deactivate from the list | status | Inactive / Active / Inactive; no toast | PASS (B-06) · DB-13 |
| RL-11 | Laboratory Administrator and V11 Reception (members) | Deactivate disabled (ORG-011) | disabled, title "A role with members cannot be deactivated." | PASS |
| RL-12 | V11 Reception (2 members): untick pricing.view, Save | confirm for all members | "This role has 2 member(s). The change applies to all of them. Continue?" | PASS · DB-13 |
| RL-13 | Sign in as `v11.reception` | BR-17: the role change reaches the member | Finance lost Payers & contracts and Base price lists; `/pricing/payers` → "The payers could not be loaded."; restored afterwards (17 keys) | PASS · DB-13 |
| RL-14 | Search `tech`, `استقبال`; status Inactive | filters | correct rows | PASS |
| RL-15 | Any rights save, (de)activation | success message | none of them shows a confirmation (204 handled as failure) | FAIL B-06 |
| RL-16 | Arabic (list and editor) | rtl | rtl, Arabic | PASS |
| RL-17 | Export | Excel / CSV export | none | FAIL B-10 |
| US-01 | Add user → Create user empty | required | 6 fields marked | PASS |
| US-02 | Username `abc`, password `Ab1!xyz`, e-mail `bad-mail`, mobile `12` | ORG-023 / ORG-020 / formats | "The value has the wrong format.", "At least 8 characters.", "Enter a valid e-mail address.", "Enter a valid mobile number." | PASS |
| US-03 | Confirm differs | refused | "The two passwords differ." | PASS |
| US-04 | Password `Xv11.reception#9` | ORG-021 | "The password must not contain the username." | PASS |
| US-05 | Password `abcdefgh` | 8+ characters, no composition rules (INV-ORG-014) | accepted; `v11.reception` created (no role, every branch, must-change ticked by default) | PASS · DB-14 |
| US-06 | `v11.tech` with the reception's e-mail, then mobile | ORG-103 / ORG-104 | "Another account uses this e-mail address." / "…this mobile number."; created after correction (role V11 Technician, MAIN, shifts, designation, employee code V11-T01, no must-change) | PASS · DB-14 |
| US-07 | Username `v11.reception` again | ORG-102 | "This username is taken."; then `v11.temp` created | PASS · DB-14 |
| US-08 | v11.reception › Role & branches: MAIN + V11-NORTH, Save | saved | stored; security event `MEMBERSHIP_CHANGED …:branches` | PASS · DB-14/15 |
| US-09 | Role V11 Reception → Assign role → confirm | saved | "The user gets the privileges of the new role at their next action." → stored; no toast | PASS (B-06) · DB-14 |
| US-10 | Choose a role, then press the branches Save first | both kept or a warning | the unsaved role choice reverted to "No role" | FAIL B-14 |
| US-11 | Reset password: `short`; then a temporary password with must-change | ORG-020; reset, dialog closes, confirmation | "At least 8 characters."; then POST 204, but the dialog **stays open** with the passwords and shows no confirmation | FAIL B-06 · DB-15 |
| US-12 | Lock v11.reception; sign in as v11.reception | AUTH-003 | "Locked"; sign-in: "The account is locked. Try again later." (B-15 wording); event `SIGN_IN_FAILED locked_out` | PASS · DB-15 |
| US-13 | Unlock; sign in | sign-in works | Active; signed in | PASS · DB-15 |
| US-14 | First sign-in with the temporary password | must-change gate | lands on Profile "Your administrator asked you to choose a new password…"; `/organization/users`, `/catalogue/tests` redirect back; "Your password was changed."; then branch selection | PASS · DB-15 |
| US-15 | v11.temp › Privileges: + registration.export, Save; Restore to role | Modified, then inherits | "Modified", "+ registration.export"; restore confirm → "Inherits the role" | PASS · DB-14 |
| US-16 | v11.temp › Deactivate / Activate | status | Inactive / Active | PASS · DB-15 |
| US-17 | Open wt.admin (own account, main administrator) | protected | "This is your own account…"; role select disabled, "The main administrator keeps the administrator role."; no security actions | PASS |
| US-18 | Filters role V11 Technician, status Locked; search `v11.t`, mobile, `فني` | filters | correct rows | PASS |
| US-19 | Arabic (list and editor) | rtl | rtl, Arabic | PASS |
| US-20 | Export | Excel / CSV export | none | FAIL B-10 |
| US-21 | Sign in as a user with 2 branches (wt.admin, v11.reception) | branch selection (CP-04) | "Choose your branch" with MAIN and V11-NORTH; `SESSION_CONTEXT_CHANGED` with the branch | PASS · DB-15 |
| TR-01 | Add treasury → Save empty | required | 4 fields marked | PASS |
| TR-02 | `v11-main-cash` on MAIN; V11-NORTH-CASH on V11-NORTH | upper-cased, branch `CODE · name` | rows V11-MAIN-CASH · MAIN · Walk-through Laboratory, V11-NORTH-CASH · V11-NORTH… | PASS · DB-16 |
| TR-03 | V11-MAIN-CASH again | TRS-100 | "A treasury with this code already exists." | PASS |
| TR-04 | Edit NORTH-CASH name (code and branch read-only) | saved | "Saved." | PASS · DB-16 |
| TR-05 | Deactivate NORTH-CASH; open Treasury access | not offered (TRS-010) | only V11-MAIN-CASH in the picker | PASS |
| TR-06 | Reactivate | Active | "Activated." | PASS · DB-16 |
| TR-07 | Search `north` | search | 1 row | PASS |
| TR-08 | After the reactivation, open Treasury access (same tab; later after sign-out / sign-in as v11.reception on V11-NORTH); press Refresh | the reactivated treasury is offered | still only V11-MAIN-CASH; offered only after a full page reload | FAIL B-07 |
| TR-09 | Export | Excel / CSV export | none | FAIL B-10 |
| TA-01 | Request access without a treasury | required | "This field is required." | PASS |
| TA-02 | wt.admin requests V11-MAIN-CASH ("V11 admin desk") | own row: Cancel only (SOD-TRS-001) | TRQ-2026-00001 Pending with `Cancel request` only | PASS · DB-16 |
| TA-03 | Cancel own request with a reason | Cancelled | "Request cancelled."; decided by wt.admin, reason kept | PASS · DB-16 |
| TA-04 | v11.reception (MAIN) requests V11-MAIN-CASH | Pending | TRQ-2026-00002 | PASS · DB-16 |
| TA-05 | Same request again | TRS-012 | "A pending or approved request already exists for this treasury on this device." | PASS |
| TA-06 | v11.reception (V11-NORTH, after reload) requests V11-NORTH-CASH | Pending | TRQ-2026-00003 | PASS · DB-16 |
| TA-07 | wt.admin approves TRQ-2026-00002 | Approved | confirm → "Request approved."; "Test Manager · 11 Oct 2026, 11:46", `Revoke` offered | PASS · DB-16 |
| TA-08 | Reject TRQ-2026-00003 without, then with a reason | reason mandatory | "A reason is required."; "Request rejected."; reason stored | PASS · DB-16 |
| TA-09 | Arabic | rtl | rtl, Arabic | PASS |
| RO-01 | v11.reception on Payers, Base price lists, a price grid, Tests, Services, Clinical groups, Doctors, Treasuries | no write actions | no Add / Edit / Deactivate / Save / Copy / Activate / Export; price inputs disabled | PASS |
| RO-02 | v11.reception on Service sequence and the V11-GLU test editor | read-only | no Save, but the sequence inputs (and "Discard changes") and 66 inputs of the test editor accept typing | FAIL B-12 |
| RO-03 | After wt.admin had the Users and Roles lists open, sign out, sign in as v11.reception, type `/organization/users` and `/organization/roles` | no data from the previous user (A-01 check) | lists reloaded and refused ("The users could not be loaded.", "The roles could not be loaded."); nothing from the admin session. The pages still open by URL (known A-08) | PASS |
| PF-01 | Price query (wt.admin): V11-GLU, 1102; contract switch | new prices, preparation, tier rules | 82.50 / 121.00 (PL27 active at that time); preparations "V11 Fasting hours?" and "V11 Are you pregnant?" (gender not specified); Nile Gold "Not priced under this contract" | PASS |
| PF-02 | v11.reception on MAIN: new patient "V11 Setup Walk-in" (Mr → Male, 40 y, mobile); Cash — Default; doctor list; services `V11-GLUP` (code), `1102` (shortcut), `V11 BG` (short name); preparation 12 h; cash 320 on V11-MAIN-CASH; Save | the visit uses the new set-up throughout | PAT-2026-00001. Doctors: DR01 (MAIN + NORTH) and DR02 (all). Discount limit "15 %" (role cap). Prices from V11-PL26: 150 / 110 / 60 = 320. Tubes: C V11 Serum · Fasting (GLUP) and H EDTA shared by HGB + BG (BR-02). The female question was not asked. Saved **ACC-2026-00001**, "2 tube(s) were issued.", "320.00 received — receipt RCT-2026-00001." | PASS · DB-17 |
| PF-03 | v11.tech › Worklist | the visit's results | 5 rows (GLU, GLUMG, NOTE, HGB, BG), samples Received | PASS · DB-18 |
| PF-04 | Results by accession: GLU 7.2; Text entry on V11-NOTE (template paragraphs); Save (1) | flag against the male adult band, formula computed, text saved | "1 results saved." (text) and "2 results saved."; GLU 7.2 **H** with range 4.0 – 6.0; GLUMG ƒ 130; text stored | PASS · DB-18 |
| PF-05 | V11 Haematology tab | see-only rights: no entry | inputs of HGB and BG disabled; no Save / Review | PASS |
| PF-06 | Review on V11 Chemistry | MAIN review right | "3 results reviewed." | PASS · DB-18 |

## 4. Database verification

All queries ran as `lis_owner`, read only, against `lis`. `T` = `01a124d7-5e87-7d30-af34-12248755e2f1`; every row
listed carries `tenant_id = T`. The queries are abridged; joins resolve ids to codes.

| Ref | Case | Table(s) | Query (abridged) | Result |
| --- | --- | --- | --- | --- |
| DB-01 | BRN-06/08/09 | app.branches, saas.countries, saas.governorates | `select b.code,c.name_en,g.name_en,g.country_id=c.id,b.name_en,b.phone,b.email,b.currency,b.is_active from app.branches b left join … where b.tenant_id=T` | after BRN-08: `V11-NORTH · V11 Land · Alexandria · f`. Final: `V11-NORTH · Egypt · Alexandria · t · V11 North Branch (edited) · فرع الشمال V11 · 0225550222 · v11.north@example.test · EGP · t`; MAIN unchanged |
| DB-02 | BRN-09…12 | audit.field_changes, audit.audit_trail, ops.outbox_events | `… where entity_id='01a12a03-713b-…'` / `aggregate_id=…` | field changes: Created (all fields), Modified CountryId → V11 Land, Modified CountryId / Phone / Name, IsActive t→f, f→t, actor "Test Manager". Trail seq 6 `BranchCreated`, 7 `BranchDeactivated`, 8 `BranchReactivated`, each outbox processed. Detail edits have no trail event (O-5) |
| DB-03 | CG | app.clinical_groups | `select code,unit_code,name_en,name_ar,report_name,display_order,is_active …` | V11-CHEM C 1 t "V11 Clinical Chemistry"; V11-HEMA H 2 t "V11 Haematology Unit"; V11-IMM C 1 t; trail `ClinicalGroupCreated` ×3, `…Changed`, `…Deactivated`, `…Reactivated` |
| DB-04 | ST | app.sample_types, app.sample_conditions | `select st.code,…,string_agg(sc.code||sc.name_en||sc.is_active) …` | V11-EDTA (no condition); V11-SER: FAST t, RAND t, REFR **f**; trail `SampleConditionsChanged` ×2 |
| DB-05 | UN | app.measurement_units | `select name,is_active …` | V11 g/dL t, V11 mmol/L t, V11 U/L f |
| DB-06 | TS-05/08 | app.tests, app.test_range_bands | `select t.code,…,g.code,t.type,st.code,sc.code,u.name,t.decimals,t.duration_hours,t.min_duration_hours,t.frequency_check_days,t.frequency_action,t.delta_percent,t.delta_window_days,t.test_days …` | V11-GLU: Numeric, V11-CHEM, V11-SER / FAST, V11 mmol/L, LOINC 2345-7, Hexokinase, 1 dec, print range, timing 6 / 2 / 30 Alert / 25 % / 90 d / 127. Bands: Male 18–150 Y 4.0–6.0 panic 2.5 / 25 rep 0.5 / 50; Female 18–150 3.8–5.8 same; Any 0–18 3.3–5.5 panic 2.2 / 20. V11-HGB: Any 0–150 12–17 panic 7 / 20 |
| DB-07 | TS-07/10/12/13/17 | app.tests, app.test_list_values, app.test_paragraphs | `select code,type,formula_expression,formula_references,is_active …`; `select value,is_normal,is_critical,is_default,is_placeholder,colour,display_order …` | V11-BG RestrictedList: "—" default + placeholder (1), A #CC0000, B, AB, O. V11-GLUMG `ROUND([V11-GLU] * 18.016, 0)` refs {V11-GLU}. V11-NOTE FormattedText: Normal / Abnormal paragraphs. V11-HGB still Numeric and V11-NOTE still FormattedText after TS-13; V11-NOTE Active. Trail `LabTestCreated` ×5, `…RangesChanged` ×2, `…ListValuesChanged`, `…FormulaChanged`, `…ParagraphsChanged`, `…TimingChanged`, `…Deactivated`, `…Reactivated` |
| DB-08 | SV | app.services, app.service_members, audit.field_changes | `select s.sequence,s.code,s.short_name,s.shortcut_no,s.type,g.code,st.code,sc.code,s.turnaround_hours,s.aliases,s.precaution,s.is_active …`; members joined to tests / services | 1 V11-GLU 1101 Test CHEM SER/FAST 6 {FBS,V11SUGAR}; 2 V11-BG 1103; 3 V11-HGB 1102; 4 V11-GLUP Profile SER/FAST precaution "V11: fast 8 hours before the draw"; 5 V11-PKG Package (no sample). Members: GLUP = GLU 1, GLUMG 2 (in_sum t), NOTE 3; PKG = Service GLUP 1, Service HGB 2. SV-13 evidence: field changes `Service 01a12a0c-d39c (V11-GLUP) IsActive t→f 11:21:41, f→t 11:21:43`; nothing on V11-PKG |
| DB-09 | SQ-01 | app.services | sequence per code | GLU 1, BG 2, HGB 3, GLUP 4, PKG 5; trail `ServiceResequenced` ×2 |
| DB-10 | PR, CM, DR, MB | app.preparations, app.comment_templates, app.doctors, app.drug_types, app.commercial_names, app.antibiotics, app.organisms, app.sensitivity_values | per table, ids resolved | V11-FAST OptionList {8 h,12 h,16 h} Any services V11-GLU,V11-GLUP; V11-PREG YesNo Female all. V11HAEM scope V11-CHEM; V11GEN no scope, edited text. V11-DR01 branches MAIN,V11-NORTH 10.00 %; V11-DR02 all, 5.00 %. Drug type V11 Penicillins; commercial name V11 Amoxil → V11 Penicillins; antibiotic V11-AMX AMX → V11 Amoxil; organism V11-ECOLI "(E. coli)"; sensitivity V11S 1, V11R 2; all Active; matching Created / Deactivated / Reactivated trail events |
| DB-11 | PY | app.payers, app.contract_tiers | `select p.name_en,p.search_key,p.type,p.web_portal_enabled,p.web_username,p.is_active,p.is_default,string_agg(tiers) …` | Cash CASH Cash default, tier Default (default). V11 Delta Corporate V11DELTA Credit, tier V11 Staff. V11 Nile Insurance V11NILE Insurance portal v11.nile.portal, tiers V11 Gold (t) and **V11 Silver Plus (f)** (rename and deactivation stored). Trail `PayerCreated` ×3, `ContractTierAdded` ×4, `ContractTierDeactivated`, `PayerDeactivated`, `PayerReactivated` |
| DB-12 | PL | app.base_price_lists, app.base_price_lines | `select l.code,l.name_en,l.year,l.is_active,copied_from,string_agg(code=price) …` | V11-PL26 "(walk-in)" 2026 **t**: BG 60, GLU 75, GLUP 150, HGB 110, PKG 230.50. V11-PL27 2027 **f**, copied from V11-PL26: 66 / 82.50 / 165 / 121 / 253.50. No PL29. Trail `BasePriceListCreated` ×2, `…PricesChanged`, `…Activated` ×2, `…Deactivated` |
| DB-13 | RL | app.roles, app.role_result_rights | `select name_en,permission_keys,special_discount_cap_percent,special_can_pay,special_can_cancel,is_active …`; rights joined to groups / branches | V11 Reception: 17 keys (billing.create/view, catalogue.view, dashboard.view, patients.create/edit/view, pricing.view, registration.cancel/create/edit/view, results.view, samples.view, treasury.cancel/create/view), cap 15, pay t, cancel t. Rights: V11-CHEM ALL see, V11-HEMA ALL see. V11 Technician: catalogue.view, dashboard.view, results.approve/create/view, samples.view; V11-CHEM **MAIN** see / review / edit / print, V11-HEMA ALL see. V11 Temp role (renamed): Inactive, 1 key. Field changes: SpecialRights.DiscountCapPercent 0→10→15, PermissionKeys without / with pricing.view (RL-12/13). Trail `RoleCreated` ×3, `RoleChanged` ×7 |
| DB-14 | US | identity.platform_users, app.tenant_memberships, app.roles | `select u.username,u.email,u.mobile,u.is_locked_out,u.must_change_password,left(u.password_hash,10),m.employee_code,m.full_name_en,m.is_active,branches,r.name_en,m.added_permission_keys,m.designation,m.shifts …` | v11.reception: v11.reception@example.test, 01005550101, `$argon2id$`, branches MAIN,V11-NORTH, V11 Reception, must-change f after the change. v11.tech: 01005550102, V11-T01, MAIN, V11 Technician, "Chemistry technician", {Morning,Evening}. v11.temp: every branch, V11 Reception, added keys {} after restore (field changes AddedPermissionKeys "" → registration.export → ""). Trail `TenantMembershipCreated` ×3, `…RoleChanged` ×3, `…PrivilegesOverridden`, `…RestoredToRole`, `…Deactivated`, `…Reactivated` |
| DB-15 | US-08…16, US-21 | audit.security_events | `select at_utc,name,severity,actor,detail … where at_utc > now()-'70 min'` | `PRIVILEGE_MATRIX_CHANGED role:…:special-rights` ×2 and `:result-rights` ×2. `MEMBERSHIP_CHANGED` for branches, role, **password-reset (warning)**, **locked (warning)**, overrides, restored, unlocked, reactivated. `USER_DEACTIVATED`. `SIGN_IN_FAILED v11.reception locked_out`. `PASSWORD_CHANGED v11.reception`. `SESSION_CONTEXT_CHANGED branch:…` (MAIN, V11-NORTH). `SESSION_REVOKED logout` per sign-out |
| DB-16 | TR, TA | app.treasuries, app.treasury_access_requests | `select r.number,t.code,b.code,requester,r.device_id,r.device_label,r.status,approver,decider,r.decision_reason …` | V11-MAIN-CASH (MAIN) t; V11-NORTH-CASH "V11 North cashier desk" (V11-NORTH) t. TRQ-2026-00001 wt.admin Cancelled, reason "V11 SoD check…". TRQ-2026-00002 v11.reception **Approved** by wt.admin. TRQ-2026-00003 v11.reception V11-NORTH Rejected "V11: north desk not staffed by reception". All on device a0ab790c-…. Trail `TreasuryCreated` ×2, `…Deactivated`, `…Reactivated`, `TreasuryAccessRequested` ×3, `…Cancelled`, `…Approved`, `…Rejected`, all outbox processed |
| DB-17 | PF-02 | app.visits, app.visit_lines, app.visit_tubes, app.payments, app.patients | `… where v.tenant_id=T` | ACC-2026-00001 · PAT-2026-00001 · MAIN · Cash / Default · V11-DR01 · Open · 320.00 / patient 320.00 / payer 0 · by v11.reception. Lines V11-GLUP 150, V11-HGB 110, V11-BG 60, Ordered, each with a tube, expected at +6 h / +4 h / +2 h. Tubes SMP-2026-00001 C V11 Chemistry V11 Serum Fasting; SMP-2026-00002 H V11 Haematology EDTA. RCT-2026-00001 320.00 Cash V11-MAIN-CASH device a0ab790c v11.reception MAIN Posted. Trail `PatientRegistered`, `VisitRegistered`, `PaymentRecorded`, `SampleCollected` ×2, `SampleReceived` ×2 |
| DB-18 | PF-03…06 | app.results | `select t.code,r.status,r.numeric_value,r.unit,r.decimals,r.range_low,r.range_high,r.range_panic_low,r.range_panic_high,r.flag,r.formatted_report,entered_by,reviewed_by …` | V11-GLU Reviewed 7.2 V11 mmol/L, 1 dec, range 4.0–6.0, panic 2.5 / 25, flag **High**. V11-GLUMG Reviewed 130. V11-NOTE Reviewed, formatted report "No abnormality detected. V11 template. Fasting glucose above the V11 reference.". All entered and reviewed by v11.tech. V11-HGB, V11-BG Ordered. Trail `ResultEntered` ×3, `ResultReviewed` ×3 |
| DB-19 | all | ops.outbox_events, audit.audit_trail | `select count(*),count(processed_at_utc),count(dead_lettered_at_utc) from ops.outbox_events where tenant_id=T and occurred_at_utc > '2026-10-11 11:00+03'`; `select count(*),max(sequence) from audit.audit_trail where tenant_id=T` | 123 / 123 / 0. Trail 128 rows, max sequence 128. At the end, 0 unprocessed or dead-lettered rows for the tenant |
| DB-20 | US, PF | audit.field_changes, ops.outbox_events | `count(*) where changes::text ilike '%PasswordHash%' or '%argon2%' or '%SecurityStamp%' or the V11 passwords` | 0 and 0: no secret in the audit or the outbox |

## 5. Defect log

| Id | Sev | Case | Steps | Expected | Observed | Evidence | Suspected root cause |
| --- | --- | --- | --- | --- | --- | --- | --- |
| B-01 | S4 | BRN-02 | Branches › Add branch | Currency starts at EGP (the field's default and hint) | Currency empty, "This field is required." on save | BRN-01/02 | `shared/ui/master-data-page/master-data-page.component.ts` l.354–360: the `code` field kind builds its control with `''` and ignores `field.defaultValue` (other kinds honour it); `features/organization/pages/branches.page.ts` declares currency as `kind: 'code', defaultValue: DEFAULT_CURRENCY` |
| B-02 | S3 | BRN-08 | Edit V11-NORTH: country V11 Land, governorate Alexandria (Egypt), Save | refused: the governorate must belong to the country (page hint; ORG-008; `branches.page.ts` comment "the server checks the pair") | saved; the branch held a governorate of another country until corrected | DB-01 (`gov_in_country` f) | `src/Platform.Domain/Organization/Branch.cs` `ApplyDetails` (l.120–125) only requires that a governorate has *a* country. `Platform.Application/Organization/Commands/BranchCommands.cs` (create / update handlers) never loads `saas.governorates` to compare `country_id`. The governorate list of the page is not filtered by the chosen country either |
| B-03 | S4 | BRN-05 | Branch with e-mail `not-an-email` | the field is marked with the server's reason | banner "The contact or location data of the branch is invalid."; no field marked; the server said "The e-mail address is not valid." | network 422 ORG-008 | ORG-008 covers e-mail, phone and governorate in one code. `public/i18n/organization.en.json` l.52 / l.352 map it to one generic text. There is no client-side e-mail check (the users page has one) |
| B-04 | **S2** | TS-02, TS-03, TS-06 | Test editor: enter range bands / list values plus another section (timing), with one value the server refuses (minimum > duration CAT-070, overlapping band CAT-054, non-placeholder default CAT-058), Save | the refused section is highlighted with the server's message; everything the user typed stays on screen | **Create:** the test is created, the toast says "…part of its configuration was refused — see the highlighted tab", and the page reopens on the saved test with **no bands / no values** and default timing. Nothing is highlighted and the refusal text (e.g. CAT-058 "only a placeholder can be the default") is never shown. **Update:** when an earlier step succeeded, the editor reloads and drops the entered bands; only the message remains | TS transcripts; network: POST 201, PUT timing 422 then no ranges call; PUT list-values 422 | `features/catalogue/pages-a/test-editor.page.ts`: `createAll()` (l.455–495) navigates to `['..', created.id]` after the first failed step. That is a different route, so a new component and facade are created and the unsaved form state is lost. `updateAll()` (l.545–551) calls `this.facade.detail.reload()` after a failure, which resets the forms from the server. `run()` sets the section problem, but the reload / navigation throws it away |
| B-05 | **S2** | TS-18, SV-13 (TS-13) | Open one service (or test) editor, then another through history navigation (browser back / forward, or a link) | the page shows and acts on the record in the address bar | the page keeps the first record: under `/catalogue/services/<V11-PKG>` the page showed V11-GLUP, and **Deactivate deactivated V11-GLUP**. Same in the test editor (V11-GLU shown under the V11-HGB address); TS-13 ran on V11-NOTE for this reason | SV-13 transcript; DB-08 field changes on V11-GLUP only | `features/catalogue/pages-a/service-editor.page.ts` l.96 and `test-editor.page.ts` l.124 read `this.route.snapshot.paramMap.get('id')` once. The router reuses the component when only `:id` changes, so the id, the facade's detail and every action stay on the old record. The role, user and price-list editors follow the parameter correctly |
| B-06 | S3 | RL-04/05/10/15, US-09/11, PY-07/08 | Save role screen / special / result rights; (de)activate a role; assign a role or branches; lock / unlock / (de)activate / restore a user; reset a password; rename or (de)activate a contract tier | a confirmation; the dialog or form closes | the server answers 204 and stores the change, but the page shows no toast. Reset password keeps its dialog open with the typed passwords (a second click resets again). A tier rename stays in edit mode and shows the stale add-tier error "The payer already has a tier with this name." | network 204s; DB-11/13/14/15 | `features/organization/data/organization.api.ts` (l.98–108, 158 …) and `features/pricing/data/pricing.api.ts` (l.72–78) return `firstValueFrom(http.put<void>(…), { defaultValue: undefined })`. A 204 emits `null`, so the promise resolves `null`. `core/state/mutation.ts` `execute()` returns that `null`. The pages test `!== null` (`role-editor.page.ts` l.242/257/267/284, `user-editor.page.ts` l.353–421, `pricing/pages/payers.page.ts` l.163/187) and treat success as failure. `features/treasury/data/treasury.api.ts` l.48–56 already carries the fix `.then(() => undefined)` ("a 204 body is null; null means failure to mutation()"); it was not applied to organization and pricing. The tier dialog also shows `addTier.problem()` first in `tierProblem` (payers.page.ts l.108) |
| B-07 | S3 | TR-08 | Deactivate a treasury and open Treasury access; reactivate it; return to Treasury access (also after signing out and in as another user); press Refresh | the picker lists the active treasuries | the reactivated V11-NORTH-CASH was missing until a full page reload, including for v11.reception after a sign-out / sign-in in the same tab | TR-08 / TA-06 transcripts | `features/treasury/state/treasury-access.facade.ts` l.24–26: `treasuries = resource({ loader })` has no params, so it loads once. The facade is provided at the route (`treasury.routes.ts` l.9), which lives for the whole app, and Refresh reloads `requests` only. Same family as A-01. If a user of another laboratory signed in on the same tab, the previous laboratory's treasury names would be offered (not tested: other laboratories were out of scope) |
| B-08 | S4 | ST-07 | Deactivate condition REFR of V11-SER; Tests › Add test, sample type V11-SER | only active conditions offered | "REFR · Refrigerated" offered | ST-07 | `test-editor.page.ts` l.711–712 (and `service-editor.page.ts` l.449) put every condition returned by `CatalogueAOptions.conditionsOf()` into the select without filtering `isActive` (patient conditions are filtered, l.219–222) |
| B-09 | S4 | CG-08, PR-08, DR-08 | Export CSV with a status filter or a search | the exported rows are those of the grid, in the grid's formats | the clinical-groups export ignores the Active filter (inactive V11-IMM exported). Formats differ between pages: Active `true` / `false` (catalogue A) vs `Yes` (catalogue B); preparations export raw enums `OptionList`, `YesNo`, `Any`; doctors export the branch **count** instead of the codes; the file-name date is the UTC date | captured CSV text | `features/catalogue/pages-a/clinical-groups.page.ts` l.60–61 calls `api.exportClinicalGroups()` without the page filter, although `data-a/csv-export.ts` says "the server returns the full filtered list". The catalogue-B exporters (`pages-b/*`) map enums and arrays without translation. `csvFileName` uses `toISOString()` |
| B-10 | S3 | BRN-15, RL-17, US-20, PY-15, TR-09 | Branches, Roles & privileges, Users, Payers & contracts, Treasuries | Excel / CSV export (SRS "Grids & editors: … Excel export") | no export action on these five lists (the catalogue lists and the price grid have one) | page actions | the shared `master-data-page` has no export slot, and the organization, pricing and treasury pages add none |
| B-11 | S4 | US-12, all sign-outs | Lock a user, sign in as them; sign out | wording fits an administrator's lock; clean sign-in URL | "The account is locked. Try again later." for an indefinite administrator lock; after sign-out the address is `/login?returnUrl=%2Flogin` | US-12 | one AUTH lock message for the throttle lock-out and the administrator lock; the sign-out navigation passes the current route `/login` as return URL |
| B-12 | S4 | RO-02 | Reception (catalogue.view only) opens Service sequence and a test editor | read-only fields | inputs accept typing; "Discard changes" shown; no Save, so nothing can be stored | RO-02 | `catalogue/pages-a/service-sequence.page.html` and `test-editor.page.ts` disable the save actions with `*appCan` but do not disable the form controls for viewers (the price grid does) |
| B-13 | S4 | TS-09 | Check formula `[V11-GLU] +` | one clear message | "The formula is not valid. (position 12) — The formula ends too early. (position 12)" | TS-09 | the page joins the problem title and the detail, and both carry the position |
| B-14 | S4 | US-10 | User › Role & branches: choose a role, then press the branches Save | both kept, or a warning that the role is not assigned | the role select falls back to "No role" after the branches save; only "Assign role" stores a role | US-10 | `organization/pages/user-editor.page.ts`: the branches save reloads the user and resets the unsaved role choice; one tab has two independent save buttons |

## 6. Observations and open points (not counted as defects)

- **O-1 Expected result time.** At registration the patient sees the expected time from the **service** turnaround:
  HGB +4 h (15:47), BG +2 h (13:47). The worklist and the BR-13 delay use the **test** duration, which defaults to
  24 h. HGB, BG, GLUMG and NOTE were therefore expected on 12/10 11:47. The patient is told one time, and the
  laboratory clock says another. The business owner should decide which drives what.
- **O-2 Unit code is not unique.** Two groups can share unit code `C` (allowed by the CAT intake). Because the
  letter is printed on the labels, ask whether this should be refused.
- **O-3 Sorting is client-side.** Sorting applies to the loaded page only (`data-table` spec). With more rows than
  one page it does not sort the whole list.
- **O-4 Activating a price list.** It has no confirmation and no toast. UAT-FIN-02 step 5 says "Activate,
  confirm". Deactivation does confirm.
- **O-5 Edits without trail events.** Detail edits of branches, payers, tiers, treasuries and price-list names write
  `audit.field_changes` but no `audit.audit_trail` event. Creates and status changes write both.
- **O-6 Contract pricing action (Release 2).** The `Contract pricing` action is still shown on every payer and tier
  (already noted in the laboratory dry run).
- **O-7 Default must-change.** "Must change the password at next sign-in" is ticked by default on Add user. A user
  can be created without a role ("No role").
- **O-8 Shared device id.** The device id `a0ab790c-…` is shared by every user of this browser profile, as it was
  in tester A's run. Treasury approvals therefore follow the browser, not the person.

## 7. Data created (dev database `lis`, Walk-through Laboratory, invented data)

- **Branch:** V11-NORTH "V11 North Branch (edited)", Egypt / Alexandria, Active.
- **Catalogue A.**
  - Clinical groups: V11-CHEM (C), V11-HEMA (H), V11-IMM (C).
  - Sample types: V11-SER (conditions FAST, RAND, REFR inactive) and V11-EDTA.
  - Units: V11 mmol/L, V11 g/dL, V11 U/L (inactive).
  - Tests: V11-GLU (3 bands, timing), V11-HGB, V11-BG (list), V11-GLUMG (formula), V11-NOTE (paragraphs).
  - Services, in sequence order: V11-GLU (1101), V11-BG (1103), V11-HGB (1102), V11-GLUP (profile), V11-PKG
    (package).
- **Catalogue B.**
  - Preparations V11-FAST and V11-PREG.
  - Comments V11HAEM and V11GEN.
  - Doctors V11-DR01 and V11-DR02.
  - Drug type V11 Penicillins, commercial name V11 Amoxil, antibiotic V11-AMX, organism V11-ECOLI, sensitivity
    values V11S and V11R.
- **Pricing.**
  - Payers: Cash / Default (provisioned on the first open); V11 Nile Insurance with tiers V11 Gold and V11 Silver Plus
    (inactive); V11 Delta Corporate with tier V11 Staff.
  - Price lists: V11-PL26 (active, prices cash) and V11-PL27 (inactive copy ×1.1).
- **Roles:** V11 Reception, V11 Technician, V11 Temp role (renamed, inactive).
- **Users:** `v11.reception`, `v11.tech`, `v11.temp`. Their passwords are recorded in `.local/dev-credentials.md`.
  `wt.admin` is unchanged; it now has two branches available because it holds "every active branch".
- **Treasuries and access.** V11-MAIN-CASH (MAIN) and V11-NORTH-CASH (V11-NORTH). Requests: TRQ-2026-00001
  Cancelled, TRQ-2026-00002 Approved (v11.reception, MAIN-CASH, device a0ab790c), TRQ-2026-00003 Rejected.
- **Operations.** Patient PAT-2026-00001 "V11 Setup Walk-in"; visit ACC-2026-00001 (V11-GLUP, V11-HGB, V11-BG,
  320.00 paid with RCT-2026-00001); samples SMP-2026-00001 and -00002. Results: GLU 7.2 H, GLUMG 130 and the NOTE
  text, all Reviewed. HGB and BG are left Ordered.
- No other laboratory was touched. The tenant parameters were not changed: the auto-print switches stay off.

## 8. Not tested or blocked, and why

| Item | Status | Reason |
| --- | --- | --- |
| Paging on the set-up lists (BRN-16 and the others) | NT | no list reached 20 rows, the smallest page size |
| Forced requests (ORG-007 duplicate result right, ORG-011, PRC-021, SOD-TRS-001 approve, TRS-013) | NT | no bearer token is placed in the page; the page did not offer these actions (RL-11, PY-10, TA-02 show the gating) |
| Revoke of an approved treasury request | NT | TRQ-2026-00002 is needed for the proof; revocation was covered by tester A (TRS-06) |
| Cross-laboratory exposure through B-07 | NT | signing in to another laboratory was out of scope |
| Verification and printing of the proof results | NT | outside the set-up scope; the V11 Technician role has no verify right by design |
| Parameters CP-57, contract pricing CP-34, diagnoses, sending labs, shortcut sets, test rules, reasons, report templates, patient conditions, additional services | not in scope | Release 2 routes, not in the client R1 menu |
| Discount at registration | NT | the tenant has no discount reasons (the reasons list is Release 2 and nothing was seeded), so a discount could not be completed; the 15 % cap is displayed ("Your limit is 15 %") |
| Physical CSV files | captured in the page | downloads intercepted, nothing written to disk |

Environment note: other testers' tabs (`tab-1`, `tab-3`, `tab-5`, `tab-7`) were left untouched. This run used only
`tab-8` on `127.0.0.1`. It ends signed in as `wt.admin`, with the viewport emulation cleared.
