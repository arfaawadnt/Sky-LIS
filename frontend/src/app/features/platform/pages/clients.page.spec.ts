import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { type ClientListItem, type Paged, PLATFORM_API } from '../../../core/api/platform.contracts';
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

describe('ClientsPage (SA-04)', () => {
  let backend: HttpTestingController;
  const prompts: string[] = [];

  beforeEach(async () => {
    prompts.length = 0;
    await TestBed.configureTestingModule({
      imports: [ClientsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
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

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<ClientsPage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(ClientsPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.method === 'GET' && request.url === PLATFORM_API.clients).flush(page([client]));
    backend.expectOne((request) => request.url === PLATFORM_API.plans).flush(page([]));
    backend.expectOne((request) => request.url === PLATFORM_API.countries).flush(page([]));
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
