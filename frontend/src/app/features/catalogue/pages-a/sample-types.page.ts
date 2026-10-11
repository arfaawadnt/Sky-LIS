import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, type SampleConditionItem, type SampleTypeDetail, type SampleTypeListItem, type SampleTypeRequest } from '../../../core/api/catalogue-a.contracts';
import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { MasterDataConfig, MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { CatalogueAApi } from '../data-a/catalogue-a.api';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';
import { SampleConditionsDrawerComponent } from '../ui-a/sample-conditions-drawer.component';

/**
 * Sample types (SRS CP-43) on the master-data page; their conditions (fasting, random…) open from the row action in
 * a second drawer, because conditions keep ids for life (tubes merge by group × sample type × condition, BR-02).
 */
@Component({
  selector: 'app-sample-types-page',
  imports: [MasterDataPageComponent, CanDirective, CatalogueExportButtonComponent, SampleConditionsDrawerComponent],
  template: `
    <app-master-data-page [config]="config">
      <app-catalogue-export-button pageActions ngProjectAs="[pageActions]" *appCan="permissions.export" [exporter]="exportCsv" />
    </app-master-data-page>
    <app-sample-conditions-drawer [(open)]="conditionsOpen" [detail]="conditionsOf()" [saving]="savingConditions()" [problem]="conditionsProblem()" (save)="saveConditions($event)" (closed)="onConditionsClosed()" />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SampleTypesPage {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueAApi);
  private readonly toast = inject(ToastService);
  private readonly masterData = viewChild.required(MasterDataPageComponent);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  /** The filter of the list on screen: the export writes exactly these rows (UAT C-04 / B-09). */
  private filter: MasterDataFilter = { search: '', active: true };

  protected readonly conditionsOpen = signal(false);
  protected readonly conditionsOf = signal<SampleTypeDetail | null>(null);
  protected readonly savingConditions = signal(false);
  protected readonly conditionsProblem = signal<ProblemDetails | null>(null);

  protected readonly config: MasterDataConfig<SampleTypeListItem, SampleTypeRequest> = {
    i18nPrefix: 'catalogue-a.sampleTypes',
    columns: [
      { key: 'code', header: 'catalogue-a.sampleTypes.column.code', mono: true, sortable: true, width: '120px' },
      { key: 'shortName', header: 'catalogue-a.sampleTypes.column.shortName', width: '120px' },
      { key: 'name', header: 'catalogue-a.sampleTypes.column.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
      { key: 'activeConditionCount', header: 'catalogue-a.sampleTypes.column.conditions', align: 'end', width: '120px' },
    ],
    fields: [
      { name: 'code', kind: 'code', required: true, maxLength: 20, hint: 'catalogue-a.sampleTypes.hint.code' },
      { name: 'shortName', kind: 'text', required: true, maxLength: 20 },
      { name: 'name', kind: 'localizedText', required: true, maxLength: 200 },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listSampleTypes({ search: filter.search, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.createSampleType(dto),
      update: (id, dto) => this.api.updateSampleType(id, dto),
      setActive: (id, active) => this.api.setSampleTypeActive(id, active),
    },
    permissions: { create: CATALOGUE_A_PERMISSIONS.create, update: CATALOGUE_A_PERMISSIONS.edit, activate: CATALOGUE_A_PERMISSIONS.manage },
    rowActions: [{ id: 'conditions', label: 'catalogue-a.sampleTypes.conditions', icon: 'layers', permission: CATALOGUE_A_PERMISSIONS.edit, run: (row) => this.openConditions(row) }],
  };

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.api.exportSampleTypes({ search: this.filter.search, isActive: this.filter.active });
    const t = (key: string): string => this.i18n.t(`catalogue-a.sampleTypes.column.${key}`);
    downloadCsv(
      csvFileName('sample-types'),
      toCsv(rows, [
        { header: t('code'), value: (row) => row.code },
        { header: t('shortName'), value: (row) => row.shortName },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('conditions'), value: (row) => row.activeConditionCount },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
  }

  protected async openConditions(row: SampleTypeListItem): Promise<void> {
    this.conditionsProblem.set(null);
    this.conditionsOf.set(await this.api.getSampleType(row.id));
    this.conditionsOpen.set(true);
  }

  protected async saveConditions(conditions: readonly SampleConditionItem[]): Promise<void> {
    const detail = this.conditionsOf();
    if (detail === null) {
      return;
    }

    this.savingConditions.set(true);
    this.conditionsProblem.set(null);
    try {
      await this.api.saveSampleConditions(detail.id, conditions);
      this.toast.success(this.i18n.t('catalogue-a.sampleTypes.conditionsSaved'));
      this.conditionsOpen.set(false);
      this.masterData().reload();
    } catch (error: unknown) {
      if (error instanceof ProblemError) {
        this.conditionsProblem.set(error.problem);
      } else {
        this.toast.error(this.i18n.t('shared.error.generic'));
      }
    } finally {
      this.savingConditions.set(false);
    }
  }

  protected onConditionsClosed(): void {
    this.conditionsOf.set(null);
    this.conditionsProblem.set(null);
  }
}
