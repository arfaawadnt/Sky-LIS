# Re-test 2026-10-11 · Tester R1 · Client portal (defects A-01…A-15, B-01…B-14)

Build under test: the running fix build. The SPA is `http://127.0.0.1:4300`, proxying to the API on `127.0.0.1:5102`.
The database is `lis` on the private PostgreSQL 5433. Laboratories:

- Demo laboratory (tenant `01a12494-…`): personas `uat.reception`, `uat.cashier`, `uat.tech`, `uat.tech.chem`,
  `uat.head` and `lab.admin.manager`.
- Walk-through Laboratory (tenant `01a124d7-…`): personas `wt.admin`, `v11.reception` and `v11.temp`.

Tester: automated browser session, one tab created for this run (`tab-2`, `127.0.0.1` only). A second tab
(`tab-5`) was opened briefly for the cross-tab sign-out check and then closed. Other tabs were not touched.

This was a test-and-report run. No code, dictionary, test or script was changed. Every record created carries the
prefix **V12**. Every account or setting that was changed has been restored (§5).

Method, as in the original runs:

- Clicks and input were dispatched from the page context. This runs the same Angular handlers as a user's click.
- Print frames were stubbed so that the rendered document text was captured.
- CSV blobs were captured in the page. Nothing was written to disk.
- Server refusals were read from the browser's network log.
- Saves were checked with read-only `lis_owner` queries.
- A typed address-bar URL was simulated with a full page load (`navigate`).
- Back / forward was simulated with the history API: `pushState` + `popstate`, `history.back()` / `forward()`.

## 1. Summary

| Verdict | Count | Defects |
| --- | --- | --- |
| **FIXED** | 28 | A-01…A-05, A-07…A-15, B-01…B-14 |
| **NOT FIXED** | 0 | — |
| **PARTLY** | 0 | — |
| **Not verifiable** | 1 | A-06: the refund page cannot be reached any more (new defect **R1-01**) |

New defects found: **1 × S2, 1 × S3, 5 × S4** (§4).

- **R1-01 (S2).** This is a side effect of the A-08 fix. The new route guard treats `/billing/refunds` as Release 2.
  The **Refund the balance** link on Payments now lands on the Dashboard, so the client portal cannot record any
  refund. The R1 workflow "Refund (from Payments)" (RFD-01…05 of the original run) is lost.

The other fixes hold:

- A-01: sign-out now ends with a full load of `/login`, and the sign-out is broadcast to other tabs.
- B-04: refusals show per section and typed input is kept.
- B-05: editors follow back / forward.
- B-06: 204 answers are treated as success.
- A-05: the print-settings read works.
- A-08: the route guard works.

The smoke run of every R1 client page under six personas found no other regression.

## 2. Defect re-test

| Id | Original finding | Steps re-run | Observed now | DB check | Verdict |
| --- | --- | --- | --- | --- | --- |
| A-01 (S1) | After sign-out / sign-in in the same tab, Worklist and Results kept the previous user's data and rights | `uat.tech` opens Worklist and Results ACC-2026-00017 (CHEM + HEMA) → sign out → `uat.tech.chem` in the same tab opens both. Reverse: `uat.reception` → `uat.tech` on ACC-2026-00020 | Sign-out does a full page load of `/login`; the helper scripts were gone afterwards. tech.chem: Worklist has 7 rows, all Clinical Chemistry, no Haemoglobin or Blood group row (tech saw 9). Results reads "1 report units are outside your result rights"; there is no Haematology tab and no enabled input on the verified case. Reverse: tech has 2 enabled inputs and Save / Review on ACC-2026-00020, and saved K 4.2 and GLU 95 ("2 results saved.") | `app.results` ACC-2026-00020: K Complete 4.2 (later verified), GLU 95 | **FIXED** |
| A-02 (S3) | Reception could open the Report comment / Internal note dialogs | `uat.reception` → Results ACC-2026-00020 (Ordered) and ACC-2026-00017 | Only Preview and History are offered. There is no Report comment, Internal note, Save or Review button, and the grid inputs are disabled (0 of 2 enabled). Tech and head still see both buttons | — | **FIXED** |
| A-03 (S3) | Documents tab offered to reception without `files.view` (403) | `uat.reception` → Patients › PAT-2026-00006 › Open | Tabs are **File / Visits** only. Manager sees "Documents 1" and the list loads (`includeInactive=false`, 200). "Show deactivated documents" is now off by default | — | **FIXED** |
| A-04 (S3) | Treasuries list showed the raw branch GUID to reception / cashier | `uat.reception` and `uat.cashier` → Finance › Treasuries; `v11.reception` likewise | Branch column reads `MAIN · Main Branch` and `NORTH · North Branch` (WT: `V11-NORTH · V11 North Branch (edited)`) | — | **FIXED** |
| A-05 (S3) | Every payment printed a receipt although `autoPrintReceipt` = false (setting read via `/settings`, refused) | `uat.cashier` → Payments ACC-2026-00020: cash 120 (RCT-2026-00028), card 90 V12-CARD-01 (RCT-2026-00029) | `GET /api/v1/billing/print-settings` → 200. The cash-desk resource is resolved with value **false**. **0 print jobs** after either payment. Reprint and Print invoice still print on request | `app.tenant_settings` autoPrintReceipt false (unchanged); `app.payments` RCT-28 120 Cash Posted, RCT-29 90 Card V12-CARD-01 Posted | **FIXED** |
| A-06 (S3) | The second refund kept the first refund's note, reason, method and reference | Paid ACC-2026-00020 in full, manager cancelled GLU (refund due 90), then `Refund the balance` as manager and as cashier | **Cannot be reached.** The link (`/billing/refunds?visitId=01a12b33-…`) lands on `/account/dashboard` (R1-01). The shared movement form does now reset after a save: after the 120 cash payment it showed amount 90, method Cash and an empty reference. The refund itself could not be exercised | `app.refunds` for the visit: 0 rows | **Not verifiable** (blocked by R1-01) |
| A-07 (S4) | Raw keys `account.profile.languageSaved` / `billing.loading` in toasts | `uat.cashier`: first Payments load; Profile › العربية › Save; English › Save | No toast on the first Payments load. The Arabic save shows "تم حفظ اللغة."; the English save shows "Your language was saved." | `preferred_language` ar → en (restored) | **FIXED** |
| A-08 (S3) | Unit head could open `/billing/payments` and R2 routes by URL | `uat.head`: full-load `/billing/payments`, then `/billing/refunds`, `batch-payment`, `cash-closing`, `/samples/collection`, `/account/audit`, `/registration/new`, `/organization/users` | Each one redirects to the portal home `/`. Permitted pages (`/results/worklist`, `/catalogue/tests`, `/account/profile`) open. `v11.reception` typing `/organization/users` lands on the Dashboard. **Side effect: R1-01, R1-03** | — | **FIXED** |
| A-09 (S3) | Patients list had no visit count / last visit | `lab.admin.manager` → Patients | **Visits** and **Last visit** columns are present, for example PAT-2026-00002 "7 · 10 Oct 2026, 21:45" and PAT-2026-00007 "2 · 11 Oct 2026, 10:57" | `count(visits)` / `max(registered_at)` per patient equals all 7 rows | **FIXED** |
| A-10 (S4) | Letterhead printed "Main Branch" (English) under the Arabic laboratory name | Print job order (ACC-2026-00017), receipt and invoice (ACC-2026-00020), report, price quote | Letterhead reads `Laboratory · Main Branch` / `المختبر · الفرع الرئيسي` on every captured document | — | **FIXED** |
| A-11 (S4) | Price query line read "Not priced yet." for a gender-refused service | Price query, gender Male, add HCG | The banner **and** the line read "HCG: The service is restricted to the other gender." | — | **FIXED** |
| A-12 (S4) | Dashboard: "today" on a past day, mixed digits, technical names, title ≠ nav | Manager: day 2026-10-10; Arabic | Captions read "on 10 Oct 2026". Recent activity shows "Result log", "Result verified" and "User membership". In Arabic, the tiles, chart values and day summary all use Arabic-Indic digits, and the title and nav both read "لوحة المعلومات". Residue: the chart caption "آخر 7 أيام" keeps a Latin 7 (R1-06) | — | **FIXED** |
| A-13 (S4) | F2 with an empty search showed "No service matches." | Price query and Registration: `Browse (F2)` | The picker lists the catalogue at once (K, NA, GLU, HGB, HCG, CREA, RENAL, BLDGRP) | — | **FIXED** |
| A-14 (S4) | Worklist search did not match patient names | `uat.tech.chem` → Worklist, search "walk-in two", then ACC-2026-00019 | 4 rows of V11 Walk-in Two, then 2 rows. The placeholder still reads "Accession, sample or patient no.…" (R1-06) | — | **FIXED** |
| A-15 (S4) | Registrations CSV wrote raw UTC ISO dates | Manager → Search registrations → Export CSV | `Date` is `"11/10/2026, 10:57"`, as in the grid. Cancelled lines are marked "HCG (Cancelled)" | — | **FIXED** |
| B-01 (S4) | Add branch: Currency empty | `wt.admin` → Branches › Add branch | Currency starts at `EGP` | — | **FIXED** |
| B-02 (S3) | Branch saved with a governorate of another country | Add V12-SOUTH with V11 Land + Aswan; Edit V11-NORTH with country V11 Land (governorate Alexandria) | 422 **ORG-017** "The governorate must belong to the chosen country." is shown on the Governorate field. Nothing is saved. Note: the governorate list is still not filtered by the chosen country | `app.branches`: V11-NORTH stays Egypt / Alexandria (`gov_in_country` t, modified 11:12); no V12-SOUTH row | **FIXED** |
| B-03 (S4) | ORG-008 shown as one generic banner, no field marked | Add branch with e-mail `not-an-email`; phone `09-ab` / `abc`; English and Arabic | E-mail: 422 **ORG-016**, with the field message "The e-mail address is not valid." / "عنوان البريد الإلكتروني غير صالح.". Phone: 422 **ORG-015**, with the field message (EN / AR) | — | **FIXED** |
| B-04 (S2) | Test editor lost bands / list values on a partial refusal and never showed the reason | New V12-K (Numeric): 2 bands + timing 6 / min 10. Then add an overlapping band + valid timing 8 / 2. Then panic low 4 inside the normal range. New V12-RL (Restricted list) with default on "Positive" (placeholder unticked) | **Create:** "The test was created, but part of its configuration was refused — correct the tab marked "!"…". The Timing tab is marked `!` with the timing rule shown, the typed 6 / 10 is kept, and both bands are saved. **Update:** "Part of the changes was saved…"; Reference ranges `!` "Two bands of the same sex and condition cover the same ages."; all 3 typed bands are kept and the timing is saved. CAT-053: "The changes were refused…" with the limits rule, and the typed panic low 4 is kept. CAT-058: List values `!` "Only a placeholder can be the default…", and all 3 values are kept. Corrections save ("Saved.") | `app.tests` V12-K Numeric, duration 8 / min 2, 2 bands; V12-RL list "—" default + placeholder; trail `LabTestCreated`, `…RangesChanged` ×3, `…TimingChanged`, `…ListValuesChanged` | **FIXED** |
| B-05 (S2) | Editors kept the previous record after back / forward and acted on it | Test editor V11-GLU → V11-HGB, back, forward. Service editor V11-GLUP → V11-PKG, then Deactivate (confirm read, then cancelled), back, forward | Heading, code and name follow the address each time. The Deactivate confirm names **V11-PKG**. See R1-07 for one corner case | Neither service changed | **FIXED** |
| B-06 (S3) | 204 answers handled as failures (no toast, reset dialog stays open, stale tier error) | V11 Temp role: Activate, special rights ×2, result rights ×2, Deactivate. v11.temp: assign role, branches, reset password, lock, unlock. Tier: duplicate add, rename ×2, reactivate, deactivate | Toasts: "Activated.", "Special rights saved." ×2, "Result rights saved." ×2, "Deactivated.", "Role assigned." ×2, "Branches saved.", "Password reset; the user's sessions were ended." (the dialog closes), "User locked.", "User unlocked.", "Tier renamed." ×2 (the stale add-tier error is cleared in rename mode), "Tier reactivated.", "Tier deactivated." | `security_events` PRIVILEGE_MATRIX_CHANGED ×4, MEMBERSHIP_CHANGED (role ×2, branches, password-reset, locked, unlocked). Trail `RoleChanged` ×6, `TenantMembershipRoleChanged` ×2, `ContractTierReactivated` / `…Deactivated` | **FIXED** |
| B-07 (S3) | Treasury picker cached for the app's life (survived deactivation / sign-out) | `wt.admin`: Treasury access → deactivate V11-NORTH-CASH → Treasury access → reactivate → Treasury access. Then sign out and sign in as `v11.reception` | The picker drops V11-NORTH-CASH after the deactivation and offers it again after the reactivation, both without a reload. v11.reception sees both | Trail `TreasuryDeactivated` / `TreasuryReactivated`; both treasuries active | **FIXED** |
| B-08 (S4) | Inactive sample condition REFR offered | New test and V11-GLUP service editor, sample type V11-SER | Conditions offered: FAST and RAND only | — | **FIXED** |
| B-09 (S4) | Export ignored the filter; mixed formats | Clinical groups export with V11-IMM inactive; roles export with filter Inactive; branches export with filter Inactive; preparations and doctors exports | Each export holds exactly the listed rows. Flags read `Yes` / `No`; preparations read `Option list` / `Yes / No` / `All patients`; doctors list branch codes `V11-NORTH \| MAIN` or `All branches`. The file name uses the local date. Residue: the users export writes the branch **count** (R1-06) | — | **FIXED** |
| B-10 (S3) | No export on Branches, Roles, Users, Payers, Treasuries | Export CSV on each of the five pages | Each one downloads `<page>-2026-10-11.csv` with a BOM and the grid's columns (for example `roles` with filter Inactive → only V11 Temp role) | — | **FIXED** |
| B-11 (S4) | Lock message fit a throttle lock; `/login?returnUrl=%2Flogin` after sign-out | Lock v11.temp, sign in as v11.temp; sign out several times | "Your account was locked by an administrator. Contact your administrator to unlock it." After sign-out the address is a clean `/login` | `SIGN_IN_FAILED locked_by_administrator` | **FIXED** |
| B-12 (S4) | Viewers could type in Service sequence and the test editor | `v11.reception`: Service sequence, V11-GLU test editor, V11-GLUP service editor | Sequence: no Save or Discard, and every sequence input is disabled (only the search and type filter are enabled). Test editor: 67 of 67 inputs disabled, no Save. Service editor: 28 of 29 disabled; only the (hidden-tab) member search box is enabled, which is harmless | — | **FIXED** |
| B-13 (S4) | Formula message repeated the position | New Formula test, `[V11-GLU] +`, Check formula | "The formula is not valid. (position 12) — The formula ends too early." The position appears once | — | **FIXED** |
| B-14 (S4) | The branches Save reset an unsaved role choice | v11.temp › Role & branches: choose V11 Technician, press the branches Save | A "Change the role" confirm appears. Confirm → "Role assigned." + "Branches saved.", and the role stays selected. Restored to V11 Reception with Assign role | `tenant_memberships` v11.temp → V11 Reception | **FIXED** |

## 3. Regression smoke (page × persona)

Each page was opened by in-app navigation, which runs the route guard, the facade re-read and the dictionary load.
"Main action" is the action exercised in the row; "—" means the page was opened and its data loaded without an
error banner or toast.

| Page | Persona(s) | Loads | Main action works | Verdict |
| --- | --- | --- | --- | --- |
| Dashboard | reception, cashier, tech, manager, wt.admin | yes | day picker 10 Oct, Arabic, quick actions | PASS, but see R1-02 (delayed table empty) |
| Registration (new) | reception (V12), manager | yes | new patient (validation EN / AR, created PAT-2026-00008); K + GLU; Cash; unpaid save → ACC-2026-00020, "1 tube(s)", no auto print; Clear | PASS |
| Edit visit | reception | yes | Treatment no. V12-T1 → "The changes were saved." (revision 3) | PASS |
| Search registrations | reception, cashier, manager, wt.admin | yes | Samples dialog, Documents › Print job order, Edit, Cancel services (manager, GLU, reason + note), Export CSV | PASS |
| Price query | cashier, reception, manager | yes | quote K + RENAL, Print quote, Continue to registration (services and tier carried), HCG refusal, F2 | PASS |
| Patients / file | reception, manager | yes | list (Visits / Last visit), Edit PAT-2026-00008 (bad mobile / e-mail refused, then e-mail + comment saved), Documents tab (manager only) | PASS |
| Worklist | tech, tech.chem, reception, head, wt.admin | yes | rights filter, name search, row `Results` → Results by accession (WRK-06 of the original run, now executed) | PASS |
| Results by accession | tech, tech.chem, reception, head | yes | tech entry + save; head Verify → "1 results verified (electronic signature)."; Print report → printed, copy 1 | PASS |
| Payments (Collect / Register) | cashier, manager | yes | 2 payments, Reprint, Print invoice, Register tab, CSV | PASS, but **R1-01** (refund link) |
| Refunds | cashier, manager | **no** | the link from Payments redirects to the Dashboard | **FAIL R1-01** |
| Treasuries | reception, cashier, wt.admin, manager | yes | branch names, (de)activate, export | PASS |
| Treasury access | reception, v11.reception, wt.admin | yes | request NORTH-CASH "V12 retest desk" → TRQ-2026-00011 Pending → cancel with reason; picker freshness | PASS |
| Payers & contracts | cashier, wt.admin, manager | yes | tiers dialog (duplicate, rename, (de)activate), export | PASS, but R1-03 |
| Base price lists | cashier, wt.admin | yes | — | PASS |
| Branches / Roles / Users | wt.admin, manager | yes | B-01/02/03/06/10/14 actions above | PASS |
| Catalogue A (clinical groups, services, tests, service sequence, sample types, units) | cashier, tech, v11.reception, wt.admin, manager | yes | test create / partial refusal / correct, editors back / forward, read-only for viewers, exports | PASS (R1-07 corner case) |
| Catalogue B (preparations, comments, doctors, microbiology) | cashier, wt.admin | yes | exports | PASS |
| Profile | cashier, tech, head | yes | language save ar → en | PASS (laboratory block "not available": data, as before) |
| Sign-in / sign-out / branch selection | all | yes | full reload to `/login`; broadcast to a second tab (tab-5 went to `/login`); `wt.admin` / `v11.reception` choose a branch | PASS |
| Address-bar guard | head, v11.reception | yes | refused URLs → home; permitted URLs open | PASS (R1-04) |
| Arabic | manager, reception, wt.admin | yes | dashboard, registration validation, worklist, payments, patients, treasury access, branch dialog | PASS (R1-06 residue) |

## 4. New defects

| Id | Sev | Steps | Expected | Observed | Evidence |
| --- | --- | --- | --- | --- | --- |
| R1-01 | **S2** | 1. A paid visit gets a negative balance: ACC-2026-00020 paid 210, then the manager cancels GLU (90). 2. As `uat.cashier` (CanRefund, approved treasury) or `lab.admin.manager`, open Payments › ACC-2026-00020. 3. Click **Refund the balance** | The refund form opens with Refundable 90 (CP-23 → CP-24; RFD-01…05 passed on 2026-10-11) | The link (`href=/billing/refunds?visitId=01a12b33-0e75-787e-8e97-5c8edff32285`) lands on `/account/dashboard` without a message. No refund can be recorded from the client portal; the visit stays "Refund due −90.00" | Both personas; `app.refunds` = 0 for the visit. Cause: `frontend/src/app/shell/nav.registry.ts` l.118 registers `/billing/refunds` with `release: 'R2'`, and the new `navRegistryGuard` (`shell/nav.guard.ts`, `mayOpen`) refuses every URL whose entry is outside R1. The refund page is R1 when reached from Payments. It needs an R1 route entry with no nav item, or the guard must allow it |
| R1-02 | S3 | Demo laboratory, Dashboard today (manager or reception) | The **Delayed tests** table lists the tests counted by the tile (BR-13) | Tile "Delayed tests 10 · past turnaround", table "No delayed tests." Not a regression: it surfaced now that the 10 Oct visits are past their expected time | DB: 10 open results with `expected_at_utc` 14:44…15:05 local. `src/Platform.Application/Account/Queries/DashboardQueries.cs` builds `BranchDashboard(…, [])`: the delayed list is always empty |
| R1-03 | S4 | `wt.admin` → Payers & contracts → row or tier action **Contract pricing** | The action is hidden in R1 (Contract pricing CP-34 is Release 2), or it explains itself | Still offered (open point O-6). The click now lands silently on the Dashboard because of the new guard | Payers row actions `Edit payer \| Deactivate \| Contract tiers \| Contract pricing`; the URL ends at `/account/dashboard` |
| R1-04 | S4 | `uat.head` (no `dashboard.view`) types a refused URL, e.g. `/billing/payments` | A refused URL leads to a user-facing home, ideally with a short notice | A silent redirect to `/`. That page shows technical data: the tenant GUID, "Scope full", the session expiry as raw UTC ISO `2026-10-11T21:38:00.175336+00:00`, and the raw permission keys. The page predates the fix, but it is now the guard's landing page | Page text after the redirect |
| R1-05 | S4 | `uat.reception` / `v11.reception` (no export key) open Treasuries; the same users open Tests, Clinical groups, Payers, Doctors | One export policy | Treasuries offer **Export CSV** (gated on `treasury.view`; Branches, Roles and Users are gated on the view key too). Catalogue and Payers exports are hidden without the export key | `treasuries.page.ts` l.24 `*appCan="permissions.view"`; `payers.page.html` l.2 `*appCan="permissions.export"` |
| R1-06 | S4 | Various | Consistent wording, digits and formats | Cosmetic residues. Worklist search placeholder: "Accession, sample or patient no.…" does not mention the name search now supported (A-14). Arabic chart caption: "آخر 7 أيام" keeps a Latin 7 (A-12). Branch phone: the page reads "(at least 5 digits)" while the server's ORG-015 text reads "at most 20 characters". Users export: `Branches` holds the count (`2`, `1`, `All`) instead of the codes (the B-09 fix was applied to doctors only). Payments register CSV: still UTC ("Paid at (UTC)") while the registrations CSV is now local. Export toasts differ: "The CSV file was downloaded." vs "The file was exported." | Captured CSV text and toasts |
| R1-07 | S4 | Test editor: create a test whose configuration is partly refused (the page continues as the new test's editor under `/catalogue/tests/<id>`). Then reach `/catalogue/tests/new` through history (back / `popstate`) | A blank "New test" form | The page keeps showing **V12-RL** (heading, values) under the `/catalogue/tests/new` address. The router appears to still be on the `new` route: `createdId` overrides the route id, and the address was replaced to `/<id>`. Exposure is low: the editor offers no link to `/new`, and reaching that history entry normally needs the synthetic navigation used here | `test-editor.page.ts` l.133–139 (`createdId ?? routeId`) and l.535 (`replaceUrl`) |

Observations (not counted):

- The B-02 governorate list is still not filtered by the chosen country. The server now refuses a mismatch.
- The visit-detail edit (treatment no.) and the tier rename still write `audit.field_changes` but no
  `audit.audit_trail` event. This is open point O-5 of the original run.
- The scenario "a refresh that returns a different user or tenant sends the tab to sign-in" was **not tested**. It
  needs a token refresh while another tab signs in a different user, and that could not be triggered reliably
  without waiting for the 15-minute access-token expiry. The cross-tab **sign-out** broadcast was tested and works.
- Outbox: 36 events of both laboratories since 16:20, all processed, 0 dead-lettered.

## 5. Data created or changed (dev database `lis`, invented data)

Walk-through Laboratory:

- **Tests V12-K** (Numeric, V11-CHEM, mmol/L, V11-SER; bands Male 18–150 3.5–5.1 and Female 18–150 3.4–5.0;
  timing 8 h / 2 h) and **V12-RL** (Restricted list, V11-HEMA; "—" default + placeholder, Positive, Negative).
  Both are active.
- Nothing was saved for V12-SOUTH (refused three times: ORG-016, ORG-015, ORG-017) or V12-AR (cancelled).
- Toggled and restored:
  - V11 Temp role: Activate → special and result rights saved and reverted → Deactivate. Final state Inactive,
    rights as before.
  - v11.temp:
    - role V11 Technician → back to V11 Reception;
    - branches unchanged;
    - password reset to the documented value `V11Tmp#Spare-9034` with must-change;
    - locked → unlocked.
    - Final state Active, must change, V11 Reception.
  - Tier V11 Silver Plus: renamed to V12 Silver Plus and back, reactivated → deactivated. Final state Inactive.
  - V11-NORTH-CASH deactivated → reactivated.
  - V11-IMM deactivated → reactivated.

Demo laboratory:

- **PAT-2026-00008** "V12 Retest One" (Mr, Male, 40 y, e-mail `v12.retest@example.test`, comment).
- **ACC-2026-00020**:
  - K: entered 4.2 by uat.tech, verified by uat.head, Printed copy 1.
  - GLU: cancelled with "Ordered by mistake — V12 refund retest".
  - Treatment no. V12-T1.
  - Sample SMP-2026-00024.
  - Payments RCT-2026-00028 (120 cash) and RCT-2026-00029 (90 card, V12-CARD-01).
  - **The visit stays "Refund due −90.00"**: the refund could not be recorded (R1-01).
- Treasury request **TRQ-2026-00011** (NORTH-CASH, "V12 retest desk"): requested, then cancelled with reason "V12
  retest cleanup".
- `uat.cashier` language ar → en (restored).
- `uat.reception`, `wt.admin` and `lab.admin.manager` keep `en`. The topbar language switch did not change the
  stored preference.
- No tenant setting was changed: autoPrint* stay false. No other laboratory was touched.

Environment at the end: `tab-2` is signed in as `wt.admin` (MAIN) with the viewport emulation cleared. The temporary
helper kept in this tab's `sessionStorage` was removed.
