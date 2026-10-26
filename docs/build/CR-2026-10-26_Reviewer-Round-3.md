# Change request 2026-10-26 — reviewer's third round: master data and sample merging (LIS-CR-015)

Source: the business owner's notes of 2026-10-26 after using 0.24.0 (quoted in §1). Four packages built in parallel by module agents in
build slots; the integrator owns the one migration of the wave (`ReviewerRound3`), navigation, shell, SRS / UAT / README, browser check
and release.

## 0. Rules of this wave

- Edit your package's files only (§1 table). Two packages share `Application/Registration/OrderPlanner.cs` and
  `Domain/Registration/Visit.cs`: each edits **only the members named for it** and re-reads the file right before every targeted edit
  (never rewrite a whole file, never reformat).
- **No migration from you**: change the model and the EF configuration; the integrator generates `ReviewerRound3` once every package
  reports, adds the RLS / CHECK statements, regenerates `deploy/postgres/migrations.sql` and messages you to run the database tests.
- Never edit `PermissionCatalog.cs`, `AppDbContext.cs`, `Persistence/Migrations/**`, `deploy/postgres/**`, `app.routes.ts`, `shell/**`,
  `public/i18n/shell.*.json`, `angular.json`, `package.json`, `CLAUDE.md`, `README.md`, `docs/srs/**`, `docs/uat/**`, test `Support/`
  folders.
- Build in your slot: `source scripts/slot-env.sh <slot>` **then** `dotnet build Platform.sln -c Release` (the slot redirects the output to
  `.local/build/<slot>`; a build before sourcing leaves `--no-build` test runs on stale binaries). Run the three non-database suites
  (Architecture, Domain, Application) and the frontend gates for your files (`npx ng lint`, `node scripts/check-boundaries.mjs`,
  `npx ng test --watch=false --include=<your specs>`, `npx ng build` at the end). Write the database tests now; run them when the
  integrator messages you.
- Frontend rules (CLAUDE.md §5): no `any`, no web storage, no `subscribe()` in components, component stylesheets under 4 kB, both
  dictionaries (`en`, `ar`, counted forms where a count is shown), RTL-safe, `*appCan` for buttons, F10 saves where a page saves.
- Final report: what you built, files changed, per-suite counts, assumptions taken, README bullets and UAT steps to add, intake text to add
  (the integrator writes the docs).

## 1. The notes and the packages

| # | Note (business owner, 2026-10-26) | Package |
| --- | --- | --- |
| M1 | "Preparation screen, edit or insert mode: the options should be separate lines, not a free text." | P-UI |
| M2 | "Services setup, in the details of members: let the user enter the setup of the member (test setup) directly from the grid and get back to the service after finishing." | P-UI (service editor) + P-FCT (test editor return) |
| M3a | "Formula test, formula entry: add the formula tools as buttons to make it easier for the user." | P-FCT |
| M3b | "When the user registers a formula test, automatically pull the tests used in the formula into the registration without adding their value to the payment." | P-FCT (operand setup) + P-FRG (registration) |
| M3c | "Add a check box in the formula entry screen beside each formula test, *Visible in receipt*: checked = the service added for the formula appears on the receipt, unchecked = invisible on the receipt." | P-FCT (setup) + P-FRG (receipt) |
| M4 | "The barcode is separated per processing unit then per sample condition. If two sample conditions are compatible, the parent's name appears and the other merges into it: Serum + Fasting and Serum + Random, with Random compatible with Fasting, give one sample ID named Serum - Fasting that includes the Random tests. I need a screen for the data of the compatible samples." | P-CMP |

| Package | Slot | Files / modules |
| --- | --- | --- |
| P-UI Preparation options as lines; member setup from the service grid | `acc` | `features/catalogue/pages-b/preparations.page.*` (+ spec), `features/catalogue/pages-a/service-editor.page.*` (+ spec), `catalogue-b.{en,ar}.json`, the service-editor keys of `catalogue-a.{en,ar}.json` (add keys only, under `services.*`) |
| P-FCT Formula tools and formula operands in the catalogue | `cat` | `Domain/Catalogue/LabTest.cs` (formula members), `LabTestCodes.cs`, new `Domain/Catalogue/FormulaOperand*.cs`, `Application/Catalogue/**` formula commands / queries / `LabTestFormulaRules`, catalogue contracts for tests, `LabTestsController`, EF configuration of `LabTest` in `CatalogueAConfigurations.cs` (the `LabTest` block only), `Infrastructure/Persistence/Catalogue/FormulaOperandLookup.cs` (replace the seam), `features/catalogue/pages-a/test-editor.page.*`, `state-a/test-editor.facade.ts`, `data-a/catalogue-a.api.ts` (formula calls only), `core/api/catalogue-a.contracts.ts` (formula types only), the test-editor keys of `catalogue-a.{en,ar}.json` (add keys only, under `tests.*`), catalogue tests |
| P-FRG Formula operands at registration and on the receipt | `reg` | `Domain/Registration/VisitLine.cs`, `RegistrationEnums.cs`, `Visit.cs` (**only** `AddItems`, cancellation / release paths for the new line kind, `IsTopLevel` users), `Application/Registration/OrderPlanner.cs` (**only** `PlanAsync`, `Check`, `CheckRepeatedTests` and new private members; not `TubeSpecOf`), `RegistrationSupport.cs` (line views), registration contracts, `Application/Results/Policies/ResultOrderPolicies.cs` (only if operand lines need it), Billing invoice read (`BillingReadAdapters.cs` `Lines`, `AccountQueries.cs` invoice, `BillingContracts.cs` `InvoiceLine`), EF configuration of `VisitLine`, `features/registration/**` (line list, price query, job order), `features/billing/ui/documents/invoice-document.component.ts`, `registration.{en,ar}.json`, registration / billing tests |
| P-CMP Compatible sample conditions | `smp` | `Domain/Catalogue/SampleType.cs` (`SampleCondition` + a method on `SampleType`), sample-type commands / queries / contracts / controller, `ICatalogueLookup.cs` **only** `CatalogueSampleConditionInfo` (+ its fakes in `tests/**/RegistrationFakes.cs`, `ResultsFakes.cs`, `CatalogueAModuleTests.cs` if they build it), `CatalogueLookup.cs` (the sample-type read only), EF configuration of `SampleCondition`, `Domain/Registration/VisitOrder.cs` (`TubeSpec`), `TubePlanner.cs`, `Visit.cs` (**only** `AssignTubes`, `CountNewTubes`, `TubeNeedsOf`, `ExistingTubes`), `OrderPlanner.cs` (**only** `TubeSpecOf`), `RegistrationSupport.cs` (**only** `KeyText`, `ParseKey`, `OrderPlanViews.Tubes` if needed), `Application/Registration/Commands/VisitCommands.cs` (the release re-keying near line 283), new page `features/catalogue/pages-a/compatible-conditions.page.*`, its route in `catalogue-a.routes.ts`, `catalogue-a.{en,ar}.json` (add keys only, under `compatible.*`), catalogue / registration / samples tests |

The integrator has already placed the seam P-FCT and P-FRG share (`Application/Catalogue/IFormulaOperandLookup.cs`,
`CatalogueFormulaOperandInfo`, stub adapter `Infrastructure/Persistence/Catalogue/FormulaOperandLookup.cs` answering no operand). P-FRG
codes and unit-tests against the port with a fake; P-FCT makes the adapter read the stored operands.

## 2. P-UI — Preparation options as lines (M1); member setup from the service grid (M2)

**M1 today.** `preparations.page.ts` holds the options in one string control shown as a `<textarea>` (one option per line, split by
`splitOptions`). The stored shape (`string[]`, at most 30 options of at most 100 characters, at least 2, no case-insensitive duplicate:
CAT-210 / CAT-211) and the registration consumer stay as they are.

**M1 build.** In insert and edit mode an *Option list* question shows one input per option: a numbered row with the option text, move up /
down and remove; *Add option* below (disabled at 30); Enter in the last row adds a row and focuses it; pasting several lines into a row
splits them into rows; blank rows are ignored on save. Errors show on the row (duplicate, too long) and under the list (fewer than 2, more
than 30). A `FormArray` like the test editor's list values; the request is built from the rows in order. Specs: add / remove / reorder /
paste / duplicate / edit an existing question (rows filled from `item.options`).

**M2 today.** The members grid of `service-editor.page.html` (rows: code, name, type, In-sum, Visible, up / down / remove) has no way to
open a member; member edits wait in `membersDirty` until saved; neither editor has a leave guard; both `back()` go to the list.

**M2 build.**
- A row action *Open setup* (icon button with a label for screen readers, `catalogue.view`) on every member row: a test member opens
  `tests/<memberId>`, a package's member service opens `services/<memberId>`, always with `returnTo` = the service editor's current URL
  (`RETURN_TO_PARAM` of `shared/ui/return-link`). The shell already shows the *Back* chip for a `returnTo`.
- Unsaved changes of the service (form or members) ask first in an `app-dialog`: *Save and open* (saves, then opens), *Open without
  saving* (discards), *Cancel*.
- The service editor's own `back()` honours a `returnTo` it was opened with (a package opened from another package's grid goes back to it).
- Returning re-reads the service (the editor already loads on each visit; check it).
- Specs: the action per member kind, the URL with `returnTo`, the three dialog choices.

## 3. P-FCT — Formula tools and operands in the catalogue (M3a, M3b setup, M3c setup; M2 return of the test editor)

**Today.** `LabTest.FormulaExpression` (text, ≤ 1000) + `FormulaReferences` (codes) set by `SetFormula` (CAT-062/063/065, cycles CAT-066
through `LabTestFormulaRules`); grammar in `IFormulaEvaluator.cs` (`[CODE]` references, `+ - * / ^ ( ) ,`, comparisons, `ROUND IF MIN MAX ABS`,
`@AGE @FEMALE`). The formula tab is a textarea, *Check formula* and a test picker inserting `[CODE]`. Operands are not ordered at
registration: a formula without its operands on the visit waits for ever (`FormulaRecalculator`, missing value).

**Build — catalogue.**
- `FormulaOperand` value (`TestCode`, `ServiceId?`, `VisibleOnReceipt`) stored with the test, one per referenced code, in reference order
  (EF: a JSON column `formula_operands` on `app.lab_tests`, `[]` by default; the integrator writes the migration). `SetFormula` takes the
  operands with the expression: every reference has exactly one operand entry (missing entries are added with the default service and
  `VisibleOnReceipt = false`; entries for codes no longer referenced are dropped).
- The operand service must be an active, orderable service whose members include the referenced test (`CAT-067` "The service chosen for
  [CODE] does not carry that test"); null is allowed (nothing is ordered for that operand; the editor warns).
- Default service for an operand: the active orderable *Test* service whose only member is the test; if none, null.
- `SetLabTestFormulaCommand` / `PUT tests/{id}/formula` carry `operands`; the formula check (`POST tests/formula/check`) answers, per reference,
  the services that carry the test (id, code, names, type) and the default, so the editor can fill the operand table without another call.
- `FormulaOperandLookup` (replace the seam): for the given service ids, the formula tests among their **direct test members** (and a test
  service's own test) with their stored operands, resolving `OperandTestId` by code in the tenant; inactive operand tests are skipped.
- Older tests keep `[]` operands: the lookup derives the default service per reference for a formula saved before this change, with
  `VisibleOnReceipt = false`, so an existing formula still pulls its operands.

**Build — test editor.**
- **Formula tools** above the textarea: buttons inserting at the caret `+ − × ÷ ^ ( ) ,` (inserting `+ - * / ^ ( ) ,`), comparisons
  `< <= > >= = <>`, functions `ROUND( )`, `IF( , , )`, `MIN( )`, `MAX( )`, `ABS( )` (caret placed inside the parentheses), variables `@AGE`,
  `@FEMALE`, and *Clear*; grouped (Operators · Compare · Functions · Patient); keyboard reachable; titles explaining each; Arabic labels
  for the groups (symbols stay as they are).
- **Operand table** under the formula (after a successful check or on load): one row per referenced test — code and name, *Service to order*
  (select of the services that carry the test, default preselected, "None — not ordered" allowed with a warning) and the checkbox
  **Visible in receipt** (default unchecked). Saved with the formula.
- **Return** (M2): `back()` and the breadcrumb honour a `returnTo` query parameter (to the service the user came from); saving keeps the
  page and the parameter.
- Tests: domain (operand normalisation, CAT-067), application (default service, check answer), functional (save and read operands; the
  lookup for a profile and a test service; an older formula with `[]`), specs (tool insertion at the caret, operand table, return).

## 4. P-FRG — Formula operands at registration and on the receipt (M3b, M3c)

**Build — ordering.**
- After the rule companions in `OrderPlanner.PlanAsync`, a *formula* step: for each new top-level line (and for each member service of a
  new package) read `IFormulaOperandLookup.ForServicesAsync`; for each operand with a service: if any live line of the visit (existing or
  in this request, package members included) already carries the operand **test**, add nothing; otherwise add the operand service once
  per visit as a line of the new kind `VisitLineKind.FormulaOperand` (origin `VisitLineOrigin.Formula`, `ParentLineId` = the formula's line,
  list / patient / payer prices 0, `VisibleOnReceipt` from the catalogue). Two formulas needing the same operand add it once (first parent).
- An operand line is not top-level (`IsTopLevel` false: no discount base, no repricing, no own payment), gets its tube and results like
  any sample-carrying line, is shown under its formula line, and follows its parent: cancelling or releasing the formula line cancels or
  releases its operand lines; an operand line is not cancelled on its own (REG code for the refusal, next free). It is skipped by the
  unpriced and duplicate checks; `CheckRepeatedTests` ignores it.
- The dry run (`visits/check`) and the price query (CP-12) list the operand lines with price 0 and the tag "for formula <code>", so the
  clerk sees them before saving.
- `VisitLine.VisibleOnReceipt` (default true for every other line). Results: verify `ResultOrdering` creates the operand line's results
  (it walks every Ordered line; fix it only if a kind filter skips the new kind).

**Build — receipt.** The *Receipt / invoice* (`GetInvoiceHandler`, `BillingReadAdapters.Lines`, `invoice-document.component.ts`) leaves
out lines with `VisibleOnReceipt = false`; visible operand lines print with 0.00 and the "for formula" tag. The job order keeps every line
(the bench needs them), operand lines marked "↳ formula". The payment receipt has no lines (unchanged).

**Tests.** Domain (line kind, parent cancel / release, price 0, top-level exclusions), application (planner with a fake lookup: added,
already present, shared operand, package member formula), functional (register a formula service → operand line with price 0 and results;
cancel the formula → operand cancelled; invoice hides an invisible operand), specs (registration line list, price query, invoice).

## 5. P-CMP — Compatible sample conditions (M4)

**Today.** BR-02 groups tubes by `TubeKey(ClinicalGroupId, SampleTypeId, SampleConditionId?)` (`VisitOrder.cs`); the clinical group is the
processing unit; a condition is a child of its sample type (`app.sample_conditions`, edited in a drawer on Sample types). No compatibility
exists.

**Build — setup.**
- `SampleCondition.MergesIntoConditionId` (nullable): the condition of **the same sample type** whose tube this condition's tests may use.
  Rules (codes `CAT-280`…): not itself; same sample type; the target is active and does not merge into another condition (one level);
  a condition that others merge into cannot merge into another. Set per sample type through `SampleType.SetCompatibleConditions(pairs)`.
- `GET /api/v1/catalogue/sample-types/compatible-conditions` (every sample type with its conditions and their target) [catalogue.view] and
  `PUT /api/v1/catalogue/sample-types/{id}/compatible-conditions` `{ pairs: [{ conditionId, mergesIntoConditionId | null }] }`
  [catalogue.edit]; `CatalogueSampleConditionInfo.MergesIntoConditionId` for the readers.
- Page **Compatible sample conditions** (`/catalogue/compatible-conditions`, and `/admin/catalogue/compatible-conditions` for the platform
  catalogue as Sample types has both): a table per sample type (sample type · condition · *Merges into* · active), search, edit in a drawer
  per sample type with a select per condition ("Its own sample" or another condition of the type), the rules checked before saving,
  export like the other catalogue lists if the list pattern gives it. The integrator adds the navigation entries (Setup, after Sample
  types) and the shell labels.

**Build — tubes.**
- `TubePlanner` (with the compatibility carried on `TubeSpec`, e.g. `MergesInto` = the target condition id and names): a need whose condition
  merges into P joins a tube keyed `(group, type, P)` when **that tube exists live on the visit or is needed in the same request**; otherwise
  it keeps its own condition (a Random-only order still prints "Serum · Random"). The merged tube keeps P's key and names (the label reads
  "Serum · Fasting") and carries both lines. Never the other way: a Fasting need does not join a Random tube; an existing Random tube is
  not renamed when Fasting is added later (Fasting opens its own tube).
- Apply it everywhere a key is planned: `Visit.AssignTubes` / `CountNewTubes` / `TubeNeedsOf`, the dry-run tube preview, the release
  re-keying in `VisitCommands`. Samples, labels and results follow the tube (no change expected; verify the Samples label document reads
  the tube's condition).
- Tests: domain (merge when the parent is needed or exists; no merge alone; never reverse; one level), application, functional (setup rules;
  a visit with a Fasting and a Random service of one unit and type → one tube, one sample, label "Fasting", both lines on it), specs
  (page, drawer, rules).

## 6. Assumptions (to confirm with the business owner)

1. M3b — the service ordered for an operand is chosen per operand in the formula setup (default: the test's own single-test service);
   "None" orders nothing. An operand already on the visit is not added again, and an operand line is free and follows its formula.
2. M3c — *Visible in receipt* applies to the itemised Receipt / invoice; unchecked by default; the job order always lists operands.
3. M4 — merging happens only when the target condition's tube is in the same visit (existing or in the same request); one level only;
   compatibility is within one sample type and one processing unit (clinical group).
4. M2 — *Open setup* covers test members and a package's member services; the return is one hop (the chip and `back()`).

## 7. Integrator checklist

Migration `ReviewerRound3` (`app.lab_tests.formula_operands jsonb not null default '[]'`, `app.visit_lines.visible_on_receipt boolean not
null default true`, the `VisitLineKind` / `VisitLineOrigin` CHECK constraints, `app.sample_conditions.merges_into_condition_id uuid null`
with the same-tenant FK if the model declares one), `migrations.sql`; navigation entries and shell labels for the new page (client Setup,
admin master data, `catalogue.view`, Release 1); `CLAUDE.md` §5 entry; SRS (CP-40, CP-41, CP-46, CP-06, CP-12, CP-23, the new page, BR-02,
WF-02) + PDF; UAT scripts; README *Unreleased*; all gates per suite; browser check; release.
