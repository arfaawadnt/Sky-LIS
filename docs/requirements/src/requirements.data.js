// LIS Requirements Specification — source data.
// Requirement tuple: [id, title, statement, priority, origin, trace]
//   priority: M = Must, S = Should, C = Could
//   origin:   Both = in LDM and Sky prototypes · LDM = LDM only (gap in Sky) · Sky = Sky only (new) · Proposed = analyst recommendation
//   trace:    where the requirement comes from (LDM manual section, Sky screen)
module.exports = {
  meta: {
    title: 'LIS Requirements Specification',
    subtitle: 'Functional and non-functional requirements for the new Laboratory Information System, derived from the LDM 9.2/9.3 knowledge base and the Sky LIS prototypes',
    docId: 'LIS-RS-001',
    version: '0.1',
    status: 'Draft for review',
    date: '2026-10-08',
    preparedFor: 'National Technology — LIS project',
    preparedBy: 'Claude Code (AI analyst), for review by Mohamed Hashem',
    classification: 'Internal — Confidential',
    sources: [
      ['LDM System Guide — Engineer Master Edition v1.9 (2026-09-13)', 'LDM_Manual.html · docs/knowledge/ldm/LDM_Business_Knowledge.md'],
      ['Sky LIS — Client Portal Interactive Prototype v1', 'Sky-LIS-Client-Portal-Interactive-Prototype-v1.html'],
      ['Sky LIS — Super Admin Production Prototype v2', 'Sky-LIS-Super-Admin-Production-Prototype-v2 (1).html'],
      ['Gap analysis', 'docs/knowledge/sky-lis/SkyLIS_vs_LDM_Gap_Analysis.md'],
      ['Sky LIS SRS (referenced by the prototypes)', 'Not supplied — to be reconciled when available']
    ]
  },

  intro: {
    purpose: 'This document specifies the requirements of the new Laboratory Information System (LIS). It consolidates two inputs: the proven operational behaviour of LDM — National Technology\'s existing Oracle Forms laboratory system — and the intended product direction shown in the Sky LIS prototypes, a cloud, subscription-based, multi-tenant LIS with a central Super Admin portal and a per-laboratory Client portal. Every requirement is tagged with its origin so the reader can see what is baseline (present in both), what the new product adds, what LDM offers that the prototypes still lack, and what is recommended by analysis.',
    scope: 'In scope: platform and tenancy, security and privileges, catalogue and master data, patient registration in all its modes, sample lifecycle, results and verification, reporting and printing, worklists and turnaround, finance, integration with analysers and external systems, auto-verification, dashboards, and the non-functional qualities of the system. Out of scope for this revision: installation and infrastructure procedures, detailed UI design, and the LDM modules the manual leaves undocumented (Tools, Quality Control, Sample Storage, SMS, Online Transfer, Outsourcing), which appear only as placeholders.',
    audience: 'Product owner, solution architects, developers, QA, implementation and integration engineers, and laboratory domain experts who will validate the rules.',
    conventions: [
      ['Requirement ID', 'Module prefix + sequence, e.g. REG-005. IDs are stable; retired requirements are marked, never renumbered.'],
      ['Priority (MoSCoW)', 'M = Must have for the first release · S = Should have · C = Could have (later release or optional).'],
      ['Origin', 'Both = present in LDM and in the Sky prototypes · LDM = present in LDM only (a gap in the prototypes) · Sky = introduced by the prototypes · Proposed = recommended by this analysis to resolve a known LDM weakness or an inconsistency.'],
      ['Trace', 'Section of the LDM manual (§) and/or the Sky LIS screen the requirement is derived from.'],
      ['"Shall"', 'Every requirement statement is normative. "Configurable" means a tenant-level setting without code change.']
    ]
  },

  actors: [
    ['Super Admin / Operations Admin (National Technology)', 'Operates the platform: client (tenant) lifecycle, internal users and roles, global master data, platform dashboards.'],
    ['Lab Administrator (tenant)', 'Owns the laboratory\'s configuration: branches, roles and users, catalogue copy, payers and price lists, treasuries, parameters.'],
    ['Reception clerk / cashier', 'Registers patients and visits, orders services, answers preparation questions, collects payments, prints labels, job orders, receipts and verified reports, closes the cash drawer.'],
    ['Phlebotomist / courier / representative', 'Collects samples (in the lab, at home, or from sending labs), scans collection, is recorded as collector.'],
    ['Technician / chemist', 'Receives samples at the unit, runs worklists, enters or imports results, reviews, rejects unfit samples, closes analyser work.'],
    ['Unit head / responsible doctor', 'Verifies (e-signs) results, handles abnormal, panic and delta-flagged results, releases reports; may delegate to auto-verification rules.'],
    ['Microbiologist / pathologist', 'Enters culture results (organisms, antibiotics, sensitivities) and formatted text reports from templates.'],
    ['Accounts officer', 'Maintains price lists and contracts, issues claims to payers and sending labs, runs income and debtor reports.'],
    ['Application / integration engineer', 'Builds the catalogue, privileges, analyser and PCR interfaces, and auto-verification rules; supports from logs.'],
    ['Patient / payer / referring doctor (external)', 'Receive reports (print, email, SMS, web portal), request quotes, settle balances.']
  ],

  sections: [
    {
      id: 'PLT', title: 'Platform & multi-tenancy',
      intro: 'Sky LIS is delivered as a cloud service. Each subscribed laboratory is an isolated tenant served through its own portal URL, while National Technology operates a Super Admin portal for the whole platform. LDM contributes the tenant-level configuration that governs daily behaviour.',
      reqs: [
        ['PLT-001', 'Multi-tenant isolation', 'The system shall host many laboratories (clients) as isolated tenants. Tenant data, users, branches, master-data copies, documents and transactions shall never be visible to another tenant. Each tenant shall be reachable through its own portal URL.', 'M', 'Sky', 'Sky Client: public site, subscription step 4; Super Admin: Clients'],
        ['PLT-002', 'Super Admin portal', 'A Super Admin portal shall provide client lifecycle management, internal admin users and roles, global master data, and a platform dashboard, accessible only to National Technology staff.', 'M', 'Sky', 'Sky Super Admin: all screens'],
        ['PLT-003', 'Self-service subscription', 'A laboratory shall be able to subscribe online through a wizard: lab details (name, country, governorate, address, mobile, medical licence number, manager name, manager mobile, email) → one-time password sent to the manager mobile → creation of the first administrator credentials under the password policy → delivery of the tenant portal URL by SMS.', 'M', 'Sky', 'Sky Client: subscription steps 1–4'],
        ['PLT-004', 'Manual client creation', 'Super Admins shall be able to create a client manually with the same data set; each client shall record its source (Subscription or Manual), creation date and status.', 'M', 'Sky', 'Super Admin: Clients'],
        ['PLT-005', 'Client activation and support actions', 'Super Admins shall be able to activate/deactivate a client and trigger an administrator password reset. A deactivated client shall block all of its users at login with a clear message.', 'M', 'Sky', 'Super Admin: Clients row actions'],
        ['PLT-006', 'Global master data inherited by tenants', 'The platform shall maintain global master data (countries, governorates, clinical groups, services, tests, service sequence, sample types, units, drug types, antibiotic commercial names, antibiotics, organisms, preparations, comments). When a client is created it shall receive an independent copy that the client maintains on its own thereafter.', 'M', 'Sky', 'Sky Client: setup info strip; Super Admin: Master Data'],
        ['PLT-007', 'Master-data update policy', 'The platform shall define how later global changes reach tenants (none, optional pull, or proposed updates with tenant approval), and shall never silently overwrite a tenant\'s edited records.', 'S', 'Proposed', 'Gap analysis §A'],
        ['PLT-008', 'Countries master', 'Countries shall carry name, ISO code, dial code (must start with + and contain digits), currency and 3-letter currency code, language, time zone, mobile-number length, flag image and status. These values shall drive phone validation, currency display and time stamps for clients, branches, users and patients.', 'M', 'Sky', 'Super Admin: Countries'],
        ['PLT-009', 'Governorates and districts', 'Governorates (with district and remarks) shall be maintained per country and used in client, branch and patient addresses.', 'S', 'Sky', 'Super Admin: Governorates'],
        ['PLT-010', 'Branches', 'A tenant shall define branches (name in Arabic and English, code, country, governorate, phone, email, status). Users shall be assigned to one or more branches, shall select a branch at login, and every visit, sample, payment and result shall be stamped with the branch where it happened.', 'M', 'Both', 'LDM §12.4, §12.8; Sky Client: Branches, branch chooser'],
        ['PLT-011', 'Immediate configuration propagation', 'Tenant configuration (catalogue, privileges, prices) shall take effect in all branches immediately without a manual "refresh setup" step, with the change audited.', 'S', 'Proposed', 'LDM §12.8 (Refresh Setup weakness)'],
        ['PLT-012', 'Tenant parameters', 'A tenant parameters screen shall control, without code change: which optional patient fields are mandatory (e.g. mobile, doctor), automatic printing on save (labels, job order, receipt and copies), automatic sample collection/receipt, the duplicate-name warning, printing of unverified results, review-before-verify enforcement, the sample-replacement barcode policy, discount caps and default result language.', 'M', 'LDM', 'LDM §3.6 blue fields, §4.7, §3.3 Auto Receive, ch. 21 item 4'],
        ['PLT-013', 'Tenant wallet', 'The tenant portal shall display a wallet balance. The business meaning (prepaid credit, consumption events, top-up, low-balance behaviour) shall be defined before implementation.', 'C', 'Sky', 'Sky Client: topbar wallet pill, dashboard KPI'],
        ['PLT-014', 'Platform dashboard', 'The Super Admin dashboard shall show clients (active/inactive), admin users, and per selectable period the served patients, tests and registrations across tenants, the top laboratories by registrations and the most recent clients.', 'S', 'Sky', 'Super Admin: Dashboard'],
        ['PLT-015', 'Tenant profile', 'The tenant administrator shall be able to view the lab profile (name and country read-only; manager name, manager mobile, email editable) and change credentials through OTP confirmation.', 'S', 'Sky', 'Sky Client: Profile']
      ]
    },

    {
      id: 'SEC', title: 'Security, users & privileges',
      intro: 'LDM\'s privilege model is far more granular than the prototypes: rights per screen action, special rights for sensitive operations, and result rights per clinical group and branch. The prototypes add modern authentication flows. Both are required.',
      reqs: [
        ['SEC-001', 'Authentication', 'Users shall sign in with username and password (show/hide password, "remember me"), then choose a branch among those assigned to them. Failed attempts shall be rate-limited and locked after a configurable number of failures.', 'M', 'Both', 'LDM §2; Sky Client: login, branch chooser'],
        ['SEC-002', 'Password recovery by OTP', 'A user shall be able to reset their password by entering username or registered mobile, receiving a 6-digit OTP, and setting new credentials.', 'M', 'Sky', 'Sky Client: forgot password, OTP modal'],
        ['SEC-003', 'Password policy', 'Passwords shall require at least 8 characters with upper case, lower case, digit and special character; confirmation must match; expiry date and "must change at next login" shall be supported per user.', 'M', 'Both', 'Sky Client: subscription step 3; LDM §12.6'],
        ['SEC-004', 'Roles before users', 'Privileges shall be defined on roles (privilege groups) and inherited by users. No roles are preset by the product, but starter templates (Lab Administrator, Reception, Technician, Unit Head, Accounts) may be offered to new tenants.', 'M', 'Both', 'LDM §12.1–12.2; Sky Client: Roles'],
        ['SEC-005', 'Screen privileges (Q/I/U/D)', 'For every screen or module a role shall carry separate rights to open/query, insert, update and delete, with an "all" shortcut. Module-level on/off toggles alone are insufficient.', 'M', 'LDM', 'LDM §12.3; Sky: module toggles'],
        ['SEC-006', 'Special rights', 'Roles shall carry explicit special rights, including: apply discount (with a per-role percentage cap), open payment screen, refund, money transfer, cancel services, remove a service after save ("delete test any day"), delete/reactivate a verified test, modify a verified result, preview or print before verification, print invoice, print all bills, view VIP patients, modify VIP data, view unreviewed/unverified results, add services after save, change own password or username, expire users\' passwords.', 'M', 'LDM', 'LDM §12.3 Special Rights, §4.6, §10.9'],
        ['SEC-007', 'Result rights per clinical group and branch', 'For each clinical group and for each branch a role shall carry independent rights to See, Review, Verify, Edit and Print results.', 'M', 'LDM', 'LDM §12.4'],
        ['SEC-008', 'Location and payer print rights', 'Roles shall be restrictable to patient-location types (in-patient, out-patient, departments) and to printing results of selected payers/ranks only.', 'C', 'LDM', 'LDM §12.5'],
        ['SEC-009', 'User record', 'A user shall have real name, username (4–30 chars: letters, digits, dots, underscores), mobile with country code, email, role, designation, branches, status (locked/unlocked, active/inactive), password expiry, must-change-password flag, optional shifts, and a signature image used on verified reports.', 'M', 'Both', 'LDM §12.6–12.7; Sky Client: Users; Super Admin: Admin Users'],
        ['SEC-010', 'Per-user overrides', 'In the tenant portal an administrator may add or remove individual privileges on a user; such users shall be flagged as modified and a "restore to role" action shall revert them. Super Admin users shall not be able to deviate from their role.', 'S', 'Both', 'LDM §12.8; Super Admin: Admin Users note'],
        ['SEC-011', 'One person, one login', 'Every action shall be attributed to the authenticated user; shared accounts shall be discouraged by policy and detectable through concurrent-session reports.', 'M', 'LDM', 'LDM §10.9 warning, §12 Monitor Sessions'],
        ['SEC-012', 'Audit trail', 'Every create, update, delete, status change, print, payment, refund, transfer, cancellation and privilege change shall be logged immutably with user, date-time, branch and device, and shall be viewable per record and reportable.', 'M', 'Both', 'LDM §8.6 RL/SL, §10.10 Log; Sky Client: Logs'],
        ['SEC-013', 'Device-bound treasury access', 'Access to a cash treasury shall be requested from a specific device, approved or rejected by a manager, and every cash transaction shall record treasury, user and device.', 'S', 'Sky', 'Sky Client: Request Treasury, Treasury Requests'],
        ['SEC-014', 'Session management', 'Sessions shall expire after configurable inactivity; administrators shall be able to list and terminate active sessions.', 'S', 'Both', 'LDM Monitor Sessions; standard practice'],
        ['SEC-015', 'Protection of health data', 'Patient-identifiable data shall be encrypted in transit and at rest; access to results shall respect the group/branch rights; exports shall be logged.', 'M', 'Proposed', 'Regulatory practice']
      ]
    },

    {
      id: 'CAT', title: 'Catalogue & master data',
      intro: 'The catalogue is the brain of the laboratory: what can be ordered, how it is sampled, measured, flagged, timed and printed. The prototypes define its modern shape (services, tests, LOINC, typed configuration); LDM supplies the depth that daily operation depends on.',
      reqs: [
        ['CAT-001', 'Clinical groups', 'Clinical groups (lab units) shall carry code, Arabic and English names, report name, report template, display order and status. Groups drive sample splitting, result rights, worklists and report sections.', 'M', 'Both', 'LDM §11.1, §11.10; Sky: Clinical Groups'],
        ['CAT-002', 'Sample types and conditions', 'Sample types (name, short name, code, description) shall each own a list of sample conditions (e.g. fasting, random, refrigerated). A test references a sample type and a condition.', 'M', 'LDM', 'LDM §11.2 steps 5; Sky: Sample Types (no conditions)'],
        ['CAT-003', 'Units of measurement', 'Units of measurement (≤50 chars) shall be a maintained list referenced by tests and analyser mappings.', 'M', 'Both', 'LDM Results Units; Sky: Units'],
        ['CAT-004', 'Catalogue item types', 'The catalogue shall support: Test (single parameter), Service of type Test (a profile: ordered set of tests from one group printed together), Package (set of services, possibly across groups, priced as one unit), Culture, Formatted Text, and Additional Service (chargeable non-test such as SMS, delivery, home visit, urgency).', 'M', 'Both', 'LDM §11.1 seven types; Sky: Services Test/Package'],
        ['CAT-005', 'Service definition', 'A service shall carry Arabic and English names, LOINC code, optional CPT code, short name, aliases, clinical group, type, header (sub-heading on report), report name, "print alone" flag (own report sheet), visible-at-ordering, visible-in-job-order, biohazard, gender restriction, status, and an ordered member list with unique order numbers (≥1 member).', 'M', 'Both', 'LDM §11.2, §11.8–11.9; Super Admin: Services'],
        ['CAT-006', 'Test definition', 'A test shall carry unique internal name, Arabic name, report name (may repeat across tests), LOINC, clinical group, test type (Numeric, Non-numeric, Optional List, Restricted List, Formula, Formatted Text, Culture), sample type and condition, unit, gender, methodology, description, parent services and status.', 'M', 'Both', 'LDM §11.2; Super Admin: Tests'],
        ['CAT-007', 'Numeric configuration', 'Numeric tests shall support several reference-range bands, each by sex, age from/to (days, months or years) and optional patient condition, with normal low/high, critical (panic) low/high, reportable low/high (printed as "<" or ">" outside), decimal format and inclusive-boundary flag.', 'M', 'Both', 'LDM §11.3, §11.7; Super Admin: Tests → Numeric'],
        ['CAT-008', 'List and non-numeric configuration', 'List tests shall define values with sequence, text, normal flag, critical flag, default ("initial") flag and colour. Restricted lists accept only listed values; optional lists allow free text. The default value shall not be a value that reads as an issued result (e.g. "Negative").', 'M', 'Both', 'LDM §11.4; Super Admin: Tests → List'],
        ['CAT-009', 'Formula tests', 'Formula tests shall define an expression over other tests, validated on save, computed automatically when inputs exist, not editable by hand and not separately priced.', 'M', 'Both', 'LDM §11.5; Super Admin: Tests → Formula'],
        ['CAT-010', 'Culture tests', 'Culture tests shall reference the microbiology masters and carry durations, labels, sample type/condition, culture days, stability, volume and precautions. A service containing a culture test shall enable culture result entry and the culture report layout.', 'M', 'Both', 'LDM §11.11; Super Admin: Tests → Culture'],
        ['CAT-011', 'Formatted text tests', 'Formatted text tests shall define a paragraph structure (numbered headings with fonts) and a default text, and shall link to a library of report templates.', 'M', 'LDM', 'LDM §11.12; Sky: Formatted Text type only'],
        ['CAT-012', 'Turnaround settings', 'Every service/test shall carry a duration (days/hours) that yields the expected result date, a minimum duration (lab turnaround), and the weekdays on which it runs; non-running days shall not count toward the duration.', 'M', 'LDM', 'LDM §11.7 Duration, Min Duration, Test Days'],
        ['CAT-013', 'Frequency check', 'A test may define the minimum interval between repeat orders for the same patient, with an Alert (warn) or Prevent (block) behaviour, enforced across all branches of the tenant.', 'S', 'LDM', 'LDM §11.7 Frequency Check; ch. 21 item 50'],
        ['CAT-014', 'Delta check settings', 'A test may define a delta-check percentage and look-back window used to flag results that moved too far from the patient\'s previous value.', 'M', 'LDM', 'LDM §11.7 Delta Check'],
        ['CAT-015', 'Label settings', 'A test shall define the number of barcode labels to print and an optional prefix printed before its name on labels and reports.', 'M', 'LDM', 'LDM §11.7 No. of labels, Prefix'],
        ['CAT-016', 'Profile behaviour', 'A profile shall order its members, may declare a binding sum with the members that participate (e.g. differential = 100 %), may hide helper members from the report, and shall control whether its name prints as a heading and whether it can be ordered on its own.', 'S', 'LDM', 'LDM §11.8–11.9'],
        ['CAT-017', 'Packages', 'A package shall group services across groups at a single price; its members keep their own result entry and report behaviour; packages appear as their own group at ordering.', 'M', 'Both', 'LDM §11.13; Super Admin: Services → Package'],
        ['CAT-018', 'Test rules (auto-add)', 'Rules shall add companion services automatically when trigger services are ordered together; each added service is charged at its own price; rules shall be validated for contradictions.', 'S', 'LDM', 'LDM §11.14'],
        ['CAT-019', 'Ordering shortcuts', 'Services shall be orderable by short code or number, by partial name search, and through named shortcut sets (e.g. "ICU") that drop several services at their own prices.', 'S', 'LDM', 'LDM §4.4, §11.15'],
        ['CAT-020', 'Global service sequence', 'A tenant shall maintain a global display/report order for services; values shall be unique and greater than zero and editable inline.', 'S', 'Sky', 'Super Admin / Client: Service Global Sequence'],
        ['CAT-021', 'Preparation questions', 'Preparations shall carry code, bilingual question, type (Text, Yes/No, Option List with ≥2 options), gender applicability, linked services (empty = all) and status; they are asked at registration and shown with the results.', 'M', 'Both', 'LDM §4.5, §8.2 Preparations; Super Admin: Preparations'],
        ['CAT-022', 'Comment library', 'Reusable comments shall carry short name, bilingual text, scope (tenant, group) and status, and be insertable into result reports with wildcard search.', 'M', 'Both', 'LDM §8.4, §11.6; Super Admin: Comments'],
        ['CAT-023', 'Doctors', 'Referring doctors shall carry code, name, specialty, occupation, branches, contact details, optional share percentage ("special customer") and status.', 'M', 'Both', 'LDM §10.12 Doctor Income; Sky Client: Doctors'],
        ['CAT-024', 'Microbiology masters', 'The system shall maintain drug types, antibiotic commercial names, antibiotics (code, name, abbreviation, commercial names), organisms (code, name), organism–antibiotic applicability and sensitivity values (S/I/R).', 'M', 'Both', 'LDM §11.11 Culture setup; Super Admin: Drug Types … Organisms'],
        ['CAT-025', 'Patient conditions', 'A master of patient conditions (pregnant, diabetic, smoker…) shall be maintained and linked to reference-range bands.', 'M', 'LDM', 'LDM §3.5'],
        ['CAT-026', 'Diagnoses', 'A diagnosis master (ICD code, name) may link a set of services that is proposed when the diagnosis is chosen at registration.', 'C', 'LDM', 'LDM §4.2 Diagnosis'],
        ['CAT-027', 'Check-up schedules', 'A recurring check-up may define a service set repeated every N days for a patient.', 'C', 'LDM', 'LDM §4.2 Checkup'],
        ['CAT-028', 'Sending laboratories', 'Sending labs (lab-to-lab clients) shall carry contract, courier/representative, phone and doctor, used by lab-to-lab registration and claims.', 'S', 'LDM', 'LDM ch. 7'],
        ['CAT-029', 'Reason masters', 'Reason lists shall be maintained for sample rejection, service cancellation, discounts and refunds.', 'M', 'Both', 'LDM §9.4, §10.10, §10.12 Discounts; Sky: cancel/refund reasons'],
        ['CAT-030', 'Service costs', 'A cost per service may be maintained for profitability reports.', 'C', 'LDM', 'LDM §10.14 Tests Costs'],
        ['CAT-031', 'Report templates', 'Report templates (per group, per service type, culture, text) shall be configurable with logo, header/footer, signature image, QR code and font settings.', 'M', 'LDM', 'LDM §11.10, §12.7'],
        ['CAT-032', 'Integrity of master data', 'Master records shall be deactivated rather than deleted when referenced; deletion shall be blocked with a clear message; all changes audited.', 'M', 'Sky', 'Super Admin: delete confirmation note'],
        ['CAT-033', 'Standard codes', 'Every service and test shall carry a LOINC code; CPT codes shall be optional; both shall be exportable for HIS and insurer integration.', 'M', 'Both', 'LDM §11.16; Sky: LOINC everywhere']
      ]
    },

    {
      id: 'REG', title: 'Patient registration & visits',
      intro: 'Registration turns a patient at the desk into an order, samples and a bill. The prototypes show the modern flow; LDM contributes duplicate protection, deferral, hospital fields, and three further registration modes.',
      reqs: [
        ['REG-001', 'Patient file', 'A patient shall have an auto-generated, immutable patient number; title (auto-filling gender); Arabic/English name parts; gender; date of birth with bidirectional age (days/months/years ↔ birth year); mobile validated by country; national ID; nationality; email; address (governorate, city, area, text); patient condition; free comments; VIP flag; status.', 'M', 'Both', 'LDM §3.1, §4.2; Sky Client: Registration patient data'],
        ['REG-002', 'Duplicate-patient protection', 'On entering a name the system shall show similar existing patients with number and mobile for comparison; the clerk chooses the existing file or confirms a new one. Search by patient number, accession, name with wildcards, mobile or national ID shall be available before typing. The check shall apply in every registration mode.', 'M', 'LDM', 'LDM §4.3, ch. 6 warning; Sky: search link only'],
        ['REG-003', 'Patient merge', 'An administrator with the right shall be able to merge duplicate patient files, moving visits, results and payments under the surviving number with an audit record.', 'C', 'Proposed', 'LDM §4.3 (no merge available)'],
        ['REG-004', 'Visit (accession)', 'Saving a registration shall create a visit with a sequential accession number (format configurable per tenant, e.g. YYMMDD + sequence), date-time, branch, user, location type and location (out-patient, in-patient, department), room, hospital file/treatment number, referring doctor, payer and contract, insurance ID, urgent/biohazard/VIP flags, reception comments, "sample collected outside" flag and diagnosis.', 'M', 'Both', 'LDM §4.2; Sky Client: Registration'],
        ['REG-005', 'Service ordering', 'Services shall be added by browsing groups, partial-name search, shortcut code/number, shortcut set, package, diagnosis or check-up. Duplicates shall be refused, gender restrictions enforced, frequency checks applied (alert/prevent), contract alert conditions shown, precautions highlighted, and for each line the expected result date and the patient/payer price shares displayed with running totals and counts.', 'M', 'Both', 'LDM §4.4–4.5, §10.5 Alert; Sky Client: Services card'],
        ['REG-006', 'Deferred (reserved) services', 'A service whose precaution is unmet shall be set to Reserved: it stays in the visit and the bill, generates no sample, and can be reactivated to Ordered later, when its sample and labels are produced.', 'M', 'LDM', 'LDM §3.3, §4.5'],
        ['REG-007', 'Removing services', 'Before save a service may be removed freely; after save removal requires the cancellation right, a reason and the financial cancellation flow (FIN-010).', 'M', 'Both', 'LDM §4.6; Sky Client: Cancel Service'],
        ['REG-008', 'Discounts', 'A discount may be entered as amount or percentage (each recomputing the other) per visit or per service, limited by the user\'s cap, with a mandatory reason, carried into payment and reported.', 'M', 'Both', 'LDM §4.6, §10.12 Discounts; Sky: discount type absolute/percent'],
        ['REG-009', 'Preparation answers', 'Preparation questions linked to the ordered services shall be answered at registration and stored on the visit, visible to the laboratory and usable by auto-verification rules.', 'M', 'Both', 'LDM §8.2 Preparations, §15.10 Prep; Sky Client: Preparation card'],
        ['REG-010', 'Collector (Done By)', 'The user who collected each sample shall be recordable, to support home-visit shares and traceability.', 'S', 'LDM', 'LDM §4.6 Done By'],
        ['REG-011', 'Save chain', 'On save the system shall create the accession and samples, enable payment and label actions, print labels, job order and (for cash payers) open the payment step automatically according to tenant parameters; contract patients shall see only their patient share, if any.', 'M', 'Both', 'LDM §4.7; Sky Client: submit modal'],
        ['REG-012', 'Editing a saved visit', 'A saved visit shall be retrievable to add services (producing new samples and labels), reactivate reserved services, correct demographics (audited), settle balances and reprint documents.', 'M', 'Both', 'LDM §4.10; Sky Client: Search Registration edit'],
        ['REG-013', 'Patient documents', 'Documents (ID, prescription, insurance card; PDF or image) shall be attachable to the patient file and visible from any branch of the tenant.', 'S', 'Proposed', 'LDM §4.11 (branch-limited); Sky: none'],
        ['REG-014', 'Home reservation', 'A booking shall capture patient data, requested visit time, mandatory address, assigned phlebotomist, services and quoted prices, and yield a booking number with preliminary labels and job order; bookings may be cancelled; entering the booking number in registration shall convert it into a visit without retyping.', 'S', 'LDM', 'LDM ch. 5'],
        ['REG-015', 'Batch registration', 'A batch shall share payer/contract, location and doctor, list one row per person (with duplicate check and existing patient numbers), choose services once for all, produce one accession per person under a batch number, print all labels and job orders together, and settle through batch payment.', 'S', 'LDM', 'LDM ch. 6'],
        ['REG-016', 'Lab-to-lab registration', 'Samples arriving from a sending laboratory shall be registered in a batch where selecting the lab fills contract, courier and contacts; each sample row carries patient data and services; a "collected inside/outside" flag sets responsibility; billing goes to the sending lab.', 'S', 'LDM', 'LDM ch. 7'],
        ['REG-017', 'Patient history', 'The patient view shall list all visits with date, branch, status, balance and links to results and payments, plus last visit and visit count.', 'M', 'Both', 'LDM §3.1; Sky Client: Patients → visits'],
        ['REG-018', 'Search registrations', 'Registrations shall be searchable by accession, patient number, name, mobile, date range, branch, payer and status, with export.', 'M', 'Both', 'LDM §4.3 F7/F8; Sky Client: Search Registration'],
        ['REG-019', 'Price quote', 'A quote shall be producible without registering: payer, contract and services → patient and payer shares, totals, expected result dates and preparations, printable and convertible into a registration.', 'M', 'Both', 'LDM §10.7 Query; Sky Client: Query Service'],
        ['REG-020', 'Daily counters', 'Reception shall see the day\'s registration count per branch.', 'C', 'Both', 'LDM §4.2 Patients Count; Sky: dashboard KPI']
      ]
    },

    {
      id: 'SMP', title: 'Samples & phlebotomy',
      intro: 'Entirely missing from the prototypes and central to LDM: how tubes are created, labelled, tracked, received, rejected and replaced.',
      reqs: [
        ['SMP-001', 'Sample generation', 'On save the system shall create one sample per distinct combination of clinical group, sample type and sample condition among the ordered services; services sharing the combination share the tube. Each sample receives a unique, never-reused sample ID encoded as a barcode (Code 128) and optionally QR.', 'M', 'LDM', 'LDM §3.2'],
        ['SMP-002', 'Sample status track', 'Each sample shall move Ordered → Collected (scan at collection) → Received (scan at the unit); Rejected is an exceptional state. Tenants may enable automatic collection and/or receipt on save.', 'M', 'LDM', 'LDM §3.3'],
        ['SMP-003', 'Received gate', 'No result shall be entered or imported for a service until its sample is Received.', 'M', 'LDM', 'LDM §3.3'],
        ['SMP-004', 'Labels', 'Labels shall print per sample with unit code, date-time, location, sample type and condition, accession, patient name, sample ID barcode and the service names; the number of copies follows the service settings and tenant parameters; a single label shall be reprintable.', 'M', 'Both', 'LDM §3.2, §4.9; Sky Client: Sample Barcodes'],
        ['SMP-005', 'Job order', 'A job order shall print per visit with patient, visit, balance due, visit sequence for the patient, and per service its unit, expected result time, sample and barcode, plus collection time and collector signature boxes; it shall state when the sample was collected outside.', 'M', 'Both', 'LDM §4.7, ch. 7; Sky Client: Job Order'],
        ['SMP-006', 'Sample rejection', 'A sample shall be rejectable by scanning its ID, choosing a reason and confirming; its services accept no results; reception/phlebotomy shall be notified to recollect; the reason shall be stored, visible in tracking and reported.', 'M', 'LDM', 'LDM §9.4'],
        ['SMP-007', 'Sample replacement', 'A rejected sample shall be reinstated under the same barcode (unreject) or replaced by a new sample with a new barcode, according to the tenant policy (quality standards recommend a new barcode); history shall keep the rejection.', 'M', 'LDM', 'LDM §9.5'],
        ['SMP-008', 'Sample tracking', 'For any sample the system shall show every location/status change with date-time and user, the time between stages, and the test round times (complete, reviewed, verified, first/last print).', 'M', 'LDM', 'LDM §9.3'],
        ['SMP-009', 'Samples view per visit', 'The visit shall show its samples (unit, type, condition, status, status date, ID, label count) and the services on each, with label and job-order printing.', 'M', 'Both', 'LDM §4.9; Sky Client: Samples dialog'],
        ['SMP-010', 'Sample storage', 'Storage location (rack/position), retention and disposal of samples may be tracked.', 'C', 'LDM', 'LDM main menu Sample Storage (undocumented)']
      ]
    },

    {
      id: 'RES', title: 'Results, review & verification',
      intro: 'The prototypes show a simplified editor; LDM defines the semantics a laboratory depends on: ranges, flags, comments, logs, delta checks, status-driven queues and specialised entry for cultures and text reports.',
      reqs: [
        ['RES-001', 'Test status track', 'Each ordered service/test shall move Ordered → Complete (result saved) → Reviewed → Verified → Printed. A profile or package advances only when every member has; test status is independent of sample status.', 'M', 'Both', 'LDM §3.3; Sky: Saved/Reviewed/Verified'],
        ['RES-002', 'Results by accession', 'The results screen shall open a visit by accession, patient number, name (wildcard), status or analyser, show patient data (age, sex, location, doctor), indicators for reception comments, preparations, diagnosis, biohazard and VIP, and list the visit\'s report units.', 'M', 'Both', 'LDM §8.1–8.2; Sky Client: Results'],
        ['RES-003', 'Results by sample', 'Scanning a sample barcode shall open only that tube\'s tests for entry, review and verification.', 'M', 'LDM', 'LDM §8.13'],
        ['RES-004', 'Result grid', 'Each row shall show test, result (typed, list, or formula-locked), unit, reference range (numeric from/to or text), analyser source, sample status, test status, flags (High, Low, High panic, Low panic), delta flag, printed flag, and buttons for attachment, result log and status log.', 'M', 'Both', 'LDM §8.2 grid; Sky Client: results grid'],
        ['RES-005', 'Reference range resolution', 'The range applied to a result shall be resolved from the test\'s bands by the patient\'s sex, age and condition, and by the analyser-specific range when defined; the resolved range shall display on screen and print on the report.', 'M', 'LDM', 'LDM §3.5, §8.2, §13.5'],
        ['RES-006', 'Manual entry', 'Entry shall validate by type (numeric, list, text), colour out-of-range and panic values immediately, allow a "not done" placeholder that saves without printing, enforce profile sums, and save to Complete.', 'M', 'Both', 'LDM §8.3, §11.4; Sky Client: result inputs'],
        ['RES-007', 'Three comment kinds', 'The system shall distinguish the printed report comment (free text or from the library), the read-only reception comment, and internal notes between staff (author and time, never printed); indicators shall show when each exists.', 'M', 'LDM', 'LDM §8.4'],
        ['RES-008', 'Review and verify', 'Review and Verify shall be separate actions governed by group/branch rights; verify requires a result; skipping review may be allowed by tenant parameter; undo review/verify shall require a right and be audited.', 'M', 'Both', 'LDM §8.5, ch. 11 9.3.0 Undo buttons; Sky Client: Review/Verify'],
        ['RES-009', 'Result and status logs', 'Every value a test ever held (with user, time, analyser flag) and every status transition (collected, received, ordered, complete, reviewed, verified, printed and reprints) shall be viewable per row.', 'M', 'Both', 'LDM §8.6; Sky Client: Logs'],
        ['RES-010', 'Previous results and delta check', 'Previous results of the same patient shall be one click away; when a result moves beyond the configured delta percentage within the window the row shall be flagged (warning, not block) and filterable.', 'M', 'Both', 'LDM §8.6 Last Result, §8.7; Sky Client: Previous Results'],
        ['RES-011', 'Result attachments', 'Documents (PDF, images) shall be attachable to a result row and printable with the report where configured.', 'M', 'Both', 'LDM §8.6 ATT; Sky Client: Attachments'],
        ['RES-012', 'Panic value workflow', 'Panic results shall be visibly alerted, require acknowledgement, record who was notified and when, and be reportable.', 'S', 'Proposed', 'LDM §8.2 colour key, §11.7 Panic'],
        ['RES-013', 'Culture entry', 'For culture services the screen shall offer culture entry: organisms grown (or no growth), antibiotics per organism with sensitivity, and a culture comment; positive cultures print the sensitivity table beneath the examination results.', 'M', 'LDM', 'LDM §11.11'],
        ['RES-014', 'Formatted text entry', 'Text reports shall be entered per paragraph heading, with loading of one or more templates, spell check, fonts and an optional voice note.', 'M', 'LDM', 'LDM §11.12'],
        ['RES-015', 'Status-driven work queue', 'A search screen shall list report units by branch, unit, group, location, doctor, service, registration-date range, accession/sample ranges, analyser (from/to), and status boxes where exact states (Complete, Reviewed, Verified, Printed) and cumulative "Not X" states (everything below X), all-verified/all-reviewed, incomplete (all/mixed), received toggle, normal/abnormal, in/out-patient and manual tests are selectable; results shall be colour-coded by state, countable, accumulable across searches and openable per row.', 'M', 'LDM', 'LDM §8.9'],
        ['RES-016', 'Bulk review and verify', 'From the work queue a user with rights shall review or verify all listed (or selected) results at once; the action shall be audited per result.', 'S', 'LDM', 'LDM §8.11'],
        ['RES-017', 'Analyser closing views', 'The queue shall answer "what did analyser X produce that awaits review" and "what should have run on X and has no result yet".', 'S', 'LDM', 'LDM §8.10'],
        ['RES-018', 'Modifying verified results', 'Editing a verified result shall require a special right, keep the previous value in the log, and flag the row as edited.', 'M', 'LDM', 'LDM §8.6 RL, §12.3 Modify Verified Result'],
        ['RES-019', 'Result value display options', 'Optional columns such as titer/second result and methodology shall be configurable per tenant.', 'C', 'LDM', 'LDM §8.2 Titer/Methodology']
      ]
    },

    {
      id: 'RPT', title: 'Reports, printing & delivery',
      intro: 'What leaves the laboratory: patient reports, documents at reception, work documents, and the rules that protect report integrity.',
      reqs: [
        ['RPT-001', 'Patient report', 'The report shall be generated per visit and per report unit (clinical group; "print alone" services on their own sheet) from the configured template, in Arabic or English, with lab logo, patient and visit data, group headings, results with units, resolved ranges and flags, comments, verifier signature image, QR code and page numbering.', 'M', 'Both', 'LDM §3.8, §8.8, §11.10, §12.7; Sky Client: View Report'],
        ['RPT-002', 'Integrity rules', 'Preview shall be refused while a report unit has services without result (unless reserved or cancelled); only verified results shall print, except under an explicit right/parameter; each print shall set the printed flag and log user, time and count.', 'M', 'LDM', 'LDM §8.8'],
        ['RPT-003', 'Print list', 'Reception shall build a print list from verified-unprinted reports (bulk add, add by accession), print it in one action, and the list number shall print on each report.', 'M', 'LDM', 'LDM §8.12'],
        ['RPT-004', 'Worksheet (abbreviated report)', 'A multi-patient worksheet with empty result boxes and ranges shall be printable for bench entry, clearly marked as a file copy, never as a patient report.', 'S', 'LDM', 'LDM §8.12'],
        ['RPT-005', 'Reception documents', 'Receipts per payment, invoice per visit, quote, job order, labels, refund receipt and batch documents shall be printable and reprintable (audited), in the user\'s language.', 'M', 'Both', 'LDM §4.7–4.9, §10.8; Sky Client: receipt/job order/barcodes'],
        ['RPT-006', 'Result delivery channels', 'Reports shall be deliverable by email, SMS notification, patient/web link and a payer results portal with its own credentials; delivery shall be logged and chargeable as additional services.', 'S', 'Both', 'LDM §4.2 Email, §10.5 Web Results, §11.1 Additional service; Sky: email on patient'],
        ['RPT-007', 'Operational printouts', 'Worklists, price-list hand-outs per contract, analyser code sheets and cash-closing sheets shall be printable.', 'S', 'LDM', 'LDM §9.1, §10.14, §13.4, §10.11'],
        ['RPT-008', 'Printer configuration', 'Workstations shall select label printer, report printer and copies; barcode fonts/rendering shall be built in, not dependent on installed fonts.', 'M', 'Proposed', 'LDM §4.7 Code 128 / Reports 6.0 dependency']
      ]
    },

    {
      id: 'WRK', title: 'Worklist & turnaround',
      intro: 'The unit\'s daily question: what must be done now, and what is about to be late.',
      reqs: [
        ['WRK-001', 'Worklist', 'The worklist shall list one row per test, filtered by collection-date range (default today), unit, group, test, analyser, location, patient/accession/sample/internal-code ranges, sample status, payer/contract, batch and doctor, with boxes for new, remaining, completed, manual (non-analyser), not verified, not printed, incomplete, printed and delayed; it shall show counters of requests, tests and samples and be printable.', 'M', 'Both', 'LDM §9.1; Sky Client: Worklist'],
        ['WRK-002', 'Delay computation', 'Each row shall show collection date, expected result date (from duration and test days) and the computed delay; rows shall be coloured by the percentage of the duration already consumed, with tenant-configurable thresholds, and a "delayed" filter shall list what passed its time.', 'M', 'LDM', 'LDM §9.2'],
        ['WRK-003', 'Row actions', 'Each row shall give access to sample tracking and allow exclusion from the printed worklist.', 'S', 'Both', 'LDM §9.3; Sky Client: Exclude'],
        ['WRK-004', 'Turnaround reporting', 'Patient turnaround (registration → verification) and laboratory turnaround (receipt → verification) shall be reportable per test, unit, branch and period against the configured targets.', 'S', 'LDM', 'LDM §11.7 Duration / Min Duration']
      ]
    },

    {
      id: 'FIN', title: 'Finance',
      intro: 'The LIS is not an accounting system, but it prices every service per contract, collects and refunds money, closes the drawer and bills payers. Sky adds treasuries bound to devices; LDM supplies the pricing engine, transfers, closing and claims.',
      reqs: [
        ['FIN-001', 'Payers and contracts', 'Payers (ranks) shall carry bilingual name, search key, type (cash, credit, insurance), contact and status, and own contracts/tiers (relatives) each with its own price list and optional web-results credentials.', 'M', 'Both', 'LDM §3.4, §10.5; Sky Client: Payers'],
        ['FIN-002', 'Base price lists', 'Named base price lists (typically yearly) shall hold a price and an active flag per service; a new list shall be derivable from an old one by multiplying by a factor or adding an amount.', 'M', 'LDM', 'LDM §10.3–10.4'],
        ['FIN-003', 'Contract price lists by formula', 'Each contract tier shall derive its prices from base lists by formula: a patient-share factor and a payer-share factor, with up to three fallback lists, an Apply action that recomputes and stamps the apply date, per-service Exclude for negotiated prices, per-service Alert conditions shown at ordering, restore-from-base, effective-from/to dates and a printable review report; a tier list may also be copied from another tier with a factor.', 'M', 'Both', 'LDM §10.5–10.6; Sky Client: Pricelists (effective dates)'],
        ['FIN-004', 'Patient and payer shares', 'Every priced line on a visit shall carry a patient share (collected at reception) and a payer share (billed through claims); registration, quotes, receipts and reports shall show both.', 'M', 'LDM', 'LDM §3.4, §10.5'],
        ['FIN-005', 'Package and additional-service pricing', 'Packages shall be priced as a unit; additional services (SMS, delivery, home visit, urgency, results portal) shall be priced per contract.', 'M', 'LDM', 'LDM §11.13, §10.14'],
        ['FIN-006', 'Payments', 'A visit shall accept multiple payments (full or partial) by cash, card, POS or bank transfer with transaction reference, currency, treasury, user and device; each payment prints a receipt; remaining balance is maintained; a visit invoice and all receipts shall be reprintable.', 'M', 'Both', 'LDM §4.8; Sky Client: Payment card, Search Payments'],
        ['FIN-007', 'Batch payment', 'A batch (batch registration or lab-to-lab) shall be settled by batch number, member by member or with a single "pay all" amount distributed by a defined rule.', 'S', 'LDM', 'LDM ch. 6 Batch Payment'],
        ['FIN-008', 'Refunds', 'When a visit balance becomes negative (cancelled service, contract transfer) or for eligible services, a refund shall be recorded with amount, method, reason and a printed refund receipt for signature, under a dedicated right and never exceeding the refundable amount.', 'M', 'Both', 'LDM §10.8; Sky Client: Refund dialog'],
        ['FIN-009', 'Money transfer', 'A visit shall be movable to another payer/contract with automatic re-pricing and balance adjustment, and a single service\'s value shall be shiftable between patient and payer shares; both logged and reported.', 'S', 'LDM', 'LDM §10.9'],
        ['FIN-010', 'Service and visit cancellation', 'Cancelling a visit or services shall require a reason, adjust receivables, cancel related samples, keep a per-row log and appear in the cancellations report; nothing is physically deleted.', 'M', 'Both', 'LDM §10.10; Sky Client: Cancel Service'],
        ['FIN-011', 'Treasuries', 'Cash treasuries shall be defined per branch; users request access from a device, managers approve; every cash movement references treasury, user and device.', 'M', 'Sky', 'Sky Client: Treasuries, Request Treasury, Treasury Requests'],
        ['FIN-012', 'Daily cash closing', 'A closing report per treasury, user, branch and period shall list every payment and refund and compute payments minus refunds as the expected cash in drawer, with card and transfer amounts separate, outstanding balances and discounts, printable for signed handover.', 'M', 'LDM', 'LDM §10.11'],
        ['FIN-013', 'Claims to payers', 'For credit payers the system shall produce a claim for a period (or selected visits) per contract or tier in several layouts (standard, per tier, with results, totals only, with test counts), with page and report totals, approval boxes and Excel export; claims to sending labs shall be supported.', 'M', 'LDM', 'LDM §10.13'],
        ['FIN-014', 'Financial reports', 'The system shall provide: patients\' income, income by payment method, income by contract type, monthly statistics per payer, patient financial statement, doctor income with share, clients income, service income analysis per payer, outstanding balances as of a date, discounts with reasons and users, cancellations, transfers, total income per day, price list hand-outs and cost-versus-income.', 'M', 'LDM', 'LDM §10.12, §10.14'],
        ['FIN-015', 'Search payments', 'Payments shall be searchable by accession, patient, status (pending/paid), date range and branch, with actions to pay or refund, and export.', 'M', 'Sky', 'Sky Client: Search Payments'],
        ['FIN-016', 'Commission shares', 'Shares for doctors and representatives shall be computable from their patients\' income over a period.', 'C', 'LDM', 'LDM §10.12 Doctor Income, Representative Income'],
        ['FIN-017', 'ERP export', 'Income, refunds and claims shall be exportable to an external accounting/ERP system.', 'C', 'LDM', 'LDM ch. 10 intro']
      ]
    },

    {
      id: 'INT', title: 'Integration',
      intro: 'Analyser and PCR interfacing removes hand-copying of results and is a major LDM capability absent from the prototypes. Cloud delivery requires a site agent architecture.',
      reqs: [
        ['INT-001', 'Analyser communication modes', 'The system shall support uni-directional (analyser → LIS), bi-directional (analyser queries orders by sample ID), download (LIS pushes orders in advance) and file-based (shared folder XML/Excel/CSV) analysers over serial, TCP/IP (client or server) and file share, using HL7/ASTM where applicable.', 'M', 'LDM', 'LDM §13.1'],
        ['INT-002', 'Analyser definition', 'Each analyser shall carry code, name, default sample type/item type/test, print copies, auto-print after auto-verification, download mode, policies to auto-register unordered tests and unknown samples, policies to overwrite reviewed/verified results on re-run, and a flag table marking which analyser flags stop a result.', 'M', 'LDM', 'LDM §13.2, §15.7'],
        ['INT-003', 'Test, sample and culture mapping', 'Per analyser the system shall map tests (analyser code, host code, result-selection index, slope/intercept, unit, query and query-details sub-values, auto-send switch, analyser-specific range, methodology, numeric-to-text conversion table), sample-type codes, and culture codes for organisms, antibiotics and sensitivities.', 'M', 'LDM', 'LDM §13.4–13.7'],
        ['INT-004', 'Site integration agent', 'A single configurable site agent (Windows service) shall connect several analysers of a branch to the cloud, with encrypted credentials, hourly and error logs readable by support, health monitoring and remote configuration; one service per analyser shall remain possible.', 'M', 'LDM', 'LDM §13.8–13.10 (direction: central service)'],
        ['INT-005', 'PCR import', 'PCR instruments shall be integrated by file import: a run-file layout per instrument (sheets, columns, well order, instrument serial lock), an interpretation profile per kit (per-gene Ct thresholds, combination verdicts including INVALID when the internal control fails), result/backup/failed folders, and an audit flag storing well, gene and Ct per saved result.', 'S', 'LDM', 'LDM ch. 14'],
        ['INT-006', 'External systems', 'The system shall expose APIs for hospital information systems (orders in, results out), insurers (claims with LOINC/CPT) and ERP (income), and support webhooks for key events.', 'S', 'Both', 'LDM §11.16; Sky: LOINC'],
        ['INT-007', 'Scanning and label hardware', 'Barcode scanners shall work on every scan-enabled field (collection, receipt, results by sample, rejection); label printers shall be configurable per workstation.', 'M', 'Both', 'LDM §3.3, §8.13; Sky: barcodes'],
        ['INT-008', 'Messaging channels', 'SMS and email gateways shall be configurable per tenant for OTP, notifications and result delivery.', 'M', 'Both', 'Sky: OTP; LDM: Sms module, Email']
      ]
    },

    {
      id: 'AUT', title: 'Auto-verification',
      intro: 'Releases normal results without a doctor\'s click, from rules collected from the laboratory itself, so that only abnormal and critical results reach the doctor.',
      reqs: [
        ['AUT-001', 'Engine and checkers', 'A tenant shall enable auto-verification globally and per clinical group, choosing checkers: medical rules, no-rule guard (a test without a rule never auto-releases), result check, comment dictionary check, option check, analyser flag check, device flag check, incomplete check, panic and reportable check, delta check, all-group-complete, inter-analyser delta, quality-control check, and an optional normal-range check.', 'S', 'LDM', 'LDM §15.3–15.6'],
        ['AUT-002', 'Rule language', 'Medical rules per test shall use an expression language with parameters in braces, arithmetic and comparison operators, And/Or/Not, normal/high/low tests, live upper/lower range values, sex, age in days, device code, doctor code, preparation answers, required companion tests, last result with window and a "no last result" guard, comment attachment and rounding; each rule may carry a pre-condition evaluated first, a recommendation text for the doctor and an active flag, and shall be validated before saving.', 'S', 'LDM', 'LDM §15.9–15.14'],
        ['AUT-003', 'Result options and dictionary', 'Allowed choice values per non-numeric parameter and a dictionary of valid comment words (case-sensitive, with invalid words) shall support the option and comment checkers.', 'C', 'LDM', 'LDM §15.8'],
        ['AUT-004', 'Automatic comments and printing', 'Rules shall be able to insert report comments automatically and, combined with analyser settings, trigger automatic printing after release.', 'C', 'LDM', 'LDM §8.4, §13.2, §15.15'],
        ['AUT-005', 'Engine audit and simulation', 'For every result the engine shall record which rule or checker released or stopped it; a simulation mode shall test rules against historical results before enabling the engine.', 'S', 'Proposed', 'LDM §15.3 warning, ch. 19 (engine runtime undocumented)']
      ]
    },

    {
      id: 'DSH', title: 'Dashboards & analytics',
      intro: 'The prototypes introduce dashboards; LDM contributes the operational measures worth showing.',
      reqs: [
        ['DSH-001', 'Tenant dashboard', 'The tenant home shall show today\'s registrations, payments, orders and patients, pending and delayed work, a seven-day registration trend, recent activity and quick actions, filtered by the user\'s branch.', 'S', 'Sky', 'Sky Client: Dashboard'],
        ['DSH-002', 'Operational KPIs', 'Dashboards shall expose turnaround compliance, delayed tests, sample rejection rate by reason, unverified backlog by group, revenue by payer and outstanding balances.', 'C', 'Proposed', 'LDM ch. 9–10 measures'],
        ['DSH-003', 'Exports', 'Every grid shall export to Excel respecting the active filters and the user\'s privileges.', 'M', 'Sky', 'Sky: Export on all grids']
      ]
    },

    {
      id: 'QCM', title: 'Quality control & other LDM modules (placeholders)',
      intro: 'LDM carries modules the manual does not document. They are listed so scope decisions are explicit.',
      reqs: [
        ['QCM-001', 'Quality control', 'QC materials, lots, targets and Levey-Jennings evaluation per analyser and test, used by the delta-check investigation and the auto-verification QC checker.', 'C', 'LDM', 'LDM main menu Quality Control; §8.7 step 2'],
        ['QCM-002', 'Outsourcing', 'Sending services to outside laboratories with their own contracts, tracking and reports.', 'C', 'LDM', 'LDM §10.1 Outsourcing'],
        ['QCM-003', 'Online transfer and SMS modules', 'Inter-branch data transfer and SMS campaigns as in LDM shall be re-evaluated against the cloud architecture.', 'C', 'LDM', 'LDM main menu Online Transfer, Sms']
      ]
    }
  ],

  nfr: [
    ['NFR-001', 'Bilingual interface', 'All screens, master data and documents shall be available in Arabic and English with full right-to-left mirroring; each user chooses a language; reports print in the language configured for the patient or payer.', 'M', 'Both'],
    ['NFR-002', 'Responsive web client', 'The client portal shall work on desktop, tablet and mobile browsers (current Chrome, Edge, Safari, Firefox) with a mobile navigation drawer.', 'M', 'Sky'],
    ['NFR-003', 'Data-entry speed', 'High-volume screens (registration, results, worklist) shall be fully keyboard-operable with shortcuts for save, new, search and quick service lookup, so that throughput is not lower than LDM\'s key-driven forms.', 'M', 'Proposed'],
    ['NFR-004', 'Performance targets', 'With 1,500 samples per shift per unit: results screen opens in under 2 s, searches return in under 3 s, labels print within 2 s of save, reports render within 5 s.', 'S', 'Proposed'],
    ['NFR-005', 'Availability and recovery', 'The service shall target 99.9 % monthly availability with daily backups, point-in-time recovery and a documented disaster-recovery plan; label printing and result entry shall degrade gracefully during short outages.', 'S', 'Proposed'],
    ['NFR-006', 'Auditability and non-repudiation', 'Result verification shall constitute an electronic signature bound to the verifying user; audit records shall be immutable and retained for the legally required period.', 'M', 'Both'],
    ['NFR-007', 'Security', 'Tenant isolation at data and application layers, TLS everywhere, encryption of health data at rest, role-based access, OTP channels, device binding for cash, session timeout, protection against common web vulnerabilities, and periodic penetration testing.', 'M', 'Both'],
    ['NFR-008', 'Localisation', 'Currency, date/time formats, time zones and phone validation shall follow the country master; numbers on reports shall respect the configured decimal format.', 'M', 'Sky'],
    ['NFR-009', 'Printing', 'Barcode (Code 128) and QR rendering, PDF generation and printer selection per workstation shall be built in and not depend on locally installed fonts or legacy report engines.', 'M', 'Proposed'],
    ['NFR-010', 'Accessibility', 'Colour shall never be the only cue (text badges accompany status colours); contrast and keyboard focus shall meet WCAG 2.1 AA basics.', 'S', 'Proposed'],
    ['NFR-011', 'Configurability', 'Behaviour described as configurable in this document shall be changeable by tenant administrators without code changes or vendor involvement.', 'M', 'Both'],
    ['NFR-012', 'Data migration', 'A migration path from LDM (Oracle) shall bring patients, visits, results history, catalogue, prices, contracts and users into a tenant with validation reports.', 'S', 'Proposed'],
    ['NFR-013', 'Data retention', 'Retention periods for medical records, audit logs and documents shall be configurable per tenant within regulatory minimums.', 'S', 'Proposed'],
    ['NFR-014', 'Extensibility', 'Public APIs and webhooks shall be versioned and documented; HL7 FHIR shall be considered for result exchange.', 'C', 'Proposed']
  ],

  rules: [
    ['BR-01', 'Immutable identifiers', 'Patient numbers are generated by the system and never change; accession numbers are sequential per visit; sample IDs are unique and never reused.', 'LDM §3.1'],
    ['BR-02', 'Tube merge', 'Services with the same clinical group, sample type and sample condition share one sample; any difference splits them. Configuration, not the clerk, decides.', 'LDM §3.2'],
    ['BR-03', 'Received gate', 'A result may not be recorded until the sample status is Received.', 'LDM §3.3'],
    ['BR-04', 'Profile completion', 'A profile or package reaches Complete, Reviewed or Verified only when every member test has.', 'LDM §3.3'],
    ['BR-05', 'Reserved keeps the bill', 'Deferring a service (Reserved) keeps it in the visit and the bill with no sample; deleting removes it from both.', 'LDM §4.5'],
    ['BR-06', 'Contract sets the price', 'Payer and contract tier determine every service\'s patient share and payer share; a wrong contract produces a wrong invoice.', 'LDM §3.4'],
    ['BR-07', 'Three-layer pricing', 'Base list price × tier factor (patient) and × tier factor (payer) = contract price; Apply propagates base-list changes to all derived tiers except excluded services.', 'LDM §10.2–10.6'],
    ['BR-08', 'Patient condition selects the range', 'Reference ranges resolve by sex, age and patient condition (and analyser when defined); the condition is captured at registration.', 'LDM §3.5'],
    ['BR-09', 'Verify is a signature', 'Verification records who signed and when; only verified results print; unverified printing is an exception under explicit right.', 'LDM §8.5, §8.8'],
    ['BR-10', 'Not-X queue logic', 'In status searches "Not X" returns everything below X in the test track (e.g. Not Verified = Ordered + Complete + Reviewed).', 'LDM §8.9'],
    ['BR-11', 'Delta check warns', 'A delta flag warns and must be investigated (instrument, patient, sample) but does not block verification.', 'LDM §8.7'],
    ['BR-12', 'Frequency check', 'A repeat order inside the configured interval alerts or is prevented, regardless of branch.', 'LDM §11.7'],
    ['BR-13', 'Turnaround clock', 'Expected result date = collection (or registration) time + duration counting only the test\'s running days; delay colours are a percentage of the duration consumed.', 'LDM §9.2, §11.7'],
    ['BR-14', 'Negative balance means refund', 'A negative visit balance (after cancellation or contract transfer) is returned through the refund flow with a signed receipt.', 'LDM §10.8'],
    ['BR-15', 'Nothing is erased', 'Cancellations, transfers, rejections and edits leave the original data with reason, user and time.', 'LDM §10.10, §9.4, §8.6'],
    ['BR-16', 'Drawer equation', 'Expected cash = payments − refunds for the treasury/user/period; card and transfer amounts are not in the drawer.', 'LDM §10.11'],
    ['BR-17', 'Group privileges propagate', 'Changing a role changes every user in it immediately; changing one user affects that user only.', 'LDM §12.8'],
    ['BR-18', 'New sample, new barcode', 'Quality standards require a replacement sample to carry a new barcode; reinstating under the old barcode is a tenant policy exception.', 'LDM §9.5'],
    ['BR-19', 'Default list value is not a result', 'The default value of a list test must be empty or a placeholder, never a value that reads as an issued result.', 'LDM §11.4'],
    ['BR-20', 'Hidden helpers', 'Helper tests feeding formulas or sums are hidden from reports by configuration (LDM used a leading dot).', 'LDM §11.8'],
    ['BR-21', 'Percentage tolerance is a multiplication', 'A ±20 % tolerance on a range is written as × 0.8 and × 1.2 of the live range values, never as a subtraction.', 'LDM §15.11'],
    ['BR-22', 'Every boundary needs its complement', 'A rule written for one side of a boundary (age, device, sex) must have a pre-condition or complement so patients outside it are not stopped.', 'LDM §15.12–15.13'],
    ['BR-23', 'No last result must not stop', 'Rules using the previous result must not stop a patient who has none inside the window.', 'LDM §15.14'],
    ['BR-24', 'Validated selection only', 'Fields that reference master data accept only selected values; free text in such fields (LDM\'s hand-typed LOV values) must be impossible.', 'LDM §3.9, §11.2'],
    ['BR-25', 'Tenant copy of master data', 'A tenant\'s master data is an independent copy of the global templates; later global changes never overwrite tenant edits silently.', 'Sky Client setup note']
  ],

  gaps: {
    matching: [
      ['Identifiers', 'Patient number, visit accession, sample ID'],
      ['Registration flow', 'Patient data → services → preparation questions → payer/contract → discount → payment → receipt, job order, barcode labels'],
      ['Reception tools', 'Price quote before registering, search registrations, patient history, edit visit, cancel service with reason, refund, partial payments and remaining balance'],
      ['Catalogue', 'Clinical groups, services and tests, test-type families (numeric, non-numeric, lists, formula, text, culture), ranges with critical limits, list values with normal/default, formula validation, sample types, units, antibiotics, organisms, preparations, comments, doctors, branches'],
      ['Lab workflow', 'Worklist, result entry with flags, review then verify, previous results, attachments, logs, printable report'],
      ['Cross-cutting', 'Bilingual Arabic/English output, roles then users, multi-branch, LOINC codes']
    ],
    extra: [
      ['Platform', 'Cloud multi-tenant SaaS, Super Admin portal, client lifecycle, per-tenant URL, self-service subscription with OTP, password policy, forgot-password OTP'],
      ['Master data', 'Global templates inherited by tenants; countries (dial code, currency, timezone, mobile length, flag); governorates/districts; service aliases; list-value colours; typed preparation questions; global service sequence; pricelist effective dates'],
      ['Finance', 'Device-bound treasuries with manager approval; POS payment method; refund eligibility rule; wallet balance'],
      ['Experience', 'Responsive web with RTL, dashboards and recent activity, Excel export on every grid, mobile drawer']
    ],
    missing: [
      ['Sample lifecycle', 'Tube generation rule, sample conditions, Ordered → Collected → Received with scanning, Received gate, rejection/unrejection with reasons, sample tracking, label copies and prefix', 'Adopt (Must)'],
      ['Result semantics', 'Reference ranges by sex/age/condition/analyser on grid and report, panic colours, formula auto-calculation, three comment kinds and library, delta check, verified-only printing and preview refusal, undo, printed flag/count, culture entry, text reports with templates, signatures/QR/templates', 'Adopt (Must)'],
      ['Work queues and TAT', 'Status search with Not-X logic, bulk review/verify, analyser closing, print list and worksheet; worklist by test and collection date with duration-based delay colours and counters', 'Adopt (Must/Should)'],
      ['Pricing and money', 'Patient/payer share split, base lists and formula-derived tiers with Apply/Exclude/Alert, money transfer, batch payment, daily cash closing, claims in six layouts, financial report set, service costs, additional services', 'Adopt (Must/Should)'],
      ['Registration breadth', 'Duplicate-patient warning, patient condition, Reserved deferral, hospital location fields, reception comments, VIP/urgent/biohazard flags, home reservation, batch and lab-to-lab registration, collector, discount cap/reason, document attachments', 'Adopt (Must/Should)'],
      ['Catalogue depth', 'Durations/TAT, test days, frequency check, delta settings, labels/prefix, profile sums and visibility, report templates, auto-add rules, shortcuts, CPT, diagnoses, check-ups, patient conditions, sending labs', 'Adopt (Must/Should)'],
      ['Privileges', 'Q/I/U/D per screen, special rights, S/R/V/E/P per clinical group and branch, locations, user lock/expiry/signature, restore to role', 'Adopt (Must)'],
      ['Integration and automation', 'Analyser modes and mapping, site agent, PCR import, auto-verification engine and rule language, HIS/ERP/insurer APIs', 'Adopt (Must for analysers; Should for PCR and auto-verification)'],
      ['Undocumented LDM modules', 'Quality Control, Sample Storage, SMS, Online Transfer, Outsourcing', 'Decide with the product owner']
    ]
  },

  openQuestions: [
    ['OQ-01', 'The Sky LIS SRS referenced by the Super Admin prototype was not available. Every "missing" item must be reconciled against it before being treated as a scope decision.', 'Product owner'],
    ['OQ-02', 'Wallet balance: what it represents (prepaid credit, per-visit consumption, subscription fee), how it is topped up and what happens at zero.', 'Product owner'],
    ['OQ-03', 'Master-data update policy after tenant creation: none, optional pull, or proposed updates requiring tenant approval (PLT-007).', 'Product owner'],
    ['OQ-04', 'Patient/payer price split: the client prototype prices one amount per service. Confirm that insurance contracts with a payer share are in scope (FIN-004).', 'Product owner / accounts'],
    ['OQ-05', 'Review/verify order: LDM desktop verifies after review; the LDM web edition reversed it. Confirm the target sequence and whether review may be skipped.', 'Laboratory director'],
    ['OQ-06', 'Default sample policy for new tenants: scan-based collection/receipt or automatic receipt on save (PLT-012, SMP-002).', 'Implementation'],
    ['OQ-07', 'Accession and sample ID formats per tenant (length, branch prefix, date component) and label stock sizes.', 'Implementation'],
    ['OQ-08', 'Privilege model depth in the first release: full Q/I/U/D + special rights + group/branch result rights, or a staged subset.', 'Product owner'],
    ['OQ-09', 'Scope of the undocumented LDM modules (QC, Sample Storage, SMS, Online Transfer, Outsourcing) in the new product.', 'Product owner'],
    ['OQ-10', 'Analyser connectivity architecture in the cloud: site agent per branch versus per analyser; supported protocols (HL7 v2, ASTM, file).', 'Integration team'],
    ['OQ-11', 'Data migration from LDM installations: which entities, history depth and cut-over approach.', 'Implementation'],
    ['OQ-12', 'LDM manual items still unconfirmed that affect this document: System Parameter fields (item 4), Auto Receive configuration (5), Conditions screen (6), Batch Payment behaviour (11), Delete Patient effects (39), restore-from-base and Exclude (40), frequency check across branches (50), Formula Setup syntax (54).', 'National Technology product team']
  ],

  entities: [
    ['Tenant (Client)', 'Branch', 'User', 'Role / Privilege', 'Treasury', 'Device'],
    ['Country', 'Governorate', 'Clinical Group', 'Sample Type', 'Sample Condition', 'Unit of Measurement'],
    ['Service', 'Test', 'Reference Range Band', 'List Value', 'Formula', 'Preparation Question'],
    ['Comment Template', 'Report Template', 'Doctor', 'Organism', 'Antibiotic', 'Sensitivity'],
    ['Patient', 'Patient Condition', 'Patient Document', 'Visit (Accession)', 'Order Line (Service on visit)', 'Preparation Answer'],
    ['Sample', 'Label', 'Sample Status Event', 'Rejection', 'Result', 'Result Log / Status Log'],
    ['Culture Result', 'Text Result', 'Attachment', 'Report Print', 'Print List', 'Worklist Item'],
    ['Payer', 'Contract Tier', 'Base Price List', 'Contract Price List', 'Price Line (patient/payer share)', 'Discount'],
    ['Payment', 'Refund', 'Money Transfer', 'Cancellation', 'Cash Closing', 'Claim'],
    ['Analyser', 'Analyser Test Mapping', 'Analyser Sample Code', 'Interface Message / Log', 'PCR Run / Interpretation', 'Auto-verification Rule']
  ],

  glossary: [
    ['Accession number', 'Sequential identifier of one visit (order); holds services, samples and the bill.'],
    ['Clinical group', 'Laboratory unit/section (Haematology, Chemistry…); drives tube splitting, rights and report sections. LDM: Group.'],
    ['Contract tier', 'Sub-category of a payer with its own price list (employees, families, cash). LDM: Relative.'],
    ['Delta check', 'Comparison of a result with the patient\'s previous value within a window; flags excessive change.'],
    ['Job order', 'Printout travelling with the tubes: services, units, samples, barcodes, collection signature. LDM: Sampling Details.'],
    ['Package', 'Set of services across groups priced as one. LDM: Panel; a set of profiles printed as one report is a Mega Profile.'],
    ['Panic (critical) value', 'Result beyond life-threatening limits requiring immediate notification.'],
    ['Patient condition', 'Attribute (pregnant, diabetic, smoker) that selects an alternative reference range.'],
    ['Payer', 'Body paying for services: cash, company, syndicate, insurer, sending lab. LDM: Rank.'],
    ['Preparation', 'Question asked at registration about the patient\'s readiness (fasting, medication).'],
    ['Received gate', 'Rule that no result can be entered before the sample is received at the unit.'],
    ['Reserved', 'Deferred service kept in the visit without a sample until its precaution is met.'],
    ['Sample ID', 'Unique barcode number of one tube.'],
    ['Service', 'Orderable catalogue item: a single test, a profile of tests, a package, a culture, a text report or an additional service.'],
    ['Treasury', 'Cash drawer at a branch, accessed by approved users from approved devices.'],
    ['Turnaround (TAT)', 'Patient TAT: registration → verification. Lab TAT: receipt → verification.'],
    ['Verification', 'Doctor\'s sign-off (electronic signature) that releases a result for printing.'],
    ['Worklist', 'List of tests to perform, one row per test, filtered by collection date.']
  ],

  traceability: [
    ['PLT', 'Sky Super Admin (all); Sky Client public/subscription/branches/profile', 'LDM ch. 12 (branches, System Parameters), §3.6, §4.7'],
    ['SEC', 'Sky Client login/OTP/roles/users/treasury requests; Super Admin roles/users', 'LDM ch. 12'],
    ['CAT', 'Super Admin Master Data; Sky Client Setup', 'LDM ch. 11, §3.2, §3.5, §10.14'],
    ['REG', 'Sky Client Registration, Search Registration, Patients, Query Service', 'LDM ch. 3–7, §10.7'],
    ['SMP', 'Sky Client Samples dialog, barcodes', 'LDM §3.2–3.3, §4.9, ch. 9'],
    ['RES', 'Sky Client Results', 'LDM ch. 8, §11.11–11.12'],
    ['RPT', 'Sky Client View Report, print buttons', 'LDM §8.8, §8.12, §11.10, §12.7'],
    ['WRK', 'Sky Client Worklist', 'LDM ch. 9'],
    ['FIN', 'Sky Client Payment, Search Payments, Payers, Pricelists, Treasuries', 'LDM ch. 10, §4.8'],
    ['INT', '—', 'LDM ch. 13–14, §11.16'],
    ['AUT', '—', 'LDM ch. 15'],
    ['DSH', 'Sky Client Dashboard; Super Admin Dashboard', 'LDM reports (ch. 9–10)'],
    ['QCM', '—', 'LDM main menu (undocumented modules)']
  ]
};
