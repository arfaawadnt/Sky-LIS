# Sky LIS UI kit (`shared/`)

The shared building blocks of both portals. Module agents use them as they are: no restyling, no local forks
(MODULE-PATTERN §4). If something is missing, ask for it in your report with the exact API you need.

```text
shared/ui/      components, directives and services of the screens      (barrel: shared/ui/index.ts)
shared/forms/   form helpers, validators, problem → field errors, paging (barrel: shared/forms/index.ts)
shared/print/   print service, Code 128, QR, document frame              (barrel: shared/print/index.ts)
```

Rules that hold everywhere:

- Every text you pass in is **already translated** (`i18n.t('<module>.key')`), except where an input says it is an
  i18n key (`TableColumn.header`, `FieldSchema.label/hint`, `RowAction.label`). The kit's own texts live in
  `public/i18n/shared.{en,ar}.json` (`shared.*`), loaded by the application root.
- Styling comes from the design tokens in `src/styles/tokens.css` and the global vocabulary in `src/styles.css`:
  buttons `.btn` (+ `.primary`, `.danger`, `.ok`, `.ghost`, `.soft`, `.sm`), `.icon-btn`, `.row-btn`, inputs `.inp`,
  `.check`, messages `.problem` / `.info-strip` / `.warn-strip`, layout `.form-grid` (+ `.three`, `.full`), `.kv`,
  `.muted`, `.small`, `.mono`, `.visually-hidden`. Use logical properties (`margin-inline-start`, `inset-inline-end`).
- Permissions are affordance only: wrap actions in `*appCan="'<module>.<action>'"`. The server decides.

## 1. Page anatomy (copy `features/treasury/pages/treasury-access.page.*`)

```html
<app-page-header [title]="i18n.t('x.title')" [subtitle]="i18n.t('x.subtitle')" [breadcrumb]="crumbs">
  <button pageActions type="button" class="btn primary" *appCan="'x.create'" (click)="add()">…</button>
</app-page-header>

<app-section-card [padded]="false">
  <app-toolbar>
    <app-search-box toolbarFilters (searchChange)="facade.setSearch($event)" />
    <select toolbarFilters class="inp" (change)="onStatus($event)">…</select>
    <button toolbarActions type="button" class="btn ghost" (click)="facade.list.reload()">…</button>
  </app-toolbar>

  <app-data-table [columns]="columns" [rows]="rows()" [loading]="facade.list.isLoading()" (rowActivate)="open($event)">
    <ng-template appCell="status" [appCellOf]="rows()" let-row><app-badge [status]="row.status" /></ng-template>
    <ng-template appRowActions [appRowActionsOf]="rows()" let-row>
      <button type="button" class="btn sm" *appCan="'x.approve'" (click)="approve(row)">…</button>
    </ng-template>
  </app-data-table>

  <app-paginator [page]="facade.filter().page" [pageSize]="facade.filter().pageSize" [totalCount]="total()" (pageChange)="facade.setPage($event)" />
</app-section-card>
```

## 2. Components (`shared/ui`)

| Selector | Inputs | Outputs / slots | Notes |
| --- | --- | --- | --- |
| `app-page-header` | `title`*, `subtitle`, `breadcrumb: Breadcrumb[]` (`{label, path \| null}`) | slot `[pageActions]` | Heading band of every page. |
| `app-section-card` | `title`, `subtitle`, `padded` (true) | slot `[cardActions]`, default slot | `[padded]="false"` when a toolbar/table fills it. |
| `app-toolbar` | – | slots `[toolbarFilters]`, `[toolbarActions]` | Wraps on small screens. |
| `app-search-box` | `[(value)]`, `placeholder`, `label`, `debounce` (300), `fallback`, `dark` | `(searchChange)` (debounced, trimmed; immediate on Enter / clear) | Esc clears. Ctrl+K focuses the first search box of the page. |
| `app-data-table` | `columns: TableColumn<T>[]`*, `rows`*, `rowId` (`row.id`), `loading`, `emptyTitle`, `emptyMessage`, `caption`, `selection: 'none'\|'single'\|'multi'`, `[(selected)]` (ids), `[(sort)]`, `serverSort`, `compact`, `zebra` (true), `interactive` (true), `rowClass` | `(sortChange)`, `(rowClick)`, `(rowActivate)`, `(selectionChange)`; templates `appCell`, `appRowActions`; slot `[tableEmpty]` | Sticky header, cards under 720 px, `secondary` columns hidden under 960 px. Client-side sort unless `serverSort`. |
| `app-paginator` | `page`*, `pageSize`*, `totalCount`*, `pageSizes` | `(pageChange): PageRequest` | "Showing 1–50 of 120", first/prev/next/last, page size. |
| `app-badge` | `tone: 'neutral'\|'info'\|'ok'\|'warn'\|'bad'\|'violet'`, `status` | content (label) | With `status` only: tone from `STATUS_TONES`, label from `shared.status.<Status>`. |
| `app-drawer` | `[(open)]`, `title`, `size: 'md'\|'lg'\|'xl'`, `dismissible` (true) | `(closed)`; slot `[drawerFooter]` | Side panel for forms; Esc / scrim close; focus moves to the first field, is trapped, returns to the opener. |
| `app-dialog` | `[(open)]`, `title`, `size: 'sm'\|'md'\|'lg'`, `danger`, `dismissible` | `(closed)`; slot `[dialogFooter]` | Centred modal. Prefer `ConfirmService` for confirmations. |
| `app-form-field` | `label`*, `hint`, `required` (derived from validators), `name`, `problem: ProblemDetails` | default slot: one control | Wires `for`/`id`, `aria-invalid`, `aria-describedby`; shows the first validation error (`shared.validation.*`) or the server message. |
| `app-localized-text-input` | `group: LocalizedTextGroup`*, `label`*, `name` ('name'), `hint`, `maxLength` (200), `problem` | – | English + Arabic (`dir="rtl"`) pair; server errors on `nameEn`/`nameAr` land on the right half. |
| `app-tabs` | `tabs: TabItem[]`* (`{id, label, badge?, disabled?}`), `[(active)]` | – | Arrow keys / Home / End move, Enter/Space select (RTL aware). Panels are yours: `role="tabpanel"`. |
| `app-empty-state` | `title`*, `message`, `icon` | slot `[emptyAction]` | |
| `app-stat-card` | `label`*, `value`*, `delta` (number → arrow, or text), `hint`, `tone: 'brand'\|'ok'\|'warn'\|'bad'\|'info'`, `icon` | – | KPI tile of the dashboards. |
| `app-master-data-page` | `config: MasterDataConfig`* | templates `appCell`, slot `[pageActions]` | See §3. |
| `app-icon` | `name: IconName`*, `size` (18) | – | Inline stroke icons (`ICONS`); decorative (`aria-hidden`). |
| `*appCan` | the permission key | – | Renders the host only while the user holds the permission. |
| `app-toast-host`, `app-dialog-host` | – | – | Placed once in `app.html`; never add them in a page. |

`TableColumn<T>`: `{ key, header (i18n key), width?, align?: 'start'|'center'|'end', sortable?, value?: (row) => string|number|boolean|null, mono?, secondary? }`.
A column shows `row[key]` unless it has `value` or an `appCell` template with the same key; booleans render
yes/no, empty values `—`. `[appCellOf]` / `[appRowActionsOf]` only type `let-row`: pass the rows array, or the
`MasterDataConfig` inside `<app-master-data-page>`.

Services:

- `ConfirmService` — `await confirm.confirm({ title, message, danger?, confirmLabel?, cancelLabel? })` → `boolean`;
  `await confirm.promptReason({ title, message, danger?, label?, placeholder?, required? (true), maxLength? })` →
  trimmed reason, or `null` when cancelled. Use it for every deactivate/delete/cancel/reject/revoke.
- `ToastService` — `toast.success(text)`, `toast.error(text)`, `info`, `warn` (`{ title?, duration? }`). Success after
  every mutation; errors use `problemMessage(…)`.
- `SearchFocusService` — internal to Ctrl+K; search boxes register themselves.
- `OverlayStackService` — internal; makes Esc close only the topmost overlay and locks body scroll.

Status tones (`STATUS_TONES` in `badge/status-tones.ts`): `Pending/Partial/Replaced` warn; `Approved/Active/Received/
Verified/Issued/Completed/Converted/Paid` ok; `Rejected/Revoked/Locked/Unpaid/Urgent/Panic` bad;
`Ordered/Complete/Open/Booked` info; `Collected/Reviewed/Reserved` violet; `Cancelled/Inactive/Printed/Draft/Closed`
neutral. Add a status there (and in `shared.status.*`) instead of colouring a badge locally.

## 3. `MasterDataPageComponent` — catalogue and setup screens

One config object gives a complete list + drawer CRUD page: search, active/inactive filter, paged table, "Add"
opens an empty drawer, a row click (or Enter on a focused row) opens it filled, Ctrl+S saves, Esc closes,
`VALIDATION-400` errors land on their fields, other problem codes show above the form
(`<prefix>.error.<CODE>` → `shared.error.<CODE>` → `shared.error.generic`), deactivate/reactivate ask for
confirmation, every success shows a toast and reloads the list.

```ts
// features/catalogue/pages/units.page.ts
@Component({
  selector: 'app-units-page',
  imports: [MasterDataPageComponent, CellTemplateDirective, BadgeComponent],
  template: `
    <app-master-data-page [config]="config">
      <!-- optional: module-specific cells and header buttons -->
      <ng-template appCell="kind" [appCellOf]="config" let-row><app-badge tone="info">{{ i18n.t('catalogue.unitKind.' + row.kind) }}</app-badge></ng-template>
      <button pageActions type="button" class="btn" (click)="print()">{{ i18n.t('catalogue.print') }}</button>
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnitsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueApi);

  protected readonly config: MasterDataConfig<UnitListItem, UnitRequest> = {
    i18nPrefix: 'catalogue.units',                     // title, subtitle, add, edit, field.<name>, error.<CODE>
    columns: [
      { key: 'code', header: 'catalogue.units.column.code', mono: true, sortable: true, width: '140px' },
      { key: 'name', header: 'catalogue.units.column.name', sortable: true,
        value: (u) => localizedPick({ en: u.nameEn, ar: u.nameAr }, this.i18n.lang()) },
      { key: 'kind', header: 'catalogue.units.column.kind' },
      { key: 'decimals', header: 'catalogue.units.column.decimals', align: 'end', secondary: true },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 16, hint: 'catalogue.units.hint.code' }, // upper-cased, immutable on edit
      { name: 'name', kind: 'localizedText', required: true, maxLength: 100 },                       // DTO: nameEn / nameAr
      { name: 'kind', kind: 'select', required: true, options: [
          { value: 'Mass', label: 'Mass', labelAr: 'كتلة' }, { value: 'Volume', label: 'Volume', labelAr: 'حجم' } ] },
      { name: 'clinicalGroupId', kind: 'select', options: () => this.api.clinicalGroupOptions() },     // async, loaded once
      { name: 'decimals', kind: 'number', min: 0, max: 6, defaultValue: 2 },
      { name: 'isDefault', kind: 'boolean' },
      { name: 'validFrom', kind: 'date' },
      { name: 'notes', kind: 'textarea', maxLength: 500 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.listUnits({ search: filter.search, isActive: filter.active, ...page }, signal),
      create: (dto) => this.api.createUnit(dto),
      update: (id, dto) => this.api.updateUnit(id, dto),
      setActive: (id, active) => (active ? this.api.reactivateUnit(id) : this.api.deactivateUnit(id)),
    },
    permissions: { create: 'catalogue.create', update: 'catalogue.update', activate: 'catalogue.manage' },
    rowActions: [{ id: 'members', label: 'catalogue.units.members', icon: 'layers', run: (u) => this.openMembers(u) }],
    // searchMode: 'client',   // when the list endpoint has no search parameter (filters the loaded page)
    // drawerSize: 'lg',       // wide forms
    // toForm / toDto,         // when the DTO is not the form value (default: field name ↔ property, localizedText ↔ <name>En/<name>Ar)
    // rowId / isActive,       // defaults: item.id / item.isActive
  };
}
```

Field kinds: `text`, `textarea`, `number`, `select` (static `options` or async provider), `boolean`, `localizedText`,
`date` (`yyyy-MM-dd` string), `code` (upper-case, `CODE_PATTERN`, immutable on edit unless `immutable: false`).
`immutable: true` disables any field on edit. Empty `select`/`date` values go to the DTO as `null`.

Dictionary keys the page looks for under the prefix (each falls back to `shared.masterData.*`): `title`, `subtitle`
(optional), `add`, `edit`, `searchPlaceholder`, `saved`, `activated`, `deactivated`, `loadError`, `activate`,
`deactivate`, `confirmDeactivate.title|message`, `confirmActivate.title|message`, plus `field.<name>` for every field
without an explicit `label`, and `error.<CODE>` for your problem codes. The treasury catalogue
(`features/treasury/pages/treasuries.page.ts`, keys `treasury.catalogue.*`) is the reference.

Use a hand-written page instead when the screen has states and transitions beyond active/inactive, child tables, or
more than one drawer.

## 4. Forms (`shared/forms`)

```ts
form = new FormGroup({
  code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(32)] }),
  name: localizedTextGroup({}, { required: true, maxLength: 200 }),
  mobile: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
});

async save(): Promise<void> {
  if (!markAllTouched(this.form)) return;                          // shows every field error
  const out = await this.facade.save.execute({ ...this.form.getRawValue() });
  if (out === null) problemToFormErrors(this.facade.save.problem(), this.form); // VALIDATION-400 → fields
}
```

```html
<form [formGroup]="form" appSaveShortcut (appSaveShortcut)="save()" (ngSubmit)="save()" novalidate>
  <div class="form-grid">
    <app-form-field [label]="i18n.t('x.field.code')" [hint]="i18n.t('x.hint.code')">
      <input type="text" formControlName="code" appUppercase />
    </app-form-field>
    <app-localized-text-input class="full" [group]="form.controls.name" [label]="i18n.t('x.field.name')" />
  </div>
</form>
```

| Export | Purpose |
| --- | --- |
| `problemToFormErrors(problem, form)` | Puts `errors: { NameEn: [...] }` on the matching controls as `{ server: message }` (camel-cases keys, `Lines[0].Qty` → `lines.0.qty`, `nameEn` → `name.en`); returns the unmatched entries. `clearServerErrors(control)` removes them. |
| `markAllTouched(control)` / `firstInvalid(control)` | Touch everything and return validity; find the first invalid leaf. |
| `requiredText`, `codeValidator`, `mobileValidator`, `textValidators({required, minLength, maxLength})`, `CODE_PATTERN`, `MOBILE_PATTERN` | Validators; error keys `required`, `code`, `mobile` are translated by `app-form-field`. |
| `SaveShortcutDirective` (`appSaveShortcut`) | Ctrl/Cmd+S emits; the innermost host containing the focus wins, else the most recently opened (the open drawer). |
| `UppercaseDirective` (`input[appUppercase]`) | Upper-cases while typing, trims on blur. |
| `localizedTextGroup`, `localizedFromDto(dto, 'name')`, `localizedToDto('name', value)`, `localizedPick(value, lang)` | `LocalizedText` ↔ `{ nameEn, nameAr }`; pick the current language with fallback. |
| `PagedFilter<F>`, `initialFilter(f)`, `withPage(f, page)`, `resetPage(f, change)`, `toHttpParams(filter)` | Facade filters with paging; `HttpParams` without empty values. `Paged<T>`, `PageRequest`, `emptyPage()` live in `core/api/paging.ts`. |
| `problemMessage(i18n, prefix, problem)` | Text for a problem code: `<prefix>.error.<CODE>` → `shared.error.<CODE>` → `shared.error.generic`. |
| `controlState(control, injector)` | A control's errors/touched/validity as a signal (used by `app-form-field`). |

## 5. Print kit (`shared/print`)

Modules expose the **data** of a document (`GET …/documents/<kind>` → typed DTO); the SPA renders it with an
ordinary OnPush component and prints it through a hidden iframe.

```ts
@Component({
  selector: 'app-receipt-document',
  imports: [DocumentFrameComponent, Code128Component, QrCodeComponent],
  template: `
    <app-document-frame [labNameEn]="doc().labNameEn" [labNameAr]="doc().labNameAr" [branch]="doc().branchName"
                        [title]="i18n.t('billing.receipt.title')" [reference]="doc().number"
                        [printedBy]="doc().printedBy" [printedAt]="printedAt()">
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="40" />
      <table>…<td class="num">{{ doc().amount }}</td>…</table>
      <app-code128 [value]="doc().accession" [height]="36" />
      <app-qr-code [value]="doc().verifyUrl" [size]="80" />
    </app-document-frame>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptDocument { readonly doc = input.required<ReceiptDocumentDto>(); … }

// in the page
await this.print.print({ title: `Receipt ${dto.number}`, size: 'A5', component: { type: ReceiptDocument, inputs: { doc: dto } } });
await this.print.print({ title: 'Labels', size: 'label-50x25', html: labelsHtml });  // or ready HTML
```

- `PrintService.print({ title, size: 'A4'|'A5'|'label-50x25'|'label-38x25', landscape?, html? | component?: { type, inputs }, dir?, lang? })`
  clones the application stylesheets, adds the `@page` rule, waits for images and fonts (max 3 s), opens the print
  dialog (PDF via "Save as PDF") and removes the frame afterwards. `render(...)` returns the HTML of a component;
  `compose(...)` the whole frame document (for previews and tests).
- `src/styles/print.css`: `.print-root.size-*` widths, `.print-page` (one page each, page break after), black-on-white
  tables with borders, `.num` (end-aligned tabular numbers), `.avoid-break`; when the app window itself is printed,
  the shell, toasts and dialogs are hidden.
- `app-code128` (`value`*, `height` 40, `showText` true, `label`): Code 128 B/C with automatic switching (digit runs
  of four or more use set C); `encodeCode128(text)` / `code128Values(text)` for checks. Only ASCII 32–126.
- `app-qr-code` (`value`*, `size` 96, `level` 'M', `margin` 2, `label`): UTF-8 QR code (`qrcode-generator`).
- `app-document-frame` (`labNameEn`*, `labNameAr`*, `branch`, `title`*, `subtitle`, `reference`, `printedBy`,
  `printedAt` (formatted)): the bilingual header (English left, Arabic right, logo in the middle via
  `[documentLogo]`) and the footer (`[documentFooter]` slot for signature boxes and page notes).

## 6. Keyboard rules (SRS cross-cutting "Keyboard")

| Key | Where | Effect |
| --- | --- | --- |
| Ctrl+K | anywhere in the shell | Focuses the first search box of the page; on pages without one, the navigation search. |
| Enter | navigation search | Opens the first matching page. |
| Ctrl+S | a form with `appSaveShortcut` (all master-data drawers) | Saves; the browser's save dialog never opens. |
| Esc | drawer, dialog, user menu, mobile sidebar, search box | Closes the topmost overlay (or clears the search). |
| ↑ ↓ Home End | a focused table row | Moves between rows (one tab stop per table). |
| Enter | a focused table row | `rowActivate` (opens the record). |
| Space | a focused row of a selectable table | Toggles its selection. |
| ← → Home End, Enter/Space | tabs | Moves between tabs (mirrored in RTL), selects. |
| Ctrl+Enter | reason prompt | Confirms. |

Module-specific keys (F2 opens the service picker, Enter adds a typed service or shortcut in registration) are
implemented by the module on its own inputs.

## 7. Shell and navigation (for the integrator)

- `shell/nav.registry.ts`: `NAV_GROUPS` (one per SRS module of each portal) and `NAV_REGISTRY` entries
  `{ portal: 'client'|'admin', group: 'shell.group.<name>', path, permission: string | null, i18nKey: 'shell.nav.<page>', release: 'R1'|'R2', icon?: IconName }`.
  Labels go to `public/i18n/shell.{en,ar}.json`. A group without visible entries is hidden; R2 entries carry an "R2" tag.
- The admin navigation shows when `PortalService.portal()` is `'admin'` (current tenant = `PLATFORM_TENANT_ID`).
  Client routes are children of the shell route in `app.routes.ts`; admin routes go under the `admin` route
  (`platformGuard`). `tenantGuard` keeps client-only pages away from the platform tenant.
