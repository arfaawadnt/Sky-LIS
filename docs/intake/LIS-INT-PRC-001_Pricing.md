# LIS-INT-PRC-001 — Pricing: payers, base price lists, contract pricing (full intake, Document 03 §11.2)

| Item | Value |
| --- | --- |
| Module | `pricing` (`PermissionCatalog.Pricing`), build slot `fin`, wave 1 (MODULES.md §2) |
| SRS pages | CP-32 Payers & contracts, CP-33 Base price lists, CP-34 Contract pricing, CP-12 Price query (API only; the page is Registration's, wave 2) |
| Business rules / workflow | BR-06 Contract sets the price, BR-07 Three-layer pricing; WF-10 Pricing maintenance and claims (pricing steps); BR-04 as cited by CP-34 (patient share + payer share per service) |
| Domain reference | LDM synthesis `docs/knowledge/ldm/LDM_Business_Knowledge.md` §2.4 (Rank/Relative), §7.1 (three-layer pricing: `Main Price List`, `Price Lists`/`Rank Fees`, `Client Price List`, `Client Copy`), §7.2 (`Query`), §7.3 (`Print Client Fees`) — LDM manual ch. 10 §10.2–10.7 |
| Status | Derived by the development team from the Business SRS (LIS-SRS-001) and the prototypes. **For review by the business owner**; every assumption is listed in §16. |
| Version | 1.1 — 2026-10-19 (change request LIS-CR-003: §19 payer contacts, price list tools, service prices) |
| Implemented in | `src/Platform.Domain/Pricing`, `src/Platform.Contracts/Pricing`, `src/Platform.Application/Pricing`, `src/Platform.Infrastructure/Persistence/Pricing`, `src/Platform.WebApi/Controllers/Pricing`, `frontend/src/app/features/pricing`, `frontend/src/app/core/api/pricing.contracts.ts`, `frontend/public/i18n/pricing.{en,ar}.json` |

## 1. Purpose and policy basis

Every service a laboratory sells is charged according to **who pays**: the patient alone (cash) or a payer (insurer,
syndicate, company, partner laboratory) that covers part of the bill under a contract. The price of a service on a
visit is split into a **patient share** (collected at the desk) and a **payer share** (claimed from the payer later).
A wrong payer or tier produces a wrong invoice (LDM §2.4), so the price is never typed at registration: it is looked
up from the contract (BR-06).

Prices are maintained in three layers (BR-07, LDM §7.1):

1. **Base price lists** hold one price per service (typically one list per year, plus special lists such as an
   institutional list). Next year's list is created by copying this year's with a factor or an added amount.
2. **Payers** (LDM "Rank") have **contract tiers** (LDM "Relative": employees, families, gold, silver …).
3. **Contract pricing** derives each tier's patient and payer shares from up to three base lists by formula
   (base price × patient factor, base price × payer factor); the first list that holds a service wins. Pressing
   **Apply** recomputes the tier; prices negotiated by hand (**excluded**) survive every Apply.

Contract prices change only on Apply (BR-07): editing a base list never silently re-prices a contract.

## 2. Scope and out of scope

In scope: payer master with tiers (CP-32), base price lists with their price lines, copy and apply (CP-33), contract
pricing per tier with formula, apply, negotiated prices, alert conditions, copy from another tier and the review report
(CP-34), the price quote API (CP-12 backend) and the read ports `IPriceLookup` / `IPayerLookup` that Registration,
Billing and Claims use in later waves, CSV export of the grids.

Out of scope: the CP-12 page (Registration, wave 2); visit re-pricing and money transfer (CP-25, Billing/Registration);
claims (CP-31, Reporting); the payer web-results portal itself and its password (only the portal username is kept,
§16 Q9); test costs (LDM `Tests Costs`); panel package prices (the Service's own price in the catalogue, CP-40);
discounts (Registration, `discountCapPercent`).

## 3. Glossary

| Business word | Platform word |
| --- | --- |
| Payer, contract body, LDM "Rank", جهة الدفع | `Payer` aggregate (`app.payers`) |
| Contract tier, LDM "Relative", فئة التعاقد | `ContractTier` entity of `Payer` (`app.contract_tiers`) |
| Cash payer, "Normal" | the tenant's default payer: `Payer.IsDefault = true`, type `Cash`, with the default tier `ContractTier.IsDefault = true` |
| Search key | `Payer.SearchKey`: short upper-case key typed at registration (`AXA`, `CASH`) |
| Base price list, LDM "Main Price List", قائمة الأسعار الأساسية | `BasePriceList` aggregate (`app.base_price_lists`) |
| Price line | `PriceLine` entity (`app.base_price_lines`): service, price, active flag |
| Contract pricing, LDM "Client Price List", تسعير التعاقدات | `ContractPricing` aggregate (`app.contract_pricings`), one per tier |
| Formula term (first/second/third list) | `PricingSource` entity (`app.contract_pricing_sources`): priority 1–3, base list, patient factor, payer factor |
| Derived price | `ContractPrice` entity (`app.contract_prices`): list price, patient share, payer share, source list |
| Exclude, negotiated price | `ContractPrice.IsExcluded` |
| Alert condition, LDM "Alert"/"Conditions" | `ContractPrice.AlertCondition` (text shown at ordering) |
| Client copy | "copy from another tier" (`CopyContractPricingCommand`) |
| Price query, LDM "Query" | `GET /api/v1/pricing/quote` and `IPriceLookup.QuoteAsync` |
| Service | catalogue service (`app.services`, module Catalogue A, same wave); referenced by id only |

## 4. Actors and permissions

The SRS actor is the **Accounts officer** (CP-32/33/34); the reception clerk reads prices through the quote.

| Permission key | Grants |
| --- | --- |
| `pricing.view` | read payers, tiers, base lists and their lines, contract pricing and its review report; price quotes; provision the default cash payer (idempotent, §16 Q1) |
| `pricing.create` | create payers, add tiers, create base lists, copy a base list into a new list |
| `pricing.edit` | edit payer details and tier names, edit a list's name/year, upsert price lines, set a tier's formula and effective dates, negotiate a price, clear an exclusion, set an alert, copy another tier's pricing |
| `pricing.approve` | **Apply**: recompute one tier (CP-34) or every tier derived from a base list (CP-33) |
| `pricing.manage` | activate / deactivate payers, tiers and base lists |
| `pricing.export` | CSV export of the price grids (affordance; the data is what `pricing.view` already shows, §16 Q17) |

Deny by default: every command and query carries exactly one `[RequirePermissionPolicy(PermissionCatalog.Pricing, …)]`.

The service typeahead of the price grids (adding a service to a list, negotiating a service a tier does not price) reads
Catalogue A's picker `GET /api/v1/catalogue/lookups/services`, which requires `catalogue.view`: an Accounts role that
edits prices also holds `catalogue.view`. Everything else the pricing pages show (service codes and names included)
comes from the pricing endpoints under `pricing.view`.

## 5. Aggregates

- **Payer** (root, `app.payers`): `Name` (`LocalizedText`), `SearchKey` (1–32, a letter or digit then `A-Z 0-9 . - _` — the SPA's `CODE_PATTERN` —, unique per tenant),
  `Type` (`Cash | Credit | Insurance | Lab`), `WebPortalEnabled`, `WebUsername` (≤ 64, required when enabled, unique per
  tenant), `IsActive`, `IsDefault` (exactly one per tenant: partial unique index), `RowVersion`.
  Children: **ContractTier** (`app.contract_tiers`, key `(tenant_id, id)`, FK `(tenant_id, payer_id)` → payers):
  `Name` (`LocalizedText`, unique per payer), `IsActive`, `IsDefault`.
- **BasePriceList** (root, `app.base_price_lists`): `Code` (1–16, same pattern, unique per tenant), `Name`,
  `Year` (2000–2100), `IsActive` (created inactive), `CopiedFromListId?`, `RowVersion`.
  Children: **PriceLine** (`app.base_price_lines`, FK `(tenant_id, price_list_id)`, unique `(tenant_id, price_list_id,
  service_id)`): `ServiceId` (reference to the catalogue by id, no FK this wave), `Price` numeric(12,2), `IsActive`.
- **ContractPricing** (root, `app.contract_pricings`): `PayerId`, `ContractTierId` (unique per tenant; FKs to payers
  and contract tiers), `EffectiveFrom` (date), `EffectiveTo?` (date), `AppliedAtUtc?`, `RowVersion`.
  Children: **PricingSource** (`app.contract_pricing_sources`): `Priority` 1–3, `BasePriceListId` (FK), `PatientFactor`,
  `PayerFactor` numeric(9,4). **ContractPrice** (`app.contract_prices`, unique `(tenant_id, contract_pricing_id,
  service_id)`): `ServiceId`, `ListPrice`, `PatientShare`, `PayerShare` numeric(12,2), `IsExcluded`, `AlertCondition?`
  (≤ 500), `SourceListId?`.

Other modules reference `app.payers(tenant_id, id)` and `app.contract_tiers(tenant_id, id)` by these names.

### Price derivation (BR-07)

```text
For a tier's formula [ (list₁, p₁, y₁), (list₂, p₂, y₂), (list₃, p₃, y₃) ] ordered by priority:
  only ACTIVE lists take part; within a list only ACTIVE lines count (an inactive line is "not held").
  For each service held by at least one of those lists, the FIRST list holding it gives base price b:
     list price    = b
     patient share = round(b × p, 2)      payer share = round(b × y, 2)      (half away from zero)
  Apply: rows not excluded are replaced by the derivation; rows no list holds any more are removed;
         excluded rows are never touched (negotiated prices survive, BR-07); AppliedAtUtc = now.
```

### Quote resolution (BR-06, CP-12)

```text
QuoteAsync(payerId?, tierId?, serviceIds):
  tier given            → that tier (if payerId is given it must be the tier's payer)
  payer only            → the payer's default tier, else its only active tier, else (HTTP) PRC-037
  neither               → the tenant's cash payer and default tier (provisioned lazily, §16 Q1)
  payer or tier inactive, or today outside [EffectiveFrom, EffectiveTo]  → every line priced = false
  tier with an applied ContractPricing → the stored ContractPrice row of each service (absent → priced = false)
  the cash default tier without an applied ContractPricing → the DEFAULT LIST at 100 % / 0 %:
     the active base list with the highest Year (ties: the most recently created); active lines only
  any other tier without an applied ContractPricing → priced = false (never guess a contract price)
```

## 6. State table

Reference-like aggregates: no workflow states beyond the active flag.

| State | Meaning | Editable fields | Frozen | Terminal | Visible to |
| --- | --- | --- | --- | --- | --- |
| Payer active | selectable at registration, quotable | name, search key, type (not for the default payer), web portal, tiers | no | no | `pricing.view` |
| Payer inactive | not selectable; quotes unpriced; history kept | same | no | no | `pricing.view` |
| Tier active / inactive | as the payer, per tier; the default tier cannot be deactivated | name | no | no | `pricing.view` |
| Base list inactive (initial) | not used by Apply, never the cash default list | name, year, lines | no | no | `pricing.view` |
| Base list active | used by Apply and as the cash default candidate | name, year, lines | no | no | `pricing.view` |
| Contract pricing not applied | formula saved, no derived price is quoted (except the cash default tier, which falls back to the default list) | formula, dates, negotiated prices | no | no | `pricing.view` |
| Contract pricing applied | derived prices quoted while in effect | everything; Apply again any time | no | no | `pricing.view` |

## 7. Transition table

| # | From → To | Action | Actor (permission) | Preconditions (codes) | SoD | Gate | Reason | Events | System? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P1 | — → Payer active | Create payer | `pricing.create` | name en/ar; search key valid (PRC-002) and unique (PRC-100); web username when the portal is enabled (PRC-003), valid (PRC-004), unique (PRC-102) | — | — | — | `PayerCreated` | no |
| P2 | Payer | Edit payer | `pricing.edit` | payer exists (PRC-404); as P1; the default payer stays `Cash` (PRC-020) | — | — | — | — (field audit) | no |
| P3 | active → inactive | Deactivate payer | `pricing.manage` | not the default payer (PRC-020) | — | — | — | `PayerDeactivated` | no |
| P4 | inactive → active | Reactivate payer | `pricing.manage` | — | — | — | — | `PayerReactivated` | no |
| P5 | — → Tier active | Add tier | `pricing.create` | payer exists (PRC-404); name unique within the payer (PRC-103) | — | — | — | `ContractTierAdded` | no |
| P6 | Tier | Rename tier | `pricing.edit` | tier exists (PRC-405); unique name (PRC-103) | — | — | — | — | no |
| P7 | Tier active ↔ inactive | Deactivate / reactivate tier | `pricing.manage` | not the default tier (PRC-021) | — | — | — | `ContractTierDeactivated` / `ContractTierReactivated` | no |
| P8 | — → Default cash payer | Ensure default payer | `pricing.view` | none; idempotent (returns the existing ids) | — | — | — | `PayerCreated` (IsDefault) + `ContractTierAdded` | yes (lazy) |
| L1 | — → List inactive | Create list | `pricing.create` | code valid (PRC-001), unique (PRC-101); year 2000–2100 (PRC-005) | — | — | — | `BasePriceListCreated` | no |
| L2 | List | Edit name / year | `pricing.edit` | list exists (PRC-406); PRC-005 | — | — | — | — | no |
| L3 | List | Upsert lines | `pricing.edit` | 1–2 000 lines, no duplicate service (PRC-016); price 0–99 999 999.99 with ≤ 2 decimals (PRC-006); every service exists in the catalogue (PRC-015) | — | — | — | `BasePriceListPricesChanged` (when something changed) | no |
| L4 | — → List inactive | Copy list | `pricing.create` | source exists (PRC-406); new code (PRC-001/101); multiply factor 0 < f ≤ 10 or add amount, rounding step ∈ {0.01, 0.25, 0.5, 1} (PRC-011); every result 0–99 999 999.99 (PRC-012) | — | — | — | `BasePriceListCreated` (CopiedFromListId) | no |
| L5 | inactive ↔ active | Activate / deactivate list | `pricing.manage` | — (idempotent) | — | — | — | `BasePriceListActivated` / `…Deactivated` | no |
| L6 | List | Apply list (CP-33) | `pricing.approve` | list exists (PRC-406) | — | G1 | — | `ContractPricingApplied` per derived tier | no |
| C1 | — / formula → formula | Set formula | `pricing.edit` | tier exists (PRC-405); 1–3 lists (PRC-008), distinct (PRC-009), existing (PRC-038); factors 0–10, ≤ 4 decimals (PRC-007); EffectiveTo ≥ EffectiveFrom (PRC-010) | — | — | — | `ContractFormulaSet` | no |
| C2 | formula → applied | Apply tier | `pricing.approve` | pricing exists (PRC-407); formula present (PRC-030); at least one referenced list active (PRC-031) | — | G1 | — | `ContractPricingApplied` | no |
| C3 | price → excluded | Negotiate price | `pricing.edit` | pricing exists (PRC-407); shares 0–99 999 999.99, ≤ 2 decimals (PRC-013); service exists in the catalogue (PRC-015) | — | — | — | `ContractPriceNegotiated` | no |
| C4 | excluded → derived | Clear exclusion (restore from formula) | `pricing.edit` | row exists (PRC-032) and is excluded (PRC-033) | — | — | — | `ContractPriceExclusionCleared` | no |
| C5 | price | Set / clear alert | `pricing.edit` | row exists (PRC-032); ≤ 500 characters (PRC-014) | — | — | — | — (field audit) | no |
| C6 | — / any → applied | Copy from another tier | `pricing.edit` | target tier exists (PRC-405); not itself (PRC-034); source has contract pricing (PRC-035); factor 0 < f ≤ 10 (PRC-007) | — | — | — | `ContractPricingCopied` | no |

## 8. Invariants

| Id | Statement | Enforced where | Code | Message |
| --- | --- | --- | --- | --- |
| INV-PRC-001 | A payer search key is 1–32 characters (a letter or digit, then `A-Z 0-9 . - _`), unique in the tenant | aggregate + handler + unique index `(tenant_id, search_key)` | PRC-002 / PRC-100 | "The search key is invalid." / "Another payer uses this search key." |
| INV-PRC-002 | A payer with the web-results portal enabled has a web username; usernames are unique in the tenant | aggregate + handler + partial unique index `(tenant_id, web_username)` | PRC-003 / PRC-102 | "A web username is required when the portal is enabled." |
| INV-PRC-003 | Each tenant has at most one default payer; it is of type Cash, active, with an active default tier | partial unique index `(tenant_id) WHERE is_default`; aggregate guards | PRC-020 / PRC-021 | "The default cash payer cannot be deactivated or change type." |
| INV-PRC-004 | Tier names are unique within their payer (English and Arabic, case-insensitive) | aggregate | PRC-103 | "The payer already has a tier with this name." |
| INV-PRC-005 | A base list code is 1–16 characters (same pattern), unique in the tenant | aggregate + handler + unique index | PRC-001 / PRC-101 | |
| INV-PRC-006 | A list holds a service at most once; prices are 0–99 999 999.99 with ≤ 2 decimals | aggregate + unique index `(tenant_id, price_list_id, service_id)` | PRC-016 / PRC-006 | |
| INV-PRC-007 | A tier has at most one contract pricing | handler (upsert) + unique index `(tenant_id, contract_tier_id)` | UNIQUE-409 | |
| INV-PRC-008 | A formula has 1–3 distinct lists with factors 0–10 | aggregate | PRC-008 / PRC-009 / PRC-007 | |
| INV-PRC-009 | A contract holds a service at most once; shares are 0–99 999 999.99 | aggregate + unique index `(tenant_id, contract_pricing_id, service_id)` | PRC-013 | |
| INV-PRC-010 | Apply never changes an excluded price (BR-07) | aggregate (`ContractPricing.Apply`) | — | |
| INV-PRC-011 | Contract prices change only by Apply, negotiation, restore or copy — never by editing a base list (BR-07) | design: lists and contracts are separate aggregates; no policy re-prices | — | |
| INV-PRC-012 | Effective dates: `EffectiveTo` ≥ `EffectiveFrom` | aggregate | PRC-010 | |

## 9. Segregation-of-duties rules

None. The SRS assigns all pricing maintenance to the Accounts officer and does not ask for a second person
(question Q12 in §16 asks whether Apply should require someone other than the formula's author).

## 10. Approval gates

| Item | G1 — Apply (C2, L6) |
| --- | --- |
| Permission | `pricing.approve` |
| Rule | single action by any holder of the permission; no second approver (Q12) |
| Recorded | `AppliedAtUtc`, the modifying user (audit stamps), the `ContractPricingApplied` event with counts, field changes of every derived row |
| After the gate | the tier's prices are quoted (while in effect); everything stays editable; Apply may run again |
| Correction path | edit the formula or the lists and Apply again; negotiate single prices |
| Step-up | none |
| Display | "Last applied" date on the contract pricing page and the review report |

## 11. Events and policies

```text
Event:    PayerCreated { PayerId, SearchKey, Type, IsDefault }               Raised by: P1, P8   Reacts: none yet
Event:    PayerDeactivated / PayerReactivated { PayerId, SearchKey }         Raised by: P3 / P4  Reacts: Registration hides the payer (read time, no policy)
Event:    ContractTierAdded / ContractTierDeactivated / ContractTierReactivated { PayerId, TierId }   Raised by: P5, P8 / P7
Event:    BasePriceListCreated { ListId, Code, Year, CopiedFromListId?, LineCount }   Raised by: L1, L4
Event:    BasePriceListPricesChanged { ListId, Code, Added, Changed }        Raised by: L3        Reacts: none (Apply is explicit, BR-07)
Event:    BasePriceListActivated / BasePriceListDeactivated { ListId, Code }  Raised by: L5
Event:    ContractFormulaSet { ContractPricingId, PayerId, ContractTierId, ListCount }   Raised by: C1
Event:    ContractPricingApplied { ContractPricingId, PayerId, ContractTierId, Priced, Added, Updated, Removed, ExcludedKept }
          Raised by: C2, L6        Reacts: none yet (Claims may snapshot later)
Event:    ContractPriceNegotiated { ContractPricingId, ContractTierId, ServiceId, PatientShare, PayerShare }   Raised by: C3
Event:    ContractPriceExclusionCleared { ContractPricingId, ContractTierId, ServiceId }   Raised by: C4
Event:    ContractPricingCopied { ContractPricingId, ContractTierId, SourceContractPricingId, SourceTierId, Factor }   Raised by: C6
```

No policy is needed in this module: prices are read at registration time through `IPriceLookup`; visits keep the
shares they were registered with (Registration stores them on the line), so later price changes never rewrite
history.

### Read ports for later waves (Application/Pricing)

```csharp
// Registration, Billing, Claims: price a set of services for a payer/tier. Total: never throws for unknown ids,
// it answers priced = false (the caller validates payer/tier through IPayerLookup first).
public interface IPriceLookup
{
    Task<IReadOnlyList<PriceQuoteLine>> QuoteAsync(Guid? payerId, Guid? contractTierId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken);
}
// PriceQuoteLine(Guid ServiceId, decimal ListPrice, decimal PatientShare, decimal PayerShare, string? AlertCondition, bool Priced)

public interface IPayerLookup
{
    Task<PayerSummary?> FindPayerAsync(Guid payerId, CancellationToken cancellationToken);             // names, search key, Type, IsActive, IsDefault
    Task<ContractTierSummary?> FindTierAsync(Guid contractTierId, CancellationToken cancellationToken); // names, PayerId, PayerType, IsActive, IsDefault
    Task<IReadOnlyList<ContractTierSummary>> ListTiersAsync(Guid payerId, CancellationToken cancellationToken);
    Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken);                       // null until provisioned (Q1)
}
```

## 12. Data classification and retention

| Field | Class | Note |
| --- | --- | --- |
| Payer names, search key, type, portal flag, web username | Operational, Audited | the web username is an identifier, not a credential |
| Tier names, flags | Operational, Audited | |
| Base list header, price lines | Commercial-confidential, Audited | every price change is a field change with user and time |
| Formula, effective dates, derived prices, exclusions, alerts | Commercial-confidential, Audited | Apply writes one field change per changed row |
| Frozen states | none | reference data; deactivation instead of deletion (SRS CP-32: a payer with visits cannot be deleted) |
| Retention | life of the tenant (prices explain historic invoices) | export: CSV by `pricing.export` |

## 13. Reference data and configuration

- The default cash payer ("Cash" / "نقدي", search key `CASH`, tier "Default" / "افتراضي") is created per tenant by
  `EnsureDefaultPayerCommand` (§16 Q1).
- Tenant setting read: `defaultCurrency` (SettingKeys.DefaultCurrency) — carried on quotes, contract pricing and the
  review report for display. No new setting key.
- Lists of values: payer type (Cash, Credit, Insurance, Lab — `نقدي، آجل، تأمين، معمل`), copy operation (Multiply,
  Add), rounding step (0.01, 0.25, 0.5, 1).

## 14. Registers, reports and exports

| Register | Filters | Columns | Who |
| --- | --- | --- | --- |
| Payers (CP-32) | search (name, search key), type, active flag, page | name, search key, type, tiers, web portal (username), default, active | `pricing.view` |
| Tiers of a payer | — | name en/ar, default, active | `pricing.view` |
| Base price lists (CP-33) | search (code, name), year, active flag, page | code, name, year, lines (active/total), derived contracts, copied from, active | `pricing.view` |
| Price lines of a list | client-side search by service code/name | service code, name (read from the catalogue with the lines), price, active | `pricing.view`; CSV `pricing.export` |
| Contract pricings | payer | payer, tier, effective from/to, last applied, priced/excluded counts | `pricing.view` |
| Contract pricing of a tier (CP-34) | client-side search | service code, name, source list, list price, patient share, payer share, total, excluded, alert | `pricing.view`; CSV `pricing.export` |
| Review report (CP-34, LDM `Print`) | tier | payer, tier, effective dates, formula, last applied, every derived price with exclusion and alert, printed by/at, signature boxes | `pricing.view` (printed in the browser) |
| Price quote (CP-12) | payer, tier, services | list price, patient share, payer share, alert, priced; totals; currency | `pricing.view` |

## 15. Notifications and escalations

None. Alert conditions are shown to the reception clerk at ordering (Registration reads `AlertCondition` from the quote).

## 16. Acceptance scenarios, open questions and assumptions

Scenario ids map to tests in `tests/Platform.Domain.UnitTests/Pricing`, `tests/Platform.IntegrationTests/PricingModuleTests.cs`
and `tests/Platform.WebApi.FunctionalTests/PricingFlowTests.cs`.

```gherkin
Scenario S-P1-ok: P1 Create payer — happy path
  Given a user holding pricing.create
  When they create "AXA Insurance" with search key " axa ", type Insurance and the portal enabled with username "axa.portal"
  Then the payer is active with search key AXA and PayerCreated is raised

Scenario S-P1-web: P1 — refused by INV-PRC-002
  When the portal is enabled without a username   Then PRC-003 (the API refuses the shape first with VALIDATION-400)

Scenario S-P1-dup: P1 — refused by INV-PRC-001
  Given a payer with search key AXA   When another payer is created with "axa"   Then PRC-100 (409)

Scenario S-P3-default: P3 — the default cash payer cannot be deactivated (PRC-020); its default tier neither (PRC-021)

Scenario S-P5-dup: P5 — two tiers named "Gold" on one payer are refused with PRC-103

Scenario S-P8-idem: P8 — ensuring the default payer twice creates one payer with one default tier and returns the same ids

Scenario S-L3-ok: L3 — upserting lines adds new services, changes existing prices, raises BasePriceListPricesChanged with the counts;
  an unchanged batch raises nothing
Scenario S-L3-price: L3 — a negative price or one with three decimals is refused with PRC-006
Scenario S-L3-svc: L3 — an id that is not a catalogue service is refused with PRC-015

Scenario S-L4-ok: L4 — copying PL26 × 1.1 rounded to 0.5 creates an inactive list whose prices are round(p × 1.1 / 0.5) × 0.5 with the same active flags
Scenario S-L4-neg: L4 — "+ -500" on a list holding a 100 price is refused with PRC-012

Scenario S-C1-size: C1 — a formula with 0 or 4 lists (PRC-008), a list twice (PRC-009), a factor of 11 (PRC-007) or EffectiveTo < EffectiveFrom (PRC-010) is refused

Scenario S-C2-ok: C2 Apply — first list wins, inactive lines fall through
  Given list A (active) holding S1 = 100 and S2 = 200 (inactive line), list B (active) holding S2 = 300 and S3 = 50
  And a formula [A ×0.1 / ×0.9, B ×0.2 / ×0.8]
  When the tier is applied
  Then S1 = (100, 10, 90, A), S2 = (300, 60, 240, B), S3 = (50, 10, 40, B) and ContractPricingApplied is raised
Scenario S-C2-excl: C2 — an excluded (negotiated) price is unchanged by Apply (BR-07)
Scenario S-C2-removed: C2 — a derived row whose service no list holds any more is removed
Scenario S-C2-inactive: C2 — when no referenced list is active, Apply is refused with PRC-031; without a formula PRC-030

Scenario S-C3-ok: C3 — negotiating S1 at 15 / 80 marks it excluded and raises ContractPriceNegotiated
Scenario S-C4-ok: C4 — clearing the exclusion restores the derived price from the formula immediately
Scenario S-C5-none: C5 — an alert on a service that the contract does not price is refused with PRC-032
Scenario S-C6-ok: C6 — copying a tier × 0.5 copies the formula with halved factors, the prices with halved shares, exclusions and alerts
Scenario S-C6-self: C6 — copying a tier onto itself is refused with PRC-034

Scenario S-Q-cash: quote without payer and tier
  Given no payer exists yet and an active list PL26 (2026) holds S1 = 180 and an inactive list PL27 (2027) holds S1 = 200
  When a reception clerk asks for S1 and an unknown service S9
  Then the cash payer and its default tier are provisioned, S1 is quoted 180 / 180 / 0 and S9 is priced = false
Scenario S-Q-tier: quote for an applied tier returns the stored shares and the alert condition
Scenario S-Q-dates: a tier whose EffectiveTo has passed quotes every service as priced = false
Scenario S-Q-mismatch: a tier of another payer is refused with PRC-036; a payer with two tiers and no tier with PRC-037

Scenario S-AUTHZ: any endpoint without the permission answers 403 AUTHZ-001 before the handler runs
Scenario S-TENANT: payers, tiers, lists, lines, contract pricings and prices of tenant X are invisible to tenant Y (query filter + FORCE RLS)
```

Open questions (assumed answers let the build proceed; confirm or change):

| # | Question | Assumed answer |
| --- | --- | --- |
| Q1 | How does the cash payer come to exist? | Every tenant has one default **Cash** payer with a **Default** tier. `EnsureDefaultPayerCommand` creates it idempotently (search key `CASH`, or `CASH-2`… if taken). It runs lazily: the quote endpoint calls it when neither payer nor tier is given, and the Payers page calls `POST /api/v1/pricing/payers/default` when it opens. Permission `pricing.view` (it takes no input and provisions a tenant invariant). Concurrent first calls are settled by the partial unique index; the loser reads the winner's ids. |
| Q2 | Which list prices cash patients? | The cash default tier uses its own applied contract pricing when one exists (so a lab can give cash a formula); otherwise the **default list** at 100 % / 0 %: the active base list with the highest year, ties broken by the most recent creation. |
| Q3 | Does an inactive price line hide the service or let a later list price it? | An inactive line counts as absent: the service falls through to the next list of the formula; in the cash default list it is unpriced. |
| Q4 | Is a new list active? | No: lists are created (and copied) inactive so that an empty list never becomes the cash default; activate when ready. |
| Q5 | What happens outside the effective dates? | The tier quotes nothing (priced = false) until the dates are extended; dates are compared with the server's UTC date (tenant time zone later). |
| Q6 | What happens to a derived price whose service left every list? | Apply removes it (with its alert); excluded prices always stay. |
| Q7 | What does "clear exclusion" do? | Restores the service from the formula immediately (LDM "Restore from main price list"); when no active list holds it the row is removed. |
| Q8 | What does "copy from another tier" copy? | The formula (factors × factor), the effective dates, every derived price (shares × factor), exclusions and alerts; the copy counts as applied now. |
| Q9 | Web-results portal credentials? | Only the username is stored (unique per tenant); the password and the portal belong to the later results-portal intake (credential material needs the envelope, 02 §3). |
| Q10 | Can payers be deleted? | No; deactivated (SRS CP-32: "a payer with visits cannot be deleted"). Lists and tiers likewise. |
| Q11 | Should Apply on a base list (CP-33) re-price every derived tier at once? | Yes: `POST /base-price-lists/{id}/apply` applies every contract pricing whose formula references the list, in one transaction. |
| Q12 | Does Apply need a second person (SoD) or step-up? | No. |
| Q13 | Rounding of shares? | To 0.01, half away from zero; copy rounding steps 0.01 / 0.25 / 0.5 / 1 (round to the nearest multiple, half away from zero). |
| Q14 | Bounds? | Prices and shares 0–99 999 999.99; factors 0–10 with four decimals; copy multiply factor 0 < f ≤ 10; 2 000 lines per upsert call; 200 services per quote; alert text ≤ 500. |
| Q15 | Does a quote check that the service is active in the catalogue? | No: the catalogue (and Registration's picker) hides inactive services; Pricing answers for any id. Unknown ids are priced = false. |
| Q16 | Versioned contract price history? | Not modelled: one contract pricing per tier; the audit trail keeps every change (who, when, before/after). Visits keep the shares they were registered with. |
| Q17 | Where is CSV produced? | In the browser, from data the user may already view (service codes and names come with the pricing DTOs); `pricing.export` gates the button. UTF-8 with BOM so Excel shows Arabic. |
| Q18 | Lab name on the printed review report? | No tenant profile exists yet; the header shows "Sky LIS" until the platform module exposes the laboratory's name. |
| Q19 | Payer types beyond Cash/Credit/Insurance/Lab? | None. A non-default payer may also be of type Cash (e.g. a staff cash scheme). |
| Q20 | Who sees prices? | Any holder of `pricing.view`; reception clerks need it for quotes (CP-12). |

## 17. Dependencies on other modules (same wave)

- **Catalogue A** (`cat`): `app.services` is read by raw SQL on its columns `id, tenant_id, code, name_en, name_ar, is_active`
  (never through the catalogue's entity types; no FK this wave): service ids are validated with
  `select id from app.services where tenant_id = @t and id = any(@ids)` (PRC-015), and the lines, contract prices and the
  review report carry the service code and names joined the same way (an unknown id carries nulls). The SPA's typeahead
  uses `GET /api/v1/catalogue/lookups/services?search=&pageSize=20` (`Paged<ServiceLookupItem>`: `code`, `nameEn`, `nameAr`;
  active services only, at most 50 per page, `catalogue.view`).
- **Billing / Catalogue B** (same wave) already read `app.payers (id, tenant_id, name_en, name_ar)` and
  `app.contract_tiers (id, tenant_id, payer_id)` by raw SQL; those names and columns are stable.
- **Registration** (wave 2) consumes `IPriceLookup`, `IPayerLookup` and the quote endpoint; `VisitRegistered.PayerId?` /
  `ContractTierId?` reference `app.payers` / `app.contract_tiers`.

## 18. API surface and screens

| Method and path | Request | Permission |
| --- | --- | --- |
| `GET /api/v1/pricing/payers?search&type&isActive&page&pageSize` | ListPayersQuery → `Paged<PayerListItem>` (with tiers) | view |
| `GET /api/v1/pricing/payers/{id}` · `GET …/{id}/tiers` | GetPayerQuery | view |
| `POST /api/v1/pricing/payers` · `PUT …/{id}` | Create / UpdatePayerCommand | create / edit |
| `POST …/payers/{id}/deactivate` · `…/reactivate` | De/ReactivatePayerCommand | manage |
| `POST /api/v1/pricing/payers/default` | EnsureDefaultPayerCommand → `DefaultPayerIds` | view |
| `GET /api/v1/pricing/payers/tiers?activeOnly` | ListTierOptionsQuery → `TierOption[]` | view |
| `POST …/payers/{id}/tiers` · `PUT …/tiers/{tierId}` · `POST …/tiers/{tierId}/deactivate|reactivate` | Add / Rename / De-, ReactivateContractTierCommand | create / edit / manage |
| `GET /api/v1/pricing/base-price-lists?search&year&isActive&page&pageSize` · `GET …/{id}` | List / GetBasePriceListQuery | view |
| `POST /api/v1/pricing/base-price-lists` · `PUT …/{id}` | Create / UpdateBasePriceListCommand | create / edit |
| `GET …/{id}/lines` · `PUT …/{id}/lines` | GetPriceLinesQuery / UpsertPriceLinesCommand → `{added, changed}` | view / edit |
| `POST …/{id}/copy` | CopyBasePriceListCommand → 201 | create |
| `POST …/{id}/activate` · `…/deactivate` | Activate / DeactivateBasePriceListCommand | manage |
| `POST …/{id}/apply` | ApplyBasePriceListCommand → `{contractsApplied, pricesChanged}` | approve |
| `GET /api/v1/pricing/contract-pricing?payerId` | ListContractPricingsQuery | view |
| `GET /api/v1/pricing/contract-pricing/{tierId}` · `PUT …/{tierId}` | GetContractPricingQuery / SetContractFormulaCommand | view / edit |
| `POST …/{tierId}/apply` | ApplyContractPricingCommand → `ContractApplyResult` | approve |
| `PUT …/{tierId}/prices/{serviceId}` · `DELETE …/prices/{serviceId}/exclusion` · `PUT …/prices/{serviceId}/alert` | Negotiate / ClearPriceExclusion / SetPriceAlertCommand | edit |
| `POST …/{tierId}/copy-from/{otherTierId}` | CopyContractPricingCommand (`{factor?}`) | edit |
| `GET …/{tierId}/review` | GetContractPricingReviewQuery → `ContractPricingReviewDocument` | view |
| `GET /api/v1/pricing/quote?payerId&tierId&serviceIds=a&serviceIds=b` | EnsureDefaultPayerCommand (only without payer and tier) + QuotePricesQuery → `PriceQuote` | view |

Screens (`frontend/src/app/features/pricing`, routes under `/pricing`): `payers` (CP-32: shared master-data page + tiers
drawer + "contract pricing" row action), `base-price-lists` (CP-33 register + copy dialog), `base-price-lists/:listId`
(CP-33 price grid: inline price and active flag, typeahead with Enter, Ctrl+S, apply, copy, activation, CSV, leave guard),
`contract-pricing` and `contract-pricing/:tierId` (CP-34: payer/tier picker, formula, Apply, derived grid with negotiate /
restore / alert, copy from tier, printable review report, CSV).

## 19. Change request 2026-10-19 — LIS-CR-003 package P-PRC (C19 payer contacts, C21 price list tools, C22 service prices)

Source: `docs/build/CR-2026-10-19_Prototype-Gaps.md` §7 (prototype gaps found when the client v3 / admin v4.1 prototypes were compared with
the application as installed, LIS-DS-001). Compatibility: the installed server runs 0.14.0 with real data; every new column is nullable,
every existing request shape keeps working (the new fields are optional), and no existing endpoint changed its answer except for the
added fields of `PayerListItem`.

### 19.1 What changes

| Item | Change |
| --- | --- |
| C19 Payer contacts | `Payer` gains the office contacts `Phone1`, `Phone2`, `WhatsApp` (optional, ≤ 32 characters: digits, spaces, `+ - ( )`, at least one digit; inner whitespace collapsed) and `Email` (optional, ≤ 254, basic shape: one `@`, a dotted domain, no spaces), held in the value `PayerContacts` and cleaned on create and update. Columns `phone1`, `phone2`, `whatsapp`, `email` on `app.payers` (nullable; existing rows keep NULL). `Create/UpdatePayerRequest` and `PayerListItem`/detail carry them; the payers page form has a *Contact* block (phones and e-mail with the server's patterns as hints) and the CSV export four more columns. A change of the contacts alone is a `PayerUpdated` event (audit trail). |
| C21 Download Excel | The SPA builds an `.xlsx` with `exceljs` (loaded on demand) holding the list's lines: `Code`, `Name`, `Current price`, `New price` (empty), sheet named after the list code. Permission `pricing.export`, like the CSV. |
| C21 Upload Excel | The SPA parses the chosen `.xlsx` (first sheet; the header row is the first of the first ten rows holding a `Code` cell and a `New price` or `Price` cell, matched case-insensitively, Arabic headers accepted) into rows `{ row, serviceCode, price }` and posts them as a **dry run** first; the dialog lists the refused rows (sheet row, code, reason) and offers *Apply* only when every row passed. `ImportPriceLinesCommand(listId, rows, dryRun)` [`pricing.edit`] checks every row — `PRC-041` no code, `PRC-015` unknown code, `PRC-006` price missing or invalid, `PRC-016` code repeated — and answers `ImportPriceLinesResult(accepted, added, changed, applied, errors[])`; with any error nothing is written (all-or-nothing); without errors and `dryRun = false` the rows are upserted through `BasePriceList.UpsertLines` in one save (existing lines keep their active flag, new lines are active). Codes compare trimmed and upper-case. At most 2 000 rows per call. |
| C21 Bulk adjust | `AdjustBasePriceListCommand(listId, mode Amount|Percent, value, rounding None|Whole|Nearest5|Nearest10, serviceIds?, preview)` [`pricing.edit`]: `PriceMath.Adjust` prices every line (or the scope) `+ amount` or `× (1 + percent / 100)`, rounds as asked (half away from zero), holds a negative result at 0 and reports it (`clamped`); the answer carries the rows `{ serviceId, code, name, oldPrice, newPrice, clamped }`, `changed`, `clamped`, `applied`. `preview = true` computes without writing; otherwise the changed rows are upserted in one save. Refusals: `PRC-044` adjustment outside the bounds (percent −100…1 000, amount within ±99 999 999.99, two decimals), `PRC-045` a scope id the list does not hold, `PRC-046` a result above the maximum price (refused, never capped silently). The dialog offers mode, value, rounding, scope (every line or the rows of the grid's filter), *Preview* table, *Apply*. |
| C21 Print | `ui/documents/price-list-document.component.ts` on `shared/print` (A4 portrait): list code, name, year, status, every stored line (code, name, price, active at ordering) in code order, line count, printed by / at. Unsaved edits are not printed. |
| C22 Service prices | `GET /api/v1/pricing/service-prices/{serviceId}` [`pricing.view`] → `ServicePricesView(serviceId, code, nameEn, nameAr, type, isActive, currency, basePrices[{ listId, listCode, listNameEn/Ar, year, listActive, price, lineActive }], contractPrices[{ tierId, payerId, payerNameEn/Ar, tierNameEn/Ar, listPrice, patientPrice, payerPrice, isExcluded, sourceListCode, tierActive, payerActive, inEffect }])` through the read port `IServicePricesReader` (the service by raw SQL on `app.services`, the module's own tables by EF; `PRC-408` when the service is not the tenant's). Page `/pricing/service-prices` (CP-33b): the catalogue picker, the two tables, inline edit of a base price (`PUT …/base-price-lists/{id}/lines` with one line, `pricing.edit`, Enter saves, Escape discards), a link to each list's editor; the chosen service travels in `?serviceId=`. Nav entry client · Finance · `pricing.view`. |

The editor's tools refuse to open while the grid holds unsaved prices (save or discard first), so a dry run never races a pending save.
None of the tools re-prices a contract: Apply to contracts stays the explicit, confirmed action (BR-07).

### 19.2 New codes

| Code | Meaning | Fault |
| --- | --- | --- |
| `PRC-017` | Phone / WhatsApp number malformed (digits, spaces, `+ - ( )`, ≤ 32, at least one digit) | Validation |
| `PRC-018` | E-mail without the basic shape or longer than 254 | Validation |
| `PRC-041` | Imported row without a service code | row error of the import answer |
| `PRC-044` | Adjustment outside the bounds | Validation |
| `PRC-045` | A scope id the list does not hold | Validation |
| `PRC-046` | Adjusted price above 99 999 999.99 | Validation |
| `PRC-408` | Service not found (service prices) | NotFound |

Row errors of the import reuse `PRC-015` (unknown code), `PRC-006` (price missing or invalid) and `PRC-016` (code repeated).

### 19.3 New endpoints

| Method and path | Request | Permission |
| --- | --- | --- |
| `POST /api/v1/pricing/base-price-lists/{id}/import` | `ImportPriceLinesRequest(rows[{ row, serviceCode, price }], dryRun)` → `ImportPriceLinesResult` | edit |
| `POST /api/v1/pricing/base-price-lists/{id}/adjust` | `AdjustBasePriceListRequest(mode, value, rounding, serviceIds?, preview)` → `AdjustBasePriceListResult` | edit |
| `GET /api/v1/pricing/service-prices/{serviceId}` | — → `ServicePricesView` | view |

`POST /api/v1/pricing/payers` and `PUT …/{id}` take the optional `phone1`, `phone2`, `whatsApp`, `email`.

### 19.4 Open questions and the assumptions built on

| # | Question | Assumption built on |
| --- | --- | --- |
| Q23 | Should the phone numbers be normalised to E.164 like user mobiles? | No: payer office numbers are free-form (extensions, local formats); they are stored as typed after cleaning, and never dialled by the system. |
| Q24 | Should the Excel upload also take the active flag per line? | No: the sheet carries the price only; existing lines keep their flag, new lines are active (the grid changes flags). |
| Q25 | Should a bulk adjustment that would exceed the maximum price cap the price? | No: it is refused (`PRC-046`) so a wrong percentage is noticed; the user narrows the scope or the value. |
| Q26 | Should the service prices page let the user edit a contract price (negotiate) inline? | No: contract pricing is Release 2; the page shows the derived prices read-only and links to the tier when CP-34 is in scope. |
| Q27 | Which laboratory name goes on the printed price list? | The same texts as the contract review document (`pricing.review.labName*`) until the print kit takes the laboratory brand. |
