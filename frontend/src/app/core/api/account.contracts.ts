/**
 * Account & onboarding contracts mirrored from `Platform.Contracts.Account` (intake LIS-INT-ACC-001; bootstrap until
 * the OpenAPI generator lands, 01 §6.2). camelCase as the API serialises them.
 */

// ---- Self-service account (CP-65 / SA-21) ----

export type AccountLanguage = 'en' | 'ar';

export interface UpdatePreferencesRequest {
  readonly language: AccountLanguage;
}

/** Both values are `[CredentialField]`: the envelope interceptor seals them (D-03) once `credential-fields.ts` lists the endpoint. */
export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
}

/** The password rule that applies to the user (02 §6.1 as decided on 2026-10-09): length only, never the username. */
export interface PasswordPolicyView {
  readonly minLength: number;
  readonly maxLength: number;
}

/** The platform's client record of the laboratory; every field but the tenant id may be `null` (no client record yet). */
export interface LaboratoryProfile {
  readonly tenantId: string;
  readonly nameEn: string | null;
  readonly nameAr: string | null;
  readonly portalUrl: string | null;
  readonly countryNameEn: string | null;
  readonly countryNameAr: string | null;
  readonly planCode: string | null;
  readonly planNameEn: string | null;
  readonly planNameAr: string | null;
  readonly managerName: string | null;
  readonly managerMobile: string | null;
  readonly mobile: string | null;
  readonly email: string | null;
  readonly status: string | null;
  readonly trialEndsAtUtc: string | null;
  /** The logo's version (it changes on every upload or removal); null when the laboratory has no logo. */
  readonly logoVersion?: number | null;
}

/** The laboratory logo after an upload (`POST /api/v1/account/laboratory/logo`). */
export interface LaboratoryLogoView {
  readonly version: number;
  readonly contentType: string;
  readonly sizeBytes: number;
}

export interface MyAccountProfile {
  readonly userId: string;
  readonly username: string;
  readonly displayName: string;
  readonly email: string;
  readonly mobile: string | null;
  readonly language: string;
  readonly tenantId: string | null;
  readonly isPlatform: boolean;
  readonly fullNameEn: string | null;
  readonly fullNameAr: string | null;
  readonly employeeCode: string | null;
  readonly designation: string | null;
  readonly roleNameEn: string | null;
  readonly roleNameAr: string | null;
  readonly mustChangePassword: boolean;
  readonly passwordChangedAtUtc: string;
  readonly passwordExpiresAtUtc: string | null;
  readonly passwordPolicy: PasswordPolicyView;
  /** `null` for the platform tenant (SA-21). */
  readonly laboratory: LaboratoryProfile | null;
}

/** A member of the tenant for the audit log's user filter. */
export interface AuditActorItem {
  readonly userId: string;
  readonly displayName: string;
  readonly username: string;
  readonly isActive: boolean;
}

// ---- Support tickets (CP-67) and the platform desk ----

export type SupportTicketStatus = 'Open' | 'InProgress' | 'Resolved' | 'Closed';
export type SupportTicketPriority = 'Low' | 'Normal' | 'High';

export const SUPPORT_TICKET_STATUSES: readonly SupportTicketStatus[] = ['Open', 'InProgress', 'Resolved', 'Closed'];
export const SUPPORT_TICKET_PRIORITIES: readonly SupportTicketPriority[] = ['Low', 'Normal', 'High'];

export interface OpenSupportTicketRequest {
  readonly subject: string;
  readonly description: string;
  readonly priority: SupportTicketPriority;
}

export interface SupportTicketReplyRequest {
  readonly text: string;
}

export interface ResolveSupportTicketRequest {
  readonly note: string | null;
}

export interface SupportTicketListItem {
  readonly id: string;
  readonly number: string;
  readonly tenantId: string;
  readonly laboratoryNameEn: string | null;
  readonly laboratoryNameAr: string | null;
  readonly subject: string;
  readonly priority: SupportTicketPriority;
  readonly status: SupportTicketStatus;
  readonly openedByUserId: string;
  readonly openedByName: string;
  readonly openedAtUtc: string;
  readonly lastActivityAtUtc: string;
  readonly replyCount: number;
  readonly lastReplyFromPlatform: boolean;
}

export interface SupportTicketReplyView {
  readonly id: string;
  readonly authorUserId: string;
  readonly authorName: string;
  readonly text: string;
  readonly fromPlatform: boolean;
  readonly atUtc: string;
}

export interface SupportTicketDetail {
  readonly id: string;
  readonly number: string;
  readonly tenantId: string;
  readonly laboratoryNameEn: string | null;
  readonly laboratoryNameAr: string | null;
  readonly subject: string;
  readonly description: string;
  readonly priority: SupportTicketPriority;
  readonly status: SupportTicketStatus;
  readonly openedByUserId: string;
  readonly openedByName: string;
  readonly openedAtUtc: string;
  readonly lastActivityAtUtc: string;
  readonly resolvedAtUtc: string | null;
  readonly closedAtUtc: string | null;
  readonly replies: readonly SupportTicketReplyView[];
}

export interface SupportTicketFilter {
  readonly status: SupportTicketStatus | null;
  readonly priority: SupportTicketPriority | null;
  readonly search: string;
  readonly mine: boolean;
  readonly page: number;
  readonly pageSize: number;
}

export interface DeskTicketFilter {
  readonly tenantId: string | null;
  readonly status: SupportTicketStatus | null;
  readonly priority: SupportTicketPriority | null;
  readonly search: string;
  readonly page: number;
  readonly pageSize: number;
}

// ---- Branch dashboard (CP-05) ----

/** A `null` figure means "not available yet" (its module is not built or not migrated), never zero. */
export interface BranchDashboardKpis {
  readonly registrations: number | null;
  readonly collectedToday: number | null;
  readonly samplesReceived: number | null;
  readonly pendingResults: number | null;
  readonly unverifiedBacklog: number | null;
  readonly delayedTests: number | null;
  readonly rejectedSamples: number | null;
  readonly walletBalance: number | null;
  readonly walletCurrency: string | null;
}

export interface DailyCount {
  /** Local day, `yyyy-MM-dd`. */
  readonly date: string;
  readonly count: number;
}

export interface DelayedTestItem {
  readonly visitId: string;
  readonly accession: string;
  readonly patientName: string | null;
  readonly testName: string;
  readonly expectedAtUtc: string;
  readonly delayMinutes: number;
  /** The result status: Ordered, Complete or Reviewed. */
  readonly status: string;
  /** The Arabic test name (UAT R1-02); absent from older servers. */
  readonly testNameAr?: string | null;
}

export interface BranchDashboard {
  readonly branchId: string;
  readonly date: string;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly currencyCode: string;
  readonly kpis: BranchDashboardKpis;
  readonly registrationsByDay: readonly DailyCount[];
  readonly delayedTests: readonly DelayedTestItem[];
}

export interface BranchDashboardQuery {
  /** `yyyy-MM-dd`, the user's local day. */
  readonly date: string;
  /** Minutes east of UTC (`-new Date().getTimezoneOffset()`). */
  readonly utcOffsetMinutes: number;
}

export const ACCOUNT_API = {
  profile: '/api/v1/account/profile',
  /** GET the image (any member), POST multipart `file` / DELETE with settings.manage. */
  laboratoryLogo: '/api/v1/account/laboratory/logo',
  preferences: '/api/v1/account/preferences',
  password: '/api/v1/account/password',
  auditActors: '/api/v1/account/audit/actors',
  auditExport: '/api/v1/account/audit/export',
  tickets: '/api/v1/support/tickets',
  deskTickets: '/api/v1/support/desk/tickets',
  dashboard: '/api/v1/dashboard/branch',
} as const;

/** Permission keys the module's screens gate on (affordance; the server decides). */
export const ACCOUNT_PERMISSIONS = {
  supportView: 'support.view',
  supportCreate: 'support.create',
  supportManage: 'support.manage',
  walletView: 'wallet.view',
  walletCreate: 'wallet.create',
  dashboardView: 'dashboard.view',
  /** The collected-money tile (validation observation O-9): payments are seen only with the billing right. */
  billingView: 'billing.view',
  auditView: 'audit.view',
  auditExport: 'audit.export',
} as const;
