import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { PaymentReceiptDocument } from '../../../core/api/billing.contracts';
import { PrintService } from '../../../shared/print/print.service';
import { fileNameOf } from '../data/billing.api';
import { I18nService } from '../../../core/i18n/i18n.service';
import { formatCount, formatDateTime, formatMoney, formatPrintDateTime, formatPrintMoney, hasTwoDecimals, localInputToIso, roundMoney, toLocalInputValue } from './billing-format';
import { dayEndIso, dayStartIso, paymentTone } from './billing-view';
import { ReceiptDocumentComponent } from './documents/receipt-document.component';

const receipt: PaymentReceiptDocument = {
  letterhead: { labNameEn: 'Sky Lab', labNameAr: 'مختبر سكاي', branchNameEn: 'Main branch', branchNameAr: 'الفرع الرئيسي' },
  paymentId: 'p-1',
  receiptNumber: 'RCT-2026-00001',
  status: 'Posted',
  paidAtUtc: '2026-10-09T10:00:00Z',
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  patientNumber: 'PAT-2026-00001',
  patientName: 'Mona Adel',
  amount: 1234.5,
  method: 'Card',
  reference: 'SLIP-77',
  treasuryCode: 'MAIN',
  treasuryNameEn: 'Main drawer',
  treasuryNameAr: 'الخزينة الرئيسية',
  cashierName: 'Cashier One',
  patientShareTotal: 1500,
  paid: 1234.5,
  refunded: 0,
  remaining: 265.5,
  voidReason: null,
  voidedAtUtc: null,
  currency: 'EGP',
  printedBy: 'Cashier One',
  printedAtUtc: '2026-10-09T10:01:00Z',
};

describe('Billing UI helpers and documents', () => {
  it('formats money with two decimals: Arabic-Indic digits on an Arabic screen, Latin on paper (validation v0.5.0 F-09)', () => {
    expect(formatMoney(1234.5, 'en')).toBe('1,234.50');
    expect(formatMoney(230, 'ar')).toBe('٢٣٠٫٠٠');
    expect(formatPrintMoney(1234.5, 'ar')).toMatch(/^1.234.50$/);
    expect(formatPrintMoney(1234.5, 'en')).toBe('1,234.50');
    expect(formatDateTime('2026-10-12T12:58:00Z', 'ar')).toMatch(/[٠-٩]/);
    expect(formatDateTime('2026-10-12T12:58:00Z', 'ar')).not.toMatch(/[0-9]/);
    expect(formatPrintDateTime('2026-10-12T12:58:00Z', 'ar')).not.toMatch(/[٠-٩]/);
    expect(formatCount(12, 'ar')).toBe('١٢');
    expect(formatCount(12, 'en')).toBe('12');
    expect(formatMoney(null, 'en')).toBe('—');
    expect(roundMoney(10.005 + 0.001)).toBe(10.01);
    expect(hasTwoDecimals(10.25)).toBe(true);
    expect(hasTwoDecimals(10.255)).toBe(false);
  });

  it('turns local inputs and day filters into ISO instants (an inclusive "to" day ends at the next midnight)', () => {
    const local = toLocalInputValue(new Date(2026, 9, 9, 7, 5));
    expect(local).toBe('2026-10-09T07:05');
    expect(localInputToIso(local)).toBe(new Date(2026, 9, 9, 7, 5).toISOString());
    expect(localInputToIso('')).toBeNull();
    expect(dayStartIso('2026-10-09')).toBe(new Date(2026, 9, 9).toISOString());
    expect(dayEndIso('2026-10-09')).toBe(new Date(2026, 9, 10).toISOString());
    expect(dayStartIso('')).toBeNull();
    expect(paymentTone('Voided')).toBe('neutral');
  });

  it('reads the file name of an export from Content-Disposition', () => {
    expect(fileNameOf("attachment; filename=payments-20261009-1400.csv; filename*=UTF-8''payments-20261009-1400.csv")).toBe('payments-20261009-1400.csv');
    expect(fileNameOf('attachment; filename="refunds.csv"')).toBe('refunds.csv');
    expect(fileNameOf(null)).toBeNull();
  });

  it('renders the receipt with its letterhead, amount, reference and a VOID banner once voided', () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const print = TestBed.inject(PrintService);

    const posted = print.render({ type: ReceiptDocumentComponent, inputs: { doc: receipt } });
    expect(posted).toContain('Sky Lab');
    expect(posted).toContain('مختبر سكاي');
    expect(posted).toContain('RCT-2026-00001');
    expect(posted).toContain('1,234.50');
    expect(posted).toContain('SLIP-77');
    expect(posted).not.toContain('void-banner');

    // F-09: the receipt printed from an Arabic screen keeps Latin digits
    TestBed.inject(I18nService).setLanguage('ar');
    const arabic = print.render({ type: ReceiptDocumentComponent, inputs: { doc: receipt } });
    TestBed.inject(I18nService).setLanguage('en');
    expect(arabic).toContain('1,234.50');
    expect(arabic).not.toMatch(/[٠-٩]/);

    const voided = print.render({ type: ReceiptDocumentComponent, inputs: { doc: { ...receipt, status: 'Voided', voidReason: 'wrong visit' } } });
    expect(voided).toContain('void-banner');
    expect(voided).toContain('wrong visit');
  });
});
