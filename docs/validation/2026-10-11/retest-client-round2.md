# Re-test 2026-10-11 · Tester R3 · Client portal, round 2 (R1-01…R1-07, A-06, R2-04)

Build under test: the running second-fix build. The SPA is `http://127.0.0.1:4300`, proxying to the API on
`127.0.0.1:5102`. The database is `lis` on the private PostgreSQL 5433, read with `lis_owner` (read-only queries).

Laboratories:

- Demo laboratory (tenant `01a12494-…`): personas `lab.admin`, `uat.reception`, `uat.cashier`, `uat.tech`,
  `uat.head` and `uat.accounts`.
- Walk-through Laboratory (tenant `01a124d7-…`): `wt.admin`, for the R1-05 exports and the users export with several branches.

Tester: automated browser session. Two tabs were created for this run, `tab-8` and `tab-9`, on `127.0.0.1` only.
`tab-9` existed only for the R2-04 check and was closed afterwards. `tab-8` ended signed out and was then closed.
The pre-existing tabs (`seed`, `tab-1`, `tab-2`) were not touched.

This was a test-and-report run. No code, dictionary, test or script was changed. Every record created carries the
prefix **V13**, and every setting that was changed has been restored (§5).

Method (as in `retest-client.md`):

- Clicks and input were dispatched from the page context.
- Address-bar URLs were simulated in two ways: in-app with `pushState` + `popstate`, and once with a full load
  (`navigate`).
- Print frames were stubbed so that the rendered text was captured. CSV blobs were captured in the page; nothing was
  written to disk.
- Server answers were read from the browser's network log. Saves were checked with read-only queries.
- Automation note: in a hidden tab the router's view transition stalls ("Transition was aborted because of invalid
  state"). This was visible right after the first sign-in. For the rest of the run, `document.startViewTransition`
  was set to `undefined` in the page after each full load, so the router navigates without the animation. This
  affects automation only.

Environment note: the dev server reloaded the page once during the run (at 18:05). Frontend files were being changed at
that time by someone else: `core/auth/portal.guards.ts`, `features/platform/pages/notification-templates.page.*`, and
`public/i18n/platform.*.json` / `public.*.json`. None of these is a client page under test, but the build was not frozen.

## 1. Summary

| Verdict | Count | Items |
| --- | --- | --- |
| **FIXED** | 7 | R1-01, A-06, R1-03, R1-05, R1-06, R1-07, R2-04 |
| **PARTLY** | 2 | R1-02 (the table has no due-time column), R1-04 (the profile page still shows the laboratory id) |
| **NOT FIXED** | 0 | — |

Regression smoke: 6 personas × 21 menu pages + 10 action-only pages. Every page opened or was refused exactly as the
persona's permission keys say, and the main action exercised per persona worked (§3).

New defects found: **1 × S3, 4 × S4** (§4).

- **R3-01 (S3).** After a refund, the visit editor's summary shows "Remaining −90.00": it ignores `balance.refunded`.
  The server and Search registrations both report the visit as settled.

## 2. Item re-test

| Id | Original finding | Steps re-run | Observed now | DB check | Verdict |
| --- | --- | --- | --- | --- | --- |
| R1-01 (S2) | "Refund the balance" landed on the Dashboard; no refund could be recorded | `uat.cashier` → Payments → `ACC-2026-00020` (Refund due −90.00) → **Refund the balance** | The link (`/billing/refunds?visitId=01a12b33-…`) opens **Refunds** with the visit loaded and "Refundable: 90.00 EGP". The refund form is pre-filled with MAIN-CASH / 90 / Cash. The sidebar has no Refunds entry (action-only page). A partial refund of 50 (Card, `V13-RFD-CARD-1`, "Service cancelled", note) → toast "Refund RFD-2026-00005 recorded; print the receipt for the patient's signature." The signature receipt prints with the note. Then 40 Cash / Overpayment → RFD-2026-00006. Refunded 90.00, Remaining 0.00 | `app.refunds` for the visit: RFD-2026-00005 50.00 Card ref `V13-RFD-CARD-1` note "V13 first refund note"; RFD-2026-00006 40.00 Cash, reference and note **empty**. Trail `RefundRecorded` ×2. Search registrations shows the visit "Settled" | **FIXED** |
| A-06 (S3) | The second refund kept the first refund's note, reason, method and reference | After RFD-2026-00005 (Card, reference, reason, note), read the form, then save the second refund without typing a note | After the first save, the form is reset: Amount 40 (the remaining refundable), Method Cash, Reference empty, Reason "Choose a reason…", Note empty. The second receipt has no note and no reference | RFD-2026-00006 `note` NULL, `reference` NULL (above) | **FIXED** |
| R1-02 (S3) | The Delayed tests table was always empty while the tile counted 10 | `uat.reception` and `lab.admin` → Dashboard, today; day 10 Oct 2026; Arabic | **Today:** the tile shows "Delayed tests 10" and the table lists the same 10 rows (ACC-2026-00003 K / GLU … ACC-2026-00011 GLU / K), with Accession, Patient, Test, Delay ("2 h 55 min") and Status ("Ordered"). In Arabic the test names are Arabic (بوتاسيوم, جلوكوز صائم, صوديوم, هيموجلوبين) and the delays use Arabic digits ("٢ س ٥٧ د"). **10 Oct:** the tile shows 0 and the table "No delayed tests." (consistent; nothing was overdue by the end of that day). **Gap:** there is no **due time** column. `expectedAtUtc` is in the API answer, but `dashboard.page.ts` `delayedColumns` shows only accession, patient, test, delay and status (R3-03) | 10 open results with `expected_at_utc` < now: ACC-03 K/GLU 14:44, ACC-05 NA 14:54, ACC-06 K 14:54, ACC-08 K/GLU 14:58, ACC-09 HGB 14:58, ACC-10 GLU 14:59, ACC-11 GLU/K 15:05. This equals the table | **PARTLY** |
| R1-03 (S4) | Release-2 "Contract pricing" offered on payers / tiers; dashboard "Open audit" link | `lab.admin` and `uat.accounts` → Payers & contracts (row actions), Contract tiers dialog (AXA), Base price lists, price-list editor CASH-2026; `lab.admin` dashboard | Payer row actions: Edit payer, Deactivate, Contract tiers. Tier actions: Rename tier, Deactivate tier, Add tier. Price lists: Edit list, Deactivate, Open price grid, Copy list. Editor: Export CSV, Copy list, Apply to contracts (lab.admin only), Deactivate, Save prices. None offers **Contract pricing**. The dashboard's Recent activity has no "Open audit" link (no `/account/audit` in the page). Typing `/pricing/contract-pricing` lands on the Dashboard with the notice. Cosmetic: the tiers dialog subtitle still says "Each tier has its own contract pricing." | — | **FIXED** |
| R1-04 (S4) | A refused URL redirected silently to a technical home page | `uat.head` (no `dashboard.view`): full page load of `/billing/payments`; in-app `/`, `/account/dashboard`, R1 pages outside the role. `uat.accounts` likewise. `lab.admin`: R2 URLs (`/pricing/contract-pricing`, `/account/audit`, `/samples/collection`, `/billing/cash-closing`, `/organization/parameters`, `/integration/analysers`) | Head: sign-in lands on **Worklist**. A refused URL shows the toast "This page is not available for your account." and lands on Worklist. Accounts: sign-in and `/` land on **Search registrations**, and refused URLs land there with the notice. Users with `dashboard.view` land on the Dashboard. The old home page with the tenant GUID, raw UTC expiry and permission keys is never reached. **Residue:** Profile › Laboratory of the demo laboratory (no subscription record) still prints "Laboratory id 01a12494-7ca8-75a8-873d-645d12599db7" (`profile.page.html` l.17–18). The Walk-through Laboratory's profile shows its subscription record, not the id (R3-04) | — | **PARTLY** |
| R1-05 (S4) | Branches, Roles, Users and Treasuries exported with the view key | `lab.admin` (demo) and `wt.admin` (Walk-through): Export CSV on the four pages. `uat.reception` and `uat.cashier` (treasury.view, no treasury.export) → Treasuries | Admins: each export downloads `branches-` / `roles-` / `users-` / `treasuries-2026-10-11.csv` with the toast "The CSV file was downloaded.". Reception and cashier see only **Refresh** on Treasuries, with no Export. The buttons are gated by `*appCan="permissions.export"` (`organization.export` / `treasury.export`) | Both Laboratory Administrator roles hold `organization.export` and `treasury.export`. Reception and Cashier roles have no export key for these pages | **FIXED** |
| R1-06 (S4) | Cosmetic residues | Worklist (reception); dashboard in Arabic; Add branch with phone `09-ab` (`lab.admin`); users export (both laboratories); payments register and refunds CSV (`uat.cashier`); export toasts on 14 pages | Worklist placeholder: "Accession, sample, patient no. or name… (Ctrl+K)" (AR "رقم الطلب أو العينة أو رقم المريض أو اسمه…"). Chart caption: "التسجيلات — آخر ٧ أيام". Branch phone: 422 **ORG-015** with the server title "The phone number must have at least 5 digits and at most 20 characters: digits, spaces, '+', '-' or parentheses.", the same wording as the page's field message and toast (only the quote style differs). Users export `Branches`: `MAIN`, `MAIN; V11-NORTH`, `All`. Payments CSV header `Paid at (Asia/Riyadh)` with `2026-10-11 16:43:26` (local, = the grid); refunds CSV `Refunded at (Asia/Riyadh)` with `17:48:45`. Every export that shows a toast now says "The CSV file was downloaded." (branches, roles, users, treasuries, worklist, patients, doctors, registrations). Residue: Payments, Refunds and the price-list editor exports show **no** toast, and server-built file names are in UTC (R3-05) | — | **FIXED** |
| R1-07 (S4) | `/catalogue/tests/new` through history showed the just-created test | `lab.admin`: Tests › Add test → V13-K (Numeric, CHEM, mmol/L, SER) with timing 6 / min 10 → Save. Then `pushState('/catalogue/tests/new')` + `popstate`, `history.back()`, `history.forward()` | The save gives the partial-refusal toast, the Timing tab is marked `!`, and the address becomes `/catalogue/tests/01a12b7c-…`. The synthetic `/new` shows the heading **New test** with every field empty (code "", clinical group "Choose…", unit / sample "— None —"). Back shows V13-K under its id; forward shows the empty New test again. The timing was then corrected to 8 / 2 → "Saved.", and the test was deactivated → "Deactivated." | `app.tests` V13-K: duration 8, min 2, `is_active` false; trail `LabTestCreated`, `LabTestTimingChanged`, `LabTestDeactivated` | **FIXED** |
| R2-04 (S2, client side) | Signing in on a tab whose sign-in page was already open left the previous session alive | `tab-8` opened `/login` and was left there. `tab-9` signed in `uat.cashier` (17:39:05). Then `uat.reception` signed in on `tab-8`'s already-open login page (17:40:13) | Reception lands on the Dashboard. `tab-9` follows the change (its page reloads into the reception session; the session-channel design). The cashier identity is no longer usable in the browser | `identity.refresh_sessions`: uat.cashier family `01a12b67-3327-…` issued 17:39:05 → `is_revoked` t, `revocation_reason` **Logout**, `revoked_at` **17:40:13**, the same second as the reception session `01a12b68-3ec2-…` was issued | **FIXED** |

## 3. Regression smoke (page × persona)

Personas: **A** = lab.admin, **R** = uat.reception, **C** = uat.cashier, **T** = uat.tech, **H** = uat.head,
**Acc** = uat.accounts.

- "✓" means the page opened with its heading and data and no error banner.
- "⊘" means the guard refused the page: the toast "This page is not available for your account." and a landing on
  the persona's home (Dashboard for A / R / C / T; Worklist for H; Search registrations for Acc). Every ⊘ matches a
  missing permission key, and every ✓ matches a held key.

Each page was opened by in-app navigation, which runs the guard, the facade re-read and the dictionary load. The
action-only pages used `ACC-2026-00020`, `PAT-2026-00008`, `CASH-2026`, service and test K, role Reception, and the
uat.tech user. `/billing/refunds` was opened with `?visitId=`.

| Page | A | R | C | T | H | Acc | Main action exercised | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Client home `/` | → Dashboard | → Dashboard | → Dashboard | → Dashboard | → Worklist | → Search registrations | landing (R1-04) | PASS |
| Dashboard | ✓ | ✓ | ✓ | ✓ | ⊘ | ⊘ | day picker, Arabic, delayed table (R1-02) | PASS |
| Registration (new) | ✓ | ✓ | ⊘ | ⊘ | ⊘ | ⊘ | R: PAT-2026-00008 + K, Cash, treatment V13-T1, paid 0 → **ACC-2026-00021**, "1 tube(s) were issued." | PASS |
| Search registrations | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | C: rows show Payments / Documents only (no Edit) | PASS |
| Patients | ✓ | ✓ | ⊘ | ⊘ | ⊘ | ⊘ | list loads | PASS |
| Price query | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | page loads | PASS |
| Worklist | ✓ | ✓ | ⊘ | ✓ | ✓ | ⊘ | R: placeholder; H: Export CSV (toast) | PASS |
| Results by accession | ✓ | ✓ | ⊘ | ✓ | ✓ | ⊘ | T: K 4.1 on ACC-2026-00021 → "1 results saved."; H: Verify → "1 results verified (electronic signature)." | PASS |
| Treasuries | ✓ | ✓ | ✓ | ⊘ | ⊘ | ⊘ | A / wt.admin export; R / C no Export (R1-05) | PASS |
| Treasury access | ✓ | ✓ | ✓ | ⊘ | ⊘ | ⊘ | page loads | PASS |
| Payers & contracts | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | tiers dialog, no Contract pricing (R1-03) | PASS |
| Base price lists | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | list loads | PASS |
| Payments | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | C: ACC-2026-00021 120 Cash → "Payment recorded — receipt RCT-2026-00030." (no auto print); Register CSV | PASS |
| Branches / Roles / Users | ✓ | ⊘ | ⊘ | ⊘ | ⊘ | ⊘ | A: exports; Add branch phone ORG-015 (nothing saved) | PASS |
| Catalogue (clinical groups, services, tests, sequence, sample types, units, preparations, comments, doctors, microbiology) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | A: test create / partial refusal / correct / deactivate | PASS |
| Profile | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | page loads (R3-04 on the demo laboratory) | PASS |
| R2 URLs (contract pricing, audit, sample collection, cash closing, parameters, analysers) | ⊘ | ⊘ | ⊘ | ⊘ | ⊘ | ⊘ | refused with the notice (A checked all six; the others checked those in their smoke) | PASS |
| *Action-only:* Job order | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | loads | PASS |
| *Action-only:* Visit edit | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | C: the form is editable, Save → 403 "You do not hold the permission for this action." (R3-02) | PASS (R3-02) |
| *Action-only:* Patient file | ✓ | ✓ | ⊘ | ⊘ | ⊘ | ⊘ | loads PAT-2026-00008 | PASS |
| *Action-only:* Price-list editor | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | Acc: K 120 → 121 "Prices saved: 0 added, 1 changed." → back to 120; Export CSV | PASS |
| *Action-only:* Service / test editors | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | A: V13-K (R1-07) | PASS |
| *Action-only:* Role / user editors | ✓ | ⊘ | ⊘ | ⊘ | ⊘ | ⊘ | A: Reception role and uat.tech user load | PASS |
| *Action-only:* Results by accession `?accession=` | ✓ | ✓ | ⊘ | ✓ | ✓ | ⊘ | T / H above | PASS |
| *Action-only:* Refunds | ✓ | ✓ | ✓ | ⊘ | ⊘ | ✓ | C: RFD-2026-00005 / 00006 (R1-01, A-06); refunds CSV | PASS |
| Sign-in / sign-out / branch choice | all + wt.admin | | | | | | full reload to `/login`; wt.admin chooses MAIN | PASS |

## 4. New defects

| Id | Sev | Steps | Expected | Observed | Evidence |
| --- | --- | --- | --- | --- | --- |
| R3-01 | **S3** | 1. A visit is refunded in full, e.g. ACC-2026-00020: paid 210, GLU cancelled, refunds RFD-2026-00005 + 00006 = 90. 2. Open it in the visit editor: Search registrations › Edit, or `/registration/visits/<id>` | The summary shows the visit settled: Paid 210, Refunded 90, Remaining 0.00, as the server, Payments and Search registrations ("Settled") do | The summary reads "Paid 210.00 · Remaining **-90.00**", as if the refund were still due. A clerk could refund twice or misreport the balance | API `GET /api/v1/registration/visits/01a12b33-…` returns `"balance":{"paid":210.00,"refunded":90.00,"remaining":0.00,"refundable":0}`. `frontend/src/app/features/registration/state/registration.facade.ts` l.173–174 computes `remaining: round2(due - paid)` from `balance.paid` and ignores `balance.refunded` / `balance.remaining` |
| R3-02 | S4 | `uat.cashier` or `uat.accounts` (registration.view, no registration.edit / create) open `/registration/visits/<id>` (address bar, or a link that reaches the editor) | The editor is read-only for a viewer, as the catalogue editors are since B-12: no Save, no service entry, no New registration | 10 of 12 inputs are enabled, and "New registration", "Browse (F2)" and "Save changes" are offered. Changing the treatment no. and saving → 403, toast "You do not hold the permission for this action."; nothing is saved (treatment no. stays V12-T1). Exposure is low: the Search registrations row offers no Edit to these users | Network `PUT …/visits/01a12b33-…/details → 403`. The guard allows the URL because its longest registry entry is `/registration/visits` (`registration.view`) |
| R3-03 | S4 | Dashboard › Delayed tests (any day with delays) | The columns promised for R1-02: accession, patient, test, **due time**, delay | There is no due-time column. The table shows Accession, Patient, Test, Delay and Status only | `features/account/pages/dashboard.page.ts` l.75–81 (`delayedColumns`); `DelayedTestItem.expectedAtUtc` is delivered but not shown |
| R3-04 | S4 | Demo laboratory (any persona) → Profile | No tenant id appears in the client portal (R1-04) | When the laboratory has no subscription record, the Laboratory card shows "The subscription record of this laboratory is not available." and then "Laboratory id 01a12494-7ca8-75a8-873d-645d12599db7". Only seeded laboratories are affected: a laboratory provisioned through Add client shows its record | `features/account/pages/profile.page.html` l.14–19 (`account.profile.tenantId`, `lab.tenantId`) |
| R3-05 | S4 | Export CSV on Payments (Register), Refunds, the CASH-2026 price-list editor and Patients, at 17:49–18:07 local | One export behaviour across pages (R1-06): the toast "The CSV file was downloaded." and a local-date file name such as `<page>-2026-10-11.csv` | **Payments, Refunds and the price-list editor** show no toast at all (`payments.page.ts` / `refunds.page.ts` call `saveFile` without a success toast). Server-built files carry a **UTC** timestamp in another pattern: `payments-20261011-1449.csv`, `refunds-20261011-1449.csv`, `patients-20261011-1507.csv` were all created at 17:49 / 18:07 local. Client-built files use the local date: `users-2026-10-11.csv`, `worklist-2026-10-11-2026-10-11.csv`, `cash-2026-prices-2026-10-11.csv`. Minor: the users export writes `All` and `MAIN; V11-NORTH`, while the doctors export writes `All branches` and `V11-NORTH \| MAIN` | Captured blob names and toasts. `Platform.Application/Billing/BillingSupport.cs` l.174 and `Patients/Queries/PatientQueries.cs` l.226 name the file with `nowUtc … "yyyyMMdd-HHmm"` |

Observations (not counted):

- Payments and Refunds show "Device: a0ab790c-b616-479e-8a45-dcf8a8dcc3f0", and both CSVs carry a `Device` column with
  the raw id. This is presumably intentional (treasury approval is per device), but it is the only raw GUID left on a
  client page apart from R3-04.
- The CSV zone is the browser's (`Asia/Riyadh` on this host). An Egyptian laboratory would see `Africa/Cairo` only
  if the browser runs in that zone. Both are UTC+3 today.
- The tiers dialog subtitle "Each tier has its own contract pricing." still refers to the Release-2 page (R1-03 residue,
  wording only).
- The refund page opens for `uat.reception` (billing.view, no CanRefund special right). Recording a refund as
  reception was not attempted; the server enforces CanRefund.
- The topbar language switch saves the preference (CP-65). `uat.reception` went to `ar` during the Arabic check and
  was switched back to `en`.
- Outbox and audit: the demo trail for the run shows `VisitRegistered`, `SampleCollected`, `SampleReceived`,
  `PaymentRecorded`, `ResultEntered`, `ResultVerified`, `RefundRecorded` ×2, `BasePriceListPricesChanged` ×2,
  `LabTestCreated`, `LabTestTimingChanged` and `LabTestDeactivated`. One `RoleChanged` in the same window was not
  made by this run.

## 5. Data created or changed (dev database `lis`, invented data)

Demo laboratory:

- **ACC-2026-00021**:
  - Registered by uat.reception: PAT-2026-00008 "V12 Retest One", K, Cash / Default, treatment no. **V13-T1**,
    tube SMP-2026-00025, saved unpaid.
  - **RCT-2026-00030** 120.00 Cash (uat.cashier, MAIN-CASH).
  - K 4.1 entered by uat.tech and verified by uat.head.
- **ACC-2026-00020**: refunds **RFD-2026-00005** (50.00 Card, `V13-RFD-CARD-1`, Service cancelled, note "V13 first
  refund note") and **RFD-2026-00006** (40.00 Cash, Overpayment). The visit is now settled (refund due 0). Both
  signature receipts went to the stubbed print frame.
- A cashier attempt to change the treatment no. of ACC-2026-00020 was refused (403); the value stays V12-T1.
- **Test V13-K**: Numeric, CHEM, mmol/L, SER, timing 8 h / 2 h, no bands. Created, corrected, then **deactivated**.
- CASH-2026 price of K: 120 → 121 → **120** (restored).
- Branch V13-PH: refused (ORG-015). Nothing saved.
- `uat.reception` language: en → ar → **en** (restored).
- Sessions: the uat.cashier session `01a12b67-3327-…` was revoked by the R2-04 check (expected). Every persona was
  signed out at the end.

Walk-through Laboratory: exports only. Nothing was saved.

No tenant setting was changed. No other laboratory was touched. Tabs `tab-8` / `tab-9` are closed and the viewport
emulation is cleared.
