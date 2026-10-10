import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { Paged } from '../../../core/api/paging';
import { ProblemCodes, ProblemError } from '../../../core/api/problem-details';
import { CellTemplateDirective } from '../data-table/cell-template.directive';
import { ConfirmService } from '../dialog/confirm.service';
import { ToastService } from '../toast/toast.service';
import { MasterDataPageComponent } from './master-data-page.component';
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
    ],
    adapter: this.adapter,
  };
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function setValue(input: HTMLInputElement | null, value: string): void {
  if (!input) {
    throw new Error('input not found');
  }

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('MasterDataPageComponent (shared/ui)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
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

    expect(adapter.calls).toContain('create:{"code":"C3","nameEn":"Gamma","nameAr":"جاما"}');
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(TestBed.inject(ToastService).toasts().length).toBe(1);
    expect(adapter.calls.filter((call) => call === 'list').length).toBe(listCalls + 1);
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(3);
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

    expect(adapter.calls).toContain('update:b:{"code":"B2","nameEn":"Beta renamed","nameAr":"بيتا"}');
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
});
