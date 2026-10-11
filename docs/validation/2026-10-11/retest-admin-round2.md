# Re-test round 2, 2026-10-11: tester R4, Super Admin portal and public pages

Scope: second re-test of the admin findings left open by `retest-admin.md`: **C-18** (partly fixed) and **R2-01…R2-05**,
plus the refusal notice and menu labels, and a regression smoke of every Super Admin menu entry and action-only page for
`platform.admin`, `uat.ops`, `uat.mdr` and `uat.sup2`, and of the public pages in both languages. SPA
`http://localhost:4300` (own tabs only) → API `127.0.0.1:5102`. Database `lis` on `127.0.0.1:5433`, read-only as
`lis_owner`. Every value was entered through the pages (browser automation, with clicks and form events issued from the
page context). No code, dictionary, test or script was changed. Time: 2026-10-11, 17:36–18:03 (UTC+3).

Method notes

- In-app navigation was driven through the Angular router (`history.pushState` + `popstate`). This is the same path as a
  menu click: no page reload, and the page's JavaScript state is kept. Typed URLs were also checked with a full page load
  (`/admin/clients` as `uat.mdr`).
- CSV exports were captured in the page: the blob's anchor `click` was intercepted and the blob read back, so no file was
  written to disk.
- R2-01 "without a reload" was checked by a helper function defined in tab A before the password change. The helper was
  still defined afterwards, so the document never reloaded.

## 1. Summary

| Fixed | Partly | Not fixed | New defects |
| --- | --- | --- | --- |
| **6** (C-18, R2-01, R2-02, R2-03, R2-04, refusal notice / labels) | **1** (R2-05) | **0** | **2** (R4-01, R4-02: S4 ×2) |

## 2. Item re-test

| Id | Expected after the fix | Steps re-run | Observed now | DB check | Verdict |
| --- | --- | --- | --- | --- | --- |
| C-18 (S3) | Client column and filter also cover field changes on the client record itself; audit CSV has a Client column | (a) `/admin/audit` as platform.admin: *Client* = V12 Retest Lab, UAT Lab B, V11 Validation Lab. (b) As **uat.ops** edit V12 Retest Lab (address → "14 V12 Street, Smouha (V13 edit)", governorate Alexandria → Cairo, *Save*), then restore both. Re-read the log as uat.sup2. (c) *Export CSV* with the V11 Validation Lab filter | (a) Rows recorded **before** the fix now carry the client: V12 "Modified Tenant Address: 12 V12 Street → 14 V12 Street" (16:40); UAT Lab B "DeactivatedAtUtc / DeactivationReason / Status" and "ActivatedAtUtc"; V11 Validation Lab "Address / GovernorateId" next to `TenantPlanChanged`, deactivation and activation. (b) Both new edits are listed as "UAT Operations Admin · V12 Retest Lab · Change Modified · Tenant · Address … ; GovernorateId …" (17:55), and the client filter keeps them (10 rows for V12). (c) CSV header `Time (UTC),User,Client,Action,Entity,Entity id,Details,Reason,Kind,Sequence,Correlation id`; 9 data rows = 9 on screen, Client = "V11 Validation Lab" on every row. *Verify chain*: "The audit chain is intact: 88 entries checked." | `audit.field_changes` (platform tenant, `entity_type='Tenant'`): 4 earlier rows plus 2 new ones at 17:55:25 / 17:55:37 by "UAT Operations Admin"; `saas.tenants` V12 restored to "14 V12 Street, Smouha", Alexandria; `audit.audit_trail` platform count 88 = max sequence 88; `AUDIT_EXPORTED rows:9` | **FIXED** |
| R2-01 (S4) | A second tab of the same session leaves `/admin/profile?mustChange=1` without a reload once the password is changed elsewhere | Created **v13.user** (must change). Tab B signs in as v13.user and lands on `/admin/profile?mustChange=1`; tab A follows that sign-in. In tab A, `/admin/dashboard` → back to `/admin/profile?mustChange=1`. Tab B: *Update password* | Tab B: "Your password was changed." Tab A, **without reloading** (its helper function survived): it moved to `/admin` by itself, the banner was gone, and `/admin/dashboard` and `/admin/catalogue/tests` then opened | `identity.platform_users` v13.user `must_change_password` t → **f** | **FIXED** |
| R2-02 (S3) | Countries and Governorates CSV contain exactly the filtered rows (search and status) | Countries: search `Egypt`; status Inactive; status All + search `+96`; Active + search `A`. Governorates: search `Cairo`; Inactive; country V11 Land / All; V11 Land / Active / `s`; All / All; Egypt / All / `a` | CSV rows = rows on screen every time: countries 1 / 0 / 3 / 5; governorates 1 / 1 (V11 Province, inactive) / 1 / 0 / 28 / 24. Dial codes are exported as `'+20`, a formula-injection guard (O-a) | — | **FIXED** |
| R2-03 (S4) | Arabic option labels in the report template dialog | Report templates in Arabic → *إضافة قالب* | Document type: اختر… / النتائج / المزرعة / تقرير نصي / المطالبة / الإيصال / الملصق / أمر الشغل. Language: الإنجليزية / العربية / الإنجليزية والعربية. Saved **V13 Report Template** from the Arabic dialog: "تم حفظ القالب." | `app.report_templates` V13 Report Template · Results · Both (deactivated at the end) | **FIXED** |
| R2-04 (S4) | Signing in from a tab whose sign-in form was already open ends the browser's previous session | Tab A and tab B on platform.admin; A signs out (B follows to `/login`); A signs in as platform.admin while B keeps its sign-in form; B signs in as `uat.mdr`. Repeated later with v13.user over platform.admin | B lands on `/admin` as uat.mdr; A reloads as uat.mdr too. *Sessions* (platform.admin, end of run): **one** row, "This session"; CSV 1 data row | `SESSION_REVOKED superseded_by_sign_in` at 17:44:36 for the platform.admin family 01a12b6c-1a61… (and again at the v13.user sign-in). Since 17:40: platform.admin 4 families, uat.mdr / uat.ops / uat.sup2 / v13.user 1 each. Only 1 non-revoked head while I worked, and **0** after my final sign-out. The revoked row's `revocation_reason` reads `Logout` while the event says `superseded_by_sign_in` (O-d) | **FIXED** |
| R2-05 (S4) | Landing feature grid and Super Admin card list Release-1 features only; Super Admin home shows only Clients and Active subscriptions, both filled | `/welcome` EN and AR; `/admin` as platform.admin (EN, AR), uat.mdr, uat.ops | **Admin home fixed**: only "Clients 12" and "Active subscriptions 12" (AR "العملاء 12", "الاشتراكات النشطة 12"); no tickets or agents tile. **Super Admin card fixed**: clients / plans / wallets, global master data, audit and live sessions; no analysers or support desk. **Feature grid**: the five cards are R1 modules and *Samples*, *Analyser integration*, *Automation* and contract tiers are gone, but two card texts still name R2 functions, and so does the hero (R4-01) | `saas.tenants`: 12 rows, all `Active` = 12 / 12 | **PARTLY** (R4-01) |
| Refusal notice / labels | A refused admin URL shows "This page is not available for your account." and lands on the admin home; menu "Home" / "Dashboard" | Typed `/admin/clients` as uat.mdr (full load); in-app URLs for each user (table §3) | Notice shown and `/admin` reached each time, including with a full load. Menu EN "Home" / "Dashboard", AR "الرئيسية" / "لوحة المتابعة". Client-portal URLs (`/registration/new`, `/patients`, `/account/profile`) from a platform session also land on `/admin`, but **without** the notice (O-b) | — | **FIXED** |

## 3. Regression smoke

### 3.1 Menu entries and action-only pages per user

Every menu entry of each user was opened in turn. "Rows" is the number of table rows on first load.

| User (rights) | Menu entries | Loads | Refused → `/admin` with the notice | Main actions exercised | Verdict |
| --- | --- | --- | --- | --- | --- |
| **platform.admin** (every key) | 33 (Home, Dashboard, Roles, Admin users, Clients, Subscriptions & wallets, Sessions, Audit log, 15 master-data, Countries, Governorates, 5 microbiology, Platform parameters, Notification templates, Profile) | 33 / 33 with their data (e.g. clients 12, governorates 27, audit 50, templates 6) | R2: `/admin/library/{analyser-models,pcr-kits,code-systems,rule-templates}`, `/admin/support`; client: `/account/*`, `/registration/new`, `/patients` (no notice, O-b) | Admin user editor: created **v13.user** "User created.", *Save* profile "Saved."; test editor: created **V13T** "The test was created." then *Deactivate* "Deactivated."; deactivated V13CG and V13 Report Template ("Deactivated."); *Sessions* list + CSV (1 row = screen); audit filters + CSV; R2-02 exports; R2-03 dialog; language switch EN ⇄ AR (left in English) | PASS |
| **uat.sup2** (Platform Administrator) | 33 | 33 / 33 | — | Role editor: **V13 Role** with 1 of 101 permissions, "Role created.", *Deactivate* → Confirm "Deactivated."; user editor (v13.user) *Lock* "User locked." / *Unlock* "User unlocked."; country **V13 Land** (time-zone picker Asia/Tashkent) "The country was saved." then *Delete country* "The country was deleted."; platform parameters: delay warning 80 → 81 → 80, each "1 parameter(s) saved."; notification template v11-notice *Save* "The template was saved."; audit *Verify chain* (88) | PASS |
| **uat.ops** (platform.view, tenancy.\*, wallet.\*, notifications.view) | 9 (Home, Dashboard, Clients, Subscriptions & wallets, Sessions, Countries, Governorates, Notification templates, Profile) | 9 / 9 | Roles, Admin users, the role and user editors by id, Audit log, Platform parameters, Tests, the test and service editors by id, Organisms, PCR kits | Client editor: edit + restore V12 Retest Lab (C-18) "The client was saved." ×2; *Open wallet* → `/admin/subscriptions?client=…`, *Top up* 1.00 V13-INV-1, "The wallet was topped up.", balance 250.55 → 251.55; plans: **V13PLAN** "The plan was saved.", *Deactivate plan* → Inactive; notification template opens without *Save* (R4-02) | PASS (R4-02) |
| **uat.mdr** (platform.view, catalogue.\*) | 24 (Home, Dashboard, Sessions, 15 master-data, 5 microbiology, Profile) | 24 / 24; *Sessions* shows only its own session | Clients (typed URL, full load), Admin users, Roles, the role and user editors by id, Audit, Subscriptions, Countries, Governorates, Parameters, Notification templates, analyser models, support, `/account/profile`, `/registration/new` | Clinical group **V13CG** created (row listed, dialog closed); test editor V11T *Save* "Nothing changed."; service editor TSH opens (General / Members) | PASS |
| **v13.user** (Master Data Reviewer, must change) | — | `/admin/profile?mustChange=1` until the change, then free (R2-01) | — | *Update password* "Your password was changed." | PASS |

### 3.2 Public pages

| Page | EN | AR | Main action | Verdict |
| --- | --- | --- | --- | --- |
| /welcome | yes | yes, rtl | Links; content per R2-05 / R4-01 | PASS (R4-01) |
| /welcome/subscribe | yes | yes, rtl | Filled **V13 Wizard Lab** (Egypt · Cairo, mobiles `1013131313` / `1013131314`) → *Send verification code* → code step "We sent a code to +201******314. The code is valid for 5 minutes." with the 60 s countdown; switched to Arabic on the code step ("أرسلنا رمزاً إلى…"); wrong code `000000` → "الرمز غير صحيح أو منتهي الصلاحية." | PASS |
| /login | yes | yes, rtl | Unknown user `v13.nobody` → "The username or password is incorrect." / "اسم المستخدم أو كلمة المرور غير صحيحة."; sign-ins of the five accounts | PASS |
| /login/recover | yes | yes (via the login page's language switch; the page has no switch of its own, O-c) | `v13.nobody` → neutral code step "If an account matches, a 6-digit code was sent…" / "إن وُجد حساب مطابق…" with countdown | PASS |
| Sign-out | — | — | Full page load to `/login`; the other tab follows | PASS |

DB for the public pages: `saas.subscription_requests` V13 Wizard Lab `Pending`, `codes_sent 1`, expires 18:38 (left to
expire); `SUBSCRIPTION_STARTED`, `SUBSCRIPTION_VERIFY_FAILED`, `SIGN_IN_FAILED unknown_account`,
`PASSWORD_RECOVERY_FAILED unknown_account` ×2. No shared account's `failed_password_attempts` changed (all 0).

## 4. New defects

| Id | Sev | Steps | Expected | Observed | Evidence |
| --- | --- | --- | --- | --- | --- |
| R4-01 | S4 | Open `/welcome` in English and Arabic | Release-1 content only (R2-05) | Release-2 functions are still marketed: the hero says Sky LIS connects "reception, sample collection, the bench, the analysers, finance…" (AR "…وسحب العينات… والأجهزة…"); the *Finance and treasuries* card ends with "and the daily cash closing" (`/billing/cash-closing` is R2 in `nav.registry.ts`); the client-portal *Catalogue and microbiology* card lists "report templates" (`/catalogue/report-templates` is R2 for the client portal) | Page text EN / AR; `frontend/public/i18n/public.*.json` keys of the hero and `feature.*` |
| R4-02 | S4 | As `uat.ops` (notifications.view only) open *Notification templates* → the row action of `otp` | A read-only view (title such as "Template", fields read-only) | Row action and dialog are titled "Edit template"; *Channel*, *Text (English)* and *Text (Arabic)* stay editable, but there is no *Save*, only *Cancel*. Nothing can be saved, but the user can type into a form that silently goes nowhere | Dialog inputs: `code` disabled, `channel`, `textEn`, `textAr` enabled; buttons: placeholders + Cancel |

Observations (no id)

- O-a: CSV exports prefix dial codes with an apostrophe (`'+20`), the usual formula-injection guard. Excel shows the
  apostrophe as text when it opens the CSV directly.
- O-b: client-portal URLs opened from a platform session (`/registration/new`, `/patients`, `/account/profile`) return
  to `/admin` silently; only admin URLs show the refusal notice.
- O-c: `/login/recover` has no language switch of its own. Arabic is reached only through `/login`, and a full load
  resets to English (no web storage, by design).
- O-d: a session ended by a new sign-in is stored with `identity.refresh_sessions.revocation_reason = Logout`, while its
  security event says `superseded_by_sign_in`.
- O-e: in the audit details of a client edit, *GovernorateId* appears as raw GUIDs; the address is readable.
- O-f (carried): the platform user editor still says "Every active branch of the laboratory". The platform role matrix
  still offers client and R2 modules (101 permissions, including analysers, QC, support). The plan dialog still asks for
  *Analysers*. The Arabic admin home greets with the English display name ("تم تسجيل الدخول باسم Platform
  Administrator"). Some audit rows show the user as "مدير المنصة": they keep the display name in force when the row was
  written.

## 5. Data created (prefix V13)

- Platform user **v13.user** (Master Data Reviewer, designation "V13 tester", password changed at first sign-in, locked and
  unlocked, active, language English). Password recorded in the git-ignored `.local/dev-credentials.md`.
- Role **V13 Role** (platform, `{platform.view}`, **inactive**, 0 members).
- Plan **V13PLAN** (13.13 EGP, 1 / 5 / 13 / 0, **inactive**).
- Global catalogue: clinical group **V13CG** (inactive), test **V13T** (CHEM, inactive), report template **V13 Report
  Template** (Results, Both, inactive).
- Country **V13 Land** created and deleted.
- Subscription request **V13 Wizard Lab** (`Pending`, never verified; expires 18:38).
- V12 Retest Lab: wallet top-up **1.00 EGP, V13-INV-1** (balance 251.55); address and governorate edited and
  **restored** ("14 V12 Street, Smouha", Alexandria).
- Platform parameter `delayWarnPercent` 80 → 81 → **80**. There is now an explicit platform row equal to the default.
- Restored or unchanged: platform.admin language **en**. `failed_password_attempts` 0 for platform.admin, uat.ops,
  uat.mdr and uat.sup2; none locked. Every session I opened was signed out: **0** active platform sessions at the end. The
  browser tabs I created were closed.
