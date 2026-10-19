# Sky LIS prototypes v3 / v4.1 — difference against the developed application

| Item | Value |
| --- | --- |
| Compared | `Sky-LIS-Client-Portal-Interactive-Prototype-v3.html` and `Sky-LIS-Super-Admin-Production-Prototype-v4.1.html` (repository root, supplied 2026-10-18) against the application as built and installed (release **v0.14.1**, WIN-9S2KO4FA64L) |
| Baseline | the earlier prototypes `…-v1.html` (client) and `…-v2 (1).html` (admin), from which Release 1 was specified (`SkyLIS_Prototypes_Knowledge.md`, Business SRS LIS-SRS-001) |
| Method | structural inventory of each prototype (navigation, pages, fields, buttons, grids, validation texts, dictionary patches), diff of the new against the earlier one, then each proposal checked against the built code (domain, contracts, routes, pages) |
| Status | Derived by the development team; the decisions in §4 are for the business owner |

The two files are **supersets of the earlier prototypes**: the v3 client keeps everything v1 had and adds layers labelled v2, v2.1 and v3 (patches on the navigation, the dictionary and the pages); the v4.1 admin keeps the v2 screens and reworks the catalogue. So the "suggestion" is the delta described in §1; §2 states, item by item, what the developed application already does, what it does differently and what it does not have; §3 lists what the built application has beyond the prototypes.

## 1. What the new prototypes propose (delta against v1 / v2)

### 1.1 Client Portal v3

**Terminology (dictionary patch).** *Service* → **Test** (the base unit), *Services / Packages* → **Profiles / Panels** (a profile contains tests, a panel contains profiles), *Service global sequence* → **Global sequence** (of tests), *Payer* → **Contract**, *Contract* → **Relationship** (a contract has relationships; each relationship has a price list and a patient share %), *Query service* → **Query test**.

**Navigation.** Added: *Dashboard* (insights, after Home), *Accession details* (before Results), *Payment methods* (setup, after Treasuries), *Test price* (setup, after Price lists). **Removed from the menu:** *Treasuries*, *Request treasury*, *Treasury requests* and *Search payments* — payments are handled inside registration / accession details; the treasury concept disappears from the client menu.

**Dashboard — Workspace / Insights (v3).** Filters: registration date from / to, payer / contract, branch. KPIs: registrations (selected period), payments (EGP), active ordered items, unique patients, current wallet. Charts: daily registrations, daily payments against visits registered each day, top 20 doctors, top 20 payers / contracts (ranked by orders, net amount), pies: net amount by share (patient / payer), registrations by gender, net amount paid / remaining, ordered test mix (test / profile / panel).

**Registration (v2 and v3 layers).** Patient block: name, Arabic name, gender, date of birth or age (D/M/Y), mobile, **second phone**, e-mail, ID number, insurance ID, address. *Contract & test selection*: contract → relationship, a catalogue picker over tests / profiles / panels with LOINC search; order grid with price, **patient share and contract share per item** (price × relationship percentage), result date = max test duration, no. of tests, total cash + contract. **Discount** as value or percentage, **applied to the patient share only**. **Payments inside registration**: several payments, each with a payment method and a transaction number when the method requires it; payment summary (gross, discount, patient payable, paid, remaining, **refund due**); payment history on the visit; edit registration; *Continue to registration* from Query test. Preparation questions of the selected tests answered on the page.

**Accession details (new page).** Find by accession (or patient name / number / phone in the orders grid), visit information, all ordered tests, **cancel an ordered test with a reason** (cancelled rows stay highlighted), **collect sample** with confirmation, **enter a result** ("result today"), previous results, open payments.

**Patients.** Patient details drawer: edit patient, visit history, open results, open payments; second phone, insurance, ID number, address.

**Setup additions.**
- *Payment methods*: code, name, Arabic name, **requires transaction no.**, status (Cash, Card, POS, Bank transfer seeded).
- *Tests* (v2.1 test configuration): types Numeric / Non-numeric, Optional list, Formatted text, Formula, Culture; **reference ranges by age** (from / to in days / months / years, inclusive flag) with normal low / high, critical low / high and non-numeric comments; optional list values with colour, normal / critical flags and a default; formula expression with *Check formula*; culture configuration from active organisms and antibiotics; duration (hours) and working days for the result date; short name; **alias names**; *Exists in profiles* read-only.
- *Profiles / Panels*: name, LOINC (unique), short name, clinical group, type Profile or Panel, included tests (profile) or included profiles (panel), print alone, header, alias name; rules: a panel contains profiles only, a profile contains tests only, members active and unique.
- *Global sequence*: drag, arrow buttons or a typed position; clinical-group filter reorders only the visible rows.
- *Contracts* (formerly payers): name, code, contact details (phone 1, phone 2, **WhatsApp**, e-mail), status, **relationships & pricing** (relationship name, price list, patient share %, contract share % = remainder); validation "the relationship price list is missing an ordered test".
- *Price lists*: create (auto-generated code, name), priced items per test / profile / panel, **Download Excel template / Upload Excel** with row-level validation, **bulk price adjustment** (± value or %, rounding to whole / nearest 5 / nearest 10, preview, apply), print price list.
- *Test price* (new page): pick a test / profile / panel → every price list that prices it, with edit.
- *Preparations*: code, question (English / Arabic), question type (Yes / No, Option list, Text), ordered answer options with a default, assign tests (picker with *select visible*), duration.
- Setup grids: pagination, *This record is in use. Set it inactive instead.*

### 1.2 Super Admin Portal v4.1

**Navigation.** Home, Dashboard, Roles, Admin users, Clients, **Client login logs** (new), Countries, Governorates (with **Location setup**), Clinical groups, **Profile / Panel** (formerly Services), Tests, Global sequence (of tests), Sample types, Units, Drug types, Antibiotic commercial names, Antibiotics, Organisms, Preparations, Comments. Dropped: *Services / Packages / Service type / Parent service / Print-alone service* wording; *Export Excel* → *Export CSV*.

**Client login logs.** Per client user session: login and logout date / time (UTC), IP address, computer / mobile, OS and browser, duration (min), session status; filters by client, user, login / logout window, device, duration bounds and status; export CSV.

**Location setup.** Governorate → **City → District → Area** hierarchy edited inside the governorate (names unique within a parent; children removed before a parent).

**Countries.** Name, code, dial code (`+` and digits), currency, currency code, time zone, mobile number length, **flag image** (JPG / PNG / SVG).

**Dashboard.** Operational period (last 7 / 30 / 90 days, this month); KPIs clients, admin users, served patients, tests, registrations; clients overview (active / inactive donut), registrations overview by top laboratories, recent clients, *View all clients*.

**Catalogue.** Tests with the same configuration as the client v2.1 (ranges, optional values with colour, formula, culture), short name, aliases, *Exist in profiles*; Profile / Panel editor with ordered members and the type rules; global sequence of tests with position input; drug types seeded as dosage forms (eye drop, IM injection, capsule, tablet …); commercial names carry a drug type.

**Roles / users / clients.** Roles with screen privileges (at least one for an active role); admin users with country code, mobile, privileges inherited from the role; clients grid with *Source* (subscribed / manual), reset password and activation from row actions; the main Super Admin user is protected.

## 2. Difference against the developed application

Status: **Built** = exists as proposed · **Different** = exists with another model, name or place · **Partial** = part of it exists · **Not built** = absent.

### 2.1 Client portal

| # | Prototype v3 proposal | Developed application (v0.14.1) | Status | Note |
| --- | --- | --- | --- | --- |
| C1 | Terminology: Test / Profile / Panel, Contract / Relationship | Service types **Test, Profile, Package, Culture** (`ServiceType`); members: a profile holds tests, a package holds services (CAT-021/022/027/036 rules); **Payer → contract tiers → contract pricing** per base price list with patient / payer factors and per-service shares (`ContractPricing`) | Different | Same structures, different names: Package ≈ Panel, Payer ≈ Contract, Contract tier ≈ Relationship. Renaming is a dictionary and SRS change; the data model needs nothing. |
| C2 | Dashboard / Insights with period, contract and branch filters, top-20 doctors and contracts, share and gender pies, paid / remaining, test mix | CP-05 dashboard: registrations, collected today, samples received, pending / unverified / delayed, rejected, wallet, 7-day registrations chart, recent activity, quick actions; **Work amount report** (`/reports/work-amount`) gives the amounts by day, payer, contract tier, user, doctor, clinical group, service or branch with CSV | Partial | The numbers exist (work amount report + dashboard); the filterable insights page with ranked lists and pies is not built. |
| C3 | Registration: second phone, e-mail, ID, insurance ID, address on the patient block | Patient: name, Arabic name, gender, DOB / age, national ID, nationality, condition, mobile, e-mail, governorate, city, address, VIP; visit: insurance ID, treatment no. | Partial | Second phone is absent; insurance ID lives on the visit (as in LDM), not the patient. |
| C4 | Contract & relationship selection; patient share / contract share per ordered item | CP-06: payer / contract chooser, service entry with typeahead, line prices with patient share and payer share from the contract pricing | Built | |
| C5 | Discount as value or percentage on the patient share only | Visit discount with `DiscountKind` (amount / percent), cap from the special right, reason; applied to the patient share | Built | |
| C6 | Several payments inside registration, payment method + transaction number when required, refund due, payment history | Registration takes one payment on the page (treasury approved for the device); further payments, partial payments and refunds on **Payments** (CP-23, by accession) with a reference field; refunds with reasons on `/billing/refunds` | Different | The money flow is complete but split across Registration and Payments; the prototype folds it into the visit. Treasury / device approval (BR-27) has no counterpart in v3. |
| C7 | Result date = max test duration, no. of tests | Expected result per line from the test's `DurationHours` / `MinDurationHours`; line count on the page | Built | |
| C8 | Continue to registration from Query test | CP-12 price query → registration | Built | |
| C9 | Accession details page: visit info, all ordered tests, cancel a test with reason, collect sample, enter a result, previous results, payments | Search registrations row actions: edit, Patient, samples, payments, documents, results, **cancel services with reason**; Results by accession (entry, review, verify, print, previous results); sample pages exist in the backend and the Release-2 menu (auto-collect / auto-receive on in Release 1) | Different | Everything is reachable from the visit row, not from one page; sample collection confirmation is Release 2 by decision. |
| C10 | Payment methods as setup (code, name, requires transaction no.) | Fixed methods Cash, Card, POS, Transfer (`PaymentMethod`) with an optional reference on every payment | Not built | Small: a tenant list of methods with the "requires reference" flag, or keep the four methods and make the reference mandatory per method. |
| C11 | Treasuries, treasury requests and Search payments removed from the menu | Treasuries, Treasury access (device approval, SoD with the administrator exception), Payments register with the collections report | Different | The prototype drops controls the business asked for (BR-27, 2026-10-14 self-approval). Not recommended to follow. |
| C12 | Test types Numeric / Non-numeric / Optional list / Formatted text / Formula / Culture | `LabTestType` Numeric, NonNumeric, OptionalList, RestrictedList; formula expression on the test; culture through the Culture service type with organisms, antibiotics and sensitivity values; formatted text = paragraphs on results | Built | Different naming only (Restricted list ≈ optional list without free text). |
| C13 | Reference ranges by age (D/M/Y, inclusive), gender, normal and critical bounds, non-numeric comments | `RangeBandSpec` with age unit days / months / years, gender restriction, low / high, critical low / high, `ReferenceRangeResolver`; reference text printed per range | Built | |
| C14 | Optional list values with colour, normal / critical flags, default | `ListValueSpec(Value, IsNormal, IsCritical, IsDefault, IsPlaceholder, Colour)` | Built | |
| C15 | Test short name, alias names, *Exists in profiles* | Code, short name, LOINC, CPT, methodology; **parent services on the test page** (added 2026-10-14); aliases absent | Partial | Alias names (search synonyms) are the only gap. |
| C16 | Duration (hours) and working days for the result date | `DurationHours`, `MinDurationHours`, `TestDays` (working days) | Built | |
| C17 | Profiles / Panels editor: members, print alone, header, alias, LOINC unique | Services editor: members (tests / services) with sequence, print alone, report header, LOINC; type rules CAT-021/022/036 | Built | Panel ≈ Package. |
| C18 | Global sequence: drag / arrows / typed position, clinical-group filter | CP-42 service sequence: a sequence number typed per service (number input), saved per row | Partial | Drag-and-drop, move up / down and reordering inside a clinical-group filter are not built. |
| C19 | Contracts: contact details (phone 1 / 2, WhatsApp, e-mail) | Payer: name, search key, web portal user, default flag, status; contract tiers; no contact block | Not built | Small: three phone / e-mail fields on the payer. |
| C20 | Relationships with price list and patient share % | Contract pricing: priority, base price list, patient factor, payer factor, per-service overrides, exclusions, alert condition | Built | Richer than the proposal (per-service overrides, exclusions, alerts). |
| C21 | Price lists: Excel template download / upload with validation, bulk adjustment with rounding, print | Base price lists with per-service prices, effective dates, CSV export | Not built | Medium: Excel import (validated), bulk adjust (± value / %, rounding), printable list. |
| C22 | Test price page (which price lists price a test) | Price query by visit (CP-12); price list detail per list | Not built | Small: a per-service view across price lists. |
| C23 | Preparations: question types Yes / No, Option list, Text; ordered options with default; assigned tests | Preparations with kinds Text, YesNo, OptionList, gender restriction, options, service assignment; answers captured at registration; **no default option** | Partial | Small: a default answer per question. |
| C24 | Patient drawer: edit, visit history, open results / payments | Patient file (CP-11) with visits, documents, merge; Patient drawer from the visits register (2026-10-13) | Built | |
| C25 | Setup grids: pagination, "in use → set inactive" | Master-data pages: server-side paging and sorting, deactivate instead of delete, read-only drawer without the edit right | Built | |
| C26 | Subscription wizard, OTP, branch choice, forgot password | CP-02 / CP-03 / CP-04 built; the OTP is shown on the page until an SMS gateway exists (`RevealCodes`) | Built | |

### 2.2 Super Admin portal

| # | Prototype v4.1 proposal | Developed application (v0.14.1) | Status | Note |
| --- | --- | --- | --- | --- |
| A1 | Client login logs: login / logout times, IP, device, OS / browser, duration, filters, CSV | SA-08 Sessions: live sessions per client user with `LastSeenAtUtc`, `RevokedAtUtc`, `IpAddress`, `UserAgent`, revoke; SA-09 audit log and `audit.security_events` (sign-in, sign-out, refusals) | Partial | The facts are stored; a history grid with the device parsed from the user agent, duration and the filter set is not built. |
| A2 | Location setup: governorate → city → district → area | Countries and governorates (with the mobile length per country); patients carry governorate, city (free text) and address | Not built | Medium (new master data + patient / client address pick lists). Needs the business to confirm that pick lists are wanted over free text. |
| A3 | Countries: dial code, currency, time zone, mobile length, flag image | Country: dial code, currency (+ code), language, time zone, mobile length, **flag** text | Built | Flag as an uploaded image instead of a code is a small change. |
| A4 | Dashboard with operational period, KPIs, clients donut, registrations by top labs, recent clients | SA-03 `PlatformDashboard`: period, client counts, admin users, activity totals, wallets at or below zero, plan distribution, registrations by client, recent clients | Built | |
| A5 | Roles with screen privileges; main Super Admin protected | SA-06 roles with catalogue permissions, special rights; primary administrator protected (TEN-012) | Built | |
| A6 | Admin users with country code + mobile; privileges from the role only | SA-07: mobile in E.164 (unique), role, no overrides in the platform tenant | Built | |
| A7 | Clients grid with source, reset password, activation | SA-04 clients: source Subscription / Manual, status, reset client password, activate / deactivate, wallets and plans (SA-05) | Built | |
| A8 | Profile / Panel catalogue, tests with configuration, global sequence of tests, aliases | SA-12 master data mirrors the client catalogue (services, tests with ranges / lists / formula, sequence, sample types, units, preparations, comments) copied to new laboratories | Built | Aliases absent (as C15); naming as C1. |
| A9 | Drug types as dosage forms; commercial names with a drug type | SA-14 drug types, commercial names, antibiotics, organisms, sensitivity values | Built | |
| A10 | Export CSV on every grid | CSV export on every register (UTF-8 BOM, formula-safe) | Built | |

## 3. Built but absent from the prototypes

The developed application covers the whole Release-1 scope of the Business SRS and a number of later decisions that the v3 / v4.1 prototypes do not show:

- **Treasury** (CP-28 / CP-29): treasuries per branch, device-bound access requests, approval with segregation of duties and the primary-administrator exception, revocation; the Payments page accepts cash only on an approved treasury (BR-27).
- **Payments register and refunds** (CP-23, `/billing/refunds`), the **collections (treasury) report** by method / cashier / branch with the branch right, cash closing (Release 2 backend).
- **Work amount report** with eight groupings, CSV, the per-user *report days* limit.
- **Special rights** (discount cap, pay, refund, transfer, cancel, modify verified, preview unverified, print invoice, see VIP, demographics edit limit by result status, branch collections, report days) and **result rights** per clinical group × branch.
- **Patient identity lock** by result status (PAT-032) and VIP masking.
- **Results by accession** as built: sticky context strip, criteria dialog, matching-visits panel, previous / next visit, review → verify with delta acknowledgement, amend with reason, undo, comments and internal notes, print lists, report templates with logo and signature, hidden-by-rights notes.
- **Audit**: field changes, hash-chained audit trail, security events, verify-chain endpoint; audit log pages.
- **Platform**: subscriptions and wallets (plans, charge run), notification templates, platform parameters, sessions, tenant settings (26 keys incl. time zone and session timeout).
- **Security**: credential envelope, refresh-token rotation with reuse detection, idle timeout, password policy, mobile uniqueness, rate limits, RLS per tenant.
- **Catalogue extras**: patient conditions, reasons, diagnoses, additional services, report templates, test rules, shortcut sets, sending labs, doctors; service sequence; microbiology sensitivity values.
- **Integration and automation backends** (analysers, site agents, PCR kits, verification settings, medical rules) ready for Release 2.

## 4. Decisions and candidate changes

**For the business owner (decisions before any work):**

1. **Terminology** — adopt the v3 words (Test / Profile / Panel, Contract / Relationship, Global sequence, Query test)? Dictionary, SRS and intake wording change only; the stored model stays. Recommendation: decide once for both portals; the LDM-trained users know *Service / Package / Payer*.
2. **Payments inside the visit versus the Payments page** — the prototype removes *Search payments* and the treasury pages. Recommendation: keep the Payments register and the treasury controls (they implement BR-27 and the self-approval decided on 2026-10-14); optionally show the visit's payment history on the registration edit page.
3. **Sample collection in the client portal** — the prototype confirms collection per visit; Release 1 runs with auto-collect / auto-receive on. Keep as decided (Release 2) unless the pilot needs manual collection.
4. **Location hierarchy** (city → district → area) — new master data or keep free-text city / address?

**Candidate changes, by size:**

| Size | Change | Reference |
| --- | --- | --- |
| Small | Payment methods per tenant with *requires reference* (or a mandatory reference for Card / POS / Transfer) | C10 |
| Small | Contact block on the payer (phone 1 / 2, WhatsApp, e-mail) | C19 |
| Small | Alias names on tests (search synonyms) | C15, A8 |
| Small | Second phone on the patient; flag image on the country | C3, A3 |
| Small | Test price view: one service across every price list | C22 |
| Medium | Price list Excel import (validated) and bulk adjustment with rounding; printable price list | C21 |
| Medium | Insights dashboard: period / contract / branch filters, top-20 doctors and contracts, share / gender / paid-remaining / test-mix charts (data already served by the work amount report) | C2 |
| Medium | Client login history grid (device from the user agent, duration, filters, CSV) over the stored sessions and security events | A1 |
| Medium | Location hierarchy master data and address pick lists | A2 |
| Medium | Global sequence: drag-and-drop and move up / down, reordering within a clinical-group filter | C18 |
| Large | Accession details as one page (visit, lines, cancel, collection, results, payments) | C9 — mostly a UI consolidation of existing pages; sample collection is a scope decision |
