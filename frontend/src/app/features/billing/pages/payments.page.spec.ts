import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API, type CollectionsReportDocument, type CollectionsReportRequest, type PaymentListItem, type VisitAccountView } from '../../../core/api/billing.contracts';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { BillingPrinter } from '../state/billing-printer';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { PaymentsFacade } from '../state/payments.facade';
import { account, collectionsReport, flushCashDesk, flushReportPrivileges } from '../testing/billing.fixtures';
import type { MovementInput } from '../ui/movement-form.component';
import { PaymentsPage } from './payments.page';

/** The parts of the page the scenarios drive (protected in the page). */
interface PaymentsPageInternals {
  pay(account: VisitAccountView, input: MovementInput): Promise<void>;
  voidPayment(row: PaymentListItem): Promise<void>;
  canVoid(row: PaymentListItem): boolean;
  onTab(id: string | null): void;
  openCollections(): void;
  loadCollections(request: CollectionsReportRequest): Promise<unknown>;
  printCollections(request: CollectionsReportRequest): Promise<void>;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const posted: PaymentListItem = {
  id: 'pay-1',
  receiptNumber: 'RCT-2026-00001',
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  amount: 100,
  method: 'Cash',
  reference: null,
  treasuryId: 't-1',
  treasuryCode: 'MAIN',
  treasuryNameEn: 'Main drawer',
  treasuryNameAr: 'الخزينة الرئيسية',
  cashierUserId: 'u-1',
  cashierName: 'Cashier One',
  branchId: 'b-1',
  deviceId: 'device-test',
  batchId: null,
  paidAtUtc: '2026-10-09T09:00:00Z',
  status: 'Posted',
  isFrozen: false,
  voidReason: null,
  voidedByName: null,
  voidedAtUtc: null,
};

class PrinterStub {
  readonly printed: string[] = [];

  receipt(id: string): Promise<void> {
    this.printed.push(`receipt:${id}`);
    return Promise.resolve();
  }

  invoice(id: string): Promise<void> {
    this.printed.push(`invoice:${id}`);
    return Promise.resolve();
  }

  collectionsReport(doc: CollectionsReportDocument): Promise<void> {
    this.printed.push(`collections:${doc.scope}:${doc.groupBy}:${doc.fromDay}`);
    return Promise.resolve();
  }
}

describe('PaymentsPage (CP-23)', () => {
  let backend: HttpTestingController;
  let printer: PrinterStub;

  beforeEach(() => {
    printer = new PrinterStub();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: BillingPrinter, useValue: printer },
        { provide: ConfirmService, useValue: { promptReason: () => Promise.resolve('wrong visit'), confirm: () => Promise.resolve(true) } satisfies Pick<ConfirmService, 'promptReason' | 'confirm'> },
        provideRouter([{ path: 'billing', providers: [CashDeskFacade], children: [{ path: 'payments', providers: [PaymentsFacade], component: PaymentsPage }] }], withComponentInputBinding()),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  let harness: RouterTestingHarness;

  async function open(url: string, autoPrintReceipt = true): Promise<PaymentsPageInternals> {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl(url, PaymentsPage)) as unknown as PaymentsPageInternals;
    TestBed.tick();
    await settle();
    flushCashDesk(backend, autoPrintReceipt);
    TestBed.tick();
    await settle();
    return page;
  }

  it('opens the visit of the link (BR-26), pays, and prints the receipt when autoPrintReceipt is on', async () => {
    const page = await open('/billing/payments?visitId=v-1');
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ payments: [posted] }));
    TestBed.tick();
    await settle();

    await Promise.all([
      page.pay(account(), { treasuryId: 't-1', amount: 200, method: 'Cash', reference: null, reasonId: null, note: null }),
      (async () => {
        await settle();
        const post = backend.expectOne(BILLING_API.payments);
        expect(post.request.body).toEqual({ visitId: 'v-1', amount: 200, method: 'Cash', reference: null, treasuryId: 't-1', deviceId: 'device-test' });
        post.flush({ id: 'pay-2', receiptNumber: 'RCT-2026-00002' }, { status: 201, statusText: 'Created' });
      })(),
    ]);

    expect(printer.printed).toEqual(['receipt:pay-2']);
    TestBed.tick();
    await settle();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ paid: 300, remaining: 0, balanceStatus: 'Paid' }));
  });

  it('does not print the receipt when the laboratory turned autoPrintReceipt off (UAT A-05)', async () => {
    const page = await open('/billing/payments?visitId=v-1', false);
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ payments: [posted] }));
    TestBed.tick();
    await settle();

    await Promise.all([
      page.pay(account(), { treasuryId: 't-1', amount: 200, method: 'Cash', reference: null, reasonId: null, note: null }),
      (async () => {
        await settle();
        backend.expectOne(BILLING_API.payments).flush({ id: 'pay-3', receiptNumber: 'RCT-2026-00003' }, { status: 201, statusText: 'Created' });
      })(),
    ]);

    expect(printer.printed).toEqual([]);
  });

  it('voids a posted payment with the reason of the prompt and never offers a frozen one', async () => {
    const page = await open('/billing/payments?visitId=v-1');
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ payments: [posted] }));
    TestBed.tick();
    await settle();

    expect(page.canVoid(posted)).toBe(true);
    expect(page.canVoid({ ...posted, isFrozen: true })).toBe(false);
    expect(page.canVoid({ ...posted, status: 'Voided' })).toBe(false);

    await Promise.all([
      page.voidPayment(posted),
      (async () => {
        await settle();
        const post = backend.expectOne(`${BILLING_API.payments}/pay-1/void`);
        expect(post.request.body).toEqual({ reason: 'wrong visit' });
        post.flush(null, { status: 204, statusText: 'No Content' });
      })(),
    ]);
    TestBed.tick();
    await settle();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account());
  });

  it('looks a visit up by accession from the link and shows the not-found code', async () => {
    await open('/billing/payments?accession=ACC-404');
    backend.expectOne(`${BILLING_API.visits}/by-accession/ACC-404/account`).flush({ type: 'about:blank', title: 'Not found', status: 404, code: 'BIL-404' }, { status: 404, statusText: 'Not Found' });
    TestBed.tick();
    await settle();

    const facade = harness.routeDebugElement!.injector.get(PaymentsFacade);
    expect((facade.account.error() as { code?: string } | undefined)?.code).toBe('BIL-404');
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')).not.toBeNull();
    backend.verify();
  });

  // ---- collections (treasury) report, change request 2026-10-13 item 2 ---------------------------------------------

  /** Opens the register tab (its list and treasuries load) and the collections dialog; answers the register's own reads. */
  async function openCollectionsDialog(page: PaymentsPageInternals): Promise<void> {
    page.onTab('register');
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === BILLING_API.payments).flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    backend.expectOne(BILLING_API.treasuries).flush([]);
    page.openCollections();
    TestBed.tick();
    await settle();
  }

  it('opens the collections dialog on the register tab, reads the member\'s rights and offers the whole branch only with the right', async () => {
    const page = await open('/billing/payments');
    await openCollectionsDialog(page);

    flushReportPrivileges(backend, false, 0);   // today only, own payments only
    TestBed.tick();
    await settle();
    harness.detectChanges();
    const dialog = harness.routeNativeElement!.querySelector('app-billing-collections-report-dialog')!;
    expect(dialog.querySelector('[role="dialog"]')).not.toBeNull();
    expect(dialog.querySelector('input[type="radio"][value="User"]')).not.toBeNull();
    expect(dialog.querySelector('input[type="radio"][value="Branch"]')).toBeNull();
    const from = dialog.querySelector<HTMLInputElement>('input[type="date"]')!;
    expect(from.value).toBe(from.getAttribute('min'));   // the earliest allowed day is today, which is also the default
    expect(dialog.querySelector('select')?.querySelectorAll('option').length).toBe(3);
    expect((dialog.querySelector('select') as HTMLSelectElement).value).toBe('Method');

    const facade = harness.routeDebugElement!.injector.get(PaymentsFacade);
    facade.closeCollections();
    facade.openCollections();
    TestBed.tick();
    await settle();
    flushReportPrivileges(backend, true, null);
    TestBed.tick();
    await settle();
    harness.detectChanges();
    expect(dialog.querySelector('input[type="radio"][value="Branch"]')).not.toBeNull();
    expect(dialog.querySelector('input[type="date"]')?.getAttribute('min')).toBeNull();
    backend.verify();
  });

  it('loads the report with the chosen period, scope and grouping, shows its rows, and prints the grouping asked for', async () => {
    const page = await open('/billing/payments');
    await openCollectionsDialog(page);
    flushReportPrivileges(backend, true, null);
    TestBed.tick();
    await settle();

    const request: CollectionsReportRequest = { fromDay: '2026-10-12', toDay: '2026-10-13', scope: 'Branch', groupBy: 'User' };
    const loading = page.loadCollections(request);
    await settle();
    const get = backend.expectOne((candidate) => candidate.url === BILLING_API.collectionsReport);
    expect([get.request.params.get('from'), get.request.params.get('to'), get.request.params.get('scope'), get.request.params.get('groupBy')]).toEqual(['2026-10-12', '2026-10-13', 'Branch', 'User']);
    get.flush(collectionsReport({ scope: 'Branch', groupBy: 'User', fromDay: '2026-10-12', cashierName: null, rows: [collectionsReport().rows[0]!, { ...collectionsReport().rows[1]!, key: 'u-2', labelEn: 'Cashier Two', labelAr: 'الصراف الثاني' }] }));
    await loading;
    TestBed.tick();
    await settle();
    harness.detectChanges();
    const table = harness.routeNativeElement!.querySelector('app-billing-collections-report-dialog table.report')!;
    expect(table.querySelectorAll('tbody tr').length).toBe(2);
    expect(table.textContent).toContain('Cashier Two');
    expect(table.querySelector('tfoot')?.textContent).toContain('350.00');
    expect(printer.printed).toEqual([]);

    const printing = page.printCollections({ ...request, groupBy: 'Method' });
    await settle();
    const again = backend.expectOne((candidate) => candidate.url === BILLING_API.collectionsReport);
    expect(again.request.params.get('groupBy')).toBe('Method');
    again.flush(collectionsReport({ scope: 'Branch', groupBy: 'Method', fromDay: '2026-10-12' }));
    await printing;
    expect(printer.printed).toEqual(['collections:Branch:Method:2026-10-12']);

    const refused = page.printCollections({ ...request, fromDay: '2026-01-01' });
    await settle();
    backend.expectOne((candidate) => candidate.url === BILLING_API.collectionsReport).flush({ type: 'about:blank', title: 'Refused', status: 403, code: 'BIL-045' }, { status: 403, statusText: 'Forbidden' });
    await refused;
    expect(printer.printed.length).toBe(1);
    const facade = harness.routeDebugElement!.injector.get(PaymentsFacade);
    expect(facade.collectionsReport.problem()?.code).toBe('BIL-045');
    backend.verify();
  });
});
