# Validation 2026-10-11 — tester C · Super Admin portal and public pages

Release under test: **v0.3.8** (`VERSION`), SPA `http://localhost:4300` → validation API `127.0.0.1:5102`, database `lis`
on the private PostgreSQL `127.0.0.1:5433`. Tester: automated validation agent C (browser automation of the real pages;
every value entered through the UI; database read-only as `lis_owner`). No application code, dictionary, script or
migration was changed. Date: 2026-10-11, 10:20–11:10 (UTC+3).

References: SRS `docs/srs/src/srs.data.js` (SA-01…SA-21, WF-12, BR-25), intakes `LIS-INT-{PLT,ACC,CAT,CTB,ORG,FND}-001`,
UAT scripts v0.4, the dry run `docs/uat/results/2026-10-10_dry-run_super-admin.md` (D-12…D-18), `nav.registry.ts`.

## 1. Summary

| Executed | Passed | Failed | Blocked | Not tested |
| --- | --- | --- | --- | --- |
| **133** | **109** | **24** | **3** | **3** |

Defects: **19** (C-01…C-19) — S1: 1 · S2: 3 · S3: 9 · S4: 6.

Headlines

- **C-01 (S1)** — a tab whose session was ended keeps showing its user but silently continues **as whoever signed in
  last in the same browser**: the refresh cookie is shared, the interceptor refreshes and replays. Observed: the stale
  tab of `v11.user` sent a password change that the server executed for `platform.admin` (failed check counted against
  the main administrator, audited as "Platform Administrator").
- **C-02 (S2)** — every 204 answer of the organization API (roles and admin users) is read as a failure: the save
  happens in the database, but no confirmation appears and the *Reset password* dialog stays open (D-12 was fixed only in
  the platform API).
- **C-05 (S2)** — a test created with a refused reference band is saved **without** the band; the band the user typed is
  lost and the reason (`CAT-053`) is never shown.
- **C-13 (S2)** — the account feature (`/admin/audit`, `/account/profile`) keeps the previous session's data: after a
  sign-out / sign-in in the same tab, `uat.mdr` (no `audit.view`) saw the platform audit log, and `platform.admin` saw the
  laboratory and account of `v11lab.admin`.
- What works well: client provisioning and **the BR-25 copy** (all 21 catalogue tables copied, every cross-reference
  remapped to the new tenant's ids), deactivation → `CLI-020` at sign-in (D-13 confirmed fixed), lockout counter,
  OTP flows up to the code, audit chain verification, outbox (every event of this run processed, 0 attempts, no error),
  portal isolation of the client feature routes (D-17 fixed for those routes), the Arabic of every admin page.

## 2. Coverage

| Page (SRS) | Functions covered | Cases |
| --- | --- | --- |
| /welcome (CP-01) | content, links, Arabic | PUB-01, PUB-02 |
| /welcome/subscribe (CP-02) | required, formats, send code, wrong code, Arabic, completion | PUB-03…PUB-08 |
| /login (SA-01 / CP-03) | wrong password, lockout, locked refusal, Arabic | PUB-09…PUB-11 |
| /login/recover (CP-03) | start (known / unknown), code step, wrong code, completion | PUB-12…PUB-14 |
| Portal isolation | client → /admin, platform → client routes, /account routes, stale data, URL without permission, identity switch | ISO-01…ISO-06, PER-01 |
| /admin (SA-02) | brand, text, nav | DSH-01 |
| /admin/dashboard (SA-03) | tiles vs DB, periods, plan distribution, recent clients, Arabic | DSH-02…DSH-05 |
| Roles (SA-06) | required, no permission, create, edit general, edit permissions, members protection, deactivate, inactive role hidden, Arabic | ROL-01…ROL-09 |
| Admin users (SA-07) | required, formats, password rule, create, edit, lock / unlock, reset password, deactivate / reactivate, assign role, main administrator, Arabic | USR-01…USR-11 |
| Clients (SA-04) | list, filters, search, CSV, add (all validations), provisioning, BR-25 copy, audit, edit, plan list, reset admin password, deactivate / activate + `CLI-020`, open wallet, Arabic, first sign-in | CLI-01…CLI-21 |
| Subscriptions & wallets (SA-05) | wallet, top-up (validations, DB), transactions filter / CSV, plans CRUD, activate / deactivate, Arabic | SUB-01…SUB-10 |
| Sessions (SA-08) | list, terminate, effect on the terminated tab, export | SES-01…SES-04 |
| Audit log (SA-09) | entries, filters, detail, verify chain, CSV, client column, Arabic | AUD-01…AUD-07 |
| Master data SA-12/13/14 | 18 registers: create + validation + DB, edit, (de)activate, duplicates, search, CSV, test range bands, service members, sequence, linked services, Arabic | CAT-01…CAT-27 |
| Countries / Governorates (SA-10/11) | list, validations, time zone, edit, delete rule, governorate CRUD, inactive filter, Arabic | GEO-01…GEO-08 |
| Platform parameters (SA-19) | validation, save, inheritance, restore, Arabic | PAR-01…PAR-05 |
| Notification templates (SA-20) | list, placeholders, validations, create + preview, edit (e-mail subject), (de)activate, fallback, Arabic | TPL-01…TPL-07 |
| Profile (SA-21) | own data, password checks, language, must-change flow | PRF-01…PRF-04 |

## 3. Test cases

### 3.1 Public pages

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| PUB-01 | Open /welcome (EN), read sections and links | brand, features, two portals, R1 highlights, Subscribe / Sign in links | all present; links `/welcome/subscribe`, `/login`. "Release 1 highlights" lists *Sample tracking* and *contract pricing* (Release 2) | PASS (C-12) |
| PUB-02 | Switch to العربية | rtl, Arabic text | `dir=rtl`, `lang=ar`; only product names stay Latin (LIS, ASTM, PCR, EGP) | PASS |
| PUB-03 | Wizard step 1, *Send verification code* empty | 8 required fields flagged | 8 × "This field is required." | PASS |
| PUB-04 | mobile `12345`, manager `010123`, e-mail `not-an-email` | refused by country rule / e-mail format | "The value is not valid." ×2 (generic, the length hint disappears), "Enter a valid e-mail address." | PASS (obs. O-1) |
| PUB-05 | V11 Wizard Lab, Egypt · Cairo, mobiles `01011110011` / `+201011110012`, licence, e-mail → send | code step, masked mobile, request stored | "We sent a code to +201******012", resend countdown; row in `saas.subscription_requests` (Pending, normalised E.164), `identity.otp_challenges` purpose `subscription` | PASS |
| PUB-06 | code `12`, then `000000` | format error, then invalid code | "The value has the wrong format." / "The code is invalid or expired." | PASS |
| PUB-07 | Arabic on the code step | rtl + Arabic | "أدخل رمز التحقق…", "الرمز غير صحيح أو منتهي الصلاحية." | PASS |
| PUB-08 | Complete the wizard | administrator created, trial | no SMS gateway on the lane — the code cannot be read | BLOCKED (gateway) |
| PUB-09 | Sign in `v11.nobody` / wrong password | `AUTH-002` text | "The username or password is incorrect." | PASS |
| PUB-10 | `v11.user` wrong password ×6, then the right one | 5th failure locks (threshold 5, 30 min); right password refused | attempts 1–5: incorrect; 6th and right password: "The account is locked. Try again later."; DB `failed_password_attempts=5, is_locked_out=t`; events 5 × `SIGN_IN_FAILED wrong_password`, `USER_LOCKED_OUT`, `SIGN_IN_FAILED locked_out` | PASS |
| PUB-11 | Arabic sign-in with a wrong password | Arabic error, rtl | "اسم المستخدم أو كلمة المرور غير صحيحة." | PASS |
| PUB-12 | Recovery: empty, then `v11.user` → code `12a`, `000000` + new password | code step; wrong code refused at completion | "If an account matches, a 6-digit code was sent…"; `12a` → wrong format; `000000` → step 3, *Set new password* → "The code is invalid or expired." (401); events `PASSWORD_RECOVERY_STARTED`, `PASSWORD_RECOVERY_FAILED otp_rejected`; challenge attempts 1 | PASS |
| PUB-13 | Recovery for `v11.nobody.xyz` | same answer (no enumeration) | identical code step (202) — but the previous attempt's "The code is invalid or expired." is still shown | PASS (obs. O-2) |
| PUB-14 | Recovery completion with the real code | password changed | code not readable (no gateway) | BLOCKED (gateway) |

### 3.2 Portal isolation and sessions

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| ISO-01 | As `v11lab.admin`: `/admin/clients`, `/admin`, `/admin/catalogue/tests` | sent back to the client portal | each → `/account/dashboard` | PASS |
| ISO-02 | As `platform.admin`: `/registration/new`, `/catalogue/tests`, `/organization/users`, `/treasury/access`, `/patients`, `/results/worklist`, `/pricing/payers`, `/billing/payments` | sent to `/admin` | each → `/admin` (D-17 fixed for these routes) | PASS |
| ISO-03 | As `platform.admin`: `/account/dashboard`, `/account/wallet`, `/account/support`, `/account/audit`, `/account/profile` | not available in the Super Admin portal | all open inside the admin shell; dashboard "could not be loaded" with the platform's own "Recent activity"; wallet "could not be loaded" | FAIL (C-14) |
| ISO-04 | Same tab: sign out `v11lab.admin`, sign in `platform.admin`, open `/account/profile`; sign out, sign in `uat.mdr`, open `/admin/audit` | each page shows the signed-in user's data | `/account/profile` shows **V11 Validation Lab** and `v11lab.admin`'s account; `uat.mdr` sees the full platform audit list and "chain intact: 59 entries" without any request (no `audit.view`) | FAIL (C-13) |
| ISO-05 | As `uat.mdr` type `/admin/clients`, `/admin/organization/users`, `/admin/parameters`, `/admin/audit` | page refused (route permission) | pages open; API answers **403** for each (`/platform/clients`, `/platform/plans`, `/organization/users`, `/settings` …) and the page shows "could not be loaded" | FAIL (C-15) — server side PASS |
| ISO-06 | Tab A signed in as `v11.user` (must change password); tab B signs in `platform.admin`; B terminates A's session; A clicks *Update password* | A is signed out | A silently refreshed with B's cookie and replayed the request **as platform.admin**: "The current password is not correct.", `platform.admin.failed_password_attempts` 0 → 1, `PASSWORD_CHANGE_FAILED` with B's user id, audit entry "Platform Administrator · PlatformUser FailedPasswordAttempts 0 → 1" | FAIL (C-01) |
| PER-01 | `uat.mdr` menu | only granted modules | Dashboard, Sessions, master data, microbiology, Profile (no Clients, Users, Audit, Parameters) | PASS |

### 3.3 Home and dashboard (SA-02, SA-03)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| DSH-01 | /admin | brand + text pointing to the dashboard | "Platform overview", "Signed in as Platform Administrator", D-18 text; Overview group shows **two "Dashboard" entries** | PASS (C-16) |
| DSH-02 | /admin/dashboard (month) vs DB | counts equal the database | Clients 3/3 active, Admin users 5, Served 5, Tests 23, Registrations 16, Wallets ≤ 0: 2 — equal to the DB (see D-01…D-03); after the run: Clients 7, Admin users 6, Wallets ≤ 0: 5 (V11 lab topped up) | PASS |
| DSH-03 | Period Today / Week / Year / Month | per-period counts | today 0/0/0; week, year, month 5/23/16 (all activity this week) | PASS |
| DSH-04 | Arabic | rtl, Arabic labels, Arabic plan names | "لوحة المتابعة", "الباقة الأساسية" | PASS |
| DSH-05 | Registrations by client | client names | demo laboratory still shows its raw tenant id (no client record; known S4 from the dry run) | PASS (known obs.) |

### 3.4 Roles (SA-06) and Admin users (SA-07)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| ROL-01 | *Create role* empty | names required | 2 × required | PASS |
| ROL-02 | names, no permission | refused before sending | toast "Grant at least one permission before creating the role." | PASS |
| ROL-03 | V11 Role / دور التحقق, description, permissions | "Role created.", row in `app.roles` | created; DB row platform tenant (see D-04) | PASS |
| ROL-04 | Screen rights: add Platform administration View + Catalogue View, *Save* | saved with confirmation | DB `{audit.view,catalogue.view,platform.view}`, `PRIVILEGE_MATRIX_CHANGED`, field change; **no toast** (PUT 204) | FAIL (C-02) |
| ROL-05 | General: description edited, *Save* | "Saved." | DB updated, audit field change; **no toast** | FAIL (C-02) |
| ROL-06 | Deactivate a role with members | refused | button disabled, title "A role with members cannot be deactivated." (all four roles) | PASS |
| ROL-07 | V11 Role B (no members) → Deactivate → Confirm | "Deactivated." | row Inactive, DB `is_active=f`; **no toast** | FAIL (C-02) |
| ROL-08 | Add user → role list | inactive role absent | V11 Role B absent (form and list filter) | PASS |
| ROL-09 | Arabic | rtl | "الأدوار والصلاحيات" | PASS |
| USR-01 | *Create user* empty | 6 required | username, password, confirm, names EN/AR, e-mail | PASS |
| USR-02 | `ab`, `short1`, confirm differs, `bad` e-mail, mobile `123`, 32-char username | format errors | wrong format / at least 8 / wrong format (mismatch) / e-mail / mobile / "At most 30 characters." | PASS (obs. O-3) |
| USR-03 | password `v11.user-Pass1` (contains username) | refused | page problem "The password must not contain the username." | PASS |
| USR-04 | `v11.user`, V11 Role, designation, shifts, mobile, must-change off | user + membership + role | "User created."; DB user (argon2id hash), membership platform tenant, employee code V11.USER, shifts `{Morning,Night}`; audit `TenantMembershipCreated`, `TenantMembershipRoleChanged`, field changes; outbox processed | PASS |
| USR-05 | Profile: designation, mobile → *Save* | "Saved." | DB updated (`Designation`, `Mobile` field changes); **no toast** | FAIL (C-02) |
| USR-06 | Security: *Unlock* (after PUB-10), *Lock*, *Unlock* | status changes + confirmation | confirm dialogs, status Locked / Active, DB `is_locked_out`; `MEMBERSHIP_CHANGED …:unlocked`; **no toast** | FAIL (C-02) |
| USR-07 | *Reset password*: mismatch, then valid | dialog closes, "done" toast | mismatch → "The two passwords differ."; valid → POST 204, DB `must_change_password=t`, event `…:password-reset`, but **the dialog stays open, no toast** (a user would reset twice) | FAIL (C-02) |
| USR-08 | *Deactivate* → Confirm, *Activate* → Confirm | status + toast | Inactive / Active, DB `is_active`, audit `TenantMembershipDeactivated/Reactivated`; **no toast** | FAIL (C-02) |
| USR-09 | Role & branches: Master Data Reviewer → *Assign role* → Confirm | role changed + toast | DB role = Master Data Reviewer; **no toast** | FAIL (C-02) |
| USR-10 | `platform.admin` opens own record | main administrator protected | Security tab without actions, role select disabled ("The main administrator keeps the administrator role."), list badge "Main administrator" | PASS |
| USR-11 | Arabic | rtl | "مستخدمو الإدارة" | PASS |

### 3.5 Clients (SA-04)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| CLI-01 | List | SA-04 columns | client, portal, country · governorate, manager + mobile, plan, wallet, branches, users, created, source, status, actions | PASS |
| CLI-02 | Search `V11`, `uatlabb`; Source = Subscription; Plan = UATPLAN; Status = Inactive | filtered rows | 4 V11 rows; UAT Lab B (by slug); "No clients match" ×3 (correct at that time) | PASS |
| CLI-03 | Search "V11 Validation" → *Export CSV* | filtered CSV | `clients-2026-10-11.csv`, 1 data row; header "Country · governorate" over a country-only column, mobiles prefixed with `'` | PASS (obs. O-4) |
| CLI-04 | *Create client* empty | 12 required | 12 × required | PASS |
| CLI-05 | mobile `0101111`, e-mail `x@`, slug `Bad Slug!`, username `AB`, password containing it, confirm differs | inline errors | each field flagged ("The password must not contain the username.", "The passwords do not match.") | PASS |
| CLI-06 | lab mobile `0233351111` (landline), manager `+2010111100` (8 digits) | refused per field (`GEO-010`) | no client-side check; server message "The mobile number does not have the length of the selected country." shown once at the top, **without saying which field**; kept after the lab mobile was fixed | FAIL (C-03) |
| CLI-07 | slug `uatlabb` | `CLI-100` | "This portal address is already taken." | PASS |
| CLI-08 | username `labb.admin` | refused | "This username is already used." | PASS |
| CLI-09 | V11 Validation Lab, Giza, Arabic messages, STARTER, empty slug, `v11lab.admin` | provisioning | "The client was created. Portal https://v11-validation-lab.skylis.app …"; tenant, wallet 0.00 EGP, branch MAIN, user + membership (primary administrator) + role "Laboratory Administrator" (93 keys) — D-05 | PASS |
| CLI-10 | BR-25 copy (V11 global items created first) | every active global item copied, references remapped | 21 tables: counts equal platform → tenant; preparation / diagnosis / rule / shortcut-set service ids, comment group, test group / unit / sample / condition, band condition, service members, antibiotic → commercial name → drug type all point to **the new tenant's** rows — D-06 | PASS |
| CLI-11 | inactive global item (sensitivity value V11S) | not copied | not copied (0) | PASS |
| CLI-12 | Platform audit of the creation | entry in the platform trail (SA-09) | `TenantProvisioned` + field changes stamped with the **new** tenant; platform trail has none (D-15 still open); `TENANT_PROVISIONED` security event; outbox processed | FAIL (C-17) |
| CLI-13 | *Edit client*: governorate Cairo, address, plan UATPLAN | saved; slug read-only | "The client was saved."; slug read-only; DB updated; platform trail `TenantPlanChanged`, field change in platform tenant | PASS |
| CLI-14 | Deactivate plan V11PLAN, open *Add client* | inactive plan not offered | V11PLAN offered in Add client and in the filter (server would refuse `PlanInactive`) | FAIL (C-07) |
| CLI-15 | *Reset administrator password*: short + mismatch, contains username, valid | validations, reset | "At least 8 characters." / "The passwords do not match." / "must not contain the username"; "The administrator password of V11 Validation Lab was reset."; DB `must_change_password=t`; `CLIENT_ADMIN_PASSWORD_RESET` | PASS |
| CLI-16 | *Deactivate client* without reason | refused | "A reason is required.", dialog stays | PASS |
| CLI-17 | reason "V11 validation deactivation" → sign in `v11lab.admin` | Inactive; `CLI-020` | "The client is inactive; its users are blocked."; sign-in: "This laboratory's subscription is inactive. Contact National Technology." (D-13 fixed); `TENANT_DEACTIVATED`, `SIGN_IN_FAILED client_inactive`, trail `TenantDeactivated` with the reason | PASS |
| CLI-18 | *Activate client* | Active | "The client is active."; `TENANT_ACTIVATED`, trail `TenantActivated` | PASS |
| CLI-19 | *Open wallet* | wallet of that client | `/admin/subscriptions?client=…`, V11 Validation Lab selected | PASS |
| CLI-20 | Arabic | rtl | "العملاء" | PASS |
| CLI-21 | `v11lab.admin` first sign-in | forced password change | `/account/profile?mustChange=1` → "Your password was changed."; banner and query stay until the next navigation | PASS (obs. O-5) |

### 3.6 Subscriptions & wallets (SA-05)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| SUB-01 | Client wallets, V11 Validation Lab | balance, fee, next charge, SMS | 0.00 EGP "Metered services are paused.", 999.00 EGP UATPLAN, next charge 11/11/2026, SMS 0 / 100 | PASS |
| SUB-02 | Top-up empty; amount 0 | required; ≥ 0.01 | "This field is required." ×2; "The value must be at least 0.01." | PASS |
| SUB-03 | 250.555, V11-INV-1, note | balance and transaction | "The wallet was topped up."; balance **250.56** (3rd decimal silently rounded); `saas.wallet_transactions` TopUp 250.56; trail `WalletToppedUp` | PASS (C-19) |
| SUB-04 | Type = Usage / Top-up; *Export CSV* | filter + CSV | Usage: none; Top-up: 1; CSV 1 row | PASS |
| SUB-05 | Plans tab | fee, limits, client count | 4 plans with counts | PASS |
| SUB-06 | code `v11 plan`, currency `EGPX`, fee −1, branches 0 | refused | wrong format / at most 3 / ≥ 0 / ≥ 1 | PASS |
| SUB-07 | V11PLAN, EGP 111.5, 2 / 11 / 111 / 1 | saved | "The plan was saved."; DB row | PASS |
| SUB-08 | Edit fee 222; code read-only | saved | read-only code; 222.00 | PASS |
| SUB-09 | Deactivate / Activate | toasts, status | "The plan is inactive; clients keep it, new clients cannot get it." / "The plan is active." | PASS |
| SUB-10 | Arabic | rtl | "الاشتراكات والمحافظ" | PASS |

### 3.7 Sessions (SA-08) and Audit log (SA-09)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| SES-01 | Sessions list | own session | "This session", IP, since, last seen, Active | PASS |
| SES-02 | Terminate `v11.user`'s session | removed + event | confirm "Terminate the session of V11 Validation User from 127.0.0.1?"; "The session was terminated."; `SESSION_TERMINATED_BY_ADMIN` | PASS |
| SES-03 | The terminated tab acts | signed out | acts as another user (ISO-06) | FAIL (C-01) |
| SES-04 | Export CSV | — | not executed | NOT TESTED |
| AUD-01 | Entries after the run | role, user, plan, wallet, tenant, country changes | listed with before → after | PASS |
| AUD-02 | Entity = Plan; user = v11.user; from = tomorrow; Reset | filtered | 7 Plan rows; v11.user none (made no change); none; reset | PASS |
| AUD-03 | Open an entry | detail | time, user, action, entity, id, correlation id, field table, "Show the history of this record" | PASS |
| AUD-04 | *Verify chain* | intact | "The audit chain is intact: 59 entries checked." = 59 rows, max sequence 59 in DB | PASS |
| AUD-05 | Entity = Plan → *Export CSV* | filtered CSV | `audit-log-…csv`, 8 lines (`/account/audit/export?entityType=Plan`) | PASS |
| AUD-06 | Columns | SA-09: time, user, action, entity, details, **client** | no client column | FAIL (C-18) |
| AUD-07 | Arabic | rtl | "سجل التدقيق" | PASS |

### 3.8 Master data (SA-12, SA-13, SA-14)

All created as `platform.admin` in the platform tenant; DB rows checked in D-07.

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| CAT-01 | Clinical groups: empty; code CHEM (dup); order −1; V11CG; edit name/order; code read-only; deactivate; *All*; reactivate; search | rules + DB | required ×4; "A clinical group with this code already exists."; "at least 0"; "Saved."; code disabled on edit; "Deactivated." / "Activated."; search V11 → 1 row | PASS |
| CAT-02 | Search V11 → *Export CSV* | filtered export | CSV holds CHEM **and** V11CG (filter ignored) | FAIL (C-04) |
| CAT-03 | Sample types: required ×4; V11ST; *Conditions* → FAST | saved | "Saved."; "The conditions were saved."; `app.sample_conditions` FAST | PASS |
| CAT-04 | Units: `MIU/L` (dup of mIU/L), `V11u/L` | case-insensitive unique | "A unit with this name already exists."; saved | PASS |
| CAT-05 | Patient conditions: required; V11 Pregnant | saved | PASS | PASS |
| CAT-06 | Additional services: shortcut −5; 911; orderable | ≥ 1; saved | "The value must be at least 1."; row Yes / Active; DB `type=Additional` | PASS |
| CAT-07 | Tests › Add: required; V11T Numeric, V11CG, V11u/L, 1 decimal, V11ST, FAST, methodology, LOINC, prefix | created | DB row with every reference | PASS |
| CAT-08 | Same creation with a band Low 110 > High 70 | refused with the reason, input kept | toast "The test was created; part of its configuration was refused — see the highlighted tab."; PUT range-bands **422 `CAT-053`**; page navigates to the new id: **0 bands, no highlight, no message, the typed band is gone** | FAIL (C-05) |
| CAT-09 | Add bands Any 0–120 y 70–110 (panic 40/400, reportable 0/1000, text) and Female · V11 Pregnant 60–95 | saved | "Saved."; tab "Reference ranges 2"; DB D-07 | PASS |
| CAT-10 | Services › Add: required ×6; V11SVC Test, shortcut 911 (taken), member V11T; shortcut 912 | uniqueness; created | "This shortcut number is used by another service."; "The service was created."; member row | PASS |
| CAT-11 | Service sequence: V11SVC 15; TSH 15 (duplicate) | saved; duplicate refused | "1 sequence(s) saved."; duplicates outlined red and *Save* disabled, no text | PASS (obs. O-6) |
| CAT-12 | Preparations: required; code `v11 bad`; option list with one distinct value; V11PREP, 3 options, linked V11SVC | rules; saved | code format; "An option list needs at least two different options."; "Preparation saved."; DB `service_ids` | PASS |
| CAT-13 | Comment library: required ×3; v11hem, V11CG | saved, short name upper-cased | "Comment saved."; V11HEM | PASS |
| CAT-14 | Reasons: required ×3; Sample rejection | saved | "Reason saved."; DB kind `Rejection` | PASS |
| CAT-15 | Diagnoses: required; code `not icd!`; Z99.V11; edit → services added V11SVC | saved | format error; "Diagnosis saved." ×2; list "V11SVC" | PASS |
| CAT-16 | Report templates: required ×3; header EN only; both headers + footers + QR | bilingual header rule | refused; saved "Template saved." (DB `language=Both`, `show_qr=t`) | PASS |
| CAT-17 | Server validation texts (report template, country) | translated messages | raw validator texts: "'Header Text Ar' must not be empty.", "'Currency Code' must be 3 characters in length. You entered 2 characters." | FAIL (C-06) |
| CAT-18 | Test rules: required ×4; V11 Rule, trigger V11SVC → adds V11AS; edit: V11SVC as added too | saved; overlap refused | saved; "A service cannot be both a trigger and an added service." | PASS |
| CAT-19 | Shortcut sets: required ×4; V11SET = V11SVC + TSH | saved | "Shortcut set saved." | PASS |
| CAT-20 | Drug types: required ×2; V11 Penicillin | saved | PASS | PASS |
| CAT-21 | Commercial names: required ×3; V11 Augmentin → V11 Penicillin | saved | PASS | PASS |
| CAT-22 | Antibiotics: required ×4; V11AMC, V11A, → V11 Augmentin | saved | PASS | PASS |
| CAT-23 | Organisms: required ×3; V11ECO | saved | PASS | PASS |
| CAT-24 | Sensitivity values: V11S; duplicate V11S; edit; deactivate | rules | "A record with this code already exists in this list."; saved; "Deactivated." | PASS |
| CAT-25 | Arabic on the 25 master-data, microbiology and geography pages | rtl, Arabic chrome | every page rtl with an Arabic title and no English chrome | PASS |
| CAT-26 | Test types other than Numeric (lists, formula, text, culture), Timing tab, service types Profile / Package / Culture / Text report | — | not executed (time) | NOT TESTED |
| CAT-27 | Report template logo / signature upload | — | not executed (file upload) | NOT TESTED |

### 3.9 Countries and governorates (SA-10, SA-11)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| GEO-01 | List | SA-10 columns | flag + name, ISO, dial code, currency, mobile digits, time zone, governorates count | PASS |
| GEO-02 | required (7); ISO `XVV`, dial `999`, currency code `V1`, mobile length 40 | refused | "At most 2 characters.", "The value must be at most 15.", "The dial code is '+' followed by one to four digits, like +20.", currency message (raw, C-06) | PASS |
| GEO-03 | time zone `Mars/Base` | IANA name refused | **saved** ("The country was saved."; DB `time_zone = Mars/Base`) | FAIL (C-08) |
| GEO-04 | Edit V11 Land: time zone Africa/Cairo; ISO read-only | saved | PASS | PASS |
| GEO-05 | Delete Egypt / V11 Land (referenced) / V11 Temp (free) | only an unreferenced country can be deleted | *Delete* absent for Egypt and (after GEO-06) V11 Land; V11 Temp: "Delete V11 Temp? Only a country without governorates and clients can be deleted." → "The country was deleted."; DB 0 rows, field change `Deleted` | PASS |
| GEO-06 | Governorates: required ×3; V11 Province in V11 Land; filter by country; edit district; deactivate | saved | "The governorate was saved."; "Deactivated." | PASS |
| GEO-07 | Add client → V11 Land | inactive governorate not offered | list empty | PASS |
| GEO-08 | Arabic | rtl | "الدول", "المحافظات" | PASS |

### 3.10 Platform parameters (SA-19), notification templates (SA-20), profile (SA-21)

| Id | Steps | Expected | Observed | Verdict |
| --- | --- | --- | --- | --- |
| PAR-01 | One-time code length 3 | refused | "The value must be at least 4."; nothing saved | PASS |
| PAR-02 | Label copies 1 → 2, *Save* | saved | "1 parameter(s) saved."; `app.tenant_settings` platform `labelCopies=2` | PASS |
| PAR-03 | `v11lab.admin` (never set the key): `/organization/parameters` | reads the platform value 2 | page and `GET /api/v1/settings` show `labelCopies` **1**, `isDefault: true` | FAIL (C-09) |
| PAR-04 | *Use the default* → *Save* | restored | "1 parameter(s) saved."; value 1 (the row stays, holding the default) | PASS |
| PAR-05 | Arabic | Arabic hints | 10 hints in English (server descriptions), e.g. "The patient mobile is required at registration. الافتراضي: لا." | FAIL (C-11) |
| TPL-01 | List and placeholders | codes, channel, text, status; 14 placeholders | otp, panic-value, password-recovery, portal-url, treasury-request; placeholder chips | PASS |
| TPL-02 | required ×3; code `V11 Bad`; text with `{bogus}` | refused | "The value has the wrong format."; "The text uses a placeholder the platform does not know." | PASS |
| TPL-03 | v11-notice SMS EN/AR with {lab} {code} {minutes} | preview + saved | preview with sample values (Al Noor Lab, 482913, 5); "The template was saved." | PASS |
| TPL-04 | Edit → E-mail without subjects; with subjects | subjects required | "An e-mail template needs a subject in both languages." (fields not marked `*`); saved, DB `Email`, subjects | PASS (obs. O-7) |
| TPL-05 | Deactivate ("The built-in text is sent instead.") / Activate | status | "Deactivated." / "Activated."; DB `is_active` | PASS |
| TPL-06 | Inactive template falls back to the built-in text | built-in text sent | no gateway; the sent text cannot be observed (shared codes such as `otp` were not deactivated) | BLOCKED |
| TPL-07 | Arabic | rtl | "قوالب الإشعارات" | PASS |
| PRF-01 | /admin/profile | own account | Platform Administrator, platform.admin, NT-PLATFORM, e-mail; details read-only ("ask your administrator") | PASS |
| PRF-02 | new password containing the username, mismatch | checks shown | "Does not contain your username" and "Both new passwords match" unticked | PASS |
| PRF-03 | Language العربية → *Save language*, back to English | saved with a localized toast | page switches to rtl, toast reads the raw key **`account.profile.languageSaved`**; back to English: "Your language was saved." | FAIL (C-10) |
| PRF-04 | Must-change flow (`v11lab.admin`) | password changed, then free navigation | see CLI-21 | PASS |

## 4. Database verification

Queries run as `lis_owner` (`psql -At`); `P` = platform tenant `00000000-0000-0000-0000-000000000001`, `T` = V11
Validation Lab `01a129eb-e15f-74ca-8f54-e7d14ee7c793`.

| Ref | Case | Table | Query | Result |
| --- | --- | --- | --- | --- |
| D-01 | DSH-02 | saas.tenants / saas.wallets | `select name_en,status,source from saas.tenants`; `select tenant_id,balance from saas.wallets` | 3 tenants Active/Manual; balances 0.00 / 500.00 / 0.00 → 2 at or below zero |
| D-02 | DSH-02 | app.tenant_memberships | `select count(*) … where tenant_id=P` | 5 (6 after USR-04) |
| D-03 | DSH-02/03 | app.visits, app.results | distinct patients, visits, non-cancelled results with `registered_at_utc >= month start` | 5 / 16 / 23; today 0 |
| D-04 | ROL-03/04/05 | app.roles | `select tenant_id,name_en,name_ar,description,permission_keys,is_active from app.roles where name_en like 'V11%'` | P · V11 Role · دور التحقق · "V11 validation role (edited)" · `{audit.view,catalogue.view,platform.view}` · t; V11 Role B · f |
| D-05 | USR-04…09 | identity.platform_users, app.tenant_memberships | user, membership, role join for `v11.user` | e-mail, mobile `+201011110021`, argon2id, `must_change_password=t` (after reset), membership P, V11.USER, `{Morning,Night}`, role Master Data Reviewer; lock 5 → 0 / f after unlock |
| D-06 | USR-* | audit.field_changes | rows of the last 15 min, platform tenant | PlatformUser Created / Modified (FailedPasswordAttempts, IsLockedOut, MustChangePassword, Mobile), TenantMembership Created / Modified (IsActive, Designation), Role Created / Modified |
| D-07 | CAT-* | app.* (21 tables) | per table `… where tenant_id=P` | V11CG/V/order 12; V11ST + FAST; V11u/L; V11 Pregnant; V11AS Additional 911; V11T (V11CG, V11u/L, V11ST, FAST, 1 dec, LOINC 2345-7, prefix S.); bands Any 0–120 y 70–110 / 40–400 / 0–1000 "70 - 110" and Female · V11 Pregnant 60–95; V11SVC (V11GLU, 912, 24 h, aliases, precaution, sequence 15) + member V11T; V11PREP OptionList `{8 h,12 h,Not fasting}` → V11SVC; V11HEM → V11CG; Rejection reason; Z99.V11 → V11SVC; report template Both / QR / headers / footers; rule V11SVC ⇒ V11AS; set {TSH, V11SVC}; drug type → commercial name → antibiotic V11AMC/V11A; V11ECO; V11S inactive |
| D-08 | CLI-09 | saas.tenants | tenant row joined to country, governorate, plan | V11 Validation Lab · معمل التحقق 11 · Egypt · Giza · +201011110031 · V11-LIC-031 · V11 Manager · +201011110032 · STARTER · `v11-validation-lab` · Manual · Active · `v11lab.admin` · ar |
| D-09 | CLI-09 | saas.wallets, app.branches, app.tenant_memberships, app.roles | `… where tenant_id=T` | wallet 0.00 EGP, next charge 2026-11-11; branch MAIN (names = lab names); `v11lab.admin` primary administrator, active, role Laboratory Administrator (93 keys); 1 role; no tenant setting rows |
| D-10 | CLI-10/11 | 21 catalogue tables | `count(*)` platform vs tenant | 2/2, 2/2, 1/1, 2/2, 1/1, 3/3, 2/2, 2/2, 3/3, 1/1 ×10; sensitivity values 1/0 (inactive) |
| D-11 | CLI-10 | references | e.g. `select (select string_agg(s.code||'@'||(s.tenant_id=T),',') from app.services s where s.id=any(p.service_ids)) from app.preparations p where p.tenant_id=T` | every reference `…@true` (preparation, diagnosis, rule, shortcut set, comment group, test group/unit/sample/condition, band condition, service members, antibiotic → commercial name → drug type) |
| D-12 | CLI-12 | audit.audit_trail, audit.security_events, ops.outbox_events | events of the last 10 min for T and P | T: `TenantProvisioned` seq 1 + catalogue copy events; P: none for the creation; `TENANT_PROVISIONED` (tenant null); outbox: all processed, 0 attempts |
| D-13 | CLI-13 | saas.tenants, audit.field_changes | tenant row; field changes on its id | Cairo / "12 V11 Street, Nasr City" / UATPLAN; Modified row in P (Address, GovernorateId, PlanId); trail P `TenantPlanChanged` seq 56 |
| D-14 | CLI-15…18 | identity.platform_users, saas.tenants, audit.* | | `must_change_password=t`; `CLIENT_ADMIN_PASSWORD_RESET`; `TENANT_DEACTIVATED sessions:0`, `SIGN_IN_FAILED client_inactive`, `TENANT_ACTIVATED`; trail P seq 57 `TenantDeactivated` (reason), 58 `TenantActivated`; status Active, reason cleared |
| D-15 | SUB-03 | saas.wallets, saas.wallet_transactions | balance; transactions joined to the wallet | 250.56; TopUp 250.56 / after 250.56 / V11-INV-1 / note; trail P `WalletToppedUp` |
| D-16 | SUB-07…09 | saas.plans | `… where code='V11PLAN'` | V11 Plan · باقة التحقق · EGP · 222.00 · 2/11/111/1 · active |
| D-17 | GEO-* | saas.countries, saas.governorates | | V11 Land XV +999 XVD en Africa/Cairo (was Mars/Base) 9; V11 Province / V11 District 2 / inactive; V11 Temp 0 rows |
| D-18 | PAR-02/04 | app.tenant_settings | `… where tenant_id=P` | labelCopies 2 → 1; passwordMinLength 8; trialDays 14 |
| D-19 | TPL-* | saas.notification_templates | `… where code='v11-notice'` | Email, subjects EN/AR, text "V11 notice (edited) for {labName}: {url}", active |
| D-20 | PUB-05, PUB-12 | saas.subscription_requests, identity.otp_challenges | latest rows | V11 Wizard Lab Pending, mobiles E.164, expires +1 h; challenges `subscription` (attempts 0→1), `password-recovery` (attempts 1, not consumed) |
| D-21 | PUB-10, ISO-06 | audit.security_events | last minutes | 5 × `SIGN_IN_FAILED wrong_password`, `USER_LOCKED_OUT`, `locked_out` ×2; `SESSION_TERMINATED_BY_ADMIN`; `PASSWORD_CHANGE_FAILED` **actor = platform.admin** |
| D-22 | AUD-04 | audit.audit_trail | `select count(*),max(sequence) … where tenant_id=P` | 59 / 59 = "59 entries checked" |
| D-23 | all | ops.outbox_events | `select processed_at_utc is not null, dead_lettered_at_utc is not null, count(*), max(attempts) … group by 1,2` | 957 processed, 0 pending, 0 dead-lettered |

## 5. Defect log

| Id | Sev | Case | Steps | Expected | Observed | Evidence | Suspected root cause |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C-01 | **S1** | ISO-06, SES-03 | In one browser: tab A signed in as user X; tab B signs in as user Y (or X's session is terminated / expires and Y signs in elsewhere); tab A acts | A is signed out (or refused) | A refreshes with Y's refresh cookie, replays the request **as Y**, and keeps showing X's name, data and permissions; any save in A is done and audited as Y (also across laboratories: a lab B tab would write into lab C after labc.admin signs in) | D-21: `PASSWORD_CHANGE_FAILED` actor `01a12231…` (platform.admin) from v11.user's tab; platform audit "Platform Administrator · FailedPasswordAttempts 0 → 1" | `core/http/auth.interceptor.ts` (401 → `store.refresh()` → replay) and `core/auth/auth.store.ts` `refresh()` replace the profile with whatever the shared cookie returns and never compare user id / tenant with the profile the page was built for; a changed identity should clear the state and route to sign-in |
| C-02 | S2 | ROL-04/05/07, USR-05…09 | Save any role / admin-user change that answers 204 | success toast; dialogs close | change saved, no toast; *Reset password* dialog stays open; *Lock*, *Unlock*, *Deactivate*, *Activate*, *Assign role*, *Save* silent (same pages in the client portal) | network: PUT/POST … → 204, no toast; DB rows updated (D-04, D-05) | `features/organization/data/organization.api.ts`: 11 calls `firstValueFrom(this.http.put/post<void>(…), { defaultValue: undefined })` resolve `null` for a 204 and `mutation().execute()` returns `null` = failure (D-12 fixed only `platform.api.ts`). Same pattern without `.then(() => undefined)`: `pricing.api.ts` (11), `results.api.ts` (6), `patients.api.ts` (3), `samples.api.ts` (2), `integration.api.ts` (2), `core/auth/auth.store.ts` (1) — for the testers of those modules |
| C-03 | S3 | CLI-06 | Add client with a landline or an 8-digit manager mobile (Egypt) | field error per mobile (`GEO-010`) before sending | no browser check; one server message at the top, no field named | screenshot text "The mobile number does not have the length of the selected country." | `features/platform/pages/clients.page.ts` uses the generic `mobileValidator` instead of the country-length rule the wizard has (`public/data/subscription-rules.ts`); `GEO-010` not mapped to a control |
| C-04 | S3 | CAT-02 | Clinical groups, search `V11`, *Export CSV* | the filtered list (the exporter's own comment says "full filtered list") | every row exported | CSV 2 data rows vs 1 on screen | `catalogue/pages-a/*.page.ts` `exportCsv` calls `api.export…()` without the list filter (search, status); check the other catalogue-A/B exporters |
| C-05 | S2 | CAT-08 | Add test with a band whose low > high, *Save* | nothing saved, or the test saved with the refused band kept and the reason shown on the highlighted tab | test created **without** bands; navigates to the new id; band lost; no message, no highlight | PUT `/range-bands` 422 `CAT-053` "Band 1: limits must read reportable low ≤ panic low ≤ low ≤ high ≤ panic high ≤ reportable high." | `catalogue/pages-a/test-editor.page.ts` `createAll()` (lines ~460–494) runs the steps, then always `router.navigate(['..', created.id])`; the new component instance drops the form and the step problem |
| C-06 | S4 | CAT-17, GEO-02 | Report template with one header; country currency code of 2 letters | translated message | raw validator text in English ("'Header Text Ar' must not be empty.", "'Currency Code' must be 3 characters in length. You entered 2 characters.") | page problem text | server validators without `WithMessage`/code (report-template and country commands); the page shows `detail` verbatim |
| C-07 | S3 | CLI-14 | Deactivate a plan, open *Add client* / *Edit client* | inactive plan not offered | offered (server refuses with `PlanInactive`) | dropdown lists "V11PLAN · V11 Plan" while Inactive | `features/platform/state/clients.facade.ts` `plans` loads `listPlans(null, …)` (all plans) |
| C-08 | S3 | GEO-03 | Country time zone `Mars/Base` | refused (IANA name, SA-10) | saved | D-17 `time_zone = Mars/Base` before the correction | `Platform.Domain/PlatformAdmin/Country.cs` checks only the length (`MaxTimeZoneLength`); no `TimeZoneInfo.TryFindSystemTimeZoneById` check |
| C-09 | S3 | PAR-03 | Platform `labelCopies = 2`; laboratory that never set it reads its parameters | 2 (platform value, D-16 rule) | 1, `isDefault: true` | `GET /api/v1/settings` body | `Platform.Application/Settings/Queries/GetTenantSettingsQuery.cs` `Describe` falls back to `descriptor.DefaultValue`, not to the platform tenant's row as `TenantSettings` does since D-16 |
| C-10 | S4 | PRF-03 | Profile → العربية → *Save language* | "تم حفظ اللغة." | toast `account.profile.languageSaved` | toast text | `features/account/pages/profile.page.ts`: the toast is translated right after the language switch, before the Arabic `account` dictionary is loaded |
| C-11 | S4 | PAR-05 | Platform parameters in Arabic | Arabic hints | 10 English hints (mandatory mobile/doctor/national id, print labels/job order/receipt, label copies, report language, auto-collect, review before verify) | page text | `features/platform/pages/parameters.page.ts:111` falls back to the server `description`; `public/i18n/platform.{ar,en}.json` lack the hint keys for those settings |
| C-12 | S4 | PUB-01 | /welcome "Release 1 highlights" | only R1 features | lists *Sample tracking* (Samples module = R2) and *contract pricing* (R2) | landing text | `features/public/pages/landing.page.html` / public dictionary |
| C-13 | S2 | ISO-04 | Same tab: sign out, sign in as another user, open `/account/profile` or `/admin/audit` | the new user's data (or a refusal) | previous session's data: V11 Validation Lab + `v11lab.admin` shown to `platform.admin`; the platform audit log shown to `uat.mdr` (no `audit.view`) without any request | page texts; network shows no `/audit/entries` call for uat.mdr | `features/account/account.routes.ts` route-provided facades (`ProfileFacade`, `AuditLogFacade`, `DashboardFacade`, `WalletFacade`, `SupportFacade`) are not refreshed on open — the D-14 fix was not applied to the account feature (open point 13) |
| C-14 | S3 | ISO-03 | Platform session types `/account/dashboard`, `/wallet`, `/support`, `/audit`, `/profile` | client pages unavailable in the Super Admin portal | open inside the admin shell; a must-change platform user is sent to `/account/profile` instead of `/admin/profile` | URLs stay, pages render | `app.routes.ts`: the `account` route has no `tenantGuard` (D-17 fix covered the other client features); `passwordChangeGuard` targets `/account/profile` for both portals |
| C-15 | S3 | ISO-05 | `uat.mdr` types admin URLs it has no permission for | refused page (route permission guard) | page opens; API 403; "could not be loaded" (or stale data, C-13) | network 403 on `/platform/clients`, `/organization/users`, `/settings` | `/admin/*` routes carry only `platformGuard`; the nav registry permissions are not enforced as route guards |
| C-16 | S4 | DSH-01 | Overview group in English | distinct labels (Home / Dashboard) | two entries "Dashboard" (`/admin`, `/admin/dashboard`) | nav text | `public/i18n/shell.en.json` `nav.adminHome` = `nav.platformDashboard` = "Dashboard" |
| C-17 | S3 | CLI-12 | Create a client, open the platform audit log | creation listed (SA-09 "every change made in the Super Admin portal") | absent; audited in the new laboratory's trail | D-12 | carried from D-15 (`TenantProvisioner.StageAsync` stamps the new tenant); open point 11, awaiting a decision |
| C-18 | S3 | AUD-06 | Platform audit columns | SA-09 includes the client | no client column (the tenant id is only inside the payload) | table header | `features/account/pages/audit-log.page.html` / audit entry contract has no client field |
| C-19 | S4 | SUB-03 | Top-up 250.555 | refused (2 decimals) or stated | silently 250.56 | D-15 | top-up amount validator (`platform` wallet dialog and `WAL-001` rule) accepts more than 2 decimals |

Observations without a defect id (backlog): O-1 the wizard's mobile error is the generic "The value is not valid." and
replaces the length hint; O-2 the recovery code step keeps the previous attempt's error after *Back* and a new request;
O-3 the admin-user confirm mismatch reads "The value has the wrong format." (the Add client form says "The passwords do
not match."); O-4 clients CSV: header "Country · governorate" over a country-only column, mobiles exported as `'+20…`;
O-5 after the forced password change the banner and `?mustChange=1` stay until the next navigation; O-6 duplicate
sequences are outlined in red with *Save* disabled but no text; O-7 e-mail subjects are not marked `*`; the platform
user editor speaks of "branches of the laboratory" / "this laboratory"; platform-user mobiles are stored as typed (not
E.164); *Use the default* stores a row holding the default instead of removing it; the wizard's "the address was sent by
SMS" toast is shown although the lane has no gateway.

## 6. Data created (prefix V11, invented)

- Public: subscription request **V11 Wizard Lab** (Pending, expires); OTP challenges (subscription, password recovery).
- Platform roles **V11 Role** (3 keys, now 0 members) and **V11 Role B** (inactive); platform user **v11.user**
  (Master Data Reviewer, must change password; locked / unlocked, deactivated / reactivated during the run).
- Client **V11 Validation Lab** (`v11-validation-lab`, Egypt · Cairo, UATPLAN, wallet 250.56 EGP from V11-INV-1,
  deactivated and reactivated; administrator **v11lab.admin**, password changed at first sign-in). Holds the copy of the
  V11 global catalogue. The three "V11 API Lab" clients were created by another tester after these items and hold the
  copy as well.
- Plan **V11PLAN** (222 EGP, active); country **V11 Land** (XV, +999) with governorate **V11 Province** (inactive);
  country V11 Temp created and deleted; notification template **v11-notice** (e-mail, active).
- Global catalogue (platform tenant): V11CG, V11ST + FAST, V11u/L, V11 Pregnant, V11AS, V11T (2 bands), V11SVC
  (sequence 15), V11PREP, V11HEM, reason "V11 Haemolysed sample", Z99.V11, V11 Results Template, V11 Rule, V11SET,
  V11 Penicillin, V11 Augmentin, V11AMC, V11ECO, V11S (inactive).
- Platform parameter `labelCopies` changed to 2 and restored to 1 (a row now exists for it). Passwords of the V11
  accounts are in the git-ignored `.local/dev-credentials.md`.
- Side effects restored: `platform.admin.failed_password_attempts` went 0 → 1 through C-01 and returned to 0 at the
  next sign-in. Early in the run one `navigate` call was sent by mistake to a browser tab of another tester
  (`127.0.0.1:4300` page reloaded to `localhost:4300/login`, then sent back); nothing was entered there.

## 7. Not tested / blocked, and why

- Blocked by the missing SMS gateway: wizard completion and trial (PUB-08), recovery completion (PUB-14), the inactive
  template fallback text (TPL-06).
- Not tested: session CSV export (SES-04); test types other than Numeric, the Timing tab and service types other than
  Test / Additional (CAT-26); report-template logo and signature upload (CAT-27); a second administrator acting on the
  main administrator (covered by UAT-SUP-02 in the dry run; here only the self view); subscription and usage debits
  (no billing run); forced API requests with another user's bearer (the token lives in a signal; the server's 403s were
  observed for URL access, ISO-05).
- The integration library (SA-15…SA-18) and the support desk are Release 2 and out of scope.
