import { ChangeDetectionStrategy, Component, computed, inject, resource, signal, viewChild } from '@angular/core';

import { type GovernorateListItem, type GovernorateRequest, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataFilter, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { CsvExportService } from '../data/csv-export';
import { PlatformApi } from '../data/platform.api';
import { fileStamp, pick } from '../ui/format';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';

/** SA-11 Governorates and districts, per country; the country is fixed once created and filters the list. */
@Component({
  selector: 'app-platform-governorates-page',
  imports: [MasterDataPageComponent, IconComponent],
  template: `
    <app-master-data-page [config]="config">
      <label pageActions class="inline-filter">
        <span>{{ i18n.t('platform.governorates.column.country') }}</span>
        <select class="inp" (change)="onCountry($event)">
          <option value="">{{ i18n.t('platform.all') }}</option>
          @for (country of countryOptions(); track country.value) {
            <option [value]="country.value" [selected]="countryFilter() === country.value">{{ i18n.lang() === 'ar' ? (country.labelAr ?? country.label) : country.label }}</option>
          }
        </select>
      </label>
      <button pageActions type="button" class="btn ghost" (click)="exportCsv()"><app-icon name="printer" [size]="15" />{{ i18n.t('platform.exportCsv') }}</button>
    </app-master-data-page>
  `,
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GovernoratesPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PlatformApi);
  private readonly csv = inject(CsvExportService);

  /** The filter of the list on screen: the export writes exactly these rows (UAT R2-02, as C-04 elsewhere). */
  private filter: MasterDataFilter = { search: '', active: true };

  private readonly page = viewChild.required(MasterDataPageComponent);

  protected readonly countryFilter = signal<string | null>(null);

  private readonly countries = resource({
    loader: ({ abortSignal }) => this.api.listCountries({ search: null, isActive: null, page: 1, pageSize: 200 }, abortSignal),
  });

  protected readonly countryOptions = computed<readonly SelectOption[]>(() =>
    (this.countries.value()?.items ?? []).map((country) => ({ value: country.id, label: country.nameEn, labelAr: country.nameAr })),
  );

  protected readonly config: MasterDataConfig<GovernorateListItem, GovernorateRequest> = {
    i18nPrefix: 'platform.governorates',
    // B-03: the server sorts the whole register before paging (PlatformAdminSortKeys.Governorates)
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'platform.governorates.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (row) => pick(row.nameEn, row.nameAr, this.i18n.lang()) },
      { key: 'country', header: 'platform.governorates.column.country', sortable: true, sortKey: localizedSortKey('country'), value: (row) => pick(row.countryNameEn, row.countryNameAr, this.i18n.lang()) },
      { key: 'district', header: 'platform.governorates.column.district' },
      { key: 'remarks', header: 'platform.governorates.column.remarks', secondary: true },
    ],
    fields: [
      {
        name: 'countryId',
        kind: 'select',
        required: true,
        immutable: true,
        options: async () => {
          const page = await this.api.listCountries({ search: null, isActive: true, page: 1, pageSize: 200 });
          return page.items.map((country) => ({ value: country.id, label: country.nameEn, labelAr: country.nameAr }));
        },
      },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 100 },
      { name: 'district', kind: 'text', maxLength: 100 },
      { name: 'remarks', kind: 'textarea', maxLength: 500 },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listGovernorates({ ...sortParams(filter.sort), countryId: this.countryFilter(), search: filter.search || null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.createGovernorate(dto),
      update: (id, dto) => this.api.updateGovernorate(id, dto),
      setActive: (id, active) => this.api.setGovernorateActive(id, active),
    },
    permissions: { create: PLATFORM_PERMISSIONS.tenancyManage, update: PLATFORM_PERMISSIONS.tenancyManage, activate: PLATFORM_PERMISSIONS.tenancyManage },
  };

  constructor() {
    void this.i18n.load('platform');
  }

  protected onCountry(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.countryFilter.set(value === '' ? null : value);
    this.page().reload();
  }

  protected async exportCsv(): Promise<void> {
    const countryId = this.countryFilter();
    const rows = await this.api.all((page) => this.api.listGovernorates({ countryId, search: this.filter.search || null, isActive: this.filter.active, ...page }));
    this.csv.download(`governorates-${fileStamp()}`, [
      { header: this.i18n.t('platform.governorates.column.country'), value: (row: GovernorateListItem) => pick(row.countryNameEn, row.countryNameAr, this.i18n.lang()) },
      { header: this.i18n.t('platform.governorates.field.nameEn'), value: (row) => row.nameEn },
      { header: this.i18n.t('platform.governorates.field.nameAr'), value: (row) => row.nameAr },
      { header: this.i18n.t('platform.governorates.column.district'), value: (row) => row.district },
      { header: this.i18n.t('platform.governorates.column.remarks'), value: (row) => row.remarks },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], rows);
  }
}
