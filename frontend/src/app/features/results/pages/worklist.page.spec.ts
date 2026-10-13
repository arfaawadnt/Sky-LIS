import resultsAr from '../../../../../public/i18n/results.ar.json';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { RESULTS_API } from '../../../core/api/results.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ResultsPrinter } from '../state/results-printer';
import { WorklistFacade } from '../state/worklist.facade';
import { settle, worklistItem, worklistPage } from '../testing/results.fixtures';
import { WorklistPage } from './worklist.page';

class PrinterStub {
  readonly printed: string[] = [];

  worklist(doc: { rows: readonly unknown[] }): Promise<void> {
    this.printed.push(`worklist:${doc.rows.length}`);
    return Promise.resolve();
  }
}

describe('WorklistPage (CP-18)', () => {
  let fixture: ComponentFixture<WorklistPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;
  let printer: PrinterStub;
  let granted: Set<string>;

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  async function open(permissions: readonly string[]): Promise<void> {
    granted = new Set(permissions);
    printer = new PrinterStub();
    await TestBed.configureTestingModule({
      imports: [WorklistPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ResultsPrinter, useValue: printer },
        WorklistFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WorklistPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    backend
      .expectOne((request) => request.url === RESULTS_API.worklist)
      .flush(worklistPage([worklistItem(), worklistItem({ resultId: 'res-2', accession: 'ACC-2026-00002', visitId: 'visit-2', tubeId: 'tube-2', delayState: 'Late', delaySeconds: 7200, consumedPercent: 130, status: 'Complete', patientName: 'VIP patient', patientNameAr: null, patientMasked: true })]));
    backend.expectOne((request) => request.url === RESULTS_API.clinicalGroups).flush([{ id: 'grp-1', code: 'CHEM', nameEn: 'Chemistry', nameAr: 'الكيمياء' }]);
    backend.expectOne((request) => request.url === RESULTS_API.tests).flush([]);
    backend.expectOne((request) => request.url === RESULTS_API.payers).flush([]);
    backend.expectOne((request) => request.url === RESULTS_API.analysers).flush([]);
    await render();
  }

  function buttons(text: string): HTMLButtonElement[] {
    return [...host.querySelectorAll<HTMLButtonElement>('button')].filter((button) => button.textContent?.trim() === text);
  }

  it('lists the tests with the delay colour, the counters and a link to the visit results', async () => {
    await open(['results.view']);

    const rows = host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]');
    expect(rows.length).toBe(2);
    expect(rows[1]?.classList.contains('delay-late')).toBe(true);
    expect(rows[0]?.classList.contains('delay-late')).toBe(false);
    // A-03 on CP-18: a VIP row masked by the server reads the dictionary's "VIP patient"
    expect(rows[1]?.textContent).toContain('results.vipMasked');
    expect(rows[0]?.textContent).not.toContain('results.vipMasked');
    expect(host.querySelectorAll('app-stat-card').length).toBe(4);
    expect(host.textContent).toContain('results.worklist.delayed');
    const link = rows[0]?.querySelector<HTMLAnchorElement>('a.link');
    expect(link?.getAttribute('href')).toBe('/results/by-accession?accession=ACC-2026-00001');
    expect(buttons('results.worklist.export').length).toBe(0);
    expect(buttons('results.worklist.print').length).toBe(1);
    backend.verify();
  });

  it('narrows the list with the delayed box and the group filter, and prints the bench list', async () => {
    await open(['results.view', 'results.export']);
    expect(buttons('results.worklist.export').length).toBe(1);

    const delayed = [...host.querySelectorAll<HTMLInputElement>('.boxes input[type="checkbox"]')].find((box) => box.parentElement?.textContent?.includes('results.worklist.delayedOnly'));
    expect(delayed).toBeDefined();
    delayed!.click();
    await render();
    const filtered = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect(filtered.request.params.get('delayedOnly')).toBe('true');
    filtered.flush(worklistPage([]));
    await render();
    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(0);

    const group = host.querySelectorAll<HTMLSelectElement>('.filter-grid select')[0];
    expect(group).toBeDefined();
    group!.value = 'grp-1';
    group!.dispatchEvent(new Event('change'));
    await render();
    const byGroup = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect(byGroup.request.params.get('clinicalGroupId')).toBe('grp-1');
    byGroup.flush(worklistPage([]));
    backend.expectOne((request) => request.url === RESULTS_API.tests).flush([]);
    await render();

    buttons('results.worklist.print')[0]!.click();
    await settle();
    const doc = backend.expectOne((request) => request.url === RESULTS_API.worklistDocument);
    expect(doc.request.params.get('delayedOnly')).toBe('true');
    doc.flush({ letterhead: { labNameEn: 'Lab', labNameAr: 'مختبر', branchNameEn: null, branchNameAr: null }, generatedAtUtc: '2026-10-10T10:00:00Z', generatedBy: 'me', fromUtc: null, toUtc: null, groupNameEn: null, groupNameAr: null, rows: [worklistItem()], truncated: false });
    await render();
    expect(printer.printed).toEqual(['worklist:1']);
    backend.verify();
  });

  it('F-09 / F-15: on an Arabic page the counters, ages, shares and thresholds read Arabic-Indic digits; the counter is the late count of the server', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(String(input) === '/i18n/results.ar.json' ? resultsAr : {}), { status: 200 }));
    granted = new Set(['results.view']);
    await TestBed.configureTestingModule({
      imports: [WorklistPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ResultsPrinter, useValue: new PrinterStub() },
        WorklistFacade,
      ],
    }).compileComponents();
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('ar');
    await i18n.load('results');
    fixture = TestBed.createComponent(WorklistPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    // the thresholds are 1 % / 2 %: two rows past 2 % are Late, and the server counts them as Delayed (F-15)
    const late = { delayState: 'Late' as const, consumedPercent: 2 };
    backend
      .expectOne((request) => request.url === RESULTS_API.worklist)
      .flush(worklistPage([worklistItem(late), worklistItem({ ...late, resultId: 'res-2', consumedPercent: 40 })], { warnPercent: 1, latePercent: 2 }));
    backend.expectOne((request) => request.url === RESULTS_API.clinicalGroups).flush([]);
    backend.expectOne((request) => request.url === RESULTS_API.tests).flush([]);
    backend.expectOne((request) => request.url === RESULTS_API.payers).flush([]);
    backend.expectOne((request) => request.url === RESULTS_API.analysers).flush([]);
    await render();

    const cards = [...host.querySelectorAll('app-stat-card')].map((card) => card.textContent ?? '');
    expect(cards[3]).toContain('٢');
    const rows = host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]');
    expect([...rows].every((row) => row.classList.contains('delay-late'))).toBe(true);
    expect(rows[1]?.textContent).toContain('٤٠٪');
    expect(rows[0]?.textContent).toContain('٣٦');
    const text = host.textContent ?? '';
    expect(text).toContain('≥ ٢٪ (متأخر)');
    expect(text).not.toMatch(/\d+%/);
    i18n.setLanguage('en');
  });
});
