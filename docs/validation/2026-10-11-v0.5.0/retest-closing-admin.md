# Closing browser check · 2026-10-12 · tester Z-ADMIN · Super Admin portal (LIS-VR-002)

Scope: the fixes for the findings of `retest-final-admin.md` on the Super Admin portal: F-01 (platform side), F-02, F-03,
F-04, C-7 and C-8, plus "Use default" on Platform parameters and a smoke of every Super Admin page.

This run tested and reported only. No application code, dictionary, test, script or migration was changed. Data was changed
only through the application pages, where a step allowed it; the settings that were changed are listed in §6.

## Environment

- **Site:** `http://localhost:4300`, the pinned build `.local/validation/ui`.
  - `PINNED.txt`: "closing df3bdac+working-tree built 2026-10-12T14:42:31Z"; bundle `main-XXTWWTBD.js`.
  - It proxies to the validation API `127.0.0.1:5102` (health `ready`; process started 14:42:35Z from
    `src/Platform.WebApi/bin/Release`).
- **Notifications:** pickup folder `E:\LIS\.local\mail-pickup\validation`.
- **Database:** `lis` on `127.0.0.1:5433`, read only as `lis_owner` (session `default_transaction_read_only = on`).
- **Accounts:** `platform.admin`, `uat.sup2`, `uat.mdr` (passwords in the git-ignored `.local/dev-credentials.md`), and
  `v20lab.admin` created in this run (§7).
- **Browser:** one own tab (`tab-6`) on host `localhost`, viewport 1366×900, closed at the end after sign-out; no other tab
  was used.
- **Time:** 2026-10-12, 17:44–18:16 local (14:44–15:16 UTC).

Method notes (as in the earlier rounds):

- **Input.** The Browser pane was mostly hidden; the tab ran in the background (`visibilityState` hidden). Clicks and input
  were dispatched from the page context (`element.click()`, `input` / `change` / `blur` events). File choices were made by
  placing a `File` in the input through `DataTransfer` and dispatching `change`, which runs the page's own upload handler.
- **Router navigation.** After each full page load the page context set `Document.prototype.startViewTransition = undefined`.
- **Network evidence.** `PerformanceResourceTiming.responseStatus` per step; for the parameter saves and the charge run the
  page's `fetch` was wrapped to record the request body / response body (inspection only).
- **Node client.** A scratch file in the session scratchpad, never committed. It seals the password as the SPA does and sends
  `Origin`, `Sec-Fetch-Site: same-origin` and `X-Requested-With: Platform`; User-Agent `lis-retest-closing-admin-v20/1.0`. Used
  for the late refresh (F-01), two read-only image probes (F-02) and two extra sessions for the sort check (C-7). It wrote no
  business data.
- No print button was clicked.

## 1. Summary

| Items | PASS | PARTLY | FAIL | Not testable | New findings |
| --- | --- | --- | --- | --- | --- |
| **8** | **5** | **2** | **0** | **1** | **3** (1 × S3, 2 × S4) |

| # | Subject | Verdict |
| --- | --- | --- |
| 1 | F-01 platform side: warning, Stay, idle sign-out, `AUTH-017`, restore by "Use the default" | **PASS** |
| 2 | F-02 report templates: logo and signature, preview, printed report, provisioned copies | **PARTLY**: upload, replace, remove, refusals, preview and copies pass; a laboratory's printed report cannot carry the template images (Z-02, S3); the oversize message is wrong above 576 KB (Z-01, S4) |
| 3 | F-03 "not saved" notice: Add client, Add notification template, Add plan, Add PCR kit, new-role page | **PASS** (PCR kits page is Release 2 and refused in Release 1; the notice is in its code) |
| 4 | F-04 no "(s)" plurals: parameters, members, permissions | **PASS** (counts of 1 and 2 later-release permissions not reachable on screen; Arabic digits in these messages: Z-03, S4) |
| 5 | C-8 wallet charge run | **PARTLY**: run, summary toast, second run and unchanged data pass; no wallet is due on this host, so a Subscription debit and a trial skip could not be seen |
| 6 | C-7 sessions "Since" sort across days | **Not testable** across days; the sort by time passes within the day |
| 7 | "Use the default" removes the stored row for any key | **PASS** |
| 8 | Smoke: every Super Admin page in English and Arabic, console | **PASS** |

Not passing in full: item 2 (Z-02: provisioning drops the clinical group's report-template link, and the Release-1 client
portal cannot set it, so no laboratory report uses a template) and item 5 (nothing due to charge). New findings Z-01…Z-03 in §4.

## 2. Items

### 1 · F-01, platform side · PASS

`/admin/parameters` as `platform.admin`. The field is now named *Sign-out after inactivity (minutes)* ("Minutes without activity
before the session signs out, with a warning one minute before (1–720). Default: 30.").

| Step | Evidence |
| --- | --- |
| Set to **2**, *Save* | `PUT /api/v1/settings` 200, toast "1 parameter saved."; `app.tenant_settings` `sessionTimeoutMinutes` = 2 (the row existed with 30 before) |
| Full page load of `/admin/dashboard` (start-up refresh 14:46:58Z, profile carries 2), no input | at **14:47:54.97Z** the dialog "Are you still there? / Your session will end in 46 s because of inactivity. / Sign out · Stay signed in" (countdown live: 46 s at 14:48:09Z) |
| *Stay signed in* (14:48:15.75Z) | dialog closes; `POST /api/v1/auth/refresh` 200 (new refresh session 14:48:16Z); at 14:48:53Z, after the first deadline, still signed in |
| No further input | warning again at 14:49:15.97Z ("42 s" at 14:49:34Z); at **14:50:16Z** full load of **`/login?reason=idle`**: "Your session ended after inactivity. Sign in again to continue."; `SESSION_REVOKED` / `logout` for platform.admin at 14:50:16Z = last renewal 14:48:16Z + 2 min |
| Node client, `uat.sup2` (platform tenant): sign-in 14:46:40Z (profile `sessionTimeoutMinutes` 2), refresh after 20 s → 200, then idle 150 s | refresh at 14:49:30Z → **401 `AUTH-017`** "Your session ended after inactivity."; security events `SESSION_REVOKED` info `idle_timeout` and `SESSION_CONTEXT_CHANGED` info `refresh_after_expired`; no reuse alarm; the session row `revocation_reason` **Expired**; a second refresh with the same cookie → 401 `AUTH-012` |
| Restore: *Use the default* on the field, *Save* | request body `{"settings":[{"key":"sessionTimeoutMinutes","value":null}]}`, 200, "1 parameter saved."; the field shows 30 and no "Use the default" button; **`app.tenant_settings` has no `sessionTimeoutMinutes` row** for the platform tenant |

Observation (not a defect): the timeout reaches a session through its profile, at sign-in or at the next refresh. The
session signed in at 14:50:41Z (while the value was still 2) was itself signed out idle at 14:52:41Z, after the value had been
restored at 14:51Z, because page-context input events are not counted as activity and no refresh happened in between. A
real user's mouse or keyboard renews the session and picks up the new value.

### 2 · F-02, report templates (SA-12) · PARTLY

**Upload, replace, remove, refusals.** New template **V20 Arabic Report** (Results, language **Arabic**, Arabic header
"معمل في20 — رأس التقرير", footer "تذييل في20") created on `/admin/catalogue/report-templates` ("Template saved."). Its row
action *Logo, signature and preview* opens the dialog "V20 Arabic Report: logo, signature and preview" with the hint "PNG, JPEG
or WebP, up to 512 KB. The logo heads the printed report; the signature prints above the verifier's name on a verified report."

| Action (scratch images, invented) | Request | Screen |
| --- | --- | --- |
| Logo `v20-logo.png` (120×48, 167 B) | `POST …/images/logo` 200 | "Image saved."; thumbnail; buttons Replace / Remove |
| Signature `v20-signature.png` (160×48, 235 B) | `POST …/images/signature` 200 | "Image saved."; thumbnail |
| Replace logo with `v20-logo-2.png` | 200 | "Image saved.", new thumbnail |
| GIF `v20-image.gif` as logo | **422** | "The image must be a PNG, JPEG or WebP file." (thumbnail kept) |
| Text file named `v20-fake.png` as signature | **422** | same message (decided by the bytes) |
| PNG header + padding, 530 KB (logo) and 513 KB (signature) | **422** each | "An image may not exceed 512 KB." |
| 600 KB (logo) | **400** | "Please check the entered values. The value has the wrong format." (Z-01) |
| 2 MB (signature) | **502** through the validation proxy | "Something went wrong on the server." (Z-01); directly on 5102 the API answers 400 `VALIDATION-400` "Failed to read the request form. Request body too large. The max request body size is 589824 bytes." |
| *Remove* logo, *Remove* signature | `DELETE` 204 each | "Image removed.", "None attached / Upload"; the preview falls back to the Sky LIS mark and drops the signature |
| Upload both again | 200 each | final state: logo `v20-logo.png`, signature `v20-signature.png` |

`app.report_templates` (platform tenant) row `01a1309b-cb16-7342-a8f4-a53c3058ec65`: language `Ar`, `logo_file_id`
`01a1309d-7f7d-…`, `signature_file_id` `01a1309d-8951-…`. The five uploaded files stay in
`.local/files/<platform>/catalogue/<template>/` (removed or replaced files are kept, BR-15). The register row shows the *Logo*
and *Signature* badges. *Remove* acts at once, without a confirmation.

**Preview, template language on an English screen: PASS.** With the screen in English the preview of V20 Arabic Report is
drawn in Arabic and right-to-left: "تقرير المختبر", "معاينة — ليس تقريرًا", "المريض / مريض نموذجي", the Arabic header and footer
texts, `template-logo` (120×48) in the letterhead and `template-signature` (160×48) above "Dr. Sample Verifier / اعتمده … توقيع
إلكتروني". The bilingual V11 Results Template offers an English / العربية switch: English shows "Laboratory report / V11 Header",
العربية shows "تقرير المختبر / ترويسة التحقق". In Arabic the dialog reads "الشعار والتوقيع والمعاينة", "شعار", "توقيع", "استبدال",
"إزالة" and the hint in Arabic.

**Copies for a provisioned laboratory: PASS.** *Add client* → **V20 Closing Lab** (`01a1309f-ac4b-7b92-a291-bf846b3b2f14`,
plan STARTER), `POST /api/v1/platform/clients` 201:

- its `app.report_templates` holds V11 Results Template, V14 Results Template and **V20 Arabic Report** (`Ar`) with **new**
  file ids (`01a1309f-af0d-…`, `01a1309f-af11-…`), stored under the laboratory's own folder
  `.local/files/01a1309fac4b7b92a291bf846b3b2f14/catalogue/<new template id>/`;
- Node client as `v20lab.admin` (read only): `GET …/report-templates/{copy}/images/logo` → 200 `image/png`, 167 B, SHA-256 prefix
  `0202d8cf0ea649de` = the uploaded logo; signature → 200, 235 B, `f081c4fa480d7202` = the uploaded signature;
- the platform's template image read with the laboratory's token → **404 `CAT-388`** (a laboratory never reads the platform's
  files);
- outbox of the new tenant: 72 events, all processed, none dead-lettered, 0 retries.

**Printed result report of a laboratory using the template: cannot happen in Release 1 (Z-02).** The report's template comes
from the clinical group of the results (`ResultHeaders`: `group.ReportTemplateId`). The platform group **V20G** was created
with Report template = V20 Arabic Report (and test / service V20K in it), but the provisioned copy of V20G in V20 Closing Lab
has `report_template_id` **null**; so has the copy of V14CG, linked to V11 Results Template on the platform. The provisioning
(`MasterDataCloner`) calls the Catalogue A cloner without a report-template map, before the templates are copied. The
laboratory cannot set the link either: the Release-1 client dialog *Edit clinical group* has Code, Unit code, Name EN/AR,
Report name and Display order only. In the whole database no laboratory clinical group links a report template (0 rows), so no
laboratory report can show a template logo or signature. Because of this no laboratory visit was registered and no report was
previewed. The rule "the signature only on a verified, final report" was seen only on the platform preview (a verified
sample) and in the renderer (`signatureUrl` is null when `doc.isPreliminary`).

### 3 · F-03, "not saved" notice · PASS

Each form was saved empty: no request left the page, the fields were marked, the first field was focused, and the notice
"**The record was not saved: correct the marked fields first.**" (`p.problem`, `role="alert"`) showed.

| Form | Marked | Focus |
| --- | --- | --- |
| *Add client* (Clients) | 12 | Laboratory name (English) |
| *Add template* (SA-20 Notification templates) | 3 | Code |
| *Add plan* (Subscriptions & wallets › Plans) | 3 | Code |
| New-role page `/admin/organization/roles/new` (*Create role*) | 2 | **Role name (English)** (the focus now moves) |
| *Add preparation*, *Add diagnosis*, *Add test rule*, *Add shortcut set* (the other four of the earlier F-03 list) | 3, 3, 4, 4 | Code, ICD code, Name (English), Shortcut code |

In Arabic, *Add client* and the new-role page showed "لم يُحفظ السجل: صحّح الحقول المعلَّمة أولًا." with the focus on
"اسم المعمل (بالإنجليزية)" and "اسم الدور (بالإنجليزية)".

*Add PCR kit*: `/admin/library/pcr-kits` is not in the Release-1 menu; a full load of the URL returned to `/admin` with "هذه
الصفحة غير متاحة لحسابك." (the integration library is Release 2). Its page code runs the same blocked-save check and renders
`shared.masterData.invalid` in the drawer; it could not be seen on screen.

### 4 · F-04, plurals · PASS (see Z-03)

| Text | English | Arabic |
| --- | --- | --- |
| Parameters saved, 1 | "1 parameter saved." | "تم حفظ إعداد واحد." (twice, Default language set to Arabic and back) |
| Parameters saved, 3 | "3 parameters saved." | — |
| Members, 1 (Operations Admin) | "This role has 1 member. The change applies to that user. Continue?" | "لهذا الدور عضو واحد. سيسري التغيير عليه. هل تريد المتابعة؟" |
| Members, 2 (V15 Role) | "This role has 2 members. The change applies to all of them. Continue?" | "لهذا الدور عضوان. سيسري التغيير عليهما. هل تريد المتابعة؟" |
| Members, 3 (Platform Administrator) / 4 (Master Data Reviewer) | "This role has 3 members." / "…4 members." | "لهذا الدور 3 أعضاء." / "…4 أعضاء." |
| Later-release permissions, 3 (Operations Admin) | "This role also holds 3 permissions of modules of a later release; they are not shown and are kept when you save." | "يحمل هذا الدور أيضًا 3 صلاحيات من وحدات إصدار لاحق؛ …" |
| Later-release permissions, 25 (Platform Administrator) | "…holds 25 permissions…" | "…25 صلاحية…" |

The member dialogs were reached by ticking one box and pressing *Save*; each was cancelled and the change discarded:
`app.roles.permission_keys` of the four roles are unchanged. No role holds exactly 1 or 2 later-release permissions, so those
two forms could not be shown; the dictionary carries them (`roles.keptLaterReleaseOne`, Arabic `…One` / `…Two` / `…Many`). No
dictionary file contains "(s)".

### 5 · C-8, wallet charge run · PARTLY

Before the run no wallet was due: the earliest `next_charge_at_utc` is 2026-10-26 12:37Z (V19 Wizard Lab, **Trial**); the
earliest Active client is due 2026-11-10 (Walk-through Laboratory); V20 Closing Lab, provisioned in this run, is due
2026-11-12. `saas.wallet_transactions` held 5 `TopUp` rows.

| Step | Evidence |
| --- | --- |
| *Run charges now* on SA-05 | dialog "Charge the monthly fee of every active client whose next charge is due? A period is never charged twice; trial and inactive clients are skipped." → *Confirm* |
| Run 1 (15:06:23Z) | `POST /api/v1/platform/wallets/charge-run` 200 `{"charged":0,"skipped":0,"failed":0,"atOrBelowZero":0,"items":[]}`; toast "**Charges run: 0 charged, 0 skipped, 0 failed.**" |
| Run 2 (15:06:34Z) | same answer and toast |
| Run 3, Arabic (15:13:19Z) | dialog "تنفيذ الخصم الآن / خصم الرسوم الشهرية لكل عميل نشط حلّ موعد خصمه التالي؟ …"; toast "تم تنفيذ الخصم: 0 مخصوم، 0 متخطى، 0 فشل." |
| Database after | `saas.wallet_transactions` still 5 `TopUp` rows, no `Subscription` row; every `next_charge_at_utc` unchanged; no `WALLET_CHARGE_FAILED` event; no wallet audit-trail event |

Not seen: a Subscription debit, the ledger row on SA-05, a moved next-charge date and the trial skip. They need a wallet whose
next charge is past, and none exists on this host before 2026-10-26; no page can move the date. The run lists only due
wallets, so a trial client is counted as skipped only once its next charge is due.

### 6 · C-7, sessions "Since" · Not testable across days

`identity.refresh_sessions` of the platform tenant holds one live session (platform.admin, created 15:06Z, expiring 23:06Z).
Sessions live at most 8 hours and now also end after 30 idle minutes, so live sessions from two calendar days can coexist only
around midnight. Within the day: with two extra sessions from the Node client (`uat.sup2` 15:07:20Z, `uat.mdr` 15:07:23Z),
*Since* ascending listed platform.admin (18:01), uat.sup2 (18:07), uat.mdr (18:07), descending the reverse (`aria-sort`
ascending / descending); both 18:07 rows were ordered by their seconds. The column sorts on the UTC timestamp
(`sortValue: row.sinceUtc`), not on the shown text. The two extra sessions were ended with *Terminate* ("The session was
terminated.").

### 7 · "Use the default" removes the stored row · PASS

| Key | Kind | Before | After *Use the default* + *Save* |
| --- | --- | --- | --- |
| `sessionTimeoutMinutes` | number | row 2 (row 30 before the run) | no row (item 1) |
| `trialDays`, `delayWarnPercent`, `timeZone` (one save) | number, number, text | rows 14, 80, `Africa/Cairo` | body `[{"key":"delayWarnPercent","value":null},{"key":"trialDays","value":null},{"key":"timeZone","value":null}]`, "3 parameters saved."; no rows; fields show the defaults 14, 80, Africa/Cairo |
| `defaultLanguage` | choice | set to Arabic and saved (row `ar`, "Changed") | body `{"key":"defaultLanguage","value":null}`; no row; field English |

Platform rows left: `labelCopies` 1 and `passwordMinLength` 8 (untouched). Typing a value equal to the default and saving
answers "Nothing was changed." and stores nothing, so the three rows that held default values before the run cannot be put back
through the page; the effective values are unchanged.

### 8 · Smoke · PASS

- `platform.admin`, English: 33 / 33 menu pages open on their own URL with a title (Platform overview … Profile); no API
  response ≥ 400; no raw dictionary key.
- Arabic: 33 / 33, every title in Arabic (نظرة عامة على المنصة … الملف الشخصي), no response ≥ 400, no raw key.
- Console, errors only, whole run: 401 × 5 (start-up refresh on each `/login` load), 422 × 4 and 400 × 1 (the deliberate image
  refusals), 502 × 1 (the 2 MB image through the validation proxy, Z-01). No other error.

## 3. Not tested, and why

| Item | Reason |
| --- | --- |
| A laboratory's printed / previewed result report with the template logo and signature | No laboratory clinical group can be linked to a report template in Release 1 (Z-02). |
| Subscription debit, ledger row, next-charge move, trial skip (C-8) | No wallet is due on this host before 2026-10-26; no page moves the date. |
| Sessions from different days (C-7) | Sessions live at most 8 hours and end after 30 idle minutes. |
| *Add PCR kit* notice on screen | The page is Release 2 and refused. |
| 1 and 2 later-release permissions | No role holds exactly 1 or 2 such keys; the role editor cannot add one. |
| Printing | No print button was clicked (instruction). |

## 4. New findings

| Id | Sev | Finding | Evidence |
| --- | --- | --- | --- |
| **Z-01** | S4 | A report-template image larger than about 576 KB is refused with the wrong message. Between 512 KB and 576 KB the page says "An image may not exceed 512 KB." (`CAT-271`); above the request limit (`MaxImageRequestBytes` = 512 KB + 64 KB) the API answers 400 `VALIDATION-400` "Request body too large", which the page shows as "Please check the entered values. The value has the wrong format.". Through the validation proxy a 2 MB file even ends in 502 "Something went wrong on the server." (behind IIS not checked). A user who picks a large photo is not told the limit. | Item 2: 600 KB → 400, 2 MB → 502 on 4300 and 400 on 5102. |
| **Z-02** | **S3** | A laboratory's result reports never use a report template, so the logo and signature of F-02 never print in a laboratory. Provisioning copies the templates and their images but drops the clinical group's report-template link (`MasterDataCloner` calls `CatalogueACloner.CloneAsync` without the template map, before Catalogue B is cloned), and the Release-1 client portal has neither a report-templates page nor a Report template field on *Edit clinical group*. | Item 2: platform V20G → V20 Arabic Report and V14CG → V11 Results Template; their copies in V20 Closing Lab have `report_template_id` null; 0 laboratory groups link any template in the database. |
| **Z-03** | S4 | Counts inside Arabic messages use Latin digits ("3 صلاحيات", "25 صلاحية", "3 أعضاء", "تم تنفيذ الخصم: 0 مخصوم، 0 متخطى، 0 فشل.") while the same screens show Arabic-Indic digits elsewhere (tab badge "صلاحيات الشاشات ٦" / "٧٦"). The release notes say Arabic screens use Arabic-Indic digits everywhere. | Items 4 and 5. |

Observations (no finding):

- *Remove* on a template image acts at once, without a confirmation; the file is kept on disk and can be uploaded again.
- After a timeout change, a session keeps the timeout of its profile until the next refresh (item 1).

## 5. Database checks

| Check | Result |
| --- | --- |
| Idle timeout (item 1) | `sessionTimeoutMinutes` 30 (row) → 2 → no row; `SESSION_REVOKED` `idle_timeout` + `refresh_after_expired` for the late refresh; browser sign-out `logout` at 14:50:16Z |
| Template images (item 2) | platform template with logo and signature file ids; files on disk; laboratory copy with new ids, byte-identical images |
| Group links after provisioning (item 2) | V20 Closing Lab: CHEM, V11CG, V14CG, V20G all `report_template_id` null |
| Roles (item 4) | Operations Admin, V15 Role, Master Data Reviewer, Platform Administrator keys unchanged |
| Wallets (item 5) | 5 `TopUp` rows only; next-charge dates unchanged; no failure event |
| Settings (item 7) | platform rows left: `labelCopies`, `passwordMinLength` |
| Outbox, run window | platform tenant 11 events, V20 Closing Lab 72 events: all processed, none dead-lettered, 0 retries |
| Sessions at the end | no live session of platform.admin, uat.sup2, uat.mdr or v20lab.admin |

## 6. Settings changed and restored

| Setting | Change | Now |
| --- | --- | --- |
| Platform `sessionTimeoutMinutes` | row 30 → 2 (14:46Z) | no row (default 30), 14:51Z |
| Platform `trialDays`, `delayWarnPercent`, `timeZone` | rows holding the defaults removed by *Use the default* (item 7) | no rows; effective values 14, 80, Africa/Cairo as before |
| Platform `defaultLanguage` | Arabic (twice, a few seconds each) | no row (default English) |
| `platform.admin` language | العربية for the Arabic checks | English again (`preferred_language` `en`) |
| Roles | four permission changes opened and cancelled, then discarded | unchanged |

## 7. Data created (prefix V20, invented)

| Record | Id / key | Where |
| --- | --- | --- |
| Report template **V20 Arabic Report** (Results, Arabic) with logo and signature | `01a1309b-cb16-7342-a8f4-a53c3058ec65` | platform catalogue |
| Clinical group **V20G** "V20 Chemistry" (Report template V20 Arabic Report) | `01a1309e-2d57-7d72-a279-1a8dd9fe1d60` | platform catalogue |
| Test **V20K** "V20 Potassium" (Numeric, mIU/L, SER, 1 decimal) | `01a1309e-85c6-7416-902c-45ba80aadfd2` | platform catalogue |
| Service **V20K** (Test, member V20K) | `01a1309e-f735-7d16-acf2-9c0c6361c0cb` | platform catalogue |
| Laboratory **V20 Closing Lab** (Manual, Active, STARTER, Egypt / Cairo, licence V20-LIC-001, mobiles +201020202001 / +201020202002, e-mail v20.closing@example.test) | tenant `01a1309f-ac4b-7b92-a291-bf846b3b2f14`, slug `v20-closing-lab`; wallet 0.00 EGP, next charge 2026-11-12 | Add client |
| User `v20lab.admin` (primary administrator) | — | V20 Closing Lab; its password is not recorded anywhere (instruction); use *Reset administrator password* on Clients to use the account |
| Uploaded files | 5 platform files (2 kept after replace/remove), 2 laboratory copies | `.local/files/` (git-ignored) |
| Notification file | `20261012T1458538983822Z-sms-752098f3c7bc.txt` (`portal-url`, +201020202002) | `.local/mail-pickup/validation` |
| Charge runs | 3 runs, nothing charged | SA-05 |
| Sessions | Node sessions of `uat.sup2` (2) and `uat.mdr` (1): ended (idle, `AUTH-017`) or terminated on the Sessions page | — |

The scratch images (`v20-logo.png`, `v20-logo-2.png`, `v20-signature.png`, `v20-image.gif`, `v20-fake.png`, padded PNGs) and the
Node client live in the session scratchpad only.
