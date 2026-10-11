import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type PaymentRegisterFilter } from '../../../core/api/billing.contracts';
import { BillingApi, browserTimeZone, localCsvName } from './billing.api';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** UAT R1-06: the payments register shows local times, so its CSV asks the server for the browser's time zone. */
describe('Billing exports carry the browser time zone', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  const filter: PaymentRegisterFilter = { accession: null, fromUtc: null, toUtc: null, treasuryId: null, method: null, status: null, page: 1, pageSize: 50 };

  it('the payments export sends timeZone; the paged list does not', async () => {
    const api = TestBed.inject(BillingApi);
    const zone = browserTimeZone();
    expect(zone).toBeTruthy();

    const exporting = api.exportPayments(filter);
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === `${BILLING_API.payments}/export`);
    expect(request.request.params.get('timeZone')).toBe(zone);
    request.flush(new Blob(['x']));
    await exporting;

    const listing = api.listPayments(filter);
    await settle();
    const list = backend.expectOne((candidate) => candidate.url === BILLING_API.payments);
    expect(list.request.params.has('timeZone')).toBe(false);
    list.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    await listing;
    backend.verify();
  });


  it('names the downloaded file with the local date, not the server UTC stamp (UAT R3-05)', async () => {
    expect(localCsvName('payments', new Date(2026, 9, 11, 23, 30))).toBe('payments-2026-10-11.csv');
    const api = TestBed.inject(BillingApi);
    const exporting = api.exportRefunds({ accession: null, fromUtc: null, toUtc: null, treasuryId: null, method: null, page: 1, pageSize: 50 } as never);
    await settle();
    backend.expectOne((candidate) => candidate.url === `${BILLING_API.refunds}/export`).flush(new Blob(['x']), { headers: { 'Content-Disposition': 'attachment; filename=refunds-20261011-1449.csv' } });
    const file = await exporting;
    expect(file.fileName).toBe(localCsvName('refunds'));
  });
});
