# Re-test round 3 · 2026-10-12 · tester R3 · fixes of the round-2 findings (R2-01…R2-07, RA-02)

Scope:

- the fixes for the findings of `retest-round2.md` §4 (R2-01…R2-07) and for RA-02, still open after round 2;
- a spot check of four round-2 items (RC-02, RC-06, RC-07, RA-01);
- a regression smoke and a database check.

This run tested and reported only. No application code, dictionary, test, script or migration was changed. One platform
parameter was changed for a test and restored (§5).

## Environment

- **Site:** `http://127.0.0.1:4300`, the pinned build `.local/validation/ui`.
  - `PINNED.txt`: "round3 5ddba06+working-tree built 2026-10-12T01:20:55Z".
  - Bundle `main-MO42XD7M.js`, `VERSION` 0.5.3.
  - It proxies to the validation API `127.0.0.1:5102` (health `live` / `ready`).
- **Database:** `lis` on `127.0.0.1:5433`, read only as `lis_owner` (session `default_transaction_read_only = on`).
- **Accounts:**
  - `lab.admin` (every permission);
  - `uat.reception` (Reception, no See-VIP);
  - `v15.export` (patients.view / export, dashboard.view; no `catalogue.view`);
  - `platform.admin`;
  - `v15.user` (Node client only).
- **Browser:** one own tab, `tab-18`, on host `127.0.0.1`; closed at the end. The other tabs (`tab-1`, `tab-2`, `tab-3`,
  `tab-10`) were not used.
- **Time:** 2026-10-12, 04:21–04:39 local (01:21–01:39 UTC). Local, Cairo and UTC dates were all **12 Oct** for the whole run.

Method notes (as in round 2):

- **Input.** The Browser pane was hidden. Clicks and input were dispatched from the page context (`element.click()`,
  `input` / `change` / `blur` events, `keydown` for Enter, Esc and F10). These run the same Angular handlers as a user.
- **Router navigation.** After each full page load the page context set `Document.prototype.startViewTransition = undefined`,
  because view transitions never finish in the hidden pane.
- **Network evidence.** From the tab's network log and, per page, from `PerformanceResourceTiming.responseStatus`.
- **No forced requests.** No bearer token was placed in the page.
- **Node client.** It was used only for R2-06 and RA-01. It is a scratch file in the session scratchpad and is not
  committed. It works like the round-2 client:
  - it seals the password as the SPA does;
  - it sends `Origin`, `Sec-Fetch-Site: same-origin` and `X-Requested-With: Platform`;
  - its User-Agent is `R3-retest-node`.
- No print button was clicked.

## 1. Summary

| Items | FIXED | PARTLY | NOT FIXED | New findings |
| --- | --- | --- | --- | --- |
| **8** (RA-02, R2-01…R2-07) | **8** | **0** | **0** | **3** (all S4) |

| Id | Subject | Verdict |
| --- | --- | --- |
| RA-02 | wizard mobile messages follow the language | **FIXED** |
| R2-01 | age anchored on the laboratory's date when the laboratory has no client record | **FIXED**: local and UTC dates were equal; proved with a zone where they differ (§2) |
| R2-02 | Arabic-Indic digits in the patient file, the tab badges and the Patients "Visits" column | **FIXED** (residue in the Visits tab: R3-01) |
| R2-03 | Super Admin greeting in the language on screen | **FIXED** |
| R2-04 | no refused call on Patients for a user without `catalogue.view` | **FIXED**: the condition display could not be shown, see §2 |
| R2-05 | no 401 between *Sign out* and the `/login` load | **FIXED** |
| R2-06 | a refresh after logout is not logged as reuse | **FIXED** |
| R2-07 | "1 report unit · 1 result" | **FIXED** |

| Spot check | Verdict |
| --- | --- |
| RC-02 Arabic digits for the age | still fixed |
| RC-06 masked VIP similar-patient warning | still fixed |
| RC-07 read-only View drawer | still fixed |
| RA-01 two simultaneous refreshes, 5 rounds | still fixed (10 / 10 answers 200) |

The **regression smoke passed** (§3). It covered:

- client menu pages as `lab.admin`: 26 in English and 10 in Arabic;
- Super Admin menu pages as `platform.admin` in English: 33;
- one registration with payment, then result entry, review and verify;
- six sign-outs.

No console error came from the application. The only error lines are:

- the expected 401 of the start-up refresh on each `/login` load;
- one deliberate 422 (R2-01 validation);
- one TypeError from the tester's own script.

Database:

- outbox 148 / 148 processed, none dead-lettered;
- audit chain of the demo laboratory 570…578 with no gap;
- no session left live.

## 2. Items re-tested

### RA-02 · wizard mobile messages follow the language · FIXED

Steps on `/welcome/subscribe`:

1. Egypt, laboratory mobile `12345`, manager mobile `010123`, e-mail `not-an-email` → *Send verification code*.
2. The English messages were shown:
   - "The laboratory mobile has 10 digits after +20 in the selected country.";
   - "The manager's mobile has 10 digits after +20 in the selected country.".
3. The language was switched four times: EN → AR → EN → AR → EN. Each time the two messages were read 150 ms and 1.2 s
   after the switch.

| Switch | At 150 ms (same at 1.2 s) |
| --- | --- |
| → العربية (`lang=ar`) | "جوال المعمل 10 أرقام بعد +20 في الدولة المختارة." / "جوال المدير 10 أرقام بعد +20 في الدولة المختارة." |
| → English | both English messages again |
| → العربية | both Arabic again |
| → English | both English again |

Both messages change at once, every time, without editing a field.

Observation: the Arabic laboratory message says "جوال المعمل" while its field label says "جوال المختبر". It also keeps the
Latin "10" and "+20".

### R2-01 · age anchored on the laboratory's date · FIXED

The demo laboratory still has no `saas.tenants` row. At the time of the run the local, Cairo and UTC dates were the same
(12 Oct, 01:21–01:39 UTC), so the default case alone could not tell the fix from the old UTC fallback.

**Default case.** `lab.admin` created **PAT-2026-00016 "V17 Age Cairo"** by age (33 years) at 04:23 local / 01:23 UTC.

- DB: `age_recorded_on` 2026-10-12 and `registered_on` 2026-10-12. These equal the Cairo date, which is also the UTC date.
- The file shows "Registered on 12 Oct 2026" and "12 Oct 1993 (estimated from the age)".
- The code now resolves the zone in this order (`LaboratoryTimeZone`):
  1. the client record's country;
  2. the `timeZone` setting (the laboratory's own value, else the platform's);
  3. `Africa/Cairo`.

  It no longer falls back to UTC.

**Test with a zone whose date differs from UTC.**

1. `platform.admin`, *Platform parameters* (SA-19), at 01:32:34 UTC: Time zone set to `America/Sao_Paulo`. Local time there
   was 22:32 on **11 Oct**.
2. `lab.admin` created **PAT-2026-00017 "V17 Age Zone Probe"** by age (40) at 01:33:07 UTC.
3. DB: `age_recorded_on` **2026-10-11**, `registered_on` **2026-10-11**. The UTC date was 12 Oct.
4. The file shows "11 Oct 1986 (estimated from the age)".

So the laboratory's zone, not UTC, decides the day. The value was restored at 01:33:49 UTC (77 s later). In that window no
other patient or visit was created on the platform (DB check).

**The `timeZone` parameter.**

- **Client portal:** the laboratory's *Parameters* page (`/organization/parameters`) is a Release-2 entry. A direct load is
  refused by the navigation guard, which forwards to the dashboard, so it cannot be checked in the R1 build. The
  laboratory inherits the platform value (`TenantSettings`), and that value is what the test above changed.
- **Super Admin, *Platform parameters*:**

| Input | Result |
| --- | --- |
| page opened, no stored row | the field shows **Africa/Cairo**; hint "…Default: Africa/Cairo." |
| `not a zone` | blocked in the browser (control invalid), no request; **no message shown** (R3-03) |
| empty | blocked in the browser, no request, no message (R3-03) |
| `Mars/Olympus` | `PUT /api/v1/settings` → **422 `SET-002`** "'Mars/Olympus' is not a valid String value for 'timeZone'."; toast "One of the values is outside its allowed range." (the field is not named, R3-03) |
| `America/Sao_Paulo` | 200, "1 parameter(s) saved." |
| `Africa/Cairo` (restore) | 200, "1 parameter(s) saved." |

After the restore, `app.tenant_settings` holds a platform row `timeZone = Africa/Cairo`. Before the run there was no row; the
value was the built-in default, which is also Africa/Cairo.

### R2-02 · Arabic-Indic digits · FIXED

As `lab.admin`, in Arabic:

| Place | Shown |
| --- | --- |
| Patients, column *الزيارات* | "٠", "٤", "٣", "٦", "٢", "١" |
| Patients, column *آخر زيارة* | "١٢‏/١٠‏/٢٠٢٦، ٣:٣٥ ص" |
| Patient file PAT-2026-00015, *تاريخ الميلاد* | "١٢‏/١٠‏/١٩٧٩ (تقديري من العمر)" (round 2: "1979-10-12") |
| Patient file, *تاريخ التسجيل* | "١٢‏/١٠‏/٢٠٢٦" (round 2: "2026-10-12") |
| Patient file, *أنشأه* | "Laboratory Administrator · ١٢‏/١٠‏/٢٠٢٦، ٣:٢٣ ص" |
| Tab badge *المستندات* | "٠" (round 2: "0") |
| Patient file, *العمر* | "٤٧ سنة" |

Residue, on the same patient file: the *الزيارات* tab shows the services count "1" in Latin digits, the status as the raw
value "Open" and the branch as "Main Branch" (R3-01). Mobile and national ID numbers stay Latin, as identifiers.

The Super Admin home tiles still read "العملاء 17 / الاشتراكات النشطة 17". They use `ar-EG-u-nu-latn` on purpose
(`admin-home.page.ts`), and the item asked only for the client places.

### R2-03 · Super Admin greeting · FIXED

`platform.admin`:

| Step | Greeting |
| --- | --- |
| Signed in in English | "Signed in as **Platform Administrator**" |
| Switch to العربية (live) | "تم تسجيل الدخول باسم **مدير المنصة**" |
| Switch to English (live) | "Signed in as **Platform Administrator**" |
| Full page load in Arabic | "تم تسجيل الدخول باسم **مدير المنصة**" |
| Then switch to English (live) | "Signed in as **Platform Administrator**" |
| Leave the home (Dashboard) and come back | "Signed in as **Platform Administrator**" |

The language was set back to English.

### R2-04 · Patients without `catalogue.view` · FIXED

`v15.export` (menu: Dashboard, Patients, Profile):

| Page | API calls (all answers) |
| --- | --- |
| Patients (router) | `GET /patients?status=Active…` 200, `/patients/form-settings` 200, `/public/countries` 200, `/public/governorates` 200, **`/patients/conditions?page=1&pageSize=200` 200** |
| Patient file PAT-2026-00015 (router) | `/patients/form-settings` 200, `/patients/{id}` 200 |
| Patient file, full page load | `/auth/refresh` 200, `/account/profile` 200, `/patients/{id}` 200, form-settings, countries, governorates 200, **`/patients/conditions` 200** |

- No call to `/catalogue/patient-conditions` and no 4xx in the network log for this user.
- The list shows 17 rows.
- The file shows only the *File* tab: Documents needs `files.view` and Visits needs `registration.view`.

**The condition display could not be shown.**

- No patient in any laboratory has a condition: the join `app.patients` ⋈ `app.patient_conditions` returns 0 rows.
- The demo laboratory's condition list is empty: `/patients/conditions` answers `[]`.
- The client New-patient form has no condition field.
- The file therefore shows "Patient condition —" for every patient.
- In the code, the list and the file both resolve the name from `GET /api/v1/patients/conditions` (`patients.view`), so a
  condition would be named for this user.

### R2-05 · sign-out without 401 · FIXED

The network log was read after each sign-out:

| User | Page signed out from | Between the click and the `/login` document |
| --- | --- | --- |
| `lab.admin` | Dashboard (`/account/dashboard`) | only `POST /auth/sign-out` → 204 |
| `platform.admin` | Platform parameters (`/admin/parameters`) | only `POST /auth/sign-out` → 204 |
| `lab.admin` | Patient file (`/patients/{id}`) | only `POST /auth/sign-out` → 204 |
| `platform.admin` | Platform parameters | only `POST /auth/sign-out` → 204 |
| `v15.export` | Patient file | only `POST /auth/sign-out` → 204 |
| `uat.reception` | Dashboard | only `POST /auth/sign-out` → 204 |

- In each case no `/dashboard/branch` or `/organization/branches/selectable` request followed, and nothing failed before
  the page load.
- The one 401 per sign-out is the start-up `POST /auth/refresh` of the freshly loaded `/login` page (no cookie). It comes
  after the load and is expected, as in round 2. The browser logs it as "Failed to load resource … 401".
- DB: each sign-out wrote `SESSION_REVOKED logout`.

### R2-06 · refresh after logout · FIXED

Node client, `v15.user`:

| Step (UTC) | Answer |
| --- | --- |
| A · sign in 01:37:01.516, sign out 01:37:01.546 | 200, 204 |
| A · refresh with the signed-out cookie, 01:37:01.565 | **401 `AUTH-012`** |
| A · again 1.5 s later, 01:37:03.087 | **401 `AUTH-012`** |
| B · sign in, rotate once (01:37:03.606) | 200 / 200 with a new cookie |
| B · replay of the rotated cookie **10.5 s** later, 01:37:14.156 | **401 `AUTH-012`** |
| B · newest cookie after the replay | 401 `AUTH-012` (family revoked) |

`audit.security_events` for `v15.user` from 01:36:30:

| Time | Event | Severity | Detail |
| --- | --- | --- | --- |
| 01:37:01.554 | SESSION_REVOKED | info | logout |
| 01:37:01.568 | **SESSION_CONTEXT_CHANGED** | **info** | **refresh_after_logout** |
| 01:37:03.090 | **SESSION_CONTEXT_CHANGED** | **info** | **refresh_after_logout** |
| 01:37:14.161 | **REFRESH_REUSE_DETECTED** | error | family `01a12dc1-944e-7b69-…` |
| 01:37:14.179 | REFRESH_REUSE_DETECTED | error | same family (the newest cookie, presented after the family was revoked) |

- There is no `REFRESH_REUSE_DETECTED` for case A.
- `identity.refresh_sessions`:
  - family `01a12dc1-8c2b-70ac-…`: revoked `Logout`;
  - family `01a12dc1-944e-7b69-…`: 2 rows, both revoked `ReuseDetected` at 01:37:14.144.

Observation: in case B the first refresh with the newest cookie, presented after the family was revoked for reuse, writes a
second `REFRESH_REUSE_DETECTED` error (round 2 saw the same pair). That case was not part of R2-06, which asked only for
the `Logout` family. Monitoring will count two alarms for one theft.

### R2-07 · "1 report unit · 1 result" · FIXED

Results by accession, query by patient PAT-2026-00015 (four visits with one unit and one result each), then a query of
the latest visits (40):

| Language | One unit, one result | Other counts in the list |
| --- | --- | --- |
| EN | "12/10/2026, 03:35 · **1 report unit · 1 result**" | "1 report unit · 2 results", "2 report units · 2 results", "2 report units · 5 results", … |
| AR | "…، 3:35 ص · **وحدة تقرير واحدة · نتيجة واحدة**" | "وحدة تقرير واحدة · ٢ نتيجة", "٢ وحدة تقرير · ٣ نتيجة", … |

Residue (R3-02): in Arabic the date of each line and the heading count "الزيارات المطابقة (4)" use Latin digits. This
is deliberate in `result-format.ts` (`ar-EG-u-nu-latn`), but it differs from the Patients pages, which use Arabic-Indic
digits for the same kind of date.

Observation: the Arabic counts of 2 and more use one form ("٢ نتيجة", "٣ نتيجة"). Arabic grammar wants the dual
"نتيجتان" and the plural "٣ نتائج". This is the same class as the round-2 note on "١٣ تسجيلات".

### Spot checks of round-2 items

| Item | Check | Result |
| --- | --- | --- |
| RC-02 | `lab.admin` in Arabic | Patients age column "≈ ٣٣ سنة", "≈ ٥٢ سنة ١ يوم", "٣٦ سنة ٤ شهر"; patient file "٤٧ سنة"; registration picker (search "V16") "أنثى · ٤٧ سنة". **Still fixed** |
| RC-06 | `uat.reception`, Patients → *New patient*, mobile `01011140002` | `GET /patients/similar?mobile=01011140002` 200; "Similar patient files exist · PAT-2026-00010 · **VIP patient** · Female · 30 y 1 d · Same mobile · *Use this file*". The list shows PAT-2026-00010 and PAT-2026-00007 as "VIP patient". Dialog cancelled. **Still fixed** |
| RC-07 | `uat.reception`, first row of Treasuries, Payers & contracts, Doctors, Units of measurement, Clinical groups | each opens a drawer titled "View" with 4 / 6 / 8 / 1 / 7 fields and **0 enabled**, Close only; F10 sends no API request. **Still fixed** |
| RA-01 | Node client, `v15.user`, 5 rounds of two `POST /auth/refresh` with the same cookie in one `Promise.all` | **10 / 10 answers 200**. Each round has one new cookie and one grace answer; the winner varies (A in 2 rounds, B in 3). All 10 access tokens work on `/auth/me` (200). Times 22–83 ms. DB: `SESSION_CONTEXT_CHANGED refresh_grace` ×5; no reuse event; family `01a12dc1-c016-7387-…` 6 rows, 5 rotated, revoked `Logout` at the final sign-out. **Still fixed** |

## 3. Regression smoke

"Bad" means a response with status ≥ 400 in the page's resource timing.

| User · language | Pages | Result | Bad | Verdict |
| --- | --- | --- | --- | --- |
| `lab.admin` · **English** | 26 menu entries | 26 / 26 open with their titles; e.g. Patients 16 → 17, Worklist 16, Search registrations 14, Treasury access 16, Users 10, Roles 8, Services 8, Tests 8, Branches 2, Units 4 | none | PASS |
| `lab.admin` · **Arabic** (`dir=rtl`) | 10: Dashboard, Registration, Search registrations «عرض ١–١٤ من ١٤», Worklist «عرض ١–١٦ من ١٦», Payments, Treasuries «عرض ١–٢ من ٢», Services «عرض ١–٨ من ٨», Patients «عرض ١–١٦ من ١٦», patient file, Results by accession | all open with Arabic titles | none | PASS |
| `platform.admin` · **English** | 33 menu entries | 33 / 33. Clients 17, Roles 11, Admin users 10, Audit log "1–50 of 445", Governorates 27, Countries 6, Notification templates 6, Sessions 2; Subscriptions & wallets opens on a client wallet; Platform parameters loaded | none | PASS |
| `uat.reception` · EN | Patients, Treasuries, Payers, Doctors, Units, Clinical groups, Dashboard | as in §2 | none | PASS |
| `v15.export` · EN | Dashboard, Patients, patient file | as in §2 | none | PASS |
| Public `/welcome/subscribe` | 1 | EN / AR | — | PASS |

**End-to-end cycle**, as `lab.admin`, in English:

| Step | Evidence |
| --- | --- |
| Registration of PAT-2026-00016: Cash — Default, service GLU (typed + Enter), cash 90.00, MAIN-CASH | checklist "4 of 4 done". `POST /registration/visits` 201 and `POST /billing/payments` 201. Dialog "Registration saved · **ACC-2026-00043** · 1 tube(s) were issued. · 90.00 received — receipt **RCT-2026-00045**", closed with *New registration* |
| Results by accession, ACC-2026-00043 | "1 report unit · 1 result"; sample SMP-2026-00053 Received |
| GLU = 92 → Save | `PUT /results/values` 200, "1 result saved." |
| Review | `POST /results/review` 200, "1 result reviewed." |
| Verify | `POST /results/verify` 200, "1 result verified (electronic signature)."; row status Verified, *Amend* offered |

DB:

- the visit is Open with one payment (RCT-2026-00045, 90.00, Cash, Posted);
- `app.results` ACC-2026-00043: Verified, 92, Normal, no delta.

PASS.

**Console** (errors only, whole run):

- eight "Failed to load resource … 401": one per `/login` (and the first `/welcome/subscribe`) load, from the start-up
  refresh without a cookie. They come after the page load, not between a sign-out and the load;
- one 422: the deliberate `Mars/Olympus` save (R2-01);
- one `TypeError … reading 'click'`: from the tester's own page script (a menu item it looked for was not open yet), not
  from the application.

No other error.

**Database checks:**

| Check | Result |
| --- | --- |
| `ops.outbox_events` since 01:20 UTC | demo laboratory 9 / 9 processed; null tenant 56 / 56; V11 API Lab 261012012119 (another lane's backend-cycle run) 83 / 83. 0 dead-lettered, max attempts 0 |
| `audit.audit_trail`, demo laboratory | 1…578: count = max = 578 (no gap); this run added 570…578 |
| `audit.field_changes` since 01:20 UTC, demo laboratory | Patient created ×2, Visit, VisitLine, VisitTube, VisitAccount, Payment, Sample, SampleEvent ×3, Result created ×1 / modified ×4, ResultLogEntry ×5, TenantMembership language ×4; nothing else |
| `audit.field_changes`, platform tenant | TenantMembership language ×4 (platform.admin); TenantSetting `timeZone` created (Sao Paulo) and modified (back to Africa/Cairo) |
| `identity.refresh_sessions` issued in this run | lab.admin 2 families, platform.admin 2, uat.reception 1, v15.export 1, v15.user 3: **0 live** (revoked `Logout`, one `ReuseDetected` on purpose) |
| Languages | `lab.admin`, `platform.admin`, `uat.reception`, `v15.export`: all `en` |

Other lanes were active during the run. The following events are not from this run:

- a backend-cycle run provisioned "V11 API Lab 261012012119" at 01:21:26;
- one `CSRF_REJECTED` (origin, `/auth/refresh`) at 01:21:29;
- a `lab.admin` sign-in at 01:22:13 and a batch of four `logout` revocations at 01:22:16;
- a `platform.admin` sign-in at 01:21:22.

This run's browser refreshes all came from the page origin and answered 200 or 401, never 403.

## 4. New findings

| Id | Sev | Where | Steps | Expected | Observed | Suspected cause |
| --- | --- | --- | --- | --- | --- | --- |
| R3-01 | S4 | Patient file, *الزيارات* tab, Arabic (R2-02 residue) | `lab.admin` in Arabic, open PAT-2026-00015 → *الزيارات* | localised values, Arabic-Indic digits, Arabic branch name | services count **"1"** (Latin), status **"Open"** (raw English value), branch **"Main Branch"** although the branch has `name_ar` "الفرع الرئيسي". The date column is correct ("١٢‏/١٠‏/٢٠٢٦، ٣:٣٥ ص") | `patient-file.page.ts` `visitColumns`: `branchName`, `servicesCount` and `status` are bound raw, with no `value:` formatter and no `patients.visit.status.*` key; the API returns only the English branch name |
| R3-02 | S4 | Results by accession, matching list, Arabic (R2-07 residue) | `lab.admin` in Arabic, run a query | one digit policy across the client portal | line dates "12‏/10‏/2026، 3:35 ص" and the heading "الزيارات المطابقة (4)" / "(40)" use Latin digits. The counts in the same line ("٢ نتيجة") and the paginator ("عرض ١–٤ من ٤") use Arabic-Indic, as do the dates on Patients and the patient file | `result-format.ts` l.118 formats dates with `ar-EG-u-nu-latn` on purpose, like Billing and the Super Admin. The policy (Latin for codes and money, Arabic-Indic for dates and counts?) is not the same in every module. A business decision on one rule is needed |
| R3-03 | S4 | Super Admin, *Platform parameters* (SA-19), field *Time zone* | Enter `not a zone` or clear the field → *Save*; or enter `Mars/Olympus` → *Save* | the field is marked and names the problem | Format error or empty: Save silently does nothing. There is no request, no toast and no field message, and `aria-invalid` stays "false" although the control is invalid. Unknown zone: the server refuses with `SET-002` naming `timeZone`, but the toast only says "One of the values is outside its allowed range." and the field is not marked. The input allows 200 characters, while the server limit is 64 | the parameters page does not render the control errors of its `app-form-field`s, and does not map `SET-002` back to the key. The same silence was seen for *Trial days* = 999 (not marked on blur) |

Observations (not counted):

- RA-02: the Arabic message says "جوال المعمل" under the label "جوال المختبر"; the digits "10" / "+20" are Latin.
- R2-06: a refresh with the newest cookie of a family already revoked for reuse writes a second `REFRESH_REUSE_DETECTED`
  error. One theft gives two alarms.
- Arabic grammar of counts on Results by accession: "٢ نتيجة", "٣ نتيجة", "٢ وحدة تقرير" should use the dual and plural
  forms.
- The client *Parameters* page (`/organization/parameters`, `timeZone` and the other laboratory keys) is Release 2.
  - A laboratory without a client record can change its time zone only through the API (`PUT /api/v1/settings`), or
    inherits the platform value.
  - The Super Admin field changes the zone of every such laboratory at once.
- Still present from earlier rounds:
  - "1 tube(s) were issued." in the saved dialog;
  - the registration grid shows the expected time as "13/10/2026, 04:27" while other pages use "12 Oct 2026, 04:23";
  - the audit actor name follows the session language ("Platform Administrator" / "مدير المنصة" in `audit.field_changes`);
  - the patient file's "Created by" shows the English display name in Arabic.
- Results by accession, the result row after Verify: the value 92 is in a read-only input, so the text of the row shows no
  value. This is not a defect, only noted for testers who read rows as text.

## 5. Data created (dev database `lis`, invented data, prefix V17)

Demo laboratory:

- **Patient** PAT-2026-00016 "V17 Age Cairo": Mrs, Female, 33 years (age recorded 2026-10-12), mobile 1017171701.
- **Patient** PAT-2026-00017 "V17 Age Zone Probe": Male, 40 years.
  - `age_recorded_on` and `registered_on` are **2026-10-11**: it was created while the platform time zone was
    `America/Sao_Paulo` (R2-01 test).
  - No mobile.
- **Visit** ACC-2026-00043 (PAT-2026-00016): GLU, cash 90.00, receipt RCT-2026-00045, sample SMP-2026-00053; GLU 92 verified.

Platform:

- **Parameter `timeZone`** (platform tenant):
  - set to `America/Sao_Paulo` at 01:32:32 UTC;
  - restored to `Africa/Cairo` at 01:33:49 UTC.
  - A platform row `timeZone = Africa/Cairo` now exists where before there was none; the effective value is unchanged
    (the default is Africa/Cairo).
  - A refused attempt (`Mars/Olympus`, 422) wrote nothing.

Not saved:

- the subscription wizard sent nothing;
- the RC-06 New patient dialog was cancelled;
- the RC-07 drawers saved nothing.

Sessions:

- `v15.user`: three Node families.
  - One was signed out (case A).
  - One was revoked `ReuseDetected` on purpose (case B).
  - One was signed out after the RA-01 rounds.
- Browser sessions of `lab.admin` (×2), `platform.admin` (×2), `v15.export` and `uat.reception` were all signed out.
- No session issued in this run is live.

Shared records:

- Languages switched for the Arabic checks, all back to `en`: `lab.admin`, `platform.admin`.
- No password, role, branch or laboratory setting was changed. No account was created.

## 6. Not tested, and why

| Item | Reason |
| --- | --- |
| R2-01 between 00:00 and 03:00 Cairo time in the default zone | the run took place at 04:21–04:39 local; the Cairo and UTC dates were equal. Proved instead with a zone whose date differed (`America/Sao_Paulo`), and in the code |
| R2-01 on the laboratory's own *Parameters* page | the page is Release 2 and hidden in the R1 build; the platform field (SA-19) was used |
| R2-04 "condition still shown" | no patient in any laboratory has a condition, the demo laboratory's condition list is empty and the client form has no condition field; checked in the code only |
| Super Admin smoke in Arabic, editors on admin pages | not requested this round |
| Client editors (Add / New dialogs) in the smoke | the menu pages were opened and listed. Only New patient (×2 saved, ×1 cancelled), Registration and the read-only drawers were exercised |
| Printing, native dialogs | print buttons were not clicked (hidden pane) |
| RA-01 in the browser | done with the Node client as asked; the browser serialises refreshes with Web Locks |
