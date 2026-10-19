import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import { PRICING_API, type PriceLineItem } from '../../../core/api/pricing.contracts';
import type { TableSort } from '../../../shared/ui/data-table/data-table.types';
import { AuthStore } from '../../../core/auth/auth.store';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { type PrintRequest, PrintService } from '../../../shared/print/print.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { PriceListEditorFacade } from '../state/price-list-editor.facade';
import { ServiceNamesStore } from '../state/service-names.store';
import { LIST_DETAIL, LIST_LINES, LISTS_PAGE } from '../testing/pricing.fixtures';
import { PriceListDocumentComponent } from '../ui/documents/price-list-document.component';
import { PriceListEditorPage } from './price-list-editor.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('PriceListEditorPage (CP-33)', () => {
  let backend: HttpTestingController;
  let printed: PrintRequest[];

  beforeEach(async () => {
    printed = [];
    await TestBed.configureTestingModule({
      imports: [PriceListEditorPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PriceListEditorFacade,
        ServiceNamesStore,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: PrintService, useValue: { print: async (request: PrintRequest): Promise<void> => void printed.push(request) } satisfies Pick<PrintService, 'print'> },
        // the print names the signed-in user; the page only reads the profile
        { provide: AuthStore, useValue: { profile: () => null } },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flush(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  async function render(): Promise<{ host: HTMLElement; page: PriceListEditorPage }> {
    const fixture = TestBed.createComponent(PriceListEditorPage);
    fixture.componentRef.setInput('listId', 'pl26');
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await flush();
    return { host: fixture.nativeElement as HTMLElement, page: fixture.componentInstance };
  }

  it('B-03: a header sort orders every line of the list before the page is cut, and goes back to page 1', async () => {
    // 60 lines, the price rising with the code: the default order puts the cheapest first
    const lines: PriceLineItem[] = Array.from({ length: 60 }, (_, index) => {
      const n = String(index + 1).padStart(2, '0');
      return { id: `l${n}`, serviceId: `s${n}`, price: (index + 1) * 10, isActive: true, serviceCode: `S${n}`, serviceNameEn: `Service ${n}`, serviceNameAr: `خدمة ${n}` };
    });
    const fixture = TestBed.createComponent(PriceListEditorPage);
    fixture.componentRef.setInput('listId', 'pl26');
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush({ ...LIST_DETAIL, lineCount: 60, activeLineCount: 60 });
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(lines);
    await flush();
    const page = fixture.componentInstance as unknown as {
      readonly page: { set(value: PageRequest): void; (): PageRequest };
      readonly pagedRows: () => readonly { readonly serviceId: string }[];
      onSort(sort: TableSort | null): void;
    };

    page.page.set({ page: 2, pageSize: 50 });
    page.onSort({ key: 'price', direction: 'desc' });
    expect(page.page().page).toBe(1);
    const first = page.pagedRows();
    expect(first.length).toBe(50);
    expect([first[0]?.serviceId, first[49]?.serviceId]).toEqual(['s60', 's11']);   // the dearest of the whole list, not of page 1

    page.page.set({ page: 2, pageSize: 50 });
    expect(page.pagedRows().map((row) => row.serviceId)).toEqual(['s10', 's09', 's08', 's07', 's06', 's05', 's04', 's03', 's02', 's01']);

    page.onSort(null);
    expect(page.pagedRows()[0]?.serviceId).toBe('s01');
  });

  it('shows the list summary and an editable price per service', async () => {
    const { host } = await render();

    expect(host.querySelector('app-page-header')?.textContent).toContain('PL26');
    const rows = host.querySelectorAll('tbody tr[data-row]');
    expect(rows.length).toBe(2);
    expect(rows[0]?.textContent).toContain('CBC');
    expect(host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price')?.value).toBe('180');
    backend.verify();
  });

  it('names the derived contracts without linking to the Release-2 contract pricing page (UAT R1-03)', async () => {
    const fixture = TestBed.createComponent(PriceListEditorPage);
    fixture.componentRef.setInput('listId', 'pl26');
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush({
      ...LIST_DETAIL,
      derivedContracts: [{ contractPricingId: 'cp1', payerId: 'p-axa', contractTierId: 't-gold', payerNameEn: 'AXA', payerNameAr: 'أكسا', tierNameEn: 'Gold', tierNameAr: 'ذهبي', appliedAtUtc: null }],
    });
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    const chip = [...host.querySelectorAll('.chips .chip')].find((candidate) => candidate.textContent?.includes('AXA'));
    expect(chip?.textContent).toContain('Gold');
    expect(chip?.tagName).toBe('SPAN');
    expect(host.querySelector('a[href*="/pricing/contract-pricing"]')).toBeNull();
  });

  it('adds a typed service from the catalogue picker with Enter (SRS keyboard rule)', async () => {
    const { host } = await render();

    const picker = host.querySelector<HTMLInputElement>('app-service-picker input[role="combobox"]');
    if (!picker) {
      throw new Error('picker missing');
    }

    picker.value = 'tsh';
    picker.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    const search = backend.expectOne((request) => request.url === PRICING_API.catalogueServices);
    expect(search.request.params.get('search')).toBe('tsh');
    expect(search.request.params.get('pageSize')).toBe('20');
    search.flush({ items: [{ id: 's-tsh', code: 'TSH', shortName: 'TSH', nameEn: 'Thyroid stimulating hormone', nameAr: 'هرمون الغدة' }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    await flush();

    picker.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await flush();

    const first = host.querySelector('tbody tr[data-row]');
    expect(first?.textContent).toContain('TSH');
    expect(first?.textContent).toContain('pricing.editor.new');
    expect(host.textContent).toContain('pricing.editor.unsaved');
    backend.verify();
  });

  it('marks a typed price as changed and saves it with Ctrl+S; leaving with changes asks first', async () => {
    const { host, page } = await render();

    const price = host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price');
    if (!price) {
      throw new Error('price input missing');
    }

    price.value = '195.5';
    price.dispatchEvent(new Event('input'));
    await flush();
    expect(host.textContent).toContain('pricing.editor.unsaved');
    expect(host.textContent).toContain('pricing.editor.changed');

    const confirm = TestBed.inject(ConfirmService);
    const leaving = page.canLeave();
    await flush();
    expect(confirm.current()?.kind).toBe('confirm');
    confirm.resolve(false);
    expect(await leaving).toBe(false);

    price.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
    await flush();
    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` });
    expect(put.request.body).toEqual({ lines: [{ serviceId: 's-cbc', price: 195.5, isActive: true }] });
    put.flush({ added: 0, changed: 1 });
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush([{ ...LIST_LINES[0], price: 195.5 }, LIST_LINES[1]]);
    await flush();

    expect(host.textContent).not.toContain('pricing.editor.unsaved');
    expect(await page.canLeave()).toBe(true);
    backend.verify();
  });

  /** The page's parts the scenarios drive (kept protected by the page). */
  interface EditorInternals {
    onPicked(service: { id: string; code: string; nameEn: string; nameAr: string }): void;
    toggleActive(): Promise<void>;
    openCopy(): Promise<void>;
  }

  function rowsOf(host: HTMLElement): readonly Element[] {
    return [...host.querySelectorAll('tbody tr[data-row]')];
  }

  function searchBox(host: HTMLElement): HTMLInputElement {
    const box = host.querySelector<HTMLInputElement>('app-search-box input[type="search"]');
    if (!box) {
      throw new Error('search box missing');
    }

    return box;
  }

  it('shows the filter of a service already on the list in the search box; clearing the box shows every line again, Refresh keeps it (UAT B-05)', async () => {
    const { host, page } = await render();
    expect(rowsOf(host).length).toBe(2);

    (page as unknown as EditorInternals).onPicked({ id: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة' });
    await flush();
    expect(rowsOf(host).length).toBe(1);
    expect(searchBox(host).value).toBe('CBC');
    expect(host.textContent).not.toContain('pricing.editor.unsaved');

    // Refresh re-reads the list and keeps the filter the box shows
    host.querySelector<HTMLButtonElement>('app-page-header button.ghost')?.click();
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await flush();
    expect(searchBox(host).value).toBe('CBC');
    expect(rowsOf(host).length).toBe(1);

    host.querySelector<HTMLButtonElement>('app-search-box button.clear')?.click();
    await flush();
    expect(searchBox(host).value).toBe('');
    expect(rowsOf(host).length).toBe(2);

    // typing filters at once, and the box and the grid agree
    const box = searchBox(host);
    box.value = 'lip';
    box.dispatchEvent(new Event('input'));
    await flush();
    expect(rowsOf(host).map((row) => row.textContent ?? '')).toEqual([expect.stringContaining('LIP')]);
    backend.verify();
  });

  it('starts another list unfiltered, so a filter never survives into a page that does not show it (UAT B-05)', async () => {
    const facade = TestBed.inject(PriceListEditorFacade);
    const { page } = await render();
    (page as unknown as EditorInternals).onPicked({ id: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة' });
    expect(facade.search()).toBe('CBC');

    facade.load('pl27');
    expect(facade.search()).toBe('');
    await flush();
    backend.match((request) => request.url.includes('/pl27')).forEach((request) => request.flush(request.request.url.endsWith('/lines') ? [] : { ...LIST_DETAIL, id: 'pl27', code: 'PL27' }));
  });

  it('asks before activating a list, because activation changes what cash patients pay (validation O-1)', async () => {
    const fixture = TestBed.createComponent(PriceListEditorPage);
    fixture.componentRef.setInput('listId', 'pl26');
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush({ ...LIST_DETAIL, isActive: false, isCashDefault: false });
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await flush();
    const page = fixture.componentInstance as unknown as EditorInternals;
    const confirm = TestBed.inject(ConfirmService);

    const refused = page.toggleActive();
    await flush();
    expect(confirm.current()?.options.message).toBe('pricing.editor.confirmActivate');
    confirm.resolve(false);
    await refused;
    backend.expectNone(`${PRICING_API.basePriceLists}/pl26/activate`);

    const accepted = page.toggleActive();
    await flush();
    confirm.resolve(true);
    await flush();
    backend.expectOne({ method: 'POST', url: `${PRICING_API.basePriceLists}/pl26/activate` }).flush(null, { status: 204, statusText: 'No Content' });
    await accepted;
    await flush();
    backend.match(`${PRICING_API.basePriceLists}/pl26`).forEach((request) => request.flush(LIST_DETAIL));
  });

  it('proposes a free code for the copy: the existing lists of any status are read first (UAT B-06)', async () => {
    const { host, page } = await render();

    const opening = (page as unknown as EditorInternals).openCopy();
    await flush();
    const lookup = backend.expectOne((request) => request.url === PRICING_API.basePriceLists);
    expect(lookup.request.params.get('search')).toBe('PL27');
    expect(lookup.request.params.has('isActive')).toBe(false);
    lookup.flush({ items: [{ ...LISTS_PAGE.items[0], id: 'pl27', code: 'PL27', year: 2027, isActive: false }], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 });
    await opening;
    await flush();

    const code = document.querySelector<HTMLInputElement>('app-copy-list-dialog input[formcontrolname="code"]') ?? host.querySelector<HTMLInputElement>('app-copy-list-dialog input[formcontrolname="code"]');
    expect(code?.value).toBe('PL27-2');
    backend.verify();
  });

  // ---- tools (CR 2026-10-19 C21) ----

  /** The tool handlers the dialogs call (kept protected by the page). */
  interface ToolInternals {
    readonly importOpen: () => boolean;
    readonly adjustOpen: () => boolean;
    readonly importResult: () => { applied: boolean } | null;
    readonly adjustResult: () => { applied: boolean } | null;
    openImport(): void;
    openAdjust(): void;
    checkImport(rows: readonly { row: number; serviceCode: string | null; price: number | null }[]): Promise<void>;
    applyImport(rows: readonly { row: number; serviceCode: string | null; price: number | null }[]): Promise<void>;
    previewAdjust(request: { mode: 'Percent' | 'Amount'; value: number; rounding: 'None' | 'Whole' | 'Nearest5' | 'Nearest10'; serviceIds: readonly string[] | null; preview: boolean }): Promise<void>;
    applyAdjust(request: { mode: 'Percent' | 'Amount'; value: number; rounding: 'None' | 'Whole' | 'Nearest5' | 'Nearest10'; serviceIds: readonly string[] | null; preview: boolean }): Promise<void>;
    print(): Promise<void>;
  }

  function expectReload(): void {
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
  }

  it('Upload Excel: checks the parsed rows as a dry run (nothing re-read), then applies them and re-reads the grid', async () => {
    const { host, page } = await render();
    const tools = page as unknown as ToolInternals;
    const rows = [{ row: 2, serviceCode: 'CBC', price: 190 }, { row: 3, serviceCode: 'TSH', price: 95 }];

    [...host.querySelectorAll<HTMLButtonElement>('app-page-header button')].find((button) => button.textContent?.includes('pricing.tools.uploadExcel'))?.click();
    await flush();
    expect(tools.importOpen()).toBe(true);

    const checking = tools.checkImport(rows);
    await settle();
    const dryRun = backend.expectOne({ method: 'POST', url: `${PRICING_API.basePriceLists}/pl26/import` });
    expect(dryRun.request.body).toEqual({ rows, dryRun: true });
    dryRun.flush({ accepted: 2, added: 1, changed: 1, applied: false, errors: [] });
    await checking;
    await flush();
    expect(tools.importResult()?.applied).toBe(false);
    backend.expectNone(`${PRICING_API.basePriceLists}/pl26/lines`);

    const applying = tools.applyImport(rows);
    await settle();
    const real = backend.expectOne({ method: 'POST', url: `${PRICING_API.basePriceLists}/pl26/import` });
    expect(real.request.body).toEqual({ rows, dryRun: false });
    real.flush({ accepted: 2, added: 1, changed: 1, applied: true, errors: [] });
    await applying;
    await flush();
    expectReload();
    await flush();
    expect(tools.importOpen()).toBe(false);
    backend.verify();
  });

  it('Bulk adjust: a preview writes nothing, an apply re-reads the grid; both carry the request as the dialog built it', async () => {
    const { page } = await render();
    const tools = page as unknown as ToolInternals;
    const request = { mode: 'Percent' as const, value: 10, rounding: 'Whole' as const, serviceIds: null, preview: true };
    const answer = { rows: [{ serviceId: 's-cbc', code: 'CBC', nameEn: 'CBC', nameAr: 'س', oldPrice: 180, newPrice: 198, clamped: false }], changed: 1, clamped: 0, applied: false };

    tools.openAdjust();
    expect(tools.adjustOpen()).toBe(true);
    const previewing = tools.previewAdjust(request);
    await settle();
    const preview = backend.expectOne({ method: 'POST', url: `${PRICING_API.basePriceLists}/pl26/adjust` });
    expect(preview.request.body).toEqual(request);
    preview.flush(answer);
    await previewing;
    await flush();
    expect(tools.adjustResult()?.applied).toBe(false);
    backend.expectNone(`${PRICING_API.basePriceLists}/pl26/lines`);

    const applying = tools.applyAdjust({ ...request, preview: false });
    await settle();
    const apply = backend.expectOne({ method: 'POST', url: `${PRICING_API.basePriceLists}/pl26/adjust` });
    expect(apply.request.body).toEqual({ ...request, preview: false });
    apply.flush({ ...answer, applied: true });
    await applying;
    await flush();
    expectReload();
    await flush();
    expect(tools.adjustOpen()).toBe(false);
    backend.verify();
  });

  it('the tools do not open while prices are unsaved (save or discard first)', async () => {
    const { host, page } = await render();
    const tools = page as unknown as ToolInternals;
    const price = host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price');
    if (!price) {
      throw new Error('price input missing');
    }

    price.value = '1';
    price.dispatchEvent(new Event('input'));
    await flush();

    tools.openImport();
    tools.openAdjust();
    expect([tools.importOpen(), tools.adjustOpen()]).toEqual([false, false]);
    backend.verify();
  });

  it('Print renders the list document with every stored line in code order', async () => {
    const { page } = await render();

    await (page as unknown as ToolInternals).print();

    expect(printed.length).toBe(1);
    expect(printed[0]?.component?.type).toBe(PriceListDocumentComponent);
    const inputs = printed[0]?.component?.inputs as { detail: { code: string }; lines: readonly { code: string; price: number; active: boolean }[] };
    expect(inputs.detail.code).toBe('PL26');
    expect(inputs.lines.map((line) => [line.code, line.price, line.active])).toEqual([
      ['CBC', 180, true],
      ['LIP', 300, true],
    ]);
    backend.verify();
  });
});
