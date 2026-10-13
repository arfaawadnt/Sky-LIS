# Final browser round · 2026-10-12 · tester F-ADMIN · Super Admin portal and public pages (LIS-VR-002)

Scope:

- the open points of LIS-VR-002 on the Super Admin portal and the public pages (report §6, §7 and §10, README "Unreleased");
- every admin and public case that earlier rounds skipped or were blocked from (`super-admin-and-public.md` §8,
  `retest-admin.md` §6).

This run tested and reported only. No application code, dictionary, test, script or migration was changed. Data was changed
only through the application pages, where a step allowed it; every shared setting that was changed was restored (§6).

## Environment

- **Site:** `http://localhost:4300`, the pinned build `.local/validation/ui`.
  - `PINNED.txt`: "open-points df3bdac+working-tree built 2026-10-12T12:33:08Z"; bundle `main-Y7NOW2RG.js`.
  - It proxies to the validation API `127.0.0.1:5102` (health `ready`).
- **Notifications:** pickup folder `E:\LIS\.local\mail-pickup\validation`, one file per message (channel, recipient, template,
  language, subject, body). Codes were read from the files; no code is written here.
- **Database:** `lis` on `127.0.0.1:5433`, read only as `lis_owner` (session `default_transaction_read_only = on`).
- **Accounts:** `platform.admin`, `uat.sup2`, `uat.mdr`, plus the V19 accounts created in this run (§7). Passwords are in the
  git-ignored `.local/dev-credentials.md` only.
- **Browser:** one own tab (`tab-4`) on host `localhost`, closed at the end after sign-out. The other tabs (`seed`, `tab-1`,
  `tab-2`, `tab-3`) were not used.
- **Time:** 2026-10-12, 15:36–16:12 local (12:36–13:12 UTC).

Method notes (as in rounds 3 and 4):

- **Input.** The Browser pane was hidden. Clicks and input were dispatched from the page context (`element.click()`,
  `input` / `change` / `blur` events, `keydown` for F10 and Ctrl+S). These run the same Angular handlers as a user.
- **Router navigation.** After each full page load the page context set `Document.prototype.startViewTransition = undefined`.
- **Network evidence.** From the tab's network log, response bodies where a code was needed, and
  `PerformanceResourceTiming.responseStatus` per step.
- **Node client.** Only for the forced requests (§2, C-9). A scratch file in the session scratchpad, never committed. It seals
  the password as the SPA does and sends `Origin`, `Sec-Fetch-Site: same-origin` and `X-Requested-With: Platform`; its
  User-Agent is `lis-retest-final-admin-v19/1.0`. No bearer token was placed in the page.
- No print button was clicked.

## 1. Summary

| Items | PASS | PARTLY | FAIL | Not testable | Does not exist | New findings |
| --- | --- | --- | --- | --- | --- | --- |
| **20** | **16** | **1** | **1** | **1** | **1** | **4** (1 × S3, 3 × S4) |

| Id | Subject | Verdict |
| --- | --- | --- |
| A-1 | Subscription wizard to completion (V19 laboratory) | **PASS** |
| A-2 | Password recovery to completion: platform user and laboratory user | **PASS** |
| A-3 | TPL-07, inactive template falls back to the built-in text | **PASS** |
| B-1 | Platform parameters: unknown time zone, `SET-003`, own message | **PASS** |
| B-2 | Countries: dial code checked as typed | **PASS** |
| B-3 | Role editor and user overrides: Release-2 modules hidden, held keys kept | **PASS** |
| B-4 | Add admin user: duplicates under their fields, blocked-save notice | **PASS** |
| B-5 | Clinical groups on SA-12 keep the Report template field | **PASS** |
| B-6 | V14L "Red" Normal and Critical: `CAT-061`, then Critical only | **PASS** |
| C-1 | Super Admin pages and dialogs in Arabic, field messages | **PASS** (finding F-03 on the notice, both languages) |
| C-2 | Report templates: logo and signature upload, preview, Arabic option labels (R2-03) | **PARTLY**: R2-03 passes; upload and preview do not exist (F-02) |
| C-3 | Platform catalogue test types: Formula (`@AGE`/`@FEMALE`), Formatted text, Culture, Non-numeric, Timing | **PASS** |
| C-4 | Services: Profile, Package, Culture, Text report | **PASS** |
| C-5 | `uat.sup2` in the browser: menu and refused URLs | **PASS** |
| C-6 | Session timeout and screen lock (platform parameter exists) | **FAIL** (F-01) |
| C-7 | Sessions list: "Since" sorts by time across days | **Not testable**: one session listed |
| C-8 | Wallet subscription and usage debits; is there a billing run? | **Does not exist in Release 1**: recorded, nothing run |
| C-9 | Forced requests: missing key, laboratory token on platform API, revoked cookie on refresh | **PASS** |
| C-10 | Keyboard: F10 saves on admin pages | **PASS** |
| C-11 | Smoke: every Super Admin page in English and Arabic, public pages, console | **PASS** |

Not passing: C-6 (no screen lock or idle timeout exists, S3), C-2 (logo/signature picker and preview not built, known intake
assumption A10). New findings F-01…F-04 in §4.

## 2. Items

### A-1 · Subscription wizard to completion · PASS

`/welcome/subscribe`, English, laboratory **V19 Wizard Lab** (Arabic name معمل في19 للتجربة), Egypt / Cairo, laboratory
mobile typed `01019190002`, manager mobile typed `01019190001` (with the trunk 0), licence `V19-LIC-001`, e-mail
`v19.wizard@example.test`.

| Step | Screen | Pickup file / database |
| --- | --- | --- |
| Send verification code | step 2: "We sent a code to +201******001. The code is valid for 5 minutes." | `…T123715…-sms-….txt`: `To: +201019190001`, `Template: otp`, `Language: en`, "Your Sky LIS verification code is ••••••. It is valid for 5 minutes. Never share it." |
| Verify with the code from the file | step 3 "Create the administrator account" | `verified_at_utc` 12:37:32Z |
| Username `v19wiz.admin`, password + confirmation, Create account | step 4 "Your laboratory is ready … https://v19-wizard-lab.skylis.app · Username: v19wiz.admin" | `…T123741…-sms-….txt`: `Template: portal-url`, "V19 Wizard Lab: your Sky LIS portal is ready at https://v19-wizard-lab.skylis.app. Sign in with the username v19wiz.admin." |
| Sign in (button on step 4 → `/login?username=v19wiz.admin`) | `/account/dashboard`, "MAIN · V19 Wizard Lab · 12 Oct 2026", menu of the Laboratory Administrator | `SIGN_IN_SUCCEEDED` |

Database:

- `saas.subscription_requests` `01a1301e-0101-7213-9d27-3e5df8220c86`: status **Completed**, `codes_sent` 1, `mobile`
  `+201019190002`, `manager_mobile` `+201019190001`, language `en`, started 12:37:15Z, verified 12:37:32Z, completed 12:37:41Z,
  `tenant_id` `01a1301e-642d-702b-b9a0-1f3307d27444`.
- `saas.tenants` (same id): `name_en` V19 Wizard Lab, slug `v19-wizard-lab`, source **Subscription**, status **Trial**,
  `trial_ends_at_utc` 2026-10-26 (14 trial days), administrator `v19wiz.admin`, preferred language `en`.
- `saas.wallets`: balance 0.00 EGP, `next_charge_at_utc` 2026-10-26 (= trial end).
- `app.branches`: MAIN "V19 Wizard Lab", active. `app.roles`: Laboratory Administrator, 95 keys.
- `app.tenant_memberships` + `identity.platform_users`: `v19wiz.admin`, mobile `+201019190001` (E.164 from the local spelling),
  active, primary administrator, `must_change_password` false.
- `ops.outbox_events` of the new tenant from 12:36Z: 43, all processed, none dead-lettered, 0 retries (provisioning copy of the
  global catalogue, BR-25).

### A-2 · Password recovery to completion · PASS

| | Laboratory user `v19wiz.admin` | Platform user `v19.user` (created in B-4) |
| --- | --- | --- |
| Identifier typed on *Forgot your password?* | `v19wiz.admin` | `01019191901` (the mobile in local spelling; stored `+201019191901`) |
| Screen after *Send code* | "If an account matches, a 6-digit code was sent to its registered mobile. It is valid for 5 minutes." | same |
| Pickup file | 12:39:26Z, `To: +201019190001`, `Template: password-recovery`, `en` | 12:42:12Z, `To: +201019191901`, `Template: password-recovery`, `en` |
| Code from the file → *Next* → new password + confirmation → *Set new password* | `/login?username=…`, "Your password was changed. Sign in with the new password." | same |
| Old password | "The username or password is incorrect." | same |
| New password | `/account/dashboard` | `/admin`, "Signed in as V19 Final Tester", menu of V15 Role (Home, Dashboard, Roles, Admin users, Sessions, Profile) |

Recovery finds the platform user by a local spelling of the mobile (C-03 holds).

### A-3 · TPL-07, inactive template fallback · PASS

SA-20 *Notification templates*, `platform.admin`. The active `password-recovery` template ends "…if you did not **ask for it**."; the
built-in text (`DefaultNotificationTemplateSource`) ends "…if you did not **request it**." and carries the subject "Password
recovery".

| Step | Evidence |
| --- | --- |
| Template active (A-2) | file 12:42:12Z: `Subject:` empty, "…Ignore this message if you did not ask for it." |
| *Deactivate* on the row → dialog "Deactivate the template password-recovery? The built-in text is sent instead." → Confirm | row leaves the Active filter, shows *Inactive* with *Activate* |
| Recovery start for `v19.user` | file 12:43:34Z: `Subject: Password recovery`, "…Ignore this message if you did not **request it**." (built-in) |
| *Activate* → dialog "Activate the template password-recovery?" → Confirm | `saas.notification_templates.is_active` = true, modified 12:43:59Z by Platform Administrator |
| Recovery start for `v19.user` | file 12:44:18Z: `Subject:` empty, "…if you did not **ask for it**." (template again) |

The two codes of the fallback check were not used; they expire after 5 minutes.

### B-1 · Platform parameters, unknown time zone · PASS

`/admin/parameters`, time zone `Mars/Olympus`, *Save*:

- `PUT /api/v1/settings` → **422**, body `code` **`SET-003`** ("'Mars/Olympus' is not a time zone this host knows for 'timeZone'…");
- under the Time zone field: "**This time zone is not known.**" (`aria-invalid="true"`, field marked);
- Arabic (C-1): "هذه المنطقة الزمنية غير معروفة." under "المنطقة الزمنية *".

The field was set back to `Africa/Cairo`; `app.tenant_settings` `timeZone` of the platform tenant is unchanged
(`Africa/Cairo`, updated 2026-10-12 04:33:49 local, before this run).

### B-2 · Countries, dial code checked as typed · PASS

*Add country*, Dial code field, `input` events only (no blur):

| Typed | Message under the field |
| --- | --- |
| `20` | "The dial code is '+' followed by one to four digits, like +20." at once, field marked |
| `+2a` | same |
| `+12345` | same |
| `+20` | none |

*Save* with `20` sent no request; the dialog showed "The record was not saved: correct the marked fields first." Cancelled.
In Arabic, editing V11 Land (`+999`) and typing `999`: "مفتاح الاتصال '+' يليه من رقم إلى أربعة أرقام، مثل ‎+20." Restored
without saving.

### B-3 · Role editor and user overrides · PASS

**Role editor** (`/admin/organization/roles`, *Operations Admin*, which holds `wallet.view/create/manage` of a Release-2 module):

- the *Screen rights* tab badge shows **6** (9 keys held − 3 wallet keys);
- the matrix lists 18 modules; none of Claims, Integration, Automation, Quality, Support or Wallet;
- note: "This role also holds 3 permission(s) of modules of a later release; they are not shown and are kept when you save."
- tick *Audit trail and security events: View* → *Save* → "This role has 1 member(s)…" → Confirm → `PUT …/permissions` 204,
  "Screen rights saved."; `app.roles.permission_keys` = the 9 keys **plus** `audit.view` (wallet keys kept);
- untick → Save → Confirm → 204; keys back to the original 9 (`notifications.view, platform.view, tenancy.create/edit/manage/view,
  wallet.create/manage/view`).

**User overrides.** Platform users have none: SA-07 has the tabs Profile, Role & branches and Security only (the SRS says
"privileges inherited from the role only"). The override matrix was checked in the client portal, as `v19wiz.admin`:

- new user `v19.labuser`, role Laboratory Administrator (95 keys, 25 of them in Release-2 modules);
- *Privileges* tab: 16 modules, "70 of 70 permissions granted", no Release-2 module;
- untick *Dashboards and KPIs: View* → *Save privileges* → `PUT …/overrides` 204, "Privileges saved for this user.", header
  "Modified · − dashboard.view · 69 of 70";
- `app.tenant_memberships`: `removed_permission_keys` = `{dashboard.view}`, `added_permission_keys` = `{}`: the 25 hidden role
  keys were not turned into removals.

Limit: no user holds an override on a Release-2 module, and the editor cannot create one, so the "kept overrides" note of the
user editor could not be shown.

### B-4 · Add admin user · PASS

*Admin users › Add user*, each duplicate sent to the server one at a time (`POST /api/v1/organization/users` → 409):

| Duplicate | Message under the field |
| --- | --- |
| Username `v15.user` | Username: "This username is taken." |
| E-mail `v15.user@example.test` | E-mail: "Another account uses this e-mail address." |
| Mobile typed `01015151501` (stored `+201015151501`) | Mobile: "Another account uses this mobile number." |
| Employee code `V15.USER` | Employee code: "This employee code is taken." |

Blocked save (Full name in English emptied): no request, notice "**The record was not saved: correct the marked fields
first.**", "This field is required." under the field, focus on it. Then created **`v19.user`**
(`01a13022-2600-760d-8c6f-80aab308e2bb`, V15 Role, mobile `+201019191901`, must-change off).

### B-5 · Clinical groups on SA-12 · PASS

*Add clinical group* on `/admin/catalogue/clinical-groups`: fields Code, Unit code, Name EN/AR, Report name, **Report template**
(options Choose…, V11 Results Template, V14 Results Template), Display order. Cancelled.

### B-6 · V14L "Red" Normal and Critical · PASS (allowed change)

Platform test V14L (`01a12c89-9ab7-787b-9a78-5d02d134da49`), *List values*:

- on opening, row 2 (Red, Normal ✓ Critical ✓) shows "**A value is Normal or Critical, not both.**";
- *Save* as it was: no request; "The record was not saved: correct the marked fields first." (server code `CAT-061`,
  `LabTestCodes.ListValueNormalAndCritical`, refuses the same at the validator and in the domain);
- untick Normal: the message goes; *Save* → `PUT …/list-values` 204, "Saved.";
- `app.test_list_values` (platform tenant): Yellow normal / default / placeholder; **Red `is_normal` false, `is_critical` true**;
  "." normal / placeholder.

### C-1 · Super Admin in Arabic · PASS (see F-03)

`uat.sup2`, language switched to العربية (`lang=ar`, `dir=rtl`):

- all 33 menu pages open with Arabic titles (C-11);
- 22 add dialogs (Clients, Countries, Governorates, Notification templates, Plans, Preparations, Comment library, Reasons,
  Diagnoses, Report templates, Test rules, Shortcut sets, Clinical groups, Sample types, Units, Patient conditions, Additional
  services and the five microbiology lists) and 4 full-page editors (role, user, test, service) were opened and saved empty:
  every label and message is Arabic ("هذا الحقل مطلوب."), no request left the page, the fields were marked and the first one
  focused (except the new-role page, which does not move the focus);
- edit dialogs: report template (*تعديل القالب*), country (*تعديل الدولة*) with the dial-code message above;
- server message in Arabic: `SET-003` (B-1).

The blocked-save notice "لم يُحفظ السجل: صحّح الحقول المعلَّمة أولًا." shows on 14 of the 22 dialogs and on the user, test and
service editors; it is missing on 8 forms (F-03).

The language was switched back; `app.tenant_memberships.preferred_language` of `uat.sup2` is `en` again.

### C-2 · Report templates · PARTLY (see F-02)

- **Upload logo / signature: not possible.** The add and edit dialogs have Name EN/AR, Document type, Language, QR, header and
  footer texts only. The register shows a *Logo* / *Signature* badge when a file id is stored, and the page states that choosing
  the file "waits for the Files upload API" (intake LIS-INT-CTB-001, assumption A10). The scratch PNGs (`v19-logo.png`,
  `v19-signature.png`, 64×32 and 96×32) could not be used.
- **Preview: none** on the page.
- **R2-03, Arabic option labels: PASS.** Document type: النتائج، المزرعة، تقرير نصي، المطالبة، الإيصال، الملصق، أمر الشغل;
  Language: الإنجليزية، العربية، الإنجليزية والعربية; the register rows read "النتائج | الإنجليزية والعربية | QR | نشط".

### C-3 · Platform catalogue test types · PASS

All created on `/admin/catalogue/tests` (clinical group CHEM, sample SER) and saved with **F10**:

| Code | Type | Specific part | Requests | Database (`app.tests`, platform tenant) |
| --- | --- | --- | --- | --- |
| V19F | Formula | *Check formula*: `ROUND([NOPE] + @AGE, 1)` → "The formula reads a test code that does not exist or is inactive. — Unknown or inactive test code(s): [NOPE]."; `ROUND([TSH] * 2 + @AGE / 10 * IF(@FEMALE, 0.9, 1), 1)` → "Valid: … — reads TSH." · **Timing tab**: duration 6 h, minimum 2 h, frequency 7 days, Alert | POST 201, `…/timing` 204, `…/formula` 204 | `formula_expression` as typed, `formula_references` `{TSH}`, 6 / 2 / 7 / Alert |
| V19NN | Non-numeric | tabs General, Reference ranges, Timing | POST 201 | type NonNumeric |
| V19FT | Formatted text | *Paragraphs* tab: "Gross description" with template text, "Conclusion" without | POST 201, `…/paragraphs` 204 | 2 rows in `app.test_paragraphs` |
| V19CU | Culture | tabs General, Timing | POST 201 | type Culture |

Each save showed "The test was created." and opened the saved record.

### C-4 · Services · PASS

Created on `/admin/catalogue/services` with **F10** ("The service was created."):

| Code | Type | Members (Members tab) | `app.service_members` |
| --- | --- | --- | --- |
| V19P | Profile | tests TSH, V19F ("A profile carries one or more tests…") | Test:TSH, Test:V19F |
| V19PK | Package | services V19P, TSH ("A package carries laboratory services at one package price.") | Service:V19P, Service:TSH |
| V19CS | Culture | test V19CU ("A culture carries its tests, exactly one of type Culture.") | Test:V19CU |
| V19TR | Text report | test V19FT ("A text report carries one formatted-text test.") | Test:V19FT |

### C-5 · `uat.sup2` in the browser · PASS

- Menu: Home plus 33 entries, the same as `platform.admin` (Platform Administrator role).
- Refused URLs, each redirected to `/admin`; on a full page load the toast "This page is not available for your account."
  shows: `/admin/library/analyser-models`, `/pcr-kits`, `/code-systems`, `/rule-templates`, `/admin/support`,
  `/registration/new`, `/account/dashboard`, `/organization/users`, `/treasury/access`, `/results/worklist`,
  `/admin/does-not-exist`.

### C-6 · Session timeout and screen lock · FAIL (F-01)

The platform tenant has the parameter (*Session timeout (minutes)*: "Idle minutes before the screen locks (1–720). Default: 30.").

- Set to **1** and saved (`app.tenant_settings` `sessionTimeoutMinutes` = 1 at 12:45:02Z); full page load of
  `/admin/dashboard` at 12:46:00Z; no input afterwards.
- Checked at 12:47:11Z and 12:48:19Z (more than twice the timeout): the page was unchanged, no dialog, no lock, no sign-out.
- The source and the pinned bundle read `sessionTimeoutMinutes` only on the two parameter pages; the server declares the setting
  (`SettingKeys.SessionTimeoutMinutes`) and reads it nowhere. No idle or lock code exists.
- Restored to **30** (12:48:49Z). The value is now stored as a row equal to the default; before the run there was no row.

### C-7 · Sessions "Since" across days · Not testable

`/admin/sessions` lists one active session (the signed-in user's own) for `platform.admin` and for `uat.sup2`.
`identity.refresh_sessions` of the platform tenant has live sessions only from 12 Oct. A sort across calendar days needs live
sessions started on different days, which cannot be produced within a run.

### C-8 · Wallet debits and the billing run · Does not exist in Release 1

- `Wallet.ChargeSubscription` (monthly fee) and `Wallet.DebitUsage` (metered usage) exist in the domain only. Nothing in
  `src/` calls them; only `WalletTests` and `PlatformAdminModuleTests` do.
- No endpoint: `PlatformPlansWalletsController` offers plans (create, edit, deactivate, reactivate), wallet top-up and the decline
  of a top-up request; `/api/v1/wallet` offers the laboratory's top-up request. The only hosted service is the outbox.
- `saas.wallet_transactions` holds only `TopUp` rows (5). No wallet is past its `next_charge_at_utc`.

Nothing was run. A billing run would be new work (scheduled jobs are listed as not built in CLAUDE.md §5).

### C-9 · Forced requests (Node client) · PASS

| Case | Request | Answer |
| --- | --- | --- |
| Platform user without `tenancy.*` (`v19.user`, V15 Role) | `POST /api/v1/platform/plans` (valid body) | **403 `AUTHZ-001`** |
| same | `POST /api/v1/platform/wallets/{V19 tenant}/top-up` | **403 `AUTHZ-001`** |
| same | `GET /api/v1/platform/plans` | 403 `AUTHZ-001` |
| Master Data Reviewer (`uat.mdr`, no `tenancy.*`) | the same three | 403 `AUTHZ-001` each |
| Laboratory token (`v19wiz.admin`, holds 95 keys of its laboratory) | `GET /api/v1/platform/plans`, `/dashboard?period=month`, `/countries`; `POST /api/v1/platform/plans` | **403 `AUTHZ-001`** |
| same | `GET /api/v1/platform/wallets`, `/api/v1/platform/tenants` | **404** (no such GET route) |
| Revoked cookie on refresh (`v19.user`): sign in, rotate once (200), sign out with the rotated cookie (204), wait 6 s | `POST /api/v1/auth/refresh` with the signed-out cookie | **401 `AUTH-012`**, security event `SESSION_CONTEXT_CHANGED` / `refresh_after_logout` (info), **no reuse alarm** |
| same | refresh with an invalid cookie value | 401 `AUTH-012` |

Observation (not a defect): presenting the **older**, rotated-away cookie of the same family after the sign-out also answers
401 `AUTH-012`, and logs `REFRESH_REUSE_DETECTED` (error). That is a real replay of a superseded token (a browser never keeps it
after a rotation), as in round 3, R2-06 case B.

### C-10 · Keyboard: F10 · PASS

- Test editor and service editor: every create in C-3 and C-4 was saved by F10 (201 + child saves).
- Platform parameters: F10 in the Time zone field sent `PUT /api/v1/settings` (422 with the bad zone, then restored).
- Dialog (*Add template* on SA-20): F10 ran the save: 3 fields marked, no request.
- Ctrl+S on the parameters page: the browser default was prevented (`defaultPrevented` true).

### C-11 · Smoke · PASS

- `uat.sup2`, English: 33 / 33 menu pages open on their own URL with a title and data; no response ≥ 400; no raw dictionary key.
- Same in Arabic: 33 / 33, every title in Arabic.
- Public pages, English and Arabic: `/welcome`, `/welcome/subscribe`, `/login`, `/login/recover`: titles in the language, no raw
  key, no response ≥ 400 except the start-up refresh of `/login` without a cookie (401, as in every round).
- Console, errors only, whole run: 401 × 11 (start-up refresh on each `/login` load and the deliberate wrong-password sign-ins),
  409 × 4 (the duplicate users of B-4), 422 × 2 (the deliberate `Mars/Olympus` saves). No other error, none during the smoke runs.

## 3. Not tested, and why

| Item | Reason |
| --- | --- |
| Sessions "Since" across days (C-7) | Only one live session can be listed; live sessions of earlier days do not exist on the lane. |
| Wallet debits (C-8) | No billing run exists in the code or the API; nothing to run. |
| Logo and signature upload, report preview (C-2) | Not built (no file picker, no preview; intake assumption A10). |
| A user override on a Release-2 module (B-3) | The editor cannot create one, and no user holds one. |
| Printing | Print buttons were not clicked (instruction; hidden pane). |

## 4. New findings

| Id | Sev | Finding | Evidence |
| --- | --- | --- | --- |
| **F-01** | **S3** | The session timeout has no effect. The parameter *Session timeout (minutes)* (SA-19, NFR-007 "session timeout") is saved, but no screen lock or idle sign-out exists in either portal. A session idle for twice the timeout stays usable. | C-6: timeout 1 min, 2 min 19 s idle after a full load, nothing happened; `sessionTimeoutMinutes` is read only by the parameter pages; the server never reads it. The work-around is to sign out by hand. |
| **F-02** | S4 | Report templates (SA-13, Release 1) cannot get a logo or a signature image, and have no preview. The SRS lists logo and signature for the page. Known and recorded as intake assumption A10 (the Files upload API). The laboratory logo exists on the client's Setup › Parameters, and user signatures exist, so printing is not blocked. | C-2. |
| **F-03** | S4 | The blocked-save notice "The record was not saved: correct the marked fields first." is missing on 8 Super Admin forms, though the fields are marked and focused: Preparations, Diagnoses, Test rules, Shortcut sets, *Add client*, *Add template* (SA-20), *Add plan*, and the new-role page (which also does not move the focus). The pages built on the shared master-data page, and the user, test and service editors, show it. | C-1, checked in Arabic and the same code path in English. |
| **F-04** | S4 | "(s)" plurals remain on Super Admin texts: "1 parameter(s) saved." (Arabic "تم حفظ 1 إعداد/إعدادات."), "This role has 1 member(s).", "…holds 3 permission(s) of modules of a later release…". The same form is used in `users.keptLaterRelease`, `sequence.saved` and several client texts. The tube texts were fixed this way in v0.6.0. | B-3, C-6. |

Observations (no finding):

- `v13.user` still shows the mobile `1013131315` in *Admin users* (stored as typed, from before the E.164 rule of C-03). It is
  legacy data; new and edited users are stored in E.164.
- Package V19PK holds TSH twice (through profile V19P and as its own service). The catalogue does not warn. Registration handles
  test-level repeats (`REG-109` / `REG-206`), so this is noted only.

## 5. Database checks

| Check | Result |
| --- | --- |
| Wizard rows (A-1) | request Completed, tenant Trial with trial end, wallet, branch, role, owner membership: match the screen |
| Templates (A-3) | `password-recovery` `is_active` false → true; texts unchanged |
| Admin user (B-4) | `v19.user` row, E.164 mobile, V15 Role, membership in the platform tenant |
| Role keys (B-3) | Operations Admin 9 → 10 (wallet kept) → 9 |
| User overrides (B-3) | `v19.labuser` removed `{dashboard.view}` only |
| V14L (B-6) | Red Critical only |
| Tests and services (C-3, C-4) | 4 tests and 4 services with timing, formula, paragraphs and members as entered |
| Settings (B-1, C-6) | `timeZone` untouched; `sessionTimeoutMinutes` 30 → 1 → 30 |
| Security events (C-9) | `refresh_after_logout` info for the signed-out cookie; no alarm for it |
| Outbox, run window, platform and V19 tenants | 59 events, all processed, none dead-lettered, 0 retries |

## 6. Settings changed and restored

| Setting | Change | Restored |
| --- | --- | --- |
| SA-20 template `password-recovery` | deactivated (12:43Z) | activated 12:43:59Z |
| Platform parameter `sessionTimeoutMinutes` | 30 → 1 (12:45:02Z) | 30 at 12:48:49Z (stored as a row, equal to the default) |
| Platform parameter `timeZone` | `Mars/Olympus` refused (422) three times | never saved; `Africa/Cairo` |
| Role *Operations Admin* | + `audit.view` (12:50:41Z) | removed 12:50:53Z; the original 9 keys |
| `uat.sup2` language | العربية for C-1 | English again (13:07:29Z) |
| Platform test V14L | Red: Normal removed (allowed change) | not restored, as asked |

## 7. Data created (prefix V19, invented)

| Record | Id / key | Where |
| --- | --- | --- |
| Laboratory **V19 Wizard Lab** (subscription, trial) | tenant `01a1301e-642d-702b-b9a0-1f3307d27444`, request `01a1301e-0101-7213-9d27-3e5df8220c86`, slug `v19-wizard-lab` | public wizard |
| User `v19wiz.admin` (primary administrator) | mobile +201019190001 | V19 Wizard Lab |
| User `v19.labuser` (Laboratory Administrator, override − dashboard.view) | `01a1303b-217c-7e18-b4a0-727dc31ff361`, mobile +201019191902 | V19 Wizard Lab |
| Admin user `v19.user` (V15 Role) | `01a13022-2600-760d-8c6f-80aab308e2bb`, mobile +201019191901, e-mail v19.user@example.test | platform tenant |
| Tests V19F, V19NN, V19FT, V19CU | `01a1302d-eeec-…`, `01a1302e-2f80-…`, `01a1302e-9c5c-…`, `01a1302e-eb9e-…` | platform catalogue |
| Services V19P, V19PK, V19CS, V19TR | `01a13030-10a9-…`, `01a13030-59c3-…`, `01a13030-947a-…`, `01a13030-b951-…` | platform catalogue |
| Notification files | 6 files from 12:37Z to 12:44Z for +201019190001 and +201019191901 (otp, portal-url, 4 × password-recovery) | `.local/mail-pickup/validation` |

The global V19 tests and services were created after V19 Wizard Lab was provisioned, so no laboratory holds a copy. The
passwords of the three V19 accounts are in the git-ignored `.local/dev-credentials.md`.
