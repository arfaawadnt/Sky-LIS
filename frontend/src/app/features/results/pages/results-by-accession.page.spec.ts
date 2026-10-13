import resultsAr from '../../../../../public/i18n/results.ar.json';
import resultsEn from '../../../../../public/i18n/results.en.json';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { RESULTS_API, type ResultRowView } from '../../../core/api/results.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { type DocumentTexts, documentTexts } from '../../../shared/print/document-texts';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ResultsCaseFacade } from '../state/results-case.facade';
import { ResultsPrinter } from '../state/results-printer';
import { caseView, page, reportDocument, reportUnit, resultRow, settle, visitItem } from '../testing/results.fixtures';
import { ResultsByAccessionPage } from './results-by-accession.page';

class PrinterStub {
  readonly printed: string[] = [];

  reportTexts(doc: { language: string }): Promise<DocumentTexts> {
    return Promise.resolve(documentTexts(doc.language === 'ar' ? 'ar' : 'en', {}));
  }

  report(doc: { reportUnitKey: string }): Promise<void> {
    this.printed.push(`report:${doc.reportUnitKey}`);
    return Promise.resolve();
  }
}

describe('ResultsByAccessionPage (CP-19)', () => {
  let fixture: ComponentFixture<ResultsByAccessionPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;
  let printer: PrinterStub;
  let granted: Set<string>;
  let confirmAnswer: boolean;
  let reasonAnswer: string | null;

  afterEach(() => vi.restoreAllMocks());   // the dictionary fetch of one test must not reach the next

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  async function open(permissions: readonly string[], rows: readonly ResultRowView[], accession = 'acc-2026-00001'): Promise<void> {
    granted = new Set(permissions);
    printer = new PrinterStub();
    confirmAnswer = true;
    reasonAnswer = 'wrong sample';
    await TestBed.configureTestingModule({
      imports: [ResultsByAccessionPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(confirmAnswer), promptReason: () => Promise.resolve(reasonAnswer) } satisfies Pick<ConfirmService, 'confirm' | 'promptReason'> },
        { provide: ResultsPrinter, useValue: printer },
        ResultsCaseFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ResultsByAccessionPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('accession', accession);
    await render();
    const request = backend.expectOne((call) => call.url === RESULTS_API.results);
    expect(request.request.params.get('accession')).toBe('ACC-2026-00001');
    request.flush(caseView([reportUnit(rows)]));
    await render();
  }

  function buttons(text: string): HTMLButtonElement[] {
    return [...host.querySelectorAll<HTMLButtonElement>('button')].filter((button) => button.textContent?.trim().startsWith(text));
  }

  function flushReload(rows: readonly ResultRowView[]): void {
    backend.expectOne((call) => call.url === RESULTS_API.results).flush(caseView([reportUnit(rows)]));
  }

  it('offers the report comment and the internal note only with the rights the server requires (UAT A-02)', async () => {
    await open(['results.view'], [resultRow()]);
    // a view-only user (reception): neither dialog, whatever the unit's rights say
    expect(host.querySelector('app-result-grid [aria-label="results.grid.comment"]')).toBeNull();
    expect(host.querySelector('app-result-grid [aria-label="results.grid.note"]')).toBeNull();
    expect(host.querySelector('app-result-grid [aria-label="results.grid.history"]')).not.toBeNull();
  });

  it('offers the report comment and the internal note to a user who may enter results of the unit', async () => {
    await open(['results.view', 'results.create'], [resultRow()]);
    expect(host.querySelector('app-result-grid [aria-label="results.grid.comment"]')).not.toBeNull();
    expect(host.querySelector('app-result-grid [aria-label="results.grid.note"]')).not.toBeNull();
  });

  it('opens the case of the query parameter, enters a value and saves it with Ctrl+S', async () => {
    await open(['results.view', 'results.create'], [resultRow()]);

    expect(host.textContent).toContain('Mona Adel');
    expect(host.textContent).toContain('ACC-2026-00001');
    expect(host.querySelector('.visits .visit-list')).toBeNull();
    // validation 2026-10-11: a visit opened from a link no longer sits under "Matching visits (0)"
    expect(host.querySelector('.visits')?.textContent).toContain('results.case.matchingTitle');
    expect(buttons('results.case.review').length).toBe(0);
    expect(buttons('results.case.verify').length).toBe(0);
    const input = host.querySelector<HTMLInputElement>('app-result-grid .value-input');
    expect(input).not.toBeNull();
    expect(input!.disabled).toBe(false);

    input!.value = '95.2';
    input!.dispatchEvent(new Event('input'));
    await render();
    expect(host.querySelector('app-result-grid tr.dirty')).not.toBeNull();

    input!.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
    await settle();
    const put = backend.expectOne(RESULTS_API.values);
    expect(put.request.body).toEqual({ values: [{ resultId: 'res-1', numericValue: 95.2, textValue: null, paragraphs: null, culture: null }] });
    put.flush({ changed: ['res-1'], refused: [] });
    await render();
    flushReload([resultRow({ status: 'Complete', numericValue: 95.2, textValue: '95.2', flag: 'Normal' })]);
    await render();
    expect(host.querySelector('app-result-grid tr.dirty')).toBeNull();
    backend.verify();
  });

  it('shows the refusal code on the row when the server refuses a value (BR-03)', async () => {
    await open(['results.view', 'results.create'], [resultRow({ sampleReceived: true })]);

    const input = host.querySelector<HTMLInputElement>('app-result-grid .value-input')!;
    input.value = '95';
    input.dispatchEvent(new Event('input'));
    await render();
    buttons('results.case.save')[0]!.click();
    await settle();
    backend.expectOne(RESULTS_API.values).flush({ changed: [], refused: [{ resultId: 'res-1', code: 'RES-010', message: 'not received' }] });
    await render();
    flushReload([resultRow()]);
    await render();

    expect(host.querySelector('app-result-grid .refusal')?.textContent).toContain('RES-010');
    expect(host.querySelector('app-result-grid tr.dirty')).not.toBeNull();
    backend.verify();
  });

  it('verifies the unit after acknowledging its delta flags, undoes with a reason and prints the report', async () => {
    const complete = resultRow({ status: 'Complete', numericValue: 140, textValue: '140.0', delta: { flag: true, percent: 40, previousValue: 100, previousResultId: 'old', acknowledged: false, acknowledgedByName: null, acknowledgedAtUtc: null, comment: null } });
    await open(['results.view', 'results.approve', 'results.cancel', 'results.export'], [complete]);
    expect(host.querySelector<HTMLInputElement>('app-result-grid .value-input')?.disabled).toBe(true);

    buttons('results.case.verify')[0]!.click();
    await settle();
    backend.expectOne(RESULTS_API.acknowledgeDelta('res-1')).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    const verify = backend.expectOne(RESULTS_API.verifyMany);
    expect(verify.request.body).toEqual({ resultIds: ['res-1'] });
    verify.flush({ changed: ['res-1'], refused: [] });
    await render();
    const verified = resultRow({ status: 'Verified', numericValue: 140, textValue: '140.0', verifiedByName: 'Dr. Hassan', delta: { ...complete.delta!, acknowledged: true } });
    flushReload([verified]);
    await render();
    expect(host.querySelector('app-result-grid .badge[data-status="Verified"]')).not.toBeNull();

    buttons('results.case.undo')[0]!.click();
    await settle();
    const undo = backend.expectOne(RESULTS_API.undo('res-1'));
    expect(undo.request.body).toEqual({ reason: 'wrong sample' });
    undo.flush(null, { status: 204, statusText: 'No Content' });
    await render();
    flushReload([verified]);
    await render();

    buttons('results.case.print')[0]!.click();
    await settle();
    backend.expectOne(RESULTS_API.print('unit-1')).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    const official = backend.expectOne((call) => call.url === RESULTS_API.report('unit-1'));
    expect(official.request.params.get('preview')).toBe('false');
    official.flush(reportDocument());
    await render();
    flushReload([{ ...verified, status: 'Printed', printCount: 1 }]);
    await render();
    expect(printer.printed).toEqual(['report:unit-1']);
    backend.verify();
  });

  it('a Verify refused for a delta found on the server re-reads the case and opens the delta dialog (validation v0.5.0 A-02)', async () => {
    // reviewed before the older result was verified: the page shows no Δ until the server re-checks at verification
    const reviewed = resultRow({ status: 'Reviewed', numericValue: 2.5, textValue: '2.50' });
    await open(['results.view', 'results.approve'], [reviewed]);

    buttons('results.case.verify')[0]!.click();
    await settle();
    const first = backend.expectOne(RESULTS_API.verifyMany);
    expect(first.request.body).toEqual({ resultIds: ['res-1'] });
    first.flush({ changed: [], refused: [{ resultId: 'res-1', code: 'RES-015', message: 'Acknowledge the delta check before verifying (BR-11).' }] });
    await render();
    const flagged = resultRow({ status: 'Reviewed', numericValue: 2.5, textValue: '2.50', delta: { flag: true, percent: 150, previousValue: 1, previousResultId: 'old', acknowledged: false, acknowledgedByName: null, acknowledgedAtUtc: null, comment: null } });
    flushReload([flagged]);
    await render();
    await settle();

    // the dialog was confirmed: the refreshed flag is acknowledged, then the result verifies — one click, no dead end
    backend.expectOne(RESULTS_API.acknowledgeDelta('res-1')).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    const second = backend.expectOne(RESULTS_API.verifyMany);
    expect(second.request.body).toEqual({ resultIds: ['res-1'] });
    second.flush({ changed: ['res-1'], refused: [] });
    await render();
    flushReload([{ ...flagged, status: 'Verified', delta: { ...flagged.delta!, acknowledged: true } }]);
    await render();
    expect(host.querySelector('app-result-grid .badge[data-status="Verified"]')).not.toBeNull();
    backend.verify();
  });

  it('A-02: cancelling the reopened delta dialog leaves the result Reviewed and acknowledges nothing', async () => {
    await open(['results.view', 'results.approve'], [resultRow({ status: 'Reviewed', numericValue: 2.5, textValue: '2.50' })]);
    confirmAnswer = false;

    buttons('results.case.verify')[0]!.click();
    await settle();
    backend.expectOne(RESULTS_API.verifyMany).flush({ changed: [], refused: [{ resultId: 'res-1', code: 'RES-015', message: 'delta' }] });
    await render();
    flushReload([resultRow({ status: 'Reviewed', numericValue: 2.5, textValue: '2.50', delta: { flag: true, percent: 150, previousValue: 1, previousResultId: 'old', acknowledged: false, acknowledgedByName: null, acknowledgedAtUtc: null, comment: null } })]);
    await render();
    await settle();

    backend.expectNone(RESULTS_API.acknowledgeDelta('res-1'));
    backend.expectNone(RESULTS_API.verifyMany);
    backend.verify();
  });

  it('previews the report in a dialog without marking it printed', async () => {
    await open(['results.view'], [resultRow({ status: 'Verified', numericValue: 95, textValue: '95.0' })]);
    expect(buttons('results.case.print').length).toBe(0);

    buttons('results.case.preview')[0]!.click();
    await settle();
    const preview = backend.expectOne((call) => call.url === RESULTS_API.report('unit-1'));
    expect(preview.request.params.get('preview')).toBe('true');
    preview.flush(reportDocument({ isPreview: true }));
    await render();

    const dialog = host.querySelector('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog?.querySelector('app-results-report-document')).not.toBeNull();
    expect(dialog?.textContent).toContain('results.report.preview');
    expect(dialog?.textContent).toContain('Dr. Hassan');
    backend.expectNone((call) => call.url === RESULTS_API.print('unit-1'));
    backend.verify();
  });

  it('runs the query panel and opens a matching visit from the list', async () => {
    granted = new Set(['results.view']);
    printer = new PrinterStub();
    await TestBed.configureTestingModule({
      imports: [ResultsByAccessionPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ResultsPrinter, useValue: printer },
        ResultsCaseFacade,
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ResultsByAccessionPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    expect(host.textContent).toContain('results.case.start');

    const name = host.querySelector<HTMLInputElement>('input[formcontrolname="patientName"]')!;
    name.value = 'mo%';
    name.dispatchEvent(new Event('input'));
    host.querySelector<HTMLFormElement>('form.query-form')!.dispatchEvent(new Event('submit'));
    await render();
    const search = backend.expectOne((call) => call.url === RESULTS_API.visits);
    expect(search.request.params.get('patientName')).toBe('mo%');
    expect(search.request.params.get('branchOnly')).toBe('true');
    search.flush(page([visitItem(), visitItem({ visitId: 'visit-vip', accession: 'ACC-2026-00027', patientNumber: 'PAT-2026-00010', patientName: 'VIP patient', patientNameAr: null, patientMasked: true })]));
    await render();
    // A-03 on CP-19: a VIP row masked by the server reads the dictionary's "VIP patient"; the other keeps its name
    const listed = [...host.querySelectorAll('.visit-list button.visit')].map((button) => button.textContent ?? '');
    expect(listed[1]).toContain('results.vipMasked');
    expect(listed[1]).toContain('PAT-2026-00010');
    expect(listed[0]).not.toContain('results.vipMasked');
    expect(host.querySelector('.visits')?.textContent).not.toContain('results.case.matchingTitle');
    expect(host.querySelector('.visits')?.textContent).toContain('results.case.matching');

    const item = host.querySelector<HTMLButtonElement>('.visit-list button.visit');
    expect(item?.textContent).toContain('ACC-2026-00001');
    item!.click();
    await render();
    const byAccession = backend.expectOne((call) => call.url === RESULTS_API.results);
    expect(byAccession.request.params.get('accession')).toBe('ACC-2026-00001');
    byAccession.flush(caseView());
    await render();
    expect(host.textContent).toContain('Mona Adel');
    backend.verify();
  });

  it('reads one report unit and one result in the singular in both languages (round 2 R2-07)', async () => {
    const dictionaries: Record<string, unknown> = { '/i18n/results.en.json': resultsEn, '/i18n/results.ar.json': resultsAr };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(dictionaries[String(input)] ?? {}), { status: 200 }));
    granted = new Set(['results.view']);
    await TestBed.configureTestingModule({
      imports: [ResultsByAccessionPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ResultsPrinter, useValue: new PrinterStub() },
        ResultsCaseFacade,
      ],
    }).compileComponents();
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    fixture = TestBed.createComponent(ResultsByAccessionPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    await i18n.load('results');
    const name = host.querySelector<HTMLInputElement>('input[formcontrolname="patientName"]')!;
    name.value = 'mo%';
    name.dispatchEvent(new Event('input'));
    host.querySelector<HTMLFormElement>('form.query-form')!.dispatchEvent(new Event('submit'));
    await render();
    backend.expectOne((call) => call.url === RESULTS_API.visits).flush(page([visitItem({ units: 1, results: 1 }), visitItem({ visitId: 'visit-2', accession: 'ACC-2026-00002', units: 2, results: 3 })]));
    await render();

    const lines = (): string[] => [...host.querySelectorAll('.visit-list .line.small.muted')].map((line) => line.textContent?.replace(/\s+/g, ' ').trim() ?? '');
    expect(lines()[0]).toContain('1 report unit · 1 result');
    expect(lines()[0]).not.toContain('units');
    expect(lines()[0]).not.toContain('results');
    expect(lines()[1]).toContain('2 report units · 3 results');

    i18n.setLanguage('ar');
    await i18n.load('results');
    await render();
    expect(lines()[0]).toContain('وحدة تقرير واحدة · نتيجة واحدة');
    // F-11: the Arabic dual and the 3–10 plural
    expect(lines()[1]).toContain('وحدتا تقرير · ٣ نتائج');
    expect(host.querySelector('app-section-card.visits')?.textContent).toContain('(٢)');   // the heading count (round 3 R3-02)
    i18n.setLanguage('en');
  });

  it('F-06: an opened VIP case reads "VIP patient" without the See-VIP right and Print is disabled with the reason', async () => {
    granted = new Set(['results.view', 'results.export']);
    printer = new PrinterStub();
    await TestBed.configureTestingModule({
      imports: [ResultsByAccessionPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ResultsPrinter, useValue: printer },
        ResultsCaseFacade,
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ResultsByAccessionPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('accession', 'ACC-2026-00018');
    await render();
    const masked = caseView([reportUnit([resultRow({ status: 'Verified', numericValue: 95, textValue: '95.0' })])]);
    backend.expectOne((call) => call.url === RESULTS_API.results).flush({
      ...masked,
      patient: { ...masked.patient, number: 'PAT-2026-00010', fullName: 'VIP patient', nameAr: null, dateOfBirth: null, isVip: true, isMasked: true },
    });
    await render();

    const title = host.querySelector('.case-title')?.textContent ?? '';
    expect(title).toContain('results.vipMasked');
    expect(title).not.toContain('Mona Adel');
    expect(host.querySelector('.case-sub')?.textContent).toContain('PAT-2026-00010');
    const print = buttons('results.case.print')[0]!;
    expect(print.disabled).toBe(true);
    expect(print.getAttribute('title')).toBe('results.case.printVipBlocked');
    expect(host.querySelector('.vip-print')?.textContent).toContain('results.case.printVipBlocked');
    print.click();
    await settle();
    backend.expectNone((call) => call.url === RESULTS_API.print('unit-1'));
    expect(printer.printed).toEqual([]);
  });

  it('F-09: an Arabic page shows the age, the counts and the print count in Arabic-Indic digits', async () => {
    const dictionaries: Record<string, unknown> = { '/i18n/results.en.json': resultsEn, '/i18n/results.ar.json': resultsAr };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(dictionaries[String(input)] ?? {}), { status: 200 }));
    granted = new Set(['results.view']);
    await TestBed.configureTestingModule({
      imports: [ResultsByAccessionPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ResultsPrinter, useValue: new PrinterStub() },
        ResultsCaseFacade,
      ],
    }).compileComponents();
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('ar');
    await i18n.load('results');
    fixture = TestBed.createComponent(ResultsByAccessionPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('accession', 'ACC-2026-00001');
    await render();
    const unit = reportUnit([resultRow({ status: 'Printed', numericValue: 95, textValue: '95.0' })]);
    backend.expectOne((call) => call.url === RESULTS_API.results).flush(caseView([{ ...unit, isPrinted: true, printCount: 2 }], { hiddenUnits: 3 }));
    await render();

    const sub = host.querySelector('.case-sub')?.textContent ?? '';
    expect(sub).toContain('٣٦');
    expect(sub).not.toMatch(/\b36\b/);
    expect(host.querySelector('.unit-title')?.textContent).toContain('×٢');
    expect(host.querySelector('.case-flags')?.textContent).toContain('٣ وحدات تقرير');
    i18n.setLanguage('en');
  });
});
