import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_PERMISSIONS } from '../../../core/api/catalogue-a.contracts';
import { COMPATIBLE_CONDITIONS_API, type CompatibleSampleTypeView } from '../../../core/api/compatible-conditions.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { CompatibleConditionsFacade } from '../state-a/compatible-conditions.facade';
import { CompatibleConditionsPage } from './compatible-conditions.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const types: readonly CompatibleSampleTypeView[] = [
  {
    id: 'serum',
    code: 'SERUM',
    shortName: 'SER',
    nameEn: 'Serum',
    nameAr: 'مصل',
    isActive: true,
    conditions: [
      { id: 'fast', code: 'FAST', nameEn: 'Fasting', nameAr: 'صائم', isActive: true, mergesIntoConditionId: null },
      { id: 'rand', code: 'RAND', nameEn: 'Random', nameAr: 'عشوائي', isActive: true, mergesIntoConditionId: 'fast' },
    ],
  },
  { id: 'urine', code: 'URINE', shortName: 'UR', nameEn: 'Urine', nameAr: 'بول', isActive: true, conditions: [] },
];

describe('CompatibleConditionsPage (LIS-CR-015 M4)', () => {
  let backend: HttpTestingController;
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    allowed = () => true;
    await TestBed.configureTestingModule({
      imports: [CompatibleConditionsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        CompatibleConditionsFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(CompatibleConditionsPage);
    TestBed.tick();
    await settle();
    backend.match((request) => request.method === 'GET' && request.url === COMPATIBLE_CONDITIONS_API.list).forEach((request) => request.flush(types));
    await settle();
    TestBed.tick();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function bodyRows(root: HTMLElement): HTMLTableRowElement[] {
    return Array.from(root.querySelectorAll<HTMLTableRowElement>('app-data-table tbody tr'));
  }

  it('lists every condition with its target, "Its own sample" otherwise, and a type without conditions', async () => {
    const root = await render();
    const text = bodyRows(root).map((row) => row.textContent ?? '');
    expect(text).toHaveLength(3);
    expect(text[0]).toContain('FAST · Fasting');
    expect(text[0]).toContain('catalogue-a.compatible.ownSample');
    expect(text[1]).toContain('RAND · Random');
    expect(text[1]).toContain('FAST · Fasting');
    expect(text[2]).toContain('URINE · Urine');
    expect(text[2]).toContain('catalogue-a.compatible.noConditions');
  });

  it('searches the loaded list', async () => {
    const root = await render();
    TestBed.inject(CompatibleConditionsFacade).setSearch('urin');
    TestBed.tick();
    expect(bodyRows(root)).toHaveLength(1);
  });

  it('opens the drawer of a sample type and saves its map', async () => {
    const root = await render();
    const edit = root.querySelector<HTMLButtonElement>('app-data-table .row-btn');
    expect(edit).not.toBeNull();
    edit?.click();
    TestBed.tick();
    await settle();
    TestBed.tick();

    const select = root.querySelector<HTMLSelectElement>('tr[data-condition="RAND"] select');
    expect(select?.value).toBe('fast');
    if (select) {
      select.value = '';
      select.dispatchEvent(new Event('change'));
    }
    TestBed.tick();
    root.querySelector<HTMLButtonElement>('app-compatible-conditions-drawer .btn.primary')?.click();
    await settle();

    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === COMPATIBLE_CONDITIONS_API.save('serum'));
    expect(put.request.body).toEqual({ pairs: [{ conditionId: 'fast', mergesIntoConditionId: null }, { conditionId: 'rand', mergesIntoConditionId: null }] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    backend.match((request) => request.url === COMPATIBLE_CONDITIONS_API.list).forEach((request) => request.flush(types));
  });

  it('shows no edit action without catalogue.edit', async () => {
    allowed = (key) => key !== CATALOGUE_A_PERMISSIONS.edit;
    const root = await render();
    expect(root.querySelector('app-data-table .row-btn')).toBeNull();
  });
});
