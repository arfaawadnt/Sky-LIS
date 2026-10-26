# Sky LIS — Release 1 User Acceptance Test Scripts (LIS-UAT-001)

Version 0.8 · 2026-10-22 · Status: draft for review by the business owner (0.8: Lab Tracker UAT-REG-16, LIS-CR-006; 0.7: sign-in by address; 0.6: treasury removed — see the revision history).

Sources: business SRS LIS-SRS-001 (`docs/srs`, page ids CP-nn / SA-nn, workflows WF-nn, rules BR-nn), the module intakes
(`docs/intake`, acceptance scenario ids and refusal codes), the Release-1 scope record (`docs/build/MODULES.md` §6, wave 4),
the navigation registry (`frontend/src/app/shell/nav.registry.ts`, `release: 'R1'` entries) and the deployment runbook
LIS-DEP-001 §7 (test lane). How to run a session and record results: `docs/uat/README.md`.

## 1. Scope

Release 1 is exactly what the release-1 prototype builds show (`prototypes/dist/*-v2.1-Release1.html` client portal,
`*-v3.1-Release1.html` Super Admin portal). The scripts below cover every Release-1 page of both portals and the
Release-1 parts of workflows WF-01, WF-02, WF-06, WF-09, WF-10, WF-11 and WF-12.

Release-1 behaviour the testers must know before they start:

- **Payment is captured on the registration page** (BR-26) and recorded by Billing right after the visit is saved; the
  Payments page collects the rest.
- **Samples are received automatically on save** (BR-03 emulated by the auto-receive default): the sample pages
  (CP-14…CP-17) are Release 2, so there is no scanning step; results can be entered at once.
- **Registration has no Location/Department and Room fields** (hospital mode is Release 2).
- **Tenant parameters (CP-57) run on the platform defaults**; the client Parameters page is Release 2. The defaults that
  matter here: `discountCapPercent` applies when the role's cap is empty, `reviewBeforeVerify` off, `printUnverified` off,
  `similarNameWarning` on, `autoPrintLabels` / `autoPrintJobOrder` / `autoPrintReceipt` on, `passwordMinLength` 8.
- **Cancellation (CP-26) and refund (CP-24)** are row actions of Search registrations and of the Payments page in Release 1.
- **Contract pricing (CP-34) has no page**: a credit payer's tier quotes nothing until Release 2; cash patients are priced
  from the active base list with the highest year (Pricing intake Q2).

Out of scope (Release 2, no scripts): sample pages, results by sample, work queue, quality control, print reports,
refunds / money transfer / cancellations / batch payment / cash closing pages, claims, financial reports, contract
pricing, home / batch / lab-to-lab registration, analyser integration and the integration library, auto-verification,
client wallet / support / audit pages, and the client-side catalogue-depth lists (patient conditions, diagnoses,
shortcut sets, test rules, reasons, report templates, additional services, sending labs) — their global twins (SA-13)
are Release 1 and are tested.

## 2. UAT environment and test data

| Item | Value |
| --- | --- |
| Environment | The **test lane** of the deployment runbook (LIS-DEP-001 §7): service `NTLIS_TEST` on 5180, site `lis-ui-test`, database `lis_test`, **with a certificate** (over plain http the refresh cookie is refused and every session ends after 15 minutes). |
| Browser | Current Chrome or Edge. One browser **profile per persona** on each test workstation: sessions are kept per browser profile. |
| Printing | Labels, job orders, receipts and reports open the browser's print dialog; a PDF printer is enough. Keep the printed PDFs with the results sheet. |
| SMS / e-mail | The OTP of the subscription wizard, the password recovery and the panic-value notification need the notification gateway of the test lane. When no gateway is configured, the scripts marked *gateway* are executed with the operator reading the code from the API log (only possible when `Lis:Security:AllowInsecureLocalDevelopment` is on, which is never the case on a production-like lane) or are deferred and recorded as "blocked — gateway". |

Seeding (`Platform.DevTools.exe` from the package `tools` folder, `LIS_DEVTOOLS_CONNECTION` pointing at `lis_test`
with the `lis_app` role; runbook §5.7):

```powershell
Platform.DevTools.exe seed-platform-admin platform.admin <one-time password>      # Super Admin portal (SA-01)
Platform.DevTools.exe seed-demo-lab lab.admin <one-time password>                 # the UAT laboratory
```

`seed-demo-lab` creates one laboratory tenant with: branch **MAIN** (currency EGP); clinical groups **CHEM** (Clinical
Chemistry) and **HEMA** (Haematology); sample types **SER** (Serum) and **EDTA** (Whole blood); units mmol/L, mg/dL, g/dL;
tests **K** (Potassium, 3.5–5.0, panic 2.5 / 6.5, 1 decimal), **NA** (Sodium, 135–145, panic 120 / 160), **GLU** (Glucose
fasting, 70–110, panic 40 / 400), **HGB** (Haemoglobin, EDTA, 12.0–16.0, panic 7.0 / 20.0); one Test service per test
priced **120 / 120 / 90 / 150** EGP on the active base list **CASH-2026**; the eight
reasons of `seed-reasons` (cancellation "Patient declined" / "Ordered by mistake", refund "Service cancelled" /
"Overpayment", discount "Staff and relatives" / "Management approval", rejection ×2); the role **Laboratory
Administrator** (every permission, every special right, every result right) assigned to **lab.admin** and
**lab.admin.manager** (the shift manager). Every laboratory user signs in at the **laboratory address** `<base>/lab/demo-lab` (the demo laboratory; another laboratory at
`<base>/lab/<its slug>`), every Super Admin persona at **`<base>/admin/login`** (BR-28, change request LIS-CR-005); `<base>` is the
`Lis:Platform:PortalBaseUrl` of the lane.

The remaining UAT users and roles are **created during the UAT itself** by the laboratory administrator scripts
(UAT-ADM-02, UAT-ADM-03) and the Super Admin scripts (UAT-SUP-01, UAT-SUP-02); section 4 lists what each persona's role
must hold. A second laboratory ("UAT Lab B") is created by UAT-OPS-02 and is used for the isolation checks.

Run order: UAT-SUP-01/02 → UAT-ADM-01…07 → UAT-ADM-08…10 (catalogue additions the reception and results scripts
depend on) → UAT-FIN-01…03 → UAT-REG → UAT-SMP → UAT-RES → UAT-VER → UAT-CSH-03…08 → UAT-FIN-04…06 →
UAT-OPS, UAT-MDR, remaining UAT-SUP. Scripts inside a persona run in their numbered order unless a script says otherwise.

## 3. Script conventions

- **Id** `UAT-<area>-nn`; **Priority** *critical* (the acceptance criterion counts it) or *standard*.
- **Refs**: SRS pages, workflow, business rules, intake scenario ids (`REG S-V1-ok` = scenario S-V1-ok of
  LIS-INT-REG-001 §16). A refusal code (e.g. `BIL-010`) names the rule; the screen shows the **translated message** of
  that code (quoted in the expected result), never the code itself. The code is visible in the API's problem details
  (browser developer tools › Network) when a tester needs to confirm it. Some rules are enforced by the page before any
  call is made (a required field, a format, a disabled button); the expected result says so ("refused by the page") and
  the API code applies only to a forced request.
- **Menu paths** use the English sidebar labels of `shell.en.json` (`Reception › Registration`); the Super Admin portal is
  prefixed `SA ›`. Routes are given once per page for the record.
- **Negative tests** are inside the script that owns the rule; they are numbered steps whose expected result is a refusal.
- **Arabic check**: every script ends with one step: switch the language with the top-bar switch (`Switch language`),
  confirm the page is laid out right-to-left with Arabic labels and Arabic master-data names, then switch back. The
  document is not translated; the check only proves the page is bilingual.
- **Result box**: `Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____`. A script fails when any expected
  result differs; record the step number and the defect reference.
- Data placeholders: `<pw>` = the persona's password; amounts in EGP.

## 4. Personas, roles and preconditions

| Persona | UAT user | Role to create (permission keys · special rights · result rights) | Preconditions |
| --- | --- | --- | --- |
| Laboratory administrator | `lab.admin` (seeded) | Laboratory Administrator (seeded: everything) | branch MAIN; `lab.admin.manager` available as a second administrator for the SoD steps |
| Shift manager (approver) | `lab.admin.manager` (seeded) | same role | second administrator account |
| Cashier | `uat.cashier` | **Cashier**: dashboard.view · registration.view · billing.view/create/approve/cancel/export · pricing.view · catalogue.view (the refund reason list of CP-24 is read from the catalogue) · special: CanPay, CanRefund, CanCancel, CanPrintInvoice · no result rights | branch MAIN; own browser profile |
| Phlebotomist / technician (sample side) | `uat.reception` and `uat.tech` | as Reception / Technician | visits registered by UAT-REG-03 |
| Laboratory technician | `uat.tech` | **Technician**: dashboard.view · results.view/create/approve/export · catalogue.view · result rights: see, review, edit, print on **CHEM and HEMA**, every branch (no verify) · no special rights | branch MAIN |
| Technician, chemistry only | `uat.tech.chem` | **Technician (chemistry)**: as Technician but result rights on **CHEM only** | used by UAT-RES-08 |
| Unit head / pathologist | `uat.head` | **Unit head**: results.view/create/edit/approve/cancel/export/manage · catalogue.view · result rights: see, review, verify, edit, print on CHEM and HEMA, every branch · special: CanModifyVerifiedResult (not "preview without verify") | branch MAIN |
| Accounts | `uat.accounts` | **Accounts**: pricing.view/create/edit/manage/export · billing.view/export · registration.view · catalogue.view · special: CanPrintInvoice (no CanPay, no CanRefund) | branch MAIN |
| Operations Admin (NT) | `uat.ops` | **Operations Admin** (platform role): platform.view · tenancy.view/create/edit/manage · wallet.view/create/manage (the catalogue has no `wallet.edit`) · notifications.view | Super Admin portal |
| Master Data Reviewer (NT) | `uat.mdr` | **Master Data Reviewer**: platform.view · catalogue.view/create/edit/manage/export | Super Admin portal |
| Super Admin (NT) | `platform.admin` (seeded) | Platform Administrator (seeded: every permission) | Super Admin portal |

The privilege matrix uses the platform's module × action keys (view, create, edit, approve, cancel, export, manage,
viewConfidential; Organization intake A4). `approve` on results covers review, verify and delta acknowledgement; the
**result rights** decide which clinical groups each action touches (RES-050…RES-054).

---

## 5. Scripts

### 5.1 Laboratory administrator (UAT-ADM)

#### UAT-ADM-01 — Branches · critical
LIS-CR-008 (C-04): the branch editor offers City / markaz → District → Area / village under the governorate (optional, cascading); a city of another governorate is refused with GEO-020.
Refs: CP-36 · WF-01 step 3 · ORG AS-B1, AS-B2, AS-B3.
Sign in as `lab.admin` at `<base>/lab/demo-lab`. Setup › Branches (`/organization/branches`).
1. `Add branch`: code `north`, name EN "North Branch" / AR "الفرع الشمالي", country Egypt, governorate, phone, e-mail, currency `EGP`. Save.
2. `Add branch` again with code `NORTH`.
3. Edit NORTH: currency `EG`. Save.
4. On NORTH choose `Deactivate`, confirm. Then on MAIN choose `Deactivate`.
5. Reactivate NORTH.
6. Arabic check.
Expected: (1) the row shows code **NORTH** (upper-cased), status Active. (2) refused `ORG-100` "A branch with this code already exists." (3) refused by the request validator (`VALIDATION-400`) with the field message "'Currency' must be 3 characters in length" — the domain code `ORG-009` guards the same rule. (4) NORTH becomes Inactive ("Branch deactivated."); the second deactivation is refused `ORG-110` — one branch always stays active. (5) NORTH Active again. The audit log of the laboratory is Release 2; the Super Admin cannot see tenant trails — no audit check here.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-02 — Roles and privileges · critical
Refs: CP-37 · WF-11 · BR-17 · ORG AS-R1…AS-R5.
Setup › Roles & privileges (`/organization/roles`).
1. `Add role`: name "Reception" / "الاستقبال", description. Save, then open the screen-rights tab and tick the keys of section 4 (Reception). Save.
2. Tab `Special rights`: discount cap 10, CanPay, CanCancel. Save. Tab `Result rights`: see on CHEM and HEMA, `Every branch`. Save.
3. Repeat for Cashier, Technician, Technician (chemistry), Unit head, Accounts exactly as in section 4.
4. Create a role "Empty" with no screen right and try to activate it.
5. On the Reception role set the discount cap to 101. Save.
6. On the result rights of Technician add CHEM twice.
7. After UAT-ADM-03 assigned users: `Deactivate` the Reception role.
8. Change the Reception role: remove `registration.cancel`; sign in as `uat.reception` in another profile and open Search registrations.
9. Restore `registration.cancel` to the role.
10. Arabic check.
Expected: (1–3) six roles Active with the rights saved; the screen-rights tab reads "Changes apply to every member of the role at their next action." (a new role is created from the screen-rights step with its first permissions; the special and result rights are saved on their tabs afterwards; saving the rights of a role with members asks "The change applies to all of them. Continue?"). (4) refused by the page: "Grant at least one permission before creating the role." (`ORG-005` on a forced request). (5) refused: the cap field reads "The discount cap must be between 0 and 100." and nothing is saved (`ORG-006`). (6) not reproducible through the grid (one row per clinical group); `ORG-007` applies to a forced request only. (7) the `Deactivate` button is disabled with "A role with members cannot be deactivated." (`ORG-011` on a forced request). (8) the `Cancel services` row action is gone for the clerk without a new sign-in (BR-17); the API answers `AUTHZ-001` when the action is forced. (9) the action is back.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-03 — Users · critical
Refs: CP-38 · WF-11 · ORG AS-U1, AS-U3 · ACC Q13.
Setup › Users (`/organization/users`).
1. `Add user`: username `uat.reception`, full name, e-mail, mobile, designation, initial password `<pw>`, confirm, `Must change the password at next sign-in` ticked, role Reception, branches MAIN. Save.
2. Repeat for `uat.cashier`, `uat.tech`, `uat.tech.chem`, `uat.head`, `uat.accounts` with their roles (section 4), **without** must-change.
3. `Add user` with username `abc`.
4. `Add user` with a 6-character password while `passwordMinLength` is 8.
5. `Add user` with username `uat.x` and password `uat.x2026!`.
6. In a new browser profile sign in as `uat.reception` with `<pw>`.
7. Change the password on the profile page (current + new + confirm), then open the dashboard.
8. Arabic check.
Expected: (1–2) seven users Active, column Role filled, `Inherits the role`; the list marks `Must change password`. (3) refused by the page under the username ("The value has the wrong format." with the hint "4–30 lower-case letters, digits, \".\" or \"_\""; `ORG-023` on a forced request). (4) refused by the page: "At least 8 characters." (`ORG-020` on a forced request). (5) refused `ORG-021` "The password must not contain the username." (6) the user lands on Account › Profile with "Your administrator asked you to choose a new password before you continue."; other pages redirect back to it. (7) "Your password was changed."; the dashboard opens; the sidebar shows Reception, Laboratory (Worklist, Results by accession), Finance (Payers & contracts and Base price lists read-only, Payments), the read-only catalogue lists under Setup (`catalogue.view`) and Account — no Branches, Roles & privileges or Users.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-04 — Lock, reset, protected administrator · standard
Refs: CP-38 · ORG AS-U2, AS-U3, AS-U5, AS-U6 · SOD-ORG-001, TEN-012.
1. Users › `uat.accounts` › `Lock`. In another profile sign in as `uat.accounts`.
2. `Unlock`; sign in again.
3. `uat.accounts` › `Reset password`: temporary password. Sign in with the old password, then with the temporary one.
4. Open `lab.admin` (the primary administrator) while signed in as `lab.admin.manager`: `Lock`, `Reset password`, change role.
5. Signed in as `lab.admin`, open your own user and change your role or branches.
6. Arabic check.
Expected: (1) sign-in refused `AUTH-003` "The account is locked"; any session of the user open at the time is ended. (2) sign-in works. (3) old password `AUTH-002`; temporary password works and lands on the profile with must-change. (4) all three refused — the main administrator is protected (`TEN-012`; the UI shows "The main administrator keeps the administrator role"). (5) refused `SOD-ORG-001` (an administrator cannot change their own access).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-05 — Per-user overrides and restore · standard
Refs: CP-38 · WF-11 step 3 · BR-17 · ORG AS-U4.
1. Users › `uat.tech` › privileges tab: add `results.cancel`; `Save privileges`.
2. Sign in as `uat.tech` in another profile and open Results by accession of a reviewed visit (after UAT-RES-04): the `Undo` button.
3. Create a second member of the Technician role (`uat.tech2`, UAT-ADM-03 pattern) and sign in as them on the same visit.
4. Back as `lab.admin`: `Restore to role`, confirm.
5. Arabic check.
Expected: (1) the user is marked **Modified**, "Differences from the role" lists `results.cancel`. (2) `Undo` is available to `uat.tech`. (3) `uat.tech2` has no `Undo`: an override affects one user only (BR-17). (4) the badge disappears, "The user inherits the role again."; `Undo` is gone for `uat.tech`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-06 — Treasuries · withdrawn
**Withdrawn on 2026-10-20** (change request LIS-CR-004: the treasury was removed from the system — Finance › Treasuries no longer exists). The id stays reserved; nothing to run.

#### UAT-ADM-07 — Treasury access decisions · withdrawn
**Withdrawn on 2026-10-20** (change request LIS-CR-004: the treasury was removed from the system — payments need no device approval). The id stays reserved; nothing to run.

#### UAT-ADM-08 — Catalogue A: groups, sample types, units, tests, services · critical
LIS-CR-007: a sample type has the switch "Used for cultures"; switch it on, save and find the type on Microbiology › Culture specimens (off → gone). A Culture service's sample-type picker lists the culture specimens first; a Test service shows one list. *Shortcut sets* and *Test rules* are in the Setup menu of the laboratory (UAT-MDR-02 steps 3–4 apply to the client pages too).
Refs: CP-39, CP-40, CP-41, CP-43, CP-44 · BR-08, BR-19, BR-24 · CAT S-T1-ok/code, S-T9-test, S-T9-members, S-T15, S-T16, S-T17, S-T14-type.
Setup › Clinical groups / Sample types / Units of measurement / Tests / Services. These additions feed UAT-REG-05, UAT-REG-14, UAT-VER-03 and UAT-RES-02.
1. Clinical groups › `Add clinical group`: code `MICRO`, unit code `M`, names, display order 3. Save. Then add another with code `micro!`.
2. Sample types › `Add sample type`: code `URN`, Urine; `Add condition` "Random" and "24 h". Save. Add the condition "Random" again.
3. Units › `Add unit`: `IU/L`. Save.
4. Tests › `Add test`: code `HCG`, "hCG (serum)", group CHEM, type Numeric, sample type SER, unit IU/L, **gender Female**, 1 decimal. Tab `Reference ranges`: band Any sex 0–150 years 0–5, panic high 100 000. Save.
5. Tests › `Add test`: code `CREA`, Creatinine, CHEM, Numeric, SER, mg/dL, 2 decimals; ranges: Male 18–150 y 0.7–1.3, Female 18–150 y 0.6–1.1, Any 0–18 y 0.3–0.7; **Timing** tab: duration 24 h, frequency check 30 days (Alert), delta 20 %. Save.
6. On CREA add a band Male 30–60 y 0.8–1.2 (overlaps the male adult band). Save.
7. On CREA add a band Any 0–150 y with normal 1–2 and panic low 1.5. Save.
8. Tests › `Add test`: `BLDGRP`, Blood group, HEMA, type Restricted list, EDTA; list values `A`, `B`, `AB`, `O`, default `—` marked placeholder. Save. Then set the default to `O` (not a placeholder). Save.
9. Tests › `Add test`: `EGFR`, type Formula, formula `ROUND(175 * CREA ^ -1.154, 0)`; then try the formula `CREA +`.
10. Tests › change HCG's type to Restricted list while it has range bands.
11. Services › `Add service`: code `HCG`, short name, names, group CHEM, type Test, sample type SER, **gender Female**, duration 24 h, member test HCG. Save. Repeat `CREA` (member CREA; also member EGFR in a Profile service `RENAL` with members CREA + EGFR, sample SER) and `BLDGRP` (HEMA, EDTA).
12. `Add service` of type Test with two member tests; then a Profile with no sample type.
13. Tests › open CREA: read the **Parent services** block of the General tab. Remove `RENAL` (the ×), save. In the picker search `ren`, `Add` RENAL, save. Then open HCG and look at its `HCG` chip.
14. Arabic check (open the Tests list in Arabic: Arabic test names appear in the grid).
Expected: (1) MICRO Active; the second is refused by the page ("Use upper-case letters, digits, '-', '_' or '.', starting with a letter or digit."; `CAT-001` on a forced request). (2) URN with two conditions (the `Conditions` row action); the duplicate condition is refused `CAT-011`. (3) IU/L listed. (4–5) tests saved; the ranges tab lists the bands. (6) refused `CAT-054` (overlapping bands for the same sex and condition). (7) refused `CAT-053` (panic inside normal). (8) saved; ticking `Default` on `O` ticks `Placeholder` with it (the editor's BR-19 affordance) — untick `Placeholder` on `O` and save: refused `CAT-058` "Only a placeholder can be the default". (9) the valid formula is saved normalised (`ROUND(175 * [CREA] ^ -1.154, 0)`); the broken one is refused `CAT-063`. (10) refused `CAT-071`. (11) services Active with the next sequence numbers. (12) refused `CAT-021`; the profile without a sample type is refused by the page ("This field is required."; `CAT-029` on a forced request). (13) CREA shows the chips `CREA` (type Test, marked "from the service", no ×) and `RENAL` (Profile); after the first save Services › RENAL carries EGFR only; the picker offers only profiles and cultures the test is not in (RENAL, not CREA); after the second save RENAL carries EGFR then CREA (CREA last, printed, not in the sum). HCG's chip `HCG` has no × (forcing the call answers `CAT-021`).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-09 — Service sequence · standard
Refs: CP-42 · CAT S-T12.
Setup › Service sequence (`/catalogue/service-sequence`).
1. Swap the sequences of K and NA in one save (Ctrl+S).
2. Give GLU the same sequence as HGB. Save.
3. Set HCG's sequence to 0. Save.
4. Arabic check.
Expected: (1) "2 sequence(s) saved."; the registration service picker and the report list K and NA in the new order. (2) refused `CAT-105`. (3) refused `CAT-031`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-10 — Catalogue B: preparations, comment library, doctors, microbiology · standard
LIS-CR-007: add a doctor with the code left blank → the code is the laboratory's next `DR-001`, `DR-002` … (placeholder "Generated on save", no required mark; read-only on Edit). Microbiology tabs read Organisms · Antibiotics · Antibiotic classes · Commercial names · Sensitivity values · Culture specimens and the page opens on Organisms; "Add drug type" is "Add antibiotic class"; the commercial-name field is "Class".
Refs: CP-46, CP-47, CP-48, CP-56 · CTB S-CTB-T1-ok, S-CTB-T1-options, S-CTB-T1-share, S-CTB-T2-ok, S-CTB-T3/T4.
1. Setup › Preparations › `Add preparation`: code `FAST`, question "Fasting hours?" / Arabic, type Option list, options `8 h`, `12 h`, linked services GLU and CREA. Save. Add `FAST2` of type Option list with one option.
2. Setup › Comment library › `Add comment`: short name `HAEM`, text EN/AR "Sample slightly haemolysed; potassium may be raised.", scope CHEM. Save.
3. Setup › Doctors › `Add doctor`: code `DR01`, name, specialty, branches MAIN, share 10 %. Save. Add `DR02` with share 150.
4. Setup › Microbiology: tab Drug types › add "Penicillins"; Antibiotics › add `AMX` Amoxicillin; Organisms › add `ECOLI` Escherichia coli. Deactivate and reactivate the organism.
5. Edit the preparation FAST: change an option text; the code field.
6. Arabic check.
Expected: (1) FAST Active with two options; `FAST2` refused `CAT-210`. (2) comment listed with scope CHEM. (3) DR01 Active; DR02 refused `CAT-220`. (4) three records Active; the organism goes Inactive then Active again. (5) the code is read-only; the option change is saved.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-ADM-11 — Profile: language and password · standard
Refs: CP-65 · ACC S-A1, S-A2.
Account › Profile (`/account/profile`).
1. Language: Arabic › `Save language`. Sign out and sign in again.
2. Change password: wrong current password; then a new password containing `lab.admin`; then a valid one.
3. With a second session of `lab.admin` open in another profile, complete the valid change in the first.
4. Arabic check (already in Arabic after step 1; confirm the sign-in page opened in Arabic).
Expected: (1) "Your language was saved."; the sign-in page and the portal open in Arabic, right-to-left. (2) `ACC-010` "The current password is not correct."; `ACC-013` "must not contain your username"; then "Your password was changed." (3) the second session is signed out at its next request; the first stays signed in. The laboratory details block shows lab name, portal address, country and plan (or "not available" for a seeded laboratory without a client record — accepted, note it).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.2 Accounts (UAT-FIN)

Sign in as `uat.accounts`.

#### UAT-FIN-01 — Payers and contract tiers · critical
Refs: CP-32 · WF-01 step 4, WF-10 · BR-06 · PRC S-P1-ok, S-P1-web, S-P1-dup, S-P3-default, S-P5-dup.
Finance › Payers & contracts (`/pricing/payers`).
1. Open the page: the default **Cash** payer with its **Default** tier exists (provisioned on first open).
2. `New payer`: name "AXA Insurance" / Arabic, search key ` axa `, type Insurance, portal enabled, web username `axa.portal`. Save. `Add tier` "Gold" / Arabic, then "Silver". (When AXA / Gold already exist from an earlier session, add the missing tier only.)
3. `Add tier` "Gold" again on AXA.
4. `New payer` with search key `axa`.
5. `New payer` with the portal enabled and no web username.
6. On the Cash payer: `Deactivate`; on its Default tier: `Deactivate tier`.
7. `Deactivate` AXA, confirm; then `Reactivate`.
8. Arabic check.
Expected: (1) Cash / Default present. (2) AXA Active, search key **AXA**, two tiers. (3) refused `PRC-103` "The payer already has a tier with this name." (4) refused `PRC-100` "Another payer uses this search key." (5) refused by the request validator (`VALIDATION-400`) with the field message "'Web Username' must not be empty." — the domain code `PRC-003` guards the same rule. (6) the payer is refused `PRC-020` "The default cash payer cannot be deactivated or change type."; the Default tier offers no `Deactivate tier` action (`PRC-021` on a forced request). (7) the confirm dialog reads "its tiers quote nothing"; Inactive (listed under the status filter All), then Active after `Reactivate`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-FIN-02 — Base price lists: edit and activate · critical
Refs: CP-33 · WF-10 step 1 · BR-07 · PRC S-L3-ok, S-L3-price.
Finance › Base price lists (`/pricing/base-price-lists`).
1. Open **CASH-2026**: status Active, "Prices cash patients", four lines (K 120, NA 120, GLU 90, HGB 150).
2. `Add a service`: HCG → price 250; CREA → 80; RENAL → 150; BLDGRP → 60. Ctrl+S.
3. Set NA to `-5`. Save. Then to `100.555`. Save.
4. `New list`: code `PL27`, name "Cash prices 2027", year 2027. Save. Open it: add K at 130. Save.
5. On PL27 click `Activate`, confirm.
6. Reception › Price query (as `uat.reception`, after UAT-ADM-03): quote K.
7. Back on PL27: `Deactivate`.
8. Arabic check.
Expected: (1) as stated (`Open price grid`). (2) "Prices saved: 4 added, 0 changed."; eight lines ("8 active of 8"). (3) both refused by the page: "Correct the highlighted prices first." (nothing is sent; `PRC-006` on a forced request). (4) "List saved."; PL27 is created **inactive** ("New lists start inactive") and its price grid opens at once. (5) Active. (6) K is quoted **130** (the active list with the highest year prices cash patients, Pricing Q2), GLU is "Not priced under this contract" (not on PL27). When the Accounts persona runs this step, the code is picked from the picker (see FIN-04). (7) Inactive; `Clear` the quote and add K again: 120.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-FIN-03 — Copy a list and Apply · standard
Refs: CP-33 · WF-10 steps 1–2 · BR-07 · PRC S-L4-ok, S-L4-neg.
1. CASH-2026 › `Copy list`: code `PL28`, multiply by factor `1.1`, round to `0.5`. `Create copy`.
2. `Copy list` again: add amount `-500`, code `PL29`.
3. On CASH-2026 press `Apply`.
4. Arabic check.
Expected: (1) "List PL28 created (inactive)."; K = 132.0, NA = 132.0, GLU = 99.0, HGB = 165.0, HCG = 275.0, CREA = 88.0, RENAL = 165.0, BLDGRP = 66.0, same active flags. (2) refused `PRC-012`. (3) the derived-contracts panel reads "No tier uses this list yet." and Apply reports nothing to refresh — in Release 1 no contract pricing is derived (CP-34 is Release 2); record the message shown.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-FIN-04 — Price query for a credit payer · standard
Refs: CP-12 · BR-06, BR-07 · PRC S-Q-cash, S-Q-tier.
Reception › Price query (`/registration/price-query`) — Accounts holds `registration.view` and `pricing.view`.
1. Payer Cash (default tier), services K, GLU, HCG with patient gender Female. `Print quote`.
2. Payer AXA, tier Gold, same services.
3. Add HCG with patient gender Male.
4. Arabic check.
Expected: (1) K 120 / patient share 120 / payer 0, GLU 90, HCG 250, preparation "Fasting hours?" shown on GLU, expected result dates by turnaround; the quote prints with "This quote is for information only". Typing a code and Enter opens the picker for Accounts (the server-side service-entry resolution needs `registration.create`, REG intake §6; the picker's catalogue lookup needs `catalogue.view` only): pick the service there. (2) every line "Not priced under this contract" (no contract pricing in Release 1). (3) the line is refused as restricted to the other gender (`REG-015`) or shown as not orderable.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-FIN-05 — Payments register and receipts · standard
Refs: CP-23 (Register tab) · BIL §14.
Run after UAT-CSH-07. Finance › Payments (`/billing/payments`) › tab `Register`.
1. Filter the day: list the receipts of the UAT visits with method, cashier and status (Posted / Voided).
2. `Reprint` a receipt.
3. Enter the accession of UAT-REG-03 on the `Collect` tab: the account shows total, discount, patient share, paid, refunded, remaining; the `Pay` button.
4. Arabic check.
Expected: (1) every payment of UAT-CSH and UAT-REG appears once; voided receipts are marked. (2) the receipt prints with "Reprint" and the figures as of printing. (3) figures match the visit; the `Pay` button is absent (Accounts holds no `billing.create`); a forced request answers `AUTHZ-001`. A role holding `billing.create` **without** the CanPay special right would be refused `BIL-040` "Your role does not hold the payment right." (the SPA cannot see special rights, so it shows the server's message).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-FIN-06 — Invoice · standard
Refs: CP-23 · BIL S-RIGHTS (BIL-043).
1. On the account of UAT-REG-03 choose `Print invoice`.
2. Sign in as `uat.reception` and repeat.
3. Arabic check.
Expected: (1) the invoice prints: visit header, lines with shares, payments, balance. (2) refused `BIL-043` "Your role does not hold the right to print invoices." (Reception lacks CanPrintInvoice).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.3 Cashier (UAT-CSH)

Sign in as `uat.cashier` in the cashier's browser profile; branch MAIN (auto-selected: one branch assigned).

#### UAT-CSH-01 — Request treasury access from this device · withdrawn
**Withdrawn on 2026-10-20** (change request LIS-CR-004: the treasury was removed from the system — there is no access request). The id stays reserved; nothing to run.

#### UAT-CSH-02 — Payment refused before approval · withdrawn
**Withdrawn on 2026-10-20** (change request LIS-CR-004: the treasury was removed from the system — a payment needs no approval). The id stays reserved; nothing to run.

#### UAT-CSH-03 — Full payment and receipt · critical
LIS-CR-007: after scanning the accession the cursor is in the amount; *Take a payment* sits in the first row beside the visit totals; F10 pays.
Refs: CP-23 · WF-09 step 2 · BR-26 · BIL S-T1-ok.
Visit: the one UAT-REG-09 saved without payment (patient share 210).
1. Payments › `Collect` › accession → account: remaining 210, status Unpaid.
2. `Pay`: amount defaults to 210, method Cash; the card reads "Cashier: <your name>". `Pay`.
3. `Reprint` the receipt.
4. Arabic check.
Expected: (2) "Payment recorded — receipt RCT-2026-nnnnn."; the receipt prints (auto-print); account paid 210, remaining 0, status **Paid**; the payment row shows receipt, time, amount, method, reference, cashier and status; the payment is stamped with the cashier and the session branch (no treasury, no device anywhere on the page, the receipt or the CSV). (3) a reprint copy. Search registrations shows the visit Settled.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-CSH-04 — Partial payments, card reference · critical
LIS-CR-007: with more than three payments the list shows the latest three and *Show all (n)* shows every one.
Refs: CP-23 · WF-09 step 2 · BR-26 · BIL S-T1-ok, S-T1-ref.
Visit: UAT-REG-08 (150 due, 100 paid at registration, 50 remaining).
1. `Pay`: 20, method Card, reference empty. `Pay`.
2. Same with reference `SLIP-1`.
3. `Pay` the remaining 30 by POS with reference `POS-7`.
4. Arabic check.
Expected: (1) refused by the page: the Reference field reads "This field is required." under the hint "required except for cash" (`BIL-003` on a forced request). (2) receipt; remaining 30, status Partial ("Partly paid"). (3) receipt; remaining 0, Paid; three receipts listed on the account (RCT at registration + two).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-CSH-05 — Payment above the remaining balance · critical
Refs: CP-23 · BR-26 · BIL S-T1-over, S-T1-amount.
Visit: any visit with remaining 50 (register one as the clerk with GLU only, paying 40).
1. `Pay` 60 cash.
2. `Pay` 0; then `-5`; then `10.005`.
3. `Pay` 50.
4. Arabic check.
Expected: (1) refused by the page: "The value must be at most 50." (nothing is sent; `BIL-010` "The amount paid exceeds the remaining balance of the visit." on a forced request). (2) refused by the page: "The value must be at least 0.01." for 0 and −5, "The value has the wrong format." for 10.005 (`BIL-001` / `BIL-002` on a forced request). (3) recorded; "Nothing is due on this visit." afterwards.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-CSH-06 — Void a payment · standard
Refs: CP-23 · BR-15 · BIL S-T2-ok, S-T2-reason, S-T2-state.
1. On the account of UAT-CSH-05: on the 50 receipt choose `Void`, leave the reason empty, confirm.
2. `Void` with reason "wrong visit".
3. `Void` the same receipt again (from the Register tab).
4. Arabic check.
Expected: (1) refused — a reason is required (`BIL-005`). (2) "Receipt … voided."; the row is **Voided** with reason, user and time; paid drops by 50, remaining 50; the receipt reprint carries **VOID**. (3) refused `BIL-012`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-CSH-07 — Refund a negative balance · critical
Refs: CP-23 ("Refund the balance"), CP-24 · WF-09 step 4 · BR-14, BR-15 · BIL S-T3-ok, S-T3-over, S-T3-reason.
Run after UAT-REG-11 cancelled a paid service (balance −150, status **Refund due**).
1. Payments › `Collect` › that accession: `Refund the balance` (a link that opens Finance › Refunds, `/billing/refunds?visitId=…`, with the visit's account).
2. In the refund form: amount 200, reason "Service cancelled", cash. `Refund`.
3. Amount 150, no reason. `Refund`.
4. Amount 150, reason "Service cancelled". `Refund`. Print the refund receipt.
5. `Refund the balance` again.
6. Arabic check.
Expected: (1) the form shows "Refundable: 150.00 EGP" and the reason list of kind Refund (the Cashier role needs `catalogue.view` for it, §4; without it the form reads "The refund reasons could not be loaded."). (2) refused by the page: "The value must be at most 150." (`BIL-011` "The amount exceeds the refundable balance." on a forced request). (3) refused by the page: the Reason field reads "This field is required." (`BIL-004` on a forced request). (4) "Refund RFD-2026-nnnnn recorded; print the receipt for the patient's signature."; the refund receipt shows amount, method, reason and "I received the amount above." signature box; remaining 0; the refunds register lists the refund with reason and cashier. (5) the Refunds form reads "Nothing is refundable on this visit."; the Payments account offers no `Refund the balance` link any more.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-CSH-08 — Payment stamped with the session branch · standard
Refs: CP-23 · CP-04 · LIS-CR-004 (cashier + session branch), ORG AS-S1.
Precondition: the cashier is assigned MAIN and NORTH (UAT-ADM-03 edit).
1. Sign in, choose branch **NORTH** on the branch selection page.
2. `Pay` a balance of a NORTH visit.
3. Payments register: filter the day; open the payment's receipt.
4. Arabic check.
Expected: (1) the branch page lists only the assigned branches (CP-04). (2) "Payment recorded"; no treasury or device is asked. (3) the row and the receipt carry the cashier and branch **NORTH**; a MAIN session does not see it in the branch's cash of the day.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-CSH-09 — Revoked access · withdrawn
**Withdrawn on 2026-10-20** (change request LIS-CR-004: the treasury was removed from the system — there is no access to revoke). The id stays reserved; nothing to run.

#### UAT-CSH-10 — Cancel own request · withdrawn
**Withdrawn on 2026-10-20** (change request LIS-CR-004: the treasury was removed from the system — there is no access request). The id stays reserved; nothing to run.

#### UAT-CSH-11 — Cash ledger, cash entries and the day close · critical
Refs: CP-70, CP-30, CP-23 · BR-16, BR-30 · LIS-CR-011 (reviewer point C-15).
As `uat.cashier` (own drawer), then a supervisor with `billing.approve`, `billing.manage` and the "view branch collections" right.
1. Finance › Cash ledger. Today's own drawer shows: opening 0 (or the last count); *Close the day* is offered only to the supervisor.
2. Take a cash payment of 100 (UAT-CSH-03); *Add cash in* 200 (Opening float); *Add cash out* 50 (Supplier, reason "Courier").
3. *Add cash out* with the category Opening float; then a cash out with an empty reason.
4. As the cashier without the branch right, look for the *Whole branch* scope; as the supervisor choose *Whole branch*, then one cashier.
5. As the supervisor, void the cash out with a reason; void it again.
6. *Close the day* with counted 295 and the note "5 short"; print the closing sheet.
7. Void the cash in posted before the close.
8. *Export Excel*; print the *Daily cash sheet*; choose a past day.
9. Arabic check.
Expected: (1) the page opens on today, own drawer. (2) the balance reads 100, then 300, then 250; the figures (receipts by method, cash in, cash out, closing) match. (3) BIL-062; the dialog does not send an empty reason (BIL-060 on the server). (4) the scope is not offered without the right; with it the other cashier's lines show. (5) the line reads voided and the balance returns to 300; the second void is refused with BIL-063. (6) a day-close line resets the balance to 295, the sheet shows opening, cash in, cash out, expected and counted cash, difference; the next window starts at that close. (7) refused with BIL-061 (closed period). (8) the workbook has a Ledger and a Summary sheet; the A4 sheet prints, in Arabic as well; a past day offers no entry buttons and shows the read-only note. (9) right-to-left, Arabic labels and counted forms.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.4 Reception clerk (UAT-REG)

Sign in as `uat.reception`; branch MAIN.

#### UAT-REG-01 — Sign in, branch, dashboard · critical
LIS-CR-007: after opening `<base>/lab/demo-lab`, a strip under the card shows the address with *Copy address* (toast "Copied"; the pasted text has no query string) and *Hide* (back after a reload). On `/login`, typing `demo-lab` shows the full address under Continue.
Refs: CP-03, CP-04, CP-05 · ACC S-D1 · ORG AS-S1.
1. Open `<base>/lab/demo-lab` (the laboratory name and logo show on the card); sign in with a wrong password; then with `<pw>`.
1a. Open `<base>/lab/uatlabb` (another laboratory) and sign in as `uat.reception` with `<pw>`; open `<base>/admin/login` and do the same; open `<base>/lab/no-such-lab`; open `<base>/` signed out.
2. Precondition variant: a user assigned MAIN and NORTH (use `uat.cashier` after UAT-CSH-08) sees the branch selection page; `Continue` with MAIN.
3. Dashboard (`/account/dashboard`): key figures of the day, 7-day chart, delayed tests, quick actions.
4. Click `Price query`, `New registration`, `Search registrations`, `Worklist`, `Payments`, `Results by accession` in turn.
5. Leave the browser idle past the session timeout (default parameter) and act.
6. Arabic check.
Expected: (1a) both refused with `AUTH-002` exactly like a wrong password (the user is not a member of UAT Lab B, not platform staff); the unknown address shows "This laboratory address was not found" with a link to *Find your laboratory*, no form; the bare address shows *Find your laboratory* (one box for the laboratory address, *Continue* opens `<base>/lab/<address>`). (5) the idle sign-out returns to `<base>/lab/demo-lab` with the inactivity notice. (1) `AUTH-002` "The username or password is incorrect."; after five failures the account is locked for 30 minutes and the next attempt — even with the right password — answers `AUTH-003` "The account is locked. Try again later." (an administrator unlocks it on Users › Security, UAT-ADM-04; the `RATE-429` limit is 20 attempts per 10 minutes and is independent); the correct password opens the portal. (2) only assigned branches listed; the session shows the branch in the top bar. (3) the KPIs count the session branch and day (registrations, collected today, pending results, unverified backlog, delayed tests; samples tiles are Release 2 and absent). (4) each quick action opens its page. (5) the session is renewed silently (refresh cookie) while the refresh session lives; after the sign-out the page returns to sign-in.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-02 — Password recovery with OTP · standard · *gateway*
Refs: CP-03 · FND §5 · `AUTH-013`.
1. Sign-in page › `Forgot your password?` › username `uat.reception` › `Send code`.
2. Enter a wrong 6-digit code.
3. Enter the received code; set a new password (rule box: length, not the username, match). `Set new password`.
4. Sign in with the new password.
5. Arabic check.
Expected: (1) "If an account matches, a 6-digit code was sent …" (the same message for an unknown username). (2) `AUTH-013` "The code is invalid or expired."; after 5 wrong codes the challenge is closed. (3) "Your password was changed." (4) works; the old password is refused.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-03 — Walk-in registration with full cash payment · critical
LIS-CR-008 (C-02): once services are entered the sample list shows one row per tube with a "Collected outside" switch and an optional time; switch the serum tube to outside with a time one hour ago and save → the serum label prints EXT, the blood label does not; with auto-collect off the serum sample is Collected at the typed time and the blood sample is Ordered; "All outside" / "None outside" switch every tube; a time in the future or 31 days back is refused with REG-041; the edit page shows the list read-only with "Outside" and the time; the CP-10 samples dialog, the accession page and the Lab Tracker ("ext") show it.
LIS-CR-007: the mobile is typed after the `+20` dial code (preselected on the laboratory's country); `0100 123 4567` reads `1001234567` after leaving the field and is still stored as `01001234567`. Paste `+966 50 123 4567`: the dial code switches to SA and the number is stored as `+966501234567`.
Refs: CP-06, CP-10 · WF-02 · BR-01, BR-02, BR-06, BR-26, BR-27 · REG S-V1-ok · BIL S-T1-ok · PAT S-T1-ok.
Reception › Registration (`/registration/new`).
1. Type the patient name "Mona Hassan": no similar file. `New patient`: title Mrs, gender (filled Female by the title), date of birth 1990-05-01, mobile `0100 123 4567`, nationality, governorate. 
2. Visit: payer Cash / Default (default), referring doctor DR01, flags none.
3. Service entry: type `K` Enter, `GLU` Enter, `HGB` Enter. 
4. Preparation "Fasting hours?" → `12 h`.
5. Payment card: method Cash, amount (defaults to the amount due 360). 
6. Ctrl+S.
7. In the success dialog open the labels and the job order, then `Open the visit`.
8. Reception › Search registrations: find the accession.
9. Arabic check.
Expected: (1) `Create and use` creates the file at once with number **PAT-2026-nnnnn** (before the visit is saved), mobile stored as 01001234567. (3) three lines with patient share 120 / 90 / 150, payer share 0, expected result dates; summary amount due **360**. (5) "Captured now and recorded by Billing right after the visit is saved." (6) "Registration saved": accession **ACC-2026-nnnnn**, "360 received — receipt RCT-2026-nnnnn"; the sample labels window lists **two tubes** — one Serum tube (CHEM: K + GLU, BR-02) and one EDTA tube (HEMA: HGB) — each with Code 128 barcode, unit code, accession, patient, services; the job order shows the visit header, balance due 0, the two tubes and their services; the receipt prints. (7) the edit page `Visit ACC-…` opens with the three lines Ordered, samples Received (auto-receive). (8) the row shows the services, patient share 360, remaining `Settled`, status Open.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-04 — Similar patient warning and existing file · critical
Refs: CP-06, CP-11 · WF-02 steps 1–2 · BR-01 · PAT S-SIM · tenant default `similarNameWarning`.
1. Registration › type "mona hassan" with mobile 01001234567.
2. Pick the existing file; add NA; pay cash. Save.
3. Type "محمد أحمد" for a new file with mobile 01112223333; save a visit with K. Then start another registration and type "محمد احمد" (no hamza).
4. Arabic check.
Expected: (1) the similar-files panel lists Mona's file (number, mobile) marked "Same name and mobile" with `Use this file`; "Compare the name and the mobile before creating a new file." (2) the visit is created on the **same** patient number; Patients › the file shows two visits. (3) the Arabic file is found despite the spelling difference (normalised match).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-05 — Duplicate and gender-restricted services · critical
Refs: CP-06 · WF-02 step 4 · REG S-V1-dup, S-V1-gender, S-V1-unpriced.
1. New registration for a **male** patient ("Ahmed Saad", Mr). Services: `K` Enter, then `K` Enter again.
2. Add `HCG` (female-only service from UAT-ADM-08).
3. Change the payer to AXA / Gold and keep K.
4. Arabic check.
Expected: (1) refused by the page: "Already on this visit: K." (`REG-100` on a forced request). (2) the HCG line reads "HCG: The service is restricted to the other gender." (`REG-015` from the dry run) and the save is blocked ("Some services were refused; remove them or change the contract."). (3) the K line reads "K: The service is not priced under this contract." (`REG-014`) as soon as the contract changes and the save is blocked (no contract pricing in Release 1); switching back to Cash saves.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-06 — Reserved service and release · standard
Refs: CP-06 · WF-02 step 5 · BR-05 · REG S-V1-reserved, S-V3-release.
1. New registration (Ahmed Saad): K and GLU; set GLU to **Reserved** (precaution unmet). Pay the full amount due. Save; open the labels.
2. Open the visit (`/registration/visits/:id`): release GLU. `Save changes`.
3. Release K (an Ordered line).
4. Arabic check.
Expected: (1) GLU shows status Reserved, is **billed** (amount due 210) and has **no sample**: the labels window lists one Serum tube with K only. (2) "GLU was released: the tube is issued and the results are expected."; the line is Ordered and joins the existing Serum tube (same group + sample type + condition, BR-02): no new tube. (3) refused `REG-105` "Only a reserved service can be released."
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-07 — Discount with reason and cap · critical
Refs: CP-06 · WF-02 step 5 · REG S-V1-discount · special right discount cap 10 %.
1. New registration (Mona Hassan): K + GLU (210). Discount: Percent 10, reason empty. Save.
2. Reason "Staff and relatives", Percent 10. Observe the summary; pay the amount due. Save.
3. New registration: Percent 30 with the same reason. Save.
4. As `lab.admin` (cap 100 %): the same 30 %.
5. Arabic check.
Expected: (1) refused by the page — "Choose the discount reason." (`REG-017` on a forced request). (2) discount 21, amount due **189**, "Your limit is 10 % of the patient share."; the payment defaults to 189; the receipt shows the discount. (3) refused `REG-060` "The discount exceeds your limit." (page: "The discount exceeds your limit of 10 %."). (4) saved (discount reduces the patient share only, Registration Q4).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-08 — Partial payment and payment validations on the page · critical
Refs: CP-06 · WF-02 step 6 · BR-26 · REG Q19 · BIL S-T1-ref, S-T1-over.
1. New registration (Ahmed Saad): HGB (150). Payment: Cash, amount 100. Save.
2. New registration: GLU (90). Payment: Card, amount 90, reference empty. Save.
3. Same with amount 95.
4. Reference `SLIP-2`, amount 90. Save.
5. Arabic check.
Expected: (1) saved; "100 received — receipt …"; Search registrations shows remaining **50**, balance Partial (used by UAT-CSH-04). (2) refused by the page: "Enter the transaction reference." (3) refused by the page: "The amount paid cannot exceed the amount due." (4) saved with the card receipt.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-09 — Registration saved without a payment · critical
Refs: CP-06 · CP-23 · LIS-CR-004 (the payment panel needs no treasury).
1. New registration (Mona Hassan): K + GLU. In the Payment card type amount **0**. Save.
2. Arabic check.
Expected: (1) the card shows no treasury, no device and no "request access" link; the visit is saved (ACC number, labels, job order), no receipt; Search registrations shows remaining 210.00, status Open (used by UAT-CSH-03).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-10 — Search registrations and row actions · critical
LIS-CR-014 (T2, T4): "⋯" at the end of the header row hides Payer and Remaining; the choice survives a sign-out and a sign-in on another browser; *Reset columns* brings them back; Escape returns the focus to "⋯", a click outside closes it. Tick two rows › *Print*: A4 landscape with the laboratory name, the list title, the active filters, the date, "2 rows", the visible columns only and "Page 1 / 1"; the browser's Save as PDF. *Clear selection* › *Excel*: one sheet named after the list, bold header, the date as a date, Patient share and Remaining as numbers, every row of the page. Arabic: document and sheet right to left. A crafted `PUT …/preferences/lists/visits` with `["<b>"]` answers 422 TEN-015.
Refs: CP-10 · WF-02 · documents (labels, job order, receipt, report).
Reception › Search registrations (`/registration/visits`).
1. Search by accession; by patient number; by name; by mobile; date range today; status filters.
2. The row shows its actions on one line: *Details*, *Edit*, then the *Payments* and *Documents* icons and a *More* menu (Enter opens it, Esc closes it). Row › More › `Samples` (needs `samples.view`): the dialog lists the tubes with unit, type, status **Received**, collected and received times, sample number; `Label` prints one, `Print all labels`.
3. Row › `Documents` (printer icon): sample labels, print / open the job order, receipts (Payments page), results report.
4. Row › `Payments` (coins icon); row › More › `Results`; row › `Edit`.
5. `Export CSV`.
6. Arabic check.
Expected: (1) each filter returns the right visits; the row shows services (reserved and cancelled styled differently), patient share, remaining balance. (2) tubes as registered; labels reprint. (3) the job order reprints; the report link opens Results by accession (prints only once verified, BR-09). (4) Payments opens with the accession filled; Results opens the visit; Edit opens the visit page. (5) a CSV (UTF-8 with BOM, Arabic readable in Excel) with the listed visits.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-11 — Cancel services with a reason · critical
Refs: CP-10, CP-26 · WF-09 step 5 · BR-05, BR-15 · REG S-V4-cancel, S-V4-verified, S-V4-right.
Visits: (a) UAT-REG-03 (K, GLU, HGB paid 360; after UAT-VER-01 K and GLU are verified, HGB is not); (b) UAT-CSH-03's visit (paid 210, no results yet).
1. Visit (b) › More › `Cancel services`: tick K, reason empty. Confirm.
2. Tick K, reason "Patient declined". Confirm.
3. Visit (a) › `Cancel services`: tick K (verified).
4. Visit (a): tick HGB with reason "Ordered by mistake". Confirm.
5. As `uat.tech` (no `registration.cancel`), open Search registrations.
6. Arabic check.
Expected: (1) the `Cancel the selected services` button stays disabled until a reason is chosen (`REG-021` on a forced request). (2) K is **Cancelled** (kept on the row as a struck-through chip; reason, user and time on the visit), patient share 90, paid 210 → remaining **−120** on the row and **Refund due** on the Payments page; the tube of K is cancelled; the result rows of K are Cancelled on Results by accession. (3) refused `REG-106` "The service has verified results and cannot be cancelled." (4) HGB cancelled; balance −150 Refund due (used by UAT-CSH-07 — adjust the expected refundable to the visit used). (5) the action is absent (a role without `registration.view` cannot even list the visits); a forced request answers `AUTHZ-001`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-12 — Price query and continue to registration · standard
Refs: CP-12 · BR-07 · PRC S-Q-cash.
1. Reception › Price query: Cash default, gender Female, services K, GLU, HCG. `Print quote`.
2. `Continue to registration`.
3. Arabic check.
Expected: (1) list price / patient share / payer share, preparation and expected result per line; the quote prints with the disclaimer; nothing is saved (no accession). (2) the registration page opens with the three services pre-filled; the patient must still be chosen.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-13 — Preparation answers on the job order · standard
Refs: CP-06, CP-46 · REG Q6.
1. New registration with GLU and CREA: answer "Fasting hours?" = `8 h`. Save. Open the job order.
2. New registration with K only.
3. Arabic check.
Expected: (1) the question is shown once (linked to both services); the answer prints on the job order. (2) no preparation question (FAST is linked to GLU and CREA only).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-14 — Frequency check · standard
Refs: CP-06 · BR-12 · REG S-V1-freq-alert, S-V1-freq-prevent · CREA configured in UAT-ADM-08 (30 days, Alert).
1. Register CREA for Mona Hassan (pay). Save. Register CREA again for her the same day.
2. Confirm the repeat order in the dialog.
3. As `lab.admin` set CREA's frequency action to **Prevent**; as the clerk register CREA a third time.
4. Arabic check.
Expected: (1) warning "The test was ordered recently for this patient. Confirm the repeat order." (`REG-102`); without confirmation nothing is saved. (2) saved. (3) refused `REG-101` even when confirmed.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-15 — Patients: file, history, documents, merge · standard
Refs: CP-11 · BR-01, BR-15 · PAT S-T1-age, S-T1-noage, S-T2-ok, S-T3-ok, S-T3-gender, S-T4-ok, S-T4-type.
Reception › Patients (`/patients`).
1. `New patient`: "Sara Ali", Female, **no** date of birth, age 34 y 2 m. Save. Then another with neither DOB nor age.
2. Open Mona Hassan: tab `Visits` (history with status), `Edit` the address. Save.
3. Tab `Documents` › `Attach a document`: a 20 KB PDF with a note; then an `.exe`; then a 12 MB file.
4. `New patient` "Mona Hasan" (duplicate, Female). Open the original file › `Merge a duplicate`: find "Mona Hasan", reason "duplicate file", `Merge`. Then try merging a Male file into Mona's without the confirmation box.
5. Registration › search "Mona Hasan" and register on the merged file.
6. Arabic check.
Expected: (1) saved with "Estimated from the age; the age is what is saved."; the second is refused `PAT-007`. (2) the number never changes; the visits list the accessions. (3) PDF attached, viewable; `.exe` refused `PAT-042`; 12 MB refused `PAT-041`. (4) the duplicate becomes **Merged** with the banner "This file was merged into PAT-… by …"; the original keeps its number and lists "Files merged into this one"; the gender mismatch is refused `PAT-100` until confirmed. (5) refused `REG-002` "The patient file is merged into another file; register on the surviving file."
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-REG-16 — Lab Tracker: follow the day's visits · critical
LIS-CR-014 (T1, T3, T5, T2, T4): each row carries Details, Edit, the Payments and Documents icons and a More menu (Patient, Samples, Results, Cancel services) by permission; open any of them, then use the "Back to Lab Tracker" chip in the top bar: the tracker returns with the same period, view, stage, attention, search and page (the filter is in the address bar). *Visits | Services*: one row per registered service with its result and sample status, next step and time left; the strip counts stay the day's. "⋯" hides Time left (Search registrations keeps its own choice); select rows › *Print* / *Excel*.
Refs: CP-69 · WF-02, WF-06 · LIS-CR-006 (stages, next step, time left; delivered = printed; ready-but-unpaid is a flag).
Preconditions: the visits of UAT-REG-03 (paid, results verified in UAT-VER), UAT-REG-09 (saved unpaid), UAT-SMP-02 (a rejected
sample) and UAT-RES-02 (a panic value) exist today. Sign in as `lab.admin` (every right), then as `uat.reception`.
1. Reception › Lab Tracker (`/tracker`). Read the pipeline strip (Registered, Awaiting sample, In the lab, Awaiting verification,
   Ready, Delivered) and the *Needs attention* chips (Delayed, Panic, Rejected, Ready but unpaid).
2. Click *Awaiting verification* on the strip, then click it again. Click the *Panic* chip.
3. Table view: find the UAT-REG-09 visit; click its *Next step*. Come back; sort by *Time left* and by *Registration*.
4. Switch to *Board*; find the same visits in their lanes; click *+ n more* on a lane.
5. Scan or type the accession of UAT-REG-03 in the search box and press Enter.
6. Leave the page open for a minute while another profile records a payment on the UAT-REG-09 visit (UAT-CSH-03).
7. Desk views: *Reception*, *Verification*, *Front desk*; *Bench*.
8. Period: pick *Yesterday*, then *Last 7 days*, then *From / to…* with yesterday 08:00 → today 12:00; back to *Today*.
9. Sign in as `uat.reception` (no `results.view`, no `billing.view`) and open the page.
10. Arabic check.
Expected: (1) one row per visit of the day on the session branch, each with Registration (time, payer), Samples, Results (with the
Panic tag on the UAT-RES-02 visit), Payment (Paid / Partial / Unpaid / Refund due with the remaining amount) and *Next step*; the strip
counts add up to the day; the UAT-REG-03 visit is Ready or Delivered, the UAT-REG-09 visit shows *Take payment*; the UAT-SMP-02
visit is in *Awaiting sample* with *Re-collect*. (2) the table narrows to that stage and widens again; the chip narrows to the panic
visit. (3) *Take payment* opens Payments with the visit loaded; the sort changes the order and the header shows the direction. (4) five
lanes with cards (accession, time-left pill, patient, detail, payment pill, flag pill, four-segment progress bar); *+ n more* returns to
the table filtered by that stage. (5) exactly that visit is listed. (6) within 30 s the row's Payment cell reads Paid and "Live ·
updated n s ago" restarts, without a page reload. (7) each view keeps only its visits; *Bench* opens the Worklist. (8) the list and the strip counts follow the period (yesterday's
visits, then both days, then only the visits registered inside the from / to minutes); *Today* restores the day. (9) the Results and
Payment cells read "—" with "Not in your rights"; the stage and next step still show. (10) right-to-left, Arabic labels and counted
forms (e.g. "زيارتان").
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.5 Phlebotomist / technician — sample side (UAT-SMP)

Release 1 has no sample scanning: tubes are generated on save (BR-02) and marked Received at once (BR-03 emulated).
These scripts prove the tube and label behaviour the laboratory relies on until the Samples module (Release 2).

#### UAT-SMP-01 — Tubes, labels and the Received gate · critical
Refs: CP-06, CP-10 (Samples dialog), CP-18 · BR-02, BR-03 · SRS "Sample (tube)" status model · OQ-04.
As `uat.reception`:
1. New registration: K, NA, GLU, HGB, BLDGRP for a new patient; pay. Save; open the labels.
2. Search registrations › row › `Samples`.
3. As `uat.tech`: Laboratory › Worklist for today, column Sample status.
4. Arabic check (labels print in the configured language; check the dialog).
Expected: (1) **two labels**: Serum (K, NA, GLU) and EDTA (HGB, BLDGRP) — group + sample type + condition decide the tube; each label carries the unit code, date/time, sample type, accession, patient, sample number barcode (`SMP-…`), services. (2) two tubes, status **Received**, status date = registration time, sample numbers as on the labels. (3) every row of the visit shows sample status Received; results can be entered without a scan.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SMP-02 — Job order content · standard
Refs: CP-06 · document "Job order".
1. From the success dialog or Search registrations › `Documents`, print the job order of UAT-SMP-01's visit.
2. Register a visit with the flag `Sample collected outside the laboratory`; print its job order.
3. Arabic check.
Expected: (1) visit header (accession, patient, age/sex, doctor, payer), balance due, one block per service with unit, expected result date, sample number and barcode, collection boxes "Drawn at / Drawn by", "Visit no. n of this patient". (2) the note "Sample collected outside the laboratory" prints.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SMP-03 — Cancelled tube · standard
Refs: CP-10 · BR-05, BR-15 · REG S-V4-cancel.
1. On UAT-SMP-01's visit cancel HGB and BLDGRP (reason). Open `Samples`.
2. Cancel only K. Open `Samples`.
3. Arabic check.
Expected: (1) the EDTA tube is **cancelled** (no live service left); its label cannot be printed. (2) the Serum tube stays Received with NA and GLU; K is listed as cancelled on the tube.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.6 Laboratory technician — result entry (UAT-RES)

Sign in as `uat.tech`; branch MAIN.

#### UAT-RES-01 — Worklist · critical
Refs: CP-18 · WF-06 step 1 · BR-13 · RES R-BR13 · document "Worklist".
Laboratory › Worklist (`/results/worklist`).
1. Collection window today; clinical group CHEM; status boxes Ordered.
2. Filter test K; clear; filter by payer Cash; `Delayed only`; `This branch only`.
3. Counters Requests / Tests / Samples / Delayed.
4. `Print worklist`; `Export CSV`.
5. Click `Results` on a row.
6. Arabic check.
Expected: (1) one row per ordered test (no status box ticked = every status; tick `Ordered` to narrow): patient no., accession, sample, patient, age/sex, test, test status, sample status Received, collected, expected result (collection + duration on test days), delay, consumed %; rows coloured by the share of turnaround consumed (legend: ≥ warn %, ≥ late %), verified/printed rows uncoloured. (2) filters narrow the list. (3) the counters match the list. (4) the bench list prints with the count; the CSV downloads. (5) Results by accession opens on that visit.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-02 — Enter numeric results, flags, panic · critical
Refs: CP-19 · WF-06 steps 2–3 · BR-03, BR-08 · RES R-T2-ok, R-T2-gate, R-T2-reportable, R-T3-state.
Laboratory › Results by accession (`/results/by-accession`), visit of UAT-REG-03 (K, GLU, HGB).
1. Query by accession (F8). 
2. Tab CHEM: K = `6.84`, GLU = `95`. Ctrl+S.
3. Tab HEMA: HGB = `6.5`. Save.
4. On a visit of CREA (UAT-REG-14): CREA = `0.04` with a reportable low of 0.1 configured by the administrator (set it on CREA first). Save.
5. Enter `abc` in NA of another visit. Save.
6. Arabic check.
Expected: (1) the visits list collapses (`Hide visits`), the query fields stay; the case header shows patient data; **one tab per report unit** (CHEM with its Serum sample id, HEMA with its EDTA sample id) with the unit status. (2) K stored **6.8** (1 decimal), flag **HH / PANIC** (above 6.5), pink/red row; GLU normal (70–110); both **Complete**; "2 results saved."; the result log of K shows a Value row ("— → 6.8", Manual) with user and time. (3) HGB 6.5 flagged **LL / PANIC** (< 7.0), Complete — use a visit whose HEMA line is live (REG-11 cancels the HGB of UAT-REG-03's visit; UAT-SMP-01's visit works). (4) the result log and the report read "< 0.10" (the test's decimals); the editable cell keeps the typed 0.04. (5) refused `RES-003` "The value must be a number." on the row ("1 results were refused; see the codes on the rows.").
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-03 — List, formula and reference-range resolution · standard
LIS-CR-007: before typing a value the range cell already shows the patient's range greyed with a tooltip; after Save it shows in normal text with the flag.
Refs: CP-19, CP-41 · BR-08, BR-19 · RES R-T2-list, R-T2-formula · CAT S-BR08.
1. Visit of UAT-SMP-01: tab HEMA: BLDGRP — pick `AB`; then type `X`. Save.
2. Visit of UAT-REG-13 (GLU + CREA for Mona, female 36 y): CREA = `1.05`. Save. Look at the range and flag. Then type a value into EGFR (profile RENAL in another visit).
3. Register (as the clerk) CREA for a male 40 y and a child 10 y; enter 1.05 in each.
4. Arabic check.
Expected: (1) `AB` saved, flag Normal/Abnormal per the list value; `X` refused `RES-006` "The value is not in the test's list."; the placeholder `—` is not accepted as a value (`RES-005`). (2) range **0.6–1.1** (female adult band), flag Normal; EGFR cannot be typed (`RES-004`) and is computed from CREA on save. (3) male: range 0.7–1.3 Normal; child: 0.3–0.7 flagged **H** (BR-08: band by sex and age).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-04 — Comment, note and review · critical
Refs: CP-19, CP-47 · WF-06 steps 4–5 · RES R-T3-ok, R-T3-state, T10, T11.
Visit of UAT-REG-03.
1. On K: `Report comment (prints)` › in the dialog's Comment library panel search `haem` and click **HAEM** (its text is appended to the comment). Save.
2. On K: `Internal note (never prints)` "recollect next time". Save.
3. `Review` the CHEM unit.
4. On a visit with no value entered: `Review`.
5. `Verify`.
6. Arabic check.
Expected: (1) "The report comment was saved."; the comment appears under the test in the preview (UAT-VER-06). (2) "The internal note was saved."; absent from the preview. (3) K and GLU **Reviewed** ("2 results reviewed."), reviewer logged in the status log. (4) "No complete result to review." (`RES-012`). (5) the technician has no verify right: the `Verify` button is absent (`RES-052` "You have no right to verify results of this clinical group in this branch." on a forced request).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-05 — Verified value is frozen · standard
Refs: CP-19 · BR-09 · RES INV-RES-009 (RES-011), T10 (RES-024).
After UAT-VER-01 verified K and GLU:
1. Change K to 5.0. Save.
2. Change K's report comment. Save.
3. Arabic check.
Expected: (1) refused `RES-011` "The result is verified or printed: use Amend to change it." (2) refused `RES-024` "The comment of a verified result is frozen: undo or amend first."
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-06 — Cancelled line · standard
Refs: CP-19 · RES R-T9 (RES-026).
After UAT-REG-11 cancelled HGB on visit (a):
1. Open the visit: tab HEMA.
2. Enter a value for HGB. Save.
3. Arabic check.
Expected: (1) HGB reads **Cancelled** with the cancellation reason in the log. (2) refused `RES-026` "The result was cancelled with its visit line."
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-07 — Query filters · standard
Refs: CP-19 (query panel) · RES R-BR10.
1. `New query`: patient name `Mona%`; test status Not verified; `not printed`; `delta check`.
2. Query by patient number.
3. Query a non-existent accession.
4. Arabic check.
Expected: (1) the list shows only visits matching all filters; "Not verified" = Ordered + Complete + Reviewed (BR-10). (2) the patient's visits. (3) "No visit matches the query." (`RES-408`).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-RES-08 — Result rights per clinical group · critical
Refs: CP-19, CP-37 · RES R-AUTHZ (RES-050, RES-054), ORG AS-R3.
Sign in as `uat.tech.chem` (CHEM only).
1. Open the visit of UAT-SMP-01 (CHEM + HEMA).
2. Enter a value on the CHEM tab. Save.
3. Worklist with clinical group HEMA.
4. Arabic check.
Expected: (1) only the CHEM tab; the banner "1 report units are outside your result rights". (2) saved. (3) no HEMA rows are returned (groups without the see right are not listed); a forced HEMA entry answers `RES-050`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.7 Unit head / pathologist (UAT-VER)

Sign in as `uat.head`; branch MAIN.

#### UAT-VER-01 — Verify (electronic signature) · critical
Refs: CP-19 · WF-06 step 5 · BR-09 · RES R-T4-ok · approval gate §10 of LIS-INT-RES-001.
Visit of UAT-REG-03 after UAT-RES-04 (K, GLU Reviewed).
1. Tab CHEM › `Verify`.
2. Open the status log of K.
3. Arabic check.
Expected: (1) "2 results verified (electronic signature)."; K and GLU **Verified** (row action `Amend` appears); the unit status Verified; the verifier's name and time are in the status log (`History and previous results`); the worklist row loses its colour. (2) the log lists Ordered → Complete (technician) → Reviewed → Verified (unit head, time). The sign-off is the electronic signature printed on the report.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-VER-02 — Missing values: verify, preview and print refused · critical
Refs: CP-19 · WF-06 step 6 · BR-09 · RES R-T4-novalue, R-PREVIEW, R-T8-unverified.
Visit of UAT-SMP-01 after UAT-RES-08 entered **one** CHEM value (K) and left NA and GLU empty.
1. Tab CHEM › `Verify` with K Complete, NA and GLU Ordered.
2. `Preview`.
3. `Print report`.
4. Enter NA and GLU, Save, `Verify`, then `Preview` and `Print report`.
5. Arabic check.
Expected: (1) only results **with a value** are signed (K Verified); the unit stays below Verified (lowest status, BR-04) with the badge "values missing"; forcing a verify on NA answers `RES-014` "A result without a value cannot be verified." (2) refused `RES-022` "A value is missing: the report cannot be previewed without the 'preview without verify' right." (3) refused `RES-014` "A result without a value cannot be verified or printed." — the results are checked in grid order and each one's value before its state, so with K verified the empty NA decides; a unit whose first unverified result holds a value is refused `RES-021` "Only verified results print (BR-09)." instead. (4) the unit is Verified; the preview shows the report; print marks it **Printed** (status Printed and "Printed (copy 1)" in the status log after the visit is queried again).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-VER-03 — Delta check acknowledgement · critical
Refs: CP-19 · BR-11 · RES R-T2-delta, T7 · CREA delta 20 % (UAT-ADM-08).
1. Mona's first CREA visit (UAT-REG-14 step 1): enter CREA = `1.00`; `Verify`.
2. Her second CREA visit: enter `1.30`. Save. Look at the Δ column and the tooltip.
3. `Verify`.
4. In the delta dialog read the message and click `Acknowledge and verify`.
5. Arabic check.
Expected: (1) Verified. (2) the row carries the **delta** badge ("Δ 30%", `Acknowledge`); its tooltip reads "Previous 1, change 30%". (3) the "Delta check" dialog opens: "These results differ from the patient's previous verified result beyond the test's delta limit (BR-11)…"; a forced verify without acknowledgement answers `RES-015`. (4) the log records "Delta acknowledged" then Verified; the value is unchanged (BR-11: warns, never blocks the value).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-VER-04 — Undo verification and review · standard
Refs: CP-19 · BR-15 · RES R-T5-ok, R-T5-reason, R-T5-printed.
1. On a verified, unprinted unit (UAT-VER-03's second visit): `Undo`, reason empty, confirm.
2. `Undo` with reason "wrong sample".
3. On the printed unit of UAT-VER-02: `Undo`.
4. Arabic check.
Expected: (1) refused — the dialog requires the reason (`RES-019`). (2) the result returns to **Reviewed** (it was reviewed) or Complete; "1 results undone."; the removed signature stays in the result log with the reason. (3) "Nothing to undo … (printed results are amended)" / `RES-018`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-VER-05 — Amend a verified result · critical
Refs: CP-19 · BR-09, BR-15 · RES R-T6-ok, R-T6-right (RES-055).
1. On the printed unit of UAT-VER-02: on K choose Amend (row action), new value 4.9, reason "transcription error". Confirm.
2. `Verify` again, `Print report`.
3. Sign in as `uat.tech` (no "modify verified result" right) and try Amend on a verified result.
4. Arabic check.
Expected: (1) "The result was amended and returned to Complete; it needs a new verification."; the log keeps the previous value 6.8, the previous signature and the reason. (2) Verified again; the report prints with the amendment count and "Printed (copy 2)" in the status log. (3) the `Amend` action is absent for the technician (`RES-055` "Amending a verified result needs the special right 'modify verified result'." on a forced request).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-VER-06 — Report preview and print · critical
LIS-CR-010 (C-14): Setup › Parameters › Printing › *Report design* — preview a verified unit in Classic, Modern, Compact, Letterhead (45 mm clear top band) and Cumulative (last three verified values of earlier visits of the patient, newest first); a contract tier design (CP-34) wins over the template's, which wins over the laboratory's; Arabic RTL in every design.
Refs: CP-19 · WF-06 step 6 · BR-09, BR-20 · document "Laboratory report" · RES R-T8-ok.
Visit of UAT-REG-03 (CHEM verified in UAT-VER-01; HEMA cancelled in UAT-REG-11 or verified).
1. `Preview` the CHEM unit.
2. `Print report`.
3. Reception (`uat.reception`): Search registrations › `Documents` › results report.
4. Arabic check (the report language follows the tenant parameter `resultLanguage` default; note what prints).
Expected: (1) the report: template header, patient and visit header, per report unit the tests with value, unit, reference range, flag (K 6.8 **HH**), the report comment under K, no internal note, the verifier's name and time as signature, QR code; hidden helper tests (none in this data) never print. (2) "The report of ACC-… was marked printed and sent to the printer."; status Printed and "Printed (copy 1)" in the status log. (3) `Results report` opens Results by accession on the visit; the clerk can `Preview` the verified report; printing needs `results.export` and the print result right, which the Reception role of §4 does not hold (open point 9) — a unit with missing values is refused at preview (`RES-022`).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-VER-07 — Panic value notification · standard · *gateway*
Refs: CP-19 · FND §5 (template `panic-value`) · RES R-T2-ok (ResultPanicDetected).
1. After UAT-RES-02 (K 6.8 panic): check the notification channel configured for the laboratory (SMS/e-mail of the test lane) and the recent-activity panel of the dashboard.
2. Arabic check.
Expected: a panic-value message with patient, accession, test and value is sent once per panic result (template SA-20 `panic-value`); when no gateway is configured record "blocked — gateway".
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.8 Operations Admin — Super Admin portal (UAT-OPS)

Sign in at the Super Admin address `<base>/admin/login` as `uat.ops` (created in UAT-SUP-02); a laboratory user there is refused with `AUTH-002`.

#### UAT-OPS-01 — Sign in, home, platform dashboard · critical
LIS-CR-007: the dashboard tile "Silent for 3+ days" (red when above 0, trial count in the subtitle) opens Clients with *Activity = Silent 3+ days*: client, address, status, administrator with a `tel:` link, last sign-in, last registration, days silent; no inactive client; the window 7 / 14 / 30 changes the list and the address (`?silent=7`); *Export CSV* downloads `silent-clients-…csv`; *Activity = Any* restores the register; `GET …/clients/silent?days=0` → 400; without `platform.view` → 403 and no select.
Refs: SA-01, SA-02, SA-03 · WF-12.
1. Sign in with a wrong password, then `<pw>`.
2. Home (`/admin`): brand and welcome.
3. Dashboard (`/admin/dashboard`): change the period; `Refresh`.
4. Arabic check.
Expected: (1) `AUTH-002` "The username or password is incorrect.", then the Super Admin home (brand, welcome, pointer to the dashboard). (3) clients active/inactive, clients in trial, admin users, served patients, tests and registrations per period, wallets at or below zero, subscription vs manual, plan distribution, registrations by client, recent clients; the UAT laboratory's registrations appear (a laboratory seeded by DevTools, without a client record, shows as its tenant id).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-OPS-02 — Add a client manually · critical
LIS-CR-008 (C-04): the client drawer offers City / District / Area; changing the city resets district and area; the register and the public laboratory card print "Area, District, City, Governorate"; clearing the locality saves. LIS-CR-012: the toolbar *Subscription* filter (Expiring / In grace / Expired) lists the clients with their days left and CSV export. LIS-CR-013: "Hide announcements for this client".
Refs: SA-04 · WF-01 step 2, WF-12 step 1 · BR-25 · PLT S-P1-manual, CLI-100.
SA › Administration › Clients (`/admin/clients`).
1. `Add client`: name EN "UAT Lab B" / AR, country Egypt, governorate, address, laboratory mobile, licence, manager name and mobile, e-mail, language, plan, portal address `uatlabb`, administrator username `labb.admin`, password + confirm. `Create client`.
2. `Add client` with portal address `uatlabb` again.
3. Sign in to the client portal as `labb.admin` (laboratory UAT Lab B): Setup › Tests, Clinical groups, Reasons (as far as Release 1 exposes them), Finance › Payers.
4. Back in SA: the client row.
5. Arabic check.
Expected: (1) "The client was created. Portal <base>/lab/uatlabb, administrator labb.admin; the address was sent by SMS." — status **Active**, source **Manual**, wallet 0, one branch, one user. (2) refused `CLI-100` "This portal address is already taken." (inline in the form). (3) the laboratory holds an **independent copy** of the global master data (clinical groups, sample types, units, tests, services, preparations, comments, reasons, microbiology — everything active in the platform tenant; on a database whose global catalogue is still empty the copy is empty and UAT-MDR-01 proves it), its own branch and the Laboratory Administrator role; no record of the UAT laboratory is visible (tenant isolation). (4) branches 1, users 1, created today.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-OPS-03 — Deactivate and activate a client · critical
Refs: SA-04 · WF-12 step 1 · PLT S-P3 (CLI-021, CLI-011, CLI-010, CLI-020).
1. Clients › UAT Lab B › `Deactivate client`, reason empty, confirm.
2. With reason "UAT". Confirm. In another profile sign in as `labb.admin`; a session of `labb.admin` already open acts.
3. `Deactivate client` again.
4. `Activate client`; sign in as `labb.admin`.
5. `Activate client` again.
6. Arabic check.
Expected: (1) refused by the page: "A reason is required." stays in the dialog, nothing is sent (`CLI-021` applies to a forced request). (2) "The client is inactive; its users are blocked.", status **Inactive**; sign-in refused `CLI-020` "This laboratory's subscription is inactive. Contact National Technology."; the open session is revoked. (3) the row of an inactive client offers `Activate client` only (`CLI-011` applies to a forced request). (4) "The client is active.", Active; sign-in works. (5) the row offers `Deactivate client` only (`CLI-010` applies to a forced request).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-OPS-04 — Reset the administrator password · standard
Refs: SA-04 · PLT Q3.
1. Clients › UAT Lab B › `Reset administrator password`: new password + confirm. Confirm.
2. Sign in as `labb.admin` with the old password; then with the new one.
3. Arabic check.
Expected: (1) "The administrator password of UAT Lab B was reset."; open sessions of the administrator end. (2) old `AUTH-002`; new one lands on the profile with must-change.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-OPS-05 — Subscriptions and wallets · critical
Refs: SA-05 · WF-12 step 1 · PLT S-W1, S-W3, S-W4.
SA › Administration › Subscriptions & wallets (`/admin/subscriptions`).
1. Plans: `Add plan` code `UATPLAN`, monthly fee, branches, users, SMS quota, analysers, currency. Save; deactivate and reactivate it.
2. Select client UAT Lab B › `Top up`: amount 500, reference empty.
3. Amount 500, reference `INV-1`. Confirm.
4. Amount 0.
5. Arabic check.
Expected: (1) plan listed with its client count (0); `Deactivate plan` → Inactive, `Activate plan` → Active. (2) refused by the page: Reference "This field is required.", nothing is sent (`WAL-002` applies to a forced request). (3) "The wallet was topped up."; balance 500; a transaction Top-up with balance after, reference and user; the dashboard's "wallets at or below zero" drops by one. (4) refused by the page: "The value must be at least 0.01." (`WAL-001` applies to a forced request).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-OPS-06 — Countries and governorates · standard
Refs: SA-10, SA-11 · PLT S-M3 (GEO-110).
1. Countries › `Add country`: "Testland" / AR, ISO `TL`, dial code `+999`, currency, language, time zone, mobile length 9. Save. Then dial code `999`.
2. Governorates › `Add governorate`: "Test City", country Testland, district. Save.
3. Countries › Testland › delete.
4. Arabic check.
Expected: (1) saved; the malformed dial code is refused ("'+' and the country code"). (2) saved. (3) refused `GEO-110` — a country with governorates cannot be deleted.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-OPS-07 — Self-service subscription wizard · critical · *gateway*
LIS-CR-014 (S1–S3): with a manager mobile that already belongs to an account, leaving the field marks it ("This mobile already belongs to an account…") and *Send verification code* does nothing; on step 3 a taken username is marked (SUB-013); *Back* on steps 2–3 returns to step 1 with every value kept — unchanged details *Continue* straight to step 3 without a new code, a changed detail sends a new one; from step 3 the rail's step 2 shows "Mobile verified" with Back / Continue; a mobile refused at step 3 (two-tab race) returns to step 1 with the field marked; a wrong code marks the code field (SUB-010); step 4 has no way back; Arabic check.
LIS-CR-008 (C-04): step 1 offers City / markaz, District and Area / village after the governorate (optional); the finished client shows the full address on SA-04.
LIS-CR-007: once Egypt is chosen the `+20` dial code appears beside both mobiles; typing `01012345678` (leading 0) is accepted; the 9-digit refusal is unchanged.
Refs: CP-01, CP-02 · WF-01 step 1 · BR-25 · PLT S-P1-sub, S-P1-mobile, S-S2-code.
Public site (`/welcome`), no sign-in.
1. `Subscribe now`. Step "Laboratory details": name EN/AR, country Egypt, governorate, address, mobile with 9 digits, licence, manager name and mobile, e-mail. Next.
2. Mobile with 10 digits. Next (code sent to the manager mobile).
3. Step "Verify code": a wrong code; then the received code.
4. Step "Create account": username `labc.admin`, password 6 characters; then a valid password. `Create account`.
5. Step "Sign in": follow the portal address received by SMS (`<base>/lab/<slug>`, the username prefilled) and sign in.
6. Arabic check (the public site in Arabic).
Expected: (1) refused `GEO-010` (mobile length by country). (2) OTP sent. (3) `SUB-010`; the fifth wrong code closes the challenge; the right code advances. (4) `CLI-040` "too short", then the client is created: status **Trial**, source **Subscription**, wallet, default branch, administrator, master-data copy; SA › Clients lists it; the dashboard's trial count rises. (5) the administrator reaches the client portal.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.9 Master Data Reviewer — Super Admin portal (UAT-MDR)

Sign in as `uat.mdr`.

#### UAT-MDR-01 — Global catalogue A and its copy to new clients · critical
Refs: SA-12 · WF-12 step 2 · BR-25 · CAT S-CLONE, Q16 · OQ-03.
SA › Master data › Tests / Services / Clinical groups / Sample types / Units / Service sequence (`/admin/catalogue/*`).
1. Tests › `Add test` `TSH` (CHEM, Numeric, SER, mIU/L, band 0.4–4.0 with an age band, e.g. 0–120 years: "Age to" is required). Services › `Add service` TSH (Test, member TSH). Service sequence: give it a sequence. When the platform catalogue is still empty, add the clinical group CHEM, the sample type SER and the unit mIU/L first (same editors).
2. As `uat.ops`: create client "UAT Lab C" (as UAT-OPS-02). Sign in as its administrator: Setup › Tests.
3. Sign in as `labb.admin` (UAT Lab B, created before step 1): Setup › Tests.
4. Arabic check.
Expected: (1) saved in the platform catalogue; the editors are the same as the client's (CP-39…CP-44). (2) UAT Lab C holds TSH. (3) UAT Lab B does **not** hold TSH: global changes reach only tenants created afterwards (Catalogue A intake Q16; the master-data update policy of SA-19 is an open question, OQ-03 — record the observed behaviour).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-MDR-02 — Global lists · standard
LIS-CR-008 (C-04): SA-11 › Locations drawer › *Import CSV* (header `city_en,city_ar,district_en,district_ar,area_en,area_ar`): the dry run shows created / existing / refused per level and writes nothing; a bad line (area without district) keeps *Import* disabled (GEO-025); fix it and import; import the same file again → nothing new. LIS-CR-010 (C-14): a Results report template takes a design and the preview offers every design with "Use this design"; a Receipt template with a design is refused (CAT-273).
Refs: SA-13 · CTB S-CTB-T1-ok, S-CTB-T1-set, S-CTB-T1-rule, S-CTB-T5, S-CTB-T1-code.
SA › Master data › Preparations, Comment library, Reasons, Diagnoses, Report templates, Test rules, Shortcut sets, Patient conditions, Additional services.
1. Reasons › `Add reason`: kind Cancellation, "Doctor's request" / AR. Save.
2. Diagnoses › `Add diagnosis`: ICD `E11`, "Type 2 diabetes", services GLU. Save.
3. Shortcut sets › `Add shortcut set`: code `LIPID`, no services. Save; then with services. Save; then another set with code `lipid`.
4. Test rules › `Add test rule`: trigger K, added K; `Validate`. Then trigger K + NA, added GLU. Save.
5. Patient conditions › add "Pregnant"; Additional services › add `HOME` "Home visit fee"; Report templates › add a results template with header text; Preparations › add; Comment library › add.
6. Arabic check.
Expected: (1–2) listed Active with both names. (3) empty set refused `CAT-230`; saved with services; `lipid` refused `CAT-300` (code unique per list). (4) validation reports `CAT-242` "A service cannot be both a trigger and an added service." without saving; the second rule saves. (5) each list accepts and shows the record; deactivation replaces deletion everywhere.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-MDR-03 — Global microbiology masters · standard
Refs: SA-14 · CTB A4 (CAT-260, CAT-261).
SA › Microbiology › Drug types, Commercial names, Antibiotics, Organisms, Sensitivity values.
1. Drug types › add "Cephalosporins". Commercial names › add "Ceporex" with that drug type. Antibiotics › add `CFX` Cefalexin, abbreviation, commercial name Ceporex. Organisms › add `SAUR` Staphylococcus aureus. Sensitivity values › add `S` Sensitive, `I`, `R`.
2. Commercial names › add one with no drug type (or a deactivated one).
3. Arabic check.
Expected: (1) five records Active; the antibiotic row shows its commercial name. (2) refused `CAT-260` "The drug type does not exist."
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

### 5.10 Super Admin — Super Admin portal (UAT-SUP)

Sign in as `platform.admin`.

#### UAT-SUP-01 — Platform roles · critical
Refs: SA-06 · ORG AS-R1, AS-R4, ORG-032.
SA › Administration › Roles (`/admin/organization/roles`).
1. `Add role` "Operations Admin" with the keys of section 4; "Master Data Reviewer" likewise.
2. `Add role` "Empty" with no permission; activate.
3. Open a user's privileges tab (after UAT-SUP-02) and try to add an override.
4. Arabic check.
Expected: (1) "Role created." twice; both roles listed Active with their permission count (the platform role editor has the General and Screen rights tabs only: no special or result rights). (2) refused by the page: "Grant at least one permission before creating the role." (`ORG-005` applies to a forced request). (3) the platform user editor has no Privileges tab: platform staff inherit their role only (`ORG-032` applies to a forced request).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-02 — Admin users · critical
Refs: SA-07 · ORG AS-U1, AS-U2, AS-U5.
SA › Administration › Admin users (`/admin/organization/users`).
1. `Add user` `uat.ops` (role Operations Admin) and `uat.mdr` (Master Data Reviewer) with full name, e-mail, mobile, initial password.
2. `Add user` with username `ab`.
3. `Lock` `uat.mdr`; sign in as `uat.mdr`; `Unlock`.
4. `Add user` `uat.sup2` with the role Platform Administrator; sign in as `uat.sup2` and open `platform.admin`: `Lock`, `Reset password`.
5. Arabic check.
Expected: (1) "User created." twice; both listed Active with their role (the list carries the role column; the employee code is derived from the username). (2) refused by the page: Username "The value has the wrong format." (`ORG-023` applies to a forced request). (3) `AUTH-003` "The account is locked. Try again later.", then sign-in works. (4) `Lock` and `Reset password` are disabled on the main administrator's Security tab ("The main administrator cannot be locked, deactivated or demoted…"); `TEN-012` applies to a forced request.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-03 — Sessions · standard
Refs: SA-08 · PLT S-X1 (PLT-010).
SA › Administration › Sessions (`/admin/sessions`).
1. With `uat.ops` signed in elsewhere: find the session (user, IP, since, last seen) › `Terminate`, confirm. `uat.ops` acts.
2. `Terminate` your own session.
3. Arabic check.
Expected: (1) "The session was terminated."; `uat.ops` is signed out at the next request. (2) refused `PLT-010`.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-04 — Platform audit log · critical
Refs: SA-09 · FND §3 · ACC S-AU1.
SA › Administration › Audit log (`/admin/audit`).
1. Filter today: entries for the client creation (UAT-OPS-02), deactivation / activation, top-up, role and user changes, session termination.
2. `Verify chain`.
3. `Export CSV`; as `uat.mdr` (no `audit.export`) try the export.
4. Try to edit or delete an entry (there is no control; note it).
5. Arabic check.
Expected: (1) one row per action with time, user, action, entity and details (deactivation / activation of the client, the top-up, the roles and users); nothing from the laboratories' own trails (tenant trails are separate, ACC Q9). The client **creation** is recorded in the new laboratory's own trail, not in the platform trail (open point 11). (2) "The audit chain is intact: n entries checked." (the SHA-256 chain over the stored rows). (3) a CSV with a header row; `uat.mdr` holds neither `audit.view` nor `audit.export`: the Audit log is absent from its menu and the export button needs `audit.export` (`AUTHZ-001` applies to a forced request). (4) immutable.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-05 — Platform parameters · critical
Refs: SA-19 · WF-12 step 3 · FND §6 · SET-001/002.
SA › Platform › Platform parameters (`/admin/parameters`).
1. Group "Security and sessions": minimum password length 10; "Subscriptions and defaults": trial days 7. `Save`.
2. Setup › Users in the UAT laboratory (as `lab.admin`): add a user with a 9-character password.
3. Create a client (UAT-OPS-07 or SA-04): its trial end.
4. Enter `-1` for the delay warning percent; then `Use the default` on the password length. Save.
5. Arabic check.
Expected: (1) "2 parameter(s) saved."; the rows are marked Changed with "Default: …" and `Use the default`. (2) refused — a laboratory that never changed the parameter reads the platform value: the user editor's hint reads "At least 10 characters." and the page refuses the 9-character password; `ORG-020` applies to a forced request. (3) trial of 7 days — only a client created by the wizard carries a trial (a manual client starts Active without one), so this step runs with UAT-OPS-07 (*gateway*). (4) `-1` is refused by the page (the field is marked invalid, nothing is sent; `SET-002` applies to a forced request); `Use the default` + `Save` → "1 parameter(s) saved.", the password length reads 8 without the Changed mark.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-06 — Notification templates · standard
Refs: SA-20 · FND §5 · PLT S-NTF (NTF-003).
SA › Platform › Notification templates (`/admin/notification-templates`).
1. Open `otp` (SMS): edit the Arabic text inserting `{code}` with the placeholder button. Save.
2. Edit `portal-url`: add `{unknown}`. Save.
3. `Deactivate` `portal-url`; create a client: the SMS text.
4. Arabic check.
Expected: (1) saved; the next OTP (UAT-REG-02 / UAT-OPS-07) uses the text. (2) refused `NTF-003`. (3) the built-in text is sent instead ("an inactive template falls back to the built-in text").
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-07 — Administrator profile · standard
Refs: SA-21 · ACC S-A1, S-A2.
SA › Platform › Profile (`/admin/profile`).
1. Language Arabic › `Save language`; sign out and in.
2. Change password with a wrong current password; then a valid change.
3. Arabic check (after step 1 the whole portal, including the sign-in page, is Arabic).
Expected: (1) "Your language was saved."; the portal opens in Arabic. (2) `ACC-010`, then "Your password was changed."
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-08 — Portal and tenant isolation · critical
Refs: cross-cutting "Tenancy & branches" · PLT S-PLT-403 · wave-4 fix (return URL per portal) · TRS/REG/RES/BIL S-TENANT.
1. Signed in as `lab.admin` (client portal), type the URL `/admin/clients`.
2. Sign out as `lab.admin`; sign in as `platform.admin` on the same browser.
3. As `labb.admin` (UAT Lab B): Search registrations by the accession of UAT-REG-03; Patients by Mona's number; Payments register.
4. As `uat.ops`: Clients › UAT Lab B › "Open wallet"; try any client-portal page URL (`/registration/new`).
5. Arabic check.
Expected: (1) the URL redirects to the client dashboard — the client portal has no admin routes; a forced API call answers `PLT-403`. (2) the platform administrator lands on the **Super Admin** home, never on the client dashboard (each portal returns only to its own routes). (3) nothing found: no visit, patient or request of the UAT laboratory is visible from Lab B. (4) the wallet opens (Subscriptions & wallets with the client selected); a client-page URL redirects to the Super Admin home — client pages are not available in the Super Admin portal (the server scopes every call to the platform tenant regardless).
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-09 — Announcements · standard
Refs: SA-22, CP-03, CP-05, SA-04 · LIS-CR-013 (reviewer point C-17, decision D-4).
As `platform.admin`, then `lab.admin` of the demo laboratory.
1. Super Admin › Announcements › *Add*: title and body in both languages, https link, placement *Sign-in*, schedule from today. Save.
2. Upload a PNG; then try an SVG; then a file over 512 KB. Check the preview in both languages.
3. *Activate*. Open `<base>/lab/demo-lab`; click the link.
4. Restrict the audience to another client; reload the sign-in page.
5. Create and activate a *Welcome* announcement. Sign in as `lab.admin`: close the dashboard card, revisit the dashboard, sign in again.
6. SA-04 › demo laboratory › "Hide announcements for this client" on; reload both placements. As `lab.admin`, try `PUT /api/v1/settings` with `hideAnnouncements`.
7. Set an end date in the past and try to activate.
8. Sign in as a `platform.view`-only user and open the page.
9. Arabic check.
Expected: (1) the announcement is a draft (inactive). (2) PNG accepted; SVG refused with ANN-008; the large file with ANN-007. (3) the hero of the sign-in page shows the announcement in place of the slogan; the click count rises by one. (4) the slogan is back. (5) the card shows once per sign-in and stays closed within the session. (6) both placements disappear for that client; the laboratory's own change is refused with SET-004. (7) refused with ANN-010. (8) register and editor read-only. (9) right-to-left, Arabic texts.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-10 — Subscription reminders, grace and expiry · critical · *gateway*
Refs: SA-03, SA-04, SA-19, SA-20, CP-03, CP-05, CP-66 · BR-31 · LIS-CR-012 (reviewer point C-16, decision D-3).
Set-up: a client whose trial ends in two days (SA-04 or the subscription wizard with trial days = 2 on SA-19), and one whose trial ended more than the grace period ago.
1. As an administrator of the first client, sign in; as a plain member, sign in.
2. SA-05 › *Run charges now* is not involved; run the reminder pass (restart the service or wait for the hourly job) and read the pickup folder / gateway; run it again.
3. As a member of the expired client, sign in; as its administrator, sign in, open Account › Wallet, then open Registration.
4. SA-03: the "Expiring soon" tile; SA-04: *Subscription* = Expiring, In grace, Expired; *Export CSV*.
5. SA-19: set the grace days to 61; then to 7. SA-20: open the templates `subscription-expiry`, `wallet-low`, `subscription-grace`.
6. Repeat 1 and 3 in Arabic.
Expected: (1) the administrator sees the banner under the top bar and the modal; the member sees the modal only; both link to the wallet. (2) one SMS and one e-mail to the manager with the days and amount; the second run sends nothing. (3) the member is refused with SUB-020 on the sign-in page; the administrator signs in, the wallet opens, every other page answers SUB-021. (4) the tile counts the two clients; the filters list them with days left and the administrator's mobile; the CSV downloads. (5) 61 refused with SET-002, 7 saved; the three templates exist with their placeholders. (6) right-to-left, Arabic texts.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

#### UAT-SUP-11 — One browser, two portals · critical
Refs: CP-03, SA-01, cross-cutting Security · LIS-CR-014 (G1).
1. Tab A: sign in at `<base>/lab/demo-lab`. Tab B, same browser: sign in at `<base>/admin/login`. Reload both.
2. In tab B, sign out. Reload tab A.
3. Sign in again in B; sign out in A.
4. Open a second laboratory tab A2 and sign out in A. Change the branch in a laboratory tab with A2 open.
5. From a laboratory session, open `/admin/clients` in a new tab.
6. DevTools › Application › Cookies.
Expected: (1) tab A stays signed in and its address does not change; after the reloads each tab keeps its own session. (2) B lands on `/admin/login`; A is still signed in. (3) A lands on `/lab/demo-lab`; B is unaffected. (4) A2 follows to `/lab/demo-lab`, the admin tab does not react; the branch change reaches A2 only. (5) the new tab goes to `/admin/login`; the laboratory tab is untouched. (6) `__Secure-rt` and `__Secure-rta` both exist with Path `/api/v1/auth`, HttpOnly, Secure, SameSite Strict.
Result: ☐ Pass ☐ Fail · Tester ____ · Date ____ · Defect ____

---

## 6. Results sheet template

One row per script execution; copy the table into `docs/uat/results/<yyyy-mm-dd>_<persona>.md` (see the README) or into
the shared sheet of the session.

| Script id | Title | Priority | Tester | Date | Build (VERSION) | Result (Pass / Fail / Blocked) | Failed step | Defect ref | Severity | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| UAT-ADM-01 | Branches | critical | | | | | | | | |
| … | | | | | | | | | | |

Severity scale (defect log): **S1** — a critical script cannot pass or data is wrong/lost (wrong accession, wrong
amount, wrong patient, unverified report printed); **S2** — a rule is not enforced or a page cannot be used for its
purpose, with a workaround; **S3** — wrong text, layout, Arabic translation or label; **S4** — cosmetic or suggestion.
"Blocked" names the blocker (gateway, missing precondition, open S1 of another script).

## 7. Acceptance criteria per persona

A persona accepts Release 1 when **every critical script of the persona passes** on the build under test, **no
severity-1 defect is open** against any of its scripts, and every failed standard script has a defect with an agreed
severity and target release. The laboratory signs when all client-portal personas have accepted; National Technology
signs when the three Super Admin personas have accepted.

| Persona | Scripts | Critical | Signature (name, date) |
| --- | --- | --- | --- |
| Laboratory administrator | UAT-ADM-01…11 (11; ADM-06, 07 withdrawn) | ADM-01, 02, 03, 08 | |
| Accounts | UAT-FIN-01…06 (6) | FIN-01, 02 | |
| Cashier | UAT-CSH-01…10 (10; CSH-01, 02, 09, 10 withdrawn) | CSH-03, 04, 05, 07 | |
| Reception clerk | UAT-REG-01…16 (16) | REG-01, 03, 04, 05, 07, 08, 09, 10, 11, 16 | |
| Phlebotomist / technician (samples) | UAT-SMP-01…03 (3) | SMP-01 | |
| Laboratory technician | UAT-RES-01…08 (8) | RES-01, 02, 04, 08 | |
| Unit head / pathologist | UAT-VER-01…07 (7) | VER-01, 02, 03, 05, 06 | |
| Operations Admin | UAT-OPS-01…07 (7) | OPS-01, 02, 03, 05, 07 | |
| Master Data Reviewer | UAT-MDR-01…03 (3) | MDR-01 | |
| Super Admin | UAT-SUP-01…08 (8) | SUP-01, 02, 04, 05, 08 | |

78 scripts, 45 critical.

## 8. Coverage

### 8.1 Release-1 pages → scripts

| Page | Scripts |
| --- | --- |
| CP-01 Public website | OPS-07 |
| CP-02 Subscription wizard | OPS-07 (*gateway*) |
| CP-03 Login & recovery | REG-01, REG-02 (*gateway*), ADM-04 |
| CP-04 Branch selection | REG-01, CSH-08 |
| CP-05 Dashboard | REG-01, VER-07 |
| CP-06 Registration | REG-03…09, REG-13, REG-14, SMP-01, SMP-02 |
| CP-10 Search registrations | REG-03, REG-10, REG-11, SMP-01, SMP-03, VER-06 |
| CP-11 Patients | REG-04, REG-15 |
| CP-12 Price query | FIN-02, FIN-04, REG-12 |
| CP-18 Worklist | RES-01, RES-08, SMP-01 |
| CP-69 Lab Tracker | REG-16 |
| CP-19 Results by accession | RES-02…08, VER-01…06 |
| CP-23 Payments (+ refund action of CP-24, void) | CSH-03…08, REG-09, FIN-05, FIN-06 |
| CP-26 Cancellations (as action of CP-10) | REG-11, SMP-03 |
| CP-28 Treasuries | withdrawn (LIS-CR-004) |
| CP-29 Treasury requests | withdrawn (LIS-CR-004) |
| CP-32 Payers & contracts | FIN-01 |
| CP-33 Base price lists | FIN-02, FIN-03 |
| CP-36 Branches | ADM-01 |
| CP-37 Roles & privileges | ADM-02, ADM-05, RES-08 |
| CP-38 Users | ADM-03, ADM-04, ADM-05 |
| CP-39…CP-44 Clinical groups, Services, Tests, Service sequence, Sample types, Units | ADM-08, ADM-09, RES-03 |
| CP-46 Preparations · CP-47 Comment library · CP-48 Doctors · CP-56 Microbiology | ADM-10, REG-13, RES-04 |
| CP-65 Profile | ADM-03, ADM-11 |
| SA-01 Login · SA-02 Home · SA-03 Dashboard | OPS-01 |
| SA-04 Clients | OPS-02, OPS-03, OPS-04, MDR-01 |
| SA-05 Subscriptions & wallets | OPS-05 |
| SA-06 Roles · SA-07 Admin users | SUP-01, SUP-02 |
| SA-08 Sessions · SA-09 Audit log | SUP-03, SUP-04 |
| SA-10 Countries · SA-11 Governorates | OPS-06 |
| SA-12 Global catalogue A | MDR-01 |
| SA-13 Global lists | MDR-02 |
| SA-14 Microbiology masters | MDR-03 |
| SA-19 Platform parameters · SA-20 Notification templates · SA-21 Profile | SUP-05, SUP-06, SUP-07 |

### 8.2 Workflows → scripts

| Workflow | Release-1 steps covered by |
| --- | --- |
| WF-01 Laboratory onboarding | OPS-07 (wizard), OPS-02 (manual), ADM-01…03, FIN-01, FIN-02, REG-03 |
| WF-02 Walk-in patient — registration to receipt | REG-03…09, CSH-03, CSH-04 |
| WF-06 Result entry, review, verification and printing (R1 part) | RES-01…04, VER-01…06 |
| WF-09 Money movements (R1 part: payments, refund, cancellation) | CSH-03…08, REG-09, REG-11 |
| WF-10 Pricing maintenance (R1 part: lists, payers) | FIN-01…04 |
| WF-11 Privileges administration | ADM-02, ADM-03, ADM-05 |
| WF-12 Platform operations | OPS-02…05, MDR-01…03, SUP-05, SUP-06 |

Business rules exercised: BR-01, 02, 03 (emulated), 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14, 15, 17, 19, 24, 25, 26, 27.
Not exercised in Release 1: BR-16 (cash closing, R2), BR-18 (sample replacement, R2), BR-20 (no hidden helper test in the
UAT data; add one if the laboratory's catalogue uses them), BR-21…BR-23 (auto-verification, R2).

### 8.3 Release-1 pages without a script, and why

- None of the pages marked `R1` in the SRS or registered with `release: 'R1'` in the navigation lacks a script.
- Pages marked "R2 | seeded in Release 1" on the client side (CP-45 Patient conditions, CP-49 Diagnoses, CP-51 Shortcut
  sets, CP-52 Test rules, CP-53 Reasons, CP-54 Report templates, CP-55 Additional services, CP-57 Parameters) have no
  client script: their editors are hidden by the release scope; the seeded data is used indirectly (reasons in REG-07,
  REG-11, CSH-07; preparations in REG-13) and their global editors are tested in MDR-02.
- CP-24 Refunds, CP-26 Cancellations and CP-34 Contract pricing are Release 2 pages; the Release-1 actions that replace
  the first two are covered (CSH-07, REG-11); CP-34 has no Release-1 behaviour beyond "credit tiers quote nothing" (FIN-04).
- Scripts marked *gateway* (REG-02, OPS-07, VER-07) need the notification gateway of the test lane; without it they are
  recorded as blocked, not as passed.

## 9. Open points for the business owner

| # | Point | Where it shows |
| --- | --- | --- |
| 1 | Master-data update policy (OQ-03): Release 1 copies the global catalogue only at client creation. Confirm this is acceptable for the pilot. | MDR-01 |
| 2 | Received gate (OQ-04): Release 1 auto-receives on save; confirm the pilot laboratory accepts entering results without a scan. | SMP-01, RES-02 |
| 3 | Review before verify (OQ-05) is off by default and cannot be changed by the laboratory in Release 1 (CP-57 is Release 2). | RES-04, VER-01 |
| 4 | Accession format `ACC-yyyy-nnnnn`, patient `PAT-yyyy-nnnnn`, sample `SMP-…` (OQ-07) until configurable formats arrive. | REG-03 |
| 5 | The notification gateway of the test lane decides whether the *gateway* scripts can run. | REG-02, OPS-07, VER-07 |
| 6 | The refund reason list (CP-24) is read from the catalogue, so the Cashier role needs `catalogue.view` (now in §4); alternatively Billing exposes its own reason list. | CSH-07 |
| 7 | Service-entry resolution (code + Enter) needs `registration.create` (REG intake §6): on the Price query page a `registration.view`-only role (Accounts) falls back to the picker without a message. Confirm, or relax the policy to `registration.view`. | FIN-02, FIN-04 |
| 8 | Closed 2026-10-20: the treasury-access list was withdrawn with the treasury (LIS-CR-004). | — |
| 9 | Printing a verified report from Search registrations › Documents needs `results.export` and the print result right; the Reception role of §4 holds `results.view` only (preview works). Decide whether reception prints reports. | VER-06, REG-10 |
| 10 | Closed 2026-10-24 (LIS-CR-007 C-05): mobiles are typed as dial code + national number and a trunk 0 is always dropped; the "Any" choice no longer exists. | — |
| 11 | The creation of a client (tenant, wallet, branch, administrator, master-data copy) is audited in the **new laboratory's** trail, where its administrator sees "Created Tenant · Platform Administrator"; the platform trail starts with the first later change (deactivation, top-up). The security-event ledger records `TENANT_PROVISIONED`. Decide whether the platform trail must carry the provisioning (D-15 of the Super Admin dry run). | SUP-04, OPS-02 |
| 12 | Closed after the run: every client feature route carries `tenantGuard` (D-17 fixed in `app.routes.ts`); SUP-08 step 4 to be re-tested. |
| 13 | Route-provided facades outlive their pages (Angular keeps the route injector): a list page opened again showed the previous visit's data — even another sign-in's — until `Refresh` (D-14, fixed for the platform, organization and catalogue features by re-reading on open; D-08 was the billing case). The laboratory features (registration, patients, results, pricing, treasury, …) follow the same pattern and need the same treatment. | MDR-01, all list pages |

## 10. Revision history

| Version | Date | Author | Change |
| --- | --- | --- | --- |
| 0.1 | 2026-10-10 | Claude Code (documentation agent), for review by Mohamed Hashem | First set of Release-1 UAT scripts: 78 scripts (45 critical) across ten personas, results sheet, acceptance criteria, coverage. |
| 0.2 | 2026-10-10 | Claude Code (UAT dry-run agent) | Corrections from the automated dry run of UAT-ADM-01/02/03/06/07/08 and UAT-REG-01/03/04/05/07/08/09/10/11 (`results/2026-10-10_dry-run_admin-reception.md`): §3 says what the screen shows for a code and which rules the page enforces before any call; §4 Reception role gains `samples.view`; expected texts aligned with the built screens (validator and page refusals, lockout after five failures, `Create and use`, dry-run line messages, treasury branch picker, decision time on treasury rows, role editor flow). Ids unchanged. |
| 0.3 | 2026-10-10 | Claude Code (UAT dry-run agent) | Corrections from the automated dry run of UAT-CSH-01/02/03/04/05/07/09, UAT-FIN-01/02, UAT-SMP-01, UAT-RES-01/02/04/08 and UAT-VER-01/02/03/05/06 (`results/2026-10-10_dry-run_laboratory.md`): §4 Cashier role gains `catalogue.view`; expected texts aligned with the built screens (page validators on the Payments / Refunds / price-grid forms, the Refunds page reached by `Refund the balance`, validator message for the web username, no deactivate action on the Default tier, picker fallback of the service entry for Accounts, status boxes of the worklist, comment-library panel, absent Verify / Amend actions, RES-014 on printing a unit with missing values, delta tooltip text, status log as the carrier of signatures and print copies, preview-only for the clerk); open points 6–10. Ids unchanged. |
| 0.4 | 2026-10-10 | Claude Code (UAT dry-run agent) | Corrections from the automated dry run of UAT-OPS-01/02/03/05, UAT-MDR-01 and UAT-SUP-01/02/04/05/08 (`results/2026-10-10_dry-run_super-admin.md`): §4 Operations Admin keys (`wallet.view/create/manage`); expected texts aligned with the built screens (page refusals for the deactivation reason, top-up reference and amount, empty role, username format, parameter range; rows offering only the opposite life-cycle action; disabled Lock / Reset on the main administrator; no Privileges tab in the platform user editor; the global catalogue preconditions and the age band of MDR-01; trial only for wizard clients; the audit chain message; client creation audited in the laboratory's trail); open points 11–13. Ids unchanged. |
| 0.5 | 2026-10-14 | Claude Code | UAT-ADM-08 gains step 13 (the parent-services block of the test editor, change request 2026-10-14: leave and rejoin a profile, locked chip on a test service); the Arabic check becomes step 14. Ids unchanged. |
| 0.6 | 2026-10-20 | Claude Code | Treasury removed from the system (change request LIS-CR-004): UAT-ADM-06, UAT-ADM-07, UAT-CSH-01, UAT-CSH-02, UAT-CSH-09 and UAT-CSH-10 withdrawn (ids reserved); UAT-CSH-08 becomes "Payment stamped with the session branch"; UAT-REG-09 becomes "Registration saved without a payment" (amount 0); payment steps, personas (no `treasury.*` keys), run order, coverage and open point 8 updated. |
| 0.7 | 2026-10-20 | Claude Code | Sign-in by address (change request LIS-CR-005, BR-28): set-up note (laboratory users at `<base>/lab/<slug>`, Super Admin personas at `<base>/admin/login`), UAT-REG-01 step 1a and expectations (another laboratory, admin address, unknown address, bare address; idle return), UAT-OPS intro (admin address, laboratory user refused), UAT-ADM-01 and the provisioning / subscription expectations print the new address. |
| 0.8 | 2026-10-22 | Claude Code | New script UAT-REG-16 Lab Tracker (CP-69, change request LIS-CR-006): strip and chips, table and board, next-step links, search, live refresh, desk views, rights, Arabic. Reception persona and coverage updated. |
| 0.9 | 2026-10-22 | Claude Code | UAT-REG-16 gains step 8 (period picker: yesterday, last 7 days, from / to); rights and Arabic checks become steps 9 and 10. Ids unchanged. |
| 1.2 | 2026-10-25 | Claude Code | Reviewer round 2 (change request LIS-CR-014): new UAT-SUP-11 (one browser, two portals); notes under UAT-OPS-07 (early mobile / username checks, Back between steps), UAT-REG-10 (column chooser, Print, Excel) and UAT-REG-16 (actions column, Services rows, Back chip, chooser, Print / Excel). Ids unchanged. |
| 1.1 | 2026-10-25 | Claude Code | Reviewer features (change requests LIS-CR-008, CR-010, CR-011, CR-012, CR-013): new scripts UAT-CSH-11 (cash ledger, cash entries, day close), UAT-SUP-09 (announcements), UAT-SUP-10 (subscription reminders, grace and expiry); notes under UAT-REG-03 (collected outside per sample), UAT-ADM-01 / UAT-OPS-02 / UAT-OPS-07 (localities), UAT-MDR-02 (locality import, template design), UAT-VER-06 (report designs). Ids unchanged. |
| 1.0 | 2026-10-24 | Claude Code | Reviewer quick wins (change request LIS-CR-007, response LIS-RC-001): UAT-REG-10 / UAT-REG-11 row actions on one line with a *More* menu; UAT-CSH-03 / 04 payment card first, focus on the amount, "Show all"; UAT-REG-01 keep-this-address strip; UAT-REG-03 and UAT-OPS-07 mobiles typed after the dial code; UAT-ADM-10 doctor code left blank → DR-nnn; UAT-RES-03 range shown before entry; UAT-ADM-08 "Used for cultures"; UAT-MDR-02 Microbiology tab names and order (Antibiotic classes, Culture specimens); UAT-OPS-01 silent-clients tile; UAT-ADM-08 / UAT-MDR-02 shortcut sets and test rules in the client menu. Open point 10 closed. Ids unchanged; the per-script notes are the "LIS-CR-007" lines under each script's Refs. |
