import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { RESULTS_API, type ResultRowView } from '../../../core/api/results.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ResultsCaseFacade } from '../state/results-case.facade';
import { ResultsPrinter } from '../state/results-printer';
import { caseView, page, reportDocument, reportUnit, resultRow, settle, visitItem } from '../testing/results.fixtures';
import { ResultsByAccessionPage } from './results-by-accession.page';

class PrinterStub {
  readonly printed: string[] = [];

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
    search.flush(page([visitItem()]));
    await render();

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
});
