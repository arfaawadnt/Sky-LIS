import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { FormGroup } from '@angular/forms';

import { type ClientDetail, type ClientListItem, type CountryListItem, type Paged, type PlanListItem, PLATFORM_API } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ClientsFacade } from '../state/clients.facade';
import { ClientsPage } from './clients.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const client: ClientListItem = {
  id: 't1',
  nameEn: 'Al Noor Lab',
  nameAr: 'معمل النور',
  portalSlug: 'al-noor',
  portalUrl: 'https://al-noor.skylis.app',
  countryId: 'eg',
  countryNameEn: 'Egypt',
  countryNameAr: 'مصر',
  governorateId: 'cairo',
  governorateNameEn: 'Cairo',
  governorateNameAr: 'القاهرة',
  managerName: 'Dr. Mona',
  managerMobile: '+201112223333',
  planId: 'p1',
  planCode: 'STARTER',
  planNameEn: 'Starter',
  planNameAr: 'الأساسية',
  walletBalance: -20,
  walletCurrency: 'EGP',
  branches: 1,
  users: 2,
  createdAtUtc: '2026-10-09T09:00:00Z',
  source: 'Subscription',
  status: 'Trial',
  trialEndsAtUtc: '2026-10-23T09:00:00Z',
};

const egypt: CountryListItem = {
  id: 'eg', nameEn: 'Egypt', nameAr: 'مصر', isoCode: 'EG', dialCode: '+20', currency: 'Pound', currencyCode: 'EGP', language: 'ar',
  timeZone: 'Africa/Cairo', mobileLength: 10, flag: null, remarks: null, isActive: true, governorateCount: 1,
};

function plan(id: string, code: string, isActive: boolean): PlanListItem {
  return { id, code, nameEn: code, nameAr: code, monthlyFee: 100, currencyCode: 'EGP', maxBranches: 1, maxUsers: 5, smsQuota: 0, maxAnalysers: 0, isActive, clientCount: 0 };
}

const detail: ClientDetail = {
  id: 't1', nameEn: 'Al Noor Lab', nameAr: 'معمل النور', countryId: 'eg', governorateId: 'cairo', address: 'Street 1', mobile: '+201012345678',
  licenceNo: null, managerName: 'Dr. Mona', managerMobile: '+201112223333', email: null, planId: 'p-old', portalSlug: 'al-noor',
  portalUrl: 'https://al-noor.skylis.app', language: 'en', source: 'Manual', status: 'Active', createdAtUtc: '2026-10-09T09:00:00Z',
  trialEndsAtUtc: null, activatedAtUtc: null, deactivatedAtUtc: null, deactivationReason: null, administratorUserId: null,
  administratorUsername: 'noor.admin', walletBalance: 0, walletCurrency: 'EGP', branches: 1, users: 1,
};

interface DrawerAccess {
  form(): FormGroup | null;
  openCreate(): void;
  openEdit(row: ClientListItem): Promise<void>;
  onCountryChange(form: FormGroup): void;
  save(): Promise<void>;
  saveProblem(): unknown;
}

describe('ClientsPage (SA-04)', () => {
  let backend: HttpTestingController;
  const prompts: string[] = [];

  beforeEach(async () => {
    prompts.length = 0;
    await TestBed.configureTestingModule({
      imports: [ClientsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        ClientsFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        {
          provide: ConfirmService,
          useValue: {
            confirm: () => Promise.resolve(true),
            promptReason: (options: { title: string }) => {
              prompts.push(options.title);
              return Promise.resolve('unpaid invoices');
            },
          } satisfies Pick<ConfirmService, 'confirm' | 'promptReason'>,
        },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(plans: PlanListItem[] = [], countries: CountryListItem[] = []): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ClientsPage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(ClientsPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === PLATFORM_API.clients).flush(page([client]));
    backend.expectOne((request) => request.url === PLATFORM_API.plans).flush(page(plans));
    backend.expectOne((request) => request.url === PLATFORM_API.countries).flush(page(countries));
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('shows the client with its portal, manager mobile and a negative wallet', async () => {
    const { host } = await render();

    const row = host.querySelector('tbody tr[data-row="t1"]');
    expect(row?.textContent).toContain('Al Noor Lab');
    expect(row?.textContent).toContain('https://al-noor.skylis.app');
    expect(row?.textContent).toContain('+201112223333');
    expect(row?.querySelector('.amount-negative')).not.toBeNull();
  });

  it('C-07: Add client offers active plans only; Edit keeps the current inactive plan, marked inactive', async () => {
    const { fixture, host } = await render([plan('p1', 'STARTER', true), plan('p-old', 'LEGACY', false)], [egypt]);
    const view = fixture.componentInstance as unknown as DrawerAccess;

    view.openCreate();
    await fixture.whenStable();
    const options = (): string[] => Array.from(document.querySelectorAll<HTMLOptionElement>('select[formcontrolname="planId"] option')).map((option) => option.textContent?.trim() ?? '');
    expect(options().some((text) => text.startsWith('STARTER'))).toBe(true);
    expect(options().some((text) => text.startsWith('LEGACY'))).toBe(false);

    const opening = view.openEdit(client);
    await settle();
    backend.expectOne(`${PLATFORM_API.clients}/t1`).flush(detail);
    await opening;
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === PLATFORM_API.governorates).forEach((request) => request.flush(page([])));
    await fixture.whenStable();
    const legacy = options().find((text) => text.startsWith('LEGACY'));
    expect(legacy).toMatch(/inactive|planInactive/);   // the text, or its key when the dictionary is not loaded
    expect(host).toBeTruthy();
  });

  it('C-03: the mobiles are checked against the chosen country and a GEO-014 refusal lands on the manager mobile', async () => {
    const { fixture } = await render([plan('p1', 'STARTER', true)], [egypt]);
    const view = fixture.componentInstance as unknown as DrawerAccess;
    view.openCreate();
    const form = view.form();
    if (form === null) {
      throw new Error('no form');
    }

    form.patchValue({ countryId: 'eg' });
    view.onCountryChange(form);
    await settle();
    backend.match((request) => request.url === PLATFORM_API.governorates).forEach((request) => request.flush(page([])));

    const mobile = form.get('mobile');
    const manager = form.get('managerMobile');
    mobile?.setValue('0233351111');          // a landline: 9 national digits
    manager?.setValue('+2010111100');        // 8 digits after +20
    expect(mobile?.errors?.['server']).toMatch(/10|validation\.mobileLength/);   // the text, or its key when the dictionary is not loaded
    expect(manager?.errors?.['server']).toMatch(/\+20|validation\.managerMobileLength/);
    mobile?.setValue('01012345678');
    manager?.setValue('+20 111 222 3333');
    expect(mobile?.valid).toBe(true);
    expect(manager?.valid).toBe(true);

    form.patchValue({
      name: { en: 'New Lab', ar: 'معمل جديد' }, governorateId: 'cairo', address: 'Street 1', managerName: 'Dr. Mona', planId: 'p1',
      adminUsername: 'new.admin', adminPassword: 'a long enough password', adminPasswordConfirm: 'a long enough password',
    });
    expect(form.valid).toBe(true);
    const saving = view.save();
    await settle();
    backend.expectOne({ method: 'POST', url: PLATFORM_API.clients }).flush(
      { type: 'about:blank', title: 'Unprocessable', status: 422, code: 'GEO-014', detail: "The manager's mobile in EG has 10 digits after +20." },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await saving;
    expect(manager?.errors?.['server']).toBeTruthy();
    expect(view.saveProblem()).toBeNull();
  });

  it('LIS-CR-007 C-05: the mobiles show the chosen country\'s dial code and keep the E.164 form without the leading 0', async () => {
    const { fixture } = await render([plan('p1', 'STARTER', true)], [egypt]);
    const view = fixture.componentInstance as unknown as DrawerAccess;
    view.openCreate();
    const form = view.form();
    if (form === null) {
      throw new Error('no form');
    }

    form.patchValue({ countryId: 'eg' });
    view.onCountryChange(form);
    await settle();
    backend.match((request) => request.url === PLATFORM_API.governorates).forEach((request) => request.flush(page([])));
    await fixture.whenStable();

    const dial = document.querySelector('app-mobile-input[formcontrolname="mobile"] .dial');
    expect(dial?.textContent).toContain('+20');
    const input = document.querySelector<HTMLInputElement>('app-mobile-input[formcontrolname="managerMobile"] input.number');
    if (input === null) {
      throw new Error('no manager mobile');
    }

    input.value = '0111 222 3333';
    input.dispatchEvent(new Event('input'));
    expect(form.get('managerMobile')?.value).toBe('+201112223333');
    expect(form.get('managerMobile')?.valid).toBe(true);
  });

  it('deactivates a trial client with the reason the operator typed', async () => {
    const { fixture, host } = await render();

    const power = host.querySelector<HTMLButtonElement>('tbody tr[data-row="t1"] .row-btn.danger');
    power?.click();
    await settle();
    await fixture.whenStable();

    expect(prompts.length).toBe(1);
    const post = backend.expectOne(`${PLATFORM_API.clients}/t1/deactivate`);
    expect(post.request.body).toEqual({ reason: 'unpaid invoices' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === PLATFORM_API.clients).flush(page([{ ...client, status: 'Inactive' }]));
    backend.verify();
  });
});
