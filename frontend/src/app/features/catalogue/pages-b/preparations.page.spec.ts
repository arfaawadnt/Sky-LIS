import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, type Paged, type PreparationListItem } from '../../../core/api/catalogue-b.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PreparationsFacade } from '../state-b/catalogue-b-list.facade';
import { PreparationsPage, splitOptions } from './preparations.page';

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
    setValue(dialog?.querySelector<HTMLTextAreaElement>('textarea[formcontrolname="options"]'), '8 h');
    await fixture.whenStable();

    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();
    await fixture.whenStable();
    backend.expectNone((request) => request.method === 'POST');
    expect(dialog?.textContent).toContain('catalogue-b.error.CAT-210');

    setValue(dialog?.querySelector<HTMLTextAreaElement>('textarea[formcontrolname="options"]'), '8 h\n\n12 h\n');
    await fixture.whenStable();
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
  });
});
