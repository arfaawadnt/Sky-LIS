import type { Portal } from '../core/portal/portal';
import { type Release, RELEASE_SCOPE, inReleaseScope } from '../core/release';
import type { IconName } from '../shared/ui/icon/icon.component';

// The release switch lives in core/release.ts (features gate their own tiles with it); re-exported for the shell's callers.
export { RELEASE_SCOPE, inReleaseScope };
export type { Release };

/**
 * A sidebar group: one per SRS module (`modules.client` / `modules.admin` of `srs.data.js`), in display order.
 * Entries reference a group by its i18n key; a group without visible entries is not rendered.
 */
export interface NavGroup {
  readonly portal: Portal;
  /** `shell.group.<name>`; also the key entries use in `group`. */
  readonly i18nKey: string;
  readonly icon: IconName;
}

/**
 * Route, permission key and i18n key in one place (01 §6.5); an e2e test walks it. `permission: null` = any
 * signed-in user of the portal. The integrator appends one entry per page from each module's report.
 */
export interface NavEntry {
  readonly portal: Portal;
  /** The `i18nKey` of a `NavGroup` of the same portal. */
  readonly group: string;
  readonly path: string;
  readonly permission: string | null;
  readonly i18nKey: string;
  readonly release: Release;
  readonly icon?: IconName;
  /**
   * Whether the sidebar lists the entry (R1-01). Omitted: listed whenever the entry is in scope. `false`: in scope and
   * guarded like any entry, but opened only from an action of another page, never from the menu. A release: listed from
   * that release on, action-only before it (Release 1 refunds are an action of Payments; Release 2 gives them a menu entry).
   * The route guard ignores this field: an action-only page opens for whoever holds its permission.
   */
  readonly menu?: false | Release;
}

/** True when the sidebar lists the entry in the delivered release: in scope, and not an action-only page (R1-01). */
export function inMenu(entry: NavEntry, scope: Release = RELEASE_SCOPE): boolean {
  if (!inReleaseScope(entry.release, scope)) {
    return false;
  }

  return entry.menu === undefined || (entry.menu !== false && inReleaseScope(entry.menu, scope));
}

export const NAV_GROUPS: readonly NavGroup[] = [
  // client portal (SRS modules.client; "Access & onboarding" is the public/sign-in flow, not a sidebar group)
  { portal: 'client', i18nKey: 'shell.group.overview', icon: 'grid' },
  { portal: 'client', i18nKey: 'shell.group.reception', icon: 'clipboard' },
  { portal: 'client', i18nKey: 'shell.group.samples', icon: 'flask' },
  { portal: 'client', i18nKey: 'shell.group.laboratory', icon: 'microscope' },
  { portal: 'client', i18nKey: 'shell.group.finance', icon: 'coins' },
  { portal: 'client', i18nKey: 'shell.group.setup', icon: 'settings' },
  { portal: 'client', i18nKey: 'shell.group.integration', icon: 'plug' },
  { portal: 'client', i18nKey: 'shell.group.automation', icon: 'zap' },
  { portal: 'client', i18nKey: 'shell.group.account', icon: 'user-circle' },
  // super admin portal (SRS modules.admin)
  { portal: 'admin', i18nKey: 'shell.group.overview', icon: 'grid' },
  { portal: 'admin', i18nKey: 'shell.group.administration', icon: 'building' },
  { portal: 'admin', i18nKey: 'shell.group.masterData', icon: 'database' },
  { portal: 'admin', i18nKey: 'shell.group.microbiology', icon: 'bug' },
  { portal: 'admin', i18nKey: 'shell.group.integrationLibrary', icon: 'library' },
  { portal: 'admin', i18nKey: 'shell.group.platform', icon: 'server' },
];

export const NAV_REGISTRY: readonly NavEntry[] = [
  { portal: 'client', group: 'shell.group.overview', path: '/account/dashboard', permission: 'dashboard.view', i18nKey: 'shell.nav.home', release: 'R1', icon: 'grid' },
  { portal: 'admin', group: 'shell.group.overview', path: '/admin', permission: null, i18nKey: 'shell.nav.adminHome', release: 'R1', icon: 'home' },
  { portal: 'client', group: 'shell.group.reception', path: '/tracker', permission: 'registration.view', i18nKey: 'shell.nav.labTracker', release: 'R1', icon: 'layers' },
  { portal: 'client', group: 'shell.group.reception', path: '/registration/new', permission: 'registration.create', i18nKey: 'shell.nav.registration', release: 'R1', icon: 'clipboard' },
  { portal: 'client', group: 'shell.group.reception', path: '/registration/visits', permission: 'registration.view', i18nKey: 'shell.nav.searchRegistrations', release: 'R1', icon: 'search' },
  { portal: 'client', group: 'shell.group.reception', path: '/patients', permission: 'patients.view', i18nKey: 'shell.nav.patients', release: 'R1', icon: 'user' },
  { portal: 'client', group: 'shell.group.reception', path: '/registration/accession', permission: 'registration.view', i18nKey: 'shell.nav.accessionDetails', release: 'R1', icon: 'clipboard' },
  { portal: 'client', group: 'shell.group.reception', path: '/registration/price-query', permission: 'registration.view', i18nKey: 'shell.nav.priceQuery', release: 'R1', icon: 'coins' },
  { portal: 'client', group: 'shell.group.laboratory', path: '/results/worklist', permission: 'results.view', i18nKey: 'shell.nav.worklist', release: 'R1', icon: 'clipboard' },
  { portal: 'client', group: 'shell.group.laboratory', path: '/results/by-accession', permission: 'results.view', i18nKey: 'shell.nav.resultsByAccession', release: 'R1', icon: 'flask' },
  { portal: 'client', group: 'shell.group.samples', path: '/samples/collection', permission: 'samples.view', i18nKey: 'shell.nav.sampleCollection', release: 'R2', icon: 'barcode' },
  { portal: 'client', group: 'shell.group.samples', path: '/samples/visits', permission: 'samples.view', i18nKey: 'shell.nav.samplesByVisit', release: 'R2', icon: 'layers' },
  { portal: 'client', group: 'shell.group.samples', path: '/samples/rejections', permission: 'samples.view', i18nKey: 'shell.nav.sampleRejections', release: 'R2', icon: 'alert' },
  { portal: 'client', group: 'shell.group.samples', path: '/samples/track', permission: 'samples.view', i18nKey: 'shell.nav.sampleTracking', release: 'R2', icon: 'search' },
  { portal: 'client', group: 'shell.group.finance', path: '/pricing/payers', permission: 'pricing.view', i18nKey: 'shell.nav.payers', release: 'R1', icon: 'building' },
  { portal: 'client', group: 'shell.group.finance', path: '/pricing/base-price-lists', permission: 'pricing.view', i18nKey: 'shell.nav.basePriceLists', release: 'R1', icon: 'layers' },
  { portal: 'client', group: 'shell.group.finance', path: '/pricing/service-prices', permission: 'pricing.view', i18nKey: 'shell.nav.servicePrices', release: 'R1', icon: 'coins' },
  { portal: 'client', group: 'shell.group.finance', path: '/pricing/contract-pricing', permission: 'pricing.view', i18nKey: 'shell.nav.contractPricing', release: 'R2', icon: 'coins' },
  { portal: 'client', group: 'shell.group.setup', path: '/organization/branches', permission: 'organization.view', i18nKey: 'shell.nav.branches', release: 'R1', icon: 'building' },
  { portal: 'client', group: 'shell.group.setup', path: '/organization/roles', permission: 'organization.view', i18nKey: 'shell.nav.roles', release: 'R1', icon: 'shield' },
  { portal: 'client', group: 'shell.group.setup', path: '/organization/users', permission: 'identity.view', i18nKey: 'shell.nav.users', release: 'R1', icon: 'user' },
  { portal: 'client', group: 'shell.group.setup', path: '/organization/parameters', permission: 'settings.view', i18nKey: 'shell.nav.parameters', release: 'R1', icon: 'settings' },
  { portal: 'client', group: 'shell.group.setup', path: '/billing/payment-methods', permission: 'billing.view', i18nKey: 'shell.nav.paymentMethods', release: 'R1', icon: 'coins' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/preparations', permission: 'catalogue.view', i18nKey: 'shell.nav.preparations', release: 'R1', icon: 'clipboard' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/comments', permission: 'catalogue.view', i18nKey: 'shell.nav.commentLibrary', release: 'R1', icon: 'inbox' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/doctors', permission: 'catalogue.view', i18nKey: 'shell.nav.doctors', release: 'R1', icon: 'user' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/microbiology', permission: 'catalogue.view', i18nKey: 'shell.nav.microbiology', release: 'R1', icon: 'bug' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/diagnoses', permission: 'catalogue.view', i18nKey: 'shell.nav.diagnoses', release: 'R2', icon: 'library' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/sending-labs', permission: 'catalogue.view', i18nKey: 'shell.nav.sendingLabs', release: 'R2', icon: 'building' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/shortcut-sets', permission: 'catalogue.view', i18nKey: 'shell.nav.shortcutSets', release: 'R1', icon: 'zap' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/test-rules', permission: 'catalogue.view', i18nKey: 'shell.nav.testRules', release: 'R1', icon: 'layers' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/reasons', permission: 'catalogue.view', i18nKey: 'shell.nav.reasons', release: 'R2', icon: 'filter' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/report-templates', permission: 'catalogue.view', i18nKey: 'shell.nav.reportTemplates', release: 'R2', icon: 'printer' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/organization/roles', permission: 'organization.view', i18nKey: 'shell.nav.adminRoles', release: 'R1', icon: 'shield' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/organization/users', permission: 'identity.view', i18nKey: 'shell.nav.adminUsers', release: 'R1', icon: 'user' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/preparations', permission: 'catalogue.view', i18nKey: 'shell.nav.preparations', release: 'R1', icon: 'clipboard' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/comments', permission: 'catalogue.view', i18nKey: 'shell.nav.commentLibrary', release: 'R1', icon: 'inbox' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/reasons', permission: 'catalogue.view', i18nKey: 'shell.nav.reasons', release: 'R1', icon: 'filter' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/diagnoses', permission: 'catalogue.view', i18nKey: 'shell.nav.diagnoses', release: 'R1', icon: 'library' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/report-templates', permission: 'catalogue.view', i18nKey: 'shell.nav.reportTemplates', release: 'R1', icon: 'printer' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/test-rules', permission: 'catalogue.view', i18nKey: 'shell.nav.testRules', release: 'R1', icon: 'layers' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/shortcut-sets', permission: 'catalogue.view', i18nKey: 'shell.nav.shortcutSets', release: 'R1', icon: 'zap' },
  { portal: 'admin', group: 'shell.group.microbiology', path: '/admin/catalogue/microbiology/organisms', permission: 'catalogue.view', i18nKey: 'shell.nav.organisms', release: 'R1', icon: 'bug' },
  { portal: 'admin', group: 'shell.group.microbiology', path: '/admin/catalogue/microbiology/antibiotics', permission: 'catalogue.view', i18nKey: 'shell.nav.antibiotics', release: 'R1', icon: 'bug' },
  { portal: 'admin', group: 'shell.group.microbiology', path: '/admin/catalogue/microbiology/drug-types', permission: 'catalogue.view', i18nKey: 'shell.nav.drugTypes', release: 'R1', icon: 'bug' },
  { portal: 'admin', group: 'shell.group.microbiology', path: '/admin/catalogue/microbiology/commercial-names', permission: 'catalogue.view', i18nKey: 'shell.nav.commercialNames', release: 'R1', icon: 'bug' },
  { portal: 'admin', group: 'shell.group.microbiology', path: '/admin/catalogue/microbiology/sensitivity-values', permission: 'catalogue.view', i18nKey: 'shell.nav.sensitivityValues', release: 'R1', icon: 'bug' },
  { portal: 'admin', group: 'shell.group.microbiology', path: '/admin/catalogue/microbiology/culture-specimens', permission: 'catalogue.view', i18nKey: 'shell.nav.cultureSpecimens', release: 'R1', icon: 'flask' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/clinical-groups', permission: 'catalogue.view', i18nKey: 'shell.nav.clinicalGroups', release: 'R1', icon: 'flask' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/clinical-groups', permission: 'catalogue.view', i18nKey: 'shell.nav.clinicalGroups', release: 'R1', icon: 'flask' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/services', permission: 'catalogue.view', i18nKey: 'shell.nav.services', release: 'R1', icon: 'clipboard' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/services', permission: 'catalogue.view', i18nKey: 'shell.nav.services', release: 'R1', icon: 'clipboard' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/tests', permission: 'catalogue.view', i18nKey: 'shell.nav.tests', release: 'R1', icon: 'microscope' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/tests', permission: 'catalogue.view', i18nKey: 'shell.nav.tests', release: 'R1', icon: 'microscope' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/service-sequence', permission: 'catalogue.view', i18nKey: 'shell.nav.serviceSequence', release: 'R1', icon: 'sort' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/service-sequence', permission: 'catalogue.view', i18nKey: 'shell.nav.serviceSequence', release: 'R1', icon: 'sort' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/sample-types', permission: 'catalogue.view', i18nKey: 'shell.nav.sampleTypes', release: 'R1', icon: 'flask' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/sample-types', permission: 'catalogue.view', i18nKey: 'shell.nav.sampleTypes', release: 'R1', icon: 'flask' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/units', permission: 'catalogue.view', i18nKey: 'shell.nav.units', release: 'R1', icon: 'layers' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/units', permission: 'catalogue.view', i18nKey: 'shell.nav.units', release: 'R1', icon: 'layers' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/patient-conditions', permission: 'catalogue.view', i18nKey: 'shell.nav.patientConditions', release: 'R2', icon: 'user' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/patient-conditions', permission: 'catalogue.view', i18nKey: 'shell.nav.patientConditions', release: 'R1', icon: 'user' },
  { portal: 'client', group: 'shell.group.setup', path: '/catalogue/additional-services', permission: 'catalogue.view', i18nKey: 'shell.nav.additionalServices', release: 'R2', icon: 'plus' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/catalogue/additional-services', permission: 'catalogue.view', i18nKey: 'shell.nav.additionalServices', release: 'R1', icon: 'plus' },
  { portal: 'client', group: 'shell.group.finance', path: '/billing/payments', permission: 'billing.view', i18nKey: 'shell.nav.payments', release: 'R1', icon: 'coins' },
  { portal: 'client', group: 'shell.group.finance', path: '/billing/refunds', permission: 'billing.view', i18nKey: 'shell.nav.refunds', release: 'R1', menu: 'R2', icon: 'arrow-down' },
  { portal: 'client', group: 'shell.group.finance', path: '/billing/batch-payment', permission: 'billing.view', i18nKey: 'shell.nav.batchPayment', release: 'R2', icon: 'layers' },
  { portal: 'client', group: 'shell.group.finance', path: '/reports/work-amount', permission: 'reporting.view', i18nKey: 'shell.nav.workAmountReport', release: 'R1', icon: 'trending-up' },
  { portal: 'client', group: 'shell.group.finance', path: '/billing/cash-closing', permission: 'billing.view', i18nKey: 'shell.nav.cashClosing', release: 'R2', icon: 'clipboard' },
  { portal: 'client', group: 'shell.group.account', path: '/account/profile', permission: null, i18nKey: 'shell.nav.profile', release: 'R1', icon: 'user-circle' },
  { portal: 'client', group: 'shell.group.account', path: '/account/wallet', permission: 'wallet.view', i18nKey: 'shell.nav.wallet', release: 'R2', icon: 'vault' },
  { portal: 'client', group: 'shell.group.account', path: '/account/support', permission: 'support.view', i18nKey: 'shell.nav.support', release: 'R2', icon: 'inbox' },
  { portal: 'client', group: 'shell.group.account', path: '/account/audit', permission: 'audit.view', i18nKey: 'shell.nav.auditLog', release: 'R2', icon: 'shield' },
  { portal: 'admin', group: 'shell.group.overview', path: '/admin/dashboard', permission: 'platform.view', i18nKey: 'shell.nav.platformDashboard', release: 'R1', icon: 'grid' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/clients', permission: 'tenancy.view', i18nKey: 'shell.nav.clients', release: 'R1', icon: 'building' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/subscriptions', permission: 'tenancy.view', i18nKey: 'shell.nav.subscriptionsWallets', release: 'R1', icon: 'coins' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/sessions', permission: 'platform.view', i18nKey: 'shell.nav.sessions', release: 'R1', icon: 'shield' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/login-logs', permission: 'platform.view', i18nKey: 'shell.nav.loginLogs', release: 'R1', icon: 'shield' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/audit', permission: 'audit.view', i18nKey: 'shell.nav.auditLog', release: 'R1', icon: 'shield' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/countries', permission: 'tenancy.view', i18nKey: 'shell.nav.countries', release: 'R1', icon: 'globe' },
  { portal: 'admin', group: 'shell.group.masterData', path: '/admin/governorates', permission: 'tenancy.view', i18nKey: 'shell.nav.governorates', release: 'R1', icon: 'globe' },
  { portal: 'admin', group: 'shell.group.integrationLibrary', path: '/admin/library/analyser-models', permission: 'integration.view', i18nKey: 'shell.nav.analyserModels', release: 'R2', icon: 'microscope' },
  { portal: 'admin', group: 'shell.group.integrationLibrary', path: '/admin/library/pcr-kits', permission: 'integration.view', i18nKey: 'shell.nav.pcrKits', release: 'R2', icon: 'flask' },
  { portal: 'admin', group: 'shell.group.integrationLibrary', path: '/admin/library/code-systems', permission: 'integration.view', i18nKey: 'shell.nav.codeSystems', release: 'R2', icon: 'database' },
  { portal: 'admin', group: 'shell.group.integrationLibrary', path: '/admin/library/rule-templates', permission: 'integration.view', i18nKey: 'shell.nav.ruleTemplates', release: 'R2', icon: 'zap' },
  { portal: 'admin', group: 'shell.group.platform', path: '/admin/parameters', permission: 'settings.view', i18nKey: 'shell.nav.platformParameters', release: 'R1', icon: 'settings' },
  { portal: 'admin', group: 'shell.group.platform', path: '/admin/notification-templates', permission: 'notifications.view', i18nKey: 'shell.nav.notificationTemplates', release: 'R1', icon: 'inbox' },
  { portal: 'admin', group: 'shell.group.platform', path: '/admin/support', permission: 'support.manage', i18nKey: 'shell.nav.supportDesk', release: 'R2', icon: 'inbox' },
  { portal: 'admin', group: 'shell.group.platform', path: '/admin/profile', permission: null, i18nKey: 'shell.nav.profile', release: 'R1', icon: 'user-circle' },
  { portal: 'client', group: 'shell.group.integration', path: '/integration/analysers', permission: 'integration.view', i18nKey: 'shell.nav.analysers', release: 'R2', icon: 'plug' },
  { portal: 'client', group: 'shell.group.integration', path: '/integration/analyser-tests', permission: 'integration.view', i18nKey: 'shell.nav.analyserTests', release: 'R2', icon: 'layers' },
  { portal: 'client', group: 'shell.group.integration', path: '/integration/analyser-samples', permission: 'integration.view', i18nKey: 'shell.nav.analyserSamples', release: 'R2', icon: 'barcode' },
  { portal: 'client', group: 'shell.group.integration', path: '/integration/site-agents', permission: 'integration.view', i18nKey: 'shell.nav.siteAgents', release: 'R2', icon: 'server' },
  { portal: 'client', group: 'shell.group.integration', path: '/integration/pcr-kits', permission: 'integration.view', i18nKey: 'shell.nav.tenantPcrKits', release: 'R2', icon: 'flask' },
];
