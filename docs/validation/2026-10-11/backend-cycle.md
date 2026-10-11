# Backend cycle validation: API to PostgreSQL (tester D)

| | |
|---|---|
| Date | 2026-10-11 |
| Release under test | v0.3.8 (`3a83e9c`), no application code, migration or test changed |
| API | validation instance `http://127.0.0.1:5102`, plus the installed site `https://192.168.80.38:5443` for the deployed path |
| Database | `lis` on the private PostgreSQL 17 at `127.0.0.1:5433`, read as `lis_owner`; one read-only probe as `lis_app` |
| Script | [`tests/validation/backend-cycle.mjs`](../../../tests/validation/backend-cycle.mjs) (Node 24 ESM, no dependencies) |
| Final verdict | **36/36 steps passed** (320 database assertions, 0 failed); deployed path **5/5 passed**; 1 minor defect (S3) |

## 1. Purpose

The question was whether data flows correctly from end to end. A request the API accepts should be stored correctly in PostgreSQL, with tenant isolation, the audit ledger and the outbox all behaving as specified. The script calls the real API the same way the SPA does:

- the compact-JWE credential envelope (RSA-OAEP-256 + A256GCM, with Node `crypto` reimplementing `frontend/src/app/core/crypto/credential-envelope.ts`);
- `X-Requested-With: Platform` on every request;
- the two cookie endpoints with `Origin` and `Sec-Fetch-Site`;
- branch selection;
- one session per user, reused.

After every write it reads the database and asserts:

- **What was stored:** the exact values (status, amounts, reference numbers), plus `tenant_id` and the foreign keys.
- **The audit rows:** every request carries its own `X-Correlation-Id` (`v11-<run>-<nnn>`), which the API writes into `audit.field_changes.correlation_id` and `audit.audit_trail.correlation_id`. The script asserts the entity/action pairs, the event types, the tenant, the acting user and, where one applies, the reason.
- **The outbox:** the transaction's events are the `ops.outbox_events` rows whose `id` equals `audit.audit_trail.event_id` for that correlation id. The script polls them for up to 20 s until the outbox worker has set `processed_at_utc`. None may be dead-lettered.
- **The outbox policies' side effects:** samples and results created after registration, and a result cancelled after a line cancellation. The script polls the rows those policies write.

## 2. How to run

From the repository root (the private PostgreSQL on 5433 and the API must be running):

```text
node tests/validation/backend-cycle.mjs                         # full cycle against http://127.0.0.1:5102
node tests/validation/backend-cycle.mjs http://127.0.0.1:5100   # same, another base URL
node tests/validation/backend-cycle.mjs --smoke                 # envelope + platform.admin sign-in/me/sign-out only
node tests/validation/backend-cycle.mjs https://192.168.80.38:5443 --deployed --insecure-tls
                                                                # read-mostly run on the installed site, reusing the
                                                                # client of the last full run (state file)
```

- **Output:** a PASS/FAIL line per step, a Markdown step table, and the full step records as JSON in `.local/validation/backend-cycle-<run>.json`. Every step lists its request, expected result, observed result and each database assertion. The exit code is `0` when every step passed and `1` otherwise.
- **Repeatable:** the first run creates the client **"V11 API Lab"**. Later runs create `V11 API Lab <run>` with the administrator `v11api.admin.<run>`, so every run asserts on fresh data. The generated passwords are appended to `.local/dev-credentials.md`, and the state for the deployed path is kept in `.local/validation/backend-cycle.state.json`. Both files are git-ignored.
- **Database access:** `psql.exe` runs as a child process. The passwords come from `deploy/postgres/.env` (`LIS_OWNER_PASSWORD`, `LIS_APP_PASSWORD`) and are passed only through `PGPASSWORD`. Every statement is a `SELECT`. The only session opened as `lis_app` is `BEGIN TRANSACTION READ ONLY … ROLLBACK`.
- **TLS:** certificate verification is switched off only by `--insecure-tls`, and only on the HTTPS agent of that run.

## 3. Runs

| Run | Target | Result | Notes |
|---|---|---|---|
| 261011073542 | 5102 `--smoke` | 1/1 | Envelope and cookie endpoints confirmed before writing anything |
| 261011073546 | 5102 full | 33/36 | Created **V11 API Lab** (`01a129e3-a571-73da-b6c7-9c5be69cc7c2`). The three failures were wrong expectations in the script, not product faults (see §7): an event name (`VisitLineCancelled`, not `…LinesCancelled`); a per-tenant accession number that the demo lab also uses (`ACC-2026-00001`), which the leak check had matched as text instead of by id; a NULL outbox `tenant_id` that is by design for platform events. All three were corrected |
| 261011073824 | 5102 full | 36/36 | V11 API Lab 261011073824 (`01a129e6-118c-7726-bf8c-0f5390d24f76`) |
| **261011074027** | 5102 full | **36/36** | **Reference run for this report.** V11 API Lab 261011074027 (`01a129e7-ee43-7ccf-a0d1-c643574e1cb9`). Adds the no-secret assertions |
| **261011074122** | 192.168.80.38:5443 `--deployed` | **5/5** | Ran against the tenant of run 074027 |

## 4. Results: full cycle (run 261011074027)

"Audit + outbox" in the DB column means:

- the expected `field_changes` entity/action rows and `audit_trail` events are present for the request's correlation id;
- every audit row carries this client's `tenant_id` (and, where asserted, the acting user);
- the outbox rows of the same transaction were all processed within the time shown, with none dead-lettered.

| Step | Request | Expected | Observed | DB check | Verdict |
|---|---|---|---|---|---|
| P-01 | `GET /auth/credential-challenge`, `POST /auth/sign-in` (password sealed) as platform.admin | 200 signed-in, platform tenant, refresh cookie | 200, cookie `rt` HttpOnly SameSite=Strict (dev host) | `identity.refresh_sessions` row in the platform tenant, live | PASS |
| P-02 | `POST /platform/clients` (adminPassword sealed): Egypt/Cairo, plan STANDARD, admin `v11api.admin.<run>` | 201 `{id, portalUrl, administratorUsername}` | 201, `https://v11-api-lab-261011074027.skylis.app` | 33 ok, including: `saas.tenants` (Active/Manual, country, governorate, plan, slug, administrator id and name, manager mobile in E.164); `saas.wallets` (EGP, 0); one MAIN branch named after the client; `identity.platform_users` (Argon2id hash, id = `tenants.administrator_user_id`); primary membership with role "Laboratory Administrator" (93 laboratory keys, no platform key, every special right, cap 100 %); master-data copy (every active platform row of clinical groups, sample types, units, tests and services copied, with copied services pointing at the tenant's own groups); security event `tenant:<id>`; audit (24 field changes: Tenant, Wallet, PlatformUser, TenantMembership, Branch, Role and the catalogue copy; 25 trail events from TenantProvisioned to ServiceCreated, all under the new tenant); outbox 25/25 processed in 0.1 s; no password, hash or security stamp in 74 audit/outbox payloads | PASS |
| A-01 | sign-in as the new administrator; `POST /auth/select-branch {MAIN}` | 200; branch bound to the session | 200, 200 | `refresh_sessions.tenant_id` = new tenant, `branch_id` = MAIN | PASS |
| A-02 | `POST /auth/refresh` (cookie, Origin, Sec-Fetch-Site); then without Origin | 200, rotation, branch kept; refused without Origin | 200; `me` 200; 403 CSRF-403 without Origin | live session keeps MAIN | PASS |
| A-03 | `PUT /settings` reviewBeforeVerify = true | 200 | 200, item value true, not default | `app.tenant_settings` value true, `updated_by_user_id` = admin; `TenantSetting:Created` in `audit.field_changes` (no domain event, so no outbox row) | PASS |
| A-04 | `POST /organization/branches` code `v11b2` | 201 | 201 | row V11B2 (code upper-cased), EGP, active, created_by admin; audit + outbox 1/1 in 0.7 s | PASS |
| C-01 | `POST` clinical group V11G, sample type V11S, unit V11-mmol/L | 201 ×3 | 201 ×3 | three rows with the sent values in the tenant; audit + outbox for each (≤ 1.3 s) | PASS |
| C-02 | `POST /catalogue/tests` V11K (Numeric, unit, 1 decimal); `PUT …/range-bands` 3.5–5.0, panic 2.5/6.5 | 201, 204 | 201, 204 | `app.tests` FKs (group, sample type, unit), decimals 1; one `test_range_bands` row with the four limits; LabTestCreated + LabTestRangesChanged processed | PASS |
| C-03 | `POST` V11L (RestrictedList); `PUT …/list-values` ".", Negative, Positive (critical) | 201, 204 | 201, 204 | three `test_list_values`, Positive critical; LabTestListValuesChanged processed | PASS |
| C-04 | `POST /catalogue/services` V11K and V11L (type Test, one member each) | 201 ×2 | 201 ×2 | `app.services` + `app.service_members` (member = test id, tenant); ServiceCreated processed | PASS |
| O-01 | `POST /organization/roles` "V11 Technician" (8 keys); `PUT …/result-rights` V11G S R V E P; `PUT …/special-rights` (modify verified only) | 201, 204, 204 | 201, 204, 204 | `app.roles` with exactly the 8 keys and the special rights; `app.role_result_rights` row for V11G; RoleCreated processed | PASS |
| O-02 | `POST /organization/users` (password sealed), role V11 Technician, branches [MAIN] | 201 | 201 | `platform_users` active, no forced change; membership `role_id` and `branch_ids = [MAIN]`; audit actor = admin; 2/2 events processed; no secret in 6 payloads | PASS |
| R-01 | `POST /pricing/base-price-lists` v11-2026; `PUT …/lines` (150 / 100); `POST …/activate` | 201, 200 `{added:2}`, 204 | as expected | list V11-2026 active; two lines 150 / 100 in the tenant; BasePriceListActivated processed | PASS |
| R-02 | `POST /pricing/payers/default`; `POST /pricing/payers` V11 Corporate (Credit); `POST …/tiers` V11 Gold | 200 created, 201, 201 | as expected | cash payer (Cash, default) with its default tier; payer search key `V11CORP`; tier → payer; PayerCreated, ContractTierAdded processed | PASS |
| T-01 | `POST /treasuries` v11cash on MAIN; `POST /treasury-access-requests` (device `v11-api-device`) by the admin | 201, 201 | 201, 201 | treasury V11CASH on MAIN; request Pending `TRQ-2026-00001`, requested_by admin, device; `reference_counters` row; TreasuryAccessRequested processed | PASS |
| T-02 | approve as the requester; approve as the technician | 403 SOD-TRS-001, nothing written; 204 | 403 SOD-TRS-001; 204 | still Pending and no audit row after the refusal; then Approved with `approved_by_user_id` = tech and comment; audit actor = tech; TreasuryAccessApproved processed | PASS |
| T-03 | `POST /catalogue/reasons` (Refund) | 201 | 201 | `app.reasons` kind Refund; audit actor admin; ReasonCreated processed | PASS |
| G-01 | `POST /patients` "V11 Mona Validation" | 201 `{id, number}` | 201 `PAT-2026-00001` | patient row (number, name, gender, date of birth, Active, tenant); PatientRegistered processed | PASS |
| V-01 | `POST /registration/visits` cash payer, V11K + V11L | 201 ACC-…, 1 tube | 201 `ACC-2026-00001`, 1 tube | visit FKs (patient, branch, payer, tier, registered_by), Open, totals 250 / 250 / 0, offset 180; two lines 150 / 100 on one tube (V11G / V11S); VisitRegistered processed; **policies:** sample `SMP-2026-00001` created and Received (autoCollect/autoReceive), two results Ordered with tenant, branch and group | PASS |
| B-01 | `POST /billing/payments` 100 Cash | 201 RCT-…, Partial | 201, account Partial, remaining 150 | `app.payments` (100, Cash, Posted, cashier, branch, treasury, device, receipt number); `visit_accounts.paid_total` 100; PaymentRecorded processed | PASS |
| B-02 | `POST /billing/payments` 150 Card `SLIP-V11` | 201, Paid | 201, Paid, remaining 0, 2 payments | payment row; `paid_total` 250 | PASS |
| E-01 | tech: `GET /results?accession=`; `PUT /results/{id}` 7.2 | case with 2 rows, reviewBeforeVerify visible; 204 | as expected | result Complete, 7.2, CriticalHigh, `is_panic`, `entered_by` tech; range and unit copied onto the result; Value log row; ResultEntered + ResultPanicDetected processed | PASS |
| E-02 | verify before review; review; verify | 409 RES-016; 204; 204 | 409 RES-016; 204; 204 | unchanged after the refusal; Reviewed then Verified, each stamped with the tech; ResultReviewed, ResultVerified processed | PASS |
| E-03 | amend without a reason; amend to 4.6 with a reason | 400 VALIDATION-400; 204 | 400; 204 | 4.6, Normal, not panic, `amendment_count` 1, status back to Complete; log 7.2 → 4.6 with the reason; `field_changes.reason` = the reason; ResultAmended processed | PASS |
| V-02 | `POST /registration/visits/{id}/lines/cancel` V11L with a reason | 204; RefundDue 100 | 204; account RefundDue, remaining −100, refundable 100 | line Cancelled with reason, actor and time; visit `patient_share_total` 150; `field_changes.reason`; VisitLineCancelled processed; **policy:** result V11L Cancelled in 0.1 s, V11K untouched | PASS |
| B-03 | `POST /billing/refunds` 100 Cash, reason V11 Overpayment; then 1 more | 201 RFD-…, Paid; 409 BIL-011 | as expected | refund row (reason FK, note, cashier, treasury, device, number); `visit_accounts` paid 250, refunded 100; RefundRecorded processed | PASS |
| S-01 | `POST /auth/sign-out` (cookie, Origin) for the 4 sessions; `me` with the old token | 204 ×4; 401 | 204 ×4; 401 AUTH-006 | no live refresh session left for the V11 users | PASS |

## 5. Negative and integrity checks

| Step | Check | Observed | DB check | Verdict |
|---|---|---|---|---|
| N-01 | Validation: patient with an empty name | 400 `application/problem+json`, `code` VALIDATION-400, `status`, `title`, `errors.FullName`, `correlationId` = the `X-Correlation-Id` header | no row written | PASS |
| N-02 | Duplicates: the same unit again (case changed); four concurrent identical clinical-group creates | 409 CAT-102. Concurrent run: one 201 and three 409, all **UNIQUE-409** (the index decides when the handler's pre-check is overtaken; run 073546 also gave one CAT-100) | exactly one V11RACE row | PASS |
| N-03 | Stale concurrency token: patient detail `rowVersion`, update with it, then update again with the same value | 204, then 409 **CONCURRENCY-409** | `rowVersion` = the row's `xmin`; the stale write was not applied (city stays Giza) | PASS |
| N-04 | Permission: the technician (8 keys) creates a payer and changes a setting | 403 AUTHZ-001 ×2 | no payer row; setting unchanged | PASS |
| N-05 | Cross-tenant: lab.admin (demo laboratory) reads the V11 patient, visit, account, result log, test and treasury request; searches by name, accession and payments; audit entries of the V11 patient | 404 on all six direct reads (PAT-404, REG-404, BIL-404, RES-404, CAT-195, TRS-405). Searches return none of the V11 ids or names. The lookup by `ACC-2026-00001` returns the demo lab's **own** visit (accession numbers are per-tenant), and audit entries are empty | no audit row written in the demo tenant | PASS |
| N-06 | Row-level-security probe as `lis_app`, read-only: `set_config('app.bypass_rls','off')`, `set_config('app.current_tenant', <demo tenant>, false)` | demo tenant: 0 patients, visits, results, payments and audit rows of V11; own tenant (control): 1 / 1 / 2 / 2 / 95; no tenant set: 0 | as listed | PASS |
| N-07 | `GET /audit/entries?entityId=<patient>`, `GET /audit/verify-chain`; technician without `audit.view` | 200 with Created + Modified field changes; 200 `{ok:true, checked:68}`; 403 | `checked` = the 68 `audit_trail` rows of the tenant; sequence gap-free 1…n; every `prev_hash` = the previous `entry_hash` | PASS |
| N-08 | Append-only audit tables (definitions read, no write attempted) | n/a | `audit_trail_append_only`, `field_changes_append_only`, `security_events_append_only`: `BEFORE DELETE OR UPDATE … EXECUTE FUNCTION audit.refuse_change()`, enabled; the function raises; `lis_app` holds INSERT only (no UPDATE, DELETE or TRUNCATE). No TRUNCATE trigger (see D-01) | PASS |
| N-09 | Inventory of tables with a `tenant_id` column | 78 tables; every `app.*` and `audit.*` table has RLS enabled and forced, with a policy | without RLS (outside app/audit): `identity.refresh_sessions`, `identity.machine_credentials`, `ops.outbox_events`, `saas.wallets`, `saas.subscription_requests` (see O-3) | PASS |
| P-02 / O-02 | No credential material in the audit ledger or the outbox | n/a | 74 + 6 payloads (`field_changes.changes`, `audit_trail.payload`, `outbox_events.payload`) contain no plaintext password, no `$argon2…` hash, and no PasswordHash, SecurityStamp or RefreshTokenHash field | PASS |

## 6. Deployed path (installed site, run 261011074122)

The installed site `https://192.168.80.38:5443` (IIS/ARR in front of the service) writes to the same `lis` database. The run signed in as `v11api.admin.261011074027` and touched nothing outside that tenant.

| Step | Request | Observed | DB check | Verdict |
|---|---|---|---|---|
| D-01 | sealed sign-in (audience `POST /api/v1/auth/sign-in`, no path base); `select-branch` MAIN (the tenant now has two active branches) | 200; production cookie `__Secure-rt` with Path, Secure, SameSite=Strict, HttpOnly; 200 | the refresh session lands in `lis@5433` for the V11 tenant | PASS |
| D-02 | `GET /patients?search=V11` | 200 with the V11 patient; HSTS and nosniff headers present | n/a | PASS |
| D-03 | `POST /catalogue/units` `V11-dep-261011074122` | 201 | row in the V11 tenant; MeasurementUnit:Created in the audit; outbox 1/1 processed in 0.1 s | PASS |
| D-04 | `GET /platform/clients` as the laboratory administrator | 403 AUTHZ-001 | n/a | PASS |
| D-05 | `POST /auth/sign-out` (Origin `https://192.168.80.38:5443`, Sec-Fetch-Site same-origin); `me` | 204; 401 AUTH-006 | n/a | PASS |

## 7. Defects

| Id | Severity | Title | Evidence | Suspected root cause |
|---|---|---|---|---|
| D-01 | S3 minor | The append-only guarantee of the audit ledger does not cover `TRUNCATE` | N-08: the three triggers are `BEFORE DELETE OR UPDATE … FOR EACH ROW`, and `pg_trigger` shows no TRUNCATE trigger (`tgtype & 32 = 0`) on `audit.audit_trail`, `audit.field_changes` or `audit.security_events`. The runtime role cannot truncate (`lis_app` has INSERT only, from `deploy/postgres/schema.sql` line 25). The owner role `lis_owner`, which runs migrations and owns the tables, can still empty them without the trigger firing. The hash chain cannot reveal a truncation: `verify-chain` walks the rows that exist, so an emptied chain has nothing left to break (not exercised; no write was attempted). The same pattern guards `app.payments` (`payments_guard`) and `app.refunds` (`refunds_immutable`) | `src/Platform.Infrastructure/Persistence/Migrations/20261009100252_Foundation.cs` lines 207–209 (and `20261010052829_Modules.cs` lines 4224–4227) create only `BEFORE UPDATE OR DELETE … FOR EACH ROW` triggers. A `BEFORE TRUNCATE … FOR EACH STATEMENT` trigger calling the same refusal (and a gate test) would close it. A per-tenant anchor, such as the last sequence kept outside the table, would let `verify-chain` detect a missing tail |

No S1 or S2 defect was found. Every write the API accepted was stored with the right values, tenant and keys, and was audited with the acting user, reason and correlation id. Every outbox event of those transactions was processed within about 2 s, the slowest observed being 1.9 s.

### Observations (not defects; recorded for the owners)

| Id | Observation | Source |
|---|---|---|
| O-1 | Events of aggregates that are not tenant-scoped (`TenantProvisioned`, `TenantActivated`, `WalletToppedUp` …) are enqueued with `ops.outbox_events.tenant_id = NULL`, while `audit.audit_trail` files the same event under the ambient tenant (the new tenant for provisioning, the platform tenant for activation). This is consistent with the code, but the two ledgers attribute the event differently, which matters for anyone joining them by tenant | `OutboxInterceptor.cs:49` vs `AuditTrailInterceptor.cs:88` |
| O-2 | `ResultPanicDetected` is processed by the outbox with no subscriber, so no `panic-value` notification is sent yet. The intake marks this as deferred ("Reacts: Notifications … [later]") | `docs/intake/LIS-INT-RES-001_Results.md` line 226 |
| O-3 | `saas.wallets` and `saas.subscription_requests` carry `tenant_id` without RLS. They are platform-schema tables, read by the lab only through `/api/v1/wallet`, so isolation there rests on the handler filter alone. `identity.refresh_sessions` and `machine_credentials` are without RLS by D-06, and `ops.outbox_events` by the processor design | N-09 |
| O-4 | Reference numbers are per-tenant counters, so `ACC-2026-00001`, `PAT-2026-00001`, `TRQ-2026-00001` and so on repeat across laboratories. Support staff and any cross-tenant tooling must key on ids, never on these numbers | N-05 |
| O-5 | After an amendment the result goes back to Complete, and its reviewed and verified stamps are cleared until it is verified again (history stays in `result_log_entries`). Visit lines keep the status Ordered while results progress; the line model has only Ordered, Reserved and Cancelled. The sample keeps both service ids after one of its two lines is cancelled (it is cancelled only when no live line is left). All three behaviours follow the intakes | E-03, V-02 |
| O-6 | The platform's global catalogue was changing during the session because other testers were adding V11 rows, so each new client received a different copy: run 074027 got CHEM, V11CG, SER, V11ST, mIU/L, V11u/L, TSH, V11T, TSH, V11AS, V11SVC. The script compares source and target at run time rather than fixed lists | P-02 |

## 8. Data created

All of it is inside the V11 clients, except the read-only sessions of platform.admin and lab.admin (signed out at the end of each run). The passwords are in `.local/dev-credentials.md` and are not reproduced here, because this repository is public.

| Run | Client (tenant id) | Users | Records |
|---|---|---|---|
| 073546 | V11 API Lab (`01a129e3-a571-73da-b6c7-9c5be69cc7c2`), slug `v11-api-lab` | `v11api.admin`, `v11api.tech` | Each full run created: branch V11B2; clinical groups V11G and V11RACE; sample type V11S; unit V11-mmol/L; tests V11K and V11L; services V11K and V11L; role V11 Technician; base price list V11-2026; cash payer, payer V11 Corporate with tier V11 Gold; treasury V11CASH with request `TRQ-2026-00001` (Approved, device `v11-api-device`); reason V11 Overpayment; patient `PAT-2026-00001` "V11 Mona Validation"; visit `ACC-2026-00001` (V11L cancelled); two payments (100 Cash, 150 Card); refund 100; setting reviewBeforeVerify = true |
| 073824 | V11 API Lab 261011073824 (`01a129e6-118c-7726-bf8c-0f5390d24f76`) | `v11api.admin.261011073824`, `v11api.tech.261011073824` | same set |
| 074027 | V11 API Lab 261011074027 (`01a129e7-ee43-7ccf-a0d1-c643574e1cb9`) | `v11api.admin.261011074027`, `v11api.tech.261011074027` | same set, plus unit `V11-dep-261011074122` (deployed path) |

All three clients are Active on plan STANDARD with an empty EGP wallet. To remove them later, deactivate them through Super Admin › Clients. The script never deletes anything.
