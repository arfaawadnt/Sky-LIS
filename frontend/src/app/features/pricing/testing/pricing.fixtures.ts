import type { BasePriceListDetail, BasePriceListItem, ContractPricingDetail, Paged, PriceLineItem, TierOption } from '../../../core/api/pricing.contracts';

/** Shared test data of the pricing specs (not shipped: nothing outside specs imports this file). */

export const LIST_DETAIL: BasePriceListDetail = {
  id: 'pl26',
  code: 'PL26',
  nameEn: 'Main price list 2026',
  nameAr: 'قائمة 2026',
  year: 2026,
  isActive: true,
  lineCount: 2,
  activeLineCount: 2,
  copiedFromListId: null,
  copiedFromCode: null,
  isCashDefault: true,
  derivedContracts: [],
};

export const LIST_LINES: readonly PriceLineItem[] = [
  { id: 'l1', serviceId: 's-cbc', price: 180, isActive: true, serviceCode: 'CBC', serviceNameEn: 'Complete blood count', serviceNameAr: 'صورة دم كاملة' },
  { id: 'l2', serviceId: 's-lip', price: 300, isActive: true, serviceCode: 'LIP', serviceNameEn: 'Lipid profile', serviceNameAr: 'دهون' },
];

export const TIER_OPTIONS: readonly TierOption[] = [
  { id: 't-cash', payerId: 'p-cash', payerNameEn: 'Cash', payerNameAr: 'نقدي', payerType: 'Cash', nameEn: 'Default', nameAr: 'افتراضي', isActive: true, payerIsActive: true, isDefault: true, hasPricing: false },
  { id: 't-gold', payerId: 'p-axa', payerNameEn: 'AXA', payerNameAr: 'أكسا', payerType: 'Insurance', nameEn: 'Gold', nameAr: 'ذهبي', isActive: true, payerIsActive: true, isDefault: false, hasPricing: true },
  { id: 't-silver', payerId: 'p-axa', payerNameEn: 'AXA', payerNameAr: 'أكسا', payerType: 'Insurance', nameEn: 'Silver', nameAr: 'فضي', isActive: true, payerIsActive: true, isDefault: false, hasPricing: true },
];

export const GOLD: ContractPricingDetail = {
  exists: true,
  contractPricingId: 'cp-gold',
  payerId: 'p-axa',
  payerNameEn: 'AXA',
  payerNameAr: 'أكسا',
  payerType: 'Insurance',
  payerIsActive: true,
  contractTierId: 't-gold',
  tierNameEn: 'Gold',
  tierNameAr: 'ذهبي',
  tierIsActive: true,
  tierIsDefault: false,
  effectiveFrom: '2026-07-01',
  effectiveTo: '2027-06-30',
  inEffect: true,
  appliedAtUtc: '2026-07-01T08:00:00Z',
  currency: 'EGP',
  terms: [{ priority: 1, baseListId: 'pl26', listCode: 'PL26', listNameEn: 'Main 2026', listNameAr: 'رئيسية 2026', listYear: 2026, listIsActive: true, patientFactor: 0.1, payerFactor: 0.9 }],
  prices: [
    { serviceId: 's-cbc', listPrice: 180, patientShare: 18, payerShare: 162, isExcluded: false, alertCondition: null, sourceListId: 'pl26', sourceListCode: 'PL26', serviceCode: 'CBC', serviceNameEn: 'Complete blood count', serviceNameAr: 'صورة دم' },
    { serviceId: 's-lip', listPrice: 300, patientShare: 20, payerShare: 250, isExcluded: true, alertCondition: 'Prior approval', sourceListId: 'pl26', sourceListCode: 'PL26', serviceCode: 'LIP', serviceNameEn: 'Lipid profile', serviceNameAr: 'دهون' },
  ],
};

export const LISTS_PAGE: Paged<BasePriceListItem> = {
  items: [
    { id: 'pl26', code: 'PL26', nameEn: 'Main 2026', nameAr: 'رئيسية 2026', year: 2026, isActive: true, lineCount: 2, activeLineCount: 2, derivedContractCount: 1, copiedFromListId: null, copiedFromCode: null },
    { id: 'inst', code: 'INST26', nameEn: 'Institutional', nameAr: 'مؤسسات', year: 2026, isActive: true, lineCount: 2, activeLineCount: 2, derivedContractCount: 0, copiedFromListId: null, copiedFromCode: null },
  ],
  page: 1,
  pageSize: 200,
  totalCount: 2,
  totalPages: 1,
};

