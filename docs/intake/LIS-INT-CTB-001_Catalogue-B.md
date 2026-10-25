# LIS-INT-CTB-001 — Catalogue B: order aids, report set-up and microbiology masters (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `catalogue` (second half, slot `cat2`). SRS pages CP-46 Preparations, CP-47 Comment library, CP-48 Doctors, CP-49 Diagnoses, CP-50 Sending labs, CP-51 Shortcut sets, CP-52 Test rules, CP-53 Reasons, CP-54 Report templates, CP-56 Drug types · Antibiotics · Organisms; admin SA-14 (adds Commercial names and Sensitivity values) and the catalogue-B lists of SA-12 / SA-13 under the platform tenant. Business rules BR-15, BR-24, BR-25. |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001), the Sky LIS prototypes and the LDM synthesis. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.3 — 2026-10-24 (proposed doctor code DR-nnn, change request LIS-CR-007 P-RES, reviewer point C-09, §18); 1.2 — 2026-10-24 (Microbiology naming and culture specimens tab, change request LIS-CR-007 P-MIC, reviewer point C-11); 1.1 — 2026-10-12 (report-template logo and signature images, preview and provisioning copy: validation F-02, A10) |
| Implemented in | `src/Platform.Domain/Catalogue` (files `CatalogueB*.cs`, `Preparation.cs`, `CommentTemplate.cs`, `Doctor.cs`, `Diagnosis.cs`, `SendingLab.cs`, `ShortcutSet.cs`, `TestRule.cs`, `Reason.cs`, `ReportTemplate.cs`, `DrugType.cs`, `CommercialName.cs`, `Antibiotic.cs`, `Organism.cs`, `SensitivityValue.cs`), `src/Platform.Contracts/Catalogue/CatalogueBContracts.cs`, `src/Platform.Application/Catalogue/{CatalogueB*.cs, Commands/CatalogueB*.cs, Queries/CatalogueB*.cs}`, `src/Platform.Infrastructure/Persistence/Catalogue/CatalogueB*`, `src/Platform.WebApi/Controllers/Catalogue/*` (controllers of this intake), `frontend/src/app/features/catalogue/{pages-b,data-b,state-b,ui-b}`, `frontend/src/app/core/api/catalogue-b.contracts.ts` |
| Shares with | Catalogue A (LIS-INT-CTA, slot `cat`: clinical groups, sample types, units, patient conditions, services, tests). This module references services and clinical groups **by id only**. |

## 1. Purpose and policy basis

Reception, laboratory and finance work faster and more safely when the lists they pick from are maintained once
and validated (BR-24 "validated selection only"):

- **Order aids** used at registration (CP-06, CP-09, CP-12): preparation questions asked for the ordered services
  (LDM §4.4 "precautions"; Sky prototypes "Preparations"), diagnoses that add their service set (LDM §3.8 `Diagnosis`),
  shortcut sets that drop several services at their own prices (LDM §3.8 "Tests Shortcuts"), test rules that auto-add
  companion services when **all** triggers are ordered (LDM §3.8 "Test Rules `{A}+{B} >> {C}+{D}`", `Check Rule`), and
  referring doctors with their share percentage (LDM §4.2 `Doctor`, §7.3 `Doctor Income`).
- **Lists of reasons** that other modules require on cancellation, sample rejection, discount, refund and transfer
  (LDM §5.9, §7.2; BR-15 "nothing is erased" — the reason travels with the record).
- **Report set-up**: the comment library inserted into report comments (LDM §5.4) and print templates with logo,
  signature, QR and language (LDM §12).
- **Microbiology masters** for culture entry (LDM §3.6): drug types, commercial names, antibiotics, organisms and
  sensitivity values.
- **Sending labs** (LDM §4.9 lab-to-lab): a contracted laboratory bound to a payer and contract tier, with its courier.

Every list is tenant reference data. The global versions are the same records under the platform tenant (MODULES.md
§1); a new laboratory receives an independent copy (BR-25).

## 2. Scope and out of scope

In scope: the fourteen aggregates below with create, edit, deactivate and reactivate; list registers with search,
status and list-specific filters; CSV export; the validation of a test rule (CP-52 "validate"); the read ports
consumed by Registration, Results and Reporting; the master-data cloner used when a laboratory is provisioned.

Out of scope (later intakes or other modules): the "publish" action of SA-12/SA-13 and the master-data update policy
(SA-33, setting `masterDataUpdatePolicy`); the use of each list inside registration, results and
billing (those modules consume the read ports); doctor-income and representative reports (Reporting); culture result
entry (Results).

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Preparation, precaution, question at reception (التحضيرات) | `Preparation` (`app.preparations`): code, bilingual question, answer type Text / YesNo / OptionList, options, gender, linked services (empty = all) |
| Comment library, insert comment (مكتبة التعليقات) | `CommentTemplate` (`app.comment_templates`): short name typed after `%`, bilingual text, optional clinical group |
| Referring doctor (الأطباء) | `Doctor` (`app.doctors`): code, bilingual name, specialty, occupation, branches (empty = all), share %, phone |
| Diagnosis, ICD (التشخيصات) | `Diagnosis` (`app.diagnoses`): ICD code (unique), bilingual name, services added at registration |
| Sending lab, lab-to-lab (المعامل المرسلة) | `SendingLab` (`app.sending_labs`): bilingual name, payer + contract tier, bilingual courier, phone, doctor |
| Shortcut, shortcut set (الاختصارات) | `ShortcutSet` (`app.shortcut_sets`): code typed at registration, bilingual name, ordered services (≥ 1) |
| Test rule, auto-add rule (قواعد الإضافة) | `TestRule` (`app.test_rules`): bilingual name, optional clinical group, trigger services, added services |
| Reason (الأسباب) | `Reason` (`app.reasons`): kind Rejection / Cancellation / Discount / Refund / Transfer, bilingual name |
| Report / print template (قوالب التقارير) | `ReportTemplate` (`app.report_templates`): bilingual name, type, logo file, signature file, QR, language, header and footer texts |
| Drug type, commercial name, antibiotic, organism, sensitivity (S/I/R) | `DrugType`, `CommercialName`, `Antibiotic`, `Organism`, `SensitivityValue` (`app.drug_types`, `app.commercial_names`, `app.antibiotics`, `app.organisms`, `app.sensitivity_values`) |
| Active / inactive (الحالة) | `IsActive`; records are deactivated, never deleted (BR-15) |

## 4. Actors and permissions

| Permission key | Grants |
| --- | --- |
| `catalogue.view` | list and read every list of this intake; validate a test rule (read-only); the pickers of other modules call the same list endpoints |
| `catalogue.create` | create a record in any list |
| `catalogue.edit` | edit a record (codes are immutable after creation) |
| `catalogue.manage` | deactivate and reactivate records |
| `catalogue.export` | download a list as CSV |

Actors (SRS): lab administrator (all lists), unit head (comment library), microbiologist (microbiology masters),
Master Data Reviewer / Operations Admin (the same pages under the platform tenant, SA-12/13/14). Permissions come
from the tenant role; deny by default.

## 5. Aggregates

All roots are `sealed`, tenant-scoped (`(tenant_id, id)`, query filter, FORCE RLS), carry `IsActive`, `RowVersion`
(xmin) and the audit stamps. References to other aggregates are by id.

| Root | Fields (limits) | References |
| --- | --- | --- |
| Preparation | `Code` (1–20, `[A-Z0-9][A-Z0-9._-]*`, unique, immutable), `Question` (LocalizedText ≤ 400), `AnswerType` (Text, YesNo, OptionList), `Options` (text[]: 2–30 distinct options of 1–100 chars when OptionList, empty otherwise), `Gender` (Any, Male, Female), `ServiceIds` (uuid[] ≤ 200, empty = all services) | services (Catalogue A) |
| CommentTemplate | `ShortName` (1–32, code pattern, unique, immutable), `Text` (LocalizedText ≤ 400), `ClinicalGroupId?` (null = all groups) | clinical group (Catalogue A) |
| Doctor | `Code` (1–20, unique, immutable), `Name` (LocalizedText ≤ 200), `Specialty?`, `Occupation?` (≤ 100), `BranchIds` (uuid[] ≤ 200, empty = all branches), `SharePercent` (0–100, 2 decimals), `Phone?` (mobile pattern) | branches (Organization) |
| Diagnosis | `IcdCode` (1–20, code pattern, unique, immutable), `Name` (LocalizedText ≤ 200), `ServiceIds` (≤ 200) | services |
| SendingLab | `Name` (LocalizedText ≤ 200, English name unique), `PayerId`, `ContractTierId` (tier of that payer), `Courier` (LocalizedText ≤ 200), `Phone?`, `DoctorId?` | payer, tier (Pricing); doctor (this module, composite FK) |
| ShortcutSet | `Code` (1–20, unique, immutable), `Name` (LocalizedText ≤ 200), `ServiceIds` (1–200, ordered) | services |
| TestRule | `Name` (LocalizedText ≤ 200, English name unique), `ClinicalGroupId?`, `TriggerServiceIds` (1–200), `AddedServiceIds` (1–200), disjoint | services, clinical group |
| Reason | `Kind` (Rejection, Cancellation, Discount, Refund, Transfer; immutable), `Name` (LocalizedText ≤ 200, unique per kind) | — |
| ReportTemplate | `Name` (LocalizedText ≤ 200, unique), `Type` (Results, Culture, Text, Claim, Receipt, Label, JobOrder), `LogoFileId?`, `SignatureFileId?` (file ids of the Files module, changed only by T6 / T7, never by T1 / T2), `ShowQr`, `Language` (En, Ar, Both), `HeaderText?`, `FooterText?` (LocalizedText ≤ 400, both languages or none) | files (Files) |
| DrugType | `Name` (LocalizedText ≤ 200, unique) | — |
| CommercialName | `DrugTypeId`, `Name` (LocalizedText ≤ 200, unique) | drug type (composite FK) |
| Antibiotic | `Code` (1–20, unique, immutable), `Name` (LocalizedText ≤ 200), `Abbreviation` (1–10, code pattern), `CommercialNameId?` | commercial name (composite FK) |
| Organism | `Code` (1–20, unique, immutable), `Name` (LocalizedText ≤ 200) | — |
| SensitivityValue | `Code` (1–10, unique, immutable: S, I, R, SDD …), `Name` (LocalizedText ≤ 200), `DisplayOrder` (0–999) | — |

No children: id sets are `uuid[]` columns (the precedent of `TenantMembership.BranchIds`), options are a `text[]`.

## 6. State table (every aggregate)

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Active | offered in pickers and applied by the read ports | everything except the code / short name / ICD code / reason kind | no | no | `catalogue.view` |
| Inactive | kept for history; not offered, not applied; existing references stay valid | same as Active | no | no | `catalogue.view` |

## 7. Transition table (X = any of the fourteen roots)

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | — → Active | Create X | `catalogue.create` | shape (VALIDATION-400); code valid (CAT-200) and unique (CAT-300); name unique where stated (CAT-301); referenced services exist (CAT-203), clinical group (CAT-204), branches (CAT-205), payer (CAT-206), tier of payer (CAT-207), doctor (CAT-208), drug type (CAT-260), commercial name (CAT-261) exist; a linked doctor / drug type / commercial name is active (CAT-310); list rules of §8 | — | — | — | `XCreated` | no |
| T2 | Active/Inactive | Edit X | `catalogue.edit` | X exists (CAT-380…393); same checks as T1 except the code | — | — | — | — (field changes are audited) | no |
| T3 | Active → Inactive | Deactivate X | `catalogue.manage` | X exists; idempotent | — | — | — | `XDeactivated` | no |
| T4 | Inactive → Active | Reactivate X | `catalogue.manage` | X exists; idempotent | — | — | — | `XReactivated` | no |
| T5 | — | Validate test rule (query) | `catalogue.view` | none; returns errors CAT-203/240/241/242 and warnings CAT-243 (same triggers as another active rule), CAT-244 (rules feed each other in a loop), CAT-245 (an inactive service) | — | — | — | — | no |
| T6 | Active/Inactive | Attach or replace a report-template image (logo or signature) | `catalogue.edit` | template exists (CAT-388); image name `logo`/`signature` (VALIDATION-400); content PNG, JPEG or WebP by its bytes (CAT-272), 1 byte … 512 KB (CAT-270 / CAT-271) | — | — | — | `ReportTemplateImageChanged` { ReportTemplateId, Image, FileId } | no |
| T7 | Active/Inactive | Remove a report-template image | `catalogue.edit` | template exists (CAT-388); the image is attached (CAT-394) | — | — | — | `ReportTemplateImageChanged` { ReportTemplateId, Image, FileId = null } | no |

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-CTB-001 | Codes (and short names, ICD codes, abbreviations) are upper-case `[A-Z0-9][A-Z0-9._-]*` within the list's length | aggregate (`CatalogueBGuard.Code`) | CAT-200 | "The code is invalid." |
| INV-CTB-002 | A code is unique per tenant within its list; codes never change | handler check + unique index `(tenant_id, code)`; no edit path | CAT-300 / UNIQUE-409 | "A record with this code already exists." |
| INV-CTB-003 | English names are unique per tenant for sending labs, test rules, report templates, drug types, commercial names, and per kind for reasons | handler check (case-insensitive) + unique expression index in `CatalogueB.migration.sql` | CAT-301 / UNIQUE-409 | "A record with this name already exists." |
| INV-CTB-004 | An option-list preparation has 2–30 distinct options; other types carry none | aggregate | CAT-210 / CAT-211 | "An option list needs at least two different options." |
| INV-CTB-005 | A shortcut set has at least one service | aggregate | CAT-230 | "A shortcut set needs at least one service." |
| INV-CTB-006 | A test rule has at least one trigger and one added service, and no service is both | aggregate | CAT-240 / CAT-241 / CAT-242 | "A service cannot be both a trigger and an added service." |
| INV-CTB-007 | A doctor's share is between 0 and 100 with at most two decimals | aggregate | CAT-220 | "The share must be between 0 and 100." |
| INV-CTB-008 | A sending lab's tier belongs to its payer | handler (read port on Pricing tables) | CAT-207 | "The contract tier does not belong to the payer." |
| INV-CTB-009 | Linked ids exist in the tenant (services, clinical group, branches, payer, doctor, drug type, commercial name) | handler (read port) | CAT-203…208, 260, 261 | "…does not exist." |
| INV-CTB-010 | Id sets are distinct, non-empty ids, at most 200 | aggregate | CAT-209 | "Too many linked records." |
| INV-CTB-011 | A report template's header and footer carry both languages or none | validator + `LocalizedText` | VALIDATION-400 | — |
| INV-CTB-012 | A phone number matches `+?digits` (8–15 digits, spaces between groups) | aggregate | CAT-202 | "The phone number is invalid." |
| INV-CTB-013 | A report-template image is a PNG, JPEG or WebP decided by its signature bytes (never the name or the declared type; SVG refused), 1 byte … 512 KB, stored by the Files module under `<tenant>/catalogue/<template>/<file id>` before the template keeps its file id | domain (`ReportTemplateImageFiles.Inspect`) + handler (bounded read, compensation delete when the commit fails) | CAT-270 / CAT-271 / CAT-272 | "The image must be a PNG, JPEG or WebP file." |
| INV-CTB-014 | A template image is read only by a signed-in member of the template's tenant: the key is composed with the session's tenant, the template is read through the tenant filter and RLS | handler (`GetReportTemplateImageQuery`, `[RequireAuthenticatedActor]`) | CAT-388 / CAT-394 | "The template has no such image." |

## 9. Segregation-of-duties rules

None. Reference data changes are attributed (audit stamps, field changes, hash-chained events) but need no second
person (assumption A6).

## 10. Approval gates

None. The SA-12/13 "publish" step is out of scope (§2).

## 11. Events and policies

```text
Event:    PreparationCreated / CommentTemplateCreated / DoctorCreated / DiagnosisCreated / SendingLabCreated /
          ShortcutSetCreated / TestRuleCreated / ReasonCreated / ReportTemplateCreated / DrugTypeCreated /
          CommercialNameCreated / AntibioticCreated / OrganismCreated / SensitivityValueCreated
          { <X>Id, Code or Kind (where the record has one), NameEn, OccurredAtUtc }
Raised by: T1
Reacts:   none yet (audit trail only)

Event:    <X>Deactivated / <X>Reactivated { <X>Id, OccurredAtUtc }
Raised by: T3 / T4
Reacts:   none yet; Registration reads the active flag at order time through the read ports

Event:    ReportTemplateImageChanged { ReportTemplateId, Image (Logo, Signature), FileId (null = removed), OccurredAtUtc }
Raised by: T6 / T7
Reacts:   none (audit trail only); printed reports read the template's current images when they are printed
```

No policy is defined by this module. All events are written to `ops.outbox_events` and `audit.audit_trail` in the
same commit.

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| All list fields | Operational reference data, Audited | field changes recorded by `FieldChangeInterceptor` |
| Doctor name, phone, share % | Personal data of a professional contact; share % is commercially sensitive | visible to `catalogue.view`; export requires `catalogue.export` |
| Sending lab courier and phone | Business contact | as above |
| Retention | kept while the tenant exists; never deleted (BR-15) | inactive records stay referenced by history |

## 13. Reference data and configuration

All fourteen lists are tenant reference data. Global templates live under the platform tenant
(`00000000-0000-0000-0000-000000000001`) and are copied to a new tenant by `ICatalogueBCloner` (BR-25) — active
rows only, fresh UUIDv7 ids, service and clinical-group ids remapped by **code** through the target tenant's
catalogue A, drug types and commercial names remapped by English name, doctors and sending labs not copied (they are
laboratory-specific), report-template file ids cleared. A row whose meaning would change is skipped rather than
copied: a preparation or diagnosis that loses every linked service, a comment whose clinical group is missing, a
shortcut set without services, a test rule that loses a trigger or every added service. Rows that already exist in the
target win (`ON CONFLICT DO NOTHING`), so a repeated clone only adds what is missing; the summary counts the rows
copied per list and the links dropped. The clone runs set-based SQL inside the provisioning's elevated scope and
transaction (no change tracker: the provisioning's own events record it). No tenant settings are read by this module.

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Preparations | search (code, question), status, answer type | code, question, type, gender, services (or "all services"), status | `catalogue.view` |
| Comment library | search (short name, text), status, clinical group | short name, text, group (or "all groups"), status | `catalogue.view` |
| Doctors | search (code, name, specialty, phone), status, branch | code, name, specialty, occupation, branches, share %, phone, status | `catalogue.view` |
| Diagnoses | search (ICD code, name), status | ICD code, name, services, status | `catalogue.view` |
| Sending labs | search (name, courier, phone), status, payer | name, payer, tier, courier, phone, doctor, status | `catalogue.view` |
| Shortcut sets | search (code, name), status | code, name, services, status | `catalogue.view` |
| Test rules | search (name), status, clinical group | name, group, rule `{A} + {B} >> {C}`, status | `catalogue.view` |
| Reasons | search (name), status, kind | kind, name, status | `catalogue.view` |
| Report templates | search (name), status, type | name, type, logo, signature, QR, language, status | `catalogue.view` |
| Drug types / Commercial names / Antibiotics / Organisms / Sensitivity values | search, status (+ drug type for commercial names) | code, name, abbreviation, commercial name, drug type, order, status as applicable | `catalogue.view` |
| CSV export of any register | same filters as the register | every column, both languages (UTF-8 with BOM, formula-injection safe) | `catalogue.export` |

## 15. Notifications and escalations

None.

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Catalogue/CatalogueB*Tests.cs`,
`tests/Platform.IntegrationTests/CatalogueBModuleTests.cs` and `tests/Platform.WebApi.FunctionalTests/CatalogueBFlowTests.cs`.

```gherkin
Scenario S-CTB-T1-ok: create a record — happy path
  Given a user holding catalogue.create
  When the user creates a preparation "FAST" (OptionList, options "8 h", "12 h") linked to two existing services
  Then the preparation is active, its code is upper-cased and PreparationCreated is raised

Scenario S-CTB-T1-code: duplicate code refused (INV-CTB-002)
  Given an existing shortcut set "ICU"
  When a user creates another set with code "icu"
  Then the attempt is refused with CAT-300 (409)

Scenario S-CTB-T1-options: option list with fewer than two options refused (INV-CTB-004)    → CAT-210 (422)
Scenario S-CTB-T1-set: shortcut set without services refused (INV-CTB-005)                  → CAT-230 (422)
Scenario S-CTB-T1-rule: a service both trigger and added refused (INV-CTB-006)               → CAT-242 (422)
Scenario S-CTB-T1-share: doctor share above 100 refused (INV-CTB-007)                        → CAT-220 (422)
Scenario S-CTB-T1-ref: unknown service / tier of another payer refused (INV-CTB-008/009)    → CAT-203 / CAT-207 (422)
Scenario S-CTB-T2-ok: edit keeps the code and records the field changes
Scenario S-CTB-T3/T4: deactivate and reactivate are idempotent and raise their events once
Scenario S-CTB-T5: validating a rule reports errors and warnings without saving anything
Scenario S-CTB-PORT: the read ports apply only active records
  Given rule {A}+{B} >> {C}, rule {C} >> {D} and an inactive rule {A} >> {E}
  When registration orders A and B
  Then C and D are added (chained), E is not
Scenario S-CTB-CLONE: provisioning copies the platform tenant's active lists with fresh ids and remapped services
Scenario S-AUTHZ: any request without the permission → 403 AUTHZ-001 before the handler runs
Scenario S-TENANT: every list of tenant X is invisible to tenant Y (query filter + FORCE RLS)
```

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| A1 | Does a test rule fire when **any** or **all** triggers are ordered? | All (LDM §3.8 "ordering all triggers together"); rules chain until no new service is added; a service already ordered is not added twice. |
| A2 | Are records deleted? The prototypes show "Delete … blocked when referenced". | Never deleted (BR-15); deactivation replaces deletion. |
| A3 | Are all names bilingual? The prototypes show single-language names for drug types, commercial names, antibiotics, organisms and report templates. | Yes: every name is `LocalizedText` with both languages required (master data carries Arabic names on every record). |
| A4 | Commercial names: one drug type or several (prototype "Drug Type(s)")? Antibiotic: one commercial name or several? | One drug type per commercial name, at most one commercial name per antibiotic (as specified for this build). |
| A5 | Reason kinds | Rejection, Cancellation, Discount, Refund, Transfer (Transfer added for CP-24 money transfer); a reason's kind never changes. |
| A6 | Does a change to reference data need a second person (SoD) or an approval? | No. |
| A7 | Long texts | Questions, comment texts, header and footer texts ≤ 400 characters per language (`LocalizedText.MaxLength`). |
| A8 | Is a doctor without branches available everywhere? | Yes: empty branches = every branch (the registration doctor picker filters by the session branch). |
| A9 | Are linked services required to be active? | They must exist in the tenant; inactive services may stay linked (the validator warns for test rules); the read ports return ids and registration applies the service's own active flag. |
| A10 | Report template logo / signature | **Built (2026-10-12, validation F-02).** The images are files of the Files module (`IFileStore`, `Lis:Files:Root`, key `<tenant>/catalogue/<template>/<file id>`); the template keeps only the file ids (`LogoFileId`, `SignatureFileId`; no migration). They are uploaded, replaced and removed on their own (`POST` / `DELETE /api/v1/catalogue/report-templates/{id}/images/{logo\|signature}`, multipart field `file`, `catalogue.edit`, T6 / T7): PNG, JPEG or WebP decided by the bytes, 512 KB at most like the laboratory logo (CAT-270…272); the create / edit request no longer carries them. `GET …/images/{logo\|signature}` serves an image inline to any signed-in member of the tenant (the results users who print reports hold no catalogue key), never to an anonymous caller or another tenant (CAT-388 / CAT-394); the SPA reads it with the bearer token as a blob and shows it through an object URL (CSP `img-src blob:`). The register's row action "Logo, signature and preview" (SA-12, and CP-54 when Release 2 opens it) shows a thumbnail of each, the upload / replace / remove buttons for `catalogue.edit`, and a preview: a sample verified result report laid out with the template, drawn by the results report renderer itself (`REPORT_DOCUMENT_RENDERER`, provided at the application root). A printed or previewed result report whose clinical group uses the template carries the logo in the letterhead (in place of the Sky LIS mark) and the signature above the verifier's name — on a verified, final report only, never on a preliminary one. **Provisioning (BR-25): copied, not shared.** The Catalogue B cloner copies each image of every template it inserts into the new laboratory's own store under the new template, with new file ids (`IFileStore.CopyAsync`, elevated scope only): a laboratory never reads the platform's files, and a later change on the platform template leaves the laboratory's copy alone; an image whose content is missing is dropped. Files are not transactional: a provisioning that rolls back afterwards leaves unreferenced copies behind (no business deletion, BR-15). Tests: `ReportTemplateImageFlowTests` (functional), `CatalogueBSetupTests` (domain), `LocalFileStoreTests` (copy), `CatalogueBModuleTests` (clone), `report-template-images.component.spec.ts`, `report-template-preview.spec.ts`, `report.document.spec.ts`. |
| A11 | Cloning order (BR-25) | Catalogue A's cloner runs first (services and clinical groups must exist in the new tenant); links to services whose code is missing in the target are dropped and counted. |
| A12 | CSV export | Server-side per list (`GET …/export`), English and Arabic columns, UTF-8 BOM, cells starting with `= + - @` prefixed with `'`. |
| A13 | Who may read reasons / doctors in other modules' pickers? | `catalogue.view` (the same list endpoints with `isActive=true`); roles that register or cancel need it. |
| A14 | Editors of lists that link sets of records | The kit's master-data page has no multi-select field, so preparations, doctors, diagnoses, shortcut sets and test rules are hand-written pages built from the kit's parts (page header, toolbar, data table, paginator, drawer, form fields) with the module's chip picker; the other nine lists use `MasterDataPageComponent`. They move to the master-data page once the kit offers a multi-select field. |
| A15 | Same-wave references | Services and clinical groups (Catalogue A), branches (Organization), payers and contract tiers (Pricing) are checked by SQL on `app.services`, `app.clinical_groups`, `app.branches`, `app.payers`, `app.contract_tiers` (`id`, `tenant_id`, plus `code`, `name_en`, `name_ar`, `is_active`, `payer_id` where used); no foreign keys to them yet. |

## 17. Change request 2026-10-24 — Microbiology naming and culture specimens (LIS-CR-007, package P-MIC, reviewer point C-11, CP-56 / SA-14)

The reviewer asked what "Drug types" are and where the culture sample types are (LIS-RC-001 C-11). The answer is wording and a direct path,
not a new structure.

**What changes**

- **Rename (wording only).** The *Drug types* list reads **Antibiotic classes** / `فئات المضادات الحيوية` with the subtitle "Groups the
  antibiotics of the sensitivity panel (Penicillins, Cephalosporins …)". The commercial-name form and list call the link to it **Class**
  (`الفئة`) with the hint "Class: Penicillins, Cephalosporins …", and the not-found messages CAT-260 / CAT-389 say "antibiotic class".
  Only dictionary values change (`catalogue-b.drugTypes.*`, `commercialNames.*`, `microbiology.tab.drug-types`): the route
  `…/microbiology/drug-types`, the API path `/api/v1/catalogue/drug-types`, the entity `DrugType`, the table `app.drug_types` and the
  permission keys keep their names. Note: in this model the class is held by the *commercial name* (antibiotic → commercial name →
  class), so the "Class" label sits on the commercial-name form; the antibiotic form has no class field of its own.
- **Sixth tab Culture specimens** (`…/microbiology/culture-specimens`, `CultureSpecimensPage`): a read-only list of the sample types
  flagged *Used for cultures* (Catalogue A, `SampleType.ForCulture`, intake LIS-INT-CAT-001 §18) — code, short name, name, status
  (active and inactive) — read from `GET /api/v1/catalogue/sample-types?forCulture=true` with `catalogue.view`, and the link **Manage on
  Sample types** to `../../sample-types` (CP-43 / SA-12 master data). No editor of its own: the flag is set on the sample type.
- **Tab order**: Organisms · Antibiotics · Antibiotic classes · Commercial names · Sensitivity values · Culture specimens; the bare
  `…/microbiology` address now opens Organisms (the first tab).

Tests: `culture-specimens.page.spec.ts` (the filter, the columns, the link, read-only, the tab order) and `catalogue-b-pages.spec.ts`
(tab of a URL, default tab).

**Open question (assumed answer lets the build proceed)**

| # | Question | Assumed answer |
| --- | --- | --- |
| A16 | Should the Super Admin portal (SA-14) list the culture specimens of the global catalogue as a menu entry like the other microbiology lists? | Yes: the page works under `/admin/catalogue/microbiology/culture-specimens`; the integrator adds the admin nav entry beside the others and relabels `shell.nav.drugTypes` "Antibiotic classes". |

## 18. Change request 2026-10-24 — Proposed doctor code (LIS-CR-007, package P-RES, reviewer point C-09, CP-48)

**Today.** *Add doctor* required a typed code (1–20, letters, digits, `.`, `-`, `_`, unique per laboratory), so reception had to
invent one for every referring doctor.

**Change.**

- `POST /api/v1/catalogue/doctors` (`CreateDoctorCommand`, `catalogue.create`): `code` is optional (`CreateDoctorRequest.Code` and the
  command's `Code` are nullable; old bodies with a code keep working). A blank, whitespace-only or missing code takes the next
  **`DR-nnn`** of the laboratory: `Doctor.GeneratedCodeKind` `doctor`, prefix `DR`, at least three digits, no year (`DR-001`, … `DR-999`,
  `DR-1000`), formatted by `Doctor.GeneratedCode` / `ReferenceNumberFormat.Short` (SharedKernel).
- Numbering: `IReferenceNumbers.NextShortAsync(kind, prefix, width)` over the same atomic upsert of `app.reference_counters` as the yearly
  numbers, on the counter row of year `0` (`ReferenceNumbers.UndatedYear`): one never-restarting counter per tenant, no migration. Gaps
  are allowed and numbers are never reused. A generated number already held by a typed code is skipped (up to 50 tries, then CAT-300).
- Order of checks: a typed code is validated first as before (CAT-200 format, CAT-300 duplicate), then the branches (CAT-205); the
  number is drawn last, so a refused request spends no number.
- The code stays immutable (Edit keeps it read-only). Typed codes are unchanged, including codes that look like `DR-nnn`.
- SPA (`features/catalogue/pages-b/doctors.page`): on *Add doctor* the Code field has no required mark and the placeholder
  `catalogue-b.doctors.codePlaceholder` ("Generated on save (DR-001 …)" / «يُولَّد عند الحفظ (DR-001 …)»); a blank field is sent as `null`;
  on Edit the field is read-only without the placeholder.

Tests: domain `DoctorCodeTests` (format, padding, valid code, counter below 1 refused); functional
`CatalogueBDoctorCodeFlowTests.A_blank_code_is_generated_per_laboratory_and_skips_a_typed_one` (DR-001, DR-002 without a `code`
property, typed `dr-003` skipped by the counter, refusals spend no number, a second laboratory starts at DR-001); spec
`doctors.page.spec.ts` (placeholder, no required mark, `code: null`, read-only on Edit).

| # | Question | Assumed answer |
| --- | --- | --- |
| A17 | Prefix and width of the generated code | `DR` and three digits, no year, per laboratory (reviewer's example). |
| A18 | Does the Super Admin portal (platform tenant) generate codes too? | Yes: the same command serves it, with the platform tenant's own counter. Doctors are not copied to new laboratories (no provisioning path creates them). |
| A19 | Should a typed code of the form `DR-nnn` advance the counter? | No: the counter only moves when it issues a number; it skips a number a typed code already holds. |

## 19. Change request 2026-10-24 — Report design of a template (LIS-CR-010, package P-RPT, reviewer point C-14, CP-54 / SA-12)

**Model.** `ReportTemplate.Design` (`ReportDesign?`, column `app.report_templates.design`, nullable string with the enum
convention's CHECK; the wave migration `ReviewFeatures` adds it, no seed). Set by T1 Create and T2 Edit
(`ReportTemplateRequest.design`, optional; null or absent clears it, so a full edit without the field follows the laboratory
again). Only the templates of result reports carry one: `Results`, `Culture`, `Text`; any other type with a design is refused
**`CAT-273`** (422). An unknown name is a shape error (400). The design is part of the register row
(`ReportTemplateListItem.design`) and of the read port (`ReportTemplateView.Design`); the platform clone copies it
(`CatalogueBCloner`). Precedence: a payer contract's design wins over the template's, which wins over the laboratory parameter
(Results intake §18).

**SPA.** The edit drawer gains *Report design* (select, empty = the laboratory's). The drawer *Logo, signature and preview*
shows the sample report in the chosen design: buttons try each design live (nothing is saved), and **Use this design**
(`catalogue.edit`) saves it on the template (a T2 edit of the template as it is). Cumulative previews carry fictitious previous
values. Specs: `report-template-images.component.spec.ts`, `catalogue-b-pages.spec.ts`.

| # | Question | Assumed answer |
| --- | --- | --- |
| A20 | Should a receipt or label template accept a design for later use? | No: refused (`CAT-273`) so the choice is not mistaken for an effect. |
| A21 | Does the CSV export list the design? | Not in this change (columns unchanged). |
