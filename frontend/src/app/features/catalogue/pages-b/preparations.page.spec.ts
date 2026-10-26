import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, type Paged, type PreparationListItem } from '../../../core/api/catalogue-b.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PreparationsFacade } from '../state-b/catalogue-b-list.facade';
import { PreparationsPage, optionValues, splitOptions } from './preparations.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page(items: PreparationListItem[]): Paged<PreparationListItem> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const medication: PreparationListItem = {
  id: 'p1',
  code: 'MED',
  questionEn: 'Current medication?',
  questionAr: 'الأدوية الحالية؟',
  answerType: 'Text',
  options: [],
  gender: 'Any',
  services: [{ id: 's1', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', isActive: true }],
  isActive: true,
};

function setValue(element: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null | undefined, value: string, event = 'input'): void {
  if (!element) {
    throw new Error('control not found');
  }

  element.value = value;
  element.dispatchEvent(new Event(event, { bubbles: true }));
}

const fasting: PreparationListItem = {
  id: 'p3',
  code: 'FAST',
  questionEn: 'Fasting hours?',
  questionAr: 'ساعات الصيام؟',
  answerType: 'OptionList',
  options: ['8 h', '10 h', '12 h'],
  gender: 'Any',
  services: [],
  isActive: true,
};

/** The option rows of the drawer, in order. */
function rows(dialog: HTMLElement | null | undefined): HTMLInputElement[] {
  return Array.from(dialog?.querySelectorAll<HTMLInputElement>('.option-row input') ?? []);
}

function addButton(dialog: HTMLElement | null | undefined): HTMLButtonElement | null {
  return dialog?.querySelector<HTMLButtonElement>('.option-foot .btn') ?? null;
}

/** The up / down / remove buttons of row `index`. */
function rowButtons(dialog: HTMLElement | null | undefined, index: number): HTMLButtonElement[] {
  return Array.from(dialog?.querySelectorAll('.option-row')[index]?.querySelectorAll<HTMLButtonElement>('.option-actions button') ?? []);
}

/** A paste event carrying `text` (jsdom has no `ClipboardEvent` constructor with data). */
function paste(element: HTMLInputElement | undefined, text: string): void {
  if (!element) {
    throw new Error('row not found');
  }

  const event = new Event('paste', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'clipboardData', { value: { getData: (type: string) => (type === 'text/plain' ? text : '') } });
  element.dispatchEvent(event);
}

describe('PreparationsPage (CP-46)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PreparationsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        PreparationsFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<PreparationsPage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(PreparationsPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.preparations).flush(page([medication]));
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('lists the preparations with their linked services', async () => {
    const { host } = await render();

    const row = host.querySelector('tbody tr[data-row="p1"]');
    expect(row?.textContent).toContain('MED');
    expect(row?.textContent).toContain('Current medication?');
    expect(row?.querySelector('app-catalogue-b-chips')?.textContent).toContain('GLU');
  });

  it('refuses an option list with one option on the client, then creates it with two', async () => {
    const { fixture, host } = await render();

    const add = Array.from(host.querySelectorAll<HTMLButtonElement>('app-page-header .btn.primary')).at(-1);
    add?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();

    setValue(dialog?.querySelector<HTMLInputElement>('input[formcontrolname="code"]'), 'fast');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]'), 'Fasting hours?');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]'), 'ساعات الصيام؟');
    setValue(dialog?.querySelector<HTMLSelectElement>('select[formcontrolname="answerType"]'), 'OptionList', 'change');
    await fixture.whenStable();
    expect(rows(dialog).length).toBe(2);   // an option list starts with two empty rows
    setValue(rows(dialog)[0], '8 h');
    await fixture.whenStable();

    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();
    await fixture.whenStable();
    backend.expectNone((request) => request.method === 'POST');
    expect(dialog?.textContent).toContain('catalogue-b.error.CAT-210');

    setValue(rows(dialog)[1], '12 h');
    addButton(dialog)?.click();   // a blank third row is ignored on save
    await fixture.whenStable();
    expect(rows(dialog).length).toBe(3);
    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();

    const post = backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_B_API.preparations);
    expect(post.request.body).toEqual({
      code: 'FAST',
      questionEn: 'Fasting hours?',
      questionAr: 'ساعات الصيام؟',
      answerType: 'OptionList',
      options: ['8 h', '12 h'],
      gender: 'Any',
      serviceIds: [],
    });
    post.flush({ id: 'p2' }, { status: 201, statusText: 'Created' });
    await settle();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.preparations).flush(page([medication]));
    await fixture.whenStable();
    expect(host.querySelector('[role="dialog"]')).toBeNull();
  });

  it('opens a row with the code locked and sends the update without it', async () => {
    const { fixture, host } = await render();

    host.querySelector<HTMLTableRowElement>('tbody tr[data-row="p1"]')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    const code = dialog?.querySelector<HTMLInputElement>('input[formcontrolname="code"]');
    expect(code?.value).toBe('MED');
    expect(code?.disabled).toBe(true);
    expect(dialog?.querySelector('app-catalogue-b-picker .chip')?.textContent).toContain('GLU');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
    await settle();
    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === `${CATALOGUE_B_API.preparations}/p1`);
    expect(put.request.body).toEqual({ questionEn: 'Current medication?', questionAr: 'الأدوية الحالية؟', answerType: 'Text', options: [], gender: 'Any', serviceIds: ['s1'] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET').flush(page([medication]));
  });

  it('splits options one per line', () => {
    expect(splitOptions(' a \r\n\n b\n')).toEqual(['a', 'b']);
    expect(optionValues([' a ', '', '  ', 'b'])).toEqual(['a', 'b']);
  });

  async function openFasting(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<PreparationsPage>>; dialog: HTMLElement }> {
    const fixture = TestBed.createComponent(PreparationsPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.preparations).flush(page([medication, fasting]));
    await settle();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLTableRowElement>('tbody tr[data-row="p3"]')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    if (dialog === null) {
      throw new Error('drawer not open');
    }

    return { fixture, dialog };
  }

  async function saveAndExpectPut(fixture: ReturnType<typeof TestBed.createComponent<PreparationsPage>>, dialog: HTMLElement, options: readonly string[]): Promise<void> {
    dialog.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();
    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === `${CATALOGUE_B_API.preparations}/p3`);
    expect((put.request.body as { options: readonly string[] }).options).toEqual(options);
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    TestBed.tick();
    await settle();
    backend.match((request) => request.method === 'GET').forEach((request) => request.flush(page([medication, fasting])));
    await fixture.whenStable();
  }

  it('LIS-CR-015 M1: an existing option list opens as numbered rows and saves them in their new order', async () => {
    const { fixture, dialog } = await openFasting();
    expect(dialog.querySelector('textarea')).toBeNull();
    expect(rows(dialog).map((row) => row.value)).toEqual(['8 h', '10 h', '12 h']);
    expect(Array.from(dialog.querySelectorAll('.option-no')).map((cell) => cell.textContent?.trim())).toEqual(['1', '2', '3']);
    expect(rowButtons(dialog, 0)[0]?.disabled).toBe(true);    // the first row cannot move up
    expect(rowButtons(dialog, 2)[1]?.disabled).toBe(true);    // the last row cannot move down

    rowButtons(dialog, 2)[0]?.click();   // 12 h up
    await fixture.whenStable();
    expect(rows(dialog).map((row) => row.value)).toEqual(['8 h', '12 h', '10 h']);
    rowButtons(dialog, 0)[2]?.click();   // remove 8 h
    await fixture.whenStable();
    expect(rows(dialog).map((row) => row.value)).toEqual(['12 h', '10 h']);
    rowButtons(dialog, 0)[1]?.click();   // 12 h down
    await fixture.whenStable();

    await saveAndExpectPut(fixture, dialog, ['10 h', '12 h']);
  });

  it('LIS-CR-015 M1: Enter in the last row adds a row and focuses it; Add option stops at 30', async () => {
    const { fixture, dialog } = await openFasting();
    rows(dialog)[2]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await fixture.whenStable();
    await settle();
    expect(rows(dialog).length).toBe(4);
    expect(document.activeElement).toBe(rows(dialog)[3]);
    backend.expectNone((request) => request.method === 'PUT');   // Enter never submits the drawer

    rows(dialog)[3]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(rows(dialog).length).toBe(4);   // an empty last row adds nothing

    for (let count = 4; count < 30; count++) {
      addButton(dialog)?.click();
    }

    await fixture.whenStable();
    expect(rows(dialog).length).toBe(30);
    expect(addButton(dialog)?.disabled).toBe(true);
    expect(dialog.querySelector('.option-count')?.textContent).toContain('catalogue-b.preparations.options.count');
  });

  it('LIS-CR-015 M1: pasting several lines into a row splits them into rows', async () => {
    const { fixture, dialog } = await openFasting();
    const second = rows(dialog)[1];
    if (second === undefined) {
      throw new Error('row missing');
    }

    second.setSelectionRange(0, second.value.length);   // replace "10 h"
    paste(second, '9 h\r\n\r\n10 h\n11 h\n');
    await fixture.whenStable();
    await settle();
    expect(rows(dialog).map((row) => row.value)).toEqual(['8 h', '9 h', '10 h', '11 h', '12 h']);
    expect(document.activeElement).toBe(rows(dialog)[3]);

    await saveAndExpectPut(fixture, dialog, ['8 h', '9 h', '10 h', '11 h', '12 h']);
  });

  it('LIS-CR-015 M1: a duplicate or over-long option is marked on its row, too few options under the list', async () => {
    const { fixture, dialog } = await openFasting();
    setValue(rows(dialog)[2], ' 8 H ');
    await fixture.whenStable();
    expect(rows(dialog)[2]?.getAttribute('aria-invalid')).toBe('true');
    expect(dialog.querySelectorAll('.option-row')[2]?.querySelector('.option-error')?.textContent).toContain('catalogue-b.preparations.options.duplicate');
    expect(dialog.querySelectorAll('.option-row')[0]?.querySelector('.option-error')).toBeNull();   // the first occurrence stays valid

    rowButtons(dialog, 0)[2]?.click();   // removing the first 8 h clears the duplicate
    await fixture.whenStable();
    expect(dialog.querySelector('.option-row .option-error')).toBeNull();

    paste(rows(dialog)[0], `${'x'.repeat(101)}\nshort`);
    await fixture.whenStable();
    expect(dialog.querySelectorAll('.option-row')[0]?.querySelector('.option-error')?.textContent).toContain('catalogue-b.preparations.options.tooLong');

    dialog.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();
    await fixture.whenStable();
    backend.expectNone((request) => request.method === 'PUT');

    while (rows(dialog).length > 1) {
      rowButtons(dialog, 0)[2]?.click();
      await fixture.whenStable();
    }

    expect(dialog.querySelector('.option-list > .option-error')?.textContent).toContain('catalogue-b.error.CAT-210');
  });
});
