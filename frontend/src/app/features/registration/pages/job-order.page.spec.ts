import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { REGISTRATION_API, type JobOrderDocument } from '../../../core/api/registration.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PrintService } from '../../../shared/print/print.service';
import { RegistrationPrinter } from '../state/registration-printer';
import { settle, visitDetail } from '../testing/registration.fixtures';
import { JobOrderDocumentComponent } from '../ui/registration-documents';
import { JobOrderPage } from './job-order.page';

interface JobOrderPageInternals {
  print(): Promise<void>;
}

class PrinterStub {
  readonly printed: string[] = [];

  jobOrder(visitId: string): Promise<void> {
    this.printed.push(`jobOrder:${visitId}`);
    return Promise.resolve();
  }
}

function jobOrder(): JobOrderDocument {
  const detail = visitDetail();
  return {
    letterhead: { labNameEn: 'Sky Lab', labNameAr: 'سكاي لاب', branchNameEn: 'Main branch', branchNameAr: 'الفرع الرئيسي' },
    visitId: detail.id,
    accession: detail.accession,
    registeredAtUtc: detail.registeredAtUtc,
    registeredByName: 'Reception One',
    patient: detail.patient,
    visitOrdinal: 3,
    contract: detail.contract,
    doctorNameEn: 'Dr Ali',
    doctorNameAr: 'د. علي',
    urgent: true,
    biohazard: false,
    collectedOutside: false,
    batchNumber: null,
    lines: detail.lines.map((line) => ({ lineId: line.id, code: line.code, nameEn: line.nameEn, nameAr: line.nameAr, kind: line.kind, status: line.status, unitCode: 'H', sampleNumber: line.sampleNumber, sampleTypeNameEn: 'Whole blood', sampleTypeNameAr: 'دم كامل', expectedResultAtUtc: line.expectedResultAtUtc })),
    tubes: [{ id: 'tube-1', sampleNumber: 'SMP-2026-00001', unitCode: 'H', clinicalGroupId: 'g-haem', clinicalGroupNameEn: 'Haematology', clinicalGroupNameAr: 'أمراض الدم', sampleTypeId: 'st-blood', sampleTypeNameEn: 'Whole blood', sampleTypeNameAr: 'دم كامل', sampleConditionNameEn: null, sampleConditionNameAr: null, isLive: true, serviceCodes: ['CBC'] }],
    preparationAnswers: [{ preparationId: 'prep-fast', questionEn: 'Fasting?', questionAr: 'صائم؟', answer: 'Yes', answeredAtUtc: detail.registeredAtUtc }],
    balance: detail.balance,
    currency: 'EGP',
    utcOffsetMinutes: 180,
    printedBy: 'Reception One',
    printedAtUtc: '2026-10-10T09:00:00Z',
  };
}

describe('JobOrderPage (job order on screen)', () => {
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
        { provide: RegistrationPrinter, useValue: printer },
        provideRouter([{ path: 'registration/visits/:visitId/job-order', component: JobOrderPage }], withComponentInputBinding()),
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

  it('loads the document of the route, renders it with the tubes and answers, and prints it', async () => {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/registration/visits/v-1/job-order', JobOrderPage)) as unknown as JobOrderPageInternals;
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1/documents/job-order`).flush(jobOrder());
    await tick();
    harness.detectChanges();

    const host = harness.routeNativeElement!;
    expect(host.querySelector('app-reg-job-order-document')).not.toBeNull();
    expect(host.textContent).toContain('ACC-2026-00001');
    expect(host.textContent).toContain('SMP-2026-00001');
    expect(host.textContent).toContain('Fasting?');
    expect(host.textContent).toContain('Dr Ali');

    await page.print();
    expect(printer.printed).toEqual(['jobOrder:v-1']);
    backend.verify();
  });

  it('shows the load problem instead of a document', async () => {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/registration/visits/v-404/job-order', JobOrderPage);
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-404/documents/job-order`).flush({ type: 'about:blank', title: 'Not found', status: 404, code: 'REG-404' }, { status: 404, statusText: 'Not Found' });
    await tick();
    harness.detectChanges();

    expect(harness.routeNativeElement?.querySelector('[role="alert"]')).not.toBeNull();
    expect(harness.routeNativeElement?.querySelector('app-reg-job-order-document')).toBeNull();
  });
});

describe('Job order document digits (validation v0.5.0 F-10)', () => {
  it('prints Latin digits from an Arabic screen, like every other printed document', () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('ar');
    try {
      const html = TestBed.inject(PrintService).render({ type: JobOrderDocumentComponent, inputs: { document: jobOrder() } });
      expect(html).not.toMatch(/[٠-٩]/);   // no Arabic-Indic digit anywhere: dates, amounts, age, ordinal
      expect(html).toMatch(/2026/);
      expect(html).toContain('ACC-');
    } finally {
      i18n.setLanguage('en');
    }
  });
});
