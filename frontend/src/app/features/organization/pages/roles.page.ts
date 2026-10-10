import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { ORGANIZATION_PERMISSIONS, type RoleListItem } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { RolesFacade } from '../state/roles.facade';

/**
 * Roles & privileges (SRS CP-37; SA-06 under the platform tenant): the register of privilege groups with their
 * permission and member counts. A role with members cannot be deactivated (ORG-011); the editor holds the matrix.
 */
@Component({
  selector: 'app-roles-page',
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
  ],
  templateUrl: './roles.page.html',
  styleUrl: './organization-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RolesPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(RolesFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly permissions = ORGANIZATION_PERMISSIONS;

  protected readonly columns: readonly TableColumn<RoleListItem>[] = [
    { key: 'name', header: 'organization.roles.column.name', sortable: true, value: (row) => this.name(row) },
    { key: 'permissionCount', header: 'organization.roles.column.permissions', align: 'end', width: '130px' },
    { key: 'memberCount', header: 'organization.roles.column.members', align: 'end', width: '110px' },
    { key: 'isActive', header: 'organization.roles.column.status', width: '120px' },
  ];

  protected readonly rows = computed<readonly RoleListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));

  protected readonly totalCount = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));

  constructor() {
    void this.i18n.load('organization');
  }

  protected name(row: RoleListItem): string {
    return localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang());
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(value === '' ? null : value === 'active');
  }

  protected add(): void {
    void this.router.navigate(['new'], { relativeTo: this.route });
  }

  protected open(row: RoleListItem): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }

  protected async toggle(row: RoleListItem): Promise<void> {
    const activate = !row.isActive;
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t(activate ? 'organization.roles.confirmActivate.title' : 'organization.roles.confirmDeactivate.title'),
      message: this.i18n.tf(activate ? 'organization.roles.confirmActivate.message' : 'organization.roles.confirmDeactivate.message', { name: this.name(row) }),
      danger: !activate,
    });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.setActive.execute({ id: row.id, active: activate })) !== null) {
      this.toast.success(this.i18n.t(activate ? 'organization.activated' : 'organization.deactivated'));
    }
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'organization', { code });
  }
}
