import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, type CreateServiceRequest, type ServiceGeneral, type ServiceListItem } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueAApi } from '../data-a/catalogue-a.api';
import { serviceGeneralOf } from '../data-a/catalogue-a.mapping';
import { csvFileName, downloadCsv, toCsv } from '../data-a/csv-export';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/** The reduced form of an additional service (CP-55): code, short name, names, shortcut number and orderability. */
interface AdditionalServiceForm {
  readonly code: string;
  readonly shortName: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly shortcutNo: number | null;
  readonly isOrderable: boolean;
}

/**
 * Additional services (SRS CP-55): chargeable non-test services (home visit, delivery, SMS…) with no sample and no
 * result, never on the job order. The Services list filtered by type Additional with a reduced form; the base price
 * is maintained in Pricing (CP-33). An edit keeps every field the reduced form does not show.
 */
@Component({
  selector: 'app-additional-services-page',
  imports: [MasterDataPageComponent, CanDirective, CatalogueExportButtonComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdditionalServicesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueAApi);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  protected readonly config: MasterDataConfig<ServiceListItem, AdditionalServiceForm> = {
    i18nPrefix: 'catalogue-a.additional',
    columns: [
      { key: 'code', header: 'catalogue-a.additional.column.code', mono: true, sortable: true, width: '120px' },
      { key: 'shortName', header: 'catalogue-a.additional.column.shortName', width: '140px' },
      { key: 'name', header: 'catalogue-a.additional.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
      { key: 'shortcutNo', header: 'catalogue-a.additional.column.shortcutNo', align: 'end', mono: true, width: '110px' },
      { key: 'isOrderable', header: 'catalogue-a.additional.column.isOrderable', width: '110px', secondary: true },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 16, hint: 'catalogue-a.additional.hint.code' },
      { name: 'shortName', kind: 'text', required: true, maxLength: 50 },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'shortcutNo', kind: 'number', min: 1, max: 999999, step: 1 },
      { name: 'isOrderable', kind: 'boolean', defaultValue: true },
    ],
    toDto: (value) => toForm(value),
    adapter: {
      list: (filter, page, signal) =>
        this.api.listServices({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize, type: 'Additional', clinicalGroupId: null }, signal),
      create: (form) => this.api.createService(createRequest(form)),
      update: async (id, form) => {
        const detail = await this.api.getService(id);
        await this.api.updateService(id, { ...serviceGeneralOf(detail), ...reduced(form), type: 'Additional' });
      },
      setActive: (id, active) => this.api.setServiceActive(id, active),
    },
    permissions: { create: CATALOGUE_A_PERMISSIONS.create, update: CATALOGUE_A_PERMISSIONS.edit, activate: CATALOGUE_A_PERMISSIONS.manage },
  };

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.api.exportServices('Additional');
    const t = (key: string): string => this.i18n.t(`catalogue-a.additional.column.${key}`);
    downloadCsv(
      csvFileName('additional-services'),
      toCsv(rows, [
        { header: t('code'), value: (row) => row.code },
        { header: t('shortName'), value: (row) => row.shortName },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('shortcutNo'), value: (row) => row.shortcutNo },
        { header: t('isOrderable'), value: (row) => row.isOrderable },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => row.isActive },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
  }
}

function toForm(value: FormValue): AdditionalServiceForm {
  const name = (value['name'] ?? {}) as { en?: string; ar?: string };
  const shortcut = value['shortcutNo'];
  return {
    code: typeof value['code'] === 'string' ? value['code'] : '',
    shortName: typeof value['shortName'] === 'string' ? value['shortName'] : '',
    nameEn: (name.en ?? '').trim(),
    nameAr: (name.ar ?? '').trim(),
    shortcutNo: typeof shortcut === 'number' ? shortcut : null,
    isOrderable: value['isOrderable'] === true,
  };
}

function reduced(form: AdditionalServiceForm): Pick<ServiceGeneral, 'shortName' | 'nameEn' | 'nameAr' | 'shortcutNo' | 'isOrderable'> {
  return { shortName: form.shortName, nameEn: form.nameEn, nameAr: form.nameAr, shortcutNo: form.shortcutNo, isOrderable: form.isOrderable };
}

function createRequest(form: AdditionalServiceForm): CreateServiceRequest {
  return {
    code: form.code,
    type: 'Additional',
    ...reduced(form),
    reportName: null,
    loinc: null,
    cpt: null,
    clinicalGroupId: null,
    sampleTypeId: null,
    sampleConditionId: null,
    header: null,
    aliases: [],
    precaution: null,
    gender: 'Any',
    turnaroundHours: 0,
    labelCount: 0,
    printAlone: false,
    showOnWeb: false,
    profileSum: null,
    members: [],
  };
}
