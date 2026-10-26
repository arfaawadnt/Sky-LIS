import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS } from '../../../core/api/catalogue-a.contracts';
import type { CompatibilityMemberView, CreateSampleCompatibilityGroupRequest, SampleCompatibilityGroupView } from '../../../core/api/sample-compatibility.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import { type TableColumn, localizedSortKey } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { SampleCompatibilityFacade } from '../state-a/sample-compatibility.facade';
import { CatalogueBChipsComponent, type CatalogueBChip } from '../ui-b/catalogue-b-chips.component';
import { CompatibilityGroupDrawerComponent } from '../ui-a/compatibility-group-drawer.component';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/**
 * Compatible samples (change request LIS-CR-015, note M4 as restated on 2026-10-26): groups of sample types / conditions
 * the laboratory sets together. Within one processing unit, the tests of a visit whose samples are in one group share one
 * sample, named after the first member ordered among those present. Mounted at `/catalogue/compatible-conditions` and
 * `/admin/catalogue/compatible-conditions`.
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
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    CanDirective,
    CatalogueExportButtonComponent,
    CatalogueBChipsComponent,
    CompatibilityGroupDrawerComponent,
  ],
  templateUrl: './compatible-conditions.page.html',
  styleUrl: './catalogue-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompatibleConditionsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SampleCompatibilityFacade);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly permissionsService = inject(PermissionsService);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<SampleCompatibilityGroupView | null>(null);

  protected readonly columns: readonly TableColumn<SampleCompatibilityGroupView>[] = [
    { key: 'code', header: 'catalogue-a.compatible.column.code', mono: true, sortable: true, width: '140px' },
    { key: 'name', header: 'catalogue-a.compatible.column.name', sortable: true, sortKey: localizedSortKey('name'), value: (row) => this.pick(row.nameEn, row.nameAr) },
    { key: 'members', header: 'catalogue-a.compatible.column.members', value: (row) => row.members.map((member) => this.memberText(member)).join(', ') },
  ];

  constructor() {
    void this.i18n.load('catalogue-a');
    this.facade.refresh();
  }

  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.facade.exportRows();
    const t = (key: string): string => this.i18n.t(`catalogue-a.compatible.column.${key}`);
    downloadCsv(
      csvFileName('compatible-samples'),
      toCsv(rows, [
        { header: t('code'), value: (row) => row.code },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('members'), value: (row) => row.members.map((member) => this.memberText(member)).join(' | ') },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
  };

  protected chips(row: SampleCompatibilityGroupView): readonly CatalogueBChip[] {
    return row.members.map((member) => ({ key: `${member.sampleTypeId}|${member.sampleConditionId ?? '-'}`, text: this.memberText(member), title: this.memberText(member), inactive: !member.available }));
  }

  protected memberText(member: CompatibilityMemberView): string {
    const type = this.pick(member.sampleTypeNameEn, member.sampleTypeNameAr);
    const condition = member.sampleConditionId === null ? this.i18n.t('catalogue-a.compatible.noCondition') : this.pick(member.sampleConditionNameEn ?? '', member.sampleConditionNameAr ?? '');
    return `${member.priority} · ${type} · ${condition}`;
  }

  protected openCreate(): void {
    this.facade.create.reset();
    this.editing.set(null);
    this.drawerOpen.set(true);
  }

  protected openEdit(row: SampleCompatibilityGroupView): void {
    if (!this.permissionsService.can(CATALOGUE_A_PERMISSIONS.edit)) {
      return;
    }

    this.facade.update.reset();
    this.editing.set(row);
    this.drawerOpen.set(true);
  }

  protected loadConditions(sampleTypeId: string): void {
    void this.facade.loadConditions(sampleTypeId);
  }

  protected saving(): boolean {
    return this.facade.create.pending() || this.facade.update.pending();
  }

  protected saveProblem(): ProblemDetails | null {
    return this.editing() === null ? this.facade.create.problem() : this.facade.update.problem();
  }

  protected async save(request: CreateSampleCompatibilityGroupRequest): Promise<void> {
    if (this.saving()) {
      return;
    }

    const editing = this.editing();
    const done =
      editing === null
        ? (await this.facade.create.execute(request)) !== null
        : (await this.facade.update.execute({ id: editing.id, request: { nameEn: request.nameEn, nameAr: request.nameAr, members: request.members } })) !== null;
    if (done) {
      this.toast.success(this.i18n.t('catalogue-a.compatible.saved'));
      this.drawerOpen.set(false);
    }
  }

  protected async toggleActive(row: SampleCompatibilityGroupView): Promise<void> {
    const key = row.isActive ? 'confirmDeactivate' : 'confirmActivate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`catalogue-a.${key}.title`), message: this.i18n.tf(`catalogue-a.${key}.message`, { code: row.code }), danger: row.isActive });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.activation.execute({ id: row.id, active: !row.isActive })) !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'catalogue-a.deactivated' : 'catalogue-a.activated'));
      return;
    }

    const problem = this.facade.activation.problem();
    this.toast.error(problem ? problemMessage(this.i18n, 'catalogue-a.compatible', problem) : this.i18n.t('shared.error.generic'));
  }

  protected onStatusFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === 'all' ? null : value === 'active');
  }

  protected onClosed(): void {
    this.editing.set(null);
  }

  private pick(en: string, ar: string): string {
    return localizedPick({ en, ar }, this.i18n.lang());
  }
}
