# Sky LIS prototypes vs LDM — what matches, what is extra, what is missing

Compared: the two Sky LIS prototypes (see [`SkyLIS_Prototypes_Knowledge.md`](SkyLIS_Prototypes_Knowledge.md)) against
the LDM 9.2/9.3 feature set (see [`../ldm/LDM_Business_Knowledge.md`](../ldm/LDM_Business_Knowledge.md)).
"Missing" means present in LDM and absent from both prototypes. Prototypes are UI mock-ups, so a missing item may
simply be out of the prototype's scope; the SRS decides.

Legend: ✅ matching · ➕ extra in Sky LIS · ❌ missing vs LDM · ◐ partial

---

## A. Platform, tenancy, access

| | Item |
|---|---|
| ➕ | Cloud multi-tenant SaaS: Super Admin portal, client lifecycle (Subscription / Manual source, activation, password reset), per-tenant URL |
| ➕ | Self-service subscription wizard with OTP, password policy, forgot-password via OTP |
| ➕ | Global master data maintained centrally and inherited into each tenant's independent copy |
| ➕ | Countries master (dial code, currency, language, timezone, mobile-length validation, flag); governorates/districts |
| ➕ | Wallet balance (prepaid credit) on the tenant; platform dashboards across clients |
| ➕ | Responsive web UI, mobile drawer, Arabic/English with true RTL, Excel export on every grid |
| ➕ | Treasury (cash drawer) **bound to a device**, requested by the user and approved by a manager |
| ✅ | Multi-branch with branch chosen at login (LDM: branches + per-group branch rights) |
| ✅ | Roles-then-users model; role inheritance (LDM: groups then users) |
| ◐ | Privileges: Sky = on/off per **module/screen** (6 modules in client portal); LDM = Q/I/U/D per screen object + ~18 special rights (discount, modify verified result, preview without verify, VIP…) + S/R/V/E/P **per medical group and per branch** + locations + print-for-ranks |
| ❌ | User lock/unlock, password expiry date, change-password right, shifts, "Modified"/Restore-to-group, signature image per user |
| ❌ | System Parameters (lab-configurable mandatory fields, auto-print behaviour), System Messages/Objects, Monitor Sessions, Refresh Setup between branches |

## B. Catalogue / master data

| | Item |
|---|---|
| ✅ | Clinical groups with code and report name (LDM groups also carry the print template) |
| ✅ | Services/tests hierarchy: Test-type service = test or profile; Package = mega/panel; member order; print-alone service = LDM mega-as-own-report |
| ✅ | Test types cover LDM's: numeric, non-numeric, list (Sky splits Optional vs Restricted), formula with syntax check, formatted text, culture |
| ✅ | Reference range by age with normal and critical (panic) limits; list values with Normal and Initial (= LDM Normal/Exact) |
| ✅ | Sample types, units of measurement, gender restriction, report name, header, LOINC |
| ✅ | Antibiotics, organisms (LDM culture setup); preparations questions; comment library; doctors (code, specialty, branches) |
| ➕ | Service aliases; LOINC mandatory on every service/test; list values with colour; antibiotic commercial names + drug types; global service sequence; inclusive-boundary flag; preparation question types (Text / Yes-No / Option List) with per-test linkage and gender |
| ◐ | Reference ranges: Sky shows one age band per type; LDM has multiple age bands × sex, patient-condition ranges, analyser-specific ranges, "Print Ref From" (ranges vs age comments), non-numeric N.C. ranges |
| ❌ | **Sample condition** (fasting, refrigerated…) on a test — the second half of LDM's tube-merge key |
| ❌ | **Turnaround/duration** per test (delivery date, patient TAT, lab TAT / Min Duration), **Test Days**, **Frequency check** (Alert/Prevent repeat orders), **Delta check** % and window, reportable low/high, result decimal format |
| ❌ | Labels per test (No. of labels), label/report prefix, Visible / Visible-in-job-order / Biohazard flags, dot-prefixed hidden helper tests |
| ❌ | Profile features: binding sum (Profile Sum), per-row visibility, "Visible in report" heading control, fonts per element, report templates per group/mega (`new_test`, `new_mega`, `New_Mega_culture`, text) |
| ❌ | Culture link mechanics (culture test inside profile ⇒ culture entry); culture tests' own durations/stability/volume; culture antibiotics/organism/sensitivity matrices |
| ❌ | Text test paragraph structure and **text templates**; Test Rules (auto-add companion tests); Tests Shortcuts (named sets) and shortcut numbers; Panels as single-price bundles (Sky packages exist but pricing of a package vs its members is not modelled); Unique Services Codes with **CPT**; Diagnosis (ICD) ↔ tests; Checkup recurring orders; Patient Conditions master; sending labs (Lab-to-Lab) master; doctor share % |

## C. Registration & patient

| | Item |
|---|---|
| ✅ | Patient file number + visit accession + sample id; registration → services → preparation questions → payment → receipt / job order / barcode labels |
| ✅ | Payer + contract chosen at registration (= Rank/Relative); referring doctor; discount per service (absolute/percent); partial payment with remaining; "sample collected outside" flag |
| ✅ | Search registrations, reopen/edit a visit, patient history (visits), cancel services with reason |
| ✅ | Price query before registering (Query Service) |
| ➕ | ID Number, Insurance ID, email on patient; payment methods incl. POS and currency; registration summary card; cancel-service cascades to samples |
| ◐ | Duplicate-patient protection: Sky has only a "search existing patient" link; LDM has similar-name warning with number+phone comparison and phone search |
| ◐ | Age: Sky derives age from DOB only; LDM is bidirectional (age → birth year) and Title auto-fills Sex |
| ❌ | **Patient Condition** (pregnant/diabetic/smoker) selecting reference ranges |
| ❌ | **Reserved** status (defer a test whose precaution is unmet, keep it in the visit, reactivate later); precaution highlighting at ordering |
| ❌ | Hospital fields: Location (in/out-patient, department), Room No., Treatment No.; Nationality, Governorate/City/Area (Sky has governorate master but not on patient); reception Comments (CR); VIP / Urgent / Biohazard flags; Patients Count; Diagnosis; Checkup |
| ❌ | **Home Reservation** (booking → phlebotomist assignment → convert to registration), **Batch Registration** (company groups, Batch No., batch payment), **Lab-to-Lab registration** (sending lab, courier/representative, batch claims) |
| ❌ | Done By (phlebotomist) per test for home-visit shares; discount cap per user and discount reason; contract **Alert** conditions popping at ordering; Scan Attach (documents on the patient file) |
| ❌ | Label printing detail: per-tube reprint (`B`), copies, barcode printer config, job order content (unit, result time, draw-by signature); automatic Payment screen only for cash patients |

## D. Samples & phlebotomy

| | Item |
|---|---|
| ◐ | Samples dialog per registration (id, type, status, print) — but status is a free "Pending" |
| ❌ | **Tube generation rule** (merge by unit + sample type + condition) — nothing in the prototypes creates tubes |
| ❌ | **Sample status track** Ordered → Collected → Received with barcode scanning at phlebotomy and at the unit; the **"no result before Received" gate**; Auto Receive option |
| ❌ | **Reject Sample** with reason list, **Unreject**, new-barcode policy; Sample Track (where/when, time between stages) |
| ❌ | Sample Storage module (undocumented in LDM too) |

## E. Results, review, sign-off, reporting

| | Item |
|---|---|
| ✅ | Result entry per accession, flags, two-step Review → Verify, previous results, attachments, logs, report view/print |
| ➕ | Accession list beside the editor; H/N flag badges inline |
| ◐ | Status model: Sky rows show Saved/Reviewed/Verified; LDM adds Ordered, Complete, Printed and the independent sample status per row |
| ❌ | **Results By Sample ID** (tube view); opening cases by status, by analyser (From/To Analyzer), Not Printed / Not Verified / Delta filters |
| ❌ | Reference range columns (From/To, N.C. range) shown per row and chosen by sex/age/condition; panic colouring; analyser column; Titer/Methodology columns; formula rows auto-computed and locked; LOV result values |
| ❌ | **Three comment kinds** (printed report comment from the unit library via Insert Comment; reception CR; internal Comments) — Sky has a Comments master but no use in results |
| ❌ | **Delta check** flagging and investigation; Last Result marker |
| ❌ | Print rules: preview refused while results are missing; **only verified prints**; Printed flag and print count in status log; Undo Review/Verify; Preview-without-verify right |
| ❌ | **Search By Patient Data** work queue: status boxes with "Not X = everything below X", All N.C./Mixed, Normal/Abnormal, Keep Previous Results, nine colour states, **bulk Review/Verify**, closing an analyser's shift |
| ❌ | **Print list** with Print List No. and **Abbreviated Report** (bench worksheet / file copy) |
| ❌ | **Culture entry** (organism, antibiotics, sensitivity, culture comment) and culture report layout; **text result entry** with paragraph headings, Load Template, spell checker, sound recorder |
| ❌ | Report assets: signature image per verifying doctor, Print Logo, QR code, per-group templates, result attachments printed |
| ❌ | Web results portal credentials per payer/tier; SMS/email delivery as additional services |

## F. Worklist & turnaround

| | Item |
|---|---|
| ✅ | A worklist grid per service with status and printed flag, print worklist |
| ➕ | Per-row "Exclude" checkbox |
| ❌ | One record per **test**, filtered by **collection date**; counters Req / Tests / Samples; filters by unit, group, test, analyser, location, ranges, contract, batch, doctor; Manual (non-analyser) tests |
| ❌ | **Delay / TAT**: expected result date from duration + test days, delay computed, row colours by % of duration consumed, Delayed filter; Sample Track button |

## G. Financial

| | Item |
|---|---|
| ✅ | Payer/contract pricelists with effective dates; price query; payments with remaining; refund with reason; cancel services with reason |
| ➕ | Effective-from/to on pricelists; payment method POS; treasury device binding and approval; refund eligibility rule |
| ◐ | Pricelist = one price per service. LDM splits every price into **Patient share + Insurance share** per tier |
| ❌ | **Three-layer pricing**: base price lists (yearly), contract tiers derived by formula (×factor / +amount) with Apply, Copy price list, Client Copy, Exclude (negotiated price), Alert conditions, Active flag, Restore from main list, second/third supplementary lists |
| ❌ | **Money Transfer** (move a visit to another contract and re-price; shift a test's value patient↔payer); Batch Payment / Pay All |
| ❌ | **Cash closing**: Income panel, Total Daily Income (payments − refunds = actual drawer; Visa separate), per-user/branch/period |
| ❌ | **Claims to payers (Invoice)** in six layouts, Excel export, prepare/review/approve boxes; claims to sending labs |
| ❌ | Financial reports: Patients' Income, Payment Methods, Contract Type, Monthly Statistics per contract, Patient Totals, Doctor Income with share %, Clients Income, Test Income Analysis, Remaining/debtors as-of date, Discounts (who/why), Cancelled, Transfers, Total per day, Price list hand-outs, Tests Costs (profit/loss), Additional Services report, Representative income |
| ❌ | Additional services as chargeable items (SMS, internet results, delivery, home visit, urgency); Outsourcing module |

## H. Integration & automation

| | Item |
|---|---|
| ➕ | LOINC on every item (LDM only in Unique Services Codes) |
| ❌ | **Analyser integration** entirely: communication modes (uni/bi-directional, download, shared folder), serial/TCP/HL7, analyser definitions and flags, test/sample/culture code mapping, Auto Send, slope/result selection/text conversion, per-machine reference ranges, Windows service + config + encrypted logs |
| ❌ | **PCR integration** (run file + interpretation sheet, Ct thresholds, verdict rules, service per machine+kit) |
| ❌ | **Auto Verification** engine: checkers, Medical Rules language, Formula Condition, result options/dictionary, auto comments, recommendations |
| ❌ | CPT codes / HIS integration hooks; QC module (undocumented in LDM too); SMS module; Online Transfer |

## I. Dashboards & analytics

| | Item |
|---|---|
| ➕ | Tenant dashboard (today's registrations, payments, orders, patients, wallet, 7-day chart, recent activity); platform dashboard across tenants |
| ❌ | LDM's operational reports listed in G and the TAT/delay views in F (LDM has no dashboard but far more reports) |

---

## Summary

**Matching (the shared core):** patient → visit → sample identifiers; registration with services, preparation questions,
payer/contract, discount, partial payment and printed receipt/job order/barcodes; price query; patient history; cancel
with reason; refund; a catalogue of groups → services → tests with the same test-type families, ranges with critical
limits, list values with normal/default, formulas with validation; sample types, units, antibiotics/organisms, comment
library, doctors, branches; worklist; result entry with flags, review → verify, previous results, attachments, logs,
printable report; bilingual output.

**Extra in Sky LIS (new platform capabilities):** SaaS tenancy and Super Admin portal, self-subscription with OTP,
central master data inherited by tenants, countries/currency/mobile validation, wallet, device-bound treasuries with
approval, responsive RTL web UI, Excel export, dashboards, LOINC everywhere, service aliases, list-value colours,
preparation question types, pricelist effective dates, POS payment method.

**Missing vs LDM (grouped by weight):**
1. **Sample lifecycle** — tube generation by unit/sample type/condition, Ordered→Collected→Received with scanning and the
   Received gate, rejection/unrejection with reasons, sample tracking.
2. **Result semantics** — reference ranges by sex/age/condition and per-analyser, panic display, formula auto-calc,
   result comments (three kinds + library), delta check, print rules (verified-only, preview refusal), undo, printed flag,
   culture entry and text-report entry with templates, signatures/templates/QR on reports.
3. **Status-driven work queues and TAT** — Search By Patient Data with "Not X" logic, bulk review/verify, analyser
   closing, print list and abbreviated report; worklist by test and collection date with duration-based delay colours.
4. **Pricing and money** — patient/insurance split, three-layer price lists with formulas and Apply, exclude/alert,
   money transfer, batch payment, daily cash closing, claims (invoices) to payers, the financial report set, tests costs,
   additional services.
5. **Registration breadth** — duplicate-patient warning, patient condition, Reserved deferral, hospital location fields,
   reception comments and VIP/urgent/biohazard flags, home reservation, batch and lab-to-lab registration, Done By,
   discount cap/reason, document attachments.
6. **Catalogue depth** — sample conditions, durations/TAT, test days, frequency check, delta check settings, labels/prefix,
   profile sums and visibility, report templates, test rules, shortcuts, CPT, diagnosis, checkups.
7. **Privilege granularity** — Q/I/U/D per screen, special rights, S/R/V/E/P per medical group and branch, locations,
   user lock/expiry/signature.
8. **Integration and automation** — analysers, PCR, auto verification, HIS/CPT.

**Note.** Several LDM areas are undocumented in the manual itself (Tools, QC, Sample Storage, SMS, Online Transfer,
Outsourcing, installation), so neither side can be compared there. The SRS behind the prototypes should be checked
before treating any "missing" item as a scope decision.
