/**
 * Integration module contracts mirrored from `Platform.Contracts.Integration` and `Platform.Contracts.Identity`
 * (machine credentials) — hand-written until the OpenAPI generator lands (01 §6.2). Intake LIS-INT-INT-001.
 */

export type { Paged } from './paging';

export interface CreatedResource {
  readonly id: string;
}

export type AnalyserMode = 'Unidirectional' | 'Bidirectional' | 'Download' | 'SharedFolder';
export type ConnectionKind = 'Serial' | 'Tcp' | 'File';
export type TcpRole = 'Client' | 'Server';
export type FlagAction = 'Record' | 'Stop';
export type LinkStatus = 'Unknown' | 'Connected' | 'Disconnected' | 'Error';
export type SiteAgentStatus = 'Online' | 'Offline' | 'Error';
export type AgentLogLevel = 'Debug' | 'Info' | 'Warning' | 'Error';
export type IntakeStatus = 'Accepted' | 'Refused' | 'Unmapped' | 'AutoSendOff' | 'UnknownSample';
export type PcrVerdict = 'Detected' | 'NotDetected' | 'Presumptive' | 'Invalid';
export type GeneCall = 'Negative' | 'Borderline' | 'Positive';
export type CredentialStatus = 'Live' | 'Expired' | 'Revoked';

export const ANALYSER_MODES: readonly AnalyserMode[] = ['Unidirectional', 'Bidirectional', 'Download', 'SharedFolder'];
export const CONNECTION_KINDS: readonly ConnectionKind[] = ['Serial', 'Tcp', 'File'];
export const TCP_ROLES: readonly TcpRole[] = ['Client', 'Server'];
export const FLAG_ACTIONS: readonly FlagAction[] = ['Record', 'Stop'];
export const AGENT_LOG_LEVELS: readonly AgentLogLevel[] = ['Debug', 'Info', 'Warning', 'Error'];
export const INTAKE_STATUSES: readonly IntakeStatus[] = ['Accepted', 'Refused', 'Unmapped', 'AutoSendOff', 'UnknownSample'];

/** `9600,n,8,1`: baud, parity n/e/o/m/s, data bits 5–8, stop bits 1, 1.5 or 2 (CP-58; the server is authoritative, INT-005). */
export const SERIAL_SETTINGS_PATTERN = /^\s*\d{3,6}\s*,\s*[nemsoNEMSO]\s*,\s*[5-8]\s*,\s*(1|1\.5|2)\s*$/;

/** The offline threshold of CP-61, mirrored for the heartbeat age display. */
export const AGENT_OFFLINE_AFTER_MINUTES = 5;

// ---- analysers (CP-58)

export interface AnalyserFlagDto {
  readonly code: string;
  readonly action: FlagAction;
  readonly description: string | null;
}

export interface AnalyserConnectionDto {
  readonly kind: ConnectionKind;
  readonly serialSettings: string | null;
  readonly serialPort: string | null;
  readonly host: string | null;
  readonly port: number | null;
  readonly tcpRole: TcpRole | null;
  readonly folderPath: string | null;
}

/** Create or update; `code` is ignored on update (immutable). */
export interface SaveAnalyserRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly clinicalGroupId: string;
  readonly defaultSampleTypeId: string | null;
  readonly analyserModelId: string | null;
  readonly mode: AnalyserMode;
  readonly connection: AnalyserConnectionDto;
  readonly autoPrint: boolean;
  readonly downloadOrders: boolean;
  readonly appendUnregisteredTests: boolean;
  readonly appendUnregisteredSamples: boolean;
  readonly overwriteReviewed: boolean;
  readonly overwriteVerified: boolean;
  readonly printCopies: number;
  readonly flags: readonly AnalyserFlagDto[];
  readonly siteAgentId: string | null;
}

export interface AnalyserListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly clinicalGroupId: string;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly mode: AnalyserMode;
  readonly connectionKind: ConnectionKind;
  readonly siteAgentId: string | null;
  readonly siteAgentName: string | null;
  readonly linkStatus: LinkStatus;
  readonly lastSeenAtUtc: string | null;
  readonly mappingCount: number;
  readonly isActive: boolean;
}

export interface AnalyserDetail extends Omit<SaveAnalyserRequest, 'flags'> {
  readonly id: string;
  readonly flags: readonly AnalyserFlagDto[];
  readonly isActive: boolean;
}

export interface AnalyserConnectionState {
  readonly analyserId: string;
  readonly siteAgentId: string | null;
  readonly siteAgentName: string | null;
  readonly agentStatus: SiteAgentStatus;
  readonly agentLastHeartbeatAtUtc: string | null;
  readonly linkStatus: LinkStatus;
  readonly message: string | null;
  readonly lastSeenAtUtc: string | null;
  readonly checkedAtUtc: string;
}

export interface AnalyserModelItem {
  readonly id: string;
  readonly vendor: string;
  readonly model: string;
  readonly protocol: string | null;
  readonly physicalLink: string | null;
}

export interface AnalyserListFilter {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly siteAgentId: string | null;
  readonly page: number;
  readonly pageSize: number;
}

// ---- mappings (CP-59)

export interface ConversionDto {
  readonly fromValue: string;
  readonly toValue: string;
}

export interface RangeOverrideDto {
  readonly low: number | null;
  readonly high: number | null;
  readonly text: string | null;
}

export interface SaveMappingRequest {
  readonly testId: string;
  readonly analyserCode: string;
  readonly hostCode: string | null;
  readonly resultSelection: number;
  readonly slope: number;
  readonly intercept: number;
  readonly unitId: string | null;
  readonly query: boolean;
  readonly queryDetails: string | null;
  readonly autoSend: boolean;
  readonly conversions: readonly ConversionDto[];
  readonly rangeOverride: RangeOverrideDto | null;
  readonly methodology: string | null;
}

export interface MappingListItem {
  readonly id: string;
  readonly analyserId: string;
  readonly testId: string;
  readonly testCode: string | null;
  readonly testNameEn: string | null;
  readonly testNameAr: string | null;
  readonly analyserCode: string;
  readonly hostCode: string | null;
  readonly resultSelection: number;
  readonly slope: number;
  readonly intercept: number;
  readonly unitId: string | null;
  readonly unitCode: string | null;
  readonly query: boolean;
  readonly queryDetails: string | null;
  readonly autoSend: boolean;
  readonly conversions: readonly ConversionDto[];
  readonly rangeOverride: RangeOverrideDto | null;
  readonly methodology: string | null;
  readonly isActive: boolean;
}

export interface CodeSheetLine {
  readonly analyserCode: string;
  readonly hostCode: string | null;
  readonly testCode: string | null;
  readonly testNameEn: string | null;
  readonly testNameAr: string | null;
  readonly unitCode: string | null;
  readonly resultSelection: number;
  readonly slope: number;
  readonly intercept: number;
  readonly autoSend: boolean;
}

export interface CodeSheetSampleLine {
  readonly code: string;
  readonly sampleTypeCode: string | null;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
}

export interface AnalyserCodeSheetDocument {
  readonly analyserId: string;
  readonly analyserCode: string;
  readonly analyserNameEn: string;
  readonly analyserNameAr: string;
  readonly mode: AnalyserMode;
  readonly connectionSummary: string;
  readonly siteAgentName: string | null;
  readonly lines: readonly CodeSheetLine[];
  readonly sampleCodes: readonly CodeSheetSampleLine[];
  readonly generatedAtUtc: string;
  readonly generatedBy: string | null;
}

// ---- sample codes (CP-60)

export interface SaveSampleCodeRequest {
  readonly sampleTypeId: string;
  readonly code: string;
}

export interface SampleCodeItem {
  readonly id: string;
  readonly analyserId: string;
  readonly sampleTypeId: string;
  readonly sampleTypeCode: string | null;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
  readonly code: string;
  readonly isActive: boolean;
}

// ---- site agents (CP-61)

export interface CreateSiteAgentRequest {
  readonly name: string;
  readonly branchId: string;
  readonly configJson: string | null;
}

export interface UpdateSiteAgentRequest {
  readonly name: string;
  readonly configJson: string | null;
}

/** Shown once: the client must store the secret now. */
export interface SiteAgentSecretResponse {
  readonly agentId: string;
  readonly credentialId: string;
  readonly clientId: string;
  readonly clientSecret: string;
  readonly expiresAtUtc: string;
}

export interface SiteAgentListItem {
  readonly id: string;
  readonly name: string;
  readonly branchId: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly clientId: string | null;
  readonly credentialStatus: CredentialStatus | null;
  readonly version: string | null;
  readonly status: SiteAgentStatus;
  readonly lastHeartbeatAtUtc: string | null;
  readonly analyserCount: number;
  readonly configVersion: number;
  readonly isActive: boolean;
}

export interface SiteAgentAnalyserItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly linkStatus: LinkStatus;
  readonly lastSeenAtUtc: string | null;
  readonly message: string | null;
  readonly isActive: boolean;
}

export interface SiteAgentDetail {
  readonly id: string;
  readonly name: string;
  readonly branchId: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly configJson: string;
  readonly configVersion: number;
  readonly clientId: string | null;
  readonly credentialStatus: CredentialStatus | null;
  readonly credentialExpiresAtUtc: string | null;
  readonly version: string | null;
  readonly status: SiteAgentStatus;
  readonly statusMessage: string | null;
  readonly lastHeartbeatAtUtc: string | null;
  readonly analysers: readonly SiteAgentAnalyserItem[];
  readonly isActive: boolean;
}

export interface AgentLogItem {
  readonly id: string;
  readonly agentId: string;
  readonly atUtc: string;
  readonly level: AgentLogLevel;
  readonly message: string;
  readonly sampleNumber: string | null;
  readonly analyserCode: string | null;
  readonly receivedAtUtc: string;
}

/** `level` is the minimum level shown; dates are ISO strings (`from`/`to` inclusive). */
export interface AgentLogFilter {
  readonly from: string | null;
  readonly to: string | null;
  readonly level: AgentLogLevel | null;
  readonly search: string;
  readonly page: number;
  readonly pageSize: number;
}

export interface IntakeLogItem {
  readonly id: string;
  readonly agentId: string;
  readonly agentName: string | null;
  readonly analyserId: string | null;
  readonly analyserCode: string;
  readonly sampleNumber: string;
  readonly analyserTestCode: string;
  readonly testId: string | null;
  readonly rawValue: string;
  readonly convertedValue: string | null;
  readonly flags: readonly string[];
  readonly status: IntakeStatus;
  readonly reason: string | null;
  readonly source: 'Analyser' | 'Pcr';
  readonly fileName: string | null;
  readonly measuredAtUtc: string | null;
  readonly atUtc: string;
}

export interface IntakeFilter {
  readonly from: string | null;
  readonly to: string | null;
  readonly status: IntakeStatus | null;
  readonly analyserId: string | null;
  readonly agentId: string | null;
  readonly search: string;
  readonly page: number;
  readonly pageSize: number;
}

// ---- PCR kits (CP-62)

export interface PcrGeneDto {
  readonly name: string;
  readonly isInternalControl: boolean;
  readonly positiveCtMax: number;
  readonly borderlineCtMax: number;
}

export interface PcrRunFileLayoutDto {
  readonly delimiter: string;
  readonly sampleColumn: string;
  readonly geneColumn: string;
  readonly wellColumn: string | null;
  readonly contentColumn: string | null;
  readonly patientContentValue: string | null;
  readonly serialLabel: string | null;
  readonly noAmplificationTokens: readonly string[];
}

export interface PcrVerdictRulesDto {
  readonly detectedMinPositiveTargets: number;
  readonly detectedText: string;
  readonly notDetectedText: string;
  readonly presumptiveText: string;
  readonly invalidText: string;
}

export interface SavePcrKitRequest {
  readonly name: string;
  readonly analyserId: string;
  readonly analyserTestCode: string;
  readonly serialLock: string | null;
  readonly layout: PcrRunFileLayoutDto;
  readonly valueColumn: string;
  readonly genes: readonly PcrGeneDto[];
  readonly rules: PcrVerdictRulesDto;
  readonly resultFolder: string;
  readonly backupFolder: string;
  readonly failedFolder: string;
}

export interface PcrKitListItem {
  readonly id: string;
  readonly name: string;
  readonly analyserId: string;
  readonly analyserCode: string | null;
  readonly analyserNameEn: string | null;
  readonly analyserNameAr: string | null;
  readonly analyserTestCode: string;
  readonly serialLock: string | null;
  readonly geneCount: number;
  readonly isActive: boolean;
}

export interface PcrKitDetail extends SavePcrKitRequest {
  readonly id: string;
  readonly isActive: boolean;
}

export interface SimulatePcrRunRequest {
  readonly instrumentSerial: string | null;
  readonly content: string;
}

export interface PcrGeneReadingView {
  readonly gene: string;
  readonly ct: number | null;
  readonly call: GeneCall;
  readonly isInternalControl: boolean;
}

export interface PcrSampleView {
  readonly sampleNumber: string;
  readonly well: string | null;
  readonly genes: readonly PcrGeneReadingView[];
  readonly verdict: PcrVerdict | null;
  readonly verdictText: string | null;
  readonly problem: string | null;
}

export interface PcrRunView {
  readonly readable: boolean;
  readonly disposition: 'Backup' | 'Failed';
  readonly refusalCode: string | null;
  readonly refusalReason: string | null;
  readonly fileSerial: string | null;
  readonly samples: readonly PcrSampleView[];
}

// ---- machine credentials (02 §6.3; Platform.Contracts.Identity)

export interface MachineCredentialListItem {
  readonly id: string;
  readonly name: string;
  readonly clientId: string;
  readonly scopes: readonly string[];
  readonly branchId: string | null;
  readonly status: CredentialStatus;
  readonly createdAtUtc: string;
  readonly expiresAtUtc: string;
  readonly lastUsedAtUtc: string | null;
  readonly secretRotatedAtUtc: string | null;
  readonly revokedAtUtc: string | null;
  readonly revocationReason: string | null;
}

// ---- lookups of other modules (documented paths; their own contracts may grow)

/** A row of a catalogue or organization list as this module needs it. */
export interface ReferenceOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** `GET /api/v1/catalogue/lookups/tests?search=` item. */
export interface TestLookupOption extends ReferenceOption {
  readonly clinicalGroupId: string | null;
  readonly type: string;
}

export const INTEGRATION_API = {
  analysers: '/api/v1/integration/analysers',
  siteAgents: '/api/v1/integration/site-agents',
  pcrKits: '/api/v1/integration/pcr-kits',
  intake: '/api/v1/integration/intake',
  analyserModels: '/api/v1/integration/analyser-models',
  machineCredentials: '/api/v1/identity/machine-credentials',
} as const;

export const INTEGRATION_LOOKUP_API = {
  tests: '/api/v1/catalogue/lookups/tests',
  clinicalGroups: '/api/v1/catalogue/clinical-groups',
  sampleTypes: '/api/v1/catalogue/sample-types',
  units: '/api/v1/catalogue/units',
  branches: '/api/v1/organization/branches',
} as const;

/** Permission keys of the module, as the catalogue defines them (intake §4). */
export const INTEGRATION_PERMISSIONS = {
  view: 'integration.view',
  create: 'integration.create',
  edit: 'integration.edit',
  manage: 'integration.manage',
  export: 'integration.export',
} as const;
