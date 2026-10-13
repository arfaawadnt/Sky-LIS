import reportingEn from '../../../../../public/i18n/reporting.en.json';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { REPORTING_API } from '../../../core/api/reporting.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { type PrintRequest, PrintService } from '../../../shared/print/print.service';
import { WorkAmountFacade } from '../state/work-amount.facade';
import { myPrivileges, workAmountReport } from '../testing/reporting.fixtures';
import { WorkAmountPage } from './work-amount.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

interface PageInternals {
  readonly form: FormGroup;
  run(): void;
}

describe('WorkAmountPage (change request 2026-10-13 item 3)', () => {
  let fixture: ComponentFixture<WorkAmountPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;
  let granted: Set<string>;
  const printed: PrintRequest[] = [];

  afterEach(() => vi.restoreAllMocks());   // the dictionary fetch of one test must not reach the next

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  async function open(permissions: readonly string[], branches: number, reportDaysLimit: number | null = null): Promise<void> {
    granted = new Set(permissions);
    printed.length = 0;
    await TestBed.configureTestingModule({
      imports: [WorkAmountPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: PrintService, useValue: { print: (request: PrintRequest) => Promise.resolve(void printed.push(request)) } satisfies Pick<PrintService, 'print'> },
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-main') } satisfies Pick<AuthStore, 'currentBranch'> },
        WorkAmountFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WorkAmountPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    await render();
    backend.expectOne((call) => call.url === REPORTING_API.workAmount).flush(workAmountReport());
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({
      currentBranchId: 'b-main',
      branches: Array.from({ length: branches }, (_, index) => ({ id: `b-${index}`, code: `B${index}`, nameEn: `Branch ${index}`, nameAr: `فرع ${index}` })),
    });
    backend.expectOne(ORGANIZATION_API.myPrivileges).flush(myPrivileges(reportDaysLimit, permissions));
    await render();
  }

  function cells(row: Element): string[] {
    return [...row.querySelectorAll('th, td')].map((cell) => cell.textContent?.trim() ?? '');
  }

  it('shows the rows of the report and a totals row, keeps the period the server applied, and hides the branch picker for one branch', async () => {
    await open(['reporting.view'], 1);

    const rows = [...host.querySelectorAll('table.amounts tbody tr')];
    expect(rows.length).toBe(2);
    expect(cells(rows[0]!)).toEqual(['Cash patients', '1', '1', '200.00', '10.00', '190.00', '0.00', '190.00']);
    expect(cells(rows[1]!)).toEqual(['Insurer One', '2', '3', '450.00', '0.00', '30.00', '420.00', '450.00']);
    const totals = host.querySelector('table.amounts tfoot tr.totals')!;
    expect(cells(totals)).toEqual(['reporting.workAmount.total', '3', '4', '650.00', '10.00', '220.00', '420.00', '640.00']);

    const page = fixture.componentInstance as unknown as PageInternals;
    expect(page.form.getRawValue()).toMatchObject({ from: '2026-10-13', to: '2026-10-13' });
    expect(host.querySelector('select[formcontrolname="branchId"]')).toBeNull();
    // export needs reporting.export (affordance only; the server decides)
    expect([...host.querySelectorAll('button')].some((button) => button.textContent?.includes('reporting.workAmount.export'))).toBe(false);
  });

  it('runs a new query from the form, offers the branch picker for several branches and prints the report in landscape', async () => {
    await open(['reporting.view', 'reporting.export'], 2);
    expect(host.querySelector('select[formcontrolname="branchId"]')).not.toBeNull();
    expect([...host.querySelectorAll('button')].some((button) => button.textContent?.includes('reporting.workAmount.export'))).toBe(true);

    const page = fixture.componentInstance as unknown as PageInternals;
    page.form.patchValue({ from: '2026-10-01', to: '', groupBy: 'Service', branchId: 'b-1' });
    page.run();
    await render();
    const request = backend.expectOne((call) => call.url === REPORTING_API.workAmount);
    expect(request.request.params.get('from')).toBe('2026-10-01');
    expect(request.request.params.get('to')).toBe('2026-10-01');   // an empty end names a one-day period
    expect(request.request.params.get('groupBy')).toBe('Service');
    expect(request.request.params.get('branchId')).toBe('b-1');
    request.flush(workAmountReport({ groupBy: 'Service', fromDay: '2026-10-01', toDay: '2026-10-01', rows: [] }));
    await render();
    expect(host.querySelector('table.amounts')).toBeNull();
    expect(host.querySelector('app-empty-state')).not.toBeNull();

    [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('reporting.workAmount.print'))!.click();
    await settle();
    expect(printed.length).toBe(1);
    expect(printed[0]).toMatchObject({ size: 'A4', landscape: true });
    backend.verify();
  });

  it('hints the member\'s earliest allowed day and bounds the date inputs with it (CP-37 report days limit)', async () => {
    await open(['reporting.view'], 1, 0);

    expect(host.querySelector('app-form-field')?.textContent).toContain('reporting.workAmount.todayOnly');
    const from = host.querySelector<HTMLInputElement>('input[formcontrolname="from"]')!;
    expect(from.getAttribute('min')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('shows the server\'s refusal of a period beyond the limit as the module\'s message (REP-001)', async () => {
    const dictionaries: Record<string, unknown> = { '/i18n/reporting.en.json': reportingEn };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(dictionaries[String(input)] ?? {}), { status: 200 }));
    await open(['reporting.view'], 1, 0);
    await TestBed.inject(I18nService).load('reporting');
    await render();

    const page = fixture.componentInstance as unknown as PageInternals;
    page.form.patchValue({ from: '2020-01-01', to: '2020-01-02' });
    page.run();
    await render();
    backend.expectOne((call) => call.url === REPORTING_API.workAmount).flush(
      { type: 'about:blank', title: 'Refused', status: 403, code: 'REP-001', traceId: null, correlationId: null },
      { status: 403, statusText: 'Forbidden' },
    );
    await render();

    expect(host.querySelector('.problem')?.textContent).toContain('Your role may not report on a period that far back.');
    expect(host.querySelector('table.amounts')).toBeNull();
  });
});
