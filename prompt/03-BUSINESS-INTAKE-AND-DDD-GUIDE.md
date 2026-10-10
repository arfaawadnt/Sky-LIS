# 03 — Business Intake and DDD Guide (Meta-Framework and Blank Templates)

| Field | Value |
| ----- | ----- |
| Scope | A **domain-agnostic method** for turning a business requirement into an aggregate the development team can build without re-interpretation: how to define aggregates, states, transitions, invariants, segregation of duties, approval gates, events, permissions and acceptance scenarios for **any** future business domain. Blank intake templates. One non-normative illustrative example |
| Audience | Business analysts and domain modellers (authors); developers (readers, to know what an intake must contain) |
| Status | **Ratified for Phase 1 (2026-10-08).** The business domain of the product is not yet known; this document defines no business rule. Technical constraints it inherits: D-04 (every command and query names a permission), D-07 (the platform offers approval and immutability building blocks; the business decides how to use them) |

---

## 1. What this document is, and is not

It **is** the procedure and the templates. When the client's requirements arrive, the business analyst fills the templates in §11 for each business module; the result is handed to the developers as the single specification of that module's domain model.

It **is not** a domain model. Nothing here says what the product's records, states, approvers or thresholds are. Where an example is needed to show a template filled in, it is marked **Non-Normative Illustrative Example Only** and carries no requirement.

| Intake artefact (what the BA writes) | Becomes in code (what the developers build) | Verified by |
| --- | --- | --- |
| Aggregate and its boundary | one class under `Platform.Domain/<Module>/`, one table family under RLS | `DomainHardeningTests`, integration tests |
| State table | an enum stored as a string with a `CHECK` constraint | `CheckConstraintTests` |
| Transition table | one aggregate method per row; one command per row | `Domain.UnitTests` (one test per row and per refused row) |
| Invariants | guards inside the aggregate with coded refusals | `Domain.UnitTests` |
| Segregation-of-duties rules | aggregate guards (`SOD-<MOD>-nnn`) | `Domain.UnitTests` |
| Approval gates (if the domain has any) | aggregate methods using the platform's `Approval` value; frozen states protected by the immutability trigger | `Domain.UnitTests`, `TransitionAtomicityTests`, `FrozenStateImmutabilityTests` |
| Permissions | entries in `PermissionCatalog`; a policy attribute on every command and query (D-04) | `RequestPolicyTests`, `AuthorizationMatrixTests` |
| Events and who reacts | domain events and policies | outbox tests |
| Audit and data-classification notes | interceptors, retention jobs | integration tests |

The developers do not invent any row of these tables. If a row is missing, the intake goes back.

---

## 2. Vocabulary (the words everyone must use the same way)

| Term | Meaning here | Test |
| --- | --- | --- |
| **Aggregate** | A cluster of records that must be consistent *together* and is saved *together*; it has one **root** through which every change passes | "If I change X, must Y be checked in the same instant?" If yes, same aggregate |
| **Entity** | A record with an identity that persists through changes | "Do I need to find this exact one again later?" |
| **Value object** | A value with no identity, compared by content (money with currency, an address, an approval record) | "Would two with the same content be interchangeable?" |
| **State** | A named stage in a record's life; the set is closed and known in advance | A record is always in exactly one state |
| **Transition** | A move from one state to another caused by a named action with a named actor | Every transition has a trigger, an actor and a precondition list |
| **Invariant** | A rule that is true of the aggregate at every moment | If it can be false and the record still saved, it is not an invariant, it is a report |
| **Domain event** | A past-tense fact the aggregate announces that other parts react to later | Never a command to someone; always something that already happened |
| **Policy** | "When event E happens, do A" in another module, eventually consistent | Named with "when … then …" |
| **Segregation of duties (SoD)** | A rule that the *same person* may not perform two specified actions on one record | Named actions, named anchor field, fixed or configurable |
| **Approval gate** | A transition the business chooses to make an approval: it requires a permission, may pass SoD and threshold rules, records who approved and when, and may freeze the record | Every gate names its permission, its rules and what it freezes |

---

## 3. From requirement to aggregate: the method

1. **Harvest the nouns and the verbs.** Read the requirement and the policy or process it implements. List every noun (candidate entities/values) and every verb with a subject (candidate transitions). Mark which nouns have a life cycle and which are reference data.
2. **Find what must be consistent together.** For each pair of nouns ask: "If someone changes A while someone else changes B at the same moment, can the business end up in an impossible situation?" If yes, one aggregate. If "we would just see it later", separate aggregates linked by id, and the later reaction is a policy.
3. **Choose the root.** The entity whose identity people use to talk about the whole.
4. **Keep aggregates small.** A child collection that can grow without bound is its own aggregate referencing the parent by id, unless an invariant genuinely spans it.
5. **Write the state table and the transition table** (§4).
6. **Write the invariants (§4.3), the SoD rules (§5) and the approval gates (§6), if the domain has them.**
7. **Write the events and who reacts (§7), the audit and data-classification notes (§8), the permissions (§9) and the acceptance scenarios (§10).**

---

## 4. States, transitions, invariants (frameworks)

### 4.1 State table

| Column | Content | Rule |
| --- | --- | --- |
| State | PascalCase name | Closed set; adding one is a change request |
| Meaning | one sentence the process owner would accept | — |
| Editable fields | which fields may change in this state | "none" for frozen states |
| Frozen | yes/no | A frozen state's row is protected by the platform's immutability trigger; the only way out is a new record (amendment, revision) that references the original |
| Terminal | yes/no | A terminal state has no outgoing transitions |
| Visible to | permission keys or "all tenant users" | Affects queries and exports |

### 4.2 Transition table

One row per transition. The developers write exactly one aggregate method and one command per row.

| Column | Content |
| --- | --- |
| # | stable id (`T1`, `T2` …) used in tests |
| From → To | states |
| Action | the verb as the user says it |
| Actor | who may trigger it: a permission key (`<module>.<action>`), **never** a job title |
| Preconditions | what must be true before — each becomes a coded refusal |
| SoD | which earlier actor on this record the actor must not be (or "—") |
| Approval gate | "—", or: records approver and time, with the business's rule (threshold, second approval, delegation) |
| Reason / comment | required / optional / not applicable |
| Events raised | past-tense facts |
| System-triggered? | yes if a schedule proposes it; the aggregate still decides |

### 4.3 Invariant table

| Column | Content |
| --- | --- |
| Id | `INV-<MOD>-nnn` |
| Statement | always-true sentence |
| Enforced where | aggregate guard (always); database constraint too when expressible (`UNIQUE`, `CHECK`, FK) |
| Refusal code | `<MOD>-nnn` |
| Message | what the user sees |

Rules for writing them:

- An invariant is about the **record**, not about the screen.
- Preconditions of a transition are *guards*; invariants are *always* true.
- If a rule depends on *configuration*, say where the value comes from and what the default is. The developers read it from tenant settings, never hard-code it.
- A rule a schedule applies is a **system-triggered transition**: the sweep *proposes*; the aggregate *decides*. Jobs never set a state directly.

---

## 5. Segregation of duties (framework)

### 5.1 Patterns the BA can choose from

| Pattern | Statement |
| --- | --- |
| Creator ≠ approver | the person who created or last edited the record cannot approve it |
| First ≠ second approver | a two-level gate needs two distinct people |
| Approver ≠ executor | the person who approved cannot perform the follow-on action the approval authorises |
| Preparer ≠ reviewer | the author cannot be the reviewer |
| Role exclusivity | an actor may not hold both role A and role B in one tenant (checked at membership assignment) |

### 5.2 How to specify a rule

```text
SOD-<MOD>-nnn  <short name>.
  On transition(s) <Tn>: the acting user must not be the user recorded as <AnchorField>.
  Refusal: "<message the user sees>"
  Configurable: yes | no.   If yes: tenant setting <name>, default <on|off>; toggling writes a security event.
```

### 5.3 Where it lives

SoD rules live **inside the aggregate** as guards on the transition method; they compare user ids recorded on the record with the acting user id passed into the method. They are **not** validator rules, **not** handler checks and **not** privilege-matrix settings, because all three can be bypassed by a different code path. The platform provides the helper (`EnsureActorIsNotCreator` and the pattern for any anchor field); the BA decides which rules exist and whether they are fixed or configurable.

### 5.4 The "system" actor

Records created by a job have no human creator; SoD rules that compare with the creator are a no-op for them. The intake states, for each system-created record, who the first accountable human actor is.

---

## 6. Approval gates (framework; D-07)

### 6.1 What the platform provides

- An `Approval` value object (actor id, time, optional comment) an aggregate may record on any transition the business designates as an approval.
- A frozen-state immutability trigger for states the business declares frozen.
- Permissioned commands (every transition already requires a permission, D-04).
- Optional step-up re-authentication for operations the configuration lists (`02 §4.5`).

### 6.2 What the platform does **not** decide

Whether a transition is an approval; who may approve; whether thresholds, second approvals or delegation exist; whether a comment is required; what freezes; how corrections happen. All of these are intake decisions per domain.

### 6.3 What the intake specifies for each gate (blank)

| Item | Content |
| --- | --- |
| Transition | `Tn` |
| Permission | `<module>.approve` (or another key the catalogue defines) |
| Rule | none / threshold (which attribute, where it is configured, default) / second approval (by whom, SoD) / delegation (allowed? to whom? how long?) |
| Recorded | approver, time (server clock), comment (required / optional) |
| After the gate | which fields remain editable (normally none); which transitions remain possible |
| Correction path | what a correction copies, what it must re-approve, whether the original is superseded or stays in force |
| Rejection | reason required? to which state does the record return? |
| Step-up | does this gate require a fresh password proof? (configuration key, default off) |
| Display | approver's name as of the action, timestamp, comment, shown on the record and in exports |

---

## 7. Events and policies (framework)

For each event:

```text
Event:    <AggregateName><PastTenseVerb> { <AggregateId>, <reference number>, <the few values a reader needs>, <ActorId>, TenantId }
Raised by: <Tn>
Reacts:   <Module> — "when <event>, then <action>" (idempotent on <natural key>)
Timing:   within one outbox cycle (seconds) | by the next scheduled sweep | …
```

Rules: an event carries ids and the few values a reader needs, never the whole record; a policy is idempotent; a policy never changes the state of the aggregate that raised the event; cross-module reactions are **always** policies, never direct calls.

---

## 8. Audit trail and data classification (framework)

The platform records, for every tracked aggregate, who created and modified it (user id and display name as of the action), every domain event in the tamper-evident audit trail, and field-level before/after history. The intake adds:

| Requirement | What the intake states |
| --- | --- |
| Actor fields | which actor ids the record itself carries (needed as SoD anchors and for display) |
| Audited fields | which fields matter enough that their changes must be visible in the history (all are recorded; this decides what the UI surfaces) |
| Reasons | which transitions require a reason or comment |
| Frozen states | which states are frozen (§4.1) |
| Retention | retention period per record type; archive rules; export format |

### 8.1 Data classification per field

Mark each field: **PII** (names, contact details of individuals), **Confidential** (requires `<module>.viewConfidential`), **Audited** (changes surfaced in the history; frozen when the record is), **Operational** (everything else). PII fields are excluded from logs; exports require a permission; the intake states the retention period and lawful basis.

---

## 9. Permissions (framework; D-04)

Permissions are `module.action` from the closed action set `view`, `create`, `edit`, `approve`, `cancel`, `export`, `manage`, `viewConfidential`. Under decision D-04 every command **and every query** carries exactly one permission, so an intake that lists a register or a detail view without naming its `view` permission is incomplete, and the build fails if a developer omits it. The intake maps every transition and every read to one key. A new *module* key is a change request; a new *action* is an architectural change. Roles are **not** defined in the intake: tenants compose roles from permissions in the privilege screen; the intake may suggest seed roles as non-binding defaults.

---

## 10. Acceptance scenarios (framework)

One Given/When/Then per transition row, per refused precondition, per SoD rule and per invariant. These become the domain unit tests verbatim.

```gherkin
Scenario: <Tn> <Action> — happy path
  Given a <Aggregate> in state <From> with <preconditions satisfied>
    And the acting user holds <permission> and <SoD anchors satisfied>
  When the user <action> with <inputs>
  Then the <Aggregate> is <To>, <recorded metadata> is set, and <Event> is raised

Scenario: <Tn> <Action> — refused by <rule>
  Given a <Aggregate> in state <From> where <rule would be violated>
  When the user attempts to <action>
  Then the attempt is refused with <CODE> and nothing is recorded
```

---

## 11. Blank intake templates

### 11.1 Lean intake (five points; enough for small changes)

```text
1. What must the business be able to do, and under which policy or process? (one paragraph)
2. Records involved (nouns) and which have a life cycle
3. Who does what (as permissions), and any pairs of actions one person may not both perform
4. Which transitions are approval gates, by whom, with which rules (or "none")
5. What others must learn about it (events) and when
```

### 11.2 Full intake (sixteen sections; required for every new aggregate)

```text
 1. Purpose and policy basis
 2. Scope and out of scope
 3. Glossary (the business's words → platform words)
 4. Actors and permissions (module.action), including read-only access
 5. Aggregates: root, children, value objects, references to other aggregates by id
 6. State table                                   (§4.1 columns)
 7. Transition table                              (§4.2 columns)
 8. Invariants                                    (§4.3 columns)
 9. Segregation-of-duties rules                   (§5.2 format)
10. Approval gates                                (§6.3 table, one per gate; or "none")
11. Events and policies                           (§7 format)
12. Data classification and retention             (§8.1)
13. Reference data and configuration (tenant settings with defaults; lists of values)
14. Registers, reports and exports (filters, columns, who may export)
15. Notifications and escalations (who, when, channel)
16. Acceptance scenarios (§10), open questions and assumptions
```

Blank tables for sections 6–8 (copy as needed):

```text
State | Meaning | Editable fields | Frozen | Terminal | Visible to
----- | ------- | --------------- | ------ | -------- | ----------

#  | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Approval gate | Reason | Events | System?
-- | --------- | ------ | ------------------ | --------------------- | --- | ------------- | ------ | ------ | -------

Id | Statement | Enforced where | Code | Message
-- | --------- | -------------- | ---- | -------
```

---

## 12. Definition of ready (what the developers check before accepting an intake)

- [ ] Every state is in the state table; no transition names a state that is not
- [ ] Every transition has an actor as a permission key and a list of preconditions each with a code
- [ ] Every SoD rule names the two actions and the anchor field, and says whether it is fixed or configurable
- [ ] Every approval gate names its permission, its rule and what it freezes (or the intake says "no gates")
- [ ] Every reason requirement is on the transition table
- [ ] Every event lists who reacts and how quickly
- [ ] Every PII and confidential field is marked
- [ ] Every scenario has an id and maps to a transition, precondition, SoD rule or invariant
- [ ] Open questions have an assumed answer so building can start

## 13. Requirement anti-patterns (send these back)

| Written | Why it fails | Rewrite as |
| --- | --- | --- |
| "Status can be set to any of the following" | states without transitions; illegal moves become possible | a transition table |
| "The system automatically approves after N days" | a job changing state without an accountable actor | a system-triggered *proposal* with an accountable actor, or a reminder |
| "The manager approves" | a job title, which varies by customer | a permission key the tenant maps to its roles, plus the gate's rule |
| "Users can edit the record" | no state, no field list | editable fields per state |
| "Delete the record" | frozen or audited records are never deleted | a `Cancel` transition with reason, or an amendment |
| "Show a warning if…" | a UI behaviour, not a rule | an invariant or a precondition with a refusal, or explicitly "advisory only" |
| "Needs approval" | no permission, no rule, no record of what is approved | §6.3 table |
| "The same as module X" | module X's rules may not be known or may be wrong | the tables, even if copied |

---

## Appendix A — Non-Normative Illustrative Example Only

> **This appendix is an illustration of the templates filled in for a generic "Request" record. It defines no requirement, no state, no permission and no rule for the product. Every value is a placeholder. The actual entities, transitions and gates will be defined by the business analyst from the client's requirements.**

### A.1 Aggregate (illustrative)

```text
Request (root)
  Number        generated "REQ-<year>-<seq>" (per tenant per year)       value object
  RequestedBy   user id (SoD anchor)
  Items         [RequestItem]                                            child entities (bounded)
  State         RequestState
  Approval      Approval { ApprovedByUserId, ApprovedAtUtc, Comment }    at most one (only if the business makes T3 a gate)
  RejectionReason, CancellationReason
```

### A.2 State table (illustrative)

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| `Draft` | being prepared | all | no | no | requester, `<module>.view` |
| `Submitted` | awaiting a decision | none | no | no | all with `<module>.view` |
| `Approved` | decision recorded | none | yes (if the business says so) | no | all |
| `Rejected` | decision recorded; a revision is a new record | none | yes | yes | all |
| `Closed` | completed | none | yes | yes | all |
| `Cancelled` | withdrawn | none | yes | yes | all |

### A.3 Transition table (illustrative)

| # | From → To | Action | Actor | Preconditions | SoD | Approval gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | — → Draft | Create | `<module>.create` | — | — | — | n/a | `RequestCreated` | no |
| T2 | Draft → Submitted | Submit | `<module>.edit` | ≥ 1 item (`REQ-010`) | — | — | n/a | `RequestSubmitted` | no |
| T3 | Submitted → Approved | Approve | `<module>.approve` | *(business-defined rule, if any)* | `SOD-REQ-001` actor ≠ RequestedBy *(if the business wants it)* | records `Approval` *(if the business makes this a gate)* | optional | `RequestApproved` | no |
| T4 | Submitted → Rejected | Reject | `<module>.approve` | reason present (`REQ-020`) | `SOD-REQ-001` | — | required | `RequestRejected` | no |
| T5 | Approved → Closed | Close | `<module>.edit` | — | — | — | optional | `RequestClosed` | no |
| T6 | Draft, Submitted, Approved → Cancelled | Cancel | `<module>.cancel` | reason present (`REQ-030`) | — | — | required | `RequestCancelled` | no |
| T7 | Approved → Closed | Auto-close after `<setting>` days | system (sweep proposes) | approved more than `<setting>` days ago | — | — | n/a | `RequestClosed` | yes |

### A.4 Invariants (illustrative)

| Id | Statement | Enforced | Code |
| --- | --- | --- | --- |
| INV-REQ-001 | `Number` is unique per tenant | DB unique `(tenant_id, number)` | REQ-100 |
| INV-REQ-002 | A record in `Approved` or later has an `Approval` *(only if T3 is a gate)* | aggregate | REQ-101 |
| INV-REQ-003 | Items cannot change after `Submitted` | aggregate; immutability trigger from the first frozen state | REQ-102 |

### A.5 Event (illustrative)

```text
Event:    RequestApproved { RequestId, Number, ApprovedBy, TenantId }
Raised by: T3
Reacts:   Notifications — "when a request is approved, notify the requester" (idempotent on RequestId)
Timing:   within one outbox cycle
```

### A.6 Scenarios (illustrative)

```gherkin
Scenario: T3 Approve — happy path
  Given a Request in state Submitted
    And the acting user holds <module>.approve and is not the requester
  When the user approves it
  Then the Request is Approved, Approval records the user and the server time, and RequestApproved is raised

Scenario: T3 Approve — refused by SoD
  Given a Request in state Submitted requested by user U
  When user U attempts to approve it
  Then the attempt is refused with SOD-REQ-001 and nothing is recorded
```

*End of the non-normative example.*
