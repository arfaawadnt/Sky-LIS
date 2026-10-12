import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';

import { type CountryListItem, type CountryRequest, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { isProblem } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataFilter, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { CsvExportService } from '../data/csv-export';
import { PlatformApi } from '../data/platform.api';
import { fileStamp, pick } from '../ui/format';
import { fieldProblem, ianaTimeZones, isDialCodeValid } from '../ui/validators';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';

/**
 * SA-10 Countries: the validation data of a laboratory's details (dial code, currency, language, time zone, mobile
 * length). The ISO code is the natural key (immutable); a country with governorates or clients cannot be deleted
 * (GEO-110) — deactivate it instead.
 */
@Component({
  selector: 'app-platform-countries-page',
  imports: [MasterDataPageComponent, IconComponent],
  template: `
    <app-master-data-page [config]="config">
      <button pageActions type="button" class="btn ghost" (click)="exportCsv()"><app-icon name="printer" [size]="15" />{{ i18n.t('platform.exportCsv') }}</button>
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CountriesPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PlatformApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);

  /** The filter of the list on screen: the export writes exactly these rows (UAT R2-02, as C-04 elsewhere). */
  private filter: MasterDataFilter = { search: '', active: true };

  private readonly page = viewChild.required(MasterDataPageComponent);

  protected readonly config: MasterDataConfig<CountryListItem, CountryRequest> = {
    i18nPrefix: 'platform.countries',
    // B-03: the server sorts the whole register before paging (PlatformAdminSortKeys.Countries)
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'platform.countries.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (row) => `${row.flag ?? ''} ${pick(row.nameEn, row.nameAr, this.i18n.lang())}`.trim() },
      { key: 'isoCode', header: 'platform.countries.column.isoCode', mono: true, width: '80px', sortable: true },
      { key: 'dialCode', header: 'platform.countries.column.dialCode', mono: true, width: '100px' },
      { key: 'currencyCode', header: 'platform.countries.column.currency', value: (row) => `${row.currencyCode} · ${row.currency}` },
      { key: 'mobileLength', header: 'platform.countries.column.mobileLength', align: 'end', width: '110px' },
      { key: 'timeZone', header: 'platform.countries.column.timeZone', secondary: true },
      { key: 'governorateCount', header: 'platform.countries.column.governorates', align: 'end', secondary: true },
    ],
    fields: [
      { name: 'name', kind: 'localizedText', required: true, maxLength: 100 },
      { name: 'isoCode', kind: 'code', required: true, maxLength: 2, hint: 'platform.countries.hint.isoCode' },
      { name: 'dialCode', kind: 'text', required: true, maxLength: 5, hint: 'platform.countries.hint.dialCode' },
      { name: 'currency', kind: 'text', required: true, maxLength: 60 },
      { name: 'currencyCode', kind: 'code', required: true, maxLength: 3, immutable: false, hint: 'platform.countries.hint.currencyCode' },
      { name: 'language', kind: 'select', required: true, defaultValue: 'ar', options: [{ value: 'ar', label: 'Arabic', labelAr: 'العربية' }, { value: 'en', label: 'English', labelAr: 'الإنجليزية' }] },
      // C-08: the zone is picked from the IANA names, never typed (the server refuses anything else with GEO-007)
      { name: 'timeZone', kind: 'select', required: true, hint: 'platform.countries.hint.timeZone', options: () => this.timeZoneOptions() },
      { name: 'mobileLength', kind: 'number', required: true, min: 6, max: 15, step: 1, defaultValue: 10, hint: 'platform.countries.hint.mobileLength' },
      { name: 'flag', kind: 'text', maxLength: 16 },
      { name: 'remarks', kind: 'textarea', maxLength: 500 },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listCountries({ ...sortParams(filter.sort), search: filter.search || null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.save(dto, () => this.api.createCountry(dto)),
      update: (id, dto) => this.save(dto, () => this.api.updateCountry(id, dto)),
      setActive: (id, active) => this.api.setCountryActive(id, active),
    },
    permissions: { create: PLATFORM_PERMISSIONS.tenancyManage, update: PLATFORM_PERMISSIONS.tenancyManage, activate: PLATFORM_PERMISSIONS.tenancyManage },
    rowActions: [
      {
        id: 'delete',
        label: 'platform.countries.delete',
        icon: 'x',
        danger: true,
        permission: PLATFORM_PERMISSIONS.tenancyManage,
        visible: (row) => row.governorateCount === 0,
        run: (row) => this.delete(row),
      },
    ],
  };

  /** The IANA zones of the browser plus the zones the countries already store, so an existing value is always offered. */
  protected async timeZoneOptions(): Promise<readonly SelectOption[]> {
    let stored: readonly string[] = [];
    try {
      stored = (await this.api.listCountries({ search: null, isActive: null, page: 1, pageSize: 200 })).items.map((country) => country.timeZone);
    } catch {
      // the browser's list alone still lets a country be saved
    }

    return ianaTimeZones(stored).map((zone) => ({ value: zone, label: zone }));
  }

  constructor() {
    void this.i18n.load('platform');
  }

  /**
   * C-09: the dial code is checked before anything is sent (the hint states the rule), and the server's own refusal (GEO-002)
   * is shown under the field as well, never only at the top of the dialog.
   */
  private async save(dto: CountryRequest, send: () => Promise<unknown>): Promise<unknown> {
    const message = (): string => this.i18n.t('platform.countries.error.GEO-002');
    if (!isDialCodeValid(dto.dialCode)) {
      throw fieldProblem('dialCode', message());
    }

    try {
      return await send();
    } catch (error: unknown) {
      throw isProblem(error, 'GEO-002') ? fieldProblem('dialCode', message()) : error;
    }
  }

  private async delete(row: CountryListItem): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('platform.countries.delete'),
      message: this.i18n.tf('platform.countries.confirmDelete', { name: pick(row.nameEn, row.nameAr, this.i18n.lang()) }),
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    await this.api.deleteCountry(row.id);   // GEO-110 surfaces as the page's toast
    this.toast.success(this.i18n.t('platform.countries.deleted'));
    this.page().reload();
  }

  protected async exportCsv(): Promise<void> {
    const rows = await this.api.all((page) => this.api.listCountries({ search: this.filter.search || null, isActive: this.filter.active, ...page }));
    this.csv.download(`countries-${fileStamp()}`, [
      { header: this.i18n.t('platform.countries.field.nameEn'), value: (row: CountryListItem) => row.nameEn },
      { header: this.i18n.t('platform.countries.field.nameAr'), value: (row) => row.nameAr },
      { header: this.i18n.t('platform.countries.column.isoCode'), value: (row) => row.isoCode },
      { header: this.i18n.t('platform.countries.column.dialCode'), value: (row) => row.dialCode },
      { header: this.i18n.t('platform.countries.field.currency'), value: (row) => row.currency },
      { header: this.i18n.t('platform.countries.field.currencyCode'), value: (row) => row.currencyCode },
      { header: this.i18n.t('platform.countries.field.language'), value: (row) => row.language },
      { header: this.i18n.t('platform.countries.column.timeZone'), value: (row) => row.timeZone },
      { header: this.i18n.t('platform.countries.column.mobileLength'), value: (row) => row.mobileLength },
      { header: this.i18n.t('platform.countries.column.governorates'), value: (row) => row.governorateCount },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], rows);
  }
}
