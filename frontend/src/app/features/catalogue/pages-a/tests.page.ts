import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { CATALOGUE_A_PERMISSIONS, LAB_TEST_TYPES, type LabTestListItem, type LabTestType } from '../../../core/api/catalogue-a.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CatalogueAApi } from '../data-a/catalogue-a.api';
import { csvFileName, downloadCsv, toCsv } from '../data-a/csv-export';
import { CatalogueAOptions, type CatalogueOption, optionLabel } from '../state-a/catalogue-a-options';
import { TestsListFacade } from '../state-a/catalogue-lists.facade';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/** Tests (SRS CP-41): the measured parameters, filtered by type, group and status; the editor opens on a row or "Add". */
@Component({
  selector: 'app-tests-page',
  imports: [
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    CanDirective,
    CatalogueExportButtonComponent,
  ],
  templateUrl: './tests.page.html',
  styleUrl: './catalogue-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TestsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TestsListFacade);
  protected readonly options = inject(CatalogueAOptions);
  private readonly api = inject(CatalogueAApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;
  protected readonly types = LAB_TEST_TYPES;

  protected readonly columns: readonly TableColumn<LabTestListItem>[] = [
    { key: 'code', header: 'catalogue-a.tests.column.code', mono: true, sortable: true, width: '120px' },
    { key: 'name', header: 'catalogue-a.tests.column.name', sortable: true, value: (row) => this.pick(row.nameEn, row.nameAr) },
    { key: 'type', header: 'catalogue-a.tests.column.type', width: '140px' },
    { key: 'group', header: 'catalogue-a.tests.column.group', value: (row) => this.pick(row.clinicalGroupNameEn, row.clinicalGroupNameAr) },
    { key: 'unitName', header: 'catalogue-a.tests.column.unit', mono: true, width: '100px', secondary: true },
    { key: 'sample', header: 'catalogue-a.tests.column.sample', secondary: true, value: (row) => this.pick(row.sampleTypeNameEn, row.sampleTypeNameAr) },
    { key: 'isHiddenHelper', header: 'catalogue-a.tests.column.hidden', width: '100px', secondary: true },
    { key: 'status', header: 'catalogue-a.tests.column.status', width: '110px' },
  ];

  protected readonly rows = computed<readonly LabTestListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly total = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly groups = computed(() => (this.options.groups.hasValue() ? this.options.groups.value() : []));

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.api.exportTests();
    const t = (key: string): string => this.i18n.t(`catalogue-a.tests.column.${key}`);
    downloadCsv(
      csvFileName('tests'),
      toCsv(rows, [
        { header: t('code'), value: (row) => row.code },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('type'), value: (row) => this.i18n.t(`catalogue-a.testType.${row.type}`) },
        { header: t('group'), value: (row) => this.pick(row.clinicalGroupNameEn, row.clinicalGroupNameAr) },
        { header: t('unit'), value: (row) => row.unitName },
        { header: t('sample'), value: (row) => this.pick(row.sampleTypeNameEn, row.sampleTypeNameAr) },
        { header: this.i18n.t('catalogue-a.tests.field.gender'), value: (row) => this.i18n.t(`catalogue-a.gender.${row.gender}`) },
        { header: t('hidden'), value: (row) => row.isHiddenHelper },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => row.isActive },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    this.options.refresh();
  }

  protected typeLabel(type: LabTestType): string {
    return this.i18n.t(`catalogue-a.testType.${type}`);
  }

  protected groupLabel(option: CatalogueOption): string {
    return optionLabel(option, this.i18n.lang());
  }

  protected onType(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setType(this.types.find((type) => type === value) ?? null);
  }

  protected onGroup(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setGroup(value === '' ? null : value);
  }

  protected onActive(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActive(value === 'all' ? null : value === 'active');
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected add(): void {
    void this.router.navigate(['new'], { relativeTo: this.route });
  }

  protected open(row: LabTestListItem): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }

  private pick(en: string | null, ar: string | null): string {
    return localizedPick({ en: en ?? '', ar: ar ?? '' }, this.i18n.lang());
  }
}
