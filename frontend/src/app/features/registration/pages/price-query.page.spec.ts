import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { REGISTRATION_API, type PriceQuoteDocument } from '../../../core/api/registration.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PriceQueryFacade } from '../state/price-query.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { cashTier, insurerTier, settle } from '../testing/registration.fixtures';
import type { PickedService } from '../ui/service-entry.component';
import { PriceQueryPage } from './price-query.page';

interface PriceQueryPageInternals {
  onAdded(services: readonly PickedService[]): void;
  print(): Promise<void>;
  continueToRegistration(): void;
}

@Component({ selector: 'app-spec-blank', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BlankComponent {}

class PrinterStub {
  readonly printed: string[] = [];

  quote(document: PriceQuoteDocument): Promise<void> {
    this.printed.push(`quote:${document.lines.length}`);
    return Promise.resolve();
  }
}

const quote: PriceQuoteDocument = {
  letterhead: { labNameEn: 'Sky Lab', labNameAr: 'سكاي لاب', branchNameEn: null, branchNameAr: null },
  contract: { payerId: 'payer-cash', payerNameEn: 'Cash', payerNameAr: 'نقدي', contractTierId: 'tier-cash', tierNameEn: 'Standard', tierNameAr: 'قياسي' },
  currency: 'EGP',
  lines: [
    { serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: '', type: 'Test', priced: true, listPrice: 120, patientShare: 100, payerShare: 20, expectedResultAtUtc: '2026-10-11T08:00:00Z', precaution: 'Fasting 8 h', alertCondition: null },
    { serviceId: 's-x', code: 'XRAY', nameEn: 'Not a lab service', nameAr: '', type: 'Test', priced: false, listPrice: 0, patientShare: 0, payerShare: 0, expectedResultAtUtc: null, precaution: null, alertCondition: null },
  ],
  totalListPrice: 120,
  totalPatientShare: 100,
  totalPayerShare: 20,
  preparations: [{ preparationId: 'prep-fast', code: 'FAST', questionEn: 'Fasting?', questionAr: 'صائم؟', answerType: 'YesNo', options: [], gender: 'Any', serviceIds: ['s-cbc'] }],
  issues: [{ code: 'REG-014', serviceId: 's-x', serviceCode: 'XRAY', message: 'Unpriced' }],
  utcOffsetMinutes: 180,
  printedBy: null,
  printedAtUtc: '2026-10-10T08:00:00Z',
};

describe('PriceQueryPage (CP-12, WF-10)', () => {
  let backend: HttpTestingController;
  let printer: PrinterStub;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    printer = new PrinterStub();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: RegistrationPrinter, useValue: printer },
        provideRouter([
          { path: 'registration/price-query', providers: [PriceQueryFacade], component: PriceQueryPage },
          { path: 'registration/new', component: BlankComponent },
        ]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  it('quotes the typed services on the default cash tier, shows the lines, totals, preparations and refusals, prints, and hands over to registration', async () => {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/registration/price-query', PriceQueryPage)) as unknown as PriceQueryPageInternals;
    await tick();
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([insurerTier, cashTier]);
    await tick();
    const facade = harness.routeDebugElement!.injector.get(PriceQueryFacade);
    expect(facade.tierId()).toBe('tier-cash');

    page.onAdded([{ serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: '' }, { serviceId: 's-x', code: 'XRAY', nameEn: 'Not a lab service', nameAr: '' }]);
    await tick();
    const post = backend.expectOne(REGISTRATION_API.quote);
    expect(post.request.body).toEqual(expect.objectContaining({ payerId: 'payer-cash', contractTierId: 'tier-cash', serviceIds: ['s-cbc', 's-x'], gender: null }));
    post.flush(quote);
    await tick();
    harness.detectChanges();

    const host = harness.routeNativeElement!;
    expect(host.querySelectorAll('table.lines tbody tr').length).toBe(2);
    expect(host.textContent).toContain('Fasting 8 h');
    expect(host.textContent).toContain('registration.quote.unpriced');
    expect(host.textContent).toContain('Fasting?');
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('XRAY');
    expect(host.querySelector('tfoot')?.textContent).toContain('100.00');

    await page.print();
    expect(printer.printed).toEqual(['quote:2']);

    page.continueToRegistration();
    await settle();
    expect(TestBed.inject(Router).url).toBe('/registration/new?services=s-cbc,s-x&tierId=tier-cash');
    backend.verify();
  });
});
