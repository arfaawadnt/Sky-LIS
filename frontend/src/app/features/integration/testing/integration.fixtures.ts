import type { AnalyserListItem, MappingListItem, Paged, PcrKitListItem, SiteAgentListItem } from '../../../core/api/integration.contracts';

export const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

export function page<T>(items: readonly T[], pageSize = 50): Paged<T> {
  return { items, page: 1, pageSize, totalCount: items.length, totalPages: items.length === 0 ? 0 : 1 };
}

export const analyser: AnalyserListItem = {
  id: 'an-1',
  code: 'XN550',
  nameEn: 'Sysmex XN-550',
  nameAr: 'سيسمكس',
  clinicalGroupId: 'cg-1',
  clinicalGroupNameEn: 'Haematology',
  clinicalGroupNameAr: 'أمراض الدم',
  mode: 'Unidirectional',
  connectionKind: 'Serial',
  siteAgentId: 'ag-1',
  siteAgentName: 'Main lab agent',
  linkStatus: 'Connected',
  lastSeenAtUtc: '2026-10-10T08:59:00Z',
  mappingCount: 2,
  isActive: true,
};

export const mapping: MappingListItem = {
  id: 'mp-1',
  analyserId: 'an-1',
  testId: 't-wbc',
  testCode: 'WBC',
  testNameEn: 'White blood cells',
  testNameAr: 'كريات الدم البيضاء',
  analyserCode: 'WBC',
  hostCode: null,
  resultSelection: 1,
  slope: 1,
  intercept: 0,
  unitId: null,
  unitCode: null,
  query: false,
  queryDetails: null,
  autoSend: false,
  conversions: [{ fromValue: '<10', toValue: 'Low' }],
  rangeOverride: null,
  methodology: null,
  isActive: true,
};

export const agent: SiteAgentListItem = {
  id: 'ag-1',
  name: 'Main lab agent',
  branchId: 'br-1',
  branchNameEn: 'Main branch',
  branchNameAr: 'الفرع الرئيسي',
  clientId: 'lis-0123456789abcdef0123456789abcdef',
  credentialStatus: 'Live',
  version: '2.1.0',
  status: 'Online',
  lastHeartbeatAtUtc: '2026-10-10T08:59:00Z',
  analyserCount: 1,
  configVersion: 3,
  isActive: true,
};

export const kit: PcrKitListItem = {
  id: 'kit-1',
  name: 'Biorad_Solgent',
  analyserId: 'an-2',
  analyserCode: 'BIORAD',
  analyserNameEn: 'BioRad CFX',
  analyserNameAr: 'بيوراد',
  analyserTestCode: 'COVID',
  serialLock: '785BR29753',
  geneCount: 4,
  isActive: true,
};
