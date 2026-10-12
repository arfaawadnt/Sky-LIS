import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { type BasePriceListItem, type CopyBasePriceListRequest, type CreateBasePriceListRequest, PRICING_PERMISSIONS } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { localizedPick } from '../../../shared/forms/localized-text';
import { sortParams } from '../../../shared/forms/paging';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { allPages, csvFileName, downloadCsv, toCsv } from '../data/csv';
import { PricingApi, takenCodes } from '../data/pricing.api';
import { CopyListDialogComponent, type CopySource, copyCodeStem } from '../ui/copy-list-dialog.component';

/**
 * Base price lists (SRS CP-33): the register on the shared master-data page (code, bilingual name, year, activation;
 * lists are created inactive, intake Q4). A new list opens its price grid; the "Copy" row action creates next year's
 * list with × factor or + amount (WF-10 step 1). Prices are edited on the list's own page. The register filters by
 * search, status and year (intake §14) and exports exactly what it shows as CSV with `pricing.export` (UAT B-07).
 */
@Component({
  selector: 'app-base-price-lists-page',
  imports: [MasterDataPageComponent, CellTemplateDirective, BadgeComponent, RouterLink, CopyListDialogComponent, CanDirective, IconComponent],
  template: `
    <app-master-data-page [config]="view()">
      <!-- the shared register has no slot for an extra filter: the year sits with the page actions -->
      <label pageActions ngProjectAs="[pageActions]" class="year-filter">
        <span class="visually-hidden">{{ i18n.t('pricing.lists.yearFilter') }}</span>
        <select class="inp" (change)="onYear($event)">
          <option value="" [selected]="year() === null">{{ i18n.t('pricing.lists.allYears') }}</option>
          @for (option of yearOptions(); track option) {
            <option [value]="option" [selected]="option === year()">{{ option }}</option>
          }
        </select>
      </label>
      <button pageActions ngProjectAs="[pageActions]" type="button" class="btn ghost" *appCan="permissions.export" [disabled]="exporting()" (click)="exportCsv()">
        <app-icon name="arrow-down" [size]="15" />{{ exporting() ? i18n.t('pricing.exporting') : i18n.t('pricing.exportCsv') }}
      </button>
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

    <app-copy-list-dialog [(open)]="copyOpen" [source]="copySource()" [takenCodes]="copyTaken()" [pending]="copyList.pending()" [problem]="copyList.problem()" (confirmCopy)="runCopy($event)" />
  `,
  styles: `
    .year-filter .inp {
      min-width: 120px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BasePriceListsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PricingApi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly register = viewChild.required(MasterDataPageComponent);

  protected readonly permissions = PRICING_PERMISSIONS;

  protected readonly copyOpen = signal(false);
  protected readonly copySource = signal<(CopySource & { readonly id: string }) | null>(null);
  /** Existing codes like the proposed copy code, read before the dialog opens (UAT B-06). */
  protected readonly copyTaken = signal<ReadonlySet<string>>(new Set<string>());

  /** The year filter (intake §14, UAT B-07); `null` = every year. */
  protected readonly year = signal<number | null>(null);
  /** The years that have lists (any status), read from the register itself. */
  private readonly knownYears = signal<readonly number[]>([]);
  protected readonly yearOptions = computed(() => {
    const year = this.year();
    const years = new Set(this.knownYears());
    if (year !== null) {
      years.add(year);
    }

    return [...years].sort((a, b) => b - a);
  });

  /** The search and status of the list on screen: the export writes exactly these rows, with the year (UAT B-07). */
  private filter: MasterDataFilter = { search: '', active: true };
  protected readonly exporting = signal(false);

  protected readonly copyList = mutation((input: { sourceId: string; request: CopyBasePriceListRequest }) => this.api.copyBasePriceList(input.sourceId, input.request));

  protected readonly config: MasterDataConfig<BasePriceListItem, CreateBasePriceListRequest> = {
    i18nPrefix: 'pricing.lists',
    // B-03: the server sorts the whole register before paging (PricingSortKeys.BasePriceLists)
    sortMode: 'server',
    columns: [
      { key: 'code', header: 'pricing.lists.column.code', mono: true, sortable: true, width: '130px' },
      { key: 'name', header: 'pricing.lists.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (list) => localizedPick({ en: list.nameEn, ar: list.nameAr }, this.i18n.lang()) },
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
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listBasePriceLists({ ...sortParams(filter.sort), search: filter.search, year: this.year(), isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: async (dto) => {
        const created = await this.api.createBasePriceList(dto);
        void this.router.navigate(['/pricing/base-price-lists', created.id]);
        return created;
      },
      update: async (id, dto) => {
        await this.api.updateBasePriceList(id, { nameEn: dto.nameEn, nameAr: dto.nameAr, year: dto.year });
        void this.loadYears();
      },
      setActive: (id, active) => this.api.setBasePriceListActive(id, active),
    },
    toDto: (value) => toListRequest(value),
    permissions: { create: PRICING_PERMISSIONS.create, update: PRICING_PERMISSIONS.edit, activate: PRICING_PERMISSIONS.manage },
    rowActions: [
      { id: 'open', label: 'pricing.lists.action.open', icon: 'grid', run: (list) => void this.router.navigate(['/pricing/base-price-lists', list.id]) },
      { id: 'copy', label: 'pricing.lists.action.copy', icon: 'clipboard', permission: PRICING_PERMISSIONS.create, run: (list) => this.openCopy(list) },
    ],
  };

  /**
   * The configuration the register renders: a new object for every year, so the register starts again at page 1 and
   * re-reads (its page follows the configuration); search and status stay as they are.
   */
  protected readonly view = computed<MasterDataConfig<BasePriceListItem, CreateBasePriceListRequest>>(() => {
    this.year();
    return { ...this.config };
  });

  constructor() {
    void this.i18n.load('pricing');
    void this.loadYears();
  }

  protected onYear(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const year = value === '' ? null : Number(value);
    this.year.set(year !== null && Number.isInteger(year) ? year : null);
  }

  /** CSV of the lists of the current search, status and year, in the grid's formats (UAT B-07, as the other registers since B-10). */
  protected async exportCsv(): Promise<void> {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    try {
      const filter = this.filter;
      const year = this.year();
      const lists = await allPages((page, pageSize) => this.api.listBasePriceLists({ search: filter.search, year, isActive: filter.active, page, pageSize }));
      const t = (key: string): string => this.i18n.t(key);
      const header = [
        t('pricing.lists.column.code'),
        t('pricing.csv.nameEn'),
        t('pricing.csv.nameAr'),
        t('pricing.lists.column.year'),
        t('pricing.lists.csv.activeLines'),
        t('pricing.lists.csv.lines'),
        t('pricing.lists.column.derived'),
        t('pricing.lists.column.copiedFrom'),
        t('pricing.csv.active'),
      ];
      const rows = lists.map((list) => [
        list.code,
        list.nameEn,
        list.nameAr,
        list.year,
        list.activeLineCount,
        list.lineCount,
        list.derivedContractCount,
        list.copiedFromCode,
        t(list.isActive ? 'shared.yes' : 'shared.no'),
      ]);
      downloadCsv(csvFileName('base-price-lists', new Date()), toCsv(header, rows));
      this.toast.success(t('pricing.exported'));
    } catch {
      this.toast.error(this.i18n.t('pricing.exportError'));
    } finally {
      this.exporting.set(false);
    }
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

  private async openCopy(list: BasePriceListItem): Promise<void> {
    this.copyList.reset();
    const source = { id: list.id, code: list.code, nameEn: list.nameEn, nameAr: list.nameAr, year: list.year };
    this.copyTaken.set(await takenCodes(this.api, copyCodeStem(source)));
    this.copySource.set(source);
    this.copyOpen.set(true);
  }

  /** The years of every list (any status) for the year filter; the filter works without them (the chosen year stays an option). */
  private async loadYears(): Promise<void> {
    try {
      const lists = await allPages((page, pageSize) => this.api.listBasePriceLists({ search: '', year: null, isActive: null, page, pageSize }));
      this.knownYears.set([...new Set(lists.map((list) => list.year))]);
    } catch {
      // no options beyond the chosen year; the register shows its own load error
    }
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
