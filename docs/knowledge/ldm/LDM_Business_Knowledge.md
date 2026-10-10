# LDM — Business & Feature Knowledge Base

**Source:** `LDM_Manual.html` (LDM System Guide — Engineer Master Edition, v1.9, 2026-09-13, National Technology).
Full extracted text: [`LDM_Manual.en.md`](LDM_Manual.en.md) (English) · [`LDM_Manual.ar.md`](LDM_Manual.ar.md) (Arabic) · screenshots in [`img/`](img/).
Section references below (e.g. §8.9) point into the manual's chapter numbering.

**What LDM is.** `Laboratory Data Manager` by National Technology — a laboratory information system running the
lab from patient reception to result delivery. Documented versions: **9.2.0** (training build) and **9.3.0**
("Al-Barq Lab" build, chapters 10–11). Platform: **Oracle Forms** desktop client over an Oracle database
(services `OracleServiceLDM`), Oracle Reports 6.0 for printing. A newer **web edition ("Web LDM")** exists or is
planned (it swaps review/verify order, centralises analyser services). The manual was built from 13 training
recordings; 4 installation/database/branch sessions are out of scope; 73 items remain unconfirmed (ch. 21).

---

## 1. Actors and roles

LDM ships **no preset roles**; each lab builds its own privilege groups (ch. 12). The manual's personas:

| Persona | What they do in LDM |
|---|---|
| Reception clerk | Register patients/visits, choose tests, collect payment, print labels/job orders/receipts, print verified reports, close the cash drawer |
| Phlebotomist / courier | Draw samples (`Done By`), home visits (`Home Reservation`), bring samples from contracted labs (`Representative`) |
| Chemist / unit technician | Receive samples, enter results (manual or from analyser), review, run worklists, reject samples |
| Unit head / responsible doctor | Verify (e-sign) results, investigate delta checks, release abnormal results |
| Microbiologist / pathologist | Culture entry (organism, antibiotics, sensitivity); free-text reports with templates |
| Accounts / cash-desk officer | Price lists, contracts, refunds, transfers, daily closing, monthly claims to payers |
| Application engineer | Builds the test catalogue, privileges, analysers, auto-verification rules |
| Integration / support engineer | Analyser & PCR interface services, config, logs |
| Lab manager / management | Reports: income, doctors, contracts, discounts, cancellations, TAT |

---

## 2. Core domain model

### 2.1 The three identifiers (§3.1)
| Identifier | Level | Behaviour |
|---|---|---|
| `Patient No.` | Patient file | Auto-generated; **never changes**; all visits, payments, results hang off it. Manual entry (national ID/phone) is possible but produces duplicate files — avoid |
| `Acc. No.` (accession) | Visit / order | Sequential, new per visit; holds tests, samples, bill |
| `Sample ID` | Tube | Unique, never reused; printed as barcode (Code 128). Length varies by version/setting (8/10/11 digits) |

Hierarchy: patient → visits → tubes per visit → tests per tube. Other keys: `Booking No.` (home reservation),
`Batch No.` (batch / lab-to-lab registration), `Invoice No.`, `Print List No.`, `Internal Code` (e.g. `C22/2846`).

### 2.2 Tube generation rule (§3.2)
Tests in the **same unit (group)** on the **same sample type + condition** merge into one tube. Different unit
⇒ separate tubes even with same sample type; different sample type/condition ⇒ separate tubes within a unit.
Entirely driven by test setup, never by the clerk. Label content: unit code letter (`C` chemistry, `H`, `R`, `I`, `M`
inferred), date/time, location, sample type & condition, accession, patient name, sample ID, test names.
Per-test `No. of labels` controls copies.

### 2.3 Two status tracks and a gate (§3.3)
**Sample (physical) track:** `Ordered` → `Collected` (barcode scanned at phlebotomy) → `Received` (scanned at unit
workstation). Exceptional: `Reserved` (deliberate hold set by clerk — condition not met, e.g. not fasting), `Rejected`
(unfit sample, with reason).
**Test (medical) track:** `Ordered` → `Complete` (result entered & saved) → `Review` (first-pass) → `Verification`
(doctor sign-off = e-signature) → `Printed`.
**Gate:** no result can be entered until the sample is `Received`.
**Auto Receive / Auto Collect:** a setting that marks samples `Received` immediately on save (labs that don't scan);
then `Received` is only permission to enter results, not proof of arrival.
**Profile rule:** a profile/mega does not advance to `Complete`/`Review`/`Verification` until **every** member test has.
Test status is independent of sample status per row; one grid can mix `Received`/`Ordered` rows.

### 2.4 Pricing: contract and tier (§3.4, ch. 10)
`Rank` = the contract/payer body (cash default `Normal`; syndicates, companies, insurers, Lab-to-Lab).
`Relative` = a tier under the contract (employees, families, Cash…). Together they select a price list; each test
shows `Patient` share (cash now) and `Insurance` share (billed to payer later). Wrong Rank/Relative = wrong invoice.

### 2.5 Patient condition → reference range (§3.5)
`Patient Condition` (pregnant, diabetic, smoker…) chosen at registration selects the reference range used to
flag results; ranges also vary by sex and age. Not fixable after results appear.

### 2.6 UI conventions that encode business rules (§3.6–3.9)
- Field label colours: **red** mandatory; **orange** has a list of values (F9); **blue** requirement configurable
  per lab in `System Parameter`; black optional.
- Two search modes, mutually exclusive per screen: Oracle Forms query mode (F7 enter query / F8 execute; results
  newest first) vs. explicit `Search`/`New Search` buttons (grid results, `R` opens a row). `%` wildcard in every
  text/search/LOV field. A `%` typed in entry mode is saved as data.
- "Record" unit differs per screen: `Results By Accession No.` = one printable report (tests grouped by clinical
  group; each mega profile is its own report); `Results By Sample Id` = one tube; `Search By Patient Data` = one
  report matching criteria; `Worklist` = one test.
- Every screen: add (F6 new record → fill → F10) vs edit (F7 → criteria → F8 → change → F10). F4 duplicates the
  previous record. Hand-typing into a LOV field saves text without forming the link (silent corruption).
- Keys: F10 save, F9 LOV, F7/F8 query, F6 new, F4 duplicate, Enter validate. No save button. Closing with unsaved
  changes prompts `Do you want to save the changes?`.

### 2.7 Main menu (16 modules, §2)
Verbatim, left to right (img-002): `Setup` · `Home Reservation` · `Registration` · `Batch Registration` ·
`Lab. Registration` · `Results` · `Financial` · `Tools` · `Search` · `Worklist` · `Sample Storage` · `Auto Verification` ·
`Quality Control` · `Sms` · `Online Transfer` · `Help`. Documented in depth: registration family, results/search,
worklist, financial, setup (tests, admin, communication, verification). Undocumented: Tools, Sample Storage,
Quality Control, Sms, Online Transfer, Help, Outsourcing (under Financial), installation/branches.

---

## 3. Catalogue (test setup) — ch. 11

### 3.1 Seven catalogue item types
| # | Type | Meaning | Screen |
|---|---|---|---|
| 1 | `Test` | Single result (numeric, non-numeric, or formula) | `Tests` |
| 2 | `Profile` | Set of tests **from one group** ordered/printed together (Lipid profile) | `Profiles` |
| 3 | `Mega profile` | Set of **profiles only** (CBC = totals + absolute + relative); **each mega prints as its own report** | `Mega Profiles` |
| 4 | `Culture` | Organisms + antibiotics + sensitivity | `Setup ▸ Culture` branch |
| 5 | `Text` | Free-text paragraph report (histology, cytology) with templates | `Text Tests`, `Text Test Templates` |
| 6 | `Panel` | Cross-group bundle at **one package price**; no order/sum/formula | `Panels` |
| 7 | `Additional service` | Chargeable non-test (SMS, internet results, delivery, home visit, urgency) | `Additional Services` group |

Build order: units & samples (`General Data`) → groups (`Groups`) → tests → profiles → megas → panels, rules, shortcuts.

### 3.2 `Tests` screen fields (§11.2)
`Group Name` (decides print template & order-grid placement) · `Test Name` (unique internal) · `Report Name` (patient-facing;
may repeat) · `Arabic Name` · `Unit` (from `Results Units`) · `Sample` + `Sample Condition` (LOV; condition belongs to sample) ·
`Sex` (Male/Female/Both — blocks ordering for wrong sex) · `Visible` (orderable at reception) · `Visible in job order` ·
`Biohazard` · `Last Result` · `Print Ref From` (Test Ranges / Age Comments / All) · `Header` (sub-heading banner) ·
`Test Methodology` · `Print Report With QR Code` · `Code` · `Test Type` (`Age Dependant` numeric / `Non_numeric Test` /
`Formula`) · buttons `Test Ranges`, `Test Conditions`, `Enter Formula`, `Test Comments`, `Test methods`,
`Panic and reportable range`, `Fonts` (per-element font for name/result/unit).
Convention: a test whose name starts with `.` is never printed (helper tests feeding formulas/sums).

### 3.3 Reference ranges & result semantics
- **Numeric (`Test Ranges`)**: one row per age band (`From`/`To`), by sex; out-of-range shaded (pink high, orange low) and marked on report.
- **Non-numeric (`Test Conditions`)**: allowed values each with `Normal` (not flagged) and `Exact` (default value on order).
  Best practice: first value `.`/blank with Normal+Exact so default is empty; never default to `Negative`.
  A `.` is a valid saved value that does not print.
- **Formula**: computed from other tests via `Formula Setup`; cannot be typed into; adds no charge.
- **Analyzer-specific ranges** may override (ch. 13); priority: analyser range → test setup range.

### 3.4 `Panic and reportable range` dialog — the test's "brain" (§11.7)
`Result Format` (decimals) · `Low/High Reportable` (print as "<"/">" outside) · `Low/High Panic` (critical alarm, call doctor) ·
`Duration in Days/Hours` (turnaround → delivery date; **patient TAT** = registration→verification) · `Min Duration`
(**lab TAT** = receipt→verification) · `Frequency Check Days/Hours` (min gap between repeat orders; Alert or Prevent;
reportedly not enforced across branches) · `Delta Check %` + `Delta Check Days` · `Test Days` (weekdays test runs;
unticked days don't count toward duration) · `No. of labels` · `Prefix` (label/report prefix e.g. `S.`).

### 3.5 Profiles & megas (§11.8–11.10)
Profile: `Group` (fills candidate tests; one group only), member `Order`, `Sum` tick + `Profile Sum` (binding total,
e.g. differential = 100; save refused otherwise), per-row `Visible`, header `Visible` (orderable) vs `Visible In Rep`
(prints as heading), fonts. Mega: profiles only, `Order`, `Report Type` template (`new_test` for test/profile, `new_mega`,
`New_Mega_culture`, text template). Single tests inherit the **group's** template (`Setup ▸ Groups`). To force a
single test onto its own report sheet: mega → profile → test.

### 3.6 Cultures (§11.11)
`Setup ▸ Culture`: `Antibiotics` · `Culture Antibiotics` · `Culture Organisms` · `Culture Tests` · `Organisms` ·
`Organisms Antibiotics` · `Sensitivity`. A culture mega = ordinary mega + a profile containing one test of type `Culture`
(convention `<sample> Swab Link`), which makes the `Culture Entry` button appear on results. Reference shape:
`Urine Examination and Culture` = profile 1 (physical/chemical exam items) + profile 2 (culture test), template
`New Mega Culture`. Negative (`No Growth`) prints without a table; positive prints antibiotic sensitivity table.
`Culture Tests` fields include durations, labels, sample/condition, `Culture Days`, `Stability`, `Precautions`, `Volume`.

### 3.7 Text tests (§11.12)
Paragraph grid (`S. No.`, `Header`, font/style) defines report structure; result screen offers free text, per-paragraph
bodies, `Load Template` (multi-select checkboxes), `Spell checker`, `Print`, `Print logo`, `Preview`, `Result Attachment`,
and a `Results Sound Recorder` (voice note attached to result).

### 3.8 Panels, rules, shortcuts, codes (§11.13–11.16)
- **Panel**: cross-group package, single price (e.g. CBC+LFT+ALT = 170 separately, 50 as panel); has `Code`; appears as
  pseudo-group `Panels` on `Patient Tests`; ordered/edited as one block.
- **Test Rules** (auto-add): `{A}+{B} >> {C}+{D}` — ordering all triggers together auto-adds linked tests, each at own
  price. `Check Rule` validates. Not for pricing (panel) nor computing (formula).
- **Tests Shortcuts**: named typing shortcut (e.g. `ICU`) that drops a set of tests at their own prices under their own
  groups; freely editable after ordering. Shortcut numbers per test (e.g. AST=1, CBC=100) also supported.
- **Unique Services Codes**: unified internal code per service + `LOINC` + `CPT` for HIS/insurer integration; also the
  **gate** for auto-verification rules (a test absent here cannot have a rule).
- `Diagnosis` screen: `IDC Code`, `Diagnosis Name`, tests grid (diagnosis-linked packages). `Checkup`/`Checkup Days`:
  recurring order every N days.

---

## 4. Registration module — ch. 4–7

### 4.1 Four entry screens (§4.1)
| Screen | Use | Distinctive |
|---|---|---|
| `Registration` | One walk-in patient | Full fields, similar-name check, `Patient Tests`, `Scan Attach`, `Sampling Details` |
| `Batch Registration` | Group sharing the same tests (company staff) | Contract/location/doctor once at top; one row per person; tests chosen once; one `Batch No.`; **no similar-name warning** |
| `Lab. Registration` (`Lab To Lab Batch Registration`) | Samples from a contracted sending lab | Pick `Lab` → courier (`Representative`), contract, phone, doctor auto-fill; tests via `Shortcut` on same screen; `Sample Collected Inside the Lab` responsibility tick |
| `Home Reservation` | Booking a home draw (not a registration) | `Address` mandatory; `Booking Date` = visit time; `User Name` assigns phlebotomist; yields `Booking No.`; preliminary labels/job order in standard build |

### 4.2 Patient/visit fields (§4.2)
`Patient No.` (auto) · `Patient Condition` · `Title` (fills `Sex`; English & Arabic titles) · `Patient Name` (1–3 parts; Enter
triggers duplicate check) · `Birth Date` ↔ age `D M Y` (either direction) · `Sex` (mandatory) · `Location` (Out Patient
default; hospital departments) · `Room No.` · `Treatment No.` (hospital file / insurance number) · `Rank` · `Relative` ·
`Tel. No.` (phone search is the fallback identity key) · `Doctor` (referring; drives doctor statistics/share) ·
`Nationality` · `Governorate`/`City`/`Area` · `Address` · `Email` (result delivery) · `Comments` (reception note, shown to
chemist as `CR`) · flags `V.I.P` / `Urgent` / `Biohazard` · `Acc. No.` (on save) · `Booking No.` · `Patients Count`
(daily counter) · `Diagnosis`/`Diagnosis Tests` · `Checkup`/`Checkup Days` · `Reg. Date`. Screens are customised per client.

### 4.3 Duplicate-patient protection (§4.3)
1. `similar name exist` warning on Enter in name (setup option; absent in Batch Registration): list shows patient no. +
   phone; compare name **and** phone; select → all data reloads under same `Patient No.`; or `New Patient`.
2. F7/F8 search by patient no., accession, name, phone. F7+F8 empty = last registered visit.
Two files for one person cannot be merged by the clerk.

### 4.4 Choosing tests — `Patient Tests` (§4.4–4.6)
Regions: `Groups` (units; shaded when a test chosen), `Tests` (group's tests; precaution tests highlighted), `Selected Tests`
(status, `Patient`/`Insurance` price, `Result Date` expected per test, `Done By`, `Tests Methods`), right panel `Shortcut`,
`Total Required`, `T. Count`, `Discount`, `Disc. Per. %`, buttons `Money`, `Labels`, `Back`, `Tests`.
Three ways to add: browse group; `Shortcut` + F9 + 3 letters (everyday); shortcut number + Enter (fastest).
Precautions (fasting etc.) surface at reception; unmet → set row `Status` = `Reserved` (kept in visit & bill, no tube
issued) and later back to `Ordered`. Remove test = double-click (separate right; time limit unconfirmed).
Discount by `Disc. Per.` or `Discount` amount (mutually computed; per-user cap e.g. 10%; discount reason captured;
`Discounts` report). Contract `Alert` conditions pop up for flagged tests.

### 4.5 Save chain (§4.7)
F10 → accession + sample IDs created → `Money`/`Labels`/`Sampling Details` enabled → auto-print per `System Parameter`:
barcode labels on printer named "barcode" (copies configurable) + job order (`Sampling Details` printout: patient,
visit, balance, per-test unit/result time/sample/barcode, draw-time & drawn-by boxes; says "This is the visit No. N")
→ `Payment` opens automatically for cash patients (not for contract patients, or shows nothing due).

### 4.6 Payment (§4.8, ch. 16 scen. 7)
Header: `Total Required`, `Discount`, `Paid`, `Remaining`. Grid: one row per payment: `Date`, `Paid` (mandatory), `Visa`
tick + `Transaction No.`, `Print`, `E` (unexplained), `Recovery` (unexplained). Full or partial payments; balance stays on
visit. Receipt auto-prints (user's language). Buttons `Print`, `Print All`, `Print Invoice`, `Print Barcode`, `Back`.
Seeing the screen is a right. `Batch Payment`: by `Batch No.`, rows per member (`Patient Fee`, `Insurance Fee`,
`Discount`, `Paid`, `Recovery`, `Remainig`, `Pay`), `Pay All` (rarely used; didn't work in training).

### 4.7 Samples screen (§4.9) and retrieval (§4.10–4.11)
`Sampling Details` → `Samples`: per tube `Unit`, `Sample Type`, `Status`, `Status Date`, `Sample ID`, `Labels` copies, `B`
(print this label); `Sample Tests` lists tube's tests with type (Test/Profile); `Print` = job order.
Retrieve visit (F7 acc/patient/name/phone F8) to add tests (new tubes' labels print), settle balance (`Money`), reactivate
`Reserved`, reprint. `Scan Attach`: attach image/PDF to patient file (stored in DB; **does not replicate between
branches** by default). Unconfirmed: editing patient demographics after save, cancelling a whole visit from here.

### 4.8 Home reservation → registration (ch. 5)
Book with time, phlebotomist (`User Name`), tests; `Booking No.`; later `Registration` → `Booking No.` + Enter pulls
everything; `Done By` = phlebotomist for home-visit share. `Cancelled` box, `Visit Date`/`Last Visit Date` unconfirmed.

### 4.9 Lab-to-lab (ch. 7)
Sending labs defined in `Setup ▸ General Data ▸ Lab` with contract and courier. `Representative` reports: labs served,
samples brought per period, share. `Sample Collected Inside the Lab` unticked ⇒ job order prints `Sample Collected
Outside` (draw quality is sender's responsibility). Claims to a sending lab via `Invoice` ▸ `Lab`.

---

## 5. Results, review, sign-off, printing — ch. 8

### 5.1 Screens
`Results ▸ Results By Accession No.` (opens in query mode; header fields are criteria) · `Results By Sample Id` (tube
view) · `Reject Sample` · `Unreject Sample`. `Search ▸ Search By Patient Data` (status-driven work queue) ·
`Search By Test Result` (by value, statistical) · `Search By Birth Date` (last two undocumented).

### 5.2 Results header (§8.2)
Filters: `Branch`, `Unit`, `Group`, `Acc. No.`, `Patient No.`, `Patient Name.`, `Invoice No.`, `B. No.`, `Test Status`,
`Sample Status`, `Not Printed`, `Not Verified`, `Delta Check`, `From Analyzer`, `To Analyzer`, `Test`, `Asc Sort By`.
Patient data (age/sex drive ranges), `Loc. Type`, `Location`, `Room No.`, `Doctor`, `Reg. Date`, `Tel. No.`.
Buttons: `CR` (reception comment; red frame when present) · `Comments` (internal unit note with author/time, never
printed) · `Image` · `Biohazard` · `Controls` (QC) · `Preparations` (registration preparation questions) · `Diagnosis` ·
`Review` · `Verify` · `R`. 9.3.0 adds `Undo Verify`, `Undo Review`, `Print Logo`.

### 5.3 Results grid
`Test Name` (profile name as banner) · `Result` (typeable / LOV / formula-locked; empty shows `.`) · `Unit` · `From`/`To`
(numeric range by sex/age/condition) · `N.C. Ranges` (text range) · `Analyzer` (source instrument; blank for
manual/formula) · `Titer`/`Methodology` (customisable, e.g. Result 1/Result 2) · `Sample Status` · `Test Status` · `P`
(printed?) · `Printed` · `Delta Check` · `C` (control?) · buttons `ATT` (attach PDF to result) · `RL` (Result Log: every
value, date, user, analyser flag; red frame when edited) · `SL` (Status Log: Collected→Received→Ordered→Complete→
Reviewed→Verified→Printed with user/time). Below: `Comment` + `Insert Comment` (unit comment library with `%` find),
`Print`, `Preview`, `Last Result` (patient's previous results; marker when exist). Colour key: High, Low, High Panic,
Low Panic.

### 5.4 Three comment kinds (§8.4)
| Where | Author | Printed |
|---|---|---|
| `Comment` + `Insert Comment` | Chemist about result (from per-unit template library) | **Yes** on patient report |
| `CR` | Reception at registration | No |
| `Comments` (header) | Any unit member, for colleagues | No (author + time stored) |
Auto Verification can insert comments by rule.

### 5.5 Result entry & workflow (§8.3, §8.5)
Entry allowed only when sample `Received` and test `Ordered` (not `Reserved`). Manual entry: type / F9 pick / formula
auto. Colours cue out-of-range. Save F10 → `Complete`. Analyser results land as `Complete`. `Review` → `Reviewed`
(name/time in SL); `Verify` → `Verified` (enables printing). Verify may skip review; cannot verify a test with no result.
Rights `R`/`V` per medical group (ch. 12). Web edition reverses review/verify order.

### 5.6 Delta check (§8.7)
Per-test % and window; flag ticks row's `Delta Check` (warning, doesn't block). Investigation order: instrument QC →
patient history/clinical reason (`CR`, ask) → suspect sample (swap/clot) → redraw before verifying. Header filter
`☑ Delta Check` lists all flagged.

### 5.7 Preview & print (§8.8, §8.12)
`Preview` refuses with `There are other results have no value` while any test lacks a result (complete it or set
`Reserved`). **Unverified results do not print** (unless a non-standard setup option / `Preview_Without_Verify` right).
Printing ticks `Printed`, logs `Printed` in SL. Print list (right panel of Search): `Add Acc. No.`, double-click rows or
`Add All to Print List`, `Clear All`; first print assigns `Print List No.` echoed on report. `Abbreviated Report` =
multi-patient worksheet/file copy ("Copy for file use only") with empty result boxes for bench entry — not the patient report.

### 5.8 `Search By Patient Data` — the work queue (§8.9–8.11)
Defaults: own branch, today 00:00→tomorrow 00:00 (registration date), `All`, `☑ Received`. Filters: `Unit`, `Group`,
`Location`, `Name`, `Doctor`, `Test`, `Acc. No.`/`S ID` ranges, `From Analyzer`, `To Analyzer`, `Normal`/`Abnormal`,
`In Patient`/`Out Patient`, `Manual`, `Sort By`, `Keep Previous Results` (accumulate). Status boxes: `Complete`/
`Reviewed`/`Verified`/`Printed` = exactly that status; **`Not X` = everything below X** (`Not Verified` = Ordered +
Complete + Reviewed; `Not Reviewed` = Ordered + Complete; `Not Complete` = Ordered only; with `NC` sub-options
`All N.C.` / `Mixed`); `All Verified`/`All Reviewed` = visits where every test is. `Count` = number of reports; `Status`
sentence describes selected row; `R` opens Results. Nine row colours (per-installation): Incomplete&Printed (dark
blue) · Incomplete&Not Printed (light pink) · Completed&Printed (purple) · Completed&Not Printed (red) · Verified&Printed
(green) · Verified&Not Printed (black) · Reviewed&Printed (pink) · Reviewed&Not Printed (light green) · Reserved&Not
Printed (yellow). `Type` column `T` (single test, with `Internal Code`) / `M` (package). Bulk `Review`/`Verify` buttons
act on the listed results (safe pattern: Complete + Normal + analyser → review all; Abnormal → one by one).
Closing an analyser's shift: `Complete` + `From Analyzer` = X (what it produced, to review) then `Not Complete` +
`To Analyzer` = X (what it should have produced and didn't).

### 5.9 Sample rejection (§9.4–9.5)
`Reject Sample` by `Sample ID` (query mode) → `Reason` LOV (reasons defined in setup, e.g. `Hemolysis`) → F10 → confirm.
Sample becomes `Rejected`; tests take no results. Reinstating: road 1 `Unreject Sample` (same barcode on new tube; back to
`Received`); road 2 (ISO-preferred) keep rejected, issue a new sample/label from `Sampling Details`.

---

## 6. Worklist and TAT monitoring — ch. 9

One record = one test. Filter by **collection date** (Search uses registration date). Filters: `Unit`, `Group`, `Test`,
`Analyzer`, `Location`, `Patient No.`, `Acc. No.`/`Sample ID`/`Int. Code` ranges, `Sample Status`, `Rank`/`Relative`,
`Batch No.`, `Doctor`, `Grouping`, `Sort By`, `Auto`. Boxes: `New Tests`, `Remainig`, `Completed`, `Manual` (not on an
analyser), `Discounts`, `Not Verified`, `Not Printed`, `Incomplete`, `Printed`, `Delayed`. Counters `Req` (visits), `Tests`,
`Samples`. Columns incl. `Collection Date`, `Result Date` (expected from `Duration` + `Test Days`), `Delay` (e.g. `2D 12H
38Min`), `Printed`, `S. T.`. Row colours = **percentage of duration consumed** (e.g. 70/80/90% thresholds, per client) —
early warning before lateness. `Print` = paper worklist. `S. T.` (Sample Track) dialog: locations/status/time per stage,
time between stages, and `Test Round Time` (complete/reviewed/verified/first & last print dates).
Three history views: S.T. (where/when), SL (who changed status), RL (who changed value).

---

## 7. Financial module — ch. 10

LDM is **not an accounting system**: pricing, collection/refund/transfer per visit, income & claim reports; large sites
feed an ERP. Menu (9.3.0): `Main Price List` · `Price Lists` · `Client Price List` · `Client Copy` · `Tests Costs` ·
`Test Fees` (legacy) · `panel_price` (custom) · `Query` · `Payment` · `Batch Payment` · `Return Money` · `Money Transfer` ·
`Delete Patient` · `Income` · `Patient Income` · `Patient Total` · `Doctor Income` · `Representative Income` ·
`Additional Services Report` · `Invoice` · `Reports ▸` (12) · `Outsourcing ▸` (separate module, undocumented).

### 7.1 Three-layer pricing (§10.2–10.6)
1. **Base list** `Main Price List`: named yearly lists (`Code`, `Price List Name`); per test `Price`, `Active` (inactive =
   invisible at registration); `Apply` propagates to derived contract lists; `Copy price list` creates next year's list by
   formula (× factor or + amount) with same Active flags.
2. **Contract & tiers** `Price Lists` (window `Rank Fees`): `Rank` (`Code`, `Rank Name`, `Web User Name`/`Web Password`,
   `Web Results` portal flag, `Rank Pass`) and `Relative` tiers (`Code`, `Relative Name`, `Type`, own web login). Tier
   price list columns: `Patient`, `Insurance`, `Alert` (pop contract conditions at ordering; text via `Conditions`),
   `Exclude` (manual negotiated price immune to Apply), `Restore from main price list`.
3. **Formula** `Client Price List`: per Rank/Relative up to three (base list, `Operator` ×, `Value`) pairs for patient and
   for insurance (`First/Second/Third patient/insurance price list`), e.g. ×1/×0 cash, ×0.5/×0.5, ×0.9/×0; second/third lists
   as supplements; `Apply` computes and stamps `Apply Date`; `Money Format`; `Print` = yearly review report. `Client Copy`
   copies one tier's list to another with a factor.

### 7.2 Visit-level transactions
- `Query` (§10.7): price quote without registering — contract + tests → cash/insurance shares, result date,
  `Preparation`, `All Preparation`, `Print`.
- `Payment` / `Batch Payment` (§4.8).
- `Return Money` (§10.8): refund when balance negative (cancelled test, contract transfer); cash or Visa; auto-prints refund
  receipt for patient signature; shows in daily report `استردادات`.
- `Money Transfer` (§10.9): tab `Rank Transfer` moves a visit to another contract and re-prices (excess → negative → refund);
  tab `Normal Transfer` shifts a single test's value between patient and insurance or changes its actual charge.
  Logged (`Pateint Transfer` report).
- `Delete Patient` (§10.10): cancels a visit or individual tests with `Cancellation Reason`; nothing physically erased;
  `Log` per row; `Canceled Patients Or Tests` report.
- Sensitive rights: refund/transfer/cancel narrowly granted; one login per person for accountability.

### 7.3 Reports & closing (§10.11–10.14)
`Income` panel (branch, user, period): `Patients' Income`, `Total Daily Income` (cash-desk daily "يومية خزينة استقبال
مجمعة": per payment rows; summary payments − refunds = `الرصيد الفعلي` actual drawer cash; Visa separate; balances;
discounts), `Payment Methods` and `Contract Type` × branch/group/unit, `Monthly Statistics` (rank × month), `Patient
Totals` (financial statement per visit/patient). `Doctor Income` (per referring doctor, `Special Customer` share %
from doctor setup). `Reports ▸`: `Print Client Fees` (+`With Sample`) hand-out price list · `Clients Income` ·
`Test Income Analysis` (per test × rank: count, cash, credit) · `Income Analysis` / `Ranks Monthly Statistics` / `Rank
Periodic Curve` (charts; broken on 64-bit) · `Remaining` (debtors; `Remaining Date` = balances as of a date) · `Pateint
Transfer` · `Canceled Patients Or Tests` · `Discounts` (who, how much, reason) · `Total Income Per Day`.
`Invoice` (§10.13): monthly claim to a credit contract: branch, `Lab` (lab-to-lab), rank, optional relative, period (by
registration date), `Order By`, `Excel`, or selected accessions; layouts `Invoice`, `Invoice with Relative`, `Invoice With
Results`, `Invoice With Totals`, `Credit`, `Invoice With No. of tests Counts`; page & report totals, prepare/review/approve
signature boxes; layout customised per payer. `Tests Costs`: cost per test for profit/loss reports. `Additional Services
Report`, `Representative Income`.

---

## 8. Users, privileges, branches — ch. 12

- Menu `Setup ▸ Adminstiration` (sic): `Groups And Privileges` · `Menu Management` · `Rights` · `Rights Adminstiartion` ·
  `System Setup Tables` · `System Messages` · `System Objects` · `System Parameters` · `System Users` · `Monitor Sessions`.
- **Groups first, then users** (users inherit). `Groups And Privileges`: `Group Name`, `Description`, `Visible` (retire
  without deleting); four tabs:
  1. `Privileges and rights`: per screen/menu object `Q` (query/open) `I` (insert) `U` (update) `D` (delete) `All`;
     `Special Rights` (`Enabled`): `Add_Tests`, `Change Password`, `Change Username`, `Delete Test Any Day`, `Delete and
     Active verified test`, `Discount`, `Expire User Password`, `Listen Test Result`, `Modify Verified Result`, `Modify Vip`,
     `Payment`, `Preview_Without_Verify`, `Print All Bills`, `Print Invoice`, `Print Vip`, plus `View Verified Result`,
     `View Reviewed Result`, `Vip`.
  2. `Groups and Branches`: per **medical group** and per **branch**, result rights `S` see · `R` review · `V` verify ·
     `E` edit · `P` print (results only). Medical groups seen: ALLERGY, Body Fluids, BONE MARROW, CHEMISTRY, COAGULATION,
     CYTOGENAT, DRUG ABUSE, HAEMATOLOGY, HORMONES, IMMUNOLOGY, MICROBIOLOGY, PATHOLOGY, SEROLOGY, TUMOR MARKERS…
  3. `Locations`: which locations the group may work on (In Patient, Out Patient, dialysis…).
  4. `Print Results For Ranks`: which ranks' results the group may print (military/government hospitals).
- `System Users`: `Real name`, `User name`, `Password`/`Confirm`, `Group` (inherit), `Locked`/`UnLocked`, `Password Expire`
  + `Expire Date`, `Period` (likely password-change interval), `Change Password` self-service, `Visible`, `Shifts`, `Modified`
  flag + `Restore` (revert to group), signature image `Browse` (JPG; printed on reports by template = `Automatic Signature`).
- Propagation: group change affects all members instantly; user-level change affects only that user.
- Multi-branch: setup done on the **main branch**; `Refresh Setup` pushes to sub-branches. Attachments don't replicate.

---

## 9. Analyser integration — ch. 13

- **Modes** (decided by the machine): `Uni-directional` (machine → LIS; scan sample at machine, fixed test; most CBC
  analysers) · `Bi-directional` (machine queries orders by Sample ID; `Query` ticked) · `Download` (LIS pushes orders at
  registration to `Download` table; `Load` ticked) · `Shared folder` (machine exports XML/Excel/CSV to watched folder;
  most PCR). **Links**: Serial (`<baud>,n,8,1`, COM port), TCP/IP (service is Client or Server, opposite of machine; HL7),
  file share.
- `Analyzers ▸ General`: `Code`, `Name`, `Default Sample`, `Default Type` (Test/Profile/Mega), `Default Test Name`,
  `Copies`, `Auto Print` (print automatically after auto-verify), `Load`, `Append UnReg Tests` (auto-register tests run but
  not ordered), `Append UnReg Patients` (auto-register unknown samples; audit of bypassed registrations), `Update R
  Result` / `Update V Result` (may re-runs overwrite reviewed/verified results), `Conf` (unknown). `Comm` tab: `Comm
  Setting`, `Comm Port`, `Form Name` (legacy Oracle Forms 6i integration), `Text File In/Out`, `Query Location`, `Result
  Location`, `Flags` popup (`Flag Code`, `Flag Name`, stop/no-stop tick).
- `Analyzers Tests` mapping: `Group`, `Test Type`, `Test Name`, `Visible`, `Analyzer Code` (machine's code), `Host Code`
  (code expected in queries), `R.S` (result selection index when several values per message), `Slope` (unit conversion
  multiplier), `Intercept`, `Cut Off`, `Unit`, `Query`, `Query Details` + `Details…` (sub-value codes, e.g. PT → INR,
  control time, concentration), **`Auto Send` (permission to save incoming results — without it nothing lands)**,
  `Reference Range…` (machine-specific range, numeric/non-numeric; overrides test range), `Methodology…`, `Result…`
  (operator/value/return table converting numbers to text, e.g. `< 10` → `Negative`). `Print Analyzer`/`Print All`.
  Codes change with kit/lot — customer should maintain.
- `Analyzer Samples`: sample type → machine sample code (some machines refuse without it). `Analyzer Culture Tests`:
  tabs Culture/Antibiotics/Organisms/Sensitivity (`Analyzer Code`, `Analyzer Code2`), e.g. bioMérieux Vitek.
- **Service**: one Windows service per machine (`sc create`, Automatic Delayed Start); config keys `ConnectionString`
  (encrypted with `Encryptor`/`Decryptor`), `AnalyzerName`/`AnalyzerId` (must match screen), `CommunicationType` (0 serial,
  1 TcpClient, 2 TcpServer), `CommunicationSettings`/`CommunicationPort`, `TcpIp`/`TcpPort`, `MessageRate`, `LogFilePath`
  (+ `backup`/`result`/`failed` folders). Encrypted hourly logs + `__Error_` logs; `Analyzer Decryptor` tool. Happy path:
  `InitializeDevicePortSettingAndLog | Success`, `StartSend: SaveResult [SID, TCode, R, Flag]` then `Passed Check`.
  `Oracle Analyzer Installer` GUI exists but discouraged. Direction: single central service in Web LDM.

---

## 10. PCR integration — ch. 14

PCR machines emit a **Ct value per gene per well**, not a result; the service computes the verdict. Two inputs: the
machine's **run file** (layout: sheets, columns, well order, serial number) and the kit's **interpretation sheet**
(per-gene thresholds + combination table). Example BioRad CFX + SOLGENT COVID kit: dyes VIC/CY5/FAM/Cal Red ↔ genes
ORF1a/RdRp/N/IC; Ct ≤ 38 positive, 38–40 borderline (PP), 0/N/A/>40 negative (IC ≤ 40 positive). Verdict: ≥2 target
genes + ⇒ `DETECTED`; one gene or any borderline ⇒ `Presumptive positive`; none + with IC ⇒ `NOT DETECTED`; all
four negative incl. IC ⇒ `INVALID`. QuantStudio 5 + NeoPlex differs in sheet layout, column names (`CT`, `Undetermined`,
`Amp Status`), three genes. **One service per machine + kit** (`<machine>_<kit>`), config adds `Parameter` (instrument
serial lock), `ResultFolder` (shared in-tray), `BackupFolder` (read OK), `FailedFolder` (rejected), `AnalyzerType`. Log
`Flag` field carries `<Well>-<Gene>-<Ct>` per gene + run-file name (audit trail). Known limitation: one export path per
machine when several kits are run.

---

## 11. Auto verification — ch. 15

Goal: release normal results without a doctor's click (1000–1500 samples/shift → doctor reviews 200–300).
**Process:** managerial half first (collect rules per group from unit heads, medical language → technical; roll out group
by group), then technical. Gate: `Unique Services Codes` must contain the test. `Setup ▸ Verification Setup` (`Verify
Setting`): `General Settings` → `Enable Online Verification` master switch (keep off until rules verified), `LMP
Preparation No.`; `Units Settings` → per group tick checkers: `Medical check` (core: apply `Medical Rules`), `No Rules`
(test without rule does not pass), `Result check`, `Comment check` (vs `Result Dictionary`), `Option check` (vs `Result
Option`), `Flag Check` (any analyser flag stops unless flag registered non-stopping), `Dev Flag Check`, `Incomplete check`,
`Panic check` (+reportable), `Normal check` (**leave off**: overrides rule widening), `Delta check` (% over days from test
setup), `All Group Complete` (engine runs only when whole group complete), `Delta check Analyzer` (variance between two
analysers), `Main Rules check`, `Missing Result check`, `Quality Control check`, `Online Verification`, `Reportable check`.
`Result Option` (allowed choice values per parameter) · `Result Dictionary` (valid comment words; case-sensitive; `Not Valid`).
**`Medical Rules` language** (per group/test; parameters in braces): operators `* / + -`, `> < = <>`, `And`/`Or`/`Not`,
`Norm`/`High`/`Low` (vs reference range), `H-Val`/`L-Val` (live upper/lower range → percentages as multiplication, e.g.
`{Albumin} > L-Val*0.8 and {Albumin} < H-Val*1.2`), `sex=1/2`, `age` (days; `365*18`), `dev` (analyser code), `Doc`,
`Prep<n>` (registration question answered yes), `Require` (companion test ordered), `LR`/`LR period`/`No LR`/`N.D.C` (last
result & delta; `No LR` prevents stopping new patients), `Comment … with … like #text#` (auto comment), `Round`, plus
unexplained `Abs`, `Lowd`, `LMP`, `Then`, `desc1-3`, `loc`, `^`. Grid columns: `Code`, `Formula`, `Formula Condition`
(new screen: pre-condition; false ⇒ rule ignored — replaces giant Or-chains), `Recommendation`/`Help` (text shown to doctor,
e.g. "Recommended to do TIBC"), `Main Rule`, `Active`. `Check Rule` validates (brackets!). Rule-writing lessons: every
boundary needs its complement (`Or` arm) or patients outside it get stopped; draft in Notepad++; grid shows names, editor
shows codes.

---

## 12. Printing & outputs inventory

Barcode labels (Code 128; per-test copies; prefix) · Job order / `Sampling Details` · Receipts (per payment; language per
user) · Invoice for patient · Refund receipt · Patient report (templates `new_test`, `new_mega`, `New_Mega_culture`, text;
group-level templates; signature image; `Print Logo`; QR code option; printed flag + log) · Print list & `Abbreviated
Report` worksheet · Worklist report · Price list hand-outs · Cash-desk daily · Income/doctor/contract/discount/remaining/
cancellation/transfer reports · Claims (6 invoice layouts, Excel) · Analyser code sheets · Preliminary booking labels/job order
(standard build) · Web results portal per rank/relative (`Web Results` login) · Email/SMS/internet result delivery (as
additional services).

---

## 13. Known weaknesses, quirks and open questions in LDM (useful when designing a successor)

- Oracle Forms desktop UX: key-driven (F6/F7/F8/F9/F10), no save button, modal query mode; LOV fields silently accept
  typed text and break links (needs DB repair); `%` saved as data when in entry mode.
- Typos baked into UI (`Shotcut`, `Remainig`, `Adminstiration`, `Rights Adminstiartion`, `Pateint Transfer`, `Min Duraion`,
  `Auto Recieve`); financial menu "unorganised"; chart reports broken on 64-bit Windows.
- Duplicate patient files: manual patient numbers, `New Patient` haste, no similar-name check in Batch Registration; no merge.
- Attachments do not replicate across branches; setup must be done on main branch and pushed (`Refresh Setup`).
- Auto Receive mode makes `Received` meaningless as a tracking state.
- Review/Verify order debated (web edition reverses). Bulk verify tempts unsafe sign-off.
- Frequency check reportedly not enforced across branches.
- Auto-verification pitfalls: `Normal check` overriding rules; `N.D.C`/`LR` stopping new patients; multiple rules evaluated
  conjunctively on old screen; percentage-as-subtraction bug.
- Analyser: one service per machine (and per kit for PCR), per-branch; plaintext config needing manual encryption; single
  export path limitation for multi-kit PCR; `Auto Send` easily forgotten.
- Many fields/buttons undocumented (`E`, `Recovery`, `Conf`, `P`/`C` columns, `R` header button, `Period`…); 73 open
  confirmation items (manual ch. 21); Tools, Sample Storage, Quality Control, Sms, Outsourcing never covered.

---

## 14. Glossary (compact)

Patient No. (permanent file) · Acc. No. (visit) · Sample ID (tube/barcode) · Booking No. · Batch No. · Rank (contract) ·
Relative (tier) · Patient/Insurance (shares) · Patient Condition (range selector) · Group/Unit (lab section; tube split) ·
Profile/Mega/Panel/Culture/Text/Additional service (catalogue types) · Shortcut (test picker) · Ordered/Collected/Received/
Reserved/Rejected (sample) · Complete/Reviewed/Verified/Printed (test) · Job Order (Sampling Details) · Done By · Representative
(courier) · System Parameter · CR / Comment / Comments (three comment kinds) · RL / SL / S.T. (result log, status log, sample
track) · Delta check · Panic / Reportable · Duration / Min Duration (patient TAT / lab TAT) · Frequency check · Test Days ·
Auto Send · Query / Load / Download · Run file / Interpretation sheet · Checkers / Medical Rules / Formula Condition.
