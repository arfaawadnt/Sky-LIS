# Version 2 — Enterprise Architecture and Technical Starter Kit

| Field | Value |
| ----- | ----- |
| Purpose | A **pure technical, structural and security engineering blueprint** for a new multi-tenant B2B enterprise SaaS platform, derived from the battle-tested NT.QAMS architecture. It pre-judges no business rule: the business domain is not yet known |
| Status | **Ratified for Phase 1.** Critique issued 2026-10-07; decisions D-01 … D-07 ratified by leadership and engineering stakeholders 2026-10-08 and incorporated across the pack |
| Baseline reviewed | NT.QAMS at `59d5106` (develop, 2026-10-06) |
| Distribution | Solution architects, lead developers, security lead, business analysts (document 03 only) |
| Change control | A change to a ratified decision is a new entry in the decision register (`00 §4`) and a revision of every document that cites it. Documents are versioned with the repository |

## What this pack decides (technical foundations; non-negotiable)

| Id | Decision | Where it is specified |
| --- | --- | --- |
| D-01 | Access JWT (ES256, 15 min) in SPA memory + rotating refresh token in `__Secure-rt` cookie against `identity.refresh_sessions` | `02 §2.4, §4.1–4.3`, `01 §2.2, §4.7, §6.1, §6.4` |
| D-02 | `sub` AES-256-GCM encrypted; zero PII in the token (`email`, `name`, `role` absent) | `02 §4.2, §4.6`, `01 §5.3`, `04 P-21` |
| D-03 | Credential envelope mandatory at the API edge; `[CredentialField]` on every password property; no `Optional` mode; shared nonce and key store | `02 §3`, `01 §3.2, §4.8, §6.4`, `04 P-41` |
| D-04 | Deny-by-default on commands **and** queries; `RequestPolicyTests` fails the build otherwise | `01 §3.4, §8`, `03 §9`, `04 P-26` |
| D-05 | .NET 9 / C# 13, EF Core 9, MediatR, PostgreSQL 17, Angular 22 zoneless; LTS upgrade on the roadmap | all documents; risk recorded in `00 §0` |
| D-06 | `identity.platform_users` + `app.tenant_memberships` under `FORCE RLS` with `(tenant_id, id)` keys | `01 §4.1, §4.7`, `02 §4.4`, `04 P-30` |
| D-07 | Domain-agnostic: no signing ceremony, no PIN, no sector regulation; the platform ships building blocks (permissioned commands, optional `Approval` value, SoD helpers, frozen-state immutability trigger, tamper-evident audit trail) and the business defines their use per domain | `00 C-21`, `01 §3.3, §4.8`, `03 §6`, `04 P-02, P-54` |

Also fixed: same-origin reverse proxy with no CORS, TLS at the proxy with HSTS in-app, the rate-limit matrix and anti-CSRF checks, the transactional outbox, the migration-role `BYPASSRLS` rule, the test gates in `01 §8`.

## What this pack deliberately does not decide (business policy; pluggable)

| Topic | Platform provides | Decided later by |
| --- | --- | --- |
| Whether MFA is required, and for whom | TOTP enrolment, recovery codes, MFA scopes and gate middleware; `Security:Mfa:Policy` configuration | business / tenant |
| Which operations need step-up re-authentication | the re-authenticate endpoint, the session stamp, `[RequireRecentAuthentication]`, `Security:StepUp:*` configuration | business / tenant |
| Password lifetime and history | configurable policy with defaults | tenant |
| Which transitions are approvals, who approves, thresholds, delegation | `Approval` value object, SoD helpers, immutability trigger | business analyst per domain (document 03) |
| Entities, states, invariants, events, permissions per module | the method and blank templates in document 03; `PermissionCatalog` as the registry | business analyst per domain |
| Modules, navigation, i18n content | feature-sliced frontend structure, nav registry, lazy dictionaries | product |

## Documents

| File | Purpose | Primary readers |
| --- | --- | --- |
| `00-CRITICAL-ARCHITECTURAL-CHALLENGE-AND-ALTERNATIVES.md` | The critique of the baseline, the alternatives weighed, the ratified decisions, the decision register and the open actions | Architects, security lead, leadership |
| `01-SYSTEM-ARCHITECTURE-BLUEPRINT.md` | Topology, layer mapping, project and folder structure, tenancy and database pattern, frontend architecture, cross-cutting services, the enforcing test gates | Architects, lead developers |
| `02-SECURITY-AND-IDENTITY-SPECIFICATION.md` | Threat model, transport and origin policy, credential envelope protocol, access and refresh token design, encrypted subject claim, MFA and step-up capabilities, rate-limit matrix, credential storage, security events, verification mapping | Security lead, lead developers |
| `03-BUSINESS-INTAKE-AND-DDD-GUIDE.md` | The domain-agnostic method and blank templates a business analyst uses to define aggregates, states, invariants, SoD, approval gates, events and permissions for any future domain; one non-normative example | Business analysts, domain modellers |
| `04-ARCHITECTURAL-PITFALLS-AND-AVOIDANCE-GUIDE.md` | Forty-one technical anti-patterns with do/don't pairs and the automated gate that catches each | Every developer and reviewer |

## Open actions before the first production deployment

| Id | Action | Owner | Why |
| --- | --- | --- | --- |
| A-1 | Set the date of the .NET 10 LTS upgrade and record it in the decision register | Engineering lead | .NET 9 left Microsoft support on 2026-05-12; no further security patches are issued for it (`00 §0`, D-05) |
| A-2 | Confirm the licence terms of the pinned MediatR version | Engineering lead | `00 C-12` |
| A-3 | Confirm whether any client other than the same-origin SPA consumes the API in the first two years | Product | `00 §5`; sizes the machine-token path |

## How to read the pack

Start with `00 §0` (one page: the seven decisions and their residual risks). Architects then read 01 and 02 in full. Business analysts read 03 when the domain requirements arrive. Reviewers keep 04 open. Every rule in 01 and 02 names the test that enforces it; a rule without a test is a suggestion and is marked as a task.

## Provenance

Every statement about the baseline cites a file in the NT.QAMS repository or one of its ADRs and change requests. No build, test or migration was run to produce this pack; the documents specify gates and do not claim results. Code samples use illustrative placeholder aggregates whose names carry no business meaning.
