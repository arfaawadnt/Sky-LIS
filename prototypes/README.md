# Sky LIS — Interactive Prototypes

Four self-contained HTML prototypes (no server, no external dependencies, logo embedded), built from one source tree in two scopes:

| File | Portal | Scope |
|---|---|---|
| `dist/Sky-LIS-Client-Portal-Prototype-v2.html` | Client Portal (tenant laboratory) | Full target scope: all enhanced pages, new pages and new modules |
| `dist/Sky-LIS-Super-Admin-Portal-Prototype-v3.html` | Super Admin Portal (platform operator) | Full target scope |
| `dist/Sky-LIS-Client-Portal-Prototype-v2.1-Release1.html` | Client Portal | **Release 1**: cross-cutting enhancements + the pages that existed in v1 with their enhancements only; registration without Location/Room and with the payment section before saving (as in v1); no new pages or modules |
| `dist/Sky-LIS-Super-Admin-Portal-Prototype-v3.1-Release1.html` | Super Admin Portal | **Release 1**: all enhanced pages plus the new pages and modules, except the integration library (analyser models, PCR kits, code systems, rule templates) that only serves the deferred client modules |

The scope is a build-time switch (`window.SCOPE = "full" | "r1"` injected by `build.js`); the release-1 files still contain the shared code but expose only the scoped navigation and screens.

Open either file directly in a browser. Demo credentials are prefilled (`lab.admin` / `Sky@2026` and `admin` / `Sky@2026`).
Both portals switch between English and Arabic (true RTL) from the top bar. Data is in-memory and resets on reload.

## What the v2 / v3 prototypes add over v1 / v2
They implement the "missing versus LDM" items of `docs/knowledge/sky-lis/SkyLIS_vs_LDM_Gap_Analysis.md` and the
requirements in `docs/requirements/LIS_Requirements_Specification.html`:

- **Reception**: duplicate-patient warning, title → gender, age ↔ date of birth, patient condition, hospital location
  fields, reception comments and VIP/urgent/biohazard flags, diagnosis-driven ordering, service search by name /
  shortcut number / shortcut set, precautions, Reserved (deferred) services, frequency check, contract alerts, auto-add
  rules, discount cap and reason, collector, tube generation by group + sample type + condition, labels / job order /
  receipt, home reservation → conversion, batch registration, lab-to-lab registration, price quote, patient documents and merge.
- **Samples**: barcode collection and receipt (the Received gate), rejection with reasons, unreject or replacement with
  a new barcode, sample tracking with stage durations and test round times.
- **Laboratory**: worklist by collection date with turnaround-percentage colours and counters; results by accession and
  by sample with resolved reference ranges (sex / age / condition), flags and panic values, formula auto-calculation,
  three comment kinds and the comment library, result and status logs, previous results and delta-check interception,
  review → verify → undo, preview and print rules (verified only), culture entry, formatted-text reports with templates;
  status work queue with "Not X" logic, nine colour states, bulk review/verify, analyser closing presets, print list
  numbers and the abbreviated worksheet; QC placeholder.
- **Finance**: payers with contract tiers, base price lists with copy-by-formula, contract pricing with patient/payer
  factors, exclusions and alert conditions, partial payments with treasury/device, refunds with rules, contract and
  line transfers, cancellations with reasons, batch payment, device-bound treasury requests, daily cash closing, claims
  in several layouts, sixteen financial reports.
- **Setup**: roles with Q/I/U/D per screen, special rights, result rights per clinical group and branch; users with
  lock, expiry, signature image and overrides; tests with range bands, list values, formulas, turnaround, frequency,
  delta, labels and prefix; services with members, order, sums and print-alone; sample conditions, patient conditions,
  reasons, diagnoses, sending labs, shortcut sets, test rules, report templates, additional services, tenant parameters.
- **Integration & automation**: analysers (modes, serial/TCP, flags), test and sample code mapping with Auto Send,
  conversion tables, site agent with decrypted log, PCR kits with thresholds, verdict rules and run simulation;
  auto-verification settings with the checker set and a medical-rules editor with keypad, formula condition and validation.
- **Super Admin**: client wallets and plans, sessions and audit log, extended global master data (sample conditions,
  patient conditions, reasons, diagnoses, additional services, report templates, rules, shortcut sets, sensitivities),
  analyser model library, PCR kit library, code systems, rule templates, notification templates and platform parameters.

## Source layout and build
```
prototypes/src/
  brand/sky-lis-logo.webp   logo (embedded as a data URI at build time)
  core.css                  design system — colours derived from the logo (blue → violet gradient, navy, specimen red)
  core.js                   framework: i18n, shell, navigation, grid, forms, modals, generic CRUD
  i18n.js                   English / Arabic dictionary
  data.js                   demo data (tenant + platform)
  screens.shared.js         domain helpers (pricing, ranges, flags) and shared editors (tests, services, roles…)
  app.client.1-4.js         Client Portal screens
  app.admin.js              Super Admin Portal screens
  build.js                  assembles dist/*.html
```
Rebuild after editing any source:
```bash
node prototypes/src/build.js
```
Preview locally with the `prototypes-preview` launch configuration (port 5151).
