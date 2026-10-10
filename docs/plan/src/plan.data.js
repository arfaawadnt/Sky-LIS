// Sky LIS — phased build plan. Order requested: foundation → existing pages with their enhancements → new pages → new modules → go-live.
// Effort sizes (full stack, incl. API, UI, tests): S ≤ 3 dev-days · M = 1–2 dev-weeks · L = 2–4 dev-weeks · XL > 4 dev-weeks
module.exports = {
  meta: { title: 'Sky LIS Build Plan — Phases', subtitle: 'Delivery phases to build the application: foundation first, then the pages that already exist in the prototypes with their enhancements, then the new pages, and the new modules at the end', docId: 'LIS-BP-001', version: '1.0', date: '2026-10-08', preparedFor: 'National Technology — LIS project', preparedBy: 'Claude Code (AI analyst), for review by Mohamed Hashem', inputs: ['Sky-LIS_Prototype_Comparison.html (LIS-PC-001)', 'LIS_Requirements_Specification.html (LIS-RS-001)', 'SkyLIS_vs_LDM_Gap_Analysis.md', 'Client Portal v2 and Super Admin v3 prototypes'] },
  assumptions: [
    'Stack per the workspace rules: .NET 9 Web API, Angular, PostgreSQL 17 (multi-tenant schema with tenant isolation), one site agent service for analysers. Indicative durations assume one team of 2 backend, 2 frontend, 1 QA and 1 product/analyst, working in two-week sprints.',
    'Every phase delivers working software against the prototype screens: the prototype is the UI contract, the requirements specification is the behaviour contract, and the comparison document is the scope list.',
    'A page is "done" when its API, UI (English and Arabic), validations, audit entries, printed documents and automated tests pass the exit criteria listed for the phase.',
    'Where an enhancement of an existing page needs master data that only exists on a new page (for example patient conditions for registration), the enhancement is built with a temporary seed and completed in the phase that delivers the page. These completions are marked "completes" in the tables.',
    'Durations are indicative for planning and must be re-estimated after Phase 0, when the team has measured its velocity on the real code base.'
  ],
  phases: [
    {
      id: 'P0', name: 'Foundation & platform plumbing', weeks: 6, kind: 'foundation',
      goal: 'Stand up the multi-tenant platform, security, shell and the shared services every page depends on. Nothing visible to a laboratory yet beyond login and the empty shell.',
      items: [
        ['Solution skeleton & environments', 'Foundation', 'Repositories, CI/CD, dev/test/staging environments, PostgreSQL multi-tenant schema (tenant_id on every table, row-level isolation), EF Core migrations, API versioning, logging and health checks.', ['Reserved ports/names in CLAUDE.md'], 'PLT-001, NFR-005, NFR-007', 'L'],
        ['Identity & access framework', 'Foundation', 'Username/password login, OTP channel, password policy, session timeout, lock/expiry, roles with per-screen Q/I/U/D and special rights, result rights per group/branch, privilege check middleware.', ['Notification gateway'], 'SEC-001–SEC-010, SEC-014', 'L'],
        ['Tenant provisioning', 'Foundation', 'Subscription wizard (lab details → OTP → credentials → portal URL), manual client creation, activation, master-data copy on creation, branches, branch selection at login.', ['Identity', 'Global master data (Phase 2)'], 'PLT-002–PLT-006, PLT-010', 'L'],
        ['Design system & application shell', 'Foundation', 'Angular shell from the prototype design system: bilingual EN/AR with RTL, navigation groups with counters, grids with search/filter/export, forms with validation, modals, toasts, keyboard shortcuts.', ['—'], 'NFR-001, NFR-002, NFR-003, NFR-010', 'L'],
        ['Audit trail & notification centre', 'Foundation', 'Immutable audit log service (user, time, branch, device), notification centre, in-app alerts.', ['Identity'], 'SEC-012, DSH-001 (activity)', 'M'],
        ['Document & printing service', 'Foundation', 'Server-side PDF rendering, label printing with built-in Code 128/QR, printer selection per workstation, report template engine (logo, signature, QR).', ['Report templates (Phase 3A)'], 'RPT-001, RPT-008, NFR-009', 'L'],
        ['Notification gateway', 'Foundation', 'SMS and email providers per tenant with templates (OTP, portal URL, result ready, panic).', ['—'], 'INT-008', 'M'],
        ['Tenant parameters framework', 'Foundation', 'Parameter store read by every module (mandatory fields, auto-print, auto-receive, discount cap, delay thresholds…).', ['—'], 'PLT-012, NFR-011', 'S']
      ],
      exit: ['A laboratory can subscribe, receive its portal URL, log in, choose a branch and see the empty bilingual shell.', 'Roles and users can be created with per-screen rights and the API refuses unauthorised calls.', 'A test label and a test PDF print from the printing service.', 'Audit entries are written for login, user and role changes.']
    },
    {
      id: 'P1', name: 'Client Portal — existing pages with their enhancements', weeks: 8, kind: 'enhanced',
      goal: 'Rebuild every page that existed in Client Portal v1, with the enhancements shown in v2, so a laboratory can register, order, collect money, enter results and print on day one.',
      items: [
        ['Dashboard', 'Overview', 'Eight KPIs (registrations, collected, samples received, pending results, unverified backlog, delayed tests, rejected samples, wallet), 7-day chart, quick actions, delayed-tests table, activity from the audit log.', ['Audit trail', 'Results & samples data (P1 results)'], 'DSH-001', 'M'],
        ['Clinical groups · Sample types · Units · Drug types · Antibiotics · Organisms · Comments · Doctors', 'Setup', 'Shared CRUD pages on the new grid/editor framework with Arabic names, reference-safe deletion; sample conditions on sample types; unit code and template on groups; group scope on comments; branches and share % on doctors.', ['Shell framework'], 'CAT-001–CAT-003, CAT-022–CAT-024', 'M'],
        ['Tests', 'Setup', 'Full editor: information, configuration by type (range bands by sex/age/condition with panic limits, list values with normal/critical/default, formula with validation, text paragraphs), timing (duration, min duration, frequency check, delta, test days), labels/prefix, visibility flags. Completes with Patient Conditions (P3A).', ['Clinical groups', 'Sample types & conditions', 'Units'], 'CAT-006–CAT-012, CAT-014, CAT-015', 'L'],
        ['Services', 'Setup', 'Types Test/Package/Culture/Text/Additional, sample and condition, header, aliases, precaution, gender, duration, labels, print-alone, visibility, members with order/sum/visibility, profile sum.', ['Tests', 'Sample types'], 'CAT-004, CAT-005, CAT-016, CAT-017', 'L'],
        ['Service sequence · Preparations', 'Setup', 'Inline sequence with uniqueness; preparation question types with options, gender and linked services.', ['Services'], 'CAT-020, CAT-021', 'S'],
        ['Roles & privileges · Users', 'Setup', 'Role editor with per-screen Q/I/U/D, special rights (discount cap…), result rights per clinical group and branch, locations; users with branches, lock, expiry, must-change, signature image, overrides with restore.', ['Identity framework', 'Clinical groups', 'Branches'], 'SEC-004–SEC-010', 'L'],
        ['Branches', 'Setup', 'Bilingual names, country/governorate from master, currency, reference-safe deletion.', ['Countries & governorates (P2)'], 'PLT-010', 'S'],
        ['Payers & contracts · Base price lists', 'Finance', 'Payers with type, tiers and web-results credentials; base lists with active flags, copy by formula, Apply to derived contracts. Completes with Contract Pricing (P3E).', ['Services'], 'FIN-001, FIN-002', 'M'],
        ['Treasuries · Treasury requests', 'Finance', 'Treasuries per branch; device-bound requests with approve/reject/revoke and audit.', ['Branches', 'Audit trail'], 'SEC-013, FIN-011', 'M'],
        ['Registration', 'Reception', 'Patient file (auto number, title → gender, age ↔ DOB, condition, IDs, address, comments, VIP), duplicate protection (similar-name modal, wildcard search), visit data (location, room, treatment no., doctor, payer/tier, insurance ID, flags, diagnosis), ordering by name/short code/number/set with picker, precautions, Reserved, gender restriction, frequency check, contract alerts, auto-add rules, per-line shares, discount with cap and reason, preparation answers, tube preview, save chain with labels/job order/payment, edit mode. Completes with Patient Conditions, Reasons, Diagnoses, Shortcut sets, Test rules, Parameters (P3A) and Contract Pricing (P3E).', ['Tests & services', 'Payers & price lists', 'Preparations', 'Doctors', 'Printing service', 'Parameters'], 'REG-001, REG-002, REG-004–REG-012, SMP-001, SMP-004, SMP-005', 'XL'],
        ['Search registrations · Patients', 'Reception', 'Filters, balance badges, actions (edit, samples, payments, documents, results); patient search, history, documents, merge, new visit.', ['Registration', 'Printing service'], 'REG-013, REG-017, REG-018, REG-003', 'M'],
        ['Price query', 'Reception', 'Quote per payer/tier with shares, expected date and preparation, printable, continue to registration.', ['Pricing', 'Services'], 'REG-019', 'S'],
        ['Payments', 'Finance', 'Partial payments, methods with references, treasury/cashier/device stamping, receipts and invoice, auto-print parameter.', ['Treasuries', 'Printing service'], 'FIN-006, FIN-015', 'M'],
        ['Worklist', 'Laboratory', 'One row per test by collection date, filters and status boxes, counters, expected date and delay from durations, colour by consumed percentage, bench printout.', ['Tests (timing)', 'Samples data (basic statuses from registration)'], 'WRK-001–WRK-003', 'M'],
        ['Results by accession', 'Laboratory', 'Query mode, case header with CR/preparations/diagnosis/flags, report units per clinical group, result grid with resolved ranges, flags and panic, formula recalculation, three comment kinds with library, logs, previous results, delta flag, Review/Verify/Undo with rights, preview and print rules, laboratory report. Culture and text entry are completed in P3D.', ['Tests', 'Roles (result rights)', 'Comment library', 'Printing service'], 'RES-001, RES-002, RES-004–RES-012, RES-018, RPT-001, RPT-002', 'XL'],
        ['Profile', 'Account', 'Lab details, portal URL, plan, credentials by OTP.', ['Identity'], 'PLT-015', 'S']
      ],
      exit: ['Walk-in registration to printed verified report works end to end on real printers.', 'Cash collected with receipts and treasury stamping; balances correct after partial payments.', 'Reference ranges resolve by sex, age and condition; panic values alert; only verified results print.', 'Arabic and English pass UI review; audit entries exist for every action above.']
    },
    {
      id: 'P2', name: 'Super Admin — existing pages with their enhancements', weeks: 4, kind: 'enhanced', parallel: 'Can run in parallel with P1 by the backend pair once the foundation is in place.',
      goal: 'Rebuild the operator portal pages that existed in v2 with the v3 enhancements, so National Technology can create and operate tenants.',
      items: [
        ['Dashboard', 'Overview', 'Period KPIs across tenants, wallets-at-zero, subscription vs manual split, plan distribution, recent clients.', ['Clients', 'Subscriptions (P4)'], 'PLT-014', 'S'],
        ['Clients', 'Administration', 'Plan, wallet, URL, licence, counts; activate/deactivate (blocks users), reset password, master-data copy on create; filters by source and plan.', ['Tenant provisioning', 'Countries'], 'PLT-004, PLT-005', 'M'],
        ['Roles · Admin users', 'Administration', 'Per-screen Q/I/U/D, platform special rights, user lock/expiry/policy, role inheritance without override.', ['Identity framework'], 'SEC-004–SEC-006, SEC-009', 'M'],
        ['Countries · Governorates', 'Master data', 'Arabic names, dial-code validation, mobile length, reference-safe deletion.', ['—'], 'PLT-008, PLT-009', 'S'],
        ['Clinical groups · Services · Tests · Sequence · Sample types · Units · Preparations · Comment library · Microbiology group', 'Master data', 'Same editors as the client (shared components) on the global templates; publish semantics per the master-data policy.', ['P1 shared editors', 'Master-data policy (P4 parameters)'], 'PLT-006, PLT-007, CAT-*', 'M']
      ],
      exit: ['A client created here appears with its own master-data copy and can log in.', 'Global catalogue edits are versioned and published according to the configured policy.']
    },
    {
      id: 'P3', name: 'Client Portal — new pages', weeks: 14, kind: 'new',
      goal: 'Add the pages that did not exist in v1, in dependency order: catalogue depth first (they complete the P1 enhancements), then reception modes, samples, laboratory, finance and account.',
      sub: [
        { id: 'P3A', name: 'Catalogue depth & parameters', weeks: 2, items: [
          ['Patient conditions · Reasons · Diagnoses · Shortcut sets · Test rules · Report templates · Additional services · Sending labs', 'Setup', 'New master-data pages. Completes: Registration (conditions, reasons, diagnoses, shortcuts, rules), Tests (condition bands), Results (templates), Reject/Cancel/Refund (reasons).', ['Services & tests'], 'CAT-013, CAT-018, CAT-019, CAT-025–CAT-029, CAT-031, CAT-032', 'M'],
          ['Parameters', 'Setup', 'Tenant switches page over the parameter framework. Completes: Registration (mandatory fields, similar-name, discount cap), printing, samples and results rules.', ['Parameter framework'], 'PLT-012', 'S']] },
        { id: 'P3B', name: 'Reception — new modes & printing', weeks: 3, items: [
          ['Home reservation', 'Reception', 'Booking with mandatory address, time, phlebotomist, services and quote; preliminary labels; cancel; convert to visit.', ['Registration', 'Users'], 'REG-014', 'M'],
          ['Batch registration', 'Reception', 'Shared contract and service set, member rows with patient lookup, batch number, bulk documents, hand-over to batch payment.', ['Registration', 'Batch payment (P3E)'], 'REG-015', 'M'],
          ['Lab-to-Lab registration', 'Reception', 'Sending-lab selection, per-row services, collected inside/outside flag, batch number, billing to the lab.', ['Sending labs', 'Registration'], 'REG-016', 'M'],
          ['Print reports', 'Reception', 'Verified-unprinted queue with print list numbering and abbreviated worksheet (shares the work-queue engine of P3D).', ['Work queue engine'], 'RPT-003, RPT-004', 'S']] },
        { id: 'P3C', name: 'Samples', weeks: 3, items: [
          ['Collection & receipt', 'Samples', 'Barcode scanning in collect/receive modes, whole-accession actions, status events, the Received gate enforced by the results API, auto-collect/receive parameters.', ['Samples generated in P1', 'Parameters'], 'SMP-002, SMP-003', 'M'],
          ['Samples by visit · Sample tracking', 'Samples', 'Tube list with reprint; timeline, stage durations and test round times.', ['Collection events', 'Results status logs'], 'SMP-008, SMP-009', 'M'],
          ['Reject / unreject', 'Samples', 'Rejection with reasons and notification, reinstatement or replacement with a new barcode per policy, rejected list.', ['Reasons', 'Labels', 'Parameters'], 'SMP-006, SMP-007', 'M']] },
        { id: 'P3D', name: 'Laboratory — queues, specialised entry', weeks: 3, items: [
          ['Work queue', 'Laboratory', 'Report-unit engine with status boxes incl. Not-X, nine colour states, received toggle, keep-previous, bulk review/verify, print list, abbreviated report, analyser-closing presets.', ['Results (P1)', 'Roles'], 'RES-015, RES-016, RES-017, RPT-003', 'L'],
          ['Results by sample', 'Laboratory', 'Scan a tube and work only its tests with the shared case panel.', ['Results (P1)', 'Samples'], 'RES-003', 'S'],
          ['Culture entry & formatted-text entry', 'Laboratory', 'Completes Results: organisms/antibiotics/sensitivities and no-growth with culture report layout; paragraph entry with templates, spell check, voice note.', ['Microbiology masters', 'Report templates'], 'RES-013, RES-014', 'M'],
          ['Quality control (placeholder)', 'Laboratory', 'Screen with sample QC runs; full module deferred to P5.', ['—'], 'QCM-001', 'S']] },
        { id: 'P3E', name: 'Finance — pricing engine, money movements, closing & claims', weeks: 3, items: [
          ['Contract pricing', 'Finance', 'Per-tier formula over up to three base lists with patient/payer factors, effective dates, Apply, exclusions, alert conditions, copy from tier. Completes: Registration and Price query shares, Payers.', ['Base price lists', 'Payers'], 'FIN-003, FIN-004, FIN-005', 'L'],
          ['Refunds · Money transfer · Cancellations', 'Finance', 'Refund rules and receipt; contract and line transfers with re-pricing; cancellations with reasons, sample cascade and receivable adjustment.', ['Payments', 'Reasons', 'Special rights'], 'FIN-008, FIN-009, FIN-010', 'M'],
          ['Batch payment · Cash closing', 'Finance', 'Batch settlement and pay-all; closing per treasury/cashier/period with the drawer equation and handover sheet.', ['Payments', 'Treasuries'], 'FIN-007, FIN-012', 'M'],
          ['Claims · Financial reports', 'Finance', 'Claims in six layouts with drafts/issued and Excel; sixteen reports on a shared report runner.', ['Pricing engine', 'Visits & payments data'], 'FIN-013, FIN-014, FIN-016', 'L']] },
        { id: 'P3F', name: 'Account', weeks: 1, items: [
          ['Wallet · Support · Audit log', 'Account', 'Wallet balance and transactions with top-up requests; support tickets; tenant audit log viewer.', ['Subscriptions (P4)', 'Audit trail'], 'PLT-013, SEC-012', 'S']] }
      ],
      exit: ['Home, batch and lab-to-lab registrations produce visits, documents and settlements.', 'No result can be entered before a sample is received; rejection and replacement follow the tenant policy.', 'The work queue reproduces the Not-X logic and bulk sign-off; print lists are numbered.', 'Contract prices derive correctly from base lists; cash closing balances to the cent; a claim prints for a credit payer.']
    },
    {
      id: 'P4', name: 'Super Admin — new pages', weeks: 3, kind: 'new', parallel: 'Can overlap with P3E/P3F.',
      goal: 'Add the operator pages that did not exist in v2.',
      items: [
        ['Subscriptions & wallets', 'Administration', 'Plans, client wallet balance and transactions, top-ups, monthly charges and metered usage; defines the client-portal wallet.', ['Clients'], 'PLT-013', 'M'],
        ['Sessions · Audit log', 'Administration', 'Active sessions with termination; platform audit trail.', ['Identity', 'Audit trail'], 'SEC-012, SEC-014', 'S'],
        ['Patient conditions · Reasons · Diagnoses · Additional services · Report templates · Test rules · Shortcut sets · Sensitivity values', 'Master data', 'Global versions of the catalogue-depth pages (shared components from P3A).', ['P3A components'], 'PLT-006, CAT-*', 'M'],
        ['Platform parameters · Notification templates · Profile', 'Platform', 'OTP, password, session, trial, defaults and master-data update policy; SMS/email templates in both languages; administrator profile.', ['Notification gateway'], 'PLT-007, INT-008', 'S'],
        ['Analyser models · PCR kits · Code systems · Rule templates (catalogue only)', 'Integration library', 'Reference libraries published to tenants; the runtime behaviour arrives with P5.', ['—'], 'INT-001 (library), INT-005 (library), CAT-033', 'M']
      ],
      exit: ['Wallets charge monthly fees and metered usage; a zero wallet pauses metered services as designed.', 'Master-data updates reach tenants according to the configured policy.']
    },
    {
      id: 'P5', name: 'New modules — integration, automation, quality control', weeks: 10, kind: 'module',
      goal: 'Deliver the modules that are entirely new to Sky LIS and depend on everything before them: analyser interfacing with a site agent, PCR import, the auto-verification engine, and quality control.',
      items: [
        ['Site agent (Windows service) & cloud endpoint', 'Integration', 'Per-branch agent with encrypted credentials, secure channel to the cloud, serial and TCP drivers, file watcher, hourly and error logs, heartbeat, remote configuration push and log fetch.', ['Analysers page', 'Foundation security'], 'INT-004', 'XL'],
        ['Analysers · Analyser tests · Analyser samples', 'Integration', 'Definitions (modes, links, flags, policies), code mapping with Auto Send, slope/intercept, result selection, query details, conversion tables, analyser-specific ranges, sample codes; results land as Complete with analyser source; From/To analyser filters in the queue.', ['Tests', 'Results engine', 'Site agent'], 'INT-001–INT-003, INT-007', 'L'],
        ['PCR kits & file import', 'Integration', 'Run-file parsers per instrument, interpretation per kit (thresholds, verdicts, invalid when IC fails), serial lock, result/backup/failed folders, audit flag per well.', ['Site agent', 'Analysers'], 'INT-005', 'L'],
        ['Verification settings & medical rules', 'Automation', 'Rule language parser and evaluator (parameters, norm/high/low, H-Val/L-Val, sex, age, device, prep, require, LR/No LR, comments), formula condition, checker pipeline (medical, no-rules, flags, panic, delta, group complete, inter-analyser delta, QC), result options and dictionary, simulation mode, engine audit, auto comment and auto print.', ['Results engine', 'Analyser flags', 'Tests'], 'AUT-001–AUT-005', 'XL'],
        ['Quality control', 'Laboratory', 'QC materials and lots, targets and SD per analyser/test, Levey-Jennings charts, Westgard rules, QC hold feeding the auto-verification QC checker and the delta investigation.', ['Analysers', 'Tests'], 'QCM-001', 'L'],
        ['External APIs', 'Integration', 'HIS orders-in/results-out, insurer claim export with LOINC/CPT, ERP income export, webhooks.', ['Pricing engine', 'Results engine'], 'INT-006, FIN-017', 'L']
      ],
      exit: ['A haematology analyser and a chemistry analyser deliver results into the results screen through the agent, with flags and logs readable by support.', 'A PCR run file is imported and interpreted correctly, including INVALID when the internal control fails.', 'Auto-verification releases normal results for one clinical group in simulation and then live, with the engine audit explaining every decision.']
    },
    {
      id: 'P6', name: 'Hardening, migration & go-live', weeks: 4, kind: 'launch',
      goal: 'Prepare the first production laboratories.',
      items: [
        ['Performance & security hardening', 'Quality', 'Load tests at 1,500 samples per shift, penetration test, backup and recovery drill, retention settings.', ['All modules'], 'NFR-004–NFR-007, NFR-013', 'M'],
        ['Data migration from LDM', 'Migration', 'Patients, visits, results history, catalogue, prices, contracts, users; validation reports; dry runs.', ['Catalogue', 'Pricing', 'Results'], 'NFR-012', 'L'],
        ['User acceptance & training', 'Launch', 'Scripted UAT per persona (reception, technician, unit head, accounts, admin) in both languages; training material from the prototypes.', ['All phases'], '—', 'M'],
        ['Pilot go-live & hypercare', 'Launch', 'First tenant live with the site agent, printers and SMS; two weeks of hypercare; backlog triage for release 2.', ['Everything'], '—', 'M']
      ],
      exit: ['Pilot laboratory runs a full day on Sky LIS without falling back to LDM.', 'Open defects are triaged; release 2 backlog agreed.']
    }
  ],
  milestones: [
    ['M0', 'Week 6', 'Platform ready: tenants subscribe, log in, roles enforce'],
    ['M1', 'Week 14', 'Client Portal core: register → results → print → cash (existing pages enhanced)'],
    ['M2', 'Week 16', 'Operator portal core (existing pages enhanced)'],
    ['M3', 'Week 30', 'All new client pages: reception modes, samples, queues, finance'],
    ['M4', 'Week 33', 'All new operator pages: wallets, libraries, platform configuration'],
    ['M5', 'Week 43', 'New modules: analysers, PCR, auto-verification, QC, external APIs'],
    ['M6', 'Week 47', 'Pilot laboratory live']
  ],
  risks: [
    ['Scope of the undocumented LDM modules (QC, outsourcing, SMS, online transfer)', 'Decide in P0 with the product owner; QC is planned in P5, the others are out of the first release.'],
    ['Printing hardware variety (label printers, barcode rendering)', 'Printing service in P0 with built-in barcode rendering; test on the pilot laboratory\'s printers before P1 ends.'],
    ['Analyser protocols differ per vendor', 'Driver library in the site agent; start with the two analysers of the pilot laboratory; add drivers as separate work items.'],
    ['Rule language misuse (auto-verification releasing a wrong result)', 'Simulation mode and engine audit before enabling; roll out group by group; normal-range checker off by default.'],
    ['Master-data update policy between platform and tenants', 'Settle the policy in P2 (open question OQ-03) before publishing global changes.'],
    ['Arabic typography and RTL regressions', 'Both languages are part of the definition of done for every page; automated RTL overflow checks in CI.']
  ]
};
