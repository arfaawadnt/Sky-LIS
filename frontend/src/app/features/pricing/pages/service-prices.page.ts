import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { PRICE_LIMITS, PRICING_PERMISSIONS, type ServiceBasePriceItem, type ServiceContractPriceItem, type ServiceLabel } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { inReleaseScope } from '../../../core/release';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { formatAmount, hasScale } from '../data/money';
import { ServiceCatalogApi } from '../data/service-catalog.api';
import { ServicePricesFacade } from '../state/service-prices.facade';
import { ServicePickerComponent } from '../ui/service-picker.component';

/**
 * Service prices (CP-33b, CR 2026-10-19 C22): pick a catalogue service and see its line in every base price list (editable
 * inline with `pricing.edit`, one-line save) and its derived price in every contract tier; each list links to its editor.
 * The chosen service travels in the `serviceId` query parameter so the page can be linked to and refreshed.
 */
@Component({
  selector: 'app-service-prices-page',
  imports: [RouterLink, PageHeaderComponent, SectionCardComponent, DataTableComponent, CellTemplateDirective, BadgeComponent, IconComponent, EmptyStateComponent, ServicePickerComponent],
  templateUrl: './service-prices.page.html',
  styleUrl: './service-prices.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServicePricesPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ServicePricesFacade);
  private readonly catalog = inject(ServiceCatalogApi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly permissionsService = inject(PermissionsService);

  /** Query parameter (`withComponentInputBinding`): the service to show. */
  readonly serviceId = input<string>();

  protected readonly permissions = PRICING_PERMISSIONS;

  /** Contract pricing (CP-34) is Release 2: its links show only when that page is in scope (UAT R1-03). */
  protected readonly contractPricingOffered = inReleaseScope('R2');

  protected readonly canEdit = computed(() => this.permissionsService.can(PRICING_PERMISSIONS.edit));

  /** The unsaved price typed in a base list row (list id → price), cleared when the row is saved or the service changes. */
  protected readonly drafts = signal<ReadonlyMap<string, number>>(new Map());

  /** The list whose row is being saved. */
  protected readonly savingListId = signal<string | null>(null);

  protected readonly searchService = (term: string): Promise<readonly ServiceLabel[]> => this.catalog.search(term);

  protected readonly baseColumns: readonly TableColumn<ServiceBasePriceItem>[] = [
    { key: 'listCode', header: 'pricing.servicePrices.column.list', mono: true, width: '140px' },
    { key: 'listName', header: 'pricing.servicePrices.column.listName', value: (row) => localizedPick({ en: row.listNameEn, ar: row.listNameAr }, this.i18n.lang()) },
    { key: 'year', header: 'pricing.lists.column.year', width: '80px', align: 'center' },
    { key: 'status', header: 'pricing.servicePrices.column.status', width: '200px' },
    { key: 'price', header: 'pricing.grid.price', align: 'end', width: '220px' },
  ];

  protected readonly contractColumns: readonly TableColumn<ServiceContractPriceItem>[] = [
    { key: 'payer', header: 'pricing.contract.payer', value: (row) => localizedPick({ en: row.payerNameEn, ar: row.payerNameAr }, this.i18n.lang()) },
    { key: 'tier', header: 'pricing.contract.tier', value: (row) => localizedPick({ en: row.tierNameEn, ar: row.tierNameAr }, this.i18n.lang()) },
    { key: 'listPrice', header: 'pricing.grid.listPrice', align: 'end', width: '120px', value: (row) => this.amount(row.listPrice) },
    { key: 'patientPrice', header: 'pricing.grid.patientShare', align: 'end', width: '120px', value: (row) => this.amount(row.patientPrice) },
    { key: 'payerPrice', header: 'pricing.grid.payerShare', align: 'end', width: '120px', value: (row) => this.amount(row.payerPrice) },
    { key: 'flags', header: 'pricing.servicePrices.column.flags', width: '220px' },
  ];

  constructor() {
    void this.i18n.load('pricing');
    effect(() => {
      const id = this.serviceId() ?? null;
      untracked(() => {
        this.drafts.set(new Map());
        this.facade.load(id === '' ? null : id);
      });
    });
  }

  protected onPicked(service: ServiceLabel): void {
    void this.router.navigate([], { queryParams: { serviceId: service.id }, queryParamsHandling: 'merge' });
  }

  protected clear(): void {
    void this.router.navigate([], { queryParams: { serviceId: null }, queryParamsHandling: 'merge' });
  }

  protected draftPrice(row: ServiceBasePriceItem): number {
    return this.drafts().get(row.listId) ?? row.price;
  }

  protected isDirty(row: ServiceBasePriceItem): boolean {
    const draft = this.drafts().get(row.listId);
    return draft !== undefined && draft !== row.price;
  }

  protected isInvalid(row: ServiceBasePriceItem): boolean {
    const price = this.draftPrice(row);
    return !(price >= 0 && price <= PRICE_LIMITS.maxAmount && hasScale(price, 2));
  }

  protected onPrice(row: ServiceBasePriceItem, event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.drafts.update((current) => new Map(current).set(row.listId, Number.isNaN(value) ? -1 : value));
  }

  protected onPriceKeydown(row: ServiceBasePriceItem, event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      void this.savePrice(row);
    } else if (event.key === 'Escape') {
      this.discard(row);
    }
  }

  protected discard(row: ServiceBasePriceItem): void {
    this.drafts.update((current) => {
      const next = new Map(current);
      next.delete(row.listId);
      return next;
    });
  }

  protected async savePrice(row: ServiceBasePriceItem): Promise<void> {
    const view = this.facade.current();
    if (view === null || !this.isDirty(row) || this.facade.setBasePrice.pending()) {
      return;
    }

    if (this.isInvalid(row)) {
      this.toast.error(this.i18n.t('pricing.editor.fixInvalid'));
      return;
    }

    this.savingListId.set(row.listId);
    try {
      const saved = await this.facade.setBasePrice.execute({ listId: row.listId, serviceId: view.serviceId, price: this.draftPrice(row), isActive: row.lineActive });
      if (saved !== null) {
        this.discard(row);
        this.toast.success(this.i18n.tf('pricing.servicePrices.priceSaved', { list: row.listCode }), { title: this.i18n.t('pricing.editor.applyReminder') });
      }
    } finally {
      this.savingListId.set(null);
    }
  }

  protected amount(value: number): string {
    return formatAmount(value, this.i18n.lang());
  }

  protected typeLabel(type: string): string {
    const key = `pricing.servicePrices.type.${type}`;
    return this.i18n.has(key) ? this.i18n.t(key) : type;
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'pricing', { code });
  }
}
