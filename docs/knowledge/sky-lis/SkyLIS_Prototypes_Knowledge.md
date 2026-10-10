# Sky LIS — Prototype Knowledge Base

**Sources (repo root):**
- `Sky-LIS-Client-Portal-Interactive-Prototype-v1.html` — tenant (laboratory) portal, bilingual EN/AR with RTL, responsive.
- `Sky-LIS-Super-Admin-Production-Prototype-v2 (1).html` — National Technology's central operator portal, English only.

Both are self-contained interactive HTML prototypes with in-memory demo data (no backend). The Super Admin file
quotes an **SRS** ("The SRS requires at least one test per service…"), so a written SRS exists behind them and is
the authoritative scope; it was not supplied. Observed demo tenant: "Al Noor Laboratories", URL
`https://alnoor.skylis.demo`, currency EGP, branches Nasr City / Maadi / Alexandria.

## 1. Product concept

**Sky LIS** is a **cloud, subscription-based, multi-tenant** laboratory information system by National Technology.
Two portals:

| Portal | Who | Purpose |
|---|---|---|
| Super Admin Portal | National Technology staff (Super Admin, Operations Admin, Master Data Reviewer, Support) | Client (tenant) lifecycle, internal admin users & roles, **global master data** |
| Client Portal | A subscribed laboratory (per-tenant URL), its branches and staff | Daily lab workflow (registration → payment → worklist → results → report) plus tenant-level setup |

**Tenancy rule (stated in the client portal):** initial master records (clinical groups, services, tests, sample types,
units, drug types, antibiotics, organisms, preparations, comments) are **inherited from the Admin Portal when the
client is created; the client then maintains its own independent copy.**

## 2. Super Admin Portal — screens

### 2.1 Shell
Login (username/password only, show-password, prefilled demo); sidebar groups **Overview** (Home, Dashboard),
**Administration** (Roles, Admin Users, Clients), **Master Data** (Countries, Governorates, Clinical Groups, Services,
Tests, Service Global Sequence, Sample Types, Units of Measurement, Drug Types, Antibiotic Commercial Names,
Antibiotics, Organisms, Preparations, Comments); topbar with notifications & profile menu; footer © National Technology.
Every grid: search box, status filter (Active/Inactive), extra type filters (test type / service type / question
type), sortable columns, pagination, **Export Excel**, row Edit/Delete; delete requires confirmation and "must be
blocked when the record is referenced by dependent data".

### 2.2 Dashboard
Period selector (7 / 30 days, this month, 90 days). KPIs: Clients (128), Admin Users, Served Patients, Tests,
Registrations (across all tenants). Donut: active vs inactive clients. Bar chart: registrations by top labs.
Recent clients list.

### 2.3 Administration
| Screen | Fields / rules |
|---|---|
| Roles | Role Name (unique), Description, Status, **Screen Privileges** = on/off per screen (Dashboard, Roles, Admin Users, Clients, each master-data screen). Active role needs ≥1 screen. Role with assigned users cannot be deleted. |
| Admin Users | Full Name, User Name (4–30 chars; letters, digits, dots, underscores), Country Code (+20/+966/+962/+971), Mobile, Email, Role (active roles only), Password + Confirm, Status; **privileges inherited from role, no per-user override**. Main super admin cannot be deleted. |
| Clients | Client Name, Country, Governorate, Address, Phone (mobile; country code follows country), Medical License Number, Manager Name, Manager Mobile, Email, Status. Columns add Created date and **Source = Subscription / Manual**. Row actions: edit, **reset password**, delete; "activation available from row actions". |

### 2.4 Master data
| Screen | Fields / rules |
|---|---|
| Countries | Name, Code (EG), Dial Code (+…, numbers only), Currency + 3-letter Currency Code, Language, Time Zone, **Mobile Number Length** (validation), Flag image (jpg/png/svg), Status, Remarks |
| Governorates | Name, Country, District, Status, Remarks |
| Clinical Groups | Name, numeric Code (100, 200…), Report Name, Status |
| Services | Service Name, **LOINC Code (required)**, Service Header, Clinical Group, **Service Type = Test / Package**, Short Name, **Alias Names** (multi), Report Name, **Print Alone Service** toggle, Status, **Members grid**: a *Test*-type service holds tests; a *Package* holds other active services; each member has LOINC and a unique numeric **Order** (drag handle shown); ≥1 member required |
| Tests | Test Name, LOINC (required), Clinical Group, **Test Type = Numeric / Non-numeric / Optional List / Restricted List / Formula / Formatted Text / Culture**, Sample Type, Unit of Measurement, Report Name, Gender (All/Male/Female), Parent Service (multi), Status, Description; tabs "Test Information" / "Configuration". **Configuration by type:** Numeric → Age From/To, Normal Low/High, Critical Low/High, inclusive boundaries; Non-numeric → age range + expected text ("Negative"); Optional/Restricted List → value rows with Sequence, Value, Normal ✓, Critical ✓, Initial (default) ✓, Color; Formula → expression `([ALT] / [AST]) * 100` + Check Formula; Culture / Formatted Text → no extra configuration |
| Service Global Sequence | Inline-edited Sequence per service; unique and > 0; no add/delete |
| Sample Types | Name, Short Name (≤20), Code (≤20), Description, Status |
| Units of Measurement | Name (≤50), Status |
| Drug Types | Name, Status (Penicillin, Cephalosporin, Macrolide, Fluoroquinolone) |
| Antibiotic Commercial Names | Commercial Name, Drug Type(s), Status |
| Antibiotics | Code (≤20), Name, Abbreviation, Commercial Name(s), Status |
| Organisms | Code (≤20), Name, Status |
| Preparations | Code, Question, **Question Type = Text / Yes-No / Option List**, linked Tests (empty = all), Gender, Status, Options grid (≥2 when Option List) |
| Comments | Short Name (≤50), Comment text, Status |

## 3. Client Portal — screens

### 3.1 Public site, subscription, authentication
- **Public landing page** (hero, features: tenant-based secure access, Arabic/English RTL, responsive) with Login / Subscribe.
- **Subscription wizard** (4 steps): Lab Details (lab name, country, governorate, address, mobile, medical license, manager
  name, manager mobile, email) → Verify OTP (6 digits to manager mobile, resend timer) → Create Account (username,
  password with policy: ≥8 chars, upper, lower, number, special, match) → Done: **custom lab portal URL** sent by SMS.
- **Login** (username/password, remember me, show password) → **branch chooser** (only branches assigned to the user)
  → portal. **Forgot password**: username or mobile → OTP → set new credentials.

### 3.2 Shell
Sidebar groups: **Home**, **Workflow** (Registration, Search Registration, Patients, Search Payments, Query Service,
Worklist, Results), **Setup** (Roles, Users, Branches, Clinical Groups, Services, Tests, Service Global Sequence,
Sample Types, Units of Measurement, Commercial Drug Types, Antibiotics, Organisms, Preparations, Comments, Doctors,
Treasuries, Request Treasury, Treasury Requests, Payers, Pricelists), **Account** (Profile; menu also has Settings,
Logout). Topbar: current **branch pill**, **Wallet Balance (EGP 7,850)** pill, language toggle, profile. Mobile drawer.

### 3.3 Dashboard
KPIs today: Registrations, Payments, Orders, Patients, Wallet balance. Registrations last 7 days chart. Quick actions
(Registration, Results, Payments, Query Service). Recent activity feed (registration, payment, results verified, report
generated).

### 3.4 Workflow screens
| Screen | Content & behaviour |
|---|---|
| **Registration** | Cards: **Patient Data** (search existing patient link; Patient Name*, Gender*, Date of Birth* → Age (read-only), Mobile, ID Number, Doctor, **Payer***, **Contract***, Insurance ID, Email, Address, ☐ **Sample Collected Outside**) · **Services** (pick service + clinical group filter → table Service / LOINC / Fee / Discount / remove; duplicate add blocked) · **Preparation** (questions linked to selected services: fasting Yes/No, current medications text) · **Payment** (method cards Cash / Credit-Debit Card / POS Machine; Currency EGP; Paid Amount; Add Payment) · **Summary** (selected services count, Total, Discount, Total Paid, Remaining) → **Submit Registration** → success modal with **Accession No.** (2609020018, date-based) and **Patient No.** (P0004913) and print buttons **Receipt / Job Order / Sample Barcodes**. Translations also define Discount Type Absolute/Percent, Transaction Number. |
| **Search Registration** | Grid: Accession, Patient No., Patient Name, Branch, Date, Payer, Total, Status (Open/Completed); date range + search; row actions: edit (reopens registration), **Samples** dialog (sample id, type, status, print label), **Cancel Service** (choose services + cancellation reason; "related samples will be canceled"); Export. |
| **Patients** | Grid: Patient No., Name, Gender, Mobile, Visits, Last Visit; actions: **visit history** (accessions with dates/status), edit. |
| **Search Payments** | Grid: Accession, Patient, Total, Total Paid, Remaining, Payment Status (Pending/Paid); actions: **Make Payment** (remaining, paid amount, method) and **Refund** (amount + reason; rule: "allowed only for eligible pending services and cannot exceed the refundable amount"). |
| **Query Service** | Filters: Reg. Branch, Payer, Contract, Clinical Group, Service, LOINC → price grid (service, LOINC, group, type, price) with **Continue to Registration**. |
| **Worklist** | Grid: Accession, Patient, Service, Sample, Status (Pending / In Progress / Completed), Printed; status filter; per-row **Exclude** checkbox; **Print Worklist**. |
| **Results** | Toolbar: accession/patient search, status, branch. Left: accession list. Right: case card (patient, accession, service) with buttons **Previous Results**, **Attachments**, **Logs**; grid Test / Result (editable) / Unit / Flag (High / Normal badges, `H`/`N`) / Status (Saved / Reviewed); actions **Save**, **Review**, **Verify**; **View Report** modal (lab header, patient meta, group heading, test/result/unit table, Print). |

### 3.5 Setup screens (tenant)
| Screen | Fields |
|---|---|
| Roles | Role Name, Description, Status, **Module Access** toggles: Registration, Results, Patients, Payments, Setup, Worklist |
| Users | Full Name, Username, Mobile, Email, Role (Lab Administrator / Reception / Lab Technician), Designation, Status, **Module Access** (per user) |
| Branches | Branch Name, Code, Arabic Name, English Name, Country, Governorate, Phone, Email, Status |
| Clinical Groups · Services · Tests · Service Global Sequence · Sample Types · Units · Commercial Drug Types · Antibiotics · Organisms · Preparations · Comments | Same shape as Super Admin (inherited copy); Services add **Price**; Tests config shows Normal Low/High; Preparations link Tests |
| Doctors | Code, Name, Specialty, Occupation, Branches (multi), Status |
| Treasuries | Code, Name, Branch, Status (cash drawers per branch) |
| Request Treasury | Branch, Treasury, **Device ID** (auto, read-only) → request sent to manager |
| Treasury Requests | Requested By, Branch, Treasury, Device, Status (Pending/Approved/Rejected) with **Approve / Reject** |
| Payers | Payer Name, Search Key, Contracts (multi), Status (Cash, AXA Insurance, MetLife) |
| Pricelists | Name, Payer, Contract, Effective From / To, Status, **price grid** (one price per service) |
| Profile | Lab details (name, country read-only; manager name/mobile, email), Credentials (username, new password via OTP) |

### 3.6 Identifiers and statuses seen
- Patient No. `P0004912`; Accession `2609020017` (YYMMDD + sequence); Sample `S26090288`; Device `DEV-A81F2-92C1`.
- Registration: Open / Completed · Payment: Pending / Paid · Worklist: Pending / In Progress / Completed · Result row:
  Saved / Reviewed (Verified exists in translations) · Flags: High / Normal (Low implied) · Treasury request:
  Pending / Approved / Rejected · Master data: Active / Inactive.

## 4. Terminology map (Sky LIS ↔ LDM)

| Sky LIS | LDM equivalent |
|---|---|
| Client (tenant lab) | One LDM installation / licensed site |
| Branch | Branch |
| Clinical Group | Group / unit (Chemistry, Haematology…) |
| Service (type Test) | Orderable catalogue item: single test or profile |
| Service (type Package) | Mega profile / Panel (set of services) |
| Test (inside a service) | Single test / parameter |
| Print Alone Service | Mega profile printing as its own report |
| Service Header | Test `Header` banner |
| Service Global Sequence | Report/order `Order` (global instead of per profile) |
| Test Type Numeric / Non-numeric / Optional-Restricted List / Formula / Formatted Text / Culture | Age Dependant / Non_numeric Test (+ Test Conditions) / Formula / Text test / Culture |
| Normal Low/High, Critical Low/High | Test Ranges From/To, Low/High Panic |
| List value Normal / Initial | Test Conditions Normal / Exact |
| Payer / Contract | Rank / Relative |
| Pricelist (payer + contract, effective dates) | Rank Fees tier price list |
| Query Service | Financial ▸ Query |
| Preparations (questions) | Preparation questions / precautions (`Preparations` button, `Prep<n>`) |
| Comments master | Test Comments / unit comment library |
| Sample Collected Outside | Lab-to-Lab `Sample Collected Inside the Lab` (inverse) |
| Cancel Service + reason | Delete Patient (cancel visit/tests) |
| Refund | Return Money |
| Make Payment / Search Payments | Payment / Money |
| Treasury | Cash drawer closed via `Income ▸ Total Daily Income` per user |
| Previous Results / Attachments / Logs | Last Result / ATT / RL + SL |
| Review / Verify | Review / Verify |
| LOINC Code | Unique Services Codes ▸ LOINC |
| Roles module access | Groups And Privileges (far coarser) |
| Doctors | Setup ▸ General Data ▸ Doctors |
| Antibiotics / Organisms / Drug types | Setup ▸ Culture branch |

## 5. Observations about the prototypes themselves
- They are UI prototypes: no tube/sample generation logic, no status-track engine, no pricing engine; results grid has
  no reference-range column and no sample status; wallet balance semantics (prepaid SaaS credit? per-test billing?) are
  not defined anywhere.
- Demo data is illustrative, not consistent (e.g. "Liver Function Tests" package contains CBC and Urine Culture).
- Client-portal Users carry per-user module access, while Super Admin users explicitly cannot override their role.
- Client-portal registration has Payer/Contract but pricelists carry a single price per service: no patient/insurance
  split is modelled.
- The Super Admin test configuration is richer (age band, critical limits, list value colours, formula check) than the
  client-portal copy (normal low/high only).
