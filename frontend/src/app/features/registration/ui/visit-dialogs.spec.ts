import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API } from '../../../core/api/catalogue-b.contracts';
import { REGISTRATION_API, type CancelLinesRequest } from '../../../core/api/registration.contracts';
import { SAMPLES_API, type SampleListItem } from '../../../core/api/samples.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { RegistrationPrinter } from '../state/registration-printer';
import { page, settle, visitDetail } from '../testing/registration.fixtures';
import { CancelServicesDialogComponent, VisitSamplesDialogComponent } from './visit-dialogs';

const sample: SampleListItem = {
  id: 'smp-1',
  sampleNumber: 'SMP-2026-00001',
  tubeId: 'tube-1',
  generation: 1,
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  patientId: 'p-1',
  patientNumber: 'PAT-2026-00001',
  patientName: 'Mona Adel',
  patientNameAr: null,
  branchId: 'b-1',
  clinicalGroupId: 'g-haem',
  unitCode: 'H',
  clinicalGroupNameEn: 'Haematology',
  clinicalGroupNameAr: null,
  sampleTypeId: 'st-blood',
  sampleTypeNameEn: 'Whole blood',
  sampleTypeNameAr: null,
  sampleConditionId: null,
  sampleConditionNameEn: null,
  sampleConditionNameAr: null,
  serviceCount: 1,
  status: 'Received',
  orderedAtUtc: '2026-10-10T08:00:00Z',
  statusChangedAtUtc: '2026-10-10T08:05:00Z',
  collectedAtUtc: '2026-10-10T08:01:00Z',
  receivedAtUtc: '2026-10-10T08:05:00Z',
  rejectionReasonId: null,
  rejectionReasonNameEn: null,
  rejectionReasonNameAr: null,
  rejectionNote: null,
  rejectedAtUtc: null,
  replacedBySampleId: null,
  replacesSampleId: null,
  collectedOutside: true,
};

class PrinterStub {
  readonly printed: string[] = [];

  labels(visitId: string, tubeId: string | null = null): Promise<number> {
    this.printed.push(`labels:${visitId}:${tubeId ?? '*'}`);
    return Promise.resolve(1);
  }
}

async function tick(): Promise<void> {
  TestBed.tick();
  await settle();
}

describe('CancelServicesDialogComponent (CP-10 cancel services, BR-15)', () => {
  let fixture: ComponentFixture<CancelServicesDialogComponent>;
  let backend: HttpTestingController;
  let submitted: { visitId: string; request: CancelLinesRequest }[];

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CancelServicesDialogComponent);
    fixture.componentRef.setInput('visitId', 'v-1');
    fixture.componentRef.setInput('accession', 'ACC-2026-00001');
    submitted = [];
    fixture.componentInstance.submitted.subscribe((value) => submitted.push(value));
    await fixture.whenStable();
  });

  async function openDialog(): Promise<HTMLElement> {
    fixture.componentInstance.open.set(true);
    await tick();
    const member = { ...visitDetail().lines[0]!, id: 'l-m', serviceId: 's-m', code: 'HB', kind: 'PackageMember' as const, parentLineId: 'l-1' };
    const cancelled = { ...visitDetail().lines[0]!, id: 'l-c', serviceId: 's-c', code: 'GLU', status: 'Cancelled' as const, cancellationReason: 'Wrong order', cancelledByName: 'Super', cancelledAtUtc: '2026-10-10T09:00:00Z' };
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail({ lines: [visitDetail().lines[0]!, member, cancelled] }));
    const reasons = backend.expectOne((request) => request.url === CATALOGUE_B_API.reasons);
    expect(reasons.request.params.get('kind')).toBe('Cancellation');
    reasons.flush(page([{ id: 'r-cancel', kind: 'Cancellation', code: 'WRONG', nameEn: 'Wrong order', nameAr: 'طلب خاطئ', isActive: true }]));
    await tick();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('loads nothing while closed, then lists the top-level lines (a package cancels with its members) with cancelled ones disabled', async () => {
    backend.expectNone((request) => request.url.startsWith(REGISTRATION_API.visits));
    const host = await openDialog();

    const boxes = host.querySelectorAll<HTMLInputElement>('.lines input[type="checkbox"]');
    expect(boxes.length).toBe(2);
    expect(host.textContent).not.toContain('HB');
    expect(boxes[1]?.disabled).toBe(true);
    expect(host.textContent).toContain('Wrong order');
    expect(host.querySelector<HTMLButtonElement>('button.danger')?.disabled).toBe(true);
  });

  it('needs a selection and a reason or a note, then emits the request', async () => {
    const host = await openDialog();
    const box = host.querySelector<HTMLInputElement>('.lines input[type="checkbox"]')!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('button.danger')?.disabled).toBe(true);

    const note = host.querySelector<HTMLInputElement>('input[type="text"]')!;
    note.value = '  patient left  ';
    note.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const submit = host.querySelector<HTMLButtonElement>('button.danger')!;
    expect(submit.disabled).toBe(false);
    submit.click();

    expect(submitted).toEqual([{ visitId: 'v-1', request: { lineIds: ['l-1'], reasonId: null, reason: 'patient left' } }]);
  });

  it('shows the refusal the page hands back', async () => {
    const host = await openDialog();
    fixture.componentRef.setInput('problem', 'Cancel right required');
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Cancel right required');
  });
});

describe('VisitSamplesDialogComponent (CP-10 samples, R1)', () => {
  let fixture: ComponentFixture<VisitSamplesDialogComponent>;
  let backend: HttpTestingController;
  let printer: PrinterStub;

  beforeEach(async () => {
    printer = new PrinterStub();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), { provide: RegistrationPrinter, useValue: printer }],
    });
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(VisitSamplesDialogComponent);
    fixture.componentRef.setInput('visitId', 'v-1');
    fixture.componentRef.setInput('accession', 'ACC-2026-00001');
    await fixture.whenStable();
  });

  it('lists the samples of the visit once opened and prints one or every label', async () => {
    backend.expectNone((request) => request.url === SAMPLES_API.samples);
    fixture.componentInstance.open.set(true);
    await tick();
    const call = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect(call.request.params.get('visitId')).toBe('v-1');
    call.flush(page([sample]));
    await tick();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('table.samples tbody tr').length).toBe(1);
    expect(host.textContent).toContain('SMP-2026-00001');
    expect(host.querySelector('table.samples tbody tr app-badge[tone="info"]')?.textContent).toContain('registration.sampleFlags.outside');   // LIS-CR-008 C-02

    host.querySelector<HTMLButtonElement>('table.samples button')!.click();
    await settle();
    host.querySelector<HTMLButtonElement>('footer button')!.click();
    await settle();
    expect(printer.printed).toEqual(['labels:v-1:tube-1', 'labels:v-1:*']);
  });
});
