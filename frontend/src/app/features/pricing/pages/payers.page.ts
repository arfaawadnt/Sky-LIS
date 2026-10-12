import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { PAYER_TYPES, PRICING_PERMISSIONS, type ContractTierItem, type PayerListItem, type PayerRequest, type PayerType } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type Release, inReleaseScope } from '../../../core/release';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, MasterDataFilter, RowAction } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { allPages, csvFileName, downloadCsv, toCsv } from '../data/csv';
import { PricingApi } from '../data/pricing.api';
import { PayersFacade } from '../state/payers.facade';

/**
 * Payers & contracts (SRS CP-32). The register is the shared master-data page (name, search key, type, web portal,
 * activation); the tiers of a payer live in a second drawer opened by the "Contract tiers" row action, and the
 * "Contract pricing" row action opens CP-34 for the payer's first tier. The tenant's cash payer is provisioned when
 * the page opens (intake Q1) and cannot be deactivated (PRC-020).
 */

/** Contract pricing (CP-34) belongs to Release 2: its row and tier actions show only when the release is in scope (UAT R1-03). */
const CONTRACT_PRICING_RELEASE: Release = 'R2';
@Component({
  selector: 'app-payers-page',
  imports: [
    ReactiveFormsModule,
    MasterDataPageComponent,
    CellTemplateDirective,
    BadgeComponent,
    DrawerComponent,
    LocalizedTextInputComponent,
    SaveShortcutDirective,
    IconComponent,
    CanDirective,
    EmptyStateComponent,
  ],
  templateUrl: './payers.page.html',
  styleUrl: './payers.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PayersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PayersFacade);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly register = viewChild.required(MasterDataPageComponent);
  private readonly api = inject(PricingApi);

  protected readonly permissions = PRICING_PERMISSIONS;

  /** Whether the Contract pricing actions (CP-34) are offered: only when contract pricing is in the release scope (UAT R1-03). */
  protected readonly contractPricingOffered = inReleaseScope(CONTRACT_PRICING_RELEASE);

  private readonly rowActions: readonly RowAction<PayerListItem>[] = [
    { id: 'tiers', label: 'pricing.payers.action.tiers', icon: 'layers', run: (payer) => this.facade.openTiers(payer.id) },
    { id: 'pricing', label: 'pricing.payers.action.contractPricing', icon: 'coins', run: (payer) => this.openPricing(payer) },
  ];

  /** The filter of the list on screen: the export writes exactly these rows (UAT B-10). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly exporting = signal(false);

  protected readonly config: MasterDataConfig<PayerListItem, PayerRequest> = {
    i18nPrefix: 'pricing.payers',
    // B-03: the server sorts the whole register before paging (PricingSortKeys.Payers); the type sorts by its stored name
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'pricing.payers.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (payer) => localizedPick({ en: payer.nameEn, ar: payer.nameAr }, this.i18n.lang()) },
      { key: 'searchKey', header: 'pricing.payers.column.searchKey', mono: true, sortable: true, width: '130px' },
      { key: 'type', header: 'pricing.payers.column.type', sortable: true, width: '120px', value: (payer) => this.typeLabel(payer.type) },
      { key: 'tiers', header: 'pricing.payers.column.tiers', value: (payer) => payer.tiers.map((tier) => this.tierName(tier)).join(', ') },
      { key: 'webPortal', header: 'pricing.payers.column.webPortal', secondary: true, value: (payer) => (payer.webPortalEnabled ? payer.webUsername : null) },
    ],
    fields: [
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'searchKey', kind: 'code', required: true, maxLength: 32, immutable: false, hint: 'pricing.payers.hint.searchKey' },
      {
        name: 'type',
        kind: 'select',
        required: true,
        defaultValue: 'Credit',
        options: PAYER_TYPES.map((type) => ({ value: type, label: this.typeText(type, 'en'), labelAr: this.typeText(type, 'ar') })),
      },
      { name: 'webPortalEnabled', kind: 'boolean', defaultValue: false },
      { name: 'webUsername', kind: 'text', maxLength: 64, hint: 'pricing.payers.hint.webUsername' },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.facade.list(filter, page, signal);
      },
      create: (dto) => this.facade.create(dto),
      update: (id, dto) => this.facade.update(id, dto),
      setActive: (id, active) => this.facade.setActive(id, active),
    },
    toDto: (value) => toPayerRequest(value),
    permissions: { create: PRICING_PERMISSIONS.create, update: PRICING_PERMISSIONS.edit, activate: PRICING_PERMISSIONS.manage },
    rowActions: this.rowActions.filter((action) => action.id !== 'pricing' || this.contractPricingOffered),
  };

  // ---- tiers drawer ----

  protected readonly tierForm = localizedTextGroup({}, { required: true, maxLength: 200 });

  /** The tier being renamed in the drawer form, or null while the form adds a tier. */
  protected readonly editingTierId = signal<string | null>(null);

  protected readonly tiersOpen = computed(() => this.facade.tiersPayerId() !== null);

  protected readonly tiersTitle = computed(() => {
    const payer = this.facade.tiersPayer.hasValue() ? this.facade.tiersPayer.value() : null;
    return payer ? this.i18n.tf('pricing.tiers.title', { payer: localizedPick({ en: payer.nameEn, ar: payer.nameAr }, this.i18n.lang()) }) : this.i18n.t('pricing.tiers.titleLoading');
  });

  /** The refusal of the last tier action only: every action clears the others, so a stale "already exists" never stays (UAT B-06). */
  protected readonly tierProblem = computed(() => this.facade.addTier.problem() ?? this.facade.renameTier.problem() ?? this.facade.setTierActive.problem());

  constructor() {
    void this.i18n.load('pricing');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    // a tier change alters the register's "tiers" column
    effect(() => {
      if (this.facade.changes() > 0) {
        untracked(() => this.register().reload());
      }
    });
  }

  /** CSV of the payers of the current search and status filter, in the grid's formats (UAT B-10). */
  protected async exportCsv(): Promise<void> {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    try {
      const filter = this.filter;
      const rows = await allPages((page, pageSize) => this.api.listPayers({ search: filter.search, type: null, isActive: filter.active, page, pageSize }));
      const t = (key: string): string => this.i18n.t(key);
      const yesNo = (value: boolean): string => t(value ? 'shared.yes' : 'shared.no');
      const header = [
        t('pricing.csv.nameEn'),
        t('pricing.csv.nameAr'),
        t('pricing.payers.column.searchKey'),
        t('pricing.payers.column.type'),
        t('pricing.payers.column.tiers'),
        t('pricing.csv.webPortal'),
        t('pricing.csv.webUsername'),
        t('pricing.csv.default'),
        t('pricing.csv.active'),
      ];
      const lines = rows.map((payer) => [
        payer.nameEn,
        payer.nameAr,
        payer.searchKey,
        this.typeLabel(payer.type),
        payer.tiers.map((tier) => this.tierName(tier) + (tier.isActive ? '' : ` (${t('pricing.csv.inactive')})`)).join(' | '),
        yesNo(payer.webPortalEnabled),
        payer.webUsername,
        yesNo(payer.isDefault),
        yesNo(payer.isActive),
      ]);
      downloadCsv(csvFileName('payers', new Date()), toCsv(header, lines));
      this.toast.success(t('pricing.exported'));
    } catch {
      this.toast.error(this.i18n.t('pricing.exportError'));
    } finally {
      this.exporting.set(false);
    }
  }

  protected typeLabel(type: PayerType): string {
    return this.i18n.t(`pricing.payerType.${type}`);
  }

  protected tierName(tier: ContractTierItem): string {
    return localizedPick({ en: tier.nameEn, ar: tier.nameAr }, this.i18n.lang());
  }

  protected onTiersOpenChange(open: boolean): void {
    if (!open) {
      this.closeTiers();
    }
  }

  protected closeTiers(): void {
    this.cancelTierEdit();
    this.facade.closeTiers();
  }

  protected editTier(tier: ContractTierItem): void {
    this.clearTierProblems();
    this.editingTierId.set(tier.id);
    this.tierForm.setValue({ en: tier.nameEn, ar: tier.nameAr });
    this.tierForm.markAsPristine();
  }

  protected isEditing(tier: ContractTierItem): boolean {
    return this.editingTierId() === tier.id;
  }

  protected cancelTierEdit(): void {
    this.clearTierProblems();
    this.editingTierId.set(null);
    this.tierForm.reset({ en: '', ar: '' });
  }

  protected async saveTier(): Promise<void> {
    const payerId = this.facade.tiersPayerId();
    if (payerId === null || !markAllTouched(this.tierForm)) {
      return;
    }

    const { en, ar } = this.tierForm.getRawValue();
    const request = { nameEn: en.trim(), nameAr: ar.trim() };
    const tierId = this.editingTierId();
    this.clearTierProblems();
    const done = tierId === null ? await this.facade.addTier.execute({ payerId, request }) : await this.facade.renameTier.execute({ payerId, tierId, request });
    if (done !== null) {
      this.toast.success(this.i18n.t(tierId === null ? 'pricing.tiers.added' : 'pricing.tiers.renamed'));
      this.cancelTierEdit();
    }
  }

  protected async toggleTier(tier: ContractTierItem): Promise<void> {
    const payerId = this.facade.tiersPayerId();
    if (payerId === null) {
      return;
    }

    const name = this.tierName(tier);
    if (tier.isActive) {
      const confirmed = await this.confirm.confirm({
        title: this.i18n.t('pricing.tiers.deactivate'),
        message: this.i18n.tf('pricing.tiers.confirmDeactivate', { tier: name }),
        danger: true,
      });
      if (!confirmed) {
        return;
      }
    }

    this.clearTierProblems();
    if ((await this.facade.setTierActive.execute({ payerId, tierId: tier.id, active: !tier.isActive })) !== null) {
      this.toast.success(this.i18n.t(tier.isActive ? 'pricing.tiers.deactivated' : 'pricing.tiers.activated'));
    }
  }

  private clearTierProblems(): void {
    this.facade.addTier.reset();
    this.facade.renameTier.reset();
    this.facade.setTierActive.reset();
  }

  protected openTierPricing(tier: ContractTierItem): void {
    this.closeTiers();
    void this.router.navigate(['/pricing/contract-pricing', tier.id]);
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'pricing', { code });
  }

  private openPricing(payer: PayerListItem): void {
    const tier = payer.tiers.find((candidate) => candidate.isActive) ?? payer.tiers[0];
    if (!tier) {
      this.toast.info(this.i18n.t('pricing.payers.noTiers'));
      this.facade.openTiers(payer.id);
      return;
    }

    void this.router.navigate(['/pricing/contract-pricing', tier.id]);
  }

  private typeText(type: PayerType, lang: 'en' | 'ar'): string {
    return PAYER_TYPE_TEXT[type][lang];
  }
}

/** Option labels of the payer type select (the master-data page takes English and Arabic texts directly). */
const PAYER_TYPE_TEXT: Readonly<Record<PayerType, { en: string; ar: string }>> = {
  Cash: { en: 'Cash', ar: 'نقدي' },
  Credit: { en: 'Credit', ar: 'آجل' },
  Insurance: { en: 'Insurance', ar: 'تأمين' },
  Lab: { en: 'Laboratory', ar: 'معمل' },
};

/** Form value of the drawer → the request (the username is dropped while the portal is off). */
export function toPayerRequest(value: FormValue): PayerRequest {
  const name = (value['name'] ?? {}) as { en?: string; ar?: string };
  const portal = value['webPortalEnabled'] === true;
  const username = typeof value['webUsername'] === 'string' ? value['webUsername'].trim() : '';
  const type = PAYER_TYPES.find((candidate) => candidate === value['type']) ?? 'Credit';
  return {
    nameEn: (name.en ?? '').trim(),
    nameAr: (name.ar ?? '').trim(),
    searchKey: typeof value['searchKey'] === 'string' ? value['searchKey'].trim() : '',
    type,
    webPortalEnabled: portal,
    webUsername: portal && username !== '' ? username : null,
  };
}
