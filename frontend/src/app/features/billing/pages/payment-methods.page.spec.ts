import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type PaymentMethodListItem, type PaymentMethodRequest } from '../../../core/api/billing.contracts';
import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { FormValue, MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { PaymentMethodsPage } from './payment-methods.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const cash: PaymentMethodListItem = { id: 'pm-cash', code: 'CASH', nameEn: 'Cash', nameAr: 'نقدي', kind: 'Cash', requiresReference: false, isActive: true, sequence: 1 };
const card: PaymentMethodListItem = { id: 'pm-card', code: 'CARD', nameEn: 'Card', nameAr: 'بطاقة', kind: 'Card', requiresReference: true, isActive: true, sequence: 2 };

function page(items: readonly PaymentMethodListItem[]): object {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

/** The parts of the page the scenarios drive (the page keeps them protected). */
interface PageInternals {
  readonly config: {
    readonly adapter: {
      list(filter: MasterDataFilter, page: { page: number; pageSize: number }): Promise<{ items: readonly PaymentMethodListItem[] }>;
      create(dto: PaymentMethodRequest): Promise<unknown>;
      update(id: string, dto: PaymentMethodRequest): Promise<unknown>;
      setActive?(id: string, active: boolean): Promise<unknown>;
    };
    readonly toDto: (value: FormValue, item: PaymentMethodListItem | null) => PaymentMethodRequest;
  };
  addDefaults(): Promise<void>;
  exportCsv(): Promise<void>;
}

function refusal(code: string, status = 422): ProblemDetails {
  return { type: 'about:blank', title: 'Refused', status, code, traceId: null, correlationId: null, errors: null };
}

describe('PaymentMethodsPage (CP-30b)', () => {
  let backend: HttpTestingController;
  let canManage = true;
  const toasts: string[] = [];

  beforeEach(async () => {
    canManage = true;
    toasts.length = 0;
    await TestBed.configureTestingModule({
      imports: [PaymentMethodsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => key !== 'billing.manage' || canManage } satisfies Pick<PermissionsService, 'can'> },
        { provide: ToastService, useValue: { success: (text: string) => void toasts.push(text), error: (text: string) => void toasts.push(text) } },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function open(items: readonly PaymentMethodListItem[] = [cash, card]): Promise<PageInternals> {
    const fixture = TestBed.createComponent(PaymentMethodsPage);
    TestBed.tick();
    await settle();
    backend.match((candidate) => candidate.method === 'GET' && candidate.url === BILLING_API.paymentMethods).forEach((candidate) => candidate.flush(page(items)));
    await settle();
    return fixture.componentInstance as unknown as PageInternals;
  }

  it('lists the methods with the server search, status filter and sort (sortMode server)', async () => {
    const internals = await open();
    const listing = internals.config.adapter.list({ search: 'cash', active: true, sort: { key: 'code', direction: 'desc' } }, { page: 2, pageSize: 25 });
    await settle();
    const request = backend.expectOne((candidate) => candidate.method === 'GET' && candidate.url === BILLING_API.paymentMethods);
    expect(request.request.params.get('search')).toBe('cash');
    expect(request.request.params.get('isActive')).toBe('true');
    expect(request.request.params.get('sort')).toBe('code');
    expect(request.request.params.get('direction')).toBe('desc');
    expect(request.request.params.get('page')).toBe('2');
    request.flush(page([cash]));
    expect((await listing).items).toEqual([cash]);
    backend.verify();
  });

  it('asks the server for the default methods once when the unfiltered list is empty and the user may manage billing', async () => {
    const fixture = TestBed.createComponent(PaymentMethodsPage);
    TestBed.tick();
    await settle();
    backend.expectOne((candidate) => candidate.method === 'GET' && candidate.url === BILLING_API.paymentMethods).flush(page([]));
    await settle();
    backend.expectOne({ method: 'POST', url: `${BILLING_API.paymentMethods}/defaults` }).flush({ created: 4 });
    await settle();
    backend.expectOne((candidate) => candidate.method === 'GET' && candidate.url === BILLING_API.paymentMethods).flush(page([cash, card]));
    await settle();

    // a second empty answer (a search) never seeds again
    const internals = fixture.componentInstance as unknown as PageInternals;
    const listing = internals.config.adapter.list({ search: 'zzz', active: true }, { page: 1, pageSize: 50 });
    await settle();
    backend.expectOne((candidate) => candidate.method === 'GET' && candidate.url === BILLING_API.paymentMethods).flush(page([]));
    expect((await listing).items).toEqual([]);
    backend.verify();
  });

  it('never seeds the defaults for a reader without billing.manage', async () => {
    canManage = false;
    await open([]);
    backend.expectNone({ method: 'POST', url: `${BILLING_API.paymentMethods}/defaults` });
    backend.verify();
  });

  it('creates with an upper-cased code, updates without the code, deactivates and reactivates, and maps the refusal codes', async () => {
    const internals = await open();
    const dto = internals.config.toDto({ code: ' wallet ', name: { en: ' Wallet ', ar: 'محفظة' }, kind: 'Card', requiresReference: false, sequence: null }, null);
    expect(dto).toEqual({ code: 'WALLET', nameEn: 'Wallet', nameAr: 'محفظة', kind: 'Card', requiresReference: false, sequence: null });

    const creating = internals.config.adapter.create(dto);
    await settle();
    const create = backend.expectOne({ method: 'POST', url: BILLING_API.paymentMethods });
    expect(create.request.body).toEqual(dto);
    create.flush({ id: 'pm-wallet' }, { status: 201, statusText: 'Created' });
    await creating;

    const duplicate = internals.config.adapter.create(dto).catch((error: unknown) => error);
    await settle();
    backend.expectOne({ method: 'POST', url: BILLING_API.paymentMethods }).flush(refusal('BIL-121', 409), { status: 409, statusText: 'Conflict' });
    expect((await duplicate) instanceof ProblemError && ((await duplicate) as ProblemError).code).toBe('BIL-121');

    const edited = internals.config.toDto({ name: { en: 'Card', ar: 'بطاقة' }, kind: 'Card', requiresReference: true, sequence: 3 }, card);
    const updating = internals.config.adapter.update(card.id, edited);
    await settle();
    const update = backend.expectOne({ method: 'PUT', url: `${BILLING_API.paymentMethods}/pm-card` });
    expect(update.request.body).toEqual({ nameEn: 'Card', nameAr: 'بطاقة', kind: 'Card', requiresReference: true, sequence: 3 });
    update.flush(null, { status: 204, statusText: 'No Content' });
    await updating;

    const deactivating = internals.config.adapter.setActive?.(cash.id, false).catch((error: unknown) => error);
    await settle();
    backend.expectOne({ method: 'POST', url: `${BILLING_API.paymentMethods}/pm-cash/deactivate` }).flush(refusal('BIL-052', 409), { status: 409, statusText: 'Conflict' });
    const lastCash = await deactivating;
    expect(lastCash instanceof ProblemError && lastCash.code).toBe('BIL-052');

    const reactivating = internals.config.adapter.setActive?.(card.id, true);
    await settle();
    backend.expectOne({ method: 'POST', url: `${BILLING_API.paymentMethods}/pm-card/reactivate` }).flush(null, { status: 204, statusText: 'No Content' });
    await reactivating;
    backend.verify();
  });

  it('adds the defaults on demand and reports how many came, then re-reads the list', async () => {
    const internals = await open();
    const adding = internals.addDefaults();
    await settle();
    backend.expectOne({ method: 'POST', url: `${BILLING_API.paymentMethods}/defaults` }).flush({ created: 0 });
    await adding;
    await settle();
    backend.match((candidate) => candidate.method === 'GET' && candidate.url === BILLING_API.paymentMethods).forEach((candidate) => candidate.flush(page([cash, card])));
    expect(toasts).toEqual([expect.stringMatching(/defaultsNone|already exist/)]);
    backend.verify();
  });

  it('exports the CSV the server writes under the current filter', async () => {
    const internals = await open();
    const view = window as unknown as { URL: { createObjectURL?: (blob: Blob) => string; revokeObjectURL?: (url: string) => void } };
    const created: string[] = [];
    view.URL.createObjectURL = () => {
      created.push('blob:payment-methods');
      return 'blob:payment-methods';
    };
    view.URL.revokeObjectURL = () => undefined;

    const exporting = internals.exportCsv();
    await settle();
    const request = backend.expectOne((candidate) => candidate.method === 'GET' && candidate.url === `${BILLING_API.paymentMethods}/export`);
    expect(request.request.params.get('isActive')).toBe('true');
    request.flush(new Blob(['Code,Name'], { type: 'text/csv' }));
    await exporting;
    expect(created).toEqual(['blob:payment-methods']);
    backend.verify();
  });
});
