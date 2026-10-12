import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, type ClinicalGroupListItem, type ClinicalGroupRequest } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { sortParams } from '../../../shared/forms/paging';
import { localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataFilter, SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueAApi } from '../data-a/catalogue-a.api';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/**
 * Clinical groups (SRS CP-39): code (immutable), unit code (the tube label letter), bilingual name, report name,
 * report template (catalogue B) and display order; deactivate instead of delete (BR-15). Also SA-12 under the
 * platform tenant.
 */
@Component({
  selector: 'app-clinical-groups-page',
  imports: [MasterDataPageComponent, CanDirective, CatalogueExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClinicalGroupsPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueAApi);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  /** The filter of the list on screen: the export writes exactly these rows (UAT C-04 / B-09). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly config: MasterDataConfig<ClinicalGroupListItem, ClinicalGroupRequest> = {
    i18nPrefix: 'catalogue-a.groups',
    sortMode: 'server',
    columns: [
      { key: 'code', header: 'catalogue-a.groups.column.code', mono: true, sortable: true, width: '120px' },
      { key: 'unitCode', header: 'catalogue-a.groups.column.unitCode', mono: true, width: '90px' },
      { key: 'name', header: 'catalogue-a.groups.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (row) => this.name(row) },
      { key: 'reportName', header: 'catalogue-a.groups.column.reportName', secondary: true },
      { key: 'displayOrder', header: 'catalogue-a.groups.column.displayOrder', align: 'end', sortable: true, width: '110px', secondary: true },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 16, hint: 'catalogue-a.groups.hint.code' },
      { name: 'unitCode', kind: 'code', required: true, maxLength: 4, immutable: false, hint: 'catalogue-a.groups.hint.unitCode' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'reportName', kind: 'text', maxLength: 200 },
      { name: 'reportTemplateId', kind: 'select', options: () => this.reportTemplateOptions() },
      { name: 'displayOrder', kind: 'number', required: true, min: 0, max: 9999, defaultValue: 0 },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listClinicalGroups({ ...sortParams(filter.sort), search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.createClinicalGroup(dto),
      update: (id, dto) => this.api.updateClinicalGroup(id, dto),
      setActive: (id, active) => this.api.setClinicalGroupActive(id, active),
    },
    permissions: { create: CATALOGUE_A_PERMISSIONS.create, update: CATALOGUE_A_PERMISSIONS.edit, activate: CATALOGUE_A_PERMISSIONS.manage },
  };

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.api.exportClinicalGroups({ search: this.filter.search, isActive: this.filter.active });
    const t = (key: string): string => this.i18n.t(`catalogue-a.groups.column.${key}`);
    downloadCsv(
      csvFileName('clinical-groups'),
      toCsv(rows, [
        { header: t('code'), value: (row) => row.code },
        { header: t('unitCode'), value: (row) => row.unitCode },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('reportName'), value: (row) => row.reportName },
        { header: t('displayOrder'), value: (row) => row.displayOrder },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
  }

  private name(row: ClinicalGroupListItem): string {
    return localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang());
  }

  private async reportTemplateOptions(): Promise<readonly SelectOption[]> {
    const templates = await this.api.listReportTemplateOptions();
    return templates.map((template) => ({ value: template.id, label: template.nameEn, labelAr: template.nameAr }));
  }
}
