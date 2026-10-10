# LIS-INT-INT-001 — Integration: analysers, mappings, site agents, PCR kits, machine credentials (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `integration` (Sky LIS Client Portal CP-58 Analysers, CP-59 Analyser tests, CP-60 Analyser samples, CP-61 Site agent, CP-62 PCR kits; workflow WF-07) and the platform capability **machine credentials** (02 §4.2–§4.3, §5.2 `machine-token`, §6.3) |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), the LDM synthesis (`docs/knowledge/ldm/LDM_Business_Knowledge.md` §9–§10, manual ch. 13–14) and the architecture pack. **For review by the business owner**; every assumption is listed in §16. Release R2 (SRS). |
| Version | 1.0 — 2026-10-09 |
| Implemented in | `src/Platform.Domain/{Integration,Identity/MachineCredential*}`, `src/Platform.Application/{Integration,Identity/MachineCredentials}`, `src/Platform.Infrastructure/Persistence/{Integration,Identity/MachineCredential*}`, `src/Platform.WebApi/Controllers/{Integration,Identity/MachineTokenController.cs}`, `src/Platform.WebApi/Security/RateLimiting.cs` (`machine-token`), `frontend/src/app/features/integration`, `frontend/src/app/core/api/integration.contracts.ts` |
| Build slot | `int` (wave 3) |

## 1. Purpose and policy basis

Laboratory instruments produce most results. A result must reach the patient's report **only** when the laboratory
has said that this instrument's code means this test and that results for it may land ("Auto Send", CP-59; LDM §9:
"permission to save incoming results — without it nothing lands"). Every incoming value must be traceable to the
instrument, the agent that relayed it, the raw value and the conversion applied, and every refusal must be visible
(WF-07: "unmapped or Auto-Send-off codes refused and logged"). PCR instruments emit Ct values, not results; the
verdict is computed from the kit's interpretation sheet, and files from another instrument must be refused (CP-62,
LDM §10, manual §14.3–§14.6).

The connection to instruments is made by a **site agent** installed in each branch (CP-61). LDM ran one Windows
service per machine (and per kit for PCR) with a plaintext, hand-encrypted configuration (LDM §9 "Service", §13
"one service per machine … plaintext config needing manual encryption"). Sky LIS replaces that with one agent per
site that authenticates to the cloud with a **machine credential** (client id + secret shown once, 02 §6.3), pulls its
configuration and pushes heartbeats, logs and results to a JSON API. OQ-08 (instrument protocols of the pilot
laboratory) is open: the agent speaks ASTM / HL7 / file to the instruments and translates to this API. **This intake
covers the cloud side only.**

## 2. Scope and out of scope

In scope:

- Machine credentials for the platform (first consumer: site agents): issue, list, rotate, revoke; token exchange and
  refresh rotation with reuse detection; the `machine-token` rate limit; security events.
- Analysers (CP-58): definition, communication mode and link settings, behaviour switches, flags (Stop / Record),
  the site agent that hosts it, activation; "test connection" = the last heartbeat state.
- Analyser test mappings (CP-59): instrument code ↔ catalogue test, host code, result selection, slope/intercept,
  unit, query, Auto Send, conversion table, analyser-specific range, methodology; printable code sheet.
- Analyser sample codes (CP-60): sample type ↔ instrument sample code.
- Site agents (CP-61): per-branch agent with its machine credential, configuration (version pushed to the agent),
  heartbeat status (Online / Offline after 5 minutes / Error), log entries appended by the agent, the result intake log.
- PCR kits (CP-62): instrument, serial lock, run-file layout, value column, genes with thresholds, verdict rules,
  folders, activation; a pure interpreter and a simulation.
- Agent API: heartbeat, logs, configuration, download orders, results, PCR runs.

Out of scope (later intakes or other modules):

- Landing results on the visit (Results module, wave 3 `res`): this module raises `AnalyserResultReceived`; the Results
  policy creates or updates the result (Complete, analyser source, flags; BR-03 Received gate, overwrite rules).
- The site agent software itself (Windows service, ASTM/HL7/serial/file drivers, Excel→CSV conversion, file moves).
- Auto-verification (Automation module; Stop flags are carried on the event for it, CP-63).
- Culture mappings (LDM `Analyzer Culture Tests`, manual §13.7), QC (CP-22), the platform libraries SA-15 / SA-16
  (Platform administration module; this module only reads `saas.analyser_models`).
- Automatic registration of unknown samples or unordered tests (LDM `Append UnReg …`): the switches are stored, sent
  to the agent and carried on the event; the cloud never creates visits (§16 A6).

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Analyser, instrument, machine, الجهاز | `Analyser` aggregate (`app.analysers`) |
| Mode: uni-directional, bi-directional (query), download (load), shared folder | `AnalyserMode` Unidirectional \| Bidirectional \| Download \| SharedFolder |
| Link: serial `9600,n,8,1` + COM port, TCP/IP client/server, file share | `AnalyserConnection` Serial \| Tcp \| File (+ `TcpRole` Client \| Server) |
| Flag (stop / no-stop) | `AnalyserFlag` {Code, Action Stop \| Record, Description} |
| Analyser code, host code, R.S, slope, intercept, Auto Send, Result… table, Reference Range…, Methodology… | `AnalyserTestMapping` (`app.analyser_test_mappings`) with `ValueConversion` rows and `RangeOverride` |
| Analyser sample code | `AnalyserSampleCode` (`app.analyser_sample_codes`) |
| Site agent, connector, service | `SiteAgent` (`app.site_agents`) + machine credential |
| Agent log (hourly encrypted log in LDM) | `AgentLogEntry` rows (`app.agent_log_entries`) |
| Result intake log | `app.analyser_result_intake` |
| PCR kit, interpretation sheet, run file, Ct, IC | `PcrKit` (`app.pcr_kits`) with `PcrGene` rows; `PcrInterpreter` |
| Client id / client secret | `MachineCredential` (`identity.machine_credentials`) |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `integration.view` | read every register (analysers, mappings, sample codes, site agents, logs, intake, kits, analyser models), connection state, PCR simulation; **agent API reads** (configuration, download orders) |
| `integration.create` | **agent API writes** only (heartbeat, logs, results, PCR runs) — held by site-agent service roles, not by people |
| `integration.edit` | create and change analysers, mappings (including Auto Send, conversions, range override), sample codes, PCR kits |
| `integration.manage` | activate/deactivate analysers, mappings, sample codes and kits; site agents (create, rotate secret, push configuration, deactivate, reactivate); machine credentials (issue, list, rotate, revoke) |
| `integration.export` | the analyser code sheet document |

Integration engineers hold view + edit (+ manage for site agents); support holds view. Every agent-API request must
also come from a **site agent principal** (the session's user is the agent's service user), else `403 INT-035`.

## 5. Aggregates

- **MachineCredential** (root, `identity.machine_credentials`, no RLS like `refresh_sessions`; carries `TenantId`
  explicitly, every management read filters by the current tenant): `ClientId` (public, `lis-<32 hex>`, unique),
  `Name`, `SecretHash` (`v1.` + HMAC-SHA256 under the master-key subkey `machine-credential`, 02 §6.3 "keyed hash" —
  the secret is 32 random bytes base64url, shown once), `ServiceUserId`, `Scopes` (`text[]`, catalogued keys),
  `BranchId?` (sessions are bound to it), `ExpiresAtUtc` (default 1 year, max 2 years), `RevokedAtUtc`,
  `RevokedByUserId`, `RevocationReason`, `SecretRotatedAtUtc`, `LastUsedAtUtc`, audit stamps (created by/at).
  Each credential owns a **service identity**: a `PlatformUser` `svc.<client id>` with an unusable random password
  hash, a `TenantMembership` in the credential's tenant (branches = the credential's branch) and a `Role`
  "Service: <name>" holding exactly the scopes — so `SessionPrincipalQuery`, permissions and RLS work unchanged.
- **Analyser** (root): `Code` (1–16, `A-Z 0-9 - _`), `Name` (`LocalizedText`), `ClinicalGroupId`, `DefaultSampleTypeId?`,
  `AnalyserModelId?` (platform library), `Mode`, `Connection` (owned: kind, serial settings, serial port, host, port,
  TCP role, folder path), `AutoPrint`, `DownloadOrders`, `AppendUnregisteredTests`, `AppendUnregisteredSamples`,
  `OverwriteReviewed`, `OverwriteVerified`, `PrintCopies` (0–10), `Flags` (owned rows `app.analyser_flags`),
  `SiteAgentId?`, `IsActive`. Raises `AnalyserResultReceived` when a mapped, Auto-Send result is accepted.
- **AnalyserTestMapping** (root): `AnalyserId`, `TestId` (catalogue), `AnalyserCode` (unique per analyser), `HostCode?`,
  `ResultSelection` (1–99), `Slope` (≠ 0, default 1), `Intercept` (default 0), `UnitId?`, `Query`, `QueryDetails?`,
  `AutoSend` (default **off**), `Conversions` (owned rows `app.analyser_value_conversions`, ≤ 50, ordered),
  `RangeOverride?` (owned: low, high, text), `Methodology?`, `IsActive`.
- **AnalyserSampleCode** (root): `AnalyserId`, `SampleTypeId`, `Code` (1–16); one per sample type and analyser.
- **SiteAgent** (root): `Name` (unique), `BranchId`, `MachineCredentialId`, `ServiceUserId`, `ConfigJson` (JSON object
  ≤ 16 KB), `ConfigVersion` (≥ 1), `IsActive`. Its analysers are the analysers whose `SiteAgentId` is the agent
  (single source of truth on the analyser).
- **PcrKit** (root): `Name` (unique), `AnalyserId` (the instrument), `AnalyserTestCode` (the mapping the verdict lands
  through), `SerialLock`, `RunFileLayout` (owned: delimiter, sample/gene/well/content columns, patient content value,
  serial label, no-amplification tokens), `ValueColumn`, `Genes` (owned rows `app.pcr_kit_genes`: name,
  internal control, positive Ct max, borderline Ct max), `VerdictRules` (owned: minimum positive targets for
  DETECTED, verdict texts), `ResultFolder`, `BackupFolder`, `FailedFolder`, `IsActive`.
- Operational rows (not aggregates, not field-audited, tenant-keyed with RLS): `app.site_agent_states` (version,
  reported status, message, last heartbeat), `app.analyser_connection_states` (status, message, last seen),
  `app.agent_log_entries` (append-only), `app.analyser_result_intake` (append-only).
- Domain service **PcrInterpreter** (pure): run-file parsing, per-gene calls, verdicts.

## 6. State table

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Analyser / Mapping / SampleCode / PcrKit **Active** | in use by agents | all but the code (analyser) / analyser (mapping, sample code) | no | no | `integration.view` |
| … **Inactive** | ignored by agents and the intake | all | no | no (reactivate) | `integration.view` |
| SiteAgent **Active** | its credential may exchange tokens | name, configuration | no | no | `integration.view` |
| SiteAgent **Inactive** | credential revoked, sessions ended | name, configuration | no | no (reactivate issues a new credential) | `integration.view` |
| MachineCredential **Live** | not revoked, not expired | name | no | no | `integration.manage` |
| MachineCredential **Revoked** / **Expired** | refused at token exchange and refresh | none | yes | yes | `integration.manage` |

Derived (read-time) agent status: **Online** (heartbeat ≤ 5 min, no error reported), **Error** (heartbeat ≤ 5 min with an
analyser or the agent reporting Error), **Offline** (no heartbeat for 5 minutes, or never).

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| M1 | — → Live | Issue machine credential | `integration.manage` | name 1–100 (MCR-001), unique among live (MCR-100); scopes catalogued, 1–50 (MCR-002), held by the actor (MCR-004); expiry future ≤ 2 y (MCR-003) | grant ≤ own privileges | — | — | `MachineCredentialIssued` + `MACHINE_CREDENTIAL_CREATED` | no |
| M2 | Live → Live | Rotate secret | `integration.manage` | live (MCR-010) | — | — | — | `MachineCredentialSecretRotated`; live sessions revoked | no |
| M3 | Live → Revoked | Revoke | `integration.manage` | — (idempotent) | — | — | optional | `MachineCredentialRevoked` + `MACHINE_CREDENTIAL_REVOKED`; service user deactivated, sessions revoked | no |
| M4 | — | Token exchange | anonymous, `machine-token` limiter | client id known, secret matches, live, not expired, service user active → else `401 AUTH-002` (one code for every cause) | — | — | — | `MACHINE_TOKEN_ISSUED` / `MACHINE_TOKEN_REFUSED` (reason category), `RefreshSessionOpened` (kind Machine) | no |
| M5 | — | Token refresh | anonymous, `machine-token` limiter | machine session, credential still live; reuse revokes the family → `401 AUTH-012` | — | — | — | `RefreshSessionRotated` / `REFRESH_REUSE_DETECTED` | no |
| A1 | — → Active | Create analyser | `integration.edit` | code (INT-001) unique (INT-100); name (INT-002); clinical group known (INT-040); sample type known (INT-041); model known (INT-045); connection valid for the mode (INT-004…007); copies 0–10 (INT-008); flags unique (INT-009); agent known (INT-407) | — | — | — | `AnalyserRegistered` | no |
| A2 | Active/Inactive | Update analyser | `integration.edit` | as A1 except the code | — | — | — | — | no |
| A3 | Active ↔ Inactive | Deactivate / reactivate | `integration.manage` | idempotent | — | — | — | `AnalyserDeactivated` / `AnalyserReactivated` | no |
| T1 | — → Active | Map test | `integration.edit` | analyser exists (INT-404); test known (INT-042); code 1–32 (INT-010) unique per analyser (INT-101); slope ≠ 0 (INT-011); conversions (INT-012); range (INT-013); selection 1–99 (INT-014); unit known (INT-043) | — | — | — | `AnalyserTestMapped` | no |
| T2 | — | Update mapping / conversions / range override | `integration.edit` | as T1 | — | — | — | — | no |
| T3 | — | Switch Auto Send | `integration.edit` | mapping exists (INT-405) | — | — | — | `AnalyserAutoSendChanged` | no |
| T4 | Active ↔ Inactive | Deactivate / reactivate mapping | `integration.manage` | idempotent | — | — | — | — | no |
| S1 | — → Active | Add sample code | `integration.edit` | sample type known (INT-041); code 1–16 (INT-015); one per sample type and analyser (INT-103) | — | — | — | — | no |
| G1 | — → Active | Create site agent | `integration.manage` | name (INT-016) unique (INT-104); branch known (INT-044); configuration a JSON object ≤ 16 KB (INT-018) | — | — | — | `SiteAgentRegistered`, M1 (scopes `integration.create` + `integration.view`, branch bound) | no |
| G2 | Active | Rotate secret | `integration.manage` | active (INT-031) | — | — | — | M2, or M1 + `SiteAgentCredentialReplaced` when the credential is no longer live | no |
| G3 | Active | Push configuration | `integration.manage` | active (INT-031) | — | — | — | `SiteAgentConfigPushed` (version + 1) | no |
| G4 | Active → Inactive | Deactivate | `integration.manage` | idempotent | — | — | — | `SiteAgentDeactivated`, M3 | no |
| G5 | Inactive → Active | Reactivate | `integration.manage` | — | — | — | — | `SiteAgentReactivated`, M1 + `SiteAgentCredentialReplaced` | no |
| K1 | — → Active | Define PCR kit | `integration.edit` | name (INT-021) unique (INT-105); analyser exists (INT-404); genes: ≥ 1 target, unique names, 0 < positive ≤ borderline ≤ 60 (INT-022); layout (INT-023); folders (INT-024); verdict rules (INT-025); serial lock (INT-026) | — | — | — | `PcrKitDefined` | no |
| K2/K3 | — | Update / deactivate / reactivate kit | edit / manage | as K1 | — | — | — | `PcrKitDeactivated` / `PcrKitReactivated` | no |
| R1 | — | Agent: result | agent (`integration.create`) | per item (never refuses the batch): analyser assigned to the caller and active → else Refused; sample known → else UnknownSample; active mapping → else Unmapped; Auto Send → else AutoSendOff; else Accepted | — | — | — | `AnalyserResultReceived` per Accepted item | yes (agent) |
| R2 | — | Agent: PCR run | agent (`integration.create`) | kit of an analyser of the caller, active (else file Failed); serial = lock (INT-037, file Failed); file readable (INT-038, Failed); per sample as R1 with the verdict text | — | — | — | `AnalyserResultReceived` per Accepted verdict | yes (agent) |
| H1 | — | Agent: heartbeat / logs | agent (`integration.create`) | shape (INT-019, INT-020) | — | — | — | — (operational rows) | yes (agent) |

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-INT-001 | Analyser code 1–16 of `A-Z 0-9 - _`, unique per tenant | aggregate + handler + unique index `(tenant_id, code)` | INT-001 / INT-100 | "The analyser code is invalid." / "An analyser with this code already exists." |
| INV-INT-002 | Serial settings look like `9600,n,8,1` (baud 300–921600, parity n/e/o/m/s, data bits 5–8, stop 1/1.5/2) with a port `COMn` or `/dev/…`; TCP has a port 1–65535 and (client) a host; File has a folder; SharedFolder mode ⇔ File link; Download mode implies "download orders" | `AnalyserConnection.Create`, `Analyser` | INT-004…INT-007 | "Serial settings must look like 9600,n,8,1." … |
| INV-INT-003 | An analyser code maps one test per analyser | handler + unique index `(tenant_id, analyser_id, analyser_code)` | INT-101 | "This analyser code is already mapped." |
| INV-INT-004 | **A mapping without Auto Send never lands a result** | `Analyser.AcceptResult` | — (outcome `AutoSendOff`) | — |
| INV-INT-005 | Slope ≠ 0; conversions ≤ 50 with distinct source values; range low ≤ high | aggregate | INT-011 / INT-012 / INT-013 | |
| INV-INT-006 | One sample code per (analyser, sample type) | handler + unique index | INT-103 | |
| INV-INT-007 | Site-agent name unique per tenant; every agent has exactly one live credential while active | handler + unique index; G1/G2/G4/G5 | INT-104 | |
| INV-INT-008 | A PCR kit has at least one target gene, unique gene names, `0 < positiveCtMax ≤ borderlineCtMax ≤ 60` | `PcrKit` | INT-022 | |
| INV-INT-009 | A run file whose instrument serial differs from the kit's serial lock is refused | `PcrInterpreter` | INT-037 | "The file comes from another instrument." |
| INV-INT-010 | The agent API serves only site-agent principals and only the caller's analysers | handlers (`AgentContext`) | INT-035 / outcome Refused | |
| INV-MCR-001 | A machine secret is never stored, logged or returned after the creating/rotating response | `MachineCredentialSecrets` (keyed hash), `[CredentialField]` on every secret property | — | |
| INV-MCR-002 | A credential grants only catalogued keys the issuing actor holds | handler | MCR-002 / MCR-004 | |

## 9. Segregation-of-duties rules

```text
SOD-INT-001  A credential never grants more than its issuer holds.
  On M1 (and G1): every scope must be a permission the acting user holds.
  Refusal: MCR-004 "You cannot grant a permission you do not hold."   Configurable: no.
```

No maker/checker rule on configuration (assumed, §16 A3): mappings are configuration, every change is field-audited.

## 10. Approval gates

None (assumed, §16 A3).

## 11. Events and policies

```text
Event:    AnalyserResultReceived { AnalyserId, AnalyserCode, SampleNumber, SampleId?, TestId, MappingId, Value, RawValue,
          Flags[], Unit?, UnitId?, StopFlags[], MeasuredAtUtc, IntakeId, Source ("Analyser" | "Pcr"), OverwriteReviewed,
          OverwriteVerified, AppendUnregisteredTests, TenantId }
Raised by: R1, R2 (Accepted items only)
Reacts:   Results — "when received, then set the value of the sample's ordered result for TestId as Complete with the
          analyser source, flags and the overwrite rules" (idempotent on IntakeId)   [Results intake, wave 3]
          Automation — "a StopFlags entry holds the result from auto-verification" (CP-58 rule)   [Automation intake]
Timing:   within one outbox cycle

Event:    AnalyserRegistered / AnalyserDeactivated / AnalyserReactivated { AnalyserId, Code }          Reacts: none yet
Event:    AnalyserTestMapped { MappingId, AnalyserId, TestId, AnalyserCode, AutoSend }                 Reacts: none yet
Event:    AnalyserAutoSendChanged { MappingId, AnalyserId, TestId, AnalyserCode, Enabled }             Reacts: none (audit)
Event:    SiteAgentRegistered { AgentId, Name, BranchId, MachineCredentialId } / SiteAgentConfigPushed { AgentId, ConfigVersion }
          / SiteAgentCredentialReplaced { AgentId, MachineCredentialId } / SiteAgentDeactivated / SiteAgentReactivated
Event:    PcrKitDefined { KitId, Name, AnalyserId } / PcrKitDeactivated / PcrKitReactivated
Event:    MachineCredentialIssued { CredentialId, TenantId, ClientId, ServiceUserId } / MachineCredentialSecretRotated /
          MachineCredentialRevoked { CredentialId, ClientId, Reason? }   (global aggregate: outbox row without tenant; the
          security events below are the tenant's ledger, foundation A6)
```

Security events (02 §7): `MACHINE_CREDENTIAL_CREATED`, `MACHINE_CREDENTIAL_ROTATED`, `MACHINE_CREDENTIAL_REVOKED`,
`MACHINE_TOKEN_ISSUED`, `MACHINE_TOKEN_REFUSED` (detail = reason category only: `unknown_client`, `wrong_secret`,
`revoked`, `expired`, `inactive_user`; never the secret, never which field), `REFRESH_REUSE_DETECTED`.

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Analyser, mapping, sample code, kit, agent configuration | Configuration, field-audited | every change in `audit.field_changes` |
| Client id | Operational (public) | shown in lists |
| Client secret | **Credential** | shown once; stored as a keyed hash; never logged; `[CredentialField]` on every contract property; the token exchange carries it in a credential envelope (D-03) |
| Agent log message, intake raw/converted value, sample number | Operational, laboratory data | no patient name or demographics are stored or sent to agents (download orders carry sample number, accession, sample type code, test codes, urgency only) |
| Heartbeat state | Operational, overwritten | |
| Retention | intake and agent logs kept with the laboratory records (assumed 2 years, purge job not built; §16 A9) | |

## 13. Reference data and configuration

- Catalogue (read by raw SQL on the agreed tables): `app.clinical_groups`, `app.sample_types`, `app.tests`, `app.units`
  (id, tenant_id, code, name_en, name_ar, is_active); organization `app.branches` (id, tenant_id, code, name_en, name_ar,
  is_active); samples `app.samples`, registration `app.visits` / `app.visit_lines`, catalogue `app.service_members`
  (download orders, sample lookup); platform library `saas.analyser_models` (id, vendor, model, protocol,
  physical_link, is_active; read-only, `lis_app` SELECT granted in `Integration.migration.sql`).
- Constants: heartbeat offline threshold 5 minutes; default credential lifetime 365 days (max 730); log batch ≤ 500
  entries; result batch ≤ 500 items; run file ≤ 2 MB; orders window default 24 h, ≤ 500 samples.
- No tenant settings in this version.

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Analysers | search, active, clinical group, site agent, page | code, name, group, mode, link, agent, connection status, last seen, active | view |
| Mappings (per analyser) | search, active, page | test (code + name), analyser code, host code, R.S, slope, intercept, unit, query, Auto Send, conversions count, range override, active | view |
| Code sheet (document) | analyser | analyser header + every active mapping (test, analyser code, host code, unit, Auto Send) + sample codes | export |
| Sample codes (per analyser) | — | sample type, code, active | view |
| Site agents | search, branch, active, page | name, branch, client id, version, status, last heartbeat (age), analysers, configuration version | view |
| Agent log | from, to, level, search, page | time, level, analyser code, sample number, message | view |
| Result intake | from, to, status, analyser, agent, search, page | time, agent, analyser, sample number, analyser code, test, raw, converted, flags, status, reason | view |
| PCR kits | search, active, analyser, page | name, instrument, serial lock, genes, active | view |
| Machine credentials | include revoked, page | name, client id, scopes, branch, expires, last used, revoked | manage |

## 15. Notifications and escalations

None in this version. An agent going Offline is visible on the list (status badge, heartbeat age); notification
policies can subscribe later (no event is raised for a missed heartbeat because nothing changes in the database).

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Integration`, `tests/Platform.IntegrationTests/IntegrationModuleTests.cs`
and `tests/Platform.WebApi.FunctionalTests/IntegrationFlowTests.cs` / `MachineCredentialFlowTests.cs`.

```gherkin
Scenario S-M4-ok: a site agent exchanges its credential for tokens
  Given a site agent created with client id C and secret S (shown once)
  When the agent posts C and the sealed S to /api/v1/auth/token
  Then it receives an access token, its expiry and a refresh token; a Machine session bound to the agent's branch opens
Scenario S-M4-wrong: wrong secret or unknown client → 401 AUTH-002, the same body for both, MACHINE_TOKEN_REFUSED
Scenario S-M5-rotate: refresh rotates; presenting the previous refresh token again → 401 AUTH-012 and the family is revoked
Scenario S-M3-revoked: after revocation the token exchange is refused (AUTH-002) and refresh is refused (AUTH-012)
Scenario S-SCOPE: the agent's token reaches /api/v1/agents/* but is refused 403 AUTHZ-001 on /api/v1/integration/analysers (POST)
Scenario S-A1-serial: serial settings "9600-n-8-1" → 422 INT-005
Scenario S-A1-dup: a second analyser with the same code → 409 INT-100
Scenario S-R1-autosend: a result for a mapping with Auto Send off → outcome AutoSendOff, nothing raised, intake row written
Scenario S-R1-ok: a mapped Auto-Send result → slope/intercept and conversions applied, intake Accepted, AnalyserResultReceived raised
         with StopFlags = the received flags marked Stop
Scenario S-R1-unmapped / S-R1-unknown: unmapped code → Unmapped; unknown sample number → UnknownSample
Scenario S-K-verdicts: Ct ≤ positive max → positive; ≤ borderline max → borderline (Presumptive); all negative incl. IC → INVALID;
         targets ≥ minimum positive → DETECTED; IC positive and targets negative → NOT DETECTED
Scenario S-K-serial: a run file whose serial differs from the kit's lock → refused INT-037, disposition Failed
Scenario S-AUTHZ: any request without the permission → 403 AUTHZ-001; a person with integration.create calling the agent API → 403 INT-035
Scenario S-TENANT: analysers, mappings, agents, kits and logs of tenant X are invisible to tenant Y (query filter + FORCE RLS)
```

Open questions and the assumed answers the build proceeds on:

| # | Question | Assumed answer |
| --- | --- | --- |
| A1 (OQ-08) | Which protocols and instruments does the pilot laboratory use? | Unknown; the agent translates ASTM / HL7 / files to this JSON API; the cloud stores the link settings for the agent only. |
| A2 | How does the agent prove possession of its secret? | Client id + secret (02 §6.3) exchanged at `POST /api/v1/auth/token`; the secret travels in a credential envelope like every credential (D-03, gate CredentialFieldTests); refresh token in the body. |
| A3 | Do mapping changes need a second person (maker/checker)? | No; every change is field-audited and Auto Send changes raise an event. |
| A4 | Does a PCR verdict need its own Auto Send? | Yes, uniformly: the kit names the analyser test code whose mapping lands the verdict (`AnalyserTestCode`); that mapping's Auto Send and conversions apply. |
| A5 | PCR "one gene positive" (LDM manual §14.6: two contradictory rows for ORF1a+ only) | Configurable per kit: `DetectedMinPositiveTargets` (default 1 per the task statement; the SOLGENT sheet uses 2); fewer positive targets, or any borderline target → Presumptive. |
| A6 | Append unregistered samples/tests (LDM `Append UnReg …`) | Stored and sent to the agent and on the event; the cloud never creates visits or orders; an unknown sample is logged UnknownSample. |
| A7 | Which samples does "download orders" return? | Samples of the agent's branch (visit branch) in status Collected or Received, changed since `since` (default 24 h), whose Ordered lines contain tests mapped on the analyser; no patient demographics. |
| A8 | Rotate: overlap of old and new secret? | None: rotation replaces the secret and ends the credential's live sessions (02 §6.3 allows no enforced overlap); the agent must be reconfigured. |
| A9 | Retention of agent logs and intake rows | Kept (2 years assumed); a purge job is a later change. |
| A10 | Run file format sent by the agent | Delimited text (CSV/TSV/semicolon); the agent converts instrument workbooks (BioRad, QuantStudio sheets) to text and also reports the serial it read; the cloud re-reads the serial from the header lines when the kit names the serial label. |
| A11 | A dedicated permission for agents? | `integration.create` + `integration.view` plus the site-agent principal check; a dedicated catalogue module (e.g. `agents.view/create`) would remove the overlap with people's keys — proposed to the integrator. |
| A12 | Credential secret hashing | HMAC-SHA256 under a master-key subkey (purpose `machine-credential`), `v1.` prefix; rotating `Lis:DataProtection:MasterKey` requires rotating every machine secret (runbook note for `deploy/SECRETS.md`). The keyed hash is resolved when a secret is hashed, so hosts without the security edge still resolve the persistence adapters. |
| A13 | Where does the agent ↔ analyser link live? | On the analyser (`SiteAgentId`, chosen in the analyser editor); the agent's analyser list is derived from it, so the two can never disagree. |
| A14 | Run-file layout storage | Typed columns (delimiter, sample/gene/well/content columns, patient content value, serial label, no-amplification tokens) instead of free JSON, so every kit is validated when saved. |
| A15 | Service identities in the user register | Service users are named `svc.<client id>` with the role "Service: <name> (<suffix>)"; the Organization user list (CP-38) should hide or badge them (proposal to the organization module); their password can never match (unusable hash). |
| A16 | Heartbeat state and logs in the audit | Heartbeat state, agent logs and the intake log are operational rows (not aggregates): they are not written to `audit.field_changes`; the intake and agent logs are append-only by trigger. |
