import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';

import { BILLING_PERMISSIONS, PAYMENT_METHODS, type PaymentMethod, type PaymentMethodListItem, type PaymentMethodRequest } from '../../../core/api/billing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { sortParams } from '../../../shared/forms/paging';
import { CanDirective } from '../../../shared/ui/can.directive';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, MasterDataFilter, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { BillingApi } from '../data/billing.api';
import { saveFile } from '../ui/save-file';

const isKind = (value: unknown): value is PaymentMethod => typeof value === 'string' && (PAYMENT_METHODS as readonly string[]).includes(value);

/**
 * Payment methods set-up list (Sky LIS CP-30b, change request 2026-10-19 P-C10) on the generic master-data page: code
 * (immutable), bilingual name, kind (the money semantics: cash lives in the cashier's drawer), whether a transaction
 * reference is required, the order of the pick lists, and activation (deactivate instead of delete: posted movements
 * keep the code). An empty list asks the server for the four defaults (CASH, CARD, POS, TRANSFER) once, when the user
 * may manage billing; the server keeps at least one active cash method (BIL-052).
 */
@Component({
  selector: 'app-payment-methods-page',
  imports: [MasterDataPageComponent, CanDirective],
  template: `
    <app-master-data-page [config]="config">
      <button pageActions ngProjectAs="[pageActions]" type="button" class="btn" *appCan="permissions.manage" (click)="addDefaults()">{{ i18n.t('billing.paymentMethods.defaults') }}</button>
      <button pageActions ngProjectAs="[pageActions]" type="button" class="btn" *appCan="permissions.export" (click)="exportCsv()">{{ i18n.t('billing.exportCsv') }}</button>
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentMethodsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(BillingApi);
  private readonly access = inject(PermissionsService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);
  private readonly page = viewChild(MasterDataPageComponent);

  protected readonly permissions = BILLING_PERMISSIONS;

  /** The filter of the list on screen: the export writes exactly these rows. */
  private filter: MasterDataFilter = { search: '', active: true };

  /** The defaults are asked for once per page life, only when the unfiltered list came back empty. */
  private defaultsTried = false;

  protected readonly config: MasterDataConfig<PaymentMethodListItem, PaymentMethodRequest> = {
    i18nPrefix: 'billing.paymentMethods',
    columns: [
      { key: 'code', header: 'billing.paymentMethods.column.code', mono: true, sortable: true, width: '140px' },
      { key: 'name', header: 'billing.paymentMethods.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (item) => this.name(item) },
      { key: 'kind', header: 'billing.paymentMethods.column.kind', sortable: true, value: (item) => this.kindLabel(item.kind) },
      { key: 'requiresReference', header: 'billing.paymentMethods.column.requiresReference', value: (item) => this.yesNo(item.requiresReference) },
      { key: 'sequence', header: 'billing.paymentMethods.column.sequence', sortable: true, secondary: true, width: '90px' },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 20, pattern: /^[A-Za-z0-9_-]+$/, patternMessage: 'billing.paymentMethods.error.BIL-051', hint: 'billing.paymentMethods.hint.code', immutable: true },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 100 },
      { name: 'kind', kind: 'select', required: true, options: () => this.kindOptions(), hint: 'billing.paymentMethods.hint.kind', defaultValue: 'Card' },
      { name: 'sequence', kind: 'number', min: 0, max: 9999, step: 1, hint: 'billing.paymentMethods.hint.sequence' },
      { name: 'requiresReference', kind: 'boolean', section: 'billing.paymentMethods.section.rules', hint: 'billing.paymentMethods.hint.requiresReference', defaultValue: true, full: true },
    ],
    adapter: {
      list: async (filter, page, signal) => {
        this.filter = filter;
        const query = { ...sortParams(filter.sort), search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize };
        const result = await this.api.listPaymentMethods(query, signal);
        if (result.totalCount === 0 && filter.search === '' && !this.defaultsTried && this.access.can(BILLING_PERMISSIONS.manage)) {
          // a laboratory without methods yet (older than the defaults): seed them once, then show them
          this.defaultsTried = true;
          const added = await this.api.ensureDefaultPaymentMethods();
          if (added.created > 0) {
            return this.api.listPaymentMethods(query, signal);
          }
        }

        return result;
      },
      create: (dto) => this.api.createPaymentMethod(dto),
      update: (id, dto) => this.api.updatePaymentMethod(id, { nameEn: dto.nameEn, nameAr: dto.nameAr, kind: dto.kind, requiresReference: dto.requiresReference, sequence: dto.sequence ?? 0 }),
      setActive: (id, active) => this.api.setPaymentMethodActive(id, active),
    },
    toDto: (value, item) => this.toDto(value, item),
    permissions: { create: BILLING_PERMISSIONS.manage, update: BILLING_PERMISSIONS.manage, activate: BILLING_PERMISSIONS.manage },
    // the server searches the code and both names and sorts the whole list before paging (BillingSortKeys.PaymentMethods)
    searchMode: 'server',
    sortMode: 'server',
  };

  /** "Add the default methods" (idempotent on the server): the ones the laboratory lacks appear in the list. */
  protected async addDefaults(): Promise<void> {
    this.defaultsTried = true;
    const added = await this.api.ensureDefaultPaymentMethods();
    this.toast.success(added.created > 0 ? this.i18n.tf('billing.paymentMethods.defaultsAdded', { count: added.created }) : this.i18n.t('billing.paymentMethods.defaultsNone'));
    this.page()?.reload();
  }

  /** CSV of the methods under the current status filter and search (the server writes it, `billing.export`). */
  protected async exportCsv(): Promise<void> {
    const file = await this.api.exportPaymentMethods({ search: this.filter.search, isActive: this.filter.active });
    saveFile(this.document, file);
    this.toast.success(this.i18n.t('billing.exported'));
  }

  private toDto(value: FormValue, item: PaymentMethodListItem | null): PaymentMethodRequest {
    const name = value['name'] as { readonly en?: string; readonly ar?: string } | undefined;
    const sequence = value['sequence'];
    return {
      code: typeof value['code'] === 'string' ? value['code'].trim().toUpperCase() : (item?.code ?? ''),
      nameEn: (name?.en ?? (value['nameEn'] as string | undefined) ?? '').trim(),
      nameAr: (name?.ar ?? (value['nameAr'] as string | undefined) ?? '').trim(),
      kind: isKind(value['kind']) ? value['kind'] : (item?.kind ?? 'Card'),
      requiresReference: value['requiresReference'] === true,
      sequence: typeof sequence === 'number' && Number.isFinite(sequence) ? sequence : (item?.sequence ?? null),
    };
  }

  private name(item: PaymentMethodListItem): string {
    return localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang());
  }

  private kindLabel(kind: PaymentMethod): string {
    return this.i18n.t(`billing.paymentMethods.kind.${kind}`);
  }

  private yesNo(value: boolean): string {
    return this.i18n.t(value ? 'billing.paymentMethods.yes' : 'billing.paymentMethods.no');
  }

  private kindOptions(): Promise<readonly SelectOption[]> {
    return Promise.resolve(PAYMENT_METHODS.map((kind) => ({ value: kind, label: this.kindLabel(kind) })));
  }
}
