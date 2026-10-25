import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import sharedEn from '../../../../../public/i18n/shared.en.json';
import type { Paged } from '../../../core/api/paging';
import { ProblemCodes, ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { CellTemplateDirective } from '../data-table/cell-template.directive';
import { ConfirmService } from '../dialog/confirm.service';
import { ToastService } from '../toast/toast.service';
import { MasterDataPageComponent } from './master-data-page.component';
import { localizedSortKey } from '../data-table/data-table.types';
import type { MasterDataAdapter, MasterDataConfig, MasterDataFilter } from './master-data.types';

interface Item {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

interface ItemDto {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly tags?: readonly string[];
}

/** An in-memory adapter that records every call (what a module's `*Api` does over HTTP). */
class FakeAdapter implements MasterDataAdapter<Item, ItemDto> {
  items: Item[] = [
    { id: 'a', code: 'A1', nameEn: 'Alpha', nameAr: 'ألفا', isActive: true },
    { id: 'b', code: 'B2', nameEn: 'Beta', nameAr: 'بيتا', isActive: true },
  ];

  readonly calls: string[] = [];
  readonly filters: MasterDataFilter[] = [];
  createError: Error | null = null;

  async list(filter: MasterDataFilter): Promise<Paged<Item>> {
    this.calls.push('list');
    this.filters.push(filter);
    const items = this.items.filter((item) => filter.active === null || item.isActive === filter.active);
    return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
  }

  async create(dto: ItemDto): Promise<void> {
    this.calls.push(`create:${JSON.stringify(dto)}`);
    if (this.createError) {
      throw this.createError;
    }

    this.items = [...this.items, { id: `n${this.items.length}`, ...dto, isActive: true }];
  }

  async update(id: string, dto: ItemDto): Promise<void> {
    this.calls.push(`update:${id}:${JSON.stringify(dto)}`);
    this.items = this.items.map((item) => (item.id === id ? { ...item, nameEn: dto.nameEn, nameAr: dto.nameAr } : item));
  }

  async setActive(id: string, active: boolean): Promise<void> {
    this.calls.push(`setActive:${id}:${active}`);
    this.items = this.items.map((item) => (item.id === id ? { ...item, isActive: active } : item));
  }
}

@Component({
  imports: [MasterDataPageComponent, CellTemplateDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-master-data-page [config]="config">
      <ng-template appCell="code" [appCellOf]="config" let-row><i class="code-cell">{{ row.code.toLowerCase() }}</i></ng-template>
    </app-master-data-page>
  `,
})
class HostComponent {
  readonly adapter = new FakeAdapter();

  readonly config: MasterDataConfig<Item, ItemDto> = {
    i18nPrefix: 'test.items',
    columns: [
      { key: 'code', header: 'test.items.column.code' },
      { key: 'nameEn', header: 'test.items.column.name' },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 8 },
      { name: 'name', kind: 'localizedText', required: true },
      { name: 'tags', kind: 'multiSelect', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }, { value: 'c', label: 'Gamma' }] },
    ],
    adapter: this.adapter,
  };
}

/** A gated register (validation v0.5.0 R-07): editing needs `test.edit`, which a reader does not hold. */
@Component({
  imports: [MasterDataPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-master-data-page [config]="config" />`,
})
class GatedHostComponent {
  readonly adapter = new FakeAdapter();

  readonly config: MasterDataConfig<Item, ItemDto> = {
    i18nPrefix: 'test.gated',
    columns: [{ key: 'code', header: 'test.gated.column.code' }],
    fields: [
      { name: 'code', kind: 'code', required: true },
      { name: 'name', kind: 'localizedText', required: true },
      { name: 'tags', kind: 'multiSelect', options: [{ value: 'a', label: 'Alpha' }] },
    ],
    adapter: this.adapter,
    permissions: { create: 'test.create', update: 'test.edit', activate: 'test.manage' },
  };
}

/** The permissions the signed-in user holds in a test (the page's affordances read them; the server decides). */
const granted = new Set<string>();

/** A branch-like page: the currency is a code field that starts at EGP (B-01). */
@Component({
  imports: [MasterDataPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-master-data-page [config]="config" />`,
})
class DefaultsHostComponent {
  readonly config: MasterDataConfig<Item, ItemDto> = {
    i18nPrefix: 'test.branches',
    columns: [{ key: 'code', header: 'test.branches.column.code' }],
    fields: [
      { name: 'code', kind: 'code', required: true },
      { name: 'currency', kind: 'code', required: true, maxLength: 3, defaultValue: 'EGP' },
    ],
    adapter: new FakeAdapter(),
  };
}

interface PlaceDto {
  readonly code: string;
  readonly countryId: string | null;
  readonly governorateId: string | null;
}

/** A branch-like page (validation v0.5.0): a two-field server rule (ORG-017) and a list sorted on the server. */
@Component({
  imports: [MasterDataPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-master-data-page [config]="config" />`,
})
class PairHostComponent {
  readonly filters: MasterDataFilter[] = [];
  readonly created: PlaceDto[] = [];
  refusals = 1;

  readonly config: MasterDataConfig<Item, PlaceDto> = {
    i18nPrefix: 'test.places',
    sortMode: 'server',
    columns: [
      { key: 'code', header: 'test.places.column.code', sortable: true },
      { key: 'name', header: 'test.places.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => item.nameEn },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true },
      { name: 'countryId', kind: 'select', options: [{ value: 'eg', label: 'Egypt' }, { value: 'jo', label: 'Jordan' }] },
      { name: 'governorateId', kind: 'select', options: [{ value: 'cairo', label: 'Cairo' }] },
    ],
    linkedFields: [['countryId', 'governorateId']],
    adapter: {
      list: async (filter) => {
        this.filters.push(filter);
        // the server order: Beta before Alpha (a client sort would put Alpha first)
        const items: Item[] = [
          { id: 'b', code: 'B2', nameEn: 'Beta', nameAr: 'بيتا', isActive: true },
          { id: 'a', code: 'A1', nameEn: 'Alpha', nameAr: 'ألفا', isActive: true },
        ];
        return { items, page: 1, pageSize: 50, totalCount: 60, totalPages: 2 };
      },
      create: async (dto) => {
        if (this.refusals-- > 0) {
          throw new ProblemError(
            { type: 'about:blank', title: '', status: 422, code: ProblemCodes.validationFailed, traceId: null, correlationId: null, errors: { governorateId: ['The governorate must belong to the chosen country.'] } },
            422,
          );
        }

        this.created.push(dto);
      },
      update: async () => undefined,
    },
  };
}

/** LIS-CR-008 C-04: cascading selects — governorate → city → district, each loaded for its parent's value. */
@Component({
  selector: 'app-cascade-host',
  imports: [MasterDataPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-master-data-page [config]="config" />`,
})
class CascadeHostComponent {
  readonly adapter = new FakeAdapter();
  readonly asked: string[] = [];
  readonly created: unknown[] = [];

  private readonly cities: Readonly<Record<string, readonly { value: string; label: string }[]>> = {
    cairo: [{ value: 'nasr', label: 'Nasr City' }],
    giza: [{ value: 'dokki', label: 'Dokki' }],
  };

  readonly config: MasterDataConfig<Item, unknown> = {
    i18nPrefix: 'test.cascade',
    columns: [{ key: 'code', header: 'test.cascade.column.code' }],
    fields: [
      { name: 'code', kind: 'code', required: true },
      { name: 'governorateId', kind: 'select', options: [{ value: 'cairo', label: 'Cairo' }, { value: 'giza', label: 'Giza' }] },
      {
        name: 'cityId',
        kind: 'select',
        dependsOn: 'governorateId',
        dependentOptions: async (governorateId) => {
          this.asked.push(`cities:${governorateId}`);
          return this.cities[governorateId] ?? [];
        },
      },
      {
        name: 'districtId',
        kind: 'select',
        dependsOn: 'cityId',
        dependentOptions: async (cityId) => {
          this.asked.push(`districts:${cityId}`);
          return cityId === 'nasr' ? [{ value: 'zone1', label: 'First Zone' }] : [];
        },
      },
    ],
    adapter: {
      list: (filter) => this.adapter.list(filter),
      create: async (dto) => {
        this.created.push(dto);
      },
      update: async () => undefined,
    },
  };
}

/** A country-like page (validation v0.5.0 C-09 live check): the dial code carries a pattern with its own message key. */
@Component({
  selector: 'app-pattern-host',
  imports: [MasterDataPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-master-data-page [config]="config" />`,
})
class PatternHostComponent {
  readonly adapter = new FakeAdapter();

  readonly config: MasterDataConfig<Item, ItemDto> = {
    i18nPrefix: 'test.countries',
    columns: [{ key: 'code', header: 'test.countries.column.code' }],
    fields: [
      { name: 'code', kind: 'text', required: true, maxLength: 5, pattern: /^\+[1-9]\d{0,3}$/, patternMessage: 'test.countries.error.dialCode' },
      { name: 'name', kind: 'localizedText', required: true },
    ],
    adapter: this.adapter,
  };
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function choose(select: HTMLSelectElement | null | undefined, label: string): void {
  if (!select) {
    throw new Error('select not found');
  }

  select.selectedIndex = [...select.options].findIndex((option) => option.text.trim() === label);
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

function setValue(input: HTMLInputElement | null, value: string): void {
  if (!input) {
    throw new Error('input not found');
  }

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('MasterDataPageComponent (shared/ui)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent, DefaultsHostComponent, PairHostComponent, GatedHostComponent, PatternHostComponent, CascadeHostComponent], providers: [provideZonelessChangeDetection(), { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> }] }).compileComponents();
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>; host: HTMLElement; adapter: FakeAdapter }> {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    TestBed.tick();
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement, adapter: fixture.componentInstance.adapter };
  }

  async function flush(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
    await settle();
    TestBed.tick();
    await settle();
    await fixture.whenStable();
  }

  it('lists the active records and reloads when the filter changes', async () => {
    const { fixture, host, adapter } = await render();

    expect(adapter.filters[0]).toEqual({ search: '', active: true });
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(2);
    // the page's projected cell template is forwarded to the table
    expect(host.querySelector('tbody tr[data-row] td .code-cell')?.textContent?.trim()).toBe('a1');

    const select = host.querySelector<HTMLSelectElement>('app-toolbar select');
    if (!select) {
      throw new Error('filter select missing');
    }

    select.value = 'all';
    select.dispatchEvent(new Event('change'));
    await flush(fixture);
    expect(adapter.filters[adapter.filters.length - 1]).toEqual({ search: '', active: null });
  });

  it('creates a record from the drawer: Add, fill, Save → adapter.create, toast, drawer closed, list reloaded', async () => {
    const { fixture, host, adapter } = await render();
    const listCalls = adapter.calls.filter((call) => call === 'list').length;

    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();

    setValue(dialog?.querySelector<HTMLInputElement>('input.code') ?? null, 'c3');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Gamma');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]') ?? null, 'جاما');
    await fixture.whenStable();

    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush(fixture);

    expect(adapter.calls).toContain('create:{"code":"C3","nameEn":"Gamma","nameAr":"جاما","tags":[]}');
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(TestBed.inject(ToastService).toasts().length).toBe(1);
    expect(adapter.calls.filter((call) => call === 'list').length).toBe(listCalls + 1);
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(3);
  });

  it('multiSelect: the chosen options reach the DTO as a string array', async () => {
    const { fixture, host, adapter } = await render();
    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    setValue(dialog?.querySelector<HTMLInputElement>('input.code') ?? null, 'c4');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Delta');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]') ?? null, 'دلتا');

    const select = dialog?.querySelector<HTMLSelectElement>('select[multiple]') ?? null;
    if (!select) {
      throw new Error('multi-select missing');
    }

    expect(select.options.length).toBe(3);   // no "choose…" placeholder on a multi-select
    for (const option of select.options) {
      option.selected = option.text === 'Alpha' || option.text === 'Gamma';
    }

    select.dispatchEvent(new Event('change', { bubbles: true }));
    await fixture.whenStable();
    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush(fixture);

    expect(adapter.calls).toContain('create:{"code":"C4","nameEn":"Delta","nameAr":"دلتا","tags":["a","c"]}');
  });

  it('refuses to save an invalid form and shows the field errors', async () => {
    const { fixture, host, adapter } = await render();

    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush(fixture);

    expect(adapter.calls.some((call) => call.startsWith('create'))).toBe(false);
    expect(host.querySelector('[role="dialog"]')).not.toBeNull();
    expect(dialog?.querySelectorAll('app-form-field .error').length).toBeGreaterThanOrEqual(3);
  });

  it('opens a row for editing with the immutable code disabled and saves through adapter.update', async () => {
    const { fixture, host, adapter } = await render();

    host.querySelector<HTMLTableRowElement>('tbody tr[data-row="b"]')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();
    const code = dialog?.querySelector<HTMLInputElement>('input.code');
    expect(code?.value).toBe('B2');
    expect(code?.disabled).toBe(true);
    expect(dialog?.querySelector<HTMLInputElement>('input[lang="en"]')?.value).toBe('Beta');

    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Beta renamed');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
    await flush(fixture);

    expect(adapter.calls).toContain('update:b:{"code":"B2","nameEn":"Beta renamed","nameAr":"بيتا","tags":[]}');
    expect(host.querySelector('[role="dialog"]')).toBeNull();
  });

  it('maps a VALIDATION-400 problem onto the fields', async () => {
    const { fixture, host, adapter } = await render();
    adapter.createError = new ProblemError(
      { type: 'about:blank', title: 'The request is not valid.', status: 400, code: ProblemCodes.validationFailed, traceId: null, correlationId: null, errors: { nameAr: ['Arabic name is too long.'] } },
      400,
    );

    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    setValue(dialog?.querySelector<HTMLInputElement>('input.code') ?? null, 'C3');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Gamma');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]') ?? null, 'جاما');
    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush(fixture);

    expect(host.querySelector('[role="dialog"]')).not.toBeNull();
    const errors = [...(dialog?.querySelectorAll('app-form-field .error') ?? [])].map((element) => element.textContent?.trim());
    expect(errors).toEqual(['Arabic name is too long.']);
  });

  it('shows a server rule in the user language instead of the validator English (C-06)', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) =>
      new Response(JSON.stringify(String(input) === '/i18n/shared.en.json' ? sharedEn : {}), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );
    await TestBed.inject(I18nService).load('shared');
    const { fixture, host, adapter } = await render();
    adapter.createError = new ProblemError(
      {
        type: 'about:blank',
        title: 'The request is not valid.',
        status: 400,
        code: ProblemCodes.validationFailed,
        traceId: null,
        correlationId: null,
        errors: { code: ["'Code' must be 3 characters in length. You entered 2 characters."] },
        rules: { code: [{ rule: 'ExactLength', minLength: 3, maxLength: 3 }] },
      },
      400,
    );

    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    setValue(dialog?.querySelector<HTMLInputElement>('input.code') ?? null, 'C3');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Gamma');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]') ?? null, 'جاما');
    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush(fixture);

    const errors = [...(dialog?.querySelectorAll('app-form-field .error') ?? [])].map((element) => element.textContent?.trim());
    expect(errors).toEqual(['Exactly 3 characters.']);
    vi.restoreAllMocks();
  });

  it('starts a new record of a code field at its default value (B-01)', async () => {
    const fixture = TestBed.createComponent(DefaultsHostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await fixture.whenStable();
    const inputs = [...(host.querySelectorAll<HTMLInputElement>('[role="dialog"] input.code') ?? [])].map((input) => input.value);
    expect(inputs).toEqual(['', 'EGP']);
  });

  it('deactivates a record after confirmation', async () => {
    const { fixture, host, adapter } = await render();
    const confirm = TestBed.inject(ConfirmService);

    host.querySelector<HTMLButtonElement>('tbody tr[data-row="a"] .row-btn.danger')?.click();
    await settle();
    expect(confirm.current()?.kind).toBe('confirm');
    expect(confirm.current()?.options.danger).toBe(true);

    confirm.resolve(true);
    await flush(fixture);
    expect(adapter.calls).toContain('setActive:a:false');
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(1);
  });

  describe('validation v0.5.0 R-07: a row opened without the edit permission', () => {
    afterEach(() => granted.clear());

    async function renderGated(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<GatedHostComponent>>; host: HTMLElement; adapter: FakeAdapter }> {
      const fixture = TestBed.createComponent(GatedHostComponent);
      await flush(fixture);
      return { fixture, host: fixture.nativeElement as HTMLElement, adapter: fixture.componentInstance.adapter };
    }

    it('opens read-only: fields disabled, no Save, the title says View, and F10 / Ctrl+S save nothing', async () => {
      granted.add('test.view');
      const { fixture, host, adapter } = await renderGated();
      expect(host.querySelector('tbody tr[data-row="b"] .row-btn')).toBeNull();   // no edit or activate button for a reader

      host.querySelector<HTMLTableRowElement>('tbody tr[data-row="b"]')?.click();
      await fixture.whenStable();
      const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      expect(dialog).not.toBeNull();
      expect(dialog?.textContent).toContain('masterData.view');
      expect(dialog?.textContent).not.toContain('masterData.edit');
      expect(dialog?.querySelector<HTMLInputElement>('input[lang="en"]')?.value).toBe('Beta');   // the record is shown
      const controls = [...(dialog?.querySelectorAll<HTMLInputElement | HTMLSelectElement>('form input, form select, form textarea') ?? [])];
      expect(controls.length).toBeGreaterThanOrEqual(4);
      expect(controls.every((control) => control.disabled)).toBe(true);
      const footer = [...(dialog?.querySelectorAll<HTMLButtonElement>('.foot button') ?? [])].map((button) => button.textContent?.trim());
      expect(footer).toEqual(['shared.drawer.close']);

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F10', bubbles: true }));
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
      await flush(fixture);
      expect(adapter.calls.some((call) => call.startsWith('update'))).toBe(false);

      dialog?.querySelector<HTMLButtonElement>('.foot .btn')?.click();
      await fixture.whenStable();
      expect(host.querySelector('[role="dialog"]')).toBeNull();
    });

    it('opens for editing with the permission (title Edit, fields enabled but the code, Save present)', async () => {
      granted.add('test.edit');
      const { fixture, host, adapter } = await renderGated();
      host.querySelector<HTMLTableRowElement>('tbody tr[data-row="b"]')?.click();
      await fixture.whenStable();
      const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      expect(dialog?.textContent).toContain('masterData.edit');
      expect(dialog?.querySelector<HTMLInputElement>('input.code')?.disabled).toBe(true);
      expect(dialog?.querySelector<HTMLInputElement>('input[lang="en"]')?.disabled).toBe(false);

      setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Beta 2');
      dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);
      expect(adapter.calls).toContain('update:b:{"code":"B2","nameEn":"Beta 2","nameAr":"بيتا","tags":[]}');
    });

    it('a reader who may create still gets an editable Add drawer, and the next row opens read-only again', async () => {
      granted.add('test.create');
      const { fixture, host } = await renderGated();
      host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
      await fixture.whenStable();
      let dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      expect(dialog?.querySelector<HTMLInputElement>('input.code')?.disabled).toBe(false);
      expect(dialog?.querySelector('.foot .btn.primary')).not.toBeNull();
      dialog?.querySelector<HTMLButtonElement>('.foot .btn')?.click();
      await flush(fixture);

      host.querySelector<HTMLTableRowElement>('tbody tr[data-row="a"]')?.click();
      await fixture.whenStable();
      dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      expect(dialog?.querySelector<HTMLInputElement>('input[lang="en"]')?.disabled).toBe(true);
      expect(dialog?.querySelector('.foot .btn.primary')).toBeNull();
    });
  });

  it('LIS-CR-008 C-04: a dependsOn select lists the options of its parent value and is cleared when the parent changes', async () => {
    const fixture = TestBed.createComponent(CascadeHostComponent);
    await flush(fixture);
    const host = fixture.nativeElement as HTMLElement;
    const page = fixture.componentInstance;
    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await flush(fixture);
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]')!;
    const selects = (): HTMLSelectElement[] => [...dialog.querySelectorAll<HTMLSelectElement>('select')];
    const labels = (select: HTMLSelectElement): string[] => [...select.options].slice(1).map((option) => option.text.trim());
    expect(labels(selects()[1]!)).toEqual([]);   // no governorate: no city

    setValue(dialog.querySelector<HTMLInputElement>('input.code'), 'east');
    choose(selects()[0], 'Cairo');
    await flush(fixture);
    expect(labels(selects()[1]!)).toEqual(['Nasr City']);
    choose(selects()[1], 'Nasr City');
    await flush(fixture);
    expect(labels(selects()[2]!)).toEqual(['First Zone']);
    choose(selects()[2], 'First Zone');
    await flush(fixture);

    // another governorate clears the city and, through it, the district
    choose(selects()[0], 'Giza');
    await flush(fixture);
    expect(labels(selects()[1]!)).toEqual(['Dokki']);
    expect(selects()[1]!.selectedIndex).toBe(0);
    expect(labels(selects()[2]!)).toEqual([]);

    choose(selects()[1], 'Dokki');
    await flush(fixture);
    dialog.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush(fixture);
    expect(page.created).toEqual([{ code: 'EAST', governorateId: 'giza', cityId: 'dokki', districtId: null }]);
    expect(page.asked).toContain('cities:cairo');
    expect(page.asked).toContain('districts:nasr');
  });

  describe('validation v0.5.0', () => {
    async function renderPair(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<PairHostComponent>>; host: HTMLElement; page: PairHostComponent }> {
      const fixture = TestBed.createComponent(PairHostComponent);
      await flush(fixture);
      return { fixture, host: fixture.nativeElement as HTMLElement, page: fixture.componentInstance };
    }

    async function openAndFill(fixture: { whenStable(): Promise<unknown> }, host: HTMLElement): Promise<HTMLElement> {
      host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
      await fixture.whenStable();
      const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      if (!dialog) {
        throw new Error('drawer missing');
      }

      setValue(dialog.querySelector<HTMLInputElement>('input.code'), 'east');
      const [country, governorate] = [...dialog.querySelectorAll<HTMLSelectElement>('select')];
      choose(country, 'Jordan');
      choose(governorate, 'Cairo');
      await fixture.whenStable();
      return dialog;
    }

    it('B-01: correcting the other field of a refused pair clears the server error, so the next save reaches the server', async () => {
      const { fixture, host, page } = await renderPair();
      const dialog = await openAndFill(fixture, host);

      dialog.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);
      expect(dialog.querySelector('app-form-field .error')?.textContent).toContain('The governorate must belong to the chosen country.');

      // the user fixes the COUNTRY, not the governorate that carries the error
      choose(dialog.querySelectorAll<HTMLSelectElement>('select')[0], 'Egypt');
      await flush(fixture);
      expect(dialog.querySelector('app-form-field .error')).toBeNull();

      dialog.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);
      expect(page.created).toEqual([{ code: 'EAST', countryId: 'eg', governorateId: 'cairo' }]);
      expect(host.querySelector('[role="dialog"]')).toBeNull();
    });

    it('B-01: an unrelated edit leaves the server error of the pair in place', async () => {
      const { fixture, host, page } = await renderPair();
      const dialog = await openAndFill(fixture, host);
      dialog.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);

      setValue(dialog.querySelector<HTMLInputElement>('input.code'), 'east2');
      await flush(fixture);
      expect(dialog.querySelector('app-form-field .error')?.textContent).toContain('The governorate must belong to the chosen country.');
      expect(page.created).toEqual([]);
    });

    it('B-01: a save blocked by the form says so and focuses the first invalid field', async () => {
      const { fixture, host, page } = await renderPair();
      host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
      await fixture.whenStable();
      const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      expect(dialog?.querySelector('p.problem[role="alert"]')).toBeNull();

      dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);

      expect(page.created).toEqual([]);
      expect(dialog?.querySelector('p.problem[role="alert"]')?.textContent).toContain('masterData.invalid');
      expect(document.activeElement).toBe(dialog?.querySelector('input.code'));

      setValue(dialog?.querySelector<HTMLInputElement>('input.code') ?? null, 'ok');
      await flush(fixture);
      expect(dialog?.querySelector('p.problem[role="alert"]')).toBeNull();   // the notice follows the form
    });

    it('C-09 live check: a pattern field shows its own message under the control as the user types, and blocks the save', async () => {
      const fixture = TestBed.createComponent(PatternHostComponent);
      await flush(fixture);
      const host = fixture.nativeElement as HTMLElement;
      host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
      await fixture.whenStable();
      const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
      const field = dialog?.querySelector<HTMLElement>('app-form-field');
      const input = field?.querySelector<HTMLInputElement>('input') ?? null;
      expect(field?.querySelector('.error')).toBeNull();

      setValue(input, '998');   // typed, never blurred
      await flush(fixture);
      expect(field?.querySelector('.error')?.textContent).toContain('test.countries.error.dialCode');
      expect(input?.getAttribute('aria-invalid')).toBe('true');

      setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]') ?? null, 'Uzbekistan');
      setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]') ?? null, 'أوزبكستان');
      dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);
      expect(fixture.componentInstance.adapter.calls.filter((call) => call.startsWith('create'))).toEqual([]);
      expect(dialog?.querySelector('p.problem[role="alert"]')?.textContent).toContain('masterData.invalid');

      setValue(input, ' +998');   // trimmed before the check
      await flush(fixture);
      expect(field?.querySelector('.error')).toBeNull();
      dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
      await flush(fixture);
      expect(fixture.componentInstance.adapter.calls.filter((call) => call.startsWith('create'))).toEqual(['create:{"code":"+998","nameEn":"Uzbekistan","nameAr":"أوزبكستان"}']);
    });

    it('B-03: a header click sorts on the server from page 1 and shows the rows in the server order', async () => {
      const { fixture, host, page } = await renderPair();
      expect(page.filters.at(-1)?.sort).toBeUndefined();

      host.querySelectorAll<HTMLButtonElement>('thead .sort-btn')[1]?.click();
      await flush(fixture);
      expect(page.filters.at(-1)?.sort).toEqual({ key: 'nameEn', direction: 'asc' });
      // not re-sorted in the browser: Beta (server order) stays before Alpha
      expect([...host.querySelectorAll('tbody tr[data-row]')].map((row) => row.getAttribute('data-row'))).toEqual(['b', 'a']);

      host.querySelectorAll<HTMLButtonElement>('thead .sort-btn')[1]?.click();
      await flush(fixture);
      expect(page.filters.at(-1)?.sort).toEqual({ key: 'nameEn', direction: 'desc' });

      TestBed.inject(I18nService).setLanguage('ar');
      await flush(fixture);
      expect(page.filters.at(-1)?.sort).toEqual({ key: 'nameAr', direction: 'desc' });   // the name of the shown language
      TestBed.inject(I18nService).setLanguage('en');

      host.querySelectorAll<HTMLButtonElement>('thead .sort-btn')[0]?.click();
      await flush(fixture);
      expect(page.filters.at(-1)?.sort).toEqual({ key: 'code', direction: 'asc' });
    });
  });
});
