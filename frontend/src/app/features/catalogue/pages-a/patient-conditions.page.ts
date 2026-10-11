import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, type PatientConditionListItem, type PatientConditionRequest } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueAApi } from '../data-a/catalogue-a.api';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/** Patient conditions (SRS CP-45): conditions that switch reference ranges (BR-08), chosen at registration. */
@Component({
  selector: 'app-patient-conditions-page',
  imports: [MasterDataPageComponent, CanDirective, CatalogueExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientConditionsPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueAApi);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  /** The filter of the list on screen: the export writes exactly these rows (UAT C-04 / B-09). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly config: MasterDataConfig<PatientConditionListItem, PatientConditionRequest> = {
    i18nPrefix: 'catalogue-a.conditions',
    columns: [{ key: 'name', header: 'catalogue-a.conditions.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) }],
    fields: [{ name: 'name', kind: 'localizedText', required: true, maxLength: 200 }],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listPatientConditions({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.createPatientCondition(dto),
      update: (id, dto) => this.api.updatePatientCondition(id, dto),
      setActive: (id, active) => this.api.setPatientConditionActive(id, active),
    },
    permissions: { create: CATALOGUE_A_PERMISSIONS.create, update: CATALOGUE_A_PERMISSIONS.edit, activate: CATALOGUE_A_PERMISSIONS.manage },
  };

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.api.exportPatientConditions({ search: this.filter.search, isActive: this.filter.active });
    downloadCsv(
      csvFileName('patient-conditions'),
      toCsv(rows, [
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
  }
}
