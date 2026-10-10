# Building a Sky LIS module — the pattern every module agent follows (LIS-BP-002)

You are building one bounded context of Sky LIS end to end (domain → application → infrastructure → API → Angular
feature → tests). The architecture pack in `prompt/` is the constitution; the Business SRS (`docs/srs/src/srs.data.js`)
is the business content; `docs/build/MODULES.md` says which SRS pages are yours and which decisions are fixed.
The **Treasury module** is the reference implementation: read it before writing anything.

## 0. Read first (in this order)

1. `CLAUDE.md` §1 (isolation) and §5 (backend solution, frontend, dev tooling).
2. `docs/build/MODULES.md` — your module's row, the fixed decisions, the shared event contracts.
3. `prompt/01-SYSTEM-ARCHITECTURE-BLUEPRINT.md` §3.3 (module conventions), §4.2 (tenant table checklist), §6 (frontend).
4. `prompt/03-BUSINESS-INTAKE-AND-DDD-GUIDE.md` §3–§10 (aggregates, states, transitions, invariants, SoD, gates, events).
5. `prompt/04-ARCHITECTURAL-PITFALLS-AND-AVOIDANCE-GUIDE.md` (skim the pitfall codes you will cite).
6. The reference module, every file: `src/Platform.Domain/Treasury/`, `src/Platform.Contracts/Treasury/`,
   `src/Platform.Application/Treasury/`, `src/Platform.Infrastructure/Persistence/Treasury/`,
   `src/Platform.WebApi/Controllers/Treasury/`, `frontend/src/app/features/treasury/`,
   `frontend/src/app/core/api/treasury.contracts.ts`, `tests/**/Treasury*` and `docs/intake/LIS-INT-TRS-001_Treasury-Access.md`.
7. Your SRS pages: `grep -n "CP-xx\|SA-xx" docs/srs/src/srs.data.js` (purpose, data, actions, rules, dependencies) and the
   workflows (WF-xx) and business rules (BR-xx) that cite them. The LDM synthesis `docs/knowledge/ldm/LDM_Business_Knowledge.md`
   explains the laboratory domain when a rule is unclear.

## 1. Work in your own slot

```bash
source scripts/slot-env.sh <slot>      # from E:\LIS, Git Bash; sets LisBuildSlot and the lis_<slot> test database
dotnet build Platform.sln -c Release
dotnet test tests/Platform.Domain.UnitTests -c Release --no-build
dotnet test tests/Platform.ArchitectureTests -c Release --no-build
dotnet test tests/Platform.Application.UnitTests -c Release --no-build
# database suites only after the integrator has generated the wave migration (you will be told):
dotnet test tests/Platform.IntegrationTests -c Release --no-build
dotnet test tests/Platform.WebApi.FunctionalTests -c Release --no-build
cd frontend && npx ng lint && node scripts/check-boundaries.mjs && npx ng test --watch=false
```

Build output goes to `.local/build/<slot>/`; never run `dotnet build` without the slot environment, never run
`dotnet ef`, never start the API or the dev server (the integrator does the browser verification). The database
suites need the migration, so write them first and run them when told. Use `-v q` and grep summaries: keep output small.

## 2. Files you own and files you never touch

You create files only under your module folders:

```text
src/Platform.Domain/<Module>/                       aggregates, value objects, enums, <Module>Codes.cs, <Module>Events.cs
src/Platform.Contracts/<Module>/                    request/response DTOs (records), list items, details
src/Platform.Application/<Module>/                  I<Aggregate>Repository.cs, read ports, Commands/, Queries/, Policies/
src/Platform.Infrastructure/Persistence/<Module>/   EF configurations, repositories, read-port adapters, projections
src/Platform.WebApi/Controllers/<Module>/           thin controllers (ISender only)
frontend/src/app/core/api/<module>.contracts.ts     mirror of the Contracts (hand-written until the generator lands)
frontend/src/app/features/<module>/                 routes, data/, state/, pages/, ui/
frontend/public/i18n/<module>.{en,ar}.json
tests/Platform.Domain.UnitTests/<Module>/           tests/Platform.IntegrationTests/<Module>*.cs
tests/Platform.WebApi.FunctionalTests/<Module>*.cs  frontend specs next to the code
docs/intake/LIS-INT-<MOD>-001_<Name>.md             the full 16-section intake (03 §11.2) you derive from the SRS
```

**Never edit** (the integrator owns them; ask in your report instead): `PermissionCatalog.cs`, `AppDbContext.cs`,
`InfrastructureServiceRegistration.cs`, `Persistence/Migrations/**`, `deploy/postgres/**`, `Program.cs`, `PipelineOrder.cs`,
`frontend/src/app/app.routes.ts`, `shell/**`, `public/i18n/shell.*.json`, `Directory.*.props`, `angular.json`, `package.json`,
`CLAUDE.md`, anything of another module, the test support folders (`tests/**/Support/`) and the existing gates. If a
shared primitive is genuinely missing (SharedKernel, Contracts/Common, UI kit), say so in the report with the exact
code you need; do not fork a local copy.

Repositories, read-port adapters and services register **by convention**: a concrete class under
`Platform.Infrastructure.Persistence.*` (or `.Services.*`) implementing an interface declared in `Platform.Application`
is a scoped adapter. A placeholder adapter for a port whose real adapter arrives with a later module (a NoOp, or a raw-SQL stand-in) carries
`[FallbackAdapter]` (namespace `Platform.Infrastructure`): the convention registers it only when no ordinary adapter
implements the port, so the real one replaces it with no registration change. Entity configurations are discovered by `ApplyConfigurationsFromAssembly`. Use
`Context.Set<T>()` in repositories; do not add `DbSet` properties.

## 3. Backend rules (each has a gate)

- **Aggregates** (03 §3–§5): `sealed class X : AggregateRoot, ITenantScoped` with private setters, a static factory,
  transition methods that guard state (`InvalidStateTransitionException` / `DomainException` with a code), SoD via
  `EnsureActorIsNot(anchor, actor, code)` inside the method, events raised at the end of the transition. Entities
  reference other aggregates by id. Bilingual names are `LocalizedText` (owned → `name_en`/`name_ar`). Codes are
  `<MOD>-nnn` constants in `<Module>Codes.cs` (001–099 validation/state, 100–199 conflicts, 404/405 not found,
  `SOD-<MOD>-nnn`). Time comes from `IClock`, ids from `Guid.CreateVersion7()`, never `DateTime.Now`/`Guid.NewGuid()`.
- **Child entities**: owned collections configured with `OwnsMany` (small, value-like: price lines, range bands, list
  values, members) or separate `Entity` types keyed `(tenant_id, id)` with a composite FK to the root when they are
  large or queried on their own. Both stay inside the aggregate's consistency boundary.
- **Commands and queries** (01 §3.3): one file per use case holding the record, its FluentValidation validator (shape
  only: required, lengths, ranges, enum names) and the handler. Every request carries exactly one
  `[RequirePermissionPolicy(PermissionCatalog.<Module>, PermissionAction.<X>)]` (or `[RequireAuthenticatedActor]` for
  self-service). Handlers: load → call the aggregate → `SaveChangesAsync` **once** (gate `TransitionAtomicityTests`);
  queries never commit. Not found → `DomainException(<MOD>-404, …, DomainFault.NotFound)`; duplicates checked in the
  handler before the aggregate and refused with a 1xx conflict code.
- **Repositories**: `sealed class XRepository(AppDbContext context) : Repository<X>(context), IXRepository`; list queries
  project to DTOs with `AsNoTracking()`, paging through `PageRequest`/`Paged<T>` (`Platform.Contracts.Common.Paging`),
  stable ordering, `Status.ToString()` for enums. Lookups for other modules go through read ports (MODULES.md §1).
- **EF configuration**: `ToTable("<snake_plural>", AppDbContext.AppSchema)`; the conventions give `(tenant_id, id)`
  keys, the query filter and xmin concurrency; you add lengths, required, indexes `(TenantId, …)`, unique indexes
  `(TenantId, Code)`, FKs with `OnDelete(DeleteBehavior.Restrict)`, and explicit `HasConstraintName`/`HasDatabaseName`
  when the conventional name would exceed 63 characters (gate). Platform-wide tables (no tenant) go to the `identity`
  schema without `ITenantScoped`. Enum columns are strings (the convention adds the CHECK constraint).
- **Controllers**: `[ApiController] [Route("api/v1/<kebab-plural>")]`, `[Authorize]` as the existing ones, actions
  `Task<ActionResult<T>>`, `Created($"/api/v1/…/{id}", created)` on POST create, `NoContent()` on transitions,
  `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]` for optional bodies, query parameters for filters. Names
  must not clash with `ControllerBase` members (`Request`, `Response`, `User`).
- **Documents**: a `GET …/{id}/documents/<kind>` query returning the typed DTO of the document (MODULES.md §1).
- **Events and policies**: events carry ids and the few values a reader needs. A policy is
  `sealed class XPolicy : IDomainEventHandler<TEvent>` in `Application/<Module>/Policies/`, idempotent on a natural key,
  registered by convention, and it never sends commands to another module.
- **Extra SQL** (grants on `saas` tables, append-only triggers, idempotent seed rows such as countries or reasons):
  put it in `src/Platform.Infrastructure/Persistence/<Module>/<Module>.migration.sql` (plain SQL, idempotent: `ON CONFLICT DO
  NOTHING`, `CREATE OR REPLACE`, `IF NOT EXISTS`). The integrator appends it to the wave migration; you never create migrations.
- **Platform-level tables** (no tenant: clients, plans, countries, libraries) go to schema `saas` (`AppDbContext.SaasSchema`,
  add the constant if missing and report it) without `ITenantScoped`; `lis_app` has no default privileges there, so the
  module's `.migration.sql` grants `SELECT, INSERT, UPDATE` on each table.
- **Same-wave lookups**: an adapter that reads a table of a module built in the same wave must not reference that
  module's entity types (they may not compile yet); use `Context.Database.SqlQueryRaw<T>(...)` against the agreed table
  name and report the dependency. Later-wave adapters may use `Context.Set<T>()` of earlier modules.
- **Settings**: read tenant parameters through `ITenantSettings` with the constants in `SettingKeys`; add your keys there
  only if the foundation has not (report it otherwise).

## 4. Frontend rules

- Angular 22 zoneless, signals only, `ChangeDetectionStrategy.OnPush`, standalone components, typed reactive forms
  (`nonNullable`), no `any`/`$any()`, no web storage, no `subscribe()` in components, `core/` and `shared/` never
  import `features/`, features never import each other (`scripts/check-boundaries.mjs`).
- Structure: `features/<module>/<module>.routes.ts` exporting `<MODULE>_ROUTES` (children of the lazy route the
  integrator registers), `data/<module>.api.ts` (HttpClient + `HttpParams`, promises via `firstValueFrom`),
  `state/<thing>.facade.ts` (`resource()` for reads, `mutation()` from `core/state/mutation.ts` for writes, provided at the
  route), `pages/*.page.ts|html|css` (smart pages), `ui/` (dumb components of the feature).
- Use the UI kit in `shared/ui` (page header, data table, drawer form, form field, toolbar, badge, paginator, confirm,
  tabs, empty state, master-data page) and `shared/print` for documents. Do not restyle them; add module CSS only for
  module-specific layout. Design tokens live in `src/styles/tokens.css`.
- Permissions are affordance: `*appCan="'<module>.action'"` on actions; the server decides.
- i18n: every string through `i18n.t('<module>.key')`; `public/i18n/<module>.en.json` and `.ar.json` with the same keys;
  RTL works through logical CSS properties. Problem codes map to messages under `<module>.error.<CODE>`.
- Data entry pages honour the SRS keyboard rules (Ctrl+S saves, Esc closes a drawer, Enter adds a typed line).
- Specs: facade specs with `HttpTestingController` (drive resources with `TestBed.tick()` + `settle()`, never
  `whenStable()` while requests are pending) and one spec per non-trivial page.

## 5. Tests you must write

- Domain unit tests: one per transition (happy), per refused precondition, per SoD rule and per invariant, named after
  the intake scenario ids.
- Integration tests (`[Collection(DatabaseCollection.Name)]`, `DatabaseFixture`, `[DatabaseFact]`): tenant invisibility
  for every aggregate, unique indexes → `UNIQUE-409`, projections and read ports. Fixtures truncate every table
  automatically.
- Functional tests (`IClassFixture<ApiFactory>`, `factory.SeedUserAsync(tenantId:, permissions:)`, sign in through
  `AuthClient`): the main workflow of each page, a 403 without the permission, a 400 on a shape violation, the codes of
  the refused transitions.
- Architecture gates must stay green; add a gate when your module introduces a new rule.

## 6. Report (your final message)

Return, in this order: (1) the SRS pages implemented with their routes, (2) aggregates and tables (schema.table) with
the event names raised, (3) endpoints, (4) permissions used, (5) nav entries to register (`portal`, `group`, `path`,
`permission`, `i18nKey`, English and Arabic labels, release), (6) settings keys, read ports and shared primitives you
need from the integrator, (7) open questions and the assumed answers you built on, (8) test counts per suite you ran,
(9) anything you left out and why. Keep it factual; the integrator merges from it.
