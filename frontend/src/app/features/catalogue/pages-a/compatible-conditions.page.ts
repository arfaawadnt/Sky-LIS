import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS } from '../../../core/api/catalogue-a.contracts';
import type { CompatibleSampleTypeView, SampleConditionPairItem } from '../../../core/api/compatible-conditions.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { type CompatibleRow, CompatibleConditionsFacade } from '../state-a/compatible-conditions.facade';
import { CompatibleConditionsDrawerComponent } from '../ui-a/compatible-conditions-drawer.component';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/**
 * Compatible sample conditions (change request LIS-CR-015, note M4): every sample type with its conditions and the
 * condition each merges into. At registration a condition that merges into P joins P's tube when that tube is in the same
 * visit (one sample named after P); alone it keeps its own sample. Edited per sample type in a drawer (`catalogue.edit`).
 * Mounted at `/catalogue/compatible-conditions` and `/admin/catalogue/compatible-conditions`.
 */
@Component({
  selector: 'app-compatible-conditions-page',
  imports: [
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    BadgeComponent,
    IconComponent,
    CanDirective,
    CatalogueExportButtonComponent,
    CompatibleConditionsDrawerComponent,
  ],
  templateUrl: './compatible-conditions.page.html',
  styleUrl: './catalogue-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompatibleConditionsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(CompatibleConditionsFacade);
  private readonly permissionsService = inject(PermissionsService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<CompatibleSampleTypeView | null>(null);

  protected readonly columns: readonly TableColumn<CompatibleRow>[] = [
    { key: 'sampleType', header: 'catalogue-a.compatible.column.sampleType', sortable: true, value: (row) => `${row.typeCode} · ${this.pick(row.typeNameEn, row.typeNameAr)}` },
    { key: 'condition', header: 'catalogue-a.compatible.column.condition', sortable: true, value: (row) => (row.conditionCode === null ? '' : `${row.conditionCode} · ${this.pick(row.conditionNameEn, row.conditionNameAr)}`) },
    { key: 'mergesInto', header: 'catalogue-a.compatible.column.mergesInto', sortable: true, value: (row) => this.mergesIntoText(row) },
  ];

  constructor() {
    void this.i18n.load('catalogue-a');
    this.facade.refresh();
  }

  protected readonly exportCsv = (): Promise<void> => {
    const t = (key: string): string => this.i18n.t(`catalogue-a.compatible.column.${key}`);
    downloadCsv(
      csvFileName('compatible-sample-conditions'),
      toCsv(this.facade.rows(), [
        { header: t('typeCode'), value: (row) => row.typeCode },
        { header: t('sampleType'), value: (row) => this.pick(row.typeNameEn, row.typeNameAr) },
        { header: t('conditionCode'), value: (row) => row.conditionCode ?? '' },
        { header: t('condition'), value: (row) => (row.conditionCode === null ? '' : this.pick(row.conditionNameEn, row.conditionNameAr)) },
        { header: t('mergesInto'), value: (row) => this.mergesIntoText(row) },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.conditionCode === null ? row.typeActive : row.conditionActive) },
      ]),
    );
    return Promise.resolve();
  };

  /** Only a type with at least two conditions has something to merge. */
  protected canEditRow(row: CompatibleRow): boolean {
    return (this.facade.typeOf(row.sampleTypeId)?.conditions.length ?? 0) > 1;
  }

  protected open(row: CompatibleRow): void {
    if (!this.permissionsService.can(CATALOGUE_A_PERMISSIONS.edit) || !this.canEditRow(row)) {
      return;
    }

    this.facade.save.reset();
    this.editing.set(this.facade.typeOf(row.sampleTypeId));
    this.drawerOpen.set(true);
  }

  protected async save(pairs: readonly SampleConditionPairItem[]): Promise<void> {
    const type = this.editing();
    if (type === null || this.facade.save.pending()) {
      return;
    }

    await this.facade.save.execute({ sampleTypeId: type.id, pairs });
    if (this.facade.save.problem() === null) {
      this.toast.success(this.i18n.t('catalogue-a.compatible.saved'));
      this.drawerOpen.set(false);
    }
  }

  protected onClosed(): void {
    this.editing.set(null);
    this.facade.save.reset();
  }

  protected rowStatus(row: CompatibleRow): string {
    return (row.conditionCode === null ? row.typeActive : row.conditionActive && row.typeActive) ? 'Active' : 'Inactive';
  }

  protected mergesIntoText(row: CompatibleRow): string {
    if (row.conditionCode === null) {
      return this.i18n.t('catalogue-a.compatible.noConditions');
    }

    return row.targetCode === null ? this.i18n.t('catalogue-a.compatible.ownSample') : `${row.targetCode} · ${this.pick(row.targetNameEn, row.targetNameAr)}`;
  }

  private pick(en: string | null, ar: string | null): string {
    return localizedPick({ en: en ?? '', ar: ar ?? '' }, this.i18n.lang());
  }
}
