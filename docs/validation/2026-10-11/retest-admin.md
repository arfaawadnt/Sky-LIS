# Re-test 2026-10-11 — tester R2 · Super Admin portal, public pages and sessions

Scope: re-test of defects **C-01…C-19** (`super-admin-and-public.md`) and **D-01** (`backend-cycle.md`) after the fixes,
plus a regression smoke of every Super Admin page and the public pages. SPA `http://localhost:4300` (own tabs only) →
API `127.0.0.1:5102`; database `lis` on `127.0.0.1:5433`, read-only as `lis_owner`. Every value was entered through the
pages (browser automation; clicks issued from the page context because the pane was scaled); no code, dictionary, test
or script was changed. Time: 2026-10-11, 16:20–16:50 (UTC+3).

Method notes

- CSV exports were captured in the page (the anchor `click` of the blob was intercepted and the blob read back), so no
  file was written to disk.
- C-01: the broadcast between tabs now sends every other tab to sign-in, so the original race cannot be reproduced
  with plain clicks. To prove the second defence (identity check on refresh), tab A was instrumented to **miss** the
  broadcast (the `data` getter of `MessageEvent` returns null for `BroadcastChannel` messages in that tab only,
  simulating a frozen or late tab). Both paths were then exercised.
- During the run the dev server hot-reloaded `features/platform/pages/countries.page.ts` (`GET /@ng/component?c=…countries.page.ts`
  at ~16:41): someone was editing that file while the test ran. The countries results below reflect the code at that time.

## 1. Summary

| Fixed | Partly | Not fixed | New defects |
| --- | --- | --- | --- |
| **19** (18 C + D-01) | **1** (C-18) | **0** | **5** (R2-01…R2-05: S3 ×1, S4 ×4) |

## 2. Defect re-test

| Id | Original finding | Steps re-run | Observed now | DB check | Verdict |
| --- | --- | --- | --- | --- | --- |
| C-01 (S1) | A stale tab refreshed with another user's cookie and replayed the request as that user (password change executed for `platform.admin`) | (a) Tab A `uat.mdr` on `/admin/profile`, instrumented to miss broadcasts; tab B signs out, signs in `platform.admin`; A submits *Update password* (wrong current password). (b) No instrumentation: B signs out → A; A signs in `platform.admin`, B signs in `uat.mdr` → A | (a) `POST /account/password` → 401, refresh returns another identity → A is reloaded to `/login` (full page load), the request is **not replayed** (one POST only). (b) A is reloaded to `/login` on B's sign-out; on B's sign-in as another user A reloads and comes back as the new user with nothing from the old page | `platform.admin.failed_password_attempts` 0 → 0; no `PASSWORD_CHANGE_FAILED` event; only `SESSION_REVOKED uat.mdr logout` and `SIGN_IN_SUCCEEDED platform.admin` | **FIXED** |
| C-02 (S2) | 204 answers of roles / admin users read as failures: no toast, *Reset password* dialog stays open | Created **V12 Role**; *Save* general; *Save* screen rights; *Deactivate* → Confirm. Created **v12.user**; *Save* profile; *Assign role*; *Reset password*; *Lock*; *Unlock*; *Deactivate*; *Activate* | Toasts: "Role created.", "Saved.", "Screen rights saved.", "Deactivated.", "User created.", "Saved.", "Role assigned.", "Password reset; the user's sessions were ended." (dialog **closes**), "User locked.", "User unlocked.", "Deactivated.", "Activated."; confirm dialogs close; status label follows | `app.roles` V12 Role `{catalogue.view,platform.view}`, description edited, `is_active=f`; v12.user membership P, designation, role Master Data Reviewer, `is_locked_out=f`, active; events `MEMBERSHIP_CHANGED …:role`, `:password-reset`, `:locked`, `:unlocked`, `USER_DEACTIVATED`, `:reactivated` | **FIXED** |
| C-03 (S3) | Add client: landline / 8-digit manager mobile not checked in the browser; one server message without the field | Add client, Egypt, lab mobile `0233351111`, manager `+2010111100`, *Create client* | Under each field: "The laboratory mobile has 10 digits after +20 in the selected country." / "The manager's mobile has 10 digits after +20 in the selected country."; nothing sent. A valid client was then created (below) | — (server codes `GEO-010` / `GEO-014` present in `Country.cs`; the browser check stops the request first, so the server mapping could not be reached from the page) | **FIXED** |
| C-04 (S3) | Catalogue CSV export ignored the list filter | Search `V11` (or `V12`) then *Export CSV* on 22 pages: clinical groups, sample types, units, patient conditions, additional services, services, tests, preparations, comments, reasons, diagnoses, report templates, test rules, shortcut sets, 5 microbiology lists, clients, admin users, roles | CSV rows = rows on screen on every one (e.g. clinical groups 1 of 2, services 2 of 3, tests 1 of 2) | — | **FIXED** (countries / governorates still export everything: R2-02) |
| C-05 (S2) | Test created with a refused band lost the band and the reason | Add test **V12T** (Numeric, V11CG, V11u/L, 1 decimal, V11ST), band Any 0–120 y Low 110 High 70, *Save*; then Low 70 / High 110, *Save* | 1st save: toasts "Limits must read: reportable low ≤ … ≤ reportable high…" and "The test was created, but part of its configuration was refused — correct the tab marked "!" and save again."; stays on `/tests/<new id>`, tab "Reference ranges !" active, **typed band kept** with the reason on the tab. 2nd save: "Saved.", tab "Reference ranges 1" | `app.tests` V12T (platform tenant); `app.test_range_bands`: Any 0–120 Years, 70–110, "V12 bad band" (1 row) | **FIXED** |
| C-06 (S4) | Raw validator texts ("'Header Text Ar' must not be empty.", "'Currency Code' must be 3 characters…") | Report template with the English header only; country with currency code `V1`; both in English and Arabic | Server 400 still carries the raw text in `errors`, but the page shows the rule localized under the field: "This field is required." / "هذا الحقل مطلوب." (Header text Arabic), "Exactly 3 characters." / "3 أحرف بالضبط." (Currency code) | — | **FIXED** |
| C-07 (S3) | Inactive plan offered in Add / Edit client | Created **V12PLAN**, client **V12 Retest Lab** on it, deactivated V12PLAN; opened Add client, the list filter, Edit client | Add client: V12PLAN absent. Filter: "V12PLAN · V12 Plan (inactive)". Edit client: current plan "V12PLAN · V12 Plan (inactive)" selected; saving the edit (address) keeps it: "The client was saved." | `saas.plans` V12PLAN `is_active=f`; tenant still on V12PLAN, address updated | **FIXED** |
| C-08 (S3) | Time zone `Mars/Base` saved | Add country | Time zone is now a picker of IANA names (Africa/Abidjan …); a free value cannot be typed. **V12 Land** saved with Asia/Tashkent, then deleted | `GEO-007` present in the domain (`Country.cs`); not exercisable from the page | **FIXED** |
| C-09 (S3) | Laboratory read the default, not the platform value | Platform `labelCopies` 1 → 2 (*Save*: "1 parameter(s) saved."); signed in `v12lab.admin` (new laboratory, never set it); read its settings; restored 2 → 1 | `GET /api/v1/settings` for the laboratory: `labelCopies` **value 2, source "Platform", inheritedValue 2**; `/registration/settings` also 2. The laboratory parameters page `/organization/parameters` is Release 2 and is now refused by the route guard (sent to the dashboard), so the value is not visible on a screen in R1 | `app.tenant_settings` P `labelCopies` 2 then back to **1** | **FIXED** (API; page out of R1) |
| C-10 (S4) | Raw key `account.profile.languageSaved` after switching to Arabic | `v12.user` profile → العربية → *Save language* | Toast "تم حفظ اللغة.", page rtl | `TenantMembership PreferredLanguage en → ar` | **FIXED** |
| C-11 (S4) | 10 English hints on platform parameters in Arabic | `/admin/parameters` in Arabic | Every hint Arabic; only the accession tokens stay Latin (`{yyyy}`, `ACC-{yyyy}-{nnnnn}`) | — | **FIXED** |
| C-12 (S4) | Landing "Release 1 highlights" listed R2 features | `/welcome` EN and AR | Highlights: registration with payment, results and worklist, catalogue and microbiology, prices / payers / treasuries, branches / roles / users, audit and security (AR equivalent) — no sample tracking, no contract pricing | — | **FIXED** (other sections still market R2: R2-05) |
| C-13 (S2) | Account pages kept the previous session's data in the same tab | Same tab: `v11lab.admin` opens `/account/profile` (V11 Validation Lab), signs out, `platform.admin` signs in and opens `/admin/profile`; earlier `uat.mdr` → sign out → `platform.admin` | Sign-out is a full page load; `/admin/profile` shows platform.admin's own account; `/account/profile` is refused for the platform session (C-14); `uat.mdr` cannot open `/admin/audit` (C-15) | — | **FIXED** |
| C-14 (S3) | `/account/*` open in the admin shell; must-change platform user sent to `/account/profile` | As `platform.admin` open `/account/{profile,dashboard,wallet,support,audit}` and 8 client routes; sign in `v12.user` after *Reset password* (must change) | Every client / account URL → `/admin`. `v12.user` lands on **`/admin/profile?mustChange=1`** with "Your administrator asked you to choose a new password…"; other URLs return to it until the change; after "Your password was changed." navigation is free | `must_change_password` t → f | **FIXED** |
| C-15 (S3) | Admin URLs without the permission opened ("could not be loaded") | As `uat.mdr` (catalogue + platform.view) type `/admin/clients`, `/admin/organization/users`, `/admin/parameters`, `/admin/audit`, `/admin/organization/roles`, `/admin/subscriptions`, `/admin/countries`, `/admin/notification-templates`; as `platform.admin` the R2 URLs `/admin/library/{analyser-models,pcr-kits,code-systems}`, `/admin/support` | All refused → `/admin` (no API call); `uat.mdr` keeps `/admin/catalogue/tests` and `/admin/dashboard`; R2 URLs refused for the administrator too | — | **FIXED** |
| C-16 (S4) | Two "Dashboard" entries | Admin menu EN / AR | "Home" / "Dashboard"; "الرئيسية" / "لوحة المتابعة" | — | **FIXED** |
| C-17 (S3) | Client creation absent from the platform trail | Created V12 Retest Lab; platform audit log | Row "Platform Administrator · V12 Retest Lab · Event TenantProvisioned · Tenant"; *Verify chain*: "The audit chain is intact: 74 entries checked." | `audit.audit_trail` P seq 74 `TenantProvisioned` (aggregate = new tenant, actor platform.admin); new tenant's own trail seq 1–27 (provisioning + catalogue copy); `TENANT_PROVISIONED`; outbox 439 processed, 0 dead | **FIXED** |
| C-18 (S3) | No client column in the platform audit log | `/admin/audit`: columns, *Client* filter = V12 Retest Lab / V11 Validation Lab; Entity = Tenant | Client column and filter present (11 clients); events (`TenantProvisioned`, `TenantPlanChanged`, `TenantDeactivated/Activated`, `WalletToppedUp`) and wallet field changes carry the client. **But field changes on the Tenant record itself show "—" and are dropped by the client filter**: "Modified Tenant Address: 12 V12 Street, Smouha → 14 V12 Street, Smouha" (my edit at 16:40), and the V11 / UAT Lab B rows (Address / GovernorateId / PlanId, DeactivatedAtUtc / DeactivationReason, ActivatedAtUtc) | — | **PARTLY** |
| C-19 (S4) | 250.555 silently rounded to 250.56 | V12 Retest Lab wallet → *Top up* 250.555, V12-INV-1; then 250.55 | 250.555: "The amount has at most two decimals." under the field, dialog stays, nothing sent; 250.55: "The wallet was topped up.", balance 250.55 EGP, one transaction | `saas.wallets` 250.55; `saas.wallet_transactions` TopUp 250.55 / after 250.55 / V12-INV-1 | **FIXED** (server rule `WAL-001` not exercised from the page) |
| D-01 (S3) | Append-only audit tables had no `TRUNCATE` guard | Read-only `pg_trigger` query (no truncate attempted) | `audit.audit_trail`, `audit.field_changes`, `audit.security_events` each carry `*_no_truncate` (tgtype 34 = BEFORE + TRUNCATE, statement level, enabled) next to `*_append_only` (BEFORE UPDATE OR DELETE, row level); function `audit.refuse_truncate()` raises `42501` "… TRUNCATE is not allowed". Also on `app.payments` and `app.refunds` | — | **FIXED** |

## 3. Regression smoke (platform.admin unless noted)

| Page | Loads | Main action | Verdict |
| --- | --- | --- | --- |
| /welcome | yes, EN / AR | links, highlights | PASS |
| /welcome/subscribe | yes | empty → 8 × required; bad mobiles → "The value is not valid." (O-1 unchanged), e-mail format; Arabic rtl | PASS |
| /login | yes | wrong password → "The username or password is incorrect."; admin lock → AUTH-015 "Your account was locked by an administrator. Contact your administrator to unlock it." | PASS |
| /login/recover | yes | unknown user → neutral code step with countdown | PASS |
| Sign-out | — | full page load to `/login` (no return URL); other tabs follow | PASS |
| /admin (Home) | yes | text and link to the dashboard (tiles show "—": R2-05) | PASS |
| /admin/dashboard | yes | periods Today / Week / Month / Year: month 15 patients / 44 tests / 27 registrations, today 10 / 21 / 11 = DB | PASS |
| Roles | yes | create, edit, rights, deactivate (C-02) | PASS |
| Admin users | yes | create, edit, role, reset, lock, deactivate (C-02); inactive V12 Role not offered | PASS |
| Clients | yes | create V12 Retest Lab (provisioning + platform trail), edit, plans list, filtered CSV | PASS |
| Subscriptions & wallets | yes | top-up, plans create / deactivate | PASS |
| Sessions | yes | list, *Terminate* (3 of my own orphan sessions: "The session was terminated."), CSV 4 lines | PASS |
| Audit log | yes | client / entity filters, verify chain (74) | PASS (C-18 partly) |
| 18 master-data and microbiology pages | yes, each with rows | search + filtered CSV on each; V12CG create ("Saved.") and deactivate; V12T create / band / deactivate | PASS |
| Countries | yes | create V12 Land (picker), server error localized, delete | PASS (CSV: R2-02) |
| Governorates | yes, 27 rows | search | PASS (CSV: R2-02) |
| Platform parameters | yes | save labelCopies, Arabic hints | PASS |
| Notification templates | yes | open v11-notice, preview, *Save* "The template was saved." | PASS |
| Profile | yes | own data, must-change flow, language toast | PASS |
| Portal isolation | — | client → `/admin/*` sent to `/account/dashboard` (v11lab.admin) | PASS |

Console: only `InvalidStateError: Transition was aborted` from the automation's rapid navigation; no application error.

## 4. New defects

| Id | Sev | Steps | Expected | Observed | Evidence |
| --- | --- | --- | --- | --- | --- |
| R2-01 | S4 | Two tabs of one browser on the same session of a must-change user (`v12.user`); change the password in tab 2 | Tab 1 follows (no more forced change) | Tab 1 keeps the must-change state: every URL returns to `/admin/profile?mustChange=1` with the banner until a reload | tab 1 after the change in tab 2: `/admin/dashboard` → `/admin/profile?mustChange=1` |
| R2-02 | S3 | `/admin/countries` search `Egypt`, `/admin/governorates` search `Cairo`, *Export CSV* | Filtered export (as C-04 now does elsewhere) | Countries: 1 row on screen, **6** in the CSV; governorates: 1 on screen, **28** in the CSV | captured `countries-2026-10-11.csv`, `governorates-2026-10-11.csv` |
| R2-03 | S4 | Report templates → *Add* in Arabic | Arabic option labels | Document type options (Results, Culture, Text report, Claim, Receipt, Label, Job order) and Language options (English, Arabic, English and Arabic) stay English | dialog text in `ar` |
| R2-04 | S4 | Tab B shows `/login` (signed out earlier) while tab A signs in; B then signs in as another user | The browser's previous session ends, or B's login page follows A's sign-in | A new session is created and the previous one stays **Active / Idle** on the server (its cookie is overwritten): three orphan sessions of `platform.admin` (16:27, 16:28, 16:36) appeared in *Sessions*; I terminated them | `SESSION_CREATED` without `SESSION_REVOKED` for 01a12b25-6164…, 01a12b26-af67…, 01a12b2e-31c1… |
| R2-05 | S4 | `/welcome` and `/admin` | Release-1 content only | Landing: feature grid still offers *Samples and barcodes*, *Analyser integration*, *Automation*, "contract tiers"; Super Admin card offers "Analyser models, PCR kits and code systems" and "the support desk". Admin Home tiles: *Open tickets*, *Agents online* (R2) and *Clients* / *Active subscriptions* show "—" while the dashboard shows 11 | page text |

Observations carried (no new id): O-1 wizard mobile error still generic; O-5 must-change banner and `?mustChange=1` stay until
the next navigation; O-7 the platform user editor still says "this laboratory" / "Every active branch of the laboratory";
the platform role matrix offers client and R2 modules (QC, support, analysers); a client administrator created through
*Add client* is not asked to change the password at first sign-in (`must_change_password=f` for all provisioned
administrators: by design or open question).

## 5. Data created (prefix V12)

- Role **V12 Role** (platform, `{catalogue.view,platform.view}`, inactive, 0 members).
- Platform user **v12.user** (Master Data Reviewer, active, unlocked, language Arabic, password changed at first sign-in).
- Plan **V12PLAN** (123.45 EGP, 1/5/50/1, **inactive**).
- Client **V12 Retest Lab** (`v12-retest-lab`, Egypt · Alexandria, "14 V12 Street, Smouha", V12PLAN, wallet 250.55 EGP from
  V12-INV-1) with administrator **v12lab.admin** and the copy of the active global catalogue.
- Global catalogue: test **V12T** (1 band, inactive), clinical group **V12CG** (inactive). Country **V12 Land** created and
  deleted.
- Restored: platform `labelCopies` 2 → **1**. My three orphan platform.admin sessions terminated. Shared accounts
  (`platform.admin`, `uat.mdr`, `v11lab.admin`) unchanged (`failed_password_attempts` 0).
- Passwords of the V12 accounts are in the git-ignored `.local/dev-credentials.md`.
