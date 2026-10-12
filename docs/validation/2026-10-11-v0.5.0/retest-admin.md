# Re-test of the v0.5.0 fixes, 2026-10-12: tester R-ADMIN, Super Admin portal and public pages

Scope: re-test of the defects **C-01…C-12** of `super-admin-and-public.md`, the server-side sorting of the admin registers,
the search with `_`, and a regression smoke of every Super Admin menu page and the public pages in English and Arabic.

## Environment

- SPA `http://localhost:4300` (own tabs only; `127.0.0.1` belongs to the other re-tester). It serves the pinned build
  `.local/validation/ui` ("fixes 5ddba06+working-tree built 2026-10-11T22:42:08Z", `VERSION` 0.5.3) and proxies to the
  validation API `127.0.0.1:5102`, which runs the fixed code.
- Database `lis` on `127.0.0.1:5433`, read-only queries as `lis_owner` (`psql -At`). Migration `GrantExportPermissions` is
  in `ops.__ef_migrations_history`.
- Every value was entered through the pages: browser automation, with clicks and form events issued from the page context.
  In-app navigation used the Angular router (`history.pushState` + `popstate`), the same path as a menu click; typed URLs were
  checked with a full page load. CSV exports were captured in the page (the blob was read back), so no file was written.
- A scratch Node client of the public auth API (scratchpad only, never committed) seals passwords exactly as the SPA does
  (compact JWE, RSA-OAEP-256 + A256GCM, audience `METHOD /path`). It talks to the same origin as the browser, `localhost:4300`,
  with `Origin`, `Sec-Fetch-Site: same-origin` and `X-Requested-With: Platform`. It was used for C-02, C-04, C-05 (server) and
  the paged sorting checks.
- Accounts: platform.admin, uat.ops, uat.mdr, uat.sup2, v13.user (legacy mobile), v14lab.admin, and the new v15.user.
- No code, dictionary, test, script or migration was changed. Time: 2026-10-12, 01:44–02:14 (UTC+3).
- Shared lane: a backend-cycle run of another tester was active at the same time (V11 API Lab 261011224410, its own
  platform.admin sign-ins and one `CSRF_REJECTED` at 01:44:20 from its negative test). At the start, `localhost` held a stale
  `wt.admin` session from an earlier campaign (tab `tab-10`, not mine); I signed it out from my own tab before signing in.

## 1. Summary

| Re-tested | Fixed | Partly | Not fixed | New findings |
| --- | --- | --- | --- | --- |
| **14** (C-01…C-12, sorting, search `_`) | **13** | **1** (C-02) | **0** | **2** (R-01 S4, R-02 S4) |

- **C-02 is PARTLY fixed.** In the browser, two tabs that reload at the same moment now stay signed in. Five pairs were
  reloaded 0.3–10 ms apart; the Web Locks lock serialises the two refreshes about 20 ms apart, and nothing is revoked.
  The server-side grace is not reliable. When the Node client sent two refreshes with the same cookie in one round trip,
  one request got `401 AUTH-012` in **9 of 20** rounds (R-01). The family survives and no reuse event is written. A replay
  of the old cookie 11 s later still revokes the family and writes `REFRESH_REUSE_DETECTED`.
- **Fixed with evidence in the UI and the database:** export keys (C-01), E.164 mobiles and recovery by either spelling (C-03),
  the server gate for the forced password change (C-04), the plan fee rule (C-05), the stale recovery error (C-06), the
  changing tab's banner (C-07), the wizard digit count (C-08), the dial-code rule (C-09), the viewer's "View template" (C-10),
  the client attribution of the administrator password reset (C-11) and the audit hint (C-12). Server sorting covers every
  page (compared with the database at page sizes 2, 4 and 5). Searching for `_` and `%` is literal.
- **Regression smoke passed:** 33 / 33 admin pages for platform.admin in both languages (list, one editor, no console error),
  the menus of uat.ops (9), uat.mdr (24) and v15.user (6), 24 refused URLs (each lands on `/admin` with the notice), and
  the public pages in EN / AR. Audit chain intact (145 = 145). Outbox since 01:44: 329 processed, 0 dead-lettered, 0 attempts.

## 2. Findings re-tested

| Id | Expected after the fix | Steps re-run | Observed now | DB / API evidence | Verdict |
| --- | --- | --- | --- | --- | --- |
| C-01 (S3) | Platform Administrator holds `organization.export` and `treasury.export`; *Export CSV* back on Roles and Admin users; the administrator can grant the key; a user without it gets ORG-030; laboratory administrator roles hold it | (a) DB. (b) platform.admin: *Export CSV* on Roles and Admin users. (c) Created **V15 Role B** (`platform.view`), ticked *Branches and roles: Export* in Screen rights, *Save*. (d) **v15.user** (V15 Role: platform/identity view, organization view/create/edit/manage, no export) opened Roles and Admin users, then ticked *Export* on V15 Role → *Save* → *Confirm* | (b) `roles-2026-10-12.csv`: 9 data rows = 9 on screen. `users-2026-10-12.csv`: 9 = 9. (c) "Screen rights saved." (d) No *Export CSV* on either page. The save was refused with the toast "You cannot grant a permission you do not hold." | `app.roles` Platform Administrator: **101 keys**, both export keys present. Every provisioned *Laboratory Administrator* role (16) holds `organization.export` and `treasury.export`; 17 rows stamped "Migration GrantExportPermissions". Not changed: one role in tenant `01a12231…` (no `saas.tenants` row, 9 keys, no `organization.view`), which is outside the migration's rule. V15 Role B → `{organization.export,platform.view}`; field change + `PRIVILEGE_MATRIX_CHANGED`. v15.user's PUT `…/roles/{id}/permissions` → **403 `ORG-030`** "…: organization.export." | **FIXED** |
| C-02 (S3) | Two tabs of one session that refresh together both stay signed in; a later replay still revokes the family | (a) Browser: tabs A and B as platform.admin, both scheduled to `location.reload()` at the same wall-clock second. Five rounds; `performance.timeOrigin` of the two documents 0.1–10 ms apart. (b) Node: sign-in as v15.user, then two `POST /auth/refresh` with the same cookie in one `Promise.all`. Then a refresh with the newest cookie; 11 s later a replay of the original cookie. (c) Node: 5 rounds, then 20 rounds of concurrent pairs, each from the latest cookie | (a) Both tabs signed in after every round (Platform Administrator in the header), no `/login`. (b) First run: A `200` with a new cookie, B `200` grace answer (access token, no cookie); both access tokens work on `/auth/me`; the next refresh `200`; the replay 11 s later `401 AUTH-012`, after which the newest cookie is also refused (`AUTH-012`) and the access token `AUTH-006`. (c) **One request refused `401 AUTH-012`** in 2 of 5 and **9 of 20** rounds. It is always the request that gets no cookie. The other gets `200` with a cookie, and the session continues (R-01) | (a) `identity.refresh_sessions` family `01a12d24-6057…`: rotations in pairs 20–34 ms apart (01:50:53.106 → .140, 01:51:01.099 → .119, …), none revoked; no event for platform.admin. (b) family `01a12d28-3ece…`: `SESSION_CONTEXT_CHANGED refresh_grace` at 01:49:34.781; replay at 01:49:45.829 → family revoked `ReuseDetected`, `REFRESH_REUSE_DETECTED` (error) ×2 (the replay and the follow-up refresh). (c) Only 11 `refresh_grace` events in the 20 rounds; no event at all for the 9 refusals | **PARTLY** (browser fixed; server grace: R-01) |
| C-03 (S3) | Admin-user mobiles stored in E.164; the other spelling is a duplicate; recovery finds the account by either spelling | Created **v15.user** with mobile `01015151501`. Created `v15.dup` with `+201015151501`. Recovery (`/login/recover`) with `01015151501`, `+201015151501`, and `+201013131315` (v13.user, stored before the fix as `1013131315`) | "User created."; v15.dup refused with the toast "Another account uses this mobile number." The recovery page shows the neutral code step for each | `identity.platform_users` v15.user `mobile = +201015151501`. POST `/organization/users` → **409 `ORG-104`**. `PASSWORD_RECOVERY_STARTED` for v15.user at 02:06:36 (national) and 02:06:44 (E.164), and for v13.user at 02:06:47 (legacy row matched via the spellings) | **FIXED** |
| C-04 (S3) | A must-change session gets `403 AUTH-016` everywhere except auth, own profile, password, preferences and privileges; after the change the same session works | Node: v15.user (must change, temporary password) and v14lab.admin (laboratory administrator, must change). Probes before and after `POST /api/v1/account/password` (both fields sealed) on the **same** session | Before: `GET /settings`, `/organization/users`, `/organization/roles`, `/platform/clients`, `/audit/entries`, `/platform/countries` → **403 `AUTH-016`** "The password must be changed before anything else."; `/auth/me`, `/account/profile`, `/organization/me/privileges`, `/organization/branches/selectable` → 200; refresh 200; `PUT /account/preferences` 204. Change → 204. After: users / roles **200** with the same token; settings / clients / audit / countries `403 AUTHZ-001` (the role lacks those keys, a permission refusal) | v15.user `must_change_password` t → f (field change "V15 Re-tester" 01:48:42, `PASSWORD_CHANGED`). v14lab.admin probed only; its password was not changed | **FIXED** |
| C-05 (S4) | A plan fee of 14.145 is refused in the form and by the server; 14.15 saves | *Plans* → *Add plan* V15PLAN, fee `14.145` → *Save*; then `14.15`. Node as uat.sup2: `POST /platform/plans` (V15PLANX) and `PUT /platform/plans/{V15PLAN}` with 14.145 | Form: "The monthly fee has at most two decimals." under the field; no request sent. 14.15: "The plan was saved." API: both **400 `VALIDATION-400`** `errors.monthlyFee = "The monthly fee has at most two decimals."` | `saas.plans` V15PLAN `14.15` (V15PLANX not created); deactivated at the end | **FIXED** |
| C-06 (S4) | After *Back* and a new request, the code step is clean | Recovery `v15.nobody` → code `000000` + new password → "The code is invalid or expired." → *Back* → `01015151501` → *Send code* | The new code step shows only "If an account matches, a 6-digit code was sent… 5 minutes" and the countdown; no stale error | `PASSWORD_RECOVERY_FAILED unknown_account` ×2 (start + completion) | **FIXED** |
| C-07 (S4) | After a forced change, the changing tab leaves `?mustChange=1` and the banner | Reset v15.user's password (must change). Tab A signs in → `/admin/profile?mustChange=1` with the banner. Tab B reloads and lands on the same page. Each tab got a marker variable. **Tab A** (the changing tab) *Update password* | Tab A: "Your password was changed.", now at `/admin` with no banner, and the marker survived (no reload). Tab B: also at `/admin` with no banner and its marker intact | `must_change_password` → f | **FIXED** |
| C-08 (S4) | The wizard's mobile errors name the digit count | `/welcome/subscribe`, Egypt, lab mobile `12345`, manager `010123`, e-mail `not-an-email` → *Send verification code*; Arabic re-submit | EN: "The laboratory mobile has 10 digits after +20 in the selected country." / "The manager's mobile has 10 digits after +20…" / "Enter a valid e-mail address." AR: "جوال المعمل 10 أرقام بعد +20 في الدولة المختارة." / "جوال المدير 10 أرقام…". After a language switch these two messages stay in the previous language until the next submit (R-02) | nothing sent | **FIXED** (R-02) |
| C-09 (S4) | A dial code without "+" is refused under the field | *Add country* V15 Land, dial `997`, all other fields valid → *Save* | "The dial code is '+' followed by one to four digits, like +20." under *Dial code*, `aria-invalid=true`; no POST sent. The check runs only on *Save*: on blur the field stays valid, and on the first *Save* (time zone still empty) only the time zone was marked (O-1) | no POST `/platform/countries`; dialog cancelled | **FIXED** |
| C-10 (S4) | A view-only user sees "View template" | uat.ops (`notifications.view`) → Notification templates | Row action `aria-label` / title "View template" ×6. The dialog is titled "Template", its 4 fields are disabled, the placeholder chips are disabled (a click changes nothing), and the only button is *Cancel* | — | **FIXED** |
| C-11 (S4) | A client administrator's password reset shows the client in the audit log; the client filter keeps it | Clients → V14 Validation Lab → *Reset administrator password* (V15 temporary password) → Audit log; Client = V14 Validation Lab → *Apply* → *Export CSV* | Top row "Platform Administrator · **V14 Validation Lab** · Change Modified · PlatformUser · PasswordChangedAtUtc…". The filter gives 12 rows, all V14 Validation Lab, including this reset and the one of 11 Oct 22:24. CSV `audit-log-2026-10-11-2253.csv`: 12 data rows, Client column filled, both PlatformUser rows present | `audit.field_changes` PlatformUser `01a12c6a-9aee…` 22:53:11 UTC; `CLIENT_ADMIN_PASSWORD_RESET` | **FIXED** |
| C-12 (S4) | The platform audit hint names platform entities | Audit log filters EN / AR | "For example Tenant, Plan, Wallet, Role" / "مثل Tenant أو Plan أو Wallet أو Role" | — | **FIXED** |
| Sorting | A sorted column covers every page; Sessions "Since" sorts by time | UI: every sortable header of Clients (client, plan, wallet, created), Countries (country, ISO), Governorates (governorate, country), Notification templates (code), Sessions (user, since). Governorates at 20 rows per page, status All, Governorate ↓, pages 1 and 2. Node (uat.sup2): every sort through pages of 2, 4 or 5 rows | Requests carry `sort=<key>&direction=<asc/desc>` (`nameEn`, `planCode`, `walletBalance`, `createdAtUtc`, `isoCode`, `countryEn`, `code`). Governorates page 1 "V14 Province … Gharbia" (20) and page 2 "Faiyum … Alexandria" (9), one continuous descending list. Paged API results (4 pages of 4 clients, 4 pages of 2 countries / templates, 6 pages of 5 governorates) have no duplicates or gaps and equal the database order. Sessions: Since ↑ 00:55, 01:45; ↓ reversed | `saas.governorates` `order by name_en desc` = the UI sequence (29). `saas.tenants` by name desc, by `wallets.balance` desc, by plan code: equal (ties ordered by creation). `saas.countries` by name desc / ISO, `saas.notification_templates` by code desc: equal. The sessions column has `sortValue: row.sinceUtc` (ISO time), not the formatted text | **FIXED** |
| Search `_` | `_` is a literal character, not a wildcard | Admin users and Roles: search `_`, `%`, then `v15.` | `_` and `%`: "No user matches the filter." / "No role matches the filter." (no platform user or role contains either). `v15.` → v15.user only | — | **FIXED** |

## 3. Regression smoke

### 3.1 Admin pages per user

"Rows" counts the table rows on first load. "Editor" means the page's *Add…* dialog or editor route opened and closed.

| User (rights) | Pages | Loads | Editor | Refused → `/admin` with the notice | Console | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| **platform.admin**, **Arabic** | 33 menu entries | 33 / 33, Arabic titles (`dir=rtl`), e.g. العملاء 15, المحافظات 27, قوالب الإشعارات 6, سجل التدقيق 12 | 26 / 26 pages that have one: dialogs (`إضافة عميل` … `إضافة قيمة حساسية`) or routes (`/roles/new` دور جديد, `/users/new`, `/services/new`, `/tests/new`); the 7 other pages have no *Add* (home, dashboard, subscriptions, sessions, audit, service sequence, parameters, profile; their actions were used in §2) | — | no error | PASS |
| **platform.admin**, **English** | 33 | 33 / 33 (Clients 15, Governorates 27, Notification templates 6, Audit log 12, Platform parameters 25 inputs) | 26 / 26 | `/registration/new`, `/patients`, `/account/profile`, `/results/worklist`, `/admin/library/pcr-kits`, `/admin/support` | only the expected 401 (before sign-in) and the 409 of C-03 | PASS |
| **uat.ops** (platform.view, tenancy.\*, wallet.\*, notifications.view) | 9 | 9 / 9 | template viewer (C-10) | in-app: Roles, Admin users, Audit, Parameters, Tests, `/patients`; typed URL `/admin/audit` (full load) | — | PASS |
| **uat.mdr** (platform.view, catalogue.\*) | 24 | 24 / 24; *Add…* present on every register except Service sequence | — | Clients, Roles, Admin users, Audit, Countries, Parameters, Notification templates, `/account/profile` | — | PASS |
| **v15.user** (V15 Role) | 6 | 6 / 6; Roles without *Export CSV*, with *Add role*; Admin users without *Export CSV* | role matrix (C-01) | Clients, Audit, Tests, `/registration/new` | — | PASS |

The home tiles read Clients **15** / Active subscriptions **15** = `saas.tenants` (15, all Active). In the Arabic shell
the header shows the role in Arabic ("مدير المنصة"). The display name stays as registered. The sidebar collapse button
is now in the top bar ("Collapse menu" / "Expand menu"); the screenshot shows the icon rail, and the menu still navigates
after expanding.

### 3.2 Public pages

| Page | EN | AR | Main action | Verdict |
| --- | --- | --- | --- | --- |
| /welcome | yes | yes, rtl | Hero "reception, the bench, finance and the patient report"; no sample collection, analysers, cash closing or report templates in either language; Subscribe / Sign in links | PASS |
| /welcome/subscribe | yes | yes, rtl | Country rule per field (C-08); nothing sent | PASS (R-02) |
| /login | yes | yes, rtl | `v15.nobody` → "The username or password is incorrect." / "اسم المستخدم أو كلمة المرور غير صحيحة."; sign-ins of four accounts (platform.admin, v15.user, uat.ops, uat.mdr) | PASS |
| /login/recover | yes | yes (via the login page's switch) | Neutral code step EN / AR; the stale error is gone (C-06); recovery by mobile in both spellings (C-03) | PASS |
| Sign-out | — | — | Full page load to `/login`; the other tab follows (three times) | PASS |

## 4. New findings

| Id | Sev | Steps | Expected | Observed | Evidence | Suspected cause |
| --- | --- | --- | --- | --- | --- | --- |
| R-01 | S4 | Sign in (browser session), then send two `POST /api/v1/auth/refresh` with the **same** cookie in one round trip (`Promise.all`); repeat from the newest cookie | Both answered 200: one rotates, the other gets the grace answer (C-02 server half) | In **9 of 20** rounds (2 of 5 in a shorter run), one request is refused with **`401 AUTH-012`**. It is always the one without a new cookie. The other rotates normally, the family stays live, and no event is written for the refusal. The SPA avoids this with the Web Locks lock (§2 C-02a). A browser without `navigator.locks` (e.g. a page served over plain HTTP from an IP address, which is not a secure context) or a tab that gave up waiting (15 s) depends on the grace, and one of its tabs would be sent to `/login` | Node run; `identity.refresh_sessions` (21 rotations, none revoked); only 11 `SESSION_CONTEXT_CHANGED refresh_grace` events for 20 rounds, nothing for the 9 refusals | `RefreshSessionCommandHandler` (`src/Platform.Application/Identity/Commands/RefreshSessionCommand.cs`): on `CONCURRENCY-409`, `TryGraceAsync(committed, now, …)` uses the `now` taken at the **start** of the losing request. The winner's `RotatedAtUtc` is later, so `now < rotatedAt` returns `null` and the request is `Rejected()`. The grace window should use a fresh clock reading, or tolerate a rotation stamped slightly after `now` |
| R-02 | S4 | `/welcome/subscribe`, Egypt, short lab and manager mobiles → *Send verification code*; switch the language (العربية / English) | Every message follows the language | The two country-rule mobile messages keep the language of the last *Send* ("The laboratory mobile has 10 digits…" under Arabic labels, "جوال المعمل 10 أرقام…" under English labels). The e-mail and required messages switch at once | page text before and after the switch | the wizard stores the translated country-rule text in the control error at validation time, instead of a key and parameters rendered in the template (`features/public` subscribe page) |

Observations (no id)

- O-1: the dial-code rule (C-09) runs only on *Save* and only once the other field errors are corrected. The first *Save* with an
  empty time zone marked the time zone only; the dial code was marked on the second *Save*. It is not checked on blur.
- O-2: the duplicate mobile (`ORG-104`) is shown as a toast, not under the *Mobile* field.
- O-3: in the Arabic wizard, the label reads "جوال المختبر" but the message reads "جوال المعمل".
- O-4: mobiles stored before the C-03 fix are not rewritten: v13.user `1013131315`, uat.reception `01000000011`,
  v11.reception / v11.tech `0100555010x`, v14.reception / v14.tech `0100555140x`. Matching still works through the spellings
  (v13.user recovered by `+201013131315`).
- O-5: a tab sitting on `/login` does not follow a sign-in made in another tab (`AuthStore.onOtherTab` ignores messages
  when the tab has no profile); it shows the session after a reload. A tab that has a session does follow.
- O-6: a user with `platform.view` only (uat.mdr) sees every platform session in *Sessions*, including platform.admin's
  session from another host (without the *Terminate* action). This is the catalogue's design; the v0.4.1 note "only its own
  session" was a coincidence of data.
- Method notes. (1) Ticking several role-matrix boxes from one script task without a pause kept only the last box; with
  500 ms between clicks every box held. No person clicks that fast, so this is not reported as a defect. (2) Once, after a
  screenshot of the hidden pane, the router stopped rendering new routes in that tab (no error; `withViewTransitions`). It
  was not reproducible after a reload, with the same sequences and with real link clicks.

## 5. Data created (prefix V15, invented)

- Platform roles **V15 Role** (platform.view, identity.view, organization.view/create/edit/manage; 1 member, active) and
  **V15 Role B** (`{organization.export, platform.view}`, **inactive**, 0 members).
- Platform user **v15.user** ("V15 Re-tester", `v15.user@example.test`, mobile `+201015151501`, V15 Role, language en).
  Its password was changed twice: once through the API (C-04), then reset by the administrator and changed on the profile
  page (C-07). Active, unlocked, failed attempts 0. Credentials are in the git-ignored `.local/dev-credentials.md`.
- Plan **V15PLAN** (14.15 EGP, 1 / 5 / 15 / 0, **inactive**).
- **v14lab.admin**: administrator password reset again (C-11). New temporary password in `.local/dev-credentials.md`; must
  change remains true.
- OTP challenges `password-recovery`: v15.user ×2, v13.user ×1 (left to expire); two unknown-account attempts.
- Session families: v15.user's Node families signed out; one deliberately revoked `ReuseDetected` (C-02 replay). Node
  sessions of uat.sup2 and v14lab.admin signed out. The stale `wt.admin` session found on `localhost` was signed out.
- Not saved: `v15.dup` (refused), V15PLANX (refused), V15 Land (dialog cancelled), no subscription request (form refused).
- Restored or unchanged: platform.admin language ar → **en**. `PUT /account/preferences {language: en}` for v15.user and
  v14lab.admin changed nothing (no field change). Failed attempts 0 and no lock for platform.admin, uat.ops, uat.mdr,
  uat.sup2, v13.user, v15.user. At the end **no live session** of my accounts (the only live platform.admin head is the one
  from host 192.168.80.38, created at 00:59, before this run). Both of my tabs were closed.

## 6. Not tested, and why

- Recovery completion and wizard completion: there is no SMS gateway on the lane, so the code cannot be read (as before).
- Sessions "Since" across calendar days: only two sessions were listed, both on 12 Oct, where text and time order agree. The
  time sort was confirmed in the code (`sortValue: row.sinceUtc`) and on the two rows.
- The integration-library registers: they are Release 2, out of scope, and their URLs are refused.
- The client-portal *Export CSV* of a laboratory administrator: checked in the database only (all 16 roles hold the key).
  That belongs to the client re-tester's scope.
- uat.sup2 in the browser: used through the API only (plans, sorting). The Platform Administrator role was covered by
  platform.admin.
- Editing an existing record on every register: one editor per page was opened (*Add…*), plus the role, user, plan, client
  and template editors used in §2.
