import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { REGISTRATION_API, type CancelLinesRequest, type VisitListItem } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { VisitsFacade } from '../state/visits.facade';
import { cashTier, page as pageOf, settle, visitRow } from '../testing/registration.fixtures';
import { VisitsPage } from './visits.page';

/** The parts of the page the scenarios drive (protected in the page). */
interface VisitsPageInternals {
  openCancel(row: VisitListItem): void;
  cancelServices(input: { visitId: string; request: CancelLinesRequest }): Promise<void>;
  results(row: VisitListItem): void;
  payments(row: VisitListItem): void;
  readonly cancelOpen: () => boolean;
}

@Component({ selector: 'app-spec-blank', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BlankComponent {}

let granted = new Set<string>();

describe('VisitsPage (CP-10)', () => {
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    granted = new Set(['registration.view', 'registration.create', 'registration.edit', 'registration.cancel', 'registration.export', 'samples.view', 'billing.view', 'results.view']);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: (key: string) => granted.has(key) } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        provideRouter([
          { path: 'registration/visits', providers: [VisitsFacade], component: VisitsPage },
          { path: 'results/by-accession', component: BlankComponent },
          { path: 'billing/payments', component: BlankComponent },
        ]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function open(rows: readonly VisitListItem[] = [visitRow()]): Promise<VisitsPageInternals> {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/registration/visits', VisitsPage)) as unknown as VisitsPageInternals;
    await tick();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf(rows));
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([cashTier]);
    await tick();
    harness.detectChanges();
    return page;
  }

  it('lists the visits with the services styled by status, the balance and every row action for a full role', async () => {
    await open();
    const host = harness.routeNativeElement!;
    const row = host.querySelector('tbody tr[data-row]');
    expect(row).not.toBeNull();
    expect(row?.textContent).toContain('ACC-2026-00001');
    expect(row?.textContent).toContain('Mona Adel');
    expect(row?.querySelector('.chip.cancelled')?.textContent).toBe('GLU');
    expect(row?.querySelector('.chip:not(.cancelled)')?.textContent).toBe('CBC');
    expect(row?.textContent).toContain('60.00');
    expect(row?.querySelectorAll('button').length).toBe(6);
    expect(host.querySelector('app-page-header .btn.primary')).not.toBeNull();
  });

  it('offers only the documents to a read-only role and masks VIP patients', async () => {
    granted = new Set(['registration.view']);
    await open([visitRow({ patientMasked: true, patientName: null, mobile: null, remaining: 0 })]);
    const host = harness.routeNativeElement!;
    const row = host.querySelector('tbody tr[data-row]');
    expect(row?.querySelectorAll('button').length).toBe(1);
    expect(row?.textContent).toContain('registration.vipMasked');
    expect(row?.textContent).not.toContain('Mona');
    expect(row?.textContent).toContain('registration.visits.settled');
    expect(host.querySelector('app-page-header .btn.primary')).toBeNull();
  });

  it('cancels services from the dialog, closes it and reloads the register', async () => {
    const page = await open();
    const row = visitRow();
    page.openCancel(row);
    expect(page.cancelOpen()).toBe(true);

    await Promise.all([
      page.cancelServices({ visitId: row.id, request: { lineIds: ['l-1'], reasonId: 'r-cancel', reason: null } }),
      (async () => {
        await settle();
        const post = backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`);
        expect(post.request.body).toEqual({ lineIds: ['l-1'], reasonId: 'r-cancel', reason: null });
        post.flush(null, { status: 204, statusText: 'No Content' });
      })(),
    ]);

    expect(page.cancelOpen()).toBe(false);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
    await tick();
    backend.match((request) => request.url === REGISTRATION_API.visits || request.url.startsWith(`${REGISTRATION_API.visits}/v-1`) || request.url.includes('/catalogue/reasons')).forEach((call) => call.flush(pageOf([])));
  });

  it('keeps the cancel dialog open with the refusal code', async () => {
    const page = await open();
    page.openCancel(visitRow());

    await Promise.all([
      page.cancelServices({ visitId: 'v-1', request: { lineIds: ['l-1'], reasonId: null, reason: 'typed' } }),
      (async () => {
        await settle();
        backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`).flush({ type: 'about:blank', title: 'Verified', status: 409, code: 'REG-106' }, { status: 409, statusText: 'Conflict' });
      })(),
    ]);

    expect(page.cancelOpen()).toBe(true);
    expect(harness.routeDebugElement!.injector.get(VisitsFacade).cancel.problem()?.code).toBe('REG-106');
  });

  it('links a row to the results by accession and to the cash desk by visit', async () => {
    const page = await open();
    const router = TestBed.inject(Router);

    page.results(visitRow());
    await settle();
    expect(router.url).toBe('/results/by-accession?accession=ACC-2026-00001');

    page.payments(visitRow());
    await settle();
    expect(router.url).toBe('/billing/payments?visitId=v-1');
  });
});
