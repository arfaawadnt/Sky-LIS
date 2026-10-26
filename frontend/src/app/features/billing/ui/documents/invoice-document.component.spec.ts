import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { InvoiceDocument } from '../../../../core/api/billing.contracts';
import { InvoiceDocumentComponent } from './invoice-document.component';

const invoice: InvoiceDocument = {
  letterhead: { labNameEn: 'Sky Lab', labNameAr: 'سكاي لاب', branchNameEn: null, branchNameAr: null },
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  registeredAtUtc: '2026-10-26T08:00:00Z',
  patientNumber: 'PAT-2026-00001',
  patientName: 'Mona Hassan',
  payerNameEn: null,
  payerNameAr: null,
  lines: [
    { lineId: 'l-gfr', serviceId: 's-gfr', serviceCode: 'GFR', serviceNameEn: 'eGFR', serviceNameAr: null, status: 'Ordered', listPrice: 100, discount: 0, patientShare: 100, payerShare: 0, kind: 'Service', formulaServiceCode: null },
    { lineId: 'l-crea', serviceId: 's-crea', serviceCode: 'CREA', serviceNameEn: 'Creatinine', serviceNameAr: null, status: 'Ordered', listPrice: 0, discount: 0, patientShare: 0, payerShare: 0, kind: 'FormulaOperand', formulaServiceCode: 'GFR' },
  ],
  totalListPrice: 100,
  discountTotal: 0,
  patientShareTotal: 100,
  payerShareTotal: 0,
  payments: [],
  refunds: [],
  paid: 0,
  refunded: 0,
  remaining: 100,
  currency: 'EGP',
  printedBy: 'Front Desk',
  printedAtUtc: '2026-10-26T08:05:00Z',
};

describe('InvoiceDocumentComponent (Receipt / invoice, LIS-CR-015 M3c)', () => {
  it('prints a visible formula operand line at 0.00 with the formula it was ordered for', async () => {
    await TestBed.configureTestingModule({ imports: [InvoiceDocumentComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    const fixture = TestBed.createComponent(InvoiceDocumentComponent);
    fixture.componentRef.setInput('doc', invoice);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    const tag = host.querySelector('[data-formula="l-crea"]');
    expect(tag?.textContent).toContain('billing.doc.forFormula');
    const row = tag?.closest('tr');
    expect(row?.textContent).toContain('CREA');
    expect(row?.textContent).toContain('0.00');
    expect(host.querySelector('[data-formula="l-gfr"]')).toBeNull();
  });
});
