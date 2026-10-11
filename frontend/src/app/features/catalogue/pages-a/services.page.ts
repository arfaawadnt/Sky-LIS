import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { CATALOGUE_A_PERMISSIONS, SERVICE_TYPES, type ServiceListItem, type ServiceType } from '../../../core/api/catalogue-a.contracts';
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
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { CatalogueAOptions, optionLabel } from '../state-a/catalogue-a-options';
import { ServicesListFacade } from '../state-a/catalogue-lists.facade';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/**
 * Services (SRS CP-40): the orderable items (tests, profiles, packages, cultures, text reports, additional
 * services) with search by code, short name, name, alias or shortcut number, filters by type, group and status,
 * CSV export, and the editor on a row (Enter) or "Add".
 */
@Component({
  selector: 'app-services-page',
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
  templateUrl: './services.page.html',
  styleUrl: './catalogue-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServicesPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ServicesListFacade);
  protected readonly options = inject(CatalogueAOptions);
  private readonly api = inject(CatalogueAApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;
  protected readonly types = SERVICE_TYPES;

  protected readonly columns: readonly TableColumn<ServiceListItem>[] = [
    { key: 'sequence', header: 'catalogue-a.services.column.sequence', align: 'end', mono: true, sortable: true, width: '80px' },
    { key: 'code', header: 'catalogue-a.services.column.code', mono: true, sortable: true, width: '120px' },
    { key: 'name', header: 'catalogue-a.services.column.name', sortable: true, value: (row) => this.pick(row.nameEn, row.nameAr) },
    { key: 'type', header: 'catalogue-a.services.column.type', width: '130px' },
    { key: 'group', header: 'catalogue-a.services.column.group', secondary: true, value: (row) => this.pick(row.clinicalGroupNameEn, row.clinicalGroupNameAr) },
    { key: 'sample', header: 'catalogue-a.services.column.sample', secondary: true, value: (row) => this.pick(row.sampleTypeNameEn, row.sampleTypeNameAr) },
    { key: 'memberCount', header: 'catalogue-a.services.column.members', align: 'end', width: '90px', secondary: true },
    { key: 'status', header: 'catalogue-a.services.column.status', width: '110px' },
  ];

  protected readonly rows = computed<readonly ServiceListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly total = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly groups = computed(() => (this.options.groups.hasValue() ? this.options.groups.value() : []));

  protected readonly exportCsv = async (): Promise<void> => {
    const filter = this.facade.filter();   // the export is the grid: search, status, type and group (UAT C-04)
    const rows = await this.api.exportServices({ search: filter.search, isActive: filter.isActive }, filter.type, filter.clinicalGroupId);
    const t = (key: string): string => this.i18n.t(`catalogue-a.services.column.${key}`);
    downloadCsv(
      csvFileName('services'),
      toCsv(rows, [
        { header: t('sequence'), value: (row) => row.sequence },
        { header: t('code'), value: (row) => row.code },
        { header: this.i18n.t('catalogue-a.services.field.shortName'), value: (row) => row.shortName },
        { header: this.i18n.t('catalogue-a.services.field.shortcutNo'), value: (row) => row.shortcutNo },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('type'), value: (row) => this.i18n.t(`catalogue-a.serviceType.${row.type}`) },
        { header: t('group'), value: (row) => this.pick(row.clinicalGroupNameEn, row.clinicalGroupNameAr) },
        { header: t('sample'), value: (row) => this.pick(row.sampleTypeNameEn, row.sampleTypeNameAr) },
        { header: this.i18n.t('catalogue-a.services.field.gender'), value: (row) => this.i18n.t(`catalogue-a.gender.${row.gender}`) },
        { header: t('members'), value: (row) => row.memberCount },
        { header: this.i18n.t('catalogue-a.services.field.isOrderable'), value: (row) => yesNo((key) => this.i18n.t(key), row.isOrderable) },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
  };

  constructor() {
    void this.i18n.load('catalogue-a');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    this.options.refresh();
  }

  protected typeLabel(type: ServiceType): string {
    return this.i18n.t(`catalogue-a.serviceType.${type}`);
  }

  protected groupLabel(option: Parameters<typeof optionLabel>[0]): string {
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

  protected open(row: ServiceListItem): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }

  private pick(en: string | null, ar: string | null): string {
    return localizedPick({ en: en ?? '', ar: ar ?? '' }, this.i18n.lang());
  }
}
