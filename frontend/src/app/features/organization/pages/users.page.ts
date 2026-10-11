import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { ORGANIZATION_PERMISSIONS, USER_STATUSES, type UserListItem, type UserStatus } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PortalService } from '../../../core/portal/portal.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { allPages, csvFileName, downloadCsv, toCsv } from '../data/csv-export';
import { OrganizationApi } from '../data/organization.api';
import { UsersFacade } from '../state/users.facade';
import { OrganizationExportButtonComponent } from '../ui/export-button.component';
import { USER_STATUS_TONES } from '../ui/user-status';

/** The CSV's Branches cell: the assigned branch codes (UAT R1-06: the count was written), `all` when unrestricted. */
export function userBranchesCell(row: Pick<UserListItem, 'branchCount' | 'branchCodes'>, all: string): string {
  if (row.branchCount === 0) {
    return all;
  }

  return row.branchCodes && row.branchCodes.length > 0 ? row.branchCodes.join('; ') : String(row.branchCount);
}

/**
 * Users (SRS CP-38; SA-07 "Admin users" under the platform tenant): staff accounts with role, branches, status and the
 * flags that matter to an administrator (main administrator, modified privileges, must change password).
 */
@Component({
  selector: 'app-users-page',
  imports: [
    CanDirective,
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
    OrganizationExportButtonComponent,
  ],
  templateUrl: './users.page.html',
  styleUrl: './organization-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(UsersFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(OrganizationApi);
  protected readonly isPlatform = inject(PortalService).isPlatform;

  protected readonly permissions = ORGANIZATION_PERMISSIONS;
  protected readonly statuses = USER_STATUSES;
  protected readonly rowId = (row: UserListItem): string => row.userId;

  protected readonly columns: readonly TableColumn<UserListItem>[] = [
    { key: 'name', header: 'organization.users.column.name', sortable: true, value: (row) => this.localized(row.fullNameEn, row.fullNameAr) },
    { key: 'role', header: 'organization.users.column.role', value: (row) => (row.roleNameEn === null ? '—' : this.localized(row.roleNameEn, row.roleNameAr ?? row.roleNameEn)) },
    { key: 'designation', header: 'organization.users.column.designation', secondary: true },
    { key: 'mobile', header: 'organization.users.column.mobile', mono: true, secondary: true },
    {
      key: 'branches',
      header: 'organization.users.column.branches',
      secondary: true,
      value: (row) => (row.branchCount === 0 ? this.i18n.t('organization.users.allBranches') : String(row.branchCount)),
    },
    { key: 'status', header: 'organization.users.column.status', width: '220px' },
  ];

  protected readonly rows = computed<readonly UserListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly roles = computed(() => (this.facade.roles.hasValue() ? this.facade.roles.value() : []));

  constructor() {
    void this.i18n.load('organization');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  /** CSV of the users of the current search, role and status filter, in the grid's formats (UAT B-10). */
  protected readonly exportCsv = async (): Promise<void> => {
    const filter = this.facade.filter();
    const rows = await allPages((page, pageSize) => this.api.listUsers({ ...filter, page, pageSize }));
    const t = (key: string): string => this.i18n.t(`organization.${key}`);
    const yesNo = (value: boolean): string => this.i18n.t(value ? 'shared.yes' : 'shared.no');
    downloadCsv(
      csvFileName('users'),
      toCsv(rows, [
        { header: t('csv.username'), value: (row) => row.username },
        { header: t('csv.nameEn'), value: (row) => row.fullNameEn },
        { header: t('csv.nameAr'), value: (row) => row.fullNameAr },
        { header: t('users.column.role'), value: (row) => (row.roleNameEn === null ? '' : this.localized(row.roleNameEn, row.roleNameAr ?? row.roleNameEn)) },
        { header: t('users.column.designation'), value: (row) => row.designation },
        { header: t('csv.email'), value: (row) => row.email },
        { header: t('users.column.mobile'), value: (row) => row.mobile },
        { header: t('users.column.branches'), value: (row) => userBranchesCell(row, t('users.allBranches')) },
        { header: t('users.column.status'), value: (row) => this.statusLabel(row.status) },
        { header: t('csv.mustChangePassword'), value: (row) => yesNo(row.mustChangePassword) },
        { header: t('csv.modified'), value: (row) => yesNo(row.isModified) },
        { header: t('csv.mainAdministrator'), value: (row) => yesNo(row.isPrimaryAdministrator) },
      ]),
    );
  };

  protected localized(en: string, ar: string): string {
    return localizedPick({ en, ar }, this.i18n.lang());
  }

  protected statusLabel(status: UserStatus): string {
    return this.i18n.t(`organization.userStatus.${status}`);
  }

  protected statusTone(status: UserStatus): BadgeTone {
    return USER_STATUS_TONES[status];
  }

  protected onRole(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setRole(value === '' ? null : value);
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected add(): void {
    void this.router.navigate(['new'], { relativeTo: this.route });
  }

  protected open(row: UserListItem): void {
    void this.router.navigate([row.userId], { relativeTo: this.route });
  }
}
