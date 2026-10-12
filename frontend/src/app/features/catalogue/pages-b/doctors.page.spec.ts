import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, CATALOGUE_B_LOOKUP_API, type DoctorListItem, type Paged } from '../../../core/api/catalogue-b.contracts';
import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { DoctorsFacade } from '../state-b/catalogue-b-list.facade';
import { DoctorsPage } from './doctors.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const doctor: DoctorListItem = {
  id: 'd1',
  code: 'D1',
  nameEn: 'Dr. Amal',
  nameAr: 'د. أمل',
  specialty: 'Cardiology',
  occupation: null,
  branchIds: ['b1'],
  sharePercent: 12.5,
  phone: null,
  isActive: true,
};

function page(items: DoctorListItem[]): Paged<DoctorListItem> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

function setValue(element: HTMLInputElement | null | undefined, value: string): void {
  if (!element) {
    throw new Error('control not found');
  }

  element.value = value;
  element.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('DoctorsPage (CP-48)', () => {
  let backend: HttpTestingController;
  /** `null` = every permission (the manager); a set = exactly those (the reception persona of validation v0.5.0). */
  let granted: Set<string> | null;

  beforeEach(async () => {
    granted = null;
    await TestBed.configureTestingModule({
      imports: [DoctorsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        DoctorsFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => granted === null || granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<DoctorsPage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(DoctorsPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_B_LOOKUP_API.branches).flush({ items: [{ id: 'b1', code: 'MAIN', nameEn: 'Main branch', nameAr: 'الفرع الرئيسي', isActive: true }] });
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.doctors).flush(page([doctor]));
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('shows the branches of each doctor by code and the share', async () => {
    const { host } = await render();

    const row = host.querySelector('tbody tr[data-row="d1"]');
    expect(row?.textContent).toContain('MAIN');
    expect(row?.textContent).toContain('12.5 %');
  });

  it('creates a doctor offered everywhere: empty texts travel as null, the share as a number', async () => {
    const { fixture, host } = await render();

    Array.from(host.querySelectorAll<HTMLButtonElement>('app-page-header .btn.primary')).at(-1)?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    setValue(dialog?.querySelector<HTMLInputElement>('input[formcontrolname="code"]'), 'd2');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="en"]'), 'Dr. Omar');
    setValue(dialog?.querySelector<HTMLInputElement>('input[lang="ar"]'), 'د. عمر');
    setValue(dialog?.querySelector<HTMLInputElement>('input[formcontrolname="sharePercent"]'), '7.25');
    await fixture.whenStable();

    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();
    const post = backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_B_API.doctors);
    expect(post.request.body).toEqual({ code: 'D2', nameEn: 'Dr. Omar', nameAr: 'د. عمر', specialty: null, occupation: null, branchIds: [], sharePercent: 7.25, phone: null });
    post.flush({ id: 'd2' }, { status: 201, statusText: 'Created' });
    await settle();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.doctors).flush(page([doctor]));
  });

  it('refuses an invalid phone and a share above 100 on the client', async () => {
    const { fixture, host } = await render();

    host.querySelector<HTMLTableRowElement>('tbody tr[data-row="d1"]')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    setValue(dialog?.querySelector<HTMLInputElement>('input[formcontrolname="phone"]'), '12-34');
    setValue(dialog?.querySelector<HTMLInputElement>('input[formcontrolname="sharePercent"]'), '120');
    await fixture.whenStable();

    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await settle();
    await fixture.whenStable();

    backend.expectNone((request) => request.method === 'PUT');
    expect(dialog?.querySelectorAll('app-form-field .error').length).toBe(2);
  });

  it('reception (catalogue.view only) reads the selectable branches, never the register, and a row opens read-only (R-07, R-08)', async () => {
    granted = new Set(['catalogue.view']);
    const fixture = TestBed.createComponent(DoctorsPage);
    TestBed.tick();
    await settle();
    backend.expectNone((request) => request.url === CATALOGUE_B_LOOKUP_API.branches);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b1', branches: [{ id: 'b1', code: 'MAIN', nameEn: 'Main branch', nameAr: 'الفرع الرئيسي' }] });
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.doctors).flush(page([doctor]));
    await settle();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('tbody tr[data-row="d1"]')?.textContent).toContain('MAIN');
    expect(host.querySelector('tbody tr[data-row="d1"] .row-btn')).toBeNull();

    host.querySelector<HTMLTableRowElement>('tbody tr[data-row="d1"]')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog?.textContent).toContain('catalogue-b.common.view');
    expect(dialog?.querySelector<HTMLInputElement>('input[lang="en"]')?.value).toBe('Dr. Amal');
    const controls = [...(dialog?.querySelectorAll<HTMLInputElement>('form input') ?? [])];
    expect(controls.length).toBeGreaterThan(0);
    expect(controls.every((control) => control.disabled)).toBe(true);
    expect(dialog?.querySelector('.foot .btn.primary')).toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F10', bubbles: true }));
    await settle();
    backend.expectNone((request) => request.method === 'PUT');
    backend.verify();
  });
});
