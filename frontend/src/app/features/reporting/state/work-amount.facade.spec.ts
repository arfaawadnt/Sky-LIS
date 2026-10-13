import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { REPORTING_API } from '../../../core/api/reporting.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { myPrivileges, workAmountReport } from '../testing/reporting.fixtures';
import { WorkAmountFacade, isoDay, shiftDay } from './work-amount.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('WorkAmountFacade (01 §6.3)', () => {
  let facade: WorkAmountFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-main') } satisfies Pick<AuthStore, 'currentBranch'> },
        WorkAmountFacade,
      ],
    });
    facade = TestBed.inject(WorkAmountFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  /** Runs the resource effects (which issue the HTTP calls) and lets the promises settle. */
  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('opens on today with the Day grouping and the session branch, then re-reads for a new query', async () => {
    await runEffects();
    const today = isoDay(new Date());
    const first = backend.expectOne((request) => request.url === REPORTING_API.workAmount);
    expect(first.request.params.get('from')).toBe(today);
    expect(first.request.params.get('to')).toBe(today);
    expect(first.request.params.get('groupBy')).toBe('Day');
    expect(first.request.params.has('branchId')).toBe(false);
    first.flush(workAmountReport({ groupBy: 'Day', fromDay: today, toDay: today }));
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b-main', branches: [{ id: 'b-main', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    backend.expectOne(ORGANIZATION_API.myPrivileges).flush(myPrivileges(null));
    await runEffects();
    expect(facade.current()?.totals.net).toBe(640);
    expect(facade.selectableBranches().length).toBe(1);
    expect(facade.earliestDay()).toBeNull();

    facade.run({ from: '2026-10-01', to: '2026-10-13', groupBy: 'Payer', branchId: 'b-east' });
    await runEffects();
    const second = backend.expectOne((request) => request.url === REPORTING_API.workAmount);
    expect(second.request.params.get('from')).toBe('2026-10-01');
    expect(second.request.params.get('to')).toBe('2026-10-13');
    expect(second.request.params.get('groupBy')).toBe('Payer');
    expect(second.request.params.get('branchId')).toBe('b-east');
    second.flush(workAmountReport());
    await runEffects();
    expect(facade.current()?.rows.length).toBe(2);
    backend.verify();
  });

  it('derives the earliest allowed day from the report days limit of the privileges (CP-37)', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === REPORTING_API.workAmount).flush(workAmountReport());
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: null, branches: [] });
    backend.expectOne(ORGANIZATION_API.myPrivileges).flush(myPrivileges(7));
    await runEffects();

    expect(facade.reportDaysLimit()).toBe(7);
    expect(facade.earliestDay()).toBe(shiftDay(isoDay(new Date()), -7));
    backend.verify();
  });

  it('shifts ISO days across month ends', () => {
    expect(shiftDay('2026-03-01', -1)).toBe('2026-02-28');
    expect(shiftDay('2026-12-31', 1)).toBe('2027-01-01');
    expect(shiftDay('2026-10-13', 0)).toBe('2026-10-13');
  });
});
