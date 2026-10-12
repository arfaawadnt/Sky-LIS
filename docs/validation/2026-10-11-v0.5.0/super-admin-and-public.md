# Validation 2026-10-11 (v0.4.1): tester C, Super Admin portal and public pages

Release under test: **v0.4.1** (`VERSION`), SPA `http://localhost:4300` (own tabs only) → validation API `127.0.0.1:5102`,
database `lis` on the private PostgreSQL `127.0.0.1:5433`, read-only as `lis_owner`. Tester: automated validation agent C.
Every value was entered through the real pages (browser automation; clicks and form events issued from the page context);
a second "device" for the session tests was a small Node client of the public auth API that seals the password exactly as
the SPA does (scratchpad only, never committed). No application code, dictionary, test, script or migration was changed.
Time: 2026-10-11, 22:09–22:58 (UTC+3).

References: SRS `docs/srs/src/srs.data.js` (SA-01…SA-21, CP-01…CP-04, WF-12, BR-19, BR-25), intakes
`LIS-INT-{PLT,ACC,CAT,CTB,ORG,FND}-001`, `nav.registry.ts`, the previous run `docs/validation/2026-10-11/super-admin-and-public.md`
and its re-tests `retest-admin.md`, `retest-admin-round2.md`.

Environment notes (read before the results)

- **The code under test changed during the run.** From about 22:36 someone edited the client-portal registration feature in
  the working tree (`frontend/src/app/features/registration/**`, `public/i18n/registration.*.json`, three new `ui/*.ts`
  files; uncommitted). The dev server reloaded every open page on each save, about ten times. None of the changed files
  belongs to the Super Admin portal or the public pages, so the results below hold for v0.4.1, but the bundle the browser
  ran was not a pristine v0.4.1 build. These reloads also exposed defect C-02.
- Early in the run one `navigate` call was sent to a tab id (`tab-5`) that I had not created (another agent opened it at
  the same moment). The navigation was refused by the pane and the tab disappeared; nothing was typed or submitted there.

## 1. Summary

| Executed | Passed | Failed | Blocked | Not tested |
| --- | --- | --- | --- | --- |
| **132** | **120** | **12** | **3** | **6** |

Defects: **12** (C-01…C-12): S1: 0 · S2: 0 · S3: 4 · S4: 8.

Headlines

- **C-01 (S3)**: the *Export CSV* of Roles and Admin users has disappeared for every platform account. v0.4.0 put that
  export behind the new key `organization.export`, but the existing *Platform Administrator* role was never given the new key
  (99 of 101 keys). Nobody can grant it either: the server refuses with `ORG-030` "You cannot grant a permission you do not
  hold". The same applies to `treasury.export`, and to 11 of the 15 laboratory administrator roles provisioned before v0.4.0.
- **C-02 (S3)**: when two tabs of the same browser refresh their session at the same moment (both reloaded, a browser
  restart that restores tabs, here the dev-server reloads), the second refresh is treated as token theft. The whole session
  family is revoked, both tabs are signed out and an `error` event `REFRESH_REUSE_DETECTED` is written. Seen in the database
  for platform.admin (22:44:07) and for another tester's account (22:43:11).
- **C-03 (S3)**: platform-user mobiles are stored as typed (`1013131315`, `01014141421`). Password recovery by mobile only
  matches that exact string. A user stored in E.164 cannot recover by typing the national form (`01014141421` → `unknown_account`),
  and the same number can be registered twice in two spellings.
- **C-04 (S3)**: a user who must change their password can still use the whole API with the temporary password. Only the
  SPA guard redirects them. The sign-in answers `signed-in` with a full access token, and `GET /api/v1/settings` answered 200
  for `v14lab.admin` while `must_change_password = true` (ORG-001 A13 says the sign-in flow enforces the change).
- What works well: every earlier defect of this scope stays fixed (section 6), including the stale-tab identity check (C-01
  of v0.3.8) under an instrumented tab that drops broadcasts. All other checks passed: client provisioning and the BR-25 copy
  (every active global item, references remapped, inactive items skipped), deactivation → `CLI-020`, the administrator lock
  → `AUTH-015`, the lockout counter, the two-decimal top-up rule, the test editor keeping a refused band, back/forward in the
  editors, filtered exports, platform parameter inheritance, the read-only views and the route guard with its notice. The
  Arabic is complete on all 33 admin pages. Audit chain verified (137 = 137), and every outbox event of the run was processed
  (296, 0 attempts, 0 dead letters).

## 2. Coverage

| Page (SRS) | Functions covered | Cases |
| --- | --- | --- |
| /welcome (CP-01) | content EN/AR, R1-only texts, links | PUB-01, PUB-02 |
| /welcome/subscribe (CP-02) | required, formats, send code, code format, wrong code, Arabic, completion | PUB-03…PUB-08 |
| /login (SA-01 / CP-03) | unknown user EN/AR, lockout, administrator lock (`AUTH-015`) EN/AR | PUB-09…PUB-11 |
| /login/recover (CP-03) | empty, unknown, code format, wrong code, Back + new request, by username / mobile, completion | PUB-12…PUB-15 |
| Sessions and isolation | sign-out broadcast, sign-in over an open sign-in page, stale tab, route guard (typed and in-app URLs), client session on /admin, simultaneous refresh, must-change on the API | ISO-01…ISO-08 |
| Sessions (SA-08) | list, search, CSV, terminate (effect on the other device) | SES-01…SES-03 |
| Home, Dashboard (SA-02, SA-03) | tiles vs DB, periods, Arabic | DSH-01…DSH-04 |
| Roles (SA-06) | required, no permission, create, screen rights, general, deactivate, members protection, main-administrator role, export key, read-only | ROL-01…ROL-10 |
| Admin users (SA-07) | required, formats, password rule, create, edit, lock / `AUTH-015` / unlock, reset password, deactivate / activate, assign role, main administrator (self, second administrator, API), read-only | USR-01…USR-12 |
| Clients (SA-04) | list, filters, search, CSV, add (all validations, active plans only), provisioning, BR-25 copy, platform trail, edit, reset administrator password, deactivate / `CLI-020` / activate, open wallet, Arabic | CLI-01…CLI-19 |
| Subscriptions & wallets (SA-05) | wallet, top-up rules, transactions filter / CSV, plans CRUD and rules, Arabic | SUB-01…SUB-10 |
| Audit log (SA-09) | client column / filter, CSV with Client, filters, entry detail and history, verify chain, attribution, hints | AUD-01…AUD-07 |
| Master data (SA-12/13/14) | 18 registers: required, uniqueness, create, edit, (de)activate, search, filtered CSV; test editor (refused section kept, bands, list test), services, sequence, editors back/forward, Arabic | CAT-01…CAT-24 |
| Countries / Governorates (SA-10/11) | validations, IANA picker, edit, delete rule, governorate CRUD, filtered exports | GEO-01…GEO-07 |
| Platform parameters (SA-19) | validation, save, inheritance, restore, Arabic | PAR-01…PAR-05 |
| Notification templates (SA-20) | list, validations, preview, e-mail subjects, (de)activate, CSV, viewer read-only, fallback | TPL-01…TPL-07 |
| Profile (SA-21) | own data, password checks, language toast, must-change across two tabs | PRF-01…PRF-04 |

## 3. Test cases

### 3.1 Public pages

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| PUB-01 | /welcome in English | Release-1 content only, Subscribe / Sign in links | Hero "reception, the bench, finance and the patient report"; five feature cards and highlights are R1 modules; no sample collection, analysers, cash closing or report templates (R4-01 fixed) | PASS |
| PUB-02 | Switch to العربية | rtl, Arabic | `dir=rtl lang=ar`; all texts Arabic, only product names Latin | PASS |
| PUB-03 | Wizard step 1, *Send verification code* empty | 8 required fields | 8 × "This field is required." | PASS |
| PUB-04 | Egypt, mobile `12345`, manager `010123`, e-mail `not-an-email` | refused with the country rule | "The value is not valid." ×2 (no digit count; the Add client form says "has 10 digits after +20"), "Enter a valid e-mail address." | FAIL (C-08) |
| PUB-05 | V14 Wizard Lab, Egypt · Cairo, `01014141401` / `+201014141402`, licence, e-mail → send | code step, request stored | "We sent a code to +201******402", "valid for 5 minutes", 60 s countdown; DB D-01 | PASS |
| PUB-06 | code `12`, then `000000` | format error (no request), then invalid code | "The value has the wrong format." (challenge attempts stayed 0) / "The code is invalid or expired." | PASS |
| PUB-07 | Arabic on the code step | rtl, Arabic | "أرسلنا رمزاً إلى +201******402.", "الرمز غير صحيح أو منتهي الصلاحية." | PASS |
| PUB-08 | Complete the wizard | administrator created | no SMS gateway on the lane, the code cannot be read | BLOCKED |
| PUB-09 | Sign in `v14.nobody` (EN, then AR) | `AUTH-002` text | "The username or password is incorrect." / "اسم المستخدم أو كلمة المرور غير صحيحة."; 2 × `SIGN_IN_FAILED unknown_account` | PASS |
| PUB-10 | `v14.user` wrong password ×6, then the right one | lock at the 5th failure (30 min) | 1–5 "incorrect"; 6th and right password: "The account is locked. Ask your administrator to unlock it, or try again later…"; DB D-05 | PASS |
| PUB-11 | `v14.user` locked by an administrator, right password (EN, AR) | `AUTH-015` | "Your account was locked by an administrator. Contact your administrator to unlock it." / "تم قفل حسابك بواسطة المسؤول…"; `SIGN_IN_FAILED locked_by_administrator` ×2, attempts stay 0 | PASS |
| PUB-12 | Recovery: empty; `v14.nobody` → code `12a`, `000000` + new password (AR) | required; neutral code step; format; refusal | "هذا الحقل مطلوب."; "إن وُجد حساب مطابق…" with countdown; "صيغة القيمة غير صحيحة."; step 3 → "الرمز غير صحيح أو منتهي الصلاحية." back on the code step; `PASSWORD_RECOVERY_FAILED unknown_account` | PASS |
| PUB-13 | *Back*, new identifier `v14.nobody2`, *Send code* | fresh code step | the previous attempt's "The code is invalid or expired." is shown at once on the new code step | FAIL (C-06) |
| PUB-14 | Recovery for `v14.user` by username, by `01014141421` (national form) and by `+201014141421` | a code for each spelling of the registered mobile | username → `PASSWORD_RECOVERY_STARTED`; national form → `PASSWORD_RECOVERY_FAILED unknown_account` (no challenge); E.164 → started (challenges 4 → 5 → 5 → 6) | FAIL (C-03) |
| PUB-15 | Recovery completion with the real code | password changed | code not readable (no gateway) | BLOCKED |

### 3.2 Sessions and portal isolation

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| ISO-01 | Two tabs as platform.admin; sign out in tab B | tab A follows | A went to `/login` within 2 s; `SESSION_REVOKED logout` | PASS |
| ISO-02 | Tab A on `/login`, tab B signed in as platform.admin; A signs in as `uat.mdr` | B's session ends; B follows as uat.mdr; one live session | B reloaded as uat.mdr; `SESSION_REVOKED superseded_by_sign_in` for platform.admin; only the uat.mdr family live (D-06) | PASS |
| ISO-03 | Tab A (platform.admin, editing v14.user, instrumented to drop broadcasts); tab B signs out, then signs in `v14.user`; A clicks *Save* (designation) | A is not served as another user; nothing saved | A did a full page load to `/login` (then hydrated as v14.user); designation unchanged ("V14 tester (edited)"); no write event | PASS |
| ISO-04 | `uat.mdr` types `/admin/clients` (full load) | refused with the notice | `/admin` + "This page is not available for your account." | PASS |
| ISO-05 | `uat.ops` opens `/admin/organization/users`, `/admin/audit`, `/admin/parameters`, `/admin/catalogue/tests`, `/admin/library/pcr-kits`, `/registration/new`, `/account/profile` | refused with the notice | all → `/admin` with the notice, client URLs included (old O-b fixed) | PASS |
| ISO-06 | Client session `v14lab.admin` (must change) opens `/admin/clients`, `/admin`, `/admin/catalogue/tests` | refused; must-change page kept | each → `/account/profile?mustChange=1`, notice on the first | PASS |
| ISO-07 | Two tabs of one session reload at the same moment (dev-server reload; then two timed reloads) | both stay signed in | 22:44:07: rotation at .394, the other tab presented the rotated token at .450 → family revoked `ReuseDetected`, both tabs at `/login`, 3 × `REFRESH_REUSE_DETECTED` (error). Two later timed reloads (40–200 ms apart) passed: timing-dependent | FAIL (C-02) |
| ISO-08 | API sign-in of `v14lab.admin` (must change), then `GET /api/v1/settings` | refused until the password is changed | sign-in `200 signed-in` with an access token, settings `200` | FAIL (C-04) |
| SES-01 | Sessions list | user, IP, since, last seen, status; own row marked | two rows (uat.ops second device, "This session") | PASS |
| SES-02 | Search `uat.ops`; *Export CSV* unfiltered and filtered | CSV = screen | 2 rows / 1 row, header `User,Username,IP address,Since,Last seen,Status` | PASS |
| SES-03 | *Terminate* the uat.ops second-device session; that device refreshes | session ends at once | "The session was terminated."; device refresh → 401 `AUTH-012`; `SESSION_TERMINATED_BY_ADMIN` (obs. O-9) | PASS |

### 3.3 Home and dashboard (SA-02, SA-03)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| DSH-01 | /admin | Clients and Active subscriptions tiles only | "Clients 13", "Active subscriptions 13", greeting, dashboard pointer; = DB (13 tenants, all Active) | PASS |
| DSH-02 | /admin/dashboard, month | counts = DB | clients 13/13, trial 0, admin users 8, served 17, tests 47, registrations 30, wallets ≤ 0: 10, plan distribution = DB (D-02) | PASS |
| DSH-03 | Period Today / This week / This year | per period | today 12 / 24 / 14 = DB for today (Asia/Riyadh day); week and year 17 / 47 / 30 | PASS |
| DSH-04 | Arabic | rtl, Arabic | "لوحة المتابعة", Arabic labels; plan codes Latin | PASS |

### 3.4 Roles (SA-06) and Admin users (SA-07)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| ROL-01 | *Create role* empty | names required | 2 × required | PASS |
| ROL-02 | names, no permission | refused | "Grant at least one permission before creating the role." and the matrix tab opens | PASS |
| ROL-03 | V14 Role / دور التحقق 14, description, Audit View | "Role created." | DB D-03 | PASS |
| ROL-04 | Screen rights + Platform View + Catalogue View, *Save* | saved with toast | "Screen rights saved."; `PRIVILEGE_MATRIX_CHANGED`; field change | PASS |
| ROL-05 | General: description edited | "Saved." | DB + field change | PASS |
| ROL-06 | V14 Role B (2 keys, no members) → *Deactivate* → Confirm | "Deactivated." | confirm "It can no longer be assigned."; DB `is_active=f` | PASS |
| ROL-07 | Role with members (Master Data Reviewer) | deactivation refused | button disabled, title "A role with members cannot be deactivated." | PASS |
| ROL-08 | Platform Administrator role: untick Payer claims Export → *Save* → Confirm | refused (main administrator's role) | toast "The main administrator's role cannot lose permissions." (`ORG-014`, 403); DB 99 keys unchanged; *Discard changes* restores 99/101 | PASS |
| ROL-09 | Roles and Admin users as platform.admin and uat.sup2: *Export CSV*; uat.sup2 grants "Branches and roles: Export" to V14 Role | export available to the administrators | no *Export CSV* on either page; the grant is refused "You cannot grant a permission you do not hold." (`ORG-030`, organization.export) | FAIL (C-01) |
| ROL-10 | `v14.user` (organization.view only) opens V14 Role | read-only | names / description read-only, 125 matrix boxes disabled, no *Save* / *Deactivate* | PASS |
| USR-01 | *Create user* empty | 6 required | username, password, confirm, names EN/AR, e-mail | PASS |
| USR-02 | `ab`, `short1`, `bad` e-mail, mobile `123`, 32-character username, confirm differs | format errors | wrong format / "At least 8 characters." / e-mail / "Enter a valid mobile number." / "At most 30 characters." / "The two passwords differ." | PASS |
| USR-03 | password `v14.user-Pass1` | refused | "The password must not contain the username." | PASS |
| USR-04 | v14.user, V14 Role, designation, shifts, mobile `01014141421`, must change | user + membership + role | "User created."; DB D-04 (mobile stored as typed, see C-03) | PASS |
| USR-05 | Profile: designation, mobile `+201014141421` → *Save* | "Saved." | DB + field changes (Mobile, Designation) | PASS |
| USR-06 | *Lock* → sign-in (`AUTH-015`, PUB-11) → *Unlock* | confirmations and toasts | "User locked." / "User unlocked."; `MEMBERSHIP_CHANGED …:locked/unlocked` | PASS |
| USR-07 | *Reset password*: empty, short + mismatch, contains username, valid | rules; dialog closes | required ×2; "At least 8 characters."; "The password must not contain the username."; "Password reset; the user's sessions were ended." and the dialog closes | PASS |
| USR-08 | *Deactivate* / *Activate* | status + toast | "Deactivated." / "Activated."; `USER_DEACTIVATED`, `:reactivated`; platform trail `TenantMembershipDeactivated/Reactivated` | PASS |
| USR-09 | *Assign role* Master Data Reviewer, then back to V14 Role | confirmations and toasts | "Role assigned." ×2; `MEMBERSHIP_CHANGED …:role:…` ×2 | PASS |
| USR-10 | platform.admin opens own record | protected | "This is your own account…"; Security without actions; role select disabled "The main administrator keeps the administrator role." | PASS |
| USR-11 | uat.sup2 opens platform.admin; API `POST …/lock` as uat.sup2 | actions refused | *Reset password*, *Lock*, *Deactivate* disabled with "The main administrator cannot be locked, deactivated or demoted…"; API 403 `TEN-012`; DB unchanged | PASS |
| USR-12 | `v14.user` (identity.view) opens uat.mdr | read-only | 9 fields read-only, no action buttons | PASS |

### 3.5 Clients (SA-04)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| CLI-01 | List | SA-04 columns | client + portal, country · governorate, manager + mobile, plan, wallet, branches, users, created, source, status, actions | PASS |
| CLI-02 | Search "V11 Validation", `uatlabb`; Source = Subscription; Plan = UATPLAN; Status = Inactive | filtered | 1 / 1 (slug) / "No clients match the filter." / 1 / none | PASS |
| CLI-03 | Search → *Export CSV* | filtered CSV | `clients-2026-10-11.csv`, 1 data row (obs. O-4) | PASS |
| CLI-04 | *Create client* empty | 12 required | 12 | PASS |
| CLI-05 | e-mail `x@`, slug `Bad Slug!`, username `AB`, password containing it, confirm differs | inline errors | e-mail, slug and username format, "must not contain the username", "The passwords do not match." | PASS |
| CLI-06 | Egypt: lab mobile `0233351111`, manager `+2010111100` | refused per field (GEO-010 / GEO-014) | "The laboratory mobile has 10 digits after +20…" / "The manager's mobile has 10 digits after +20…" under the fields; nothing sent | PASS |
| CLI-07 | slug `uatlabb` | `CLI-100` | "This portal address is already taken." (409) | PASS |
| CLI-08 | username `labb.admin` | refused | "This username is already used." (409) | PASS |
| CLI-09 | Plan list | active plans only | V11PLAN, UATPLAN, STARTER, STANDARD, ENTERPRISE; V12PLAN / V13PLAN (inactive) absent; filter lists them "(inactive)" | PASS |
| CLI-10 | V14 Validation Lab, Giza, Arabic messages, STARTER, empty slug, `v14lab.admin` | provisioning | "The client was created. Portal https://v14-validation-lab.skylis.app, administrator v14lab.admin…"; DB D-07 | PASS |
| CLI-11 | BR-25 copy | every active global item copied, references remapped, inactive skipped | D-08 | PASS |
| CLI-12 | Platform audit | creation in the platform trail | platform seq 104 `TenantProvisioned` (client V14 Validation Lab); `TENANT_PROVISIONED`; outbox processed | PASS |
| CLI-13 | *Edit client*: Cairo, address, UATPLAN, manager mobile; slug and username | saved; slug read-only, username not editable | slug disabled, no username field; DB D-09; platform seq 105 `TenantPlanChanged` | PASS |
| CLI-14 | *Reset administrator password*: short + mismatch, contains username, valid | rules; reset | "At least 8 characters." / "The passwords do not match." / "must not contain the username"; "The administrator password of V14 Validation Lab was reset."; must change = t; `CLIENT_ADMIN_PASSWORD_RESET` | PASS |
| CLI-15 | *Deactivate client* without reason | refused | "A reason is required." | PASS |
| CLI-16 | Reason "V14 validation deactivation" → sign in `v14lab.admin` | Inactive; `CLI-020` | "The client is inactive; its users are blocked."; sign-in: "This laboratory's subscription is inactive. Contact National Technology."; D-10 | PASS |
| CLI-17 | *Activate client* | Active | "The client is active."; reason cleared; seq 107 `TenantActivated` | PASS |
| CLI-18 | *Open wallet* | that client's wallet | `/admin/subscriptions?client=01a12c6a…`, V14 Validation Lab selected | PASS |
| CLI-19 | Arabic: Add client mobile errors | Arabic per field | "جوال المعمل 10 أرقام بعد +20 في الدولة المختارة." / "جوال المدير…" | PASS |

### 3.6 Subscriptions & wallets (SA-05)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| SUB-01 | Client wallet of V14 Validation Lab | balance, fee, next charge, SMS | 0.00 EGP "Metered services are paused.", 999.00 EGP UATPLAN, next charge 11/11/2026, SMS 0 / 100 | PASS |
| SUB-02 | Top-up: empty; 0; 250.555; −5 | required; ≥ 0.01; two decimals | "This field is required." ×2; "The value must be at least 0.01."; "The amount has at most two decimals."; "…at least 0.01." | PASS |
| SUB-03 | 250.55, V14-INV-1, note | balance and transaction | "The wallet was topped up."; 250.55 EGP; D-11 | PASS |
| SUB-04 | Type Usage / Top-up; *Export CSV* | filtered rows and CSV | 0 / 1; CSV header only / 1 row (obs. O-8) | PASS |
| SUB-05 | Plans tab | fee, limits, clients | 7 plans, client counts = DB | PASS |
| SUB-06 | `v14 plan`, `EGPX`, fee −1, branches 0, users 0, SMS −1, analysers −1 | refused | code format; "At most 3 characters."; ≥ 0 / ≥ 1 / ≥ 1 / ≥ 0 / ≥ 0 | PASS |
| SUB-07 | V14PLAN, fee **14.145**, 2 / 14 / 140 / 0 | refused (two decimals, as top-ups) | "The plan was saved."; stored **14.15** | FAIL (C-05) |
| SUB-08 | Edit fee 14.00; code | saved; code read-only | code disabled; "The plan was saved."; field change 14.15 → 14 | PASS |
| SUB-09 | Deactivate / Activate / Deactivate; Status = Inactive | toasts, filter | "The plan is inactive; clients keep it…" / "The plan is active."; filter lists V13PLAN, V12PLAN (+V14PLAN after) | PASS |
| SUB-10 | Arabic top-up 1.005 | Arabic rule | "المبلغ برقمين عشريين على الأكثر." | PASS |

### 3.7 Audit log (SA-09)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| AUD-01 | Entries after the run | user, client, action, entity, details | role, user, client, plan, wallet changes with before → after; client column filled for tenant and wallet rows | PASS |
| AUD-02 | Client = V14 Validation Lab → *Export CSV* | filtered rows and CSV with Client | 10 rows (provisioning, plan change, edit, deactivation, activation, top-up); CSV `Time (UTC),User,Client,Action,…`, 10 data rows | PASS |
| AUD-03 | Entity PlatformUser / Plan; user v14.user; From = tomorrow; *Reset* | filtered | 31 / 16 / "No entries match the filters." / 0 / reset to 50 rows and empty filters | PASS |
| AUD-04 | Open the `TenantProvisioned` entry → *Show the history of this record* | detail and history | time, user, client, action, entity, id, chain sequence 104, correlation id, event data; history = 7 rows filtered by Tenant + id | PASS |
| AUD-05 | *Verify chain* (twice) | intact, = DB | "intact: 108 entries" then "137 entries" = `count = max(sequence)` | PASS |
| AUD-06 | Reset the administrator password of V14 Validation Lab, filter by that client | the reset listed under the client | the `PlatformUser` change (MustChangePassword, PasswordChangedAtUtc) shows Client "—" and the client filter drops it | FAIL (C-11) |
| AUD-07 | Entity filter hint | Super Admin entities | "For example Treasury, Visit, SupportTicket" (client entities; SupportTicket is R2), the same in Arabic | FAIL (C-12) |

### 3.8 Master data (SA-12, SA-13, SA-14)

All created as platform.admin in the platform tenant; DB in D-12.

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| CAT-01 | Clinical groups: required ×4; code CHEM; order −1; V14CG (report template); edit; code; search + CSV; deactivate; Inactive filter + CSV; activate | rules, DB | required ×4; "A clinical group with this code already exists."; "at least 0"; "Saved."; code disabled; CSV 1 row = screen; "Deactivated." / "Activated."; inactive CSV 1 row `No` | PASS |
| CAT-02 | Sample types: required ×4; V14ST; *Conditions* → V14F | saved | "Saved."; "The conditions were saved." | PASS |
| CAT-03 | Units: `MIU/L`, `V14u/L` | case-insensitive unique | "A unit with this name already exists."; "Saved." | PASS |
| CAT-04 | Patient conditions: V14 Pregnant | saved | "Saved." | PASS |
| CAT-05 | Additional services: shortcut −5; V14AS 914 orderable | ≥ 1; saved | "The value must be at least 1."; "Saved."; DB `Additional`, 914 | PASS |
| CAT-06 | Tests › Add V14T (Numeric, V14CG, V14u/L, 1 dec, V14ST, V14F, LOINC, methodology) with band Low 110 > High 70 | test created, band kept, reason shown | "The test was created, but part of its configuration was refused — correct the tab marked "!"…"; stays on `/tests/<id>`, tab "Reference ranges !", the band and its text kept, reason "Limits must read: reportable low ≤ … ≤ reportable high" | PASS |
| CAT-07 | Correct to 70–110 (panic 40/400, reportable 0/1000), add Female · V14 Pregnant 15–60 y 60–95 | saved | a new band's "Age from" defaults to the previous "Age to" (120 → refused "An age interval reads from < to…", corrected); "Saved.", tab "2"; DB D-12 | PASS |
| CAT-08 | Editor V14T → TSH (router) → back → forward | each step shows its own test | V14T / TSH / V14T (bands 2) / TSH (bands 1) | PASS |
| CAT-09 | Restricted list test V14L: Yellow (normal, default), Red (critical), "." (placeholder) | saved per BR-19 | "The test was created."; ticking *Default* also ticks *Placeholder* and clears *Critical*; a new value starts *Normal* (obs. O-10) | PASS |
| CAT-10 | Services › Add: required ×6; V14SVC, shortcut 914 (taken), member V14T; then 915 | uniqueness; created | "This shortcut number is used by another service."; "The service was created.", seq 17; DB member Test:V14T | PASS |
| CAT-11 | Editors back/forward: services V14SVC ↔ TSH, users v14.user ↔ uat.mdr, roles V14 Role ↔ V14 Role B | each step shows its record | correct record on every back / forward | PASS |
| CAT-12 | Service sequence: V14SVC 15 (duplicate), 0, 18 | duplicate / zero refused; saved | *Save (1)* disabled for 15 and 0, no text at the field (obs. O-6); 18 → "1 sequence(s) saved." | PASS |
| CAT-13 | Preparations: required; `v14 bad`; option list; V14PREP, 3 options, linked V14SVC | rules; saved | code format; "An option list needs at least two different options."; "Preparation saved." | PASS |
| CAT-14 | Comment library: required ×3; v14hem, V14CG | saved, short name upper-cased | "Comment saved."; V14HEM | PASS |
| CAT-15 | Reasons: required ×3; Sample rejection | saved | "Reason saved."; kind `Rejection` | PASS |
| CAT-16 | Diagnoses: `not icd!`; Z99.V14 + V14SVC; edit name; code | rules; saved | format error; "Diagnosis saved." ×2; ICD code disabled on edit | PASS |
| CAT-17 | Report templates: required ×3; header EN only; full + QR | bilingual header | "This field is required." under Header (Arabic); "Template saved." | PASS |
| CAT-18 | Test rules: required ×4; V14SVC as trigger and added; then added V14AS | overlap refused; saved | "A service cannot be both a trigger and an added service."; "Test rule saved." | PASS |
| CAT-19 | Shortcut sets: required ×4; V14SET = V14SVC + TSH | saved | "Shortcut set saved." | PASS |
| CAT-20 | Drug types (req ×2), commercial names (req ×3), antibiotics (req ×4): V14 Penicillin → V14 Augmentin → V14AMC | saved | three "… saved." toasts; chain in DB | PASS |
| CAT-21 | Organisms: required ×3; V14ECO | saved | "Organism saved." | PASS |
| CAT-22 | Sensitivity values: V14S; duplicate V14S; deactivate | rules | "Sensitivity value saved."; "A record with this code already exists in this list."; "Deactivated." | PASS |
| CAT-23 | Search `V14` → *Export CSV* on Tests, Services, Preparations, Report templates, Antibiotics, Units, Sample types, Diagnoses | CSV = screen | 1/1, 2/2, 1/1, 1/1, 1/1, 1/1, 1/1, 1/1 | PASS |
| CAT-24 | Arabic on all 33 admin menu entries | rtl, Arabic chrome | all rtl with Arabic titles; Latin only in data (plan codes, units, placeholders, accession tokens, `Ctrl`) | PASS |

### 3.9 Countries and governorates (SA-10, SA-11)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| GEO-01 | Add country: required; ISO `XWW`; dial `998`; currency code `XW`; mobile length 40 | refused per field | required ×6 + time zone; "At most 2 characters."; "…at most 15."; currency: server 400 shown under the field "Exactly 3 characters."; dial `998` passes the browser, then the server message "The dial code is '+' followed by one to four digits…" at the top of the dialog | FAIL (C-09) |
| GEO-02 | V14 Land, XW, +998, XWD, English, time zone picker Asia/Tashkent, 9 digits | saved; IANA only | picker of 419 IANA names; "The country was saved." | PASS |
| GEO-03 | Edit: time zone Asia/Riyadh; ISO | saved; ISO read-only | ISO disabled; saved; field change | PASS |
| GEO-04 | Delete rule | only an unreferenced country | Egypt: no *Delete*; V14 Land: *Delete country* before its governorate, gone after | PASS |
| GEO-05 | Governorates: required ×3; V14 Province (district); country filter + CSV; deactivate; Inactive + CSV | saved; filtered CSV | "The governorate was saved."; 1 row = CSV; "Deactivated."; inactive 1 = CSV | PASS |
| GEO-06 | Add client → V14 Land | inactive governorate not offered | only "Choose…" | PASS |
| GEO-07 | Countries: search Egypt → CSV; Inactive → CSV (after V14 Land deactivated) | CSV = screen | 1 / 1 and 1 / 1 | PASS |

### 3.10 Platform parameters (SA-19), notification templates (SA-20), profile (SA-21)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| PAR-01 | One-time code length 3; label copies 11 | refused | "The value must be at least 4."; "…at most 10."; nothing sent | PASS |
| PAR-02 | Label copies 1 → 3, *Save* | saved, audited | "1 parameter(s) saved."; D-13 | PASS |
| PAR-03 | `v14lab.admin` (never set it) reads its settings (API) | 3 from the platform | `value 3, source "Platform", inheritedValue 3` (`isDefault: true`, obs. O-11) | PASS |
| PAR-04 | Back to 1, *Save* | restored | "1 parameter(s) saved."; value 1 | PASS |
| PAR-05 | Arabic | Arabic hints | every hint Arabic (tokens `{yyyy}`, `ACC-` Latin) | PASS |
| TPL-01 | List, placeholders | codes, channel, text, status; 14 chips | otp, panic-value, password-recovery, portal-url, treasury-request, v11-notice | PASS |
| TPL-02 | required ×3; code `V14 Bad`; text `{bogus}` | refused | "The value has the wrong format."; "The text uses a placeholder the platform does not know." | PASS |
| TPL-03 | v14-notice SMS EN/AR with {lab} {code} {minutes} | preview, saved | preview "V14 notice for Al Noor Lab: code 482913 valid 5 min." + Arabic; "The template was saved." | PASS |
| TPL-04 | Edit → E-mail without subjects; with subjects | subjects required | "An e-mail template needs a subject in both languages." (fields not marked `*`, O-7); saved; DB D-14 | PASS |
| TPL-05 | Deactivate / Activate / Deactivate; *Export CSV* | status, CSV = screen | "Deactivated." / "Activated."; CSV 6 rows = 6 active | PASS |
| TPL-06 | `uat.ops` (notifications.view) opens `otp` | read-only view | dialog titled "Template", all fields read-only, only *Cancel* (R4-02 fixed); but the row action still reads "Edit template" | FAIL (C-10) |
| TPL-07 | Inactive template falls back to the built-in text | built-in text sent | no gateway; the sent text cannot be observed | BLOCKED |
| PRF-01 | /admin/profile | own account | Platform Administrator, platform.admin, NT-PLATFORM, e-mail; details read-only | PASS |
| PRF-02 | New password containing the username, mismatch | checks shown | "Does not contain your username" and "Both new passwords match" unticked; nothing sent | PASS |
| PRF-03 | العربية → *Save language*; English → *Save language* | localized toasts | "تم حفظ اللغة." (rtl) / "Your language was saved."; DB `en` at the end | PASS |
| PRF-04 | v14.user (must change) in tabs A and B; A tries `/admin/dashboard`; B changes the password | A blocked, then freed without reload; B freed | A held on `/admin/profile?mustChange=1`, then moved to `/admin` by itself (no reload) and opens the dashboard; **B** keeps "Your administrator asked you to choose a new password…" and `?mustChange=1` | FAIL (C-07) |

## 4. Database verification

Queries run as `lis_owner` (`psql -At`). `P` = platform tenant `00000000-0000-0000-0000-000000000001`, `T` = V14 Validation Lab
`01a12c6a-98eb-7bf8-a5da-b13fd4bca6c8`.

| Ref | Case | Table | Query | Result |
| --- | --- | --- | --- | --- |
| D-01 | PUB-05/06 | saas.subscription_requests, identity.otp_challenges, audit.security_events | `select r.name_en,c.name_en,g.name_en,r.mobile,r.manager_mobile,r.status,r.codes_sent,r.expires_at_utc-r.started_at_utc … where r.name_en like 'V14%'` | V14 Wizard Lab · Egypt · Cairo · +201014141401 · +201014141402 · Pending · 1 · 01:00; challenge `subscription` attempts 1 (the `12` never reached the server), 5 min; `SUBSCRIPTION_STARTED`, `SUBSCRIPTION_VERIFY_FAILED` |
| D-02 | DSH-01…03 | saas.tenants, app.tenant_memberships, saas.wallets, saas.plans, app.visits, app.results | status counts; members of P; `balance<=0`; tenants per plan; month / today counts | 13 Active; 8 members; 10 wallets ≤ 0; STANDARD 8, STARTER 3, UATPLAN 1, V12PLAN 1; month 17 patients / 30 visits / 47 results; today 12 / 14 / 24 |
| D-03 | ROL-03…06 | app.roles, audit.field_changes, audit.audit_trail | `… where name_en like 'V14 Role%'` | P · V14 Role · "V14 validation role (edited)" · `{audit.view,catalogue.view,platform.view}` → after ROL-09 `{audit.view,catalogue.view,identity.view,organization.view,platform.view}` (by UAT Second Administrator); V14 Role B `{catalogue.view,platform.view}` inactive; trail seq 94–97 RoleCreated / RoleChanged |
| D-04 | USR-04…09 | identity.platform_users, app.tenant_memberships, app.roles | user + membership + role of `v14.user` | e-mail, argon2id hash, mobile `01014141421` → `+201014141421`, membership P, V14.USER, `{Morning,Night}`, "V14 tester (edited)", role V14 Role; trail seq 98–103 (membership created, role changed ×3, deactivated, reactivated) |
| D-05 | PUB-10/11, USR-06 | identity.platform_users, audit.security_events | `select failed_password_attempts,is_locked_out,lockout_end_utc-now() …` | lock by admin: `is_locked_out=t`, attempts 0, 2 × `locked_by_administrator`; lockout: attempts 5, locked, 29:48 left, 5 × `wrong_password`, `USER_LOCKED_OUT`, 2 × `locked_out`; after *Unlock* 0 / f |
| D-06 | ISO-02 | identity.refresh_sessions, audit.security_events | sessions issued in the last 12 min with user | platform.admin family revoked at 22:19:06 with event `superseded_by_sign_in` (column `revocation_reason = Logout`, O-d carried); uat.mdr one live head |
| D-07 | CLI-10 | saas.tenants, saas.wallets, app.branches, app.tenant_memberships, app.roles | tenant row joined to country, governorate, plan; `… where tenant_id=T` | V14 Validation Lab · معمل التحقق 14 · Egypt · Giza · +201014141431 · V14-LIC-031 · V14 Manager · +201014141432 · ar · STARTER · `v14-validation-lab` · Manual · Active · v14lab.admin; wallet 0.00 EGP, next charge 2026-11-11; branch MAIN; administrator primary, active, role Laboratory Administrator (95 keys incl. organization.export); no tenant settings |
| D-08 | CLI-11 | 21 catalogue tables, references | per table `count(*)` P / active P / T; references `…@(tenant_id=T)` | active items copied 1:1 (clinical groups 2 of 4, tests 2 of 4, report templates 1 of 2, sensitivity values 0 of 1 — the inactive ones skipped); bands 3/3, service members 2/2; tests → group/unit/sample of T, bands → test and condition of T, preparation → V11SVC@T, antibiotic → commercial name of T |
| D-09 | CLI-13…17 | saas.tenants, audit.field_changes, audit.audit_trail, audit.security_events | tenant row; field changes on T; P trail seq > 104 | Cairo · "14 V14 Street, Nasr City" · +201014141433 · UATPLAN; Modified row in P; seq 105 `TenantPlanChanged`, 106 `TenantDeactivated` (reason "V14 validation deactivation"), 107 `TenantActivated`; `CLIENT_ADMIN_PASSWORD_RESET`, `TENANT_DEACTIVATED sessions:0`, `SIGN_IN_FAILED client_inactive`, `TENANT_ACTIVATED`; v14lab.admin `must_change_password=t` |
| D-10 | CLI-12 | audit.audit_trail, ops.outbox_events | trail of T and P since 22:22; outbox since the creation | T seq 1–25 (BranchCreated, catalogue copies, membership, role, `TenantProvisioned`); P seq 104 `TenantProvisioned` actor platform.admin; outbox 45 processed, 0 attempts |
| D-11 | SUB-03 | saas.wallets, saas.wallet_transactions, audit.audit_trail | balance; transactions of T's wallet | 250.55; TopUp 250.55 / after 250.55 / V14-INV-1 / note / by platform.admin; P seq 108 `WalletToppedUp` |
| D-12 | CAT-* | app.* | per table `… where tenant_id=P and code/name like 'V14%'` | V14CG (V · report V14 Group Report · V11 Results Template · order 15 · active); V14ST + V14F; V14u/L; V14 Pregnant; V14AS Additional 914; V14T (V14CG, V14u/L, 1 dec, V14ST, V14F, 2345-7, Enzymatic) with bands Any 0–120 y 70–110 / 40–400 / 0–1000 "70 - 110" and Female · V14 Pregnant 15–60 y 60–95; V14L list (Yellow default+placeholder, Red normal+critical, "." placeholder); V14SVC (V14GLU, 915, V14CG, V14ST, "v14 sugar", 24 h, seq 18, member V14T); V14PREP OptionList {8 h,12 h,Not fasting} → V14SVC; V14HEM → V14CG; Rejection "V14 Clotted sample"; Z99.V14 → V14SVC ("(edited)"); V14 Results Template Both/QR/headers/footers; V14 Rule V14SVC ⇒ V14AS; V14SET {TSH,V14SVC}; V14AMC → V14 Augmentin → V14 Penicillin; V14ECO; V14S inactive |
| D-13 | PAR-02/04 | app.tenant_settings, audit.field_changes | `… where key='labelCopies'` | P 1 → 3 (TenantSetting Modified, Value 1 → 3) → 1 |
| D-14 | TPL-03…05 | saas.notification_templates | `… where code='v14-notice'` | Email · "V14 subject" / "موضوع 14" · texts with {lab} {code} {minutes} · active → **inactive** at the end |
| D-15 | GEO-02…05 | saas.countries, saas.governorates | `… where name_en='V14 Land'` | XW · +998 · XWD · en · Asia/Riyadh (was Asia/Tashkent) · 9 · inactive; V14 Province / V14 District 1 / inactive |
| D-16 | ISO-07 | identity.refresh_sessions, audit.security_events | family `01a12c68-407f…` rotations; `REFRESH_REUSE_DETECTED` | rotations in pairs at each reload (22:34:49, 22:37:41, 22:38:40, 22:42:49, 22:43:11); 22:44:07.394 rotated, 22:44:07.450 reuse → whole family `ReuseDetected`; 3 error events; another user's family (actor 01a1258c…) the same at 22:43:11 |
| D-17 | SES-03, ISO-08 | audit.security_events | last minutes | `SESSION_TERMINATED_BY_ADMIN`, then the device's refresh → `REFRESH_REUSE_DETECTED` (error) for the terminated family; v14lab.admin sign-in `SIGN_IN_SUCCEEDED` while must change |
| D-18 | PUB-14 | identity.otp_challenges, audit.security_events | `count(*) … where purpose='password-recovery'`; events | 4 → 5 (username) → 5 (`01014141421`: `PASSWORD_RECOVERY_FAILED unknown_account`) → 6 (`+201014141421`); stored mobiles: v13.user `1013131315`, others E.164 |
| D-19 | AUD-05, all | audit.audit_trail, ops.outbox_events | `select count(*),max(sequence) … where tenant_id=P`; outbox since 22:09 grouped | 137 / 137 = "137 entries checked"; 296 processed, 0 pending, 0 dead-lettered, max attempts 0 |
| D-20 | end | identity.refresh_sessions, identity.platform_users, app.tenant_settings | live heads of my accounts; lock state; platform settings | 0 live sessions for platform.admin, uat.ops, uat.mdr, uat.sup2, v14.user, v14lab.admin; all attempts 0, none locked; platform settings delayWarnPercent 80, labelCopies 1, passwordMinLength 8, trialDays 14 |

## 5. Defect log

| Id | Sev | Case | Steps | Expected | Observed | Evidence | Suspected root cause |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C-01 | S3 | ROL-09 | As platform.admin or uat.sup2 open Roles / Admin users; then grant "Branches and roles: Export" to any role | *Export CSV* on both registers (it worked in the v0.3.8 re-test; v0.4.0 moved it behind `organization.export`) | no export button for any platform account; granting the key is refused `ORG-030` "You cannot grant a permission you do not hold: organization.export." | `app.roles` Platform Administrator: 99 keys, `organization.export` and `treasury.export` missing (catalogue = 101); PUT `/organization/roles/{id}/permissions` → 403 `ORG-030`; 11 of 15 *Laboratory Administrator* roles also lack `organization.export` | `PlatformAdministratorSeeder` re-syncs the role to `PermissionCatalog.Keys` only when DevTools runs it; no migration or start-up step grants keys added to the catalogue to the existing administrator roles (platform and laboratories); `roles.page.html` / `users.page.html` gate the button on `permissions.export`. A catalogue key that no role holds can never be granted (ORG-030) |
| C-02 | S3 | ISO-07 | One browser, two tabs of the same session; both refresh at the same moment (both reloaded together, a browser restart restoring tabs; here the dev-server reloads) | both tabs stay signed in | the second refresh presents the token the first just rotated: the whole family is revoked `ReuseDetected`, every tab goes to `/login`, `REFRESH_REUSE_DETECTED` (error) is logged | D-16: rotation 22:44:07.394, reuse 22:44:07.450, family revoked; the same for another account at 22:43:11; reproduction is timing-dependent (two timed reloads 40–200 ms apart passed) | `core/auth/auth.store.ts`: refresh is single-flight per tab only (`refreshInFlight`), no cross-tab lock (e.g. `navigator.locks`) or leader election; `RefreshSessionCommand` treats any rotated token as reuse with no grace window for the immediate successor (02 §4.3) |
| C-03 | S3 | PUB-14, USR-04 | Create or edit an admin user with mobile `01014141421`; recover by mobile in the other spelling | one canonical form (E.164 as clients); recovery finds the account in both spellings; uniqueness covers both | stored as typed (`01014141421`, v13.user `1013131315`); recovery with the national form → `unknown_account`; the same number can be registered twice | D-04, D-18; `UserCommands` uniqueness via `FindByMobileAsync` on the typed string | `PlatformUser.NormaliseMobile` is country-agnostic (keeps the national form); platform users have no country to normalise against; `PasswordRecovery` and `IdentityRepositories.FindByMobileAsync` compare the normalised string exactly. Client forms normalise to E.164 with the country rule (`Country.cs`), admin users do not |
| C-04 | S3 | ISO-08 | Reset a user's password with "must change"; that user calls the API with the temporary password | the API refuses everything but the password change until it is changed (ORG-001 A13: "enforced by the sign-in flow"; pack 02 §6.1 forced change) | sign-in returns `step: signed-in` with an access token; `GET /api/v1/settings` → 200 | node client: `sign-in 200 signed-in mustChange=true`, `settings 200` | only the SPA `passwordChangeGuard` enforces it; no server gate (pipeline has `MfaEnrollmentGate` but nothing for `MustChangePassword`) |
| C-05 | S4 | SUB-07 | Add plan with monthly fee 14.145 | refused like a top-up ("at most two decimals") | saved as 14.15 without a word | `saas.plans` V14PLAN 14.15; field change Created MonthlyFee 14.15 | `CreatePlanCommand` / `UpdatePlanCommand` validators: only `GreaterThanOrEqualTo(0)`; the plan dialog has no two-decimal rule (`DomainText` two-decimal check used for wallet amounts only, C-19 fix) |
| C-06 | S4 | PUB-13 | Recovery: wrong code → *Back* → new identifier → *Send code* | a clean code step | the previous "The code is invalid or expired." is shown on the fresh step | page text EN/AR | the recovery page keeps the completion error in its state across *Back* / new start (O-2 of v0.3.8, still open); `features/auth` recover page |
| C-07 | S4 | PRF-04 | v14.user must change, two tabs; change the password in tab B | B leaves the must-change state like A does | A moves to `/admin` by itself; B keeps the banner "Your administrator asked you…" and `?mustChange=1` until the next navigation | page text of both tabs | the profile page clears the flag for other tabs (`profile-changed`) but not its own banner / query (O-5 of v0.3.8) |
| C-08 | S4 | PUB-04 | Subscription wizard, Egypt, mobiles `12345` / `010123` | the digit count of the country, as Add client now says ("has 10 digits after +20") | generic "The value is not valid." under both mobiles | page text | `features/public` wizard still maps the country-length rule to the generic message (O-1 of v0.3.8); the C-03 fix covered only `clients.page.ts` |
| C-09 | S4 | GEO-01 | Add country with dial code `998` | refused under the field in the browser (the hint describes the rule) | accepted by the browser; the server message "The dial code is '+' followed by one to four digits, like +20." appears at the top of the dialog, the field is not flagged | POST `/platform/countries` → 400 after the currency fix; field `aria-invalid=false` | `countries.page.ts` has no pattern validator on the dial code; the domain code (GEO-002) is not mapped to the control |
| C-10 | S4 | TPL-06 | uat.ops (notifications.view only) on Notification templates | the row action reads "View" / "Open" | the action still reads "Edit template" (aria-label) although the dialog is read-only with *Cancel* only | button `aria-label="Edit template"` ×6 | `notification-templates.page` row action label not switched by `*appCan` (R4-02 fix covered the dialog only) |
| C-11 | S4 | AUD-06 | *Reset administrator password* of V14 Validation Lab; Audit log, Client = V14 Validation Lab | the reset listed under the client (SA-04 action on the client) | the `PlatformUser` change (MustChangePassword, PasswordChangedAtUtc) shows Client "—" and the client filter drops it | audit rows; field change in P with entity PlatformUser | the client attribution of field changes covers Tenant / Wallet records only; the client administrator's user record is not linked to its tenant (C-18 fix scope) |
| C-12 | S4 | AUD-07 | Audit log filters | examples from the Super Admin portal (Tenant, Plan, Wallet, Role…) | "For example Treasury, Visit, SupportTicket" (client entities; SupportTicket is R2), also in Arabic | page text | shared hint key of the account audit page reused in the admin portal (`public/i18n/account.*.json`) |

Observations without a defect id (backlog)

- O-4 (carried, partly): clients CSV has a separate *Governorate* column now, but the previous column is still headed
  "Country · governorate" and holds the country only; mobiles and dial codes exported as `'+20…` (formula guard).
- O-6 (carried): a duplicate or zero sequence disables *Save (n)* with no text at the field.
- O-7 (carried): e-mail subjects are required but not marked `*`.
- O-8: the wallet CSV writes the date as raw ISO UTC with microseconds (`2026-10-11T19:25:11.556389+00:00`) and names the
  file with the short tenant id (`wallet-01a12c6a-…csv`).
- O-9: a refresh with the token of a session the administrator terminated is logged as `REFRESH_REUSE_DETECTED` (error);
  63 such events are in the ledger, which makes real token theft hard to spot.
- O-10: list tests: ticking *Default* silently turns the value into a non-printing placeholder (BR-19 by construction);
  *Normal* and *Critical* can both be ticked on one value.
- O-11: the laboratory's `labelCopies` is reported `isDefault: true` while it inherits 3 from the platform (default 1).
- O-d (carried): a session ended by a new sign-in is stored with `revocation_reason = Logout`; its event says
  `superseded_by_sign_in`.
- O-f (carried): the admin user editor and dialogs speak of "this laboratory" / "Every active branch of the laboratory";
  the plan dialog requires *Analysers*; the platform role matrix offers client and R2 modules (101 keys).
- The client creation toast says "the address was sent by SMS" although the lane has no gateway; the main administrator
  has no mobile, so cannot recover by OTP and cannot be reset by others (TEN-012). The admin users list contains an
  extra seeded "Platform Administrator" `nt.01a1222b719379109` (2026-10-09).
- Landing: the client-portal card "Branches, roles and users" promises "an audit trail of every change"; the client audit
  page `/account/audit` is Release 2 (the trail itself is recorded).

## 6. Previous defects (v0.3.8 run and its re-tests)

| Old id | Subject | Re-checked in | Status |
| --- | --- | --- | --- |
| C-01 | stale tab continued as another user | ISO-03 (broadcast dropped), ISO-01/02 | still fixed |
| C-02 | 204 answers read as failures (roles, users) | ROL-04…06, USR-05…09 | still fixed |
| C-03 | Add client mobile length per field | CLI-06, CLI-19 | still fixed |
| C-04 | exports ignored the filter | CLI-03, SES-02, AUD-02, CAT-01, CAT-23, GEO-05/07, TPL-05 | still fixed |
| C-05 | refused test band lost | CAT-06 | still fixed |
| C-06 | raw validator texts | GEO-01 (currency code localized under the field) | still fixed (dial code: C-09) |
| C-07 | inactive plans offered | CLI-09 | still fixed |
| C-08 | time zone free text | GEO-02 (IANA picker) | still fixed |
| C-09 | laboratory read the default parameter | PAR-03 | still fixed |
| C-10 | raw key after the language switch | PRF-03 | still fixed |
| C-11 | English parameter hints in Arabic | PAR-05 | still fixed |
| C-12 | landing R2 highlights | PUB-01/02 | still fixed |
| C-13 | previous session's data in the account feature | ISO-01…03 (full reload on every identity change) | still fixed |
| C-14 | /account pages in the admin shell | ISO-05 | still fixed |
| C-15 | admin URLs without permission | ISO-04/05 | still fixed |
| C-16 | two "Dashboard" entries | DSH, CAT-24 ("Home" / "Dashboard", "الرئيسية" / "لوحة المتابعة") | still fixed |
| C-17 | client creation absent from the platform trail | CLI-12 | still fixed |
| C-18 | no client column | AUD-01/02 | still fixed (client admin password reset not attributed: C-11) |
| C-19 | top-up rounded silently | SUB-02 | still fixed (plan fee: C-05) |
| R2-01 | must-change second tab | PRF-04 | still fixed for the other tab (own tab: C-07) |
| R2-02 | countries / governorates exports | GEO-05/07 | still fixed |
| R2-03 | Arabic report-template options | — | not re-run (page Arabic checked in CAT-24) |
| R2-04 | orphan session after a sign-in over an open sign-in page | ISO-02 | still fixed |
| R2-05 / R4-01 | R2 content on the landing / admin home | PUB-01/02, DSH-01 | still fixed |
| R4-02 | editable template form for a viewer | TPL-06 | fixed (row action label: C-10) |
| O-b | client URLs refused without the notice | ISO-05 | fixed |
| O-1, O-2, O-5 | wizard mobile text, recovery stale error, own-tab must-change banner | PUB-04, PUB-13, PRF-04 | reappeared as C-08, C-06, C-07 (never fixed) |

## 7. Data created (prefix V14, invented)

- Public: subscription request **V14 Wizard Lab** (Pending, never verified, expires 23:11); OTP challenges (subscription,
  password recovery ×2 for v14.user).
- Platform roles **V14 Role** (5 view keys, 1 member) and **V14 Role B** (inactive); platform user **v14.user** (V14 Role,
  "V14 tester (edited)", mobile +201014141421, password changed at first sign-in; locked / unlocked, locked out / unlocked,
  deactivated / reactivated, roles changed during the run; active, unlocked at the end).
- Client **V14 Validation Lab** (`v14-validation-lab`, Egypt · Cairo, UATPLAN, wallet 250.55 EGP from V14-INV-1;
  deactivated and reactivated, Active at the end); administrator **v14lab.admin** (temporary password, must change). Holds
  the copy of the global catalogue at 22:22.
- Plan **V14PLAN** (14.00 EGP, inactive). Country **V14 Land** (XW, +998, inactive) with governorate **V14 Province**
  (inactive). Notification template **v14-notice** (e-mail, inactive).
- Global catalogue (platform tenant): V14CG, V14ST + V14F, V14u/L, V14 Pregnant, V14AS (914), V14T (2 bands), V14L (list),
  V14SVC (915, seq 18), V14PREP, V14HEM, reason "V14 Clotted sample", Z99.V14, V14 Results Template, V14 Rule, V14SET,
  V14 Penicillin, V14 Augmentin, V14AMC, V14ECO, V14S (inactive). Laboratories created from now on receive the active ones.
- Restored: platform `labelCopies` 3 → 1; platform.admin language ar → en; no shared account locked, failed attempts 0;
  0 live sessions of my accounts at the end; a second-device session of uat.ops and one of uat.sup2 were terminated. The
  passwords of the V14 accounts are in the git-ignored `.local/dev-credentials.md`. The browser tabs I created were closed.

## 8. Not tested / blocked, and why

- Blocked by the missing SMS gateway: wizard completion and trial (PUB-08), recovery completion (PUB-15), the inactive
  template fallback text (TPL-07).
- Not tested: session timeout / screen lock (30 min idle); report-template logo and signature upload; test types Formula,
  Formatted text, Culture and Non-numeric, the Timing tab, and service types Profile / Package / Culture / Text report;
  subscription and usage debits of the wallet (no billing run); the Arabic option labels of the report-template dialog
  (R2-03); a deterministic reproduction of C-02 (it needs two refresh requests within the same round trip).
- The integration library (SA-15…SA-18) and the support desk are Release 2 and out of scope (their URLs were refused,
  ISO-05).
