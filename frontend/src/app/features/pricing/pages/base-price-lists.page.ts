import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { type BasePriceListItem, type CopyBasePriceListRequest, type CreateBasePriceListRequest, PRICING_PERMISSIONS } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { PricingApi } from '../data/pricing.api';
import { CopyListDialogComponent, type CopySource } from '../ui/copy-list-dialog.component';

/**
 * Base price lists (SRS CP-33): the register on the shared master-data page (code, bilingual name, year, activation;
 * lists are created inactive, intake Q4). A new list opens its price grid; the "Copy" row action creates next year's
 * list with × factor or + amount (WF-10 step 1). Prices are edited on the list's own page.
 */
@Component({
  selector: 'app-base-price-lists-page',
  imports: [MasterDataPageComponent, CellTemplateDirective, BadgeComponent, RouterLink, CopyListDialogComponent],
  template: `
    <app-master-data-page [config]="config">
      <ng-template appCell="code" [appCellOf]="config" let-row>
        <a class="link mono" [routerLink]="['/pricing/base-price-lists', row.id]">{{ row.code }}</a>
      </ng-template>
      <ng-template appCell="lines" [appCellOf]="config" let-row>
        <span class="mono">{{ row.activeLineCount }} / {{ row.lineCount }}</span>
      </ng-template>
      <ng-template appCell="derived" [appCellOf]="config" let-row>
        @if (row.derivedContractCount > 0) {
          <app-badge tone="violet">{{ row.derivedContractCount }}</app-badge>
        } @else {
          <span class="muted">—</span>
        }
      </ng-template>
    </app-master-data-page>

    <app-copy-list-dialog [(open)]="copyOpen" [source]="copySource()" [pending]="copyList.pending()" [problem]="copyList.problem()" (confirmCopy)="runCopy($event)" />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BasePriceListsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PricingApi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly register = viewChild.required(MasterDataPageComponent);

  protected readonly copyOpen = signal(false);
  protected readonly copySource = signal<(CopySource & { readonly id: string }) | null>(null);

  protected readonly copyList = mutation((input: { sourceId: string; request: CopyBasePriceListRequest }) => this.api.copyBasePriceList(input.sourceId, input.request));

  protected readonly config: MasterDataConfig<BasePriceListItem, CreateBasePriceListRequest> = {
    i18nPrefix: 'pricing.lists',
    columns: [
      { key: 'code', header: 'pricing.lists.column.code', mono: true, sortable: true, width: '130px' },
      { key: 'name', header: 'pricing.lists.column.name', sortable: true, value: (list) => localizedPick({ en: list.nameEn, ar: list.nameAr }, this.i18n.lang()) },
      { key: 'year', header: 'pricing.lists.column.year', sortable: true, align: 'end', width: '90px' },
      { key: 'lines', header: 'pricing.lists.column.lines', align: 'end', width: '110px', value: (list) => list.activeLineCount },
      { key: 'derived', header: 'pricing.lists.column.derived', align: 'center', width: '110px', value: (list) => list.derivedContractCount },
      { key: 'copiedFromCode', header: 'pricing.lists.column.copiedFrom', mono: true, secondary: true, width: '120px' },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 16, hint: 'pricing.lists.hint.code' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'year', kind: 'number', required: true, min: 2000, max: 2100, step: 1, defaultValue: new Date().getFullYear() },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.listBasePriceLists({ search: filter.search, year: null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: async (dto) => {
        const created = await this.api.createBasePriceList(dto);
        void this.router.navigate(['/pricing/base-price-lists', created.id]);
        return created;
      },
      update: (id, dto) => this.api.updateBasePriceList(id, { nameEn: dto.nameEn, nameAr: dto.nameAr, year: dto.year }),
      setActive: (id, active) => this.api.setBasePriceListActive(id, active),
    },
    toDto: (value) => toListRequest(value),
    permissions: { create: PRICING_PERMISSIONS.create, update: PRICING_PERMISSIONS.edit, activate: PRICING_PERMISSIONS.manage },
    rowActions: [
      { id: 'open', label: 'pricing.lists.action.open', icon: 'grid', run: (list) => void this.router.navigate(['/pricing/base-price-lists', list.id]) },
      { id: 'copy', label: 'pricing.lists.action.copy', icon: 'clipboard', permission: PRICING_PERMISSIONS.create, run: (list) => this.openCopy(list) },
    ],
  };

  constructor() {
    void this.i18n.load('pricing');
  }

  protected async runCopy(request: CopyBasePriceListRequest): Promise<void> {
    const source = this.copySource();
    if (source === null) {
      return;
    }

    const created = await this.copyList.execute({ sourceId: source.id, request });
    if (created !== null) {
      this.copyOpen.set(false);
      this.toast.success(this.i18n.tf('pricing.copy.done', { code: request.code.toUpperCase() }));
      this.register().reload();
      void this.router.navigate(['/pricing/base-price-lists', created.id]);
    }
  }

  private openCopy(list: BasePriceListItem): void {
    this.copyList.reset();
    this.copySource.set({ id: list.id, code: list.code, nameEn: list.nameEn, nameAr: list.nameAr, year: list.year });
    this.copyOpen.set(true);
  }
}

/** Form value of the drawer → the request (the year arrives as a number control). */
export function toListRequest(value: FormValue): CreateBasePriceListRequest {
  const name = (value['name'] ?? {}) as { en?: string; ar?: string };
  const year = Number(value['year']);
  return {
    code: typeof value['code'] === 'string' ? value['code'].trim() : '',
    nameEn: (name.en ?? '').trim(),
    nameAr: (name.ar ?? '').trim(),
    year: Number.isFinite(year) ? Math.trunc(year) : 0,
  };
}
