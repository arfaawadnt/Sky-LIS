/**
 * Platform administration contracts mirrored from `Platform.Contracts.PlatformAdmin` (Super Admin portal SA-03…SA-20,
 * the laboratory's wallet CP-66; bootstrap until the OpenAPI generator lands, 01 §6.2). JSON is camelCase; mobiles are
 * E.164; amounts are numbers with two decimals.
 */

import type { SortRequest } from './paging';

export type { Paged } from './paging';

export interface CreatedResource {
  readonly id: string;
}

// ── Clients (SA-04) ──────────────────────────────────────────────────────────────────────────────

export type ClientStatus = 'Trial' | 'Active' | 'Inactive';
export type ClientSource = 'Subscription' | 'Manual';

export const CLIENT_STATUSES: readonly ClientStatus[] = ['Trial', 'Active', 'Inactive'];
export const CLIENT_SOURCES: readonly ClientSource[] = ['Subscription', 'Manual'];

export interface ClientListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly portalSlug: string;
  readonly portalUrl: string;
  readonly countryId: string;
  readonly countryNameEn: string;
  readonly countryNameAr: string;
  readonly governorateId: string;
  readonly governorateNameEn: string;
  readonly governorateNameAr: string;
  readonly managerName: string;
  readonly managerMobile: string;
  readonly planId: string;
  readonly planCode: string;
  readonly planNameEn: string;
  readonly planNameAr: string;
  readonly walletBalance: number;
  readonly walletCurrency: string;
  readonly branches: number;
  readonly users: number;
  readonly createdAtUtc: string;
  readonly source: ClientSource;
  readonly status: ClientStatus;
  readonly trialEndsAtUtc: string | null;
  /** LIS-CR-008 C-04: "Area, District, City, Governorate" when the client has a city (null otherwise). */
  readonly addressLineEn?: string | null;
  readonly addressLineAr?: string | null;
}

export interface ClientDetail {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly countryId: string;
  readonly governorateId: string;
  readonly address: string;
  readonly mobile: string;
  readonly licenceNo: string | null;
  readonly managerName: string;
  readonly managerMobile: string;
  readonly email: string | null;
  readonly planId: string;
  readonly portalSlug: string;
  readonly portalUrl: string;
  readonly language: string;
  readonly source: ClientSource;
  readonly status: ClientStatus;
  readonly createdAtUtc: string;
  readonly trialEndsAtUtc: string | null;
  readonly activatedAtUtc: string | null;
  readonly deactivatedAtUtc: string | null;
  readonly deactivationReason: string | null;
  readonly administratorUserId: string | null;
  readonly administratorUsername: string | null;
  readonly walletBalance: number;
  readonly walletCurrency: string;
  readonly branches: number;
  readonly users: number;
  /** LIS-CR-008 C-04: the optional city (markaz) / district / area under the governorate. */
  readonly cityId?: string | null;
  readonly districtId?: string | null;
  readonly areaId?: string | null;
  readonly addressLineEn?: string | null;
  readonly addressLineAr?: string | null;
}

/** `sort`: `nameEn`, `nameAr`, `planCode`, `walletBalance` or `createdAtUtc` (B-03, server allow-list); absent = newest first. */
export interface ClientFilter extends SortRequest {
  readonly search: string | null;
  readonly source: ClientSource | null;
  readonly planId: string | null;
  readonly status: ClientStatus | null;
  readonly page: number;
  readonly pageSize: number;
}

/** The editable details of a client (SA-04 drawer). */
export interface ClientDetailsRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly countryId: string;
  readonly governorateId: string;
  readonly address: string;
  readonly mobile: string;
  readonly licenceNo: string | null;
  readonly managerName: string;
  readonly managerMobile: string;
  readonly email: string | null;
  readonly planId: string;
  readonly language: string | null;
  /** LIS-CR-008 C-04: optional; null clears (GEO-020 city of another governorate, GEO-021 / GEO-022 a broken chain). */
  readonly cityId?: string | null;
  readonly districtId?: string | null;
  readonly areaId?: string | null;
}

/** SA-04 "Add": `adminPassword` is a `[CredentialField]`, sealed by the envelope interceptor. */
export interface ProvisionClientRequest extends ClientDetailsRequest {
  readonly portalSlug: string | null;
  readonly adminUsername: string;
  readonly adminPassword: string;
}

export type UpdateClientRequest = ClientDetailsRequest;

export interface ProvisionedClient {
  readonly id: string;
  readonly portalUrl: string;
  readonly administratorUsername: string;
}

/** SA-04 "Reset password": `newPassword` is a `[CredentialField]`. */
export interface ResetClientAdminPasswordRequest {
  readonly newPassword: string;
}

// ── Plans and wallets (SA-05, CP-66) ───────────────────────────────────────────────────────────

export type WalletTransactionKind = 'TopUp' | 'Subscription' | 'Usage';
export type TopUpRequestStatus = 'Pending' | 'Completed' | 'Declined';

export const WALLET_TRANSACTION_KINDS: readonly WalletTransactionKind[] = ['TopUp', 'Subscription', 'Usage'];

export interface PlanListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly monthlyFee: number;
  readonly currencyCode: string;
  readonly maxBranches: number;
  readonly maxUsers: number;
  readonly smsQuota: number;
  readonly maxAnalysers: number;
  readonly isActive: boolean;
  readonly clientCount: number;
}

export interface PlanRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly monthlyFee: number;
  readonly currencyCode: string;
  readonly maxBranches: number;
  readonly maxUsers: number;
  readonly smsQuota: number;
  readonly maxAnalysers: number;
}

export interface TopUpRequestItem {
  readonly id: string;
  readonly amount: number;
  readonly note: string | null;
  readonly status: TopUpRequestStatus;
  readonly requestedAtUtc: string;
  readonly requestedByUserId: string;
  readonly requestedByName: string | null;
  readonly decidedAtUtc: string | null;
}

export interface WalletDetail {
  readonly walletId: string;
  readonly tenantId: string;
  readonly clientNameEn: string;
  readonly clientNameAr: string;
  readonly balance: number;
  readonly currencyCode: string;
  readonly nextChargeAtUtc: string;
  readonly planId: string;
  readonly planCode: string;
  readonly planNameEn: string;
  readonly planNameAr: string;
  readonly monthlyFee: number;
  readonly planCurrencyCode: string;
  readonly smsQuota: number;
  readonly smsUsedThisMonth: number;
  readonly pendingTopUpRequests: readonly TopUpRequestItem[];
}

export interface WalletTransactionItem {
  readonly id: string;
  readonly kind: WalletTransactionKind;
  readonly amount: number;
  readonly balanceAfter: number;
  readonly reference: string;
  readonly note: string | null;
  readonly atUtc: string;
  readonly byUserId: string | null;
  readonly byName: string | null;
}

export interface TopUpWalletRequest {
  readonly amount: number;
  readonly reference: string;
  readonly note: string | null;
  readonly topUpRequestId: string | null;
}

/** Outcome of one due wallet in a monthly charge run (W2). */
export type WalletChargeOutcome = 'Charged' | 'Skipped' | 'Failed';

/** One due wallet of a charge run; `amount` is the ledger amount (negative debit); reason: trial, inactive, not-due, no-wallet, no-plan, currency-mismatch, error. */
export interface WalletChargeRunItem {
  readonly tenantId: string;
  readonly outcome: WalletChargeOutcome;
  readonly reason: string | null;
  readonly period: string | null;
  readonly amount: number | null;
  readonly balanceAfter: number | null;
}

/** SA-05 "Run charges now" (`tenancy.manage`): the same pass as the hosted monthly job. */
export interface WalletChargeRunSummary {
  readonly runAtUtc: string;
  readonly charged: number;
  readonly skipped: number;
  readonly failed: number;
  readonly atOrBelowZero: number;
  readonly items: readonly WalletChargeRunItem[];
}

/** CP-66 "Request top-up" (client tenant, `wallet.create`). */
export interface CreateTopUpRequest {
  readonly amount: number;
  readonly note: string | null;
}

// ── Countries, governorates, templates (SA-10, SA-11, SA-20) ───────────────────────────────────

export interface CountryListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isoCode: string;
  readonly dialCode: string;
  readonly currency: string;
  readonly currencyCode: string;
  readonly language: string;
  readonly timeZone: string;
  readonly mobileLength: number;
  readonly flag: string | null;
  readonly remarks: string | null;
  readonly isActive: boolean;
  readonly governorateCount: number;
}

export interface CountryRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isoCode: string;
  readonly dialCode: string;
  readonly currency: string;
  readonly currencyCode: string;
  readonly language: string;
  readonly timeZone: string;
  readonly mobileLength: number;
  readonly flag: string | null;
  readonly remarks: string | null;
}

export interface GovernorateListItem {
  readonly id: string;
  readonly countryId: string;
  readonly countryNameEn: string;
  readonly countryNameAr: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly district: string | null;
  readonly remarks: string | null;
  readonly isActive: boolean;
}

export interface GovernorateRequest {
  readonly countryId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly district: string | null;
  readonly remarks: string | null;
}

export type TemplateChannel = 'Sms' | 'Email';

export const TEMPLATE_CHANNELS: readonly TemplateChannel[] = ['Sms', 'Email'];

export interface NotificationTemplateListItem {
  readonly id: string;
  readonly code: string;
  readonly channel: TemplateChannel;
  readonly subjectEn: string | null;
  readonly subjectAr: string | null;
  readonly textEn: string;
  readonly textAr: string;
  readonly isActive: boolean;
}

export interface NotificationTemplateRequest {
  readonly code: string;
  readonly channel: TemplateChannel;
  readonly textEn: string;
  readonly textAr: string;
  readonly subjectEn: string | null;
  readonly subjectAr: string | null;
}

export interface TemplatePlaceholders {
  readonly placeholders: readonly string[];
  readonly aliases: Readonly<Record<string, string>>;
}

// ── Integration library (SA-15…SA-18) ──────────────────────────────────────────────────────────

export type AnalyserProtocol = 'Astm' | 'Hl7' | 'Proprietary';
export type PhysicalLink = 'Serial' | 'Tcp' | 'File';
export type CodeSystemName = 'Loinc' | 'Cpt' | 'Icd10' | 'Snomed';

export const ANALYSER_PROTOCOLS: readonly AnalyserProtocol[] = ['Astm', 'Hl7', 'Proprietary'];
export const PHYSICAL_LINKS: readonly PhysicalLink[] = ['Serial', 'Tcp', 'File'];
export const CODE_SYSTEM_NAMES: readonly CodeSystemName[] = ['Loinc', 'Cpt', 'Icd10', 'Snomed'];

export interface AnalyserModelListItem {
  readonly id: string;
  readonly vendor: string;
  readonly model: string;
  readonly discipline: string;
  readonly protocol: AnalyserProtocol;
  readonly physicalLink: PhysicalLink;
  readonly modes: readonly string[];
  readonly driverVersion: string | null;
  readonly isActive: boolean;
}

export interface AnalyserModelRequest {
  readonly vendor: string;
  readonly model: string;
  readonly discipline: string;
  readonly protocol: AnalyserProtocol;
  readonly physicalLink: PhysicalLink;
  readonly modes: readonly string[];
  readonly driverVersion: string | null;
}

export interface PcrKitListItem {
  readonly id: string;
  readonly name: string;
  readonly analyserModelId: string | null;
  readonly analyserModelName: string | null;
  readonly layoutJson: string;
  readonly thresholdsJson: string;
  readonly verdictRulesJson: string;
  readonly isActive: boolean;
}

export interface PcrKitRequest {
  readonly name: string;
  readonly analyserModelId: string | null;
  readonly layoutJson: string;
  readonly thresholdsJson: string;
  readonly verdictRulesJson: string;
}

export interface CodeSystemListItem {
  readonly id: string;
  readonly name: CodeSystemName;
  readonly version: string;
  readonly use: string | null;
  readonly itemCount: number;
  readonly lastImportedAtUtc: string | null;
  readonly isActive: boolean;
}

export interface CodeSystemRequest {
  readonly name: CodeSystemName;
  readonly version: string;
  readonly use: string | null;
}

export interface CodeSystemItemView {
  readonly code: string;
  readonly display: string;
}

export interface CodeSystemImportResult {
  readonly inserted: number;
  readonly updated: number;
  readonly unchanged: number;
  readonly total: number;
}

export interface RuleTemplateListItem {
  readonly id: string;
  readonly name: string;
  readonly clinicalGroupName: string | null;
  readonly formula: string;
  readonly description: string | null;
  readonly isActive: boolean;
}

export interface RuleTemplateRequest {
  readonly name: string;
  readonly clinicalGroupName: string | null;
  readonly formula: string;
  readonly description: string | null;
}

/** Shared list query of the reference registers. */
/** `sort`: a key of the list's server allow-list (B-03, PlatformAdminSortKeys); absent = the list's own order. */
export interface ReferenceListFilter extends SortRequest {
  readonly search: string | null;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

// ── Dashboard and sessions (SA-03, SA-08) ──────────────────────────────────────────────────────

export type DashboardPeriod = 'today' | 'week' | 'month' | 'year';

export const DASHBOARD_PERIODS: readonly DashboardPeriod[] = ['today', 'week', 'month', 'year'];

export interface ClientCounts {
  readonly total: number;
  readonly active: number;
  readonly inactive: number;
  readonly trial: number;
  readonly bySubscription: number;
  readonly byManual: number;
}

export interface ActivityTotals {
  readonly servedPatients: number;
  readonly tests: number;
  readonly registrations: number;
}

export interface PlanShare {
  readonly planId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly clients: number;
}

export interface ClientActivity {
  readonly tenantId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly registrations: number;
}

export interface RecentClient {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly portalUrl: string;
  readonly countryNameEn: string;
  readonly countryNameAr: string;
  readonly planCode: string;
  readonly walletBalance: number;
  readonly currencyCode: string;
  readonly createdAtUtc: string;
  readonly source: ClientSource;
  readonly status: ClientStatus;
}

export interface PlatformDashboard {
  readonly period: DashboardPeriod;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly clients: ClientCounts;
  readonly adminUsers: number;
  readonly activity: ActivityTotals;
  readonly walletsAtOrBelowZero: number;
  readonly planDistribution: readonly PlanShare[];
  readonly registrationsByClient: readonly ClientActivity[];
  readonly recentClients: readonly RecentClient[];
  /** LIS-CR-007 A-01: Trial and Active clients with no sign-in and no registration for `silentDays` days (default 3), and the trial ones among them. */
  readonly silentClients: number;
  readonly silentTrialClients: number;
  readonly silentDays: number;
  /** LIS-CR-012 (C-16): clients whose trial, wallet cover or grace ends within 7 days, the ones in grace and the expired ones (optional until the generator lands). */
  readonly expiringSoonClients?: number;
  readonly graceClients?: number;
  readonly expiredClients?: number;
}

// ── Subscription state (LIS-CR-012, C-16) ──────────────────────────────────────────────────────

/** The SA-04 "Subscription" filter; `expiring` = trial, wallet cover or grace ending within `days` (default 7). */
export type SubscriptionFilter = 'expiring' | 'grace' | 'expired';

export const SUBSCRIPTION_FILTERS: readonly SubscriptionFilter[] = ['expiring', 'grace', 'expired'];

/** One client of a subscription filter: level, reason, whole days to `endsAtUtc`, the amount that brings the wallet to one month of the fee, the manager to call and the last reminder sent. */
export interface SubscriptionClientItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly portalUrl: string;
  readonly status: ClientStatus;
  readonly level: 'Ok' | 'Warning' | 'Grace' | 'Expired';
  readonly reason: 'None' | 'Trial' | 'Wallet' | 'WalletAtZero';
  readonly daysLeft: number | null;
  readonly endsAtUtc: string | null;
  readonly trialEndsAtUtc: string | null;
  readonly walletBalance: number | null;
  readonly monthlyFee: number;
  readonly currencyCode: string | null;
  readonly amountToTopUp: number | null;
  readonly administratorName: string;
  readonly administratorMobile: string;
  readonly lastReminderKey: string | null;
  readonly lastReminderAtUtc: string | null;
}

export interface SubscriptionClientList {
  readonly state: SubscriptionFilter;
  readonly days: number;
  readonly graceDays: number;
  readonly asOfUtc: string;
  readonly items: readonly SubscriptionClientItem[];
}

// ── Silent clients (LIS-CR-007 A-01) ───────────────────────────────────────────────────────────

/** The windows the Clients page offers; the API takes 1…90 days (VALIDATION-400 otherwise). */
export const SILENT_DAYS: readonly number[] = [3, 7, 14, 30];

/** The window of the dashboard tile. */
export const DEFAULT_SILENT_DAYS = 3;

/**
 * A client with no activity for the window: last sign-in (a browser session issued to one of its users), last
 * registration, the later of the two (null: none since it was created), whole days of silence, and the administrator to
 * call (the client record's manager, mobile in E.164). Status is Trial or Active.
 */
export interface SilentClientItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly portalUrl: string;
  readonly status: ClientStatus;
  readonly createdAtUtc: string;
  readonly lastSignInAtUtc: string | null;
  readonly lastRegistrationAtUtc: string | null;
  readonly lastActivityAtUtc: string | null;
  readonly silentDays: number;
  readonly administratorName: string;
  readonly administratorMobile: string;
}

/** The silent clients of a window, longest silence first; `trial` counts the trial clients among them. */
export interface SilentClientList {
  readonly days: number;
  readonly asOfUtc: string;
  readonly trial: number;
  readonly items: readonly SilentClientItem[];
}

export type AdminSessionStatus = 'Active' | 'Idle';

export interface AdminSessionItem {
  readonly sessionId: string;
  readonly userId: string;
  readonly displayName: string;
  readonly username: string;
  readonly ipAddress: string | null;
  readonly sinceUtc: string;
  readonly lastSeenAtUtc: string;
  readonly expiresAtUtc: string;
  readonly status: AdminSessionStatus;
  readonly isCurrent: boolean;
}

// ── Endpoints, permissions, refusal codes ──────────────────────────────────────────────────────

export const PLATFORM_API = {
  clients: '/api/v1/platform/clients',
  plans: '/api/v1/platform/plans',
  wallets: '/api/v1/platform/wallets',
  /** W2 monthly subscription charge, on demand (SA-05 "Run charges now"). */
  walletChargeRun: '/api/v1/platform/wallets/charge-run',
  countries: '/api/v1/platform/countries',
  governorates: '/api/v1/platform/governorates',
  templates: '/api/v1/platform/notification-templates',
  analyserModels: '/api/v1/platform/library/analyser-models',
  pcrKits: '/api/v1/platform/library/pcr-kits',
  codeSystems: '/api/v1/platform/library/code-systems',
  ruleTemplates: '/api/v1/platform/library/rule-templates',
  sessions: '/api/v1/platform/sessions',
  /** SA-08b client login logs (LIS-CR-003 §2): the paged history and its CSV (`…/export`). */
  loginLogs: '/api/v1/platform/login-logs',
  dashboard: '/api/v1/platform/dashboard',
  /** LIS-CR-007 A-01: the silent clients of a window (`?days=`) and their CSV (`…/export`). */
  silentClients: '/api/v1/platform/clients/silent',
  /** LIS-CR-012 (C-16): the clients of a subscription filter (`?state=expiring|grace|expired&days=`). */
  subscriptionClients: '/api/v1/platform/clients/subscriptions',
  /** The laboratory's own wallet (CP-66). */
  myWallet: '/api/v1/wallet',
} as const;

/** Permission keys of the pages (the server also requires the platform tenant: PLT-403). */
export const PLATFORM_PERMISSIONS = {
  platformView: 'platform.view',
  platformManage: 'platform.manage',
  tenancyView: 'tenancy.view',
  tenancyCreate: 'tenancy.create',
  tenancyEdit: 'tenancy.edit',
  tenancyManage: 'tenancy.manage',
  notificationsView: 'notifications.view',
  notificationsManage: 'notifications.manage',
  integrationView: 'integration.view',
  integrationManage: 'integration.manage',
  settingsView: 'settings.view',
  settingsManage: 'settings.manage',
  walletView: 'wallet.view',
  walletCreate: 'wallet.create',
} as const;

/** The largest code file SA-17 accepts. */
export const CODE_IMPORT_MAX_BYTES = 20 * 1024 * 1024;

// ── Client login logs (SA-08b, change request LIS-CR-003 §2) ───────────────────────────────────

export type LoginLogDevice = 'Computer' | 'Mobile' | 'Tablet' | 'Unknown';
export type LoginLogStatus = 'Active' | 'SignedOut' | 'Revoked' | 'Expired';

export const LOGIN_LOG_DEVICES: readonly LoginLogDevice[] = ['Computer', 'Mobile', 'Tablet', 'Unknown'];
export const LOGIN_LOG_STATUSES: readonly LoginLogStatus[] = ['Active', 'SignedOut', 'Revoked', 'Expired'];

/**
 * One browser session of a client user (a rotation family): login and logout in UTC, the device read from the user
 * agent, the duration in minutes (logout − login, or last activity − login while active) and how it ended.
 */
export interface ClientLoginLogItem {
  readonly familyId: string;
  readonly tenantId: string;
  readonly clientNameEn: string | null;
  readonly clientNameAr: string | null;
  readonly userId: string;
  readonly username: string;
  readonly displayName: string;
  readonly loginAtUtc: string;
  readonly logoutAtUtc: string | null;
  readonly lastSeenAtUtc: string;
  readonly ipAddress: string | null;
  readonly device: LoginLogDevice;
  readonly os: string;
  readonly browser: string;
  readonly durationMinutes: number;
  readonly status: LoginLogStatus;
  readonly revocationReason: string | null;
}

/** The filter of the history (query parameters of `GET /api/v1/platform/login-logs`); instants are ISO UTC. */
export interface ClientLoginLogFilter extends SortRequest {
  readonly tenantId: string | null;
  readonly search: string | null;
  readonly loginFromUtc: string | null;
  readonly loginToUtc: string | null;
  readonly logoutFromUtc: string | null;
  readonly logoutToUtc: string | null;
  readonly device: LoginLogDevice | null;
  readonly minMinutes: number | null;
  readonly maxMinutes: number | null;
  readonly status: LoginLogStatus | null;
  readonly page: number;
  readonly pageSize: number;
}

// ── Location hierarchy under a governorate (SA-11b "Locations", change request LIS-CR-003 §3) ───

/** The three levels under a governorate: `cities` of a governorate, `districts` of a city, `areas` of a district. */
export type LocationLevel = 'cities' | 'districts' | 'areas';

export const LOCATION_LEVELS: readonly LocationLevel[] = ['cities', 'districts', 'areas'];

/** A city, district or area row of the Locations drawer; `parentId` is the governorate, the city or the district. */
export interface LocationListItem {
  readonly id: string;
  readonly parentId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

/** Create and rename a city, district or area; the parent is fixed once created. */
export interface LocationRequest {
  readonly nameEn: string;
  readonly nameAr: string;
}

/**
 * Where each level lives: the collection of a parent (`GET/POST <parent collection>/{parentId}/<level>`) and the item
 * (`PUT <items>/{id}`, `POST <items>/{id}/deactivate|reactivate`).
 */
export const PLATFORM_LOCATIONS_API: Readonly<Record<LocationLevel, { readonly parents: string; readonly items: string }>> = {
  cities: { parents: '/api/v1/platform/governorates', items: '/api/v1/platform/cities' },
  districts: { parents: '/api/v1/platform/cities', items: '/api/v1/platform/districts' },
  areas: { parents: '/api/v1/platform/districts', items: '/api/v1/platform/areas' },
};

// ── Locality import (SA-11 "Import CSV", LIS-CR-008 C-04, decision D-2) ─────────────────────────────

/** `POST …/governorates/{id}/localities/import?dryRun=` (multipart field `file`): UTF-8 CSV, at most 4 MB. */
export const localityImportUrl = (governorateId: string): string => `/api/v1/platform/governorates/${encodeURIComponent(governorateId)}/localities/import`;

export const LOCALITY_IMPORT_MAX_BYTES = 4 * 1024 * 1024;

/** One level of an import: rows created, distinct existing rows matched, lines refused at this level. */
export interface LocalityImportLevel {
  readonly created: number;
  readonly existing: number;
  readonly refused: number;
}

/** A refused line (1 = the header): `level` City | District | Area, `code` GEO-025 / GEO-015 / GEO-006. */
export interface LocalityImportError {
  readonly line: number;
  readonly level: string;
  readonly code: string;
  readonly message: string;
}

/** `applied` only when nothing was refused and the call was not a dry run (all-or-nothing). */
export interface LocalityImportResult {
  readonly rows: number;
  readonly cities: LocalityImportLevel;
  readonly districts: LocalityImportLevel;
  readonly areas: LocalityImportLevel;
  readonly dryRun: boolean;
  readonly applied: boolean;
  readonly errors: readonly LocalityImportError[];
}
