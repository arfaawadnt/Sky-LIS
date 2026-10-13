import type { MyPrivileges } from '../../../core/api/organization.contracts';
import type { WorkAmountReport } from '../../../core/api/reporting.contracts';

/** Test data of the Reporting specs (shared by the facade and page specs). */

export function workAmountReport(overrides: Partial<WorkAmountReport> = {}): WorkAmountReport {
  return {
    fromDay: '2026-10-13',
    toDay: '2026-10-13',
    groupBy: 'Payer',
    branchId: 'b-main',
    timeZone: 'Africa/Cairo',
    letterhead: { labNameEn: 'Sky Lab', labNameAr: 'مختبر سكاي', branchNameEn: 'Main', branchNameAr: 'الرئيسي' },
    rows: [
      { key: 'p-cash', labelEn: 'Cash patients', labelAr: 'الدفع النقدي', code: null, visits: 1, services: 1, gross: 200, discount: 10, cashIncome: 190, creditIncome: 0, net: 190 },
      { key: 'p-ins', labelEn: 'Insurer One', labelAr: 'شركة تأمين', code: null, visits: 2, services: 3, gross: 450, discount: 0, cashIncome: 30, creditIncome: 420, net: 450 },
    ],
    totals: { key: 'total', labelEn: 'Total', labelAr: 'الإجمالي', code: null, visits: 3, services: 4, gross: 650, discount: 10, cashIncome: 220, creditIncome: 420, net: 640 },
    generatedBy: 'Reporter',
    generatedAtUtc: '2026-10-13T09:00:00Z',
    ...overrides,
  };
}

/** The member's privileges as `GET /api/v1/organization/me/privileges` answers them, with the report days limit under test. */
export function myPrivileges(reportDaysLimit: number | null, permissionKeys: readonly string[] = ['reporting.view']): MyPrivileges {
  return {
    userId: 'u-1',
    tenantId: 't-1',
    branchId: 'b-main',
    isPrimaryAdministrator: false,
    mustChangePassword: false,
    passwordExpiresAtUtc: null,
    privileges: {
      permissionKeys,
      specialRights: {
        discountCapPercent: null,
        canPay: false,
        canRefund: false,
        canTransfer: false,
        canCancel: false,
        canModifyVerifiedResult: false,
        canPreviewWithoutVerify: false,
        canPrintInvoice: false,
        canSeeVip: false,
        demographicsEditUpTo: 'Complete',
        canViewBranchCollections: false,
        reportDaysLimit,
      },
      resultRights: [],
    },
  };
}
