# Re-test round 4 · 2026-10-12 · tester R4 · fixes of the round-3 findings (R3-01…R3-03) and the shared form-field change

Scope:

- the fixes for the findings of `retest-round3.md` §4 (R3-01, R3-02, R3-03);
- the shared form-field change (`app-form-field` re-binds to its control after render) on nine editors;
- a regression smoke of every client menu page (`lab.admin`, English) and every Super Admin page (`platform.admin`, English);
- a database check.

This run tested and reported only. No application code, dictionary, test, script or migration was changed. No setting was
changed: every save on an existing record wrote back the values it had (§5).

## Environment

- **Site:** `http://127.0.0.1:4300`, the pinned build `.local/validation/ui`.
  - `PINNED.txt`: "round4 5ddba06+working-tree built 2026-10-12T01:48:49Z".
  - Bundle `main-LBS336QZ.js`.
  - It proxies to the validation API `127.0.0.1:5102` (health `ready` 200).
- **Database:** `lis` on `127.0.0.1:5433`, read only as `lis_owner` (session `default_transaction_read_only = on`).
- **Accounts:** `lab.admin` (every permission) and `platform.admin`.
- **Browser:** one own tab, `tab-19`, on host `127.0.0.1`; closed at the end. The other tabs (`tab-1`, `tab-2`, `tab-3`,
  `tab-10`) were not used.
- **Time:** 2026-10-12, 04:49–05:00 local (01:49–02:00 UTC).

Method notes (as in round 3):

- **Input.** The Browser pane was hidden. Clicks and input were dispatched from the page context (`element.click()`,
  `input` / `change` / `blur` events, `keydown` for Enter and F10). These run the same Angular handlers as a user. One try
  of real keyboard typing (to show the 64-character cap) failed because the hidden pane does not draw; the cap was checked
  through the `maxlength` attribute and the validator instead (R3-03).
- **Router navigation.** After each full page load the page context set `Document.prototype.startViewTransition = undefined`.
- **Network evidence.** From the tab's network log and, per step, from `PerformanceResourceTiming.responseStatus`.
- **Field state.** Read from the DOM of each `app-form-field`: the `invalid` host class, `aria-invalid` and
  `aria-describedby` of the control, and the text of the error line (`…-error`, `role="alert"`).
- No print button was clicked. No bearer token was placed in the page.

## 1. Summary

| Items | FIXED | PARTLY | NOT FIXED | New findings |
| --- | --- | --- | --- | --- |
| **3** (R3-01…R3-03) + form-field change | **3** + **PASS** | **0** | **0** | **0** |

| Id | Subject | Verdict |
| --- | --- | --- |
| R3-01 | patient file, Visits tab in Arabic: digits, status, branch | **FIXED** |
| R3-02 | Results by accession in Arabic: heading count | **FIXED** (dates stay Latin on purpose, recorded) |
| R3-03 | Platform parameters, time zone: length, field messages, unknown zone | **FIXED** |
| Form-field change | blocked save shows field messages; valid save works | **PASS** on 9 editors (7 client, 2 Super Admin) plus Registration |

Smoke: **26 / 26** client menu pages (`lab.admin`, English) and **34 / 34** Super Admin menu entries (`platform.admin`,
English: home + 33 pages) open and list, with no response ≥ 400.

Console (errors only, whole run): two "Failed to load resource … 401" (the start-up refresh of each `/login` load, no
cookie) and one 422 (the deliberate `Mars/Olympus` save). No other error.

## 2. Items re-tested

### R3-01 · patient file, Visits tab in Arabic · FIXED

`lab.admin`, language switched to العربية (`lang=ar`, `dir=rtl`), Patients → PAT-2026-00015 → tab *الزيارات*:

| رقم الزيارة | التاريخ | الفرع | الخدمات | الحالة |
| --- | --- | --- | --- | --- |
| ACC-2026-00042 | ١٢‏/١٠‏/٢٠٢٦، ٣:٣٥ ص | الفرع الرئيسي | ١ | مفتوح |
| ACC-2026-00041 | ١٢‏/١٠‏/٢٠٢٦، ٣:٢٤ ص | الفرع الرئيسي | ١ | مفتوح |
| ACC-2026-00040 | ١٢‏/١٠‏/٢٠٢٦، ٣:٢٤ ص | الفرع الرئيسي | ١ | مفتوح |
| ACC-2026-00039 | ١٢‏/١٠‏/٢٠٢٦، ٣:٢٣ ص | الفرع الرئيسي | ١ | مفتوح |

- services count in Arabic-Indic digits ("١"; round 3: "1");
- status translated ("مفتوح"; round 3: "Open");
- branch by its Arabic name ("الفرع الرئيسي"; round 3: "Main Branch").

Limit of the evidence: all 57 visits in the database are `Open` and at branch MAIN, so only that status and that branch
could be seen.

### R3-02 · Results by accession in Arabic · FIXED

`lab.admin` in Arabic, *النتائج برقم الزيارة*:

| Query | Heading | Paginator | Line date (example) |
| --- | --- | --- | --- |
| page opened, no query | "الزيارات المطابقة (٠)" | — | — |
| patient PAT-2026-00015 | "الزيارات المطابقة (٤)" | — | "12‏/10‏/2026، 3:35 ص" |
| empty query (latest visits) | "الزيارات المطابقة (٤٢)" | "عرض ١–٤٠ من ٤٢", pages "١", "٢" | "12‏/10‏/2026، 4:55 ص" |

The heading count now uses Arabic-Indic digits (round 3: "(4)", "(40)"). The counts in each line read "وحدة تقرير واحدة ·
نتيجة واحدة".

**Recorded, not a defect:** the date of each line keeps Latin digits ("12‏/10‏/2026، 3:35 ص"). This is deliberate
(`result-format.ts`, `ar-EG-u-nu-latn`), as the brief states.

### R3-03 · Platform parameters, time zone · FIXED

`platform.admin`, *Platform parameters* (`/admin/parameters`). The field showed "Africa/Cairo" on opening.

| Check | Result |
| --- | --- |
| Length | the input carries `maxlength="64"` (`maxLength` 64; round 3: 200). A 70-character value placed by script is refused by the validator: field marked, message **"At most 64 characters."**, no request |
| `not a zone` → *Save* | no request; notice **"The record was not saved: correct the marked fields first."**; field marked (`invalid`, `aria-invalid="true"`, `aria-describedby` = the error line) with **"The value has the wrong format."** under it |
| empty → *Save* | no request; same notice; field marked with **"This field is required."** |
| `Mars/Olympus` → *Save* | `PUT /api/v1/settings` → **422**; the refusal is shown **under the Time zone field** ("One of the values is outside its allowed range.", `aria-invalid="true"`). No banner and no separate toast: the only `role="alert"` element on the page is the field's error line |
| edit the field back to `Africa/Cairo` | the server message clears at once (`aria-invalid="false"`, `aria-describedby` back to the hint) |
| *Save* with `Africa/Cairo` | "Nothing was changed." (no request: the stored value was already Africa/Cairo, since every refused attempt wrote nothing) |
| *Trial days* = 999 (round-3 note) | marked on blur with "The value must be at most 365."; value set back to 14 before leaving |

Database after the checks: `app.tenant_settings` holds one `timeZone` row, platform tenant
`00000000-0000-0000-0000-000000000001`, value **`Africa/Cairo`**, `updated_at_utc` 2026-10-12 01:33:49 UTC (the round-3
restore). This run did not change it.

Observation: the unknown-zone message is the generic range text; it does not say that the zone is unknown. It is placed on
the right field, which is what the item asked.

### Shared form-field change · PASS

For each editor: one blocked save (required field cleared or a malformed value), then the original values put back and a
valid save. "Request" is what the save sent.

| # | Editor (user) | Blocked save | Request | Valid save |
| --- | --- | --- | --- | --- |
| 1 | Branch, *Add branch* drawer (`lab.admin`) | Code, Name (English), Name (Arabic) marked "This field is required." + notice "The record was not saved: correct the marked fields first." | none | — (drawer cancelled, nothing created) |
| 1b | Branch, edit NORTH | Name (English) emptied → marked + notice | none | `PUT /organization/branches/{id}` **204**, "Branch saved.", drawer closed |
| 2 | User, *Add user* page | Username, Initial password, Confirm password, Full name EN/AR "This field is required."; E-mail `not-an-email` "Enter a valid e-mail address." | none | — (left without saving, nothing created) |
| 2b | User, edit `v15.export` | E-mail `bad@` → "Enter a valid e-mail address." | none | `PUT /organization/users/{id}` **204**, "Saved." |
| 3 | Test, edit HCG | Name (Arabic) emptied → "This field is required."; Decimals 99 → "The value must be at most 6." | none | `PUT /catalogue/tests/{id}` **204**, "Saved."; values back to "هرمون الحمل (مصل)" / 1 |
| 4 | Service, edit HGB | Short name and Turnaround (hours) emptied → both "This field is required." | none | `PUT /catalogue/services/{id}` **204**, "Saved." |
| 5 | Payer, edit AXA Insurance | Name (English) and Search key emptied → marked + notice | none | `PUT /pricing/payers/{id}` **204**, "Payer saved." |
| 6 | Treasury, edit NORTH-CASH | Name (English) emptied → marked + notice | none | `PUT /treasuries/{id}/name` **204**, "Saved." |
| 7 | Patient, *Edit* PAT-2026-00016 (Patients list) | Name (English) emptied → "This field is required."; E-mail `x@` → "Enter a valid e-mail address." | none | saved with **F10**: `PUT /patients/{id}` **204**, "Patient file PAT-2026-00016 was saved." |
| 7b | Patient, *New patient* inside Registration | *Create and use* on the empty dialog → Name (English) "This field is required." | none | `POST /patients` **201** → PAT-2026-00018 chosen on the page |
| 8 | Registration (`/registration/new`) | *Save registration* with nothing → notice "2 items need attention before saving; they are listed at the bottom of the page.", patient row and services card marked (this page uses its readiness checklist, not `app-form-field`) | none | GLU added (typed + Enter), cash 90.00, F10: `POST /registration/visits` **201**, `POST /billing/payments` **201**, "Registration saved · ACC-2026-00044 … receipt RCT-2026-00046" |
| 9 | Super Admin, Units of measurement, edit `V14u/L` (`platform.admin`) | Unit emptied → marked + notice | none | `PUT /catalogue/units/{id}` **204**, "Saved." |
| 10 | Super Admin, Countries, edit V11 Land | Name (English) emptied and Time zone select cleared → both marked + notice | none | `PUT /platform/countries/{id}` **204**, "The country was saved."; row unchanged (XV, +999, Africa/Cairo) |

In every blocked case the marked control had `aria-invalid="true"` and the error line under it; no field showed a stale
message after a valid save. No console error came from these steps.

Observation (consistency, not counted): the drawer editors (branch, payer, treasury, unit, country) show the notice
"The record was not saved: correct the marked fields first." on a blocked save; the full-page editors (user, test,
service) and the patient form mark the fields only, without that notice.

## 3. Smoke

"Bad" means a response with status ≥ 400 in the page's resource timing. Pages opened through the sidebar (router
navigation), 2 s each.

**Client, `lab.admin`, English — 26 / 26, bad: none.**

| Page | Shown |
| --- | --- |
| Dashboard | 14 rows |
| Registration | form, 1 empty row |
| Search registrations | "Showing 1–15 of 15" |
| Patients | "1–17 of 17" |
| Price query | form |
| Worklist | "1–17 of 17" |
| Results by accession | query page |
| Treasuries | "1–2 of 2" |
| Treasury access | "1–16 of 16" |
| Payers & contracts | "1–2 of 2" |
| Base price lists | "1–1 of 1" |
| Payments | accession lookup ("Find a visit") |
| Branches | "1–2 of 2" |
| Roles & privileges | "1–8 of 8" |
| Users | "1–10 of 10" |
| Preparations, Comment library, Doctors | "1–1 of 1" each |
| Microbiology | opens *Drug types*, "0–0 of 0" |
| Clinical groups | "1–3 of 3" |
| Services, Tests | "1–8 of 8" each |
| Service sequence | 8 rows |
| Sample types | "1–3 of 3" |
| Units of measurement | "1–4 of 4" |
| Profile | form |

**Super Admin, `platform.admin`, English — 34 / 34 (home + 33), bad: none.**

| Page | Shown |
| --- | --- |
| Home | "Platform overview" |
| Dashboard | 5 rows |
| Roles | "1–11 of 11" |
| Admin users | "1–10 of 10" |
| Clients | "1–17 of 17" |
| Subscriptions & wallets | opens on a client wallet (balance 0.00 EGP, monthly fee 3,500.00 EGP), its ledger empty |
| Sessions | 2 rows |
| Audit log | "1–50 of 447" |
| Preparations, Comment library, Reasons, Diagnoses, Report templates, Test rules, Shortcut sets | "1–2 of 2" each |
| Clinical groups | "1–3 of 3" |
| Services | "1–5 of 5" |
| Tests | "1–4 of 4" |
| Service sequence | 5 rows |
| Sample types, Units of measurement | "1–3 of 3" each |
| Patient conditions, Additional services | "1–2 of 2" each |
| Countries | "1–6 of 6" |
| Governorates | "1–27 of 27" |
| Drug types, Commercial names, Antibiotics, Organisms | "1–2 of 2" each |
| Sensitivity values | "0–0 of 0" |
| Platform parameters | loaded, 14 fields |
| Notification templates | "1–6 of 6" |
| Profile | form |

**Sign-outs.** `lab.admin` (from Results by accession) and `platform.admin` (from Countries): between the click and the
`/login` document only `POST /auth/sign-out` → 204; the one 401 is the start-up refresh of the new `/login` page.

**Database checks** (since 01:49 UTC):

| Check | Result |
| --- | --- |
| `ops.outbox_events` | demo laboratory 7 / 7 processed; null tenant 5 / 5; 0 dead-lettered |
| `audit.audit_trail`, demo laboratory | count = max = 585 (no gap); this run added 579…585 |
| `audit.field_changes` | demo laboratory: Patient, Visit, VisitLine, VisitTube, VisitAccount, Payment, Sample, SampleEvent ×3, Result created / modified, ResultLogEntry, TenantMembership ×2 (language). The unchanged saves of §2 wrote no field change |
| `identity.refresh_sessions` issued in this run | `lab.admin` 1, `platform.admin` 1: both revoked `Logout`, **0 live** |
| Languages | `lab.admin`, `platform.admin`: `en` |
| `app.tenant_settings` `timeZone` | platform row `Africa/Cairo`, unchanged |

## 4. New findings

None.

Observations (not counted):

- R3-03: the unknown-zone refusal reads "One of the values is outside its allowed range." It is on the right field but does
  not say the zone is unknown.
- Blocked-save notice: shown by the drawer editors, not by the full-page editors (user, test, service) or the patient form
  (§2, form-field change).
- R3-01: only the status `Open` and the branch MAIN exist on visits in this database, so other statuses and branches were
  not seen in Arabic.
- Still present from earlier rounds: "1 tube(s) were issued." in the saved dialog; the registration grid shows the expected
  time as "13/10/2026, 04:55".

## 5. Data created (dev database `lis`, invented data, prefix V18)

Demo laboratory:

- **Patient** PAT-2026-00018 "V18 Form Field Probe": Male, 45 years (age recorded 2026-10-12), no mobile; created from
  *New patient* inside Registration.
- **Visit** ACC-2026-00044 (PAT-2026-00018): GLU, Cash — Default, 90.00 paid in cash on MAIN-CASH, receipt RCT-2026-00046;
  one tube issued. Visit `Open`, result not entered.

Saved without a change (the original values were written back):

- branch NORTH, user `v15.export`, test HCG, service HGB, payer AXA Insurance, treasury NORTH-CASH, patient PAT-2026-00016
  (demo laboratory);
- unit `V14u/L` and country V11 Land (platform).

Not saved:

- the *Add branch* drawer and the *Add user* page were left without saving;
- every refused Platform-parameters attempt (`not a zone`, empty, 70 characters, `Mars/Olympus` 422) wrote nothing;
  *Trial days* was set back to 14 before leaving.

Sessions and shared records:

- browser sessions of `lab.admin` and `platform.admin` were signed out; none issued in this run is live;
- `lab.admin` was switched to Arabic for R3-01/R3-02 and back to English;
- no password, role, setting or account was changed or created.

## 6. Not tested, and why

| Item | Reason |
| --- | --- |
| Typing more than 64 characters with a real keyboard | the hidden pane does not draw, so keyboard input could not be sent; the `maxlength="64"` attribute and the validator message were checked instead |
| Other visit statuses and branches in the Arabic Visits tab | every visit in the database is `Open` at MAIN |
| Smoke in Arabic, Super Admin editors beyond units and countries | not requested this round |
| Printing | print buttons were not clicked (hidden pane) |
