import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { VisitLineItem } from '../../../core/api/registration.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { visitDetail } from '../testing/registration.fixtures';
import { AccessionLinesComponent } from './accession-lines.component';

function line(overrides: Partial<VisitLineItem>): VisitLineItem {
  const base = visitDetail().lines[0];
  if (base === undefined) {
    throw new Error('fixture without a line');
  }

  return { ...base, ...overrides };
}

describe('AccessionLinesComponent (C9 lines, LIS-CR-015 formula operands)', () => {
  it('lists each formula operand under its formula line, tagged, priced 0 and without a Cancel button', async () => {
    await TestBed.configureTestingModule({
      imports: [AccessionLinesComponent],
      providers: [provideZonelessChangeDetection(), { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> }],
    }).compileComponents();
    const fixture = TestBed.createComponent(AccessionLinesComponent);
    fixture.componentRef.setInput('lines', [
      line({ id: 'l-gfr', serviceId: 's-gfr', code: 'GFR', nameEn: 'eGFR' }),
      line({ id: 'l-cbc', serviceId: 's-cbc', code: 'CBC' }),
      line({ id: 'l-crea', serviceId: 's-crea', code: 'CREA', nameEn: 'Creatinine', kind: 'FormulaOperand', parentLineId: 'l-gfr', listPrice: 0, patientShare: 0, origin: 'Formula', visibleOnReceipt: false }),
    ]);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    const rows = [...host.querySelectorAll<HTMLTableRowElement>('tbody tr')];
    expect(rows.map((row) => row.getAttribute('data-line'))).toEqual(['l-gfr', 'l-crea', 'l-cbc']);
    expect(rows[1]?.classList.contains('member')).toBe(true);
    expect(rows[1]?.querySelector('[data-formula]')?.textContent).toContain('registration.forFormula');
    expect(rows[1]?.querySelector('[data-formula]')?.textContent).toContain('registration.offReceipt');
    expect(rows[1]?.querySelector('button')).toBeNull();
    expect(rows[0]?.querySelector('button')).not.toBeNull();
  });
});
