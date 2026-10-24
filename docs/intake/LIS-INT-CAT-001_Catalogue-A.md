# LIS-INT-CAT-001 — Catalogue A: groups, samples, units, conditions, services, tests (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `catalogue` (half A; slot `cat`). Client pages CP-39 Clinical groups, CP-40 Services, CP-41 Tests, CP-42 Service sequence, CP-43 Sample types, CP-44 Units of measurement, CP-45 Patient conditions, CP-55 Additional services; admin SA-12/SA-13 reuse the same pages under the platform tenant. Business rules BR-02 (input), BR-08, BR-11, BR-12, BR-13, BR-19, BR-20, BR-24, BR-25. |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), the Sky LIS prototypes synthesis and the LDM knowledge base (`docs/knowledge/ldm/LDM_Business_Knowledge.md` §2.2, §2.5, §3.1–§3.8, §5.6, §6). **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.3 — 2026-10-24 (culture specimens: `SampleType.ForCulture`, change request LIS-CR-007 P-MIC, reviewer point C-11: §18, scenario S-T3c, questions Q21–Q23); 1.2 — 2026-10-19 (service sequence reordering, change request LIS-CR-003 P-C18: §17, scenario S-T12b, questions Q18–Q20); 1.1 — 2026-10-14 (parent services on the test page, change request: T21, scenario S-T21, question Q17) |
| Implemented in | `src/Platform.Domain/Catalogue` (ClinicalGroup, SampleType, MeasurementUnit, PatientCondition, Service, LabTest, ReferenceRangeResolver), `src/Platform.Contracts/Catalogue`, `src/Platform.Application/Catalogue` (+ `Services/` formula evaluator, `ICatalogueLookup`, `ICatalogueACloner`), `src/Platform.Infrastructure/Persistence/Catalogue`, `src/Platform.WebApi/Controllers/Catalogue`, `frontend/src/app/features/catalogue/{pages-a,data-a,state-a,ui-a}`, `frontend/src/app/core/api/catalogue-a.contracts.ts`, `frontend/public/i18n/catalogue-a.{en,ar}.json` |
| Codes | `CAT-001`…`CAT-199` (catalogue B uses `CAT-200`…`CAT-399`) |

## 1. Purpose and policy basis

The catalogue is the laboratory's definition of **what can be ordered and how it is measured**. Every later module
reads it: registration orders services and generates tubes from their clinical group, sample type and condition
(BR-02, LDM §2.2); results resolve reference ranges by sex, age and patient condition (BR-08, LDM §2.5, §3.3),
compute formulas, flag panic values and delta changes (BR-11, LDM §3.4, §5.6); the worklist computes expected
dates from durations and test days (BR-13, LDM §6); pricing attaches a price to every service. A wrong catalogue row
is a wrong report, so the catalogue validates its own consistency (BR-19, BR-20, BR-24) and never erases rows that
other records reference (BR-15: deactivate instead of delete).

## 2. Scope and out of scope

In scope: the seven reference lists of half A with their editors, the composition of services from tests and
services, the configuration of a test per type (range bands, list values, formula, paragraphs, timing), the global
service sequence, the additional-services view, lookup endpoints for every later module, the read port
`ICatalogueLookup`, the reference-range resolver (domain service), the formula evaluator, and the master-data
cloner for a new tenant (BR-25). CSV export of every list.

Out of scope: prices (Pricing module, CP-33/CP-34: the "base price" of CP-40/CP-55 is shown and edited there),
preparations, comment templates, report templates, test rules, shortcut sets, doctors, diagnoses, reasons and the
microbiology masters (catalogue B), analyser-specific ranges (Integration, LDM §3.3 last bullet), auto-verification
rules (Automation), the "publish" workflow of SA-12/SA-13 (master-data update policy, SA-33: the platform tenant's
rows are edited in place; propagation to existing tenants is a later intake).

## 3. Glossary

| Business word (SRS / LDM) | Platform word |
| --- | --- |
| Clinical group, unit, section (LDM "Group") | `ClinicalGroup` (`app.clinical_groups`); `UnitCode` = the label letter (LDM §2.2: `C`, `H`…) |
| Sample type + sample condition | `SampleType` (`app.sample_types`) with child `SampleCondition` (`app.sample_conditions`) |
| Unit of measurement (LDM "Results Units") | `MeasurementUnit` (`app.measurement_units`) |
| Patient condition (pregnant, diabetic…) | `PatientCondition` (`app.patient_conditions`) |
| Service (orderable item: test, profile, package/panel/mega, culture, text report, additional service) | `Service` (`app.services`) with child `ServiceMember` (`app.service_members`) |
| Test (measured parameter) | `LabTest` (`app.tests`) with children `RangeBand` (`app.test_range_bands`), `ListValue` (`app.test_list_values`), `TestParagraph` (`app.test_paragraphs`) |
| Test conditions / Normal / Exact (LDM) | list values / `IsNormal` / `IsDefault` |
| Panic and reportable range (LDM §3.4) | `RangeBand.PanicLow/High`, `RangeBand.ReportableLow/High` |
| Duration / Min duration / Frequency check / Delta check / Test days (LDM §3.4) | `LabTest` timing: `DurationHours`, `MinDurationHours`, `FrequencyCheckDays` + `FrequencyAction`, `DeltaPercent` + `DeltaWindowDays`, `TestDays` (weekday bit mask) |
| Hidden helper test (LDM: name starting with `.`) | `LabTest.IsHiddenHelper` (BR-20) |
| Service global sequence | `Service.Sequence` |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `catalogue.view` | every list, detail, lookup and the formula check |
| `catalogue.create` | create clinical groups, sample types, units, patient conditions, services, additional services, tests |
| `catalogue.edit` | edit any of them: general data, sample conditions, service members, range bands, list values, formula, paragraphs, timing, service sequence |
| `catalogue.manage` | deactivate and reactivate any of them |
| `catalogue.export` | the CSV exports (server returns the full list; the SPA writes the file) |

Actors: laboratory administrator (all), unit head (tests), National Technology master-data reviewer / operations
admin in the platform tenant (SA-12/SA-13: same pages, platform tenant = global templates). Deny by default.

## 5. Aggregates

All are tenant-scoped (`ITenantScoped`, key `(tenant_id, id)`, query filter, FORCE RLS), sealed, created through
factories, with xmin concurrency on the root. Children are separate tenant tables with a composite FK
`(tenant_id, <root>_id)` to their root (cascade on the root only; roots are never deleted). References to other
aggregates are by id with a composite FK and `Restrict`.

- **ClinicalGroup**: `Code` (1–16, `[A-Z0-9][A-Z0-9._-]*`, unique per tenant, immutable), `UnitCode` (1–4,
  `[A-Z0-9]`, label letter), `Name` (en/ar), `ReportName` (≤200), `ReportTemplateId` (catalogue B, by id, no FK
  in this wave), `DisplayOrder` (0…9999), `IsActive`.
- **SampleType**: `Code` (1–20, unique, immutable), `ShortName` (1–20), `Name`, `IsActive`; children
  **SampleCondition** (`Code` 1–20 unique within the type, `Name`, `IsActive`; never deleted, referenced by id).
- **MeasurementUnit**: `Name` (1–50, unique per tenant ignoring case through `NameKey` = upper-cased name; language-neutral symbol such as `mg/dL`), `IsActive`.
- **PatientCondition**: `Name` (en/ar, English unique per tenant ignoring case through `NameKey`), `IsActive`.
- **Service**: `Code` (1–16, unique, immutable), `ShortName` (1–50), `ShortcutNo` (1…999999, unique when set),
  `Name`, `ReportName`, `Loinc`, `Cpt` (≤20 `[A-Za-z0-9.-]`), `ClinicalGroupId` (required except Additional), `Type`
  (`Test | Profile | Package | Culture | TextReport | Additional`, immutable), `SampleTypeId` + `SampleConditionId`
  (required for Test/Profile/Culture/TextReport, forbidden for Package/Additional; the condition belongs to the type),
  `Header` (≤200), `Aliases` (≤20 × ≤100, distinct), `Precaution` (≤1000), `Gender` (`Any | Male | Female`),
  `TurnaroundHours` (0…8760), `LabelCount` (0…20), `PrintAlone`, `IsOrderable`, `ShowOnWeb`, `ProfileSum`
  (> 0, optional), `Sequence` (> 0, unique per tenant; assigned max+1 at creation), `IsActive`; children
  **ServiceMember** (`Kind Test | Service`, `MemberId`, `Order` > 0 unique within the service, `InSum`, `Visible`).
- **LabTest**: `Code` (1–16, unique, immutable), `Name`, `ReportName`, `Loinc`, `Cpt`, `ClinicalGroupId`, `Type`
  (`Numeric | NonNumeric | OptionalList | RestrictedList | Formula | FormattedText | Culture`), `SampleTypeId?`,
  `SampleConditionId?`, `UnitId?`, `Gender`, `Methodology` (≤200), `Decimals` (0…6), `IsHiddenHelper` (BR-20),
  `PrintReference`, `LabelCount` (0…20), `Prefix` (≤10), timing (`DurationHours` 0…8760 default 24,
  `MinDurationHours` ≤ duration, `FrequencyCheckDays` 1…3650 + `FrequencyAction Alert | Prevent`, `DeltaPercent`
  0 < x ≤ 1000 + `DeltaWindowDays` 1…3650, `TestDays` 1…127, bit 0 = Sunday … bit 6 = Saturday, default 127),
  `FormulaExpression` (≤1000, normalised), `IsActive`; children **RangeBand** (`Sex Any | Male | Female`, `AgeFrom`
  inclusive, `AgeTo` exclusive, `AgeUnit Days | Months | Years`, `PatientConditionId?`, `Low`, `High`, `PanicLow`,
  `PanicHigh`, `ReportableLow`, `ReportableHigh` (decimal), `TextRange` (≤200), `DisplayOrder`), **ListValue**
  (`Value` ≤200, `IsNormal`, `IsCritical`, `IsDefault`, `IsPlaceholder`, `Colour` `#RRGGBB`, `DisplayOrder`),
  **TestParagraph** (`Title` ≤200, `Text` ≤4000, `DisplayOrder`).
  The test stores no link to its services: its **parent services** (prototype "Parent Service", CP-41) are read from
  the `ServiceMember` rows that point at it (`LabTestDetail.ParentServices`, in the global sequence) and changed from
  the test page through T21, which rewrites the members of each affected service.

Configuration allowed per test type: range bands — Numeric, Formula (limits) and NonNumeric (text range only);
list values — OptionalList, RestrictedList; formula — Formula; paragraphs — FormattedText; Culture has none.

Domain services: `ReferenceRangeResolver` (BR-08) picks the band for (sex, age, condition): bands of the patient's
condition beat general bands, a sex-specific band beats `Any`, then the narrowest age span, then display order;
`null` when nothing matches. Age is a `PatientAge` (completed days, months and years, computed from the date of
birth by the caller, or approximated from days). `FormulaEvaluator` (Application) tokenises, parses and evaluates
`+ - * / ^ ( )`, comparisons `< <= > >= = <>`, `ROUND(x[,n])`, `IF(c,a,b)`, `MIN`, `MAX`, `ABS`, numbers and test
references `[CODE]` (or a bare `CODE` of letters, digits and `_`); decimal arithmetic; no reflection, no dynamic code;
limits 1000 characters, 400 tokens, depth 32.

## 6. State table

| Aggregate | State | Meaning | Editable | Visible to |
| --- | --- | --- | --- | --- |
| every root | Active | selectable in other records (BR-24) and in lookups | all but the code (and the service type) | `catalogue.view` |
| every root | Inactive | kept for history; not selectable; existing references keep working | all but the code | `catalogue.view` (inactive filter) |
| SampleCondition | Active / Inactive | as above, within its sample type | code immutable | `catalogue.view` |

No terminal state; nothing is deleted (BR-15).

## 7. Transition table

| # | Transition | Permission | Preconditions (codes) | Events |
| --- | --- | --- | --- | --- |
| T1 | Create clinical group | create | code valid (CAT-001) and unique (CAT-100); unit code (CAT-003); order (CAT-008); texts (CAT-009) | `ClinicalGroupCreated` |
| T2 | Edit clinical group | edit | exists (CAT-190); as T1 | `ClinicalGroupChanged` |
| T3 | Deactivate / reactivate (every root) | manage | exists (CAT-19x); idempotent | `<Root>Deactivated` / `<Root>Reactivated` |
| T4 | Create sample type (+ conditions) | create | code (CAT-001) unique (CAT-101); short name (CAT-010); condition codes unique in the type (CAT-011) | `SampleTypeCreated` |
| T5 | Edit sample type | edit | exists (CAT-191) | `SampleTypeChanged` |
| T6 | Save conditions (upsert; omitted → inactive) | edit | known condition ids (CAT-012); codes valid and unique (CAT-011); ≤50 (CAT-013) | `SampleConditionsChanged` |
| T7 | Create / edit unit | create / edit | name 1–50 (CAT-015), unique ignoring case (CAT-102); exists (CAT-192) | `MeasurementUnitCreated` / `…Changed` |
| T8 | Create / edit patient condition | create / edit | English name unique ignoring case (CAT-103); exists (CAT-193) | `PatientConditionCreated` / `…Changed` |
| T9 | Create service (with members) | create | code (CAT-001) unique (CAT-104); shortcut unique (CAT-106); references exist and are active (CAT-040, BR-24); group required unless Additional (CAT-041); sample rules (CAT-028/029/030); member rules (CAT-021…027, CAT-036, CAT-037); numbers (CAT-008); aliases (CAT-034) | `ServiceCreated` |
| T10 | Edit service (general) | edit | exists (CAT-194); type immutable (CAT-035); as T9; a reference is checked for activity only when it changes | `ServiceChanged` |
| T11 | Replace members | edit | exists; member rules: Test → exactly one test; Profile → ≥1 tests; Culture → ≥1 tests, exactly one Culture test; TextReport → exactly one FormattedText test; Package → ≥1 services, none Package/Additional, not itself (CAT-025/026); Additional → none; no duplicate (CAT-023); orders > 0 and unique (CAT-024); members exist and are active (CAT-027); profile sum needs an in-sum member (CAT-037) | `ServiceMembersChanged` |
| T12 | Save service sequence | edit | services exist (CAT-194); sequence > 0 (CAT-031); unique across the tenant after the change (CAT-105) | `ServiceResequenced` (per changed service) |
| T13 | Create test | create | code (CAT-001) unique (CAT-107); references exist and are active (CAT-040); decimals 0…6 (CAT-050); numbers/texts (CAT-008/009) | `LabTestCreated` |
| T14 | Edit test (general) | edit | exists (CAT-195); a type change is refused while configuration of another type exists (CAT-071) | `LabTestChanged` |
| T15 | Replace range bands | edit | type allows bands (CAT-051); age (CAT-052); limits ordered: panic ≤ normal ≤ panic, reportable outside panic (CAT-053); same sex + condition must not overlap (CAT-054); something to show (CAT-055); NonNumeric = text only; ≤100 bands | `LabTestRangesChanged` |
| T16 | Replace list values | edit | type is a list (CAT-056); values non-empty, ≤200, distinct ignoring case, colour `#RRGGBB` (CAT-057); default must be a placeholder (CAT-058, BR-19); at most one default (CAT-059); a placeholder is never critical (CAT-060); a value is Normal or Critical, not both (CAT-061, validation v0.5.0 O-2; a field error on `values[n].isCritical`); ≤100 values | `LabTestListValuesChanged` |
| T17 | Set / clear formula | edit | type Formula (CAT-062); syntax (CAT-063, with position); every reference is an active test of the tenant (CAT-064); not itself (CAT-065); no cycle through other formulas (CAT-066) | `LabTestFormulaChanged` |
| T18 | Replace paragraphs | edit | type FormattedText (CAT-068); title 1–200, text ≤4000, ≤50 (CAT-069) | `LabTestParagraphsChanged` |
| T19 | Set timing | edit | ranges of §5 (CAT-070) | `LabTestTimingChanged` |
| T20 | Check formula | view | none (returns the verdict, never refuses) | — |
| T21 | Set parent services (from the test page, v1.1) | edit | test exists (CAT-195); every listed service exists (CAT-194); ids distinct (VALIDATION-400); the test joins each listed service it is not in (appended last, printed, not in the sum) and leaves each carrying service no longer listed, each through T11 on that service, so its member rules decide: a Test or TextReport service keeps its single test (CAT-021), a Package takes no test (CAT-022), a Culture keeps exactly one culture test (CAT-036), a profile sum keeps an in-sum member (CAT-037), an inactive test joins nothing (CAT-027, BR-24); one refusal leaves every service unchanged | `ServiceMembersChanged` (per changed service) |

## 8. Invariants

| Id | Statement | Enforced where | Code |
| --- | --- | --- | --- |
| INV-CAT-001 | Codes are well-formed and unique per tenant and kind (group, sample type, service, test); a condition code is unique in its type | aggregate (`CatalogueText.Code`) + handler + unique index `(tenant_id, code)` / `(tenant_id, sample_type_id, code)` | CAT-001, CAT-011, CAT-100/101/104/107 |
| INV-CAT-002 | Unit names and patient-condition English names are unique per tenant ignoring case | handler + unique index `(tenant_id, name_key)` on the upper-cased name kept by the aggregate | CAT-102, CAT-103 |
| INV-CAT-003 | A service's members fit its type (T11) | aggregate `Service.ReplaceMembers` | CAT-021…027, CAT-036 |
| INV-CAT-004 | Sequence > 0 and unique per tenant | aggregate + handler + deferrable unique constraint `ux_services_sequence` (module SQL) | CAT-031, CAT-105 |
| INV-CAT-005 | Shortcut numbers unique per tenant when set | handler + partial unique index | CAT-106 |
| INV-CAT-006 | A test carries only the configuration of its type | aggregate (each replace method and the type change) | CAT-051, 056, 062, 068, 071 |
| INV-CAT-007 | Range bands are ordered and do not overlap for the same sex and condition | aggregate | CAT-052…055 |
| INV-CAT-008 | BR-19: at most one default list value and it is a placeholder; a placeholder is never critical | aggregate | CAT-058…060 |
| INV-CAT-009 | A formula references active tests only, never itself, never through a cycle | handler (needs the tenant's tests) + aggregate (self) | CAT-064…066 |
| INV-CAT-010 | References to other catalogue rows exist in the tenant; newly selected ones are active (BR-24) | handler + composite FKs | CAT-040 |
| INV-CAT-011 | A sample condition belongs to the selected sample type | handler | CAT-030 |
| INV-CAT-012 | Nothing is deleted; deactivation keeps references working (BR-15) | no delete endpoint | — |

## 9. Segregation-of-duties rules

None. Catalogue maintenance is a single-actor administrative task in the SRS; no SoD rule is specified (assumed, §16).

## 10. Approval gates

None. Changes take effect on save (CP-39…CP-55 list "Add, edit" without approval). The platform tenant's
"publish" (SA-12/SA-13) is out of scope (§2).

## 11. Events and policies

```text
ClinicalGroupCreated/Changed/Deactivated/Reactivated { ClinicalGroupId, Code }
SampleTypeCreated/Changed/Deactivated/Reactivated     { SampleTypeId, Code }
SampleConditionsChanged                               { SampleTypeId, Code, ActiveConditionCount }
MeasurementUnitCreated/Changed/Deactivated/Reactivated { MeasurementUnitId, Name }
PatientConditionCreated/Changed/Deactivated/Reactivated { PatientConditionId, NameEn }
ServiceCreated { ServiceId, Code, Type, Sequence } · ServiceChanged · ServiceMembersChanged { ServiceId, Code, MemberCount }
ServiceResequenced { ServiceId, Code, Sequence } · ServiceDeactivated/Reactivated
LabTestCreated { LabTestId, Code, Type } · LabTestChanged · LabTestRangesChanged { …, BandCount } · LabTestListValuesChanged { …, ValueCount }
LabTestFormulaChanged { LabTestId, Code, Expression? } · LabTestParagraphsChanged · LabTestTimingChanged · LabTestDeactivated/Reactivated
```

Raised at the end of each transition, written to the outbox and the audit trail in the same commit. No policy in
this module; consumers (registration, results, pricing) read the catalogue through `ICatalogueLookup` at the moment
they act, so no projection needs these events yet. Every event is a sealed record of ids, strings, enums and numbers
(gate `DomainEventPolicyTests`).

## 12. Data classification and retention

All fields are operational reference data, audited field by field (`audit.field_changes`), never PII. Retained for the
life of the tenant (results and reports refer to them). Export: CSV of every list (`catalogue.export`).

## 13. Reference data and configuration

The platform tenant (`TenantIds.Platform`) holds the global templates; a new tenant receives an independent copy
of every **active** row through `ICatalogueACloner.CloneAsync(source, target)` (BR-25): fresh ids, **codes unchanged** (catalogue B's cloner runs afterwards and remaps services and clinical groups by code), references remapped,
children copied, inactive rows and members pointing at uncopied rows skipped (a service left without its mandatory
members is skipped), the report template id cleared unless a map is supplied, formulas copied verbatim (they refer
to codes). The cloner stages rows only; the caller commits once inside an elevated scope. No tenant setting is read
by this module.

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Clinical groups | search (code, names), active, page | code, unit code, name, report name, display order, active | view |
| Sample types | search, active, page | code, short name, name, active conditions, active | view |
| Units / Patient conditions | search, active, page | name(s), active | view |
| Services | search (code, short name, names, aliases, shortcut), type, group, active, page | code, short name, shortcut, name, type, group, sample, gender, sequence, members, orderable, active | view |
| Tests | search, type, group, active, page | code, name, type, group, unit, sample, gender, hidden, active | view |
| Service sequence | search, type | sequence, code, name, type, group, active | view |
| Lookups (other modules) | search, type, orderable (services); ≤50 per page; active only | id, code, names, group, type | view |
| Exports | as the register (all rows, ≤10 000) | as the register | export |

## 15. Notifications and escalations

None.

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Catalogue`, `tests/Platform.Application.UnitTests/Catalogue`,
`tests/Platform.IntegrationTests/CatalogueAModuleTests.cs` and `tests/Platform.WebApi.FunctionalTests/CatalogueAFlowTests.cs`.

```gherkin
Scenario S-T1-ok / S-T1-code: create a clinical group; a malformed code is refused with CAT-001, a duplicate with CAT-100 (409)
Scenario S-T3: deactivate and reactivate any root; both are idempotent and raise one event each
Scenario S-T6: conditions upsert keeps ids, deactivates omitted ones, refuses duplicate codes (CAT-011) and unknown ids (CAT-012)
Scenario S-T9-test: a Test service with exactly one test member is created with the next sequence
Scenario S-T9-members: a Test service with two tests (CAT-021), a Profile with a service member (CAT-022), a duplicate member (CAT-023),
                       duplicate orders (CAT-024), a package including itself (CAT-025) or a package (CAT-026) are refused
Scenario S-T9-sample: Additional with a sample (CAT-028), Profile without a sample (CAT-029), a condition of another type (CAT-030)
Scenario S-T10-type: changing the service type is refused (CAT-035)
Scenario S-T12: swapping two sequences in one save succeeds; a duplicate or non-positive sequence is refused (CAT-105 / CAT-031)
Scenario S-T15: overlapping bands for the same sex and condition (CAT-054), panic inside normal (CAT-053), bands on a list test (CAT-051)
Scenario S-T16: two defaults (CAT-059), a default that is not a placeholder (CAT-058), a critical placeholder (CAT-060)
Scenario S-T17: a formula with a syntax error (CAT-063), an unknown code (CAT-064), itself (CAT-065), a cycle (CAT-066); a valid formula is normalised
Scenario S-T14-type: changing a Numeric test with bands to a list type is refused (CAT-071)
Scenario S-BR08: the resolver prefers the condition band, then the sex band, then the narrowest age band; returns null when nothing matches
Scenario S-FORMULA: evaluation of arithmetic, precedence, ROUND, IF, MIN/MAX/ABS, missing values and division by zero
Scenario S-AUTHZ: without catalogue.create the API answers 403 AUTHZ-001; a shape violation answers 400 VALIDATION-400
Scenario S-TENANT: rows of tenant X are invisible to tenant Y (query filter and FORCE RLS)
Scenario S-CLONE: the cloner copies the active rows of the platform tenant to a new tenant with fresh ids and remapped references
Scenario S-T21: the test detail lists its carrying services in sequence order; leaving the only test of a Test service (CAT-021), joining a
                Test service that has its test (CAT-021) or a Package (CAT-022) and an unknown service (CAT-194) are refused and change nothing;
                leaving a profile then joining it again appends the test last; a deactivated test cannot join (CAT-027); a viewer gets 403
```

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | Are LOINC codes mandatory for services and tests (prototype marks them required)? | Optional; Automation will require a LOINC on tests used in medical rules (LDM §3.8). |
| Q2 | Must a profile's tests belong to the profile's clinical group (LDM §3.5)? | No; any active tests. |
| Q3 | May a package contain another package or an additional service? | No (no nesting; additional services are ordered on their own). |
| Q4 | Is the service type changeable after creation? | No; create a new service. The test type is changeable while no configuration of another type exists. |
| Q5 | Is the sample type mandatory on services? | Yes for Test, Profile, Culture and TextReport services (tube generation, BR-02); optional on tests (informational). |
| Q6 | Do units of measurement need an Arabic name? | No; units are language-neutral symbols (`mg/dL`). |
| Q7 | How are ages compared in range bands? | `AgeFrom` inclusive, `AgeTo` exclusive, in the band's unit, on completed days / months / years; overlap checks use average month/year lengths. |
| Q8 | What does "default list value must not read as a result" mean operationally (BR-19)? | A default must be flagged `IsPlaceholder`; placeholders are not printed as results and are never critical; at most one default. |
| Q9 | Does the frequency check alert or prevent (BR-12)? | Per test: `FrequencyAction` Alert (default) or Prevent. |
| Q10 | Which delta window applies (BR-11)? | Per test `DeltaWindowDays` (optional; Results decides the default when empty). |
| Q11 | Which functions may formulas use; may they use patient age or sex? | `ROUND`, `IF`, `MIN`, `MAX`, `ABS`, `^` and comparisons; test codes only, no patient variables in this version. |
| Q12 | Are catalogue rows ever deleted? | No; deactivation only (BR-15). |
| Q13 | Shortcut numbers: unique per tenant? | Yes, among services (LDM §3.8). |
| Q14 | Does deactivating a clinical group / sample type / unit cascade to services and tests? | No; existing references keep working, new selections are refused (BR-24). |
| Q15 | Cloning (BR-25): which rows reach a new tenant? | Active rows only; formulas verbatim; report templates cleared unless the catalogue B map is supplied. |
| Q16 | Is a "publish" step needed for the global catalogue (SA-12)? | Not in this intake; edits of the platform tenant are immediate and reach only tenants created afterwards. |
| Q17 | Which services may a test join or leave from its own page (prototype "Parent Service", v1.1)? | Profiles and cultures, which take any number of tests; a Test service or a text report carries exactly one test and a package none, so those are set up from the service (the page shows them locked, the server refuses regardless). A test joining a profile is appended last, printed and not in the sum; order and flags are then edited on the service. |

## 17. Change request 2026-10-19 — service sequence reordering (LIS-CR-003, package P-C18, CP-42b)

The business owner asked for the prototype's reorder page: drag a row to a new position, move it up / down, type a position, and
reorder inside a clinical-group filter so that only the visible rows renumber. Nothing in the data model changes (no migration).

**What changes**

- `ListServiceSequenceQuery` gains `ClinicalGroupId?` (`GET /api/v1/catalogue/services/sequence?clinicalGroupId=`), beside the
  existing search and type filters; `ServiceSequenceRow` carries `ClinicalGroupId` next to the group names.
- `SaveServiceSequenceCommand` keeps its shape (the list of `(serviceId, sequence)`); the handler now refuses one number sent for two
  listed services before anything is read — new code **`CAT-033`** (`ServiceCodes.SequenceRepeated`, 422, rule `Service.EnsureDistinctSequences`,
  T12). A number held by a row outside the list is still `CAT-105` (409, tenant-wide INV-CAT-004) and nothing is applied in either case.
- Page CP-42 (`/catalogue/service-sequence`, same route and permissions `catalogue.view` / `catalogue.edit`): the rows move by native
  HTML5 drag and drop (handle with screen-reader text), by ↑ / ↓ buttons (disabled at the ends), by Alt+↑ / Alt+↓ on the focused row, or
  by typing a number in the position input — the row takes the place of that number and the rows in between shift. A clinical-group
  select stands beside the type filter. The page is *dirty* once the order differs from the stored one: a strip names the count of rows
  that will change with *Save order* / *Discard* (confirmed), the filters and *Refresh* are disabled until the order is saved or
  discarded, leaving the page asks first (`canDeactivate`), F10 saves. Every move is announced to assistive technology.
- Rule of the page (`ui-a/sequence-order.ts`, pure): **the visible rows keep the set of sequence numbers they already occupy and only
  their order changes**. With a filter on, a reorder therefore renumbers only within the numbers those rows held, so the rows outside the
  filter keep their numbers and no duplicate can arise; the save sends only the rows whose number changed.

**Scenarios added**

```gherkin
Scenario S-T12b: one number for two listed services is refused with CAT-033 (422) and nothing changes; the sequence rows filter by
                 clinical group and carry the group id (a service without a group is outside every group filter; an unknown group is
                 empty); a reorder of a filtered subset sends the subset's own numbers and leaves the rows outside it untouched
Scenario S-T12b-ui: the arrow buttons, a drop before / after a row, a typed number (its place, or the place of the last number below it)
                 and Alt+↑ / Alt+↓ reorder the visible rows within their numbers; an invalid entry is flagged and dropped; the dirty
                 strip, the disabled filters, the discard and leave confirmations, the read-only viewer (UAT B-12)
```

**Open questions (assumed answers let the build proceed; confirm or change)**

| # | Question | Assumed answer |
| --- | --- | --- |
| Q18 | What does a typed number mean on the reorder page: the row's new sequence number, or the position whose number it is? | The position: the row takes the place of that number (between two numbers, the place of the lower one) and every visible row keeps a number the set already uses. A laboratory that wants gaps for later inserts gives a service its number on the service page (CP-40), as before. |
| Q19 | May the filters change while an order is unsaved? | No: the filters and *Refresh* are disabled until the order is saved or discarded, because the renumbering is defined over the rows on screen. |
| Q20 | Should a reorder of a filtered subset be allowed to renumber rows outside the filter (for example to close gaps)? | No: only the visible rows change, within their own numbers (the prototype's "only the visible rows renumber"). |

## 18. Change request 2026-10-24 — culture specimens (LIS-CR-007, package P-MIC, reviewer point C-11, CP-43 / CP-40 / CP-56)

The reviewer asked where the culture sample types are (LIS-RC-001 C-11). Culture specimens stay ordinary sample types; a flag marks the
ones cultures use, so the Microbiology page can list them and a culture service can find them first.

**What changes**

- Domain: `SampleType.ForCulture` (bool, default false). `SampleType.Create(…, forCulture = false)`; `SampleType.Change(shortName, name,
  now, forCulture?)` — a null flag keeps the stored one, a change of the flag raises `SampleTypeChanged` like any other edit. The flag
  restricts nothing (any sample type may still be chosen for any service); no new code.
- Table: `app.sample_types.for_culture boolean NOT NULL DEFAULT false` (migration **`ReviewQuickWins`**). The migration then runs the
  dated section of `Persistence/Catalogue/CatalogueA.migration.sql`: every tenant's sample types — the platform's global templates
  included — whose English name contains *swab*, *culture*, *urine*, *stool*, *sputum*, *pus*, *csf* or *wound* (case-insensitive) are
  flagged. The statement only sets the flag, so a rerun changes nothing. The catalogue cloner copies the flag into a new laboratory.
- Contracts (all new fields optional, older clients keep working): `CreateSampleTypeRequest.ForCulture?` (absent = false),
  `UpdateSampleTypeRequest.ForCulture?` (absent = kept), `SampleTypeListItem.ForCulture`, `SampleTypeDetail.ForCulture`.
- Endpoints: `GET /api/v1/catalogue/sample-types?forCulture=true|false` and `GET …/sample-types/export?forCulture=` filter by the
  flag (absent = every type); create / update / detail carry the field. Permissions unchanged (`catalogue.view` / `create` / `edit` /
  `export`).
- CP-43 Sample types: the editor has the switch **Used for cultures** (`مستخدم للمزارع`); the list shows a *Cultures* column and the CSV
  export a yes / no column.
- CP-40 Service editor: for a service of type **Culture** the sample-type picker shows two groups — *Culture specimens* first, then
  *Other sample types*; every other type, or a laboratory without flagged types, keeps the single list.

**Scenario added**

```gherkin
Scenario S-T3c: a sample type is created as a culture specimen or not (default not); the editor switches the flag on and off, an update
                without the field keeps it; the register filtered forCulture=true lists the culture specimens only (forCulture=false the
                others, no filter all of them); the export honours the filter; a culture service's picker lists the culture specimens
                first, a test service one list
```

Tests: `ReferenceListTests.CR007_*` (domain), `CatalogueAFlowTests.Culture_specimens_are_flagged_on_the_sample_type_and_filtered_by_the_register`
(functional), `sample-types.page.spec.ts` and `service-editor.page.spec.ts` (SPA).

**Open questions (assumed answers let the build proceed; confirm or change)**

| # | Question | Assumed answer |
| --- | --- | --- |
| Q21 | Which existing sample types are culture specimens? | Those whose English name contains swab, culture, urine, stool, sputum, pus, csf or wound (the list of the change request). Urine also serves chemistry: the flag only orders the culture picker and fills the Microbiology tab, so a flagged urine type stays usable everywhere. The laboratory corrects the rest with the switch. |
| Q22 | Should a culture service be *restricted* to culture specimens? | No: the picker offers them first and still lists the other types (a blood culture bottle may be modelled as any type). |
| Q23 | Does an update that omits the flag clear it? | No: absent = kept, so a client older than this change never clears it; the SPA always sends it. |
