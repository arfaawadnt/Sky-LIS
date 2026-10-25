import billingEn from '../../../../../public/i18n/billing.en.json';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API, type CashEntryKind, type CashLedgerRow, type CashLedgerView, type PostCashEntryRequest } from '../../../core/api/billing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService, type PromptReasonOptions } from '../../../shared/ui/dialog/confirm.service';
import { BillingPrinter } from '../state/billing-printer';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { CashLedgerFacade } from '../state/cash-ledger.facade';
import { flushCashDesk, flushReportPrivileges, ledgerRow, ledgerView, preview } from '../testing/billing.fixtures';
import type { DayCloseInput } from '../ui/day-close-dialog.component';
import { CashLedgerPage } from './cash-ledger.page';

interface LedgerInternals {
  readonly canViewBranch: () => boolean;
  readonly canAddEntries: () => boolean;
  readonly entryOpen: () => boolean;
  readonly entryKind: () => CashEntryKind;
  readonly closeOpen: () => boolean;
  readonly pagedRows: () => readonly CashLedgerRow[];
  openEntry(kind: CashEntryKind): void;
  postEntry(request: PostCashEntryRequest): Promise<void>;
  voidEntry(row: CashLedgerRow): Promise<void>;
  openClose(view: CashLedgerView): void;
  closeDay(input: DayCloseInput): Promise<void>;
  printSheet(view: CashLedgerView): Promise<void>;
  onScope(event: Event): void;
  onCashier(event: Event): void;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** Today in the browser's zone, so the page treats the fixture's day as the present (entries can be added). */
function todayValue(): string {
  const now = new Date();
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

describe('CashLedgerPage (CP-70, LIS-CR-011)', () => {
  let backend: HttpTestingController;
  const printed: string[] = [];
  const prompts: PromptReasonOptions[] = [];
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    printed.length = 0;
    prompts.length = 0;
    allowed = () => true;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
        {
          provide: BillingPrinter,
          useValue: {
            closingSheet: (id: string) => Promise.resolve(void printed.push(`closing:${id}`)),
            cashSheet: (doc: CashLedgerView) => Promise.resolve(void printed.push(`sheet:${doc.fromDay}`)),
            receipt: (id: string) => Promise.resolve(void printed.push(`receipt:${id}`)),
            refundReceipt: (id: string) => Promise.resolve(void printed.push(`refund:${id}`)),
          } satisfies Pick<BillingPrinter, 'closingSheet' | 'cashSheet' | 'receipt' | 'refundReceipt'>,
        },
        {
          provide: ConfirmService,
          useValue: {
            promptReason: (options: PromptReasonOptions) => {
              prompts.push(options);
              return Promise.resolve('typed twice');
            },
          } satisfies Pick<ConfirmService, 'promptReason'>,
        },
        provideRouter([{ path: 'billing/cash-ledger', providers: [CashDeskFacade, CashLedgerFacade], component: CashLedgerPage }]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(String(input) === '/i18n/billing.en.json' ? billingEn : {}), { status: 200 }));
    await TestBed.inject(I18nService).load('billing');
    vi.restoreAllMocks();
  });

  async function open(view: CashLedgerView, canViewBranch = false): Promise<{ page: LedgerInternals; root: HTMLElement; harness: RouterTestingHarness }> {
    const harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/billing/cash-ledger', CashLedgerPage)) as unknown as LedgerInternals;
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(view);
    flushReportPrivileges(backend, canViewBranch, null);
    flushCashDesk(backend);
    TestBed.tick();
    await settle();
    harness.detectChanges();
    return { page, root: harness.routeNativeElement as HTMLElement, harness };
  }

  it('shows the opening, the figures and the running balance of today\'s own drawer, with its actions', async () => {
    const { page, root } = await open(ledgerView({ fromDay: todayValue(), toDay: todayValue() }));

    const text = root.textContent ?? '';
    expect(text).toContain('Cash ledger');
    expect(text).toContain('CLS-2026-00003');
    expect(text).toContain('360.00');
    expect(text).toContain('Supplier · Courier');
    expect(text).toContain('Add cash out');
    expect(text).toContain('Close the day');
    expect(page.canAddEntries()).toBe(true);
    expect(page.canViewBranch()).toBe(false);
    expect(root.querySelector('select.scope-select')).toBeNull();   // no scope choice without the branch right
    expect(page.pagedRows()).toHaveLength(2);

    await page.printSheet(ledgerView());
    expect(printed).toEqual(['sheet:2026-10-09']);
  });

  it('hides the entry, void and close actions the role does not hold, and a past day offers no entry', async () => {
    allowed = (key) => key === 'billing.view';
    const { page, root } = await open(ledgerView({ closeWindow: null, rows: [ledgerRow({ kind: 'CashIn', category: 'OpeningFloat' })] }));

    const text = root.textContent ?? '';
    expect(text).not.toContain('Add cash out');
    expect(text).not.toContain('Close the day');
    expect(text).not.toContain('Export Excel');
    expect(root.querySelectorAll('tbody button').length).toBeGreaterThan(0);   // print / open stay
    expect(Array.from(root.querySelectorAll('button')).some((button) => button.textContent?.trim() === billingEn['void.action'])).toBe(false);
    expect(page.canAddEntries()).toBe(false);   // 2026-10-09 is in the past
  });

  it('posts a cash out from the dialog and voids an entry with a reason', async () => {
    const { page } = await open(ledgerView({ fromDay: todayValue(), toDay: todayValue() }));

    page.openEntry('CashOut');
    expect(page.entryOpen()).toBe(true);
    expect(page.entryKind()).toBe('CashOut');
    await Promise.all([
      page.postEntry({ kind: 'CashOut', category: 'Supplier', amount: 40, reason: 'Courier', method: 'Cash', methodCode: 'CASH', reference: null, note: null }),
      (async () => {
        await settle();
        const post = backend.expectOne((request) => request.method === 'POST' && request.url === BILLING_API.cashEntries);
        expect(post.request.body).toEqual({ kind: 'CashOut', category: 'Supplier', amount: 40, reason: 'Courier', method: 'Cash', methodCode: 'CASH', reference: null, note: null });
        post.flush({ id: 'e-2', number: 'CSH-2026-00002' }, { status: 201, statusText: 'Created' });
      })(),
    ]);
    expect(page.entryOpen()).toBe(false);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(ledgerView());
    TestBed.tick();
    await settle();

    const courier = ledgerView().rows[1]!;
    await Promise.all([
      page.voidEntry(courier),
      (async () => {
        await settle();
        const call = backend.expectOne(`${BILLING_API.cashEntries}/e-1/void`);
        expect(call.request.body).toEqual({ reason: 'typed twice' });
        call.flush(null, { status: 204, statusText: 'No Content' });
      })(),
    ]);
    expect(prompts[0]?.message).toContain('CSH-2026-00001');
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(ledgerView());
  });

  it('closes the day of the ledger\'s scope over its close window and prints the closing sheet', async () => {
    const view = ledgerView({ fromDay: todayValue(), toDay: todayValue() });
    const { page } = await open(view);

    page.openClose(view);
    expect(page.closeOpen()).toBe(true);
    TestBed.tick();
    await settle();
    const drawer = preview({ cashierUserId: 'u-1', periodFromUtc: view.closeWindow!.fromUtc, periodToUtc: view.closeWindow!.toUtc });
    backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/preview`).flush(drawer);
    TestBed.tick();
    await settle();

    await Promise.all([
      page.closeDay({ countedCash: 360, note: null }),
      (async () => {
        await settle();
        const post = backend.expectOne((request) => request.method === 'POST' && request.url === BILLING_API.cashClosings);
        expect(post.request.body).toEqual({ cashierUserId: 'u-1', periodFromUtc: drawer.periodFromUtc, periodToUtc: drawer.periodToUtc, countedCash: 360, note: null });
        post.flush({ id: 'c-9', number: 'CLS-2026-00009' }, { status: 201, statusText: 'Created' });
      })(),
    ]);
    expect(page.closeOpen()).toBe(false);
    expect(printed).toEqual(['closing:c-9']);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(ledgerView({ closeWindow: null }));
  });

  it('with the branch right, reads the whole branch and then one cashier', async () => {
    const { page, harness, root } = await open(ledgerView(), true);
    expect(page.canViewBranch()).toBe(true);
    expect(root.querySelector('select.scope-select')).not.toBeNull();

    page.onScope({ target: { value: 'Branch' } } as unknown as Event);
    TestBed.tick();
    await settle();
    const branch = backend.expectOne((request) => request.url === BILLING_API.cashLedger);
    expect(branch.request.params.get('scope')).toBe('Branch');
    expect(branch.request.params.has('cashierUserId')).toBe(false);
    branch.flush(ledgerView({ scope: 'Branch', cashierUserId: null, cashierName: null, cashiers: [{ userId: 'u-2', name: 'Cashier Two' }] }));
    TestBed.tick();
    await settle();
    harness.detectChanges();
    expect(root.textContent).toContain('Cashier Two');

    page.onCashier({ target: { value: 'u-2' } } as unknown as Event);
    TestBed.tick();
    await settle();
    const one = backend.expectOne((request) => request.url === BILLING_API.cashLedger);
    expect(one.request.params.get('cashierUserId')).toBe('u-2');
    one.flush(ledgerView({ scope: 'Branch', cashierUserId: 'u-2', cashierName: 'Cashier Two' }));
  });
});
