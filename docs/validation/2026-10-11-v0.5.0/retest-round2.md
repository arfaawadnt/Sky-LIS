# Re-test round 2 · 2026-10-12 · tester R2 · fixes of the re-test findings (RC-01…RC-12, RA-01, RA-02)

Scope: the fixes for the new findings of the first re-test round, a regression smoke and a database check.

- The client findings R-01…R-12 of `retest-client.md` §5 are named here **RC-01…RC-12**.
- The admin findings R-01 and R-02 of `retest-admin.md` §4 are named **RA-01** and **RA-02**.
- The A-02 observation of `retest-client.md` §2 is re-checked too.

This run tested and reported only. No application code, dictionary, test, script or migration was changed.

## Environment

- **Site:** `http://127.0.0.1:4300`, the pinned build `.local/validation/ui`.
  - `PINNED.txt`: "final-fixes 5ddba06+working-tree built 2026-10-12T00:19:15Z".
  - Bundle `main-S22KYE4G.js`, `VERSION` 0.5.3.
  - It proxies to the validation API `127.0.0.1:5102`, which runs the same final code.
- **Database:** `lis` on `127.0.0.1:5433`, read only as `lis_owner` (session `default_transaction_read_only = on`).
- **Accounts:**
  - `lab.admin` (every permission);
  - `uat.reception` (Reception, no See-VIP);
  - `v15.export` (V15 Patient Export: patients.view / export, dashboard.view; no See-VIP, **no patients.create**);
  - `v11api.admin.261011224410` (V11 API Lab 261011224410, for RC-10);
  - `platform.admin`;
  - `v15.user` (Node client only).
- **Browser:** own tabs only, `tab-16` (closed after sign-in) and `tab-17`, on host `127.0.0.1`. Other tabs (`tab-1`, `tab-2`,
  `tab-3`, `tab-10`) were not used.
- **Time:** 2026-10-12, 03:19–03:46 local (UTC+3; Cairo is also UTC+3 until 29 Oct).

Method notes:

- **Input.** The Browser pane was hidden. Clicks and input were dispatched from the page context (`element.click()`,
  `input` / `change` / `blur` events, `keydown` for Enter, Esc and F10). These run the same Angular handlers as a user.
- **Router navigation.** In the hidden pane `requestAnimationFrame` stalls, so the router's view transitions never finish:
  the URL changes but the view does not. After each full page load, the page context set
  `Document.prototype.startViewTransition = undefined`. The router then navigates without the animation; nothing else
  changes. Full page loads (`navigate`) were used where that was simpler.
- **Node client (RA-01).** A scratch client in the session scratchpad, never committed, speaks to the public auth API through
  `127.0.0.1:4300`. It seals the password as the SPA does: compact JWE, RSA-OAEP-256 + A256GCM, payload `{v, f, aud, n}`,
  audience `POST /api/v1/auth/sign-in`. It sends the headers `Origin`, `Sec-Fetch-Site: same-origin` and
  `X-Requested-With: Platform`.
- **No forced requests.** No bearer token was placed in the page. Network results come from the tab's network log.
- No print button was clicked.

## 1. Summary

| Items | FIXED | PARTLY | NOT FIXED | New findings |
| --- | --- | --- | --- | --- |
| **15** (RC-01…RC-12, A-02 observation, RA-01, RA-02) | **12** | **2** (RC-11, RC-12) | **1** (RA-02) | **7** (all S4) |

| Id | Subject | Verdict |
| --- | --- | --- |
| RC-01 | saved-registration dialog leaves stale picker results | **FIXED** |
| RC-02 | Arabic digits for the age | **FIXED** (other Latin digits: R2-02) |
| RC-03 | Arabic digits in the paginator | **FIXED** |
| RC-04 | one Arabic name for Results by accession | **FIXED** |
| RC-05 | masked VIP rows sorted at the real name's place | **FIXED** |
| RC-06 (S3) | no similar-patient warning for a VIP file without See-VIP | **FIXED** (run as uat.reception, see §2) |
| RC-07 | viewer gets the Edit drawer on master data | **FIXED** |
| RC-08 | Doctors as reception: 403 on branches | **FIXED** (same pattern on Patients: R2-04) |
| RC-09 | byte-order sorting of names | **FIXED** |
| RC-10 | "1 registrations" on the dashboard chart | **FIXED** |
| RC-11 | age anchored on the UTC date | **PARTLY** (R2-01) |
| RC-12 | user's Arabic name in the shell; Super Admin greeting | **PARTLY** (R2-03) |
| A-02 obs. | first Verify after a delta refresh refused by the server | **FIXED** |
| RA-01 | concurrent refreshes with one cookie: one refused | **FIXED** |
| RA-02 | wizard mobile messages do not follow a language switch | **NOT FIXED** |

**Regression smoke passed** (§3), with these exceptions:

- the console 401s at sign-out (R2-05);
- one 403 for `v15.export` (R2-04).

The checks behind it:

- Client menu pages as `lab.admin`: 26 in Arabic and 26 in English.
- Super Admin menu pages as `platform.admin` in English: 33.
- One registration cycle: payment, result entry, review and verify, including a delta acknowledgement.
- Outbox since 03:19:
  - demo laboratory: 27 / 27 processed;
  - platform scope: 98 / 98 processed;
  - none dead-lettered, no retry.
- Audit chain of the demo laboratory: sequences 543…569. The tenant maximum (569) equals the row count, so there is no gap.

## 2. Items re-tested

### RC-01 · saved-registration dialog leaves the picker results · FIXED

Three registrations were saved for the new patient PAT-2026-00015, as `lab.admin` in English. Each closed its "Registration
saved" dialog a different way:

- **ACC-2026-00039** (patient created inline, GLU, cash 90): closed with **×**.
- **ACC-2026-00040** (patient found through the picker search "V16 Age", NA, cash 120, saved with **F10**): closed with
  **Esc**.
- **ACC-2026-00041** (picker search again, CREA, cash 80): closed with **×**.

After each close:

- the picker box was empty and the result list had 0 entries;
- the checklist read "1 of 3 done" and the services table "No services yet";
- the picker showed no result on focus, nor 50 ms after typing "V" (within the debounce).

DB: one visit and one payment per save (RCT-2026-00041…00043, Cash, Posted), with no duplicate.

### RC-02 · Arabic digits for the age · FIXED

As `lab.admin`, in Arabic:

| Place | Shown |
| --- | --- |
| Picker (search "V15 Age") | "أنثى · ٥٢ سنة ١ يوم" |
| Registration patient card | "أنثى · ٥٢ سنة ١ يوم" |
| Patients list, Age column | "≈ ٤٧ سنة", "≈ ٥٢ سنة ١ يوم", "٣٦ سنة ٤ شهر", "٣٩ سنة ١ شهر" |
| Patient file PAT-2026-00015 | "العمر ٤٧ سنة" |
| Merge dialog (search "V15", candidate chosen; **Cancel**, nothing merged) | the list shows "٥٢ سنة ١ يوم" and "٣٦ سنة ٤ شهر"; the side-by-side view shows "٤٧ سنة" and "٥٢ سنة ١ يوم" |
| Visit editor ACC-2026-00041 | "أنثى · ٤٧ سنة" |

Every age shows Arabic-Indic digits. Other numbers on the same pages stay Latin (R2-02).

### RC-03 · Arabic digits in the paginator · FIXED

In Arabic:

| List | Paginator |
| --- | --- |
| Patients | "عرض ١–١٥ من ١٥", page sizes "٢٠ ٥٠ ١٠٠ ٢٠٠", page "١" |
| Search registrations | "عرض ١–١٣ من ١٣" |
| Tests | "عرض ١–٨ من ٨" |

In the Arabic smoke, every other paged list of the client portal also used Arabic-Indic digits. Examples: Treasuries
"عرض ١–٢ من ٢", Treasury access "عرض ١–١٦ من ١٦", Users "عرض ١–١٠ من ١٠".

### RC-04 · one Arabic name for Results by accession · FIXED

In Arabic, the same term "النتائج برقم الزيارة" appears in three places:

- the sidebar entry;
- the page title (`h1`) of `/results/by-accession`;
- the dashboard quick action.

### RC-05 · masked VIP rows in the name sort · FIXED

As `v15.export`, Patients was sorted by Name in both directions.

- Requests: `…&sort=name&direction=asc` and `…&sort=name&direction=desc`.
- **Ascending:** Ahmed Saad … V16 Age Only, then **PAT-2026-00007 "VIP patient", PAT-2026-00010 "VIP patient"**.
- **Descending:** V16 Age Only … Ahmed Saad, then **PAT-2026-00007, PAT-2026-00010**.

The masked rows come after every named row and are ordered by number, in both directions. Their real names, "V11 Walk-in
Two" and "V14 Walk-in Two", would sort among the V-names.

### RC-06 (S3) · similar-patient warning for a VIP file · FIXED

**Persona deviation.** `v15.export` holds no `patients.create`, so its Patients page has no *New patient* button and the check
cannot be started. The run used `uat.reception` instead. Its role "Reception" has `special_can_see_vip` = f and no
`patients.viewConfidential` (checked in `app.roles`).

`uat.reception`, Patients → *New patient*, against the VIP file PAT-2026-00010:

| Step | Request | Warning shown |
| --- | --- | --- |
| Mobile `01011140002` | `GET /patients/similar?mobile=01011140002` → 200 | "Similar patient files exist" · **PAT-2026-00010 · VIP patient · Female · 30 y 1 d · Same mobile** · *Use this file* |
| + name "V14 Walk-in Two" | `…?name=V14%20Walk-in%20Two&mobile=01011140002` → 200 | the same entry, reason **"Same name and mobile"** |
| Name only (mobile cleared) | `…?name=V14%20Walk-in%20Two` → 200 | the same entry, reason **"Similar name"** |

- The response body of the masked entry:
  - identity withheld: `"fullName":""`, `"mobile":null`, `"nationalId":null`, `"identityMasked":true`, `"match":"Mobile"`;
  - still returned: `title` "Mrs" and the estimated date of birth.
- **Use this file** opened `/patients/01a12c83-…` (PAT-2026-00010) and closed the dialog. No duplicate file was created.
- The opened file shows the real identity (name, mobile). This follows the stated rule in `PatientProjections.IsIdentityMasked`:
  "Opening the file … keeps the real identity" (an assumption awaiting the business owner; see the observations).

`lab.admin`, same steps:

- mobile only: "PAT-2026-00010 · **V14 Walk-in Two** · Female · 30 y 1 d · **01011140002** · Same mobile";
- name and mobile: "Same name and mobile".

The dialog was cancelled each time.

### RC-07 · read-only view for a viewer · FIXED

As `uat.reception`, the first row of each list was clicked. Each time a drawer titled **"View"** opened:

| Page | Row | Fields | Enabled fields | Buttons |
| --- | --- | --- | --- | --- |
| Treasuries | MAIN-CASH | 4 | 0 | Close only |
| Payers & contracts | Cash / Default | 6 | 0 | Close only |
| Doctors | DR01 | 8 | 0 | Close only |
| Units of measurement | g/dL | 1 | 0 | Close only |
| Clinical groups | CHEM | 7 | 0 | Close only |

- There is no Save in any of them.
- **F10** in each drawer sent no API request (resource timing before and after), showed no toast and left the drawer open.
- Esc closed it.

### RC-08 · Doctors for reception · FIXED

As `uat.reception`, Doctors loads with:

- `GET /api/v1/organization/branches/selectable` → **200** (no longer `/organization/branches` → 403);
- `GET /api/v1/catalogue/doctors?…` → 200.

The console shows no error from the page. The row reads "DR01 | Dr Ahmed Fathy | Internal medicine | — | **All branches** | 10 % |
— | Active".

### RC-09 · name sorting · FIXED

As `lab.admin`, in English:

| List | Ascending | Descending |
| --- | --- | --- |
| Tests (server sort, `sort=nameEn`) | Blood group, Creatinine, **eGFR**, Glucose (fasting), Haemoglobin, **hCG (serum)**, Potassium, Sodium | Sodium, Potassium, hCG (serum), Haemoglobin, Glucose (fasting), eGFR, Creatinine, Blood group |
| Services | Blood group, Creatinine, Glucose (fasting), Haemoglobin, **hCG (serum)**, Potassium, Renal profile, Sodium | Sodium, Renal profile, Potassium, hCG (serum), Haemoglobin, Glucose (fasting), Creatinine, Blood group |
| Worklist, Test column | Creatinine, **eGFR**, Glucose (fasting), Haemoglobin, Potassium, Sodium | the reverse |

"eGFR" sorts with E and "hCG" with H in every list. The demo catalogue has no eGFR *service*.

### RC-10 · "1 registration" on the dashboard chart · FIXED

The demo laboratory has no day with exactly one registration (12 Oct has 13). The check used V11 API Lab 261011224410 (another
lane's backend-cycle laboratory, read only):

- its one visit, ACC-2026-00001, was registered at 01:44 local on 12 Oct;
- the run signed in as `v11api.admin.261011224410` and chose branch MAIN.

| Language | Chart line for 12 Oct |
| --- | --- |
| EN | "12 Oct 2026: **1 registration**" |
| AR | "١٢‏/١٠‏/٢٠٢٦: **تسجيل واحد**" |

The other days read "0 registrations" / "٠ تسجيلات". The visit falls on the local day (12 Oct), not the UTC day (11 Oct).
The language was set back to English.

### RC-11 · age anchored on the laboratory's day · PARTLY

PAT-2026-00015 "V16 Age Only" was created by age (47 years) at **03:23 local** on 12 Oct.

- DB: `age_recorded_on` **2026-10-12** and `registered_on` **2026-10-12**. Both equal the local date.
- The screen showed the estimated date of birth 1979-10-12.

This does **not** test the fix. At 03:23 local the UTC date is also 12 Oct. The two dates differ only from 00:00 to 03:00
local, and the run could not wait for that window.

The code shows the fix:

- `RegisterPatientHandler` reads `ILaboratoryTimeZone`;
- `Patient.LocalDate` converts to that zone.

The zone comes from the country of the tenant's client record (`saas.tenants` → `saas.countries.time_zone`). The **demo
laboratory has no `saas.tenants` row**, so the zone is null and `LocalDate` falls back to the **UTC** date (R2-01). R-11 was
reported in that same laboratory, so it would still reproduce there between 00:00 and 03:00. A provisioned laboratory has a
client record:

- the Walk-through Laboratory → Egypt → `Africa/Cairo`;
- with a client record, the fix applies.

### RC-12 · the user's name in the screen language · PARTLY

**Fixed in the client portal top bar.**

| User | Arabic top bar | English top bar |
| --- | --- | --- |
| `uat.reception` | "**موظف استقبال**" / "الاستقبال" (`full_name_ar` = موظف استقبال) | "UAT Reception Clerk" / "Reception" |
| `lab.admin` | "مدير المختبر" (`full_name_ar`) | — |
| `v11api.admin.261011224410` | "V11 Manager" (its `full_name_ar` is "V11 Manager") | — |

**Super Admin home:**

| Step | Top bar | Greeting |
| --- | --- | --- |
| Signed in as `platform.admin` in English | "Platform Administrator" | "Signed in as **Platform Administrator**" |
| Switch to العربية | "مدير المنصة" | **"تم تسجيل الدخول باسم Platform Administrator"** (English name) |
| Full page load in Arabic | — | "تم تسجيل الدخول باسم **مدير المنصة**" (correct) |
| Switch back to English | — | **"Signed in as مدير المنصة"**, also after leaving the home and coming back |

So the greeting shows the name in the language of the last sign-in or page load, not the language on screen (R2-03).

### A-02 observation · Verify after a delta refresh · FIXED

Same order as the first re-test's case 2, on PAT-2026-00015 (CREA delta limit 20 %):

1. ACC-2026-00042 (CREA, the repeat-order confirmation accepted with *Register anyway*): CREA **2.5** saved and
   **reviewed**. No Δ: nothing was verified yet.
2. ACC-2026-00041: CREA **1.0** saved, reviewed, **verified**.
3. ACC-2026-00042 opened again:
   - the row already shows "**Δ 150%** · Acknowledge";
   - the **first** *Verify* opened the "Delta check" dialog: "Creatinine: 2.50 (Δ 150%, previous 1)";
   - there was no server refusal and no "1 result was refused" toast;
   - *Acknowledge and verify* verified it.

The network log has no refused verify:

- `POST /results/{id}/acknowledge-delta` → 204;
- `POST /results/verify` → 200.

DB (`app.results`):

- ACC-42 CREA: Verified 2.5, `flag` High, `delta_flag` t, `delta_percent` 150.0, `previous_result_id` → ACC-41,
  `previous_value` 1, acknowledged;
- ACC-41 CREA: Verified 1.0.

Only this order was re-run: the visit was re-opened after the older result was verified. A page left open across that
verification was not tried.

### RA-01 · two refreshes with the same cookie · FIXED

The Node client signed in as `v15.user` at 00:21:02Z. It then sent **20 rounds** of two `POST /api/v1/auth/refresh` with the
same cookie in one `Promise.all`; each round used the newest cookie.

- **40 / 40 answers 200.**
  - In every round, exactly one answer carries a new cookie and the other is the grace answer (an access token, no cookie).
  - Which request wins varies (A in 11 rounds, B in 9).
  - All 40 access tokens work on `/auth/me` (200).
  - Times 16–48 ms.
- Then one refresh with the newest cookie → 200 with a new cookie.
- **10.5 s later**, the replay of the superseded cookie → **401 `AUTH-012`**.
- After the replay:
  - the newest cookie is refused too (401 `AUTH-012`);
  - the access token is refused (401 `AUTH-006`).

DB:

- `identity.refresh_sessions`, family `01a12d7b-fc45-7632-…`:
  - 22 rows (sign-in + 21 rotations), 21 rotated;
  - **none revoked before 03:21:14.35**;
  - all revoked `ReuseDetected` at the replay.
- `audit.security_events` for v15.user:
  - `SESSION_CONTEXT_CHANGED refresh_grace` ×**20** (one per round);
  - `REFRESH_REUSE_DETECTED` ×2, only at 03:21:14.380 / .405 (the replay and the follow-up refresh), none during the 20
    rounds.

### RA-02 · wizard mobile messages follow the language · NOT FIXED

Steps on `/welcome/subscribe`:

1. Egypt, laboratory mobile `12345`, manager mobile `010123`, e-mail `not-an-email` → *Send verification code*.
2. English messages: "The laboratory mobile has 10 digits after +20 in the selected country.", "The manager's mobile has 10
   digits after +20…", "Enter a valid e-mail address.", "This field is required.".
3. Switch to **العربية**:
   - read at 150 ms and at 1.2 s, the required and e-mail messages switch ("هذا الحقل مطلوب.", "أدخل بريدًا إلكترونيًا صحيحًا.");
   - **both mobile messages stay in English** under Arabic labels (label "جوال المختبر *", `lang=ar`, `dir=rtl`).
4. Switching EN → AR → EN again changed nothing; the two messages stayed English.
5. *Send verification code* again in Arabic: still English.
6. Editing the laboratory mobile (`12346`, blur): its message became "جوال المعمل 10 أرقام بعد +20 في الدولة المختارة.".
   The manager's message stayed English.

The fix is in the build: `subscribe.page.ts` (working tree, 02:17 local, before the 03:19 build) has an `effect` on `i18n.lang()`
that calls `control.updateValueAndValidity({ emitEvent: false })` for the two mobiles. It has no visible effect.

Suspected cause: with `emitEvent: false` the controls emit no `statusChanges`, so `app-form-field` does not refresh the
error text it shows. Possibly also, the effect runs before the Arabic `public` dictionary is loaded. A user edit (which emits
events) does refresh the message.

Suggested fix: render the message from a key and its parameters in the template (as the admin tester proposed), or re-validate
with events.

## 3. Regression smoke

"Editor" means the page's *Add…* / *New…* dialog or editor route opened and closed. For pages without one, the page's own
entry was used.

| User · language | Pages | Opens and lists | Editor | Console | Verdict |
| --- | --- | --- | --- | --- | --- |
| `lab.admin` · **Arabic** (`dir=rtl`) | 26 menu entries | 26 / 26, Arabic titles, paginators in Arabic-Indic digits (e.g. المرضى ١٥, قائمة العمل ١٥, الخزائن ٢, المستخدمون ١٠) | 18 Add dialogs / routes (e.g. «ملف مريض جديد» 19 fields, «إضافة خزينة», «جهة دفع جديدة», «دور جديد» route, «خدمة جديدة», «فحص جديد», «إضافة وحدة»); Registration: inline new-patient form; Search registrations: *تعديل* → `/registration/visits/:id` «الزيارة ACC-2026-00041»; Payments: search form; Service sequence: save / discard; Profile: language and password | no error | PASS |
| `lab.admin` · **English** | 26 | 26 / 26 (Patients 15, Worklist 15, Search registrations 13, Treasury access 16, Users 10, Roles 8, Services 8, Tests 8) | the same 18 (e.g. "New patient file", "Add treasury", "New payer", "New list", "Add branch", "New role", "New user", "New service", "New test", "Add drug type") | no error | PASS |
| `platform.admin` · **English** | 33 menu entries | 33 / 33 (Clients 16, Roles 11, Admin users 10, Audit log "1–50 of 440", Governorates 27, Countries 6, Notification templates 6, Sessions 2, Platform parameters loaded) | not required | no error, no 4xx | PASS |
| `uat.reception` · EN / AR | 23 menu entries (RC-07, RC-08, RC-12) | Treasuries, Payers, Doctors, Units, Clinical groups, Patients | read-only view drawers | no error | PASS |
| `v15.export` · EN | Dashboard, Patients, Profile | Patients 15 rows, sort both ways | — | **403** `GET /catalogue/patient-conditions` (R2-04) | PASS with R2-04 |
| Public `/welcome/subscribe` | 1 | EN / AR | — | — | RA-02 not fixed |

End-to-end cycle, as `lab.admin`:

| Visit | Service | Payment | Result |
| --- | --- | --- | --- |
| ACC-2026-00039 | GLU | cash 90.00, RCT-2026-00041 | 95, Normal; saved ("1 result saved."), reviewed, verified |
| ACC-2026-00040 | NA | cash 120.00, RCT-2026-00042 | nothing entered |
| ACC-2026-00041 | CREA | cash 80.00, RCT-2026-00043 | 1.0; saved, reviewed, verified |
| ACC-2026-00042 | CREA (repeat order confirmed) | cash 80.00, RCT-2026-00044 | 2.5; Δ acknowledged, verified |

PASS.

Console:

- The only error entries during the run are the browser's "Failed to load resource" lines for the 401s around sign-out (R2-05,
  four sign-outs) and the one 403 of R2-04.
- Every sign-in landed on `/login` → `POST /auth/refresh` 401 first. That is expected: no cookie yet.

DB checks:

| Check | Result |
| --- | --- |
| Visits and payments | ACC-39…42, one payment each, Cash, Posted |
| Results | as above |
| `ops.outbox_events` since 03:19 | demo laboratory 27 / 27 processed, 0 dead, max attempts 0; null-tenant 98 / 98 |
| `audit.audit_trail` demo laboratory | 543…569, max = count = 569 (no gap) |
| `audit.field_changes` since 03:19 | the expected creates and modifies (Patient ×1, Visit ×4, Payment ×4, Result, Sample, VisitLine, TenantMembership language ×4); nothing else |

The other events in the outbox (V11 API Lab 261012001946, 83) belong to another lane's backend-cycle run.

## 4. New findings

| Id | Sev | Where | Steps | Expected | Observed | Suspected cause |
| --- | --- | --- | --- | --- | --- | --- |
| R2-01 | S4 | New patient by age, demo laboratory (RC-11 residue) | Create a patient by age between 00:00 and 03:00 local in the demo laboratory | `age_recorded_on` = the laboratory's local date | Not run in that window. The code falls back to the **UTC** date for this laboratory, so R-11 still reproduces where it was found | `LaboratoryTimeZone` returns null for a tenant without a `saas.tenants` row (the demo laboratory has none). `Patient.LocalDate(nowUtc, null)` then uses `nowUtc.UtcDateTime`. Fall back to a platform default zone (or the host zone) instead of UTC, or give the seeded laboratory a client record |
| R2-02 | S4 | Arabic digits, residues next to RC-02 / RC-03 | Open the patient file and the Patients list in Arabic | one digit set | Latin digits in: patient file "تاريخ الميلاد 1979-10-12 (تقديري من العمر)" and "تاريخ التسجيل 2026-10-12" (ISO text); the *المستندات* tab badge "0"; the Patients list column *الزيارات* ("3", "6"); the Super Admin home tiles "العملاء 16 / الاشتراكات النشطة 16" | these values are bound raw or as ISO dates. The admin tiles are formatted with `ar-EG-u-nu-latn` on purpose (`admin-home.page.ts` l.47), which differs from the client portal |
| R2-03 | S4 | Super Admin home greeting (RC-12 residue) | `platform.admin`: sign in in English, switch to Arabic; or load in Arabic and switch to English | the greeting names the user in the language on screen | It keeps the name of the last sign-in or page load: "تم تسجيل الدخول باسم Platform Administrator" / "Signed in as مدير المنصة". It is correct only after a full load in the current language. The top bar is correct | `admin-home.page.ts` l.31 `displayNameFor(this.auth.currentUser(), lang)`. `currentUser()` (`auth.store.ts` l.76) maps the profile to `{id, displayName, language, scope}` without `displayNameEn/Ar`, so `displayNameFor` always falls back to `displayName`. Pass `auth.profile()` as the top bar does |
| R2-04 | S4 | Patients, user without `catalogue.view` (RC-08 pattern) | Sign in as `v15.export`, open Patients | no failed request | `GET /api/v1/catalogue/patient-conditions?isActive=true&page=1&pageSize=200` → **403** (console "Failed to load resource … 403"). The list itself loads | the shared patient form / lookups load the patient-conditions catalogue on the list page even when the user cannot create or edit patients. Load it lazily with the form, or through a lookup open to `patients.view` |
| R2-05 | S4 | Sign out, any page | Sign out from the user menu | the full load of `/login` with no request in between | Before the `/login` load the SPA fires `GET /dashboard/branch?date=…` → **401** and `GET /organization/branches/selectable` → 401 (others aborted), seen at each of four sign-outs. The browser logs "Failed to load resource … 401" | clearing the auth state lets the shell / home route render and start its loads before `BROWSER_NAVIGATION` replaces the page. Navigate first, or gate the shell on the signing-out state |
| R2-06 | S4 | Security events | A refresh that presents the cookie of a session ended by *Sign out* (here the sign-out response was cut off by a page navigation, so the browser kept the cookie) | refused (401), logged as a refresh of an ended session | Refused `AUTH-012` as expected, but logged as **`REFRESH_REUSE_DETECTED` (severity error)**. For `uat.reception`: family `01a12d8d-2a44-77d7-…`, revoked `Logout` at 03:40:47.594; reuse events at 03:40:47.658 and 03:41:51.316 | the refresh handler treats every revoked or rotated token as theft. A token whose family was revoked with reason `Logout` (or `ReuseDetected` already) should be refused quietly or logged at info, or the monitoring will show false alarms whenever a sign-out response is lost |
| R2-07 | S4 | Results by accession, matching list | Run a query that lists a visit with one report unit and one result | "1 report unit · 1 result" | "12/10/2026, 03:35 · **1 report units · 1 results**" | the plural is not applied to the matching-list line (the "1 result saved." toast is correct) |

Observations (not counted):

- The read-only drawer of RC-07 is titled only "View" (no entity name). It keeps edit hints such as "Unique, cannot change
  later" under disabled fields.
- RC-06 privacy model:
  - the masked warning's *Use this file* opens the full, unmasked file;
  - the match reason ("Same name", "Same name and mobile") confirms the typed name;
  - the similar-check response still returns the masked file's title and estimated date of birth.

  This follows the documented assumption in `PatientProjections` (opening the file keeps the identity) and needs the business
  owner's confirmation.
- Arabic grammar of counts:
  - the dashboard chart reads "١٣ تسجيلات" / "١٦ تسجيلات" / "٠ تسجيلات" (11 and above take the singular "تسجيلًا");
  - ages read "٣٦ سنة ٤ شهر" (should be "٤ أشهر").
- Still present from the first round:
  - "1 tube(s) were issued." in the saved dialog;
  - the delta dialog shows "previous 1" (not 1.00);
  - the registration grid shows the expected time as "13/10/2026, 03:23" while other pages use "12 Oct 2026, 03:23".
- New patient: the Title stays "Mr" when Female is chosen. PAT-2026-00015 was saved as "Mr", Female, because the default was not
  changed.
- The audit actor name follows the session language. `audit.field_changes` lists the same user as "Laboratory Administrator" and
  as "مدير المختبر" for two language switches a minute apart.
- The Payers page sends `POST /api/v1/pricing/payers/default` (200) for a viewer (`uat.reception`). Nothing was written
  (no field change).
- The `CSRF_REJECTED` (origin, `/auth/refresh`) at 03:19:56 predates this run's first tab; it is not from this run.

## 5. Data created (dev database `lis`, invented data, prefix V16)

Demo laboratory:

- **Patient** PAT-2026-00015 "V16 Age Only":
  - Female, 47 years (age recorded 2026-10-12), mobile 1016161601;
  - title "Mr" (left at the default).
- **Visits:**
  - ACC-2026-00039: GLU, cash 90, RCT-2026-00041; GLU 95 verified.
  - ACC-2026-00040: NA, cash 120, RCT-2026-00042; no result.
  - ACC-2026-00041: CREA, cash 80, RCT-2026-00043; CREA 1.0 verified.
  - ACC-2026-00042: CREA (repeat order confirmed), cash 80, RCT-2026-00044; CREA 2.5, Δ 150 % acknowledged, verified.

Not saved:

- the merge dialog was cancelled;
- the New patient dialogs of RC-06 were cancelled (no duplicate of PAT-2026-00010);
- the RC-07 drawers saved nothing;
- the subscription wizard sent nothing.

Session families:

- `v15.user`: the Node family `01a12d7b-fc45-7632-…` was revoked `ReuseDetected` on purpose (RA-01 replay); the client then
  signed out.
- The browser sessions of `lab.admin`, `uat.reception` (×2), `v15.export`, `v11api.admin.261011224410` and `platform.admin`
  were signed out. One `uat.reception` sign-out response was cut off (R2-06), and its family is revoked `Logout`.
- At the end no session issued in this run is live.

Shared records:

- Languages switched for the Arabic checks, all back to `en`: `lab.admin`, `uat.reception`, `platform.admin`,
  `v11api.admin.261011224410`.
- No password, role, branch or setting was changed. No new account was created.

## 6. Not tested, and why

| Item | Reason |
| --- | --- |
| RC-11 between 00:00 and 03:00 local | the run took place at 03:19–03:46; the UTC and local dates are equal then. Checked by equality and in the code (R2-01) |
| RC-06 as `v15.export` | the role has no `patients.create`, so there is no New patient. Run as `uat.reception` (also no See-VIP) |
| RC-10 in the demo laboratory | no day with exactly one registration there; run in V11 API Lab 261011224410 (read only) |
| A-02 with the page left open while the older result is verified | only the re-opened page was tried (as in the first round) |
| Super Admin smoke in Arabic, editors on admin pages | not requested this round (only RC-12 was checked in Arabic) |
| Printing, native dialogs | print buttons were not clicked (hidden pane) |
| RA-01 in the browser | done with the Node client as asked; the browser serialises refreshes with Web Locks (C-02, first round) |
