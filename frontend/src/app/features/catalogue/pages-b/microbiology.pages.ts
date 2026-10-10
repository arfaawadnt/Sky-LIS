import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import {
  type AntibioticListItem,
  type AntibioticRequest,
  CATALOGUE_B_PERMISSIONS,
  type CommercialNameListItem,
  type CommercialNameRequest,
  type DrugTypeListItem,
  type DrugTypeRequest,
  type OrganismListItem,
  type OrganismRequest,
  type SensitivityValueListItem,
  type SensitivityValueRequest,
} from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataPermissions } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueBApi } from '../data-b/catalogue-b.api';
import { CatalogueBLookups, toSelectOption } from '../data-b/catalogue-b-lookups';
import { CatalogueBExportButtonComponent } from '../ui-b/catalogue-b-export-button.component';

// Microbiology masters (SRS CP-56, SA-14; LDM §3.6 culture set-up): one master-data page per list, under the tabs
// of MicrobiologyPage. Every name is bilingual (intake A3).

const PERMISSIONS: MasterDataPermissions = { create: CATALOGUE_B_PERMISSIONS.create, update: CATALOGUE_B_PERMISSIONS.edit, activate: CATALOGUE_B_PERMISSIONS.manage };

const TEMPLATE = `
  <app-master-data-page [config]="config">
    <app-catalogue-b-export-button pageActions [export]="exportCsv" />
  </app-master-data-page>
`;

@Component({
  selector: 'app-catalogue-b-drug-types-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: TEMPLATE,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DrugTypesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);

  protected readonly exportCsv = (): Promise<void> => this.api.drugTypes.exportCsv({});

  protected readonly config: MasterDataConfig<DrugTypeListItem, DrugTypeRequest> = {
    i18nPrefix: 'catalogue-b.drugTypes',
    columns: [{ key: 'name', header: 'catalogue-b.drugTypes.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) }],
    fields: [{ name: 'name', kind: 'localizedText', required: true, maxLength: 200 }],
    adapter: {
      list: (filter, page, signal) => this.api.drugTypes.list({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.drugTypes.create(dto),
      update: (id, dto) => this.api.drugTypes.update(id, dto),
      setActive: (id, active) => this.api.drugTypes.setActive(id, active),
    },
    permissions: PERMISSIONS,
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}

@Component({
  selector: 'app-catalogue-b-commercial-names-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: TEMPLATE,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommercialNamesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly exportCsv = (): Promise<void> => this.api.commercialNames.exportCsv({});

  protected readonly config: MasterDataConfig<CommercialNameListItem, CommercialNameRequest> = {
    i18nPrefix: 'catalogue-b.commercialNames',
    columns: [
      { key: 'name', header: 'catalogue-b.commercialNames.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
      { key: 'drugType', header: 'catalogue-b.commercialNames.column.drugType', sortable: true, value: (item) => localizedPick({ en: item.drugTypeNameEn, ar: item.drugTypeNameAr }, this.i18n.lang()) },
    ],
    fields: [
      { name: 'drugTypeId', kind: 'select', required: true, options: async () => (await this.lookups.drugTypes()).map(toSelectOption) },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.commercialNames.list({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.commercialNames.create(dto),
      update: (id, dto) => this.api.commercialNames.update(id, dto),
      setActive: (id, active) => this.api.commercialNames.setActive(id, active),
    },
    permissions: PERMISSIONS,
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}

@Component({
  selector: 'app-catalogue-b-antibiotics-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: TEMPLATE,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AntibioticsPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);
  private readonly lookups = inject(CatalogueBLookups);

  protected readonly exportCsv = (): Promise<void> => this.api.antibiotics.exportCsv({});

  protected readonly config: MasterDataConfig<AntibioticListItem, AntibioticRequest> = {
    i18nPrefix: 'catalogue-b.antibiotics',
    columns: [
      { key: 'code', header: 'catalogue-b.antibiotics.column.code', mono: true, sortable: true, width: '120px' },
      { key: 'name', header: 'catalogue-b.antibiotics.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
      { key: 'abbreviation', header: 'catalogue-b.antibiotics.column.abbreviation', mono: true, width: '120px' },
      {
        key: 'commercialName',
        header: 'catalogue-b.antibiotics.column.commercialName',
        secondary: true,
        value: (item) => (item.commercialNameId === null ? '' : localizedPick({ en: item.commercialNameEn ?? '', ar: item.commercialNameAr ?? '' }, this.i18n.lang())),
      },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 20, hint: 'catalogue-b.common.hint.code' },
      { name: 'abbreviation', kind: 'code', required: true, maxLength: 10, immutable: false, hint: 'catalogue-b.antibiotics.hint.abbreviation' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
      { name: 'commercialNameId', kind: 'select', full: true, options: async () => (await this.lookups.commercialNames()).map(toSelectOption) },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.antibiotics.list({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.antibiotics.create(dto),
      update: (id, dto) => this.api.antibiotics.update(id, dto),
      setActive: (id, active) => this.api.antibiotics.setActive(id, active),
    },
    permissions: PERMISSIONS,
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}

@Component({
  selector: 'app-catalogue-b-organisms-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: TEMPLATE,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganismsPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);

  protected readonly exportCsv = (): Promise<void> => this.api.organisms.exportCsv({});

  protected readonly config: MasterDataConfig<OrganismListItem, OrganismRequest> = {
    i18nPrefix: 'catalogue-b.organisms',
    columns: [
      { key: 'code', header: 'catalogue-b.organisms.column.code', mono: true, sortable: true, width: '140px' },
      { key: 'name', header: 'catalogue-b.organisms.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 20, hint: 'catalogue-b.common.hint.code' },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.organisms.list({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.organisms.create(dto),
      update: (id, dto) => this.api.organisms.update(id, dto),
      setActive: (id, active) => this.api.organisms.setActive(id, active),
    },
    permissions: PERMISSIONS,
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}

@Component({
  selector: 'app-catalogue-b-sensitivity-values-page',
  imports: [MasterDataPageComponent, CatalogueBExportButtonComponent],
  template: TEMPLATE,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SensitivityValuesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);

  protected readonly exportCsv = (): Promise<void> => this.api.sensitivityValues.exportCsv({});

  protected readonly config: MasterDataConfig<SensitivityValueListItem, SensitivityValueRequest> = {
    i18nPrefix: 'catalogue-b.sensitivityValues',
    columns: [
      { key: 'displayOrder', header: 'catalogue-b.sensitivityValues.column.displayOrder', align: 'end', sortable: true, width: '90px' },
      { key: 'code', header: 'catalogue-b.sensitivityValues.column.code', mono: true, sortable: true, width: '110px' },
      { key: 'name', header: 'catalogue-b.sensitivityValues.column.name', sortable: true, value: (item) => localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang()) },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 10, hint: 'catalogue-b.sensitivityValues.hint.code' },
      { name: 'displayOrder', kind: 'number', required: true, min: 0, max: 999, step: 1, defaultValue: 1 },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
    ],
    adapter: {
      list: (filter, page, signal) => this.api.sensitivityValues.list({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal),
      create: (dto) => this.api.sensitivityValues.create(dto),
      update: (id, dto) => this.api.sensitivityValues.update(id, dto),
      setActive: (id, active) => this.api.sensitivityValues.setActive(id, active),
    },
    permissions: PERMISSIONS,
  };

  constructor() {
    void this.i18n.load('catalogue-b');
  }
}
