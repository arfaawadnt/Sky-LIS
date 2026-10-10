import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, type MeasurementUnitListItem, type MeasurementUnitRequest } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { CanDirective } from '../../../shared/ui/can.directive';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueAApi } from '../data-a/catalogue-a.api';
import { csvFileName, downloadCsv, toCsv } from '../data-a/csv-export';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/** Units of measurement (SRS CP-44): a language-neutral symbol (mg/dL), unique ignoring case; deactivate instead of delete. */
@Component({
  selector: 'app-units-page',
  imports: [MasterDataPageComponent, CanDirective, CatalogueExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnitsPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueAApi);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  protected readonly config: MasterDataConfig<MeasurementUnitListItem, MeasurementUnitRequest> = {
    i18nPrefix: 'catalogue-a.units',
    columns: [{ key: 'name', header: 'catalogue-a.units.column.name', mono: true, sortable: true }],
    fields: [{ name: 'name', kind: 'text', required: true, maxLength: 50, hint: 'catalogue-a.units.hint.name' }],
    adapter: {
      list: (filter, page, signal) => this.api.listUnits({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.createUnit(dto),
      update: (id, dto) => this.api.updateUnit(id, dto),
      setActive: (id, active) => this.api.setUnitActive(id, active),
    },
    permissions: { create: CATALOGUE_A_PERMISSIONS.create, update: CATALOGUE_A_PERMISSIONS.edit, activate: CATALOGUE_A_PERMISSIONS.manage },
  };

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.api.exportUnits();
    downloadCsv(
      csvFileName('units'),
      toCsv(rows, [
        { header: this.i18n.t('catalogue-a.units.column.name'), value: (row) => row.name },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => row.isActive },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
  }
}
