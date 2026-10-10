import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { NO_SPECIAL_RIGHTS, ORGANIZATION_PERMISSIONS, type ResultRight, type SpecialRights } from '../../../core/api/organization.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PortalService } from '../../../core/portal/portal.service';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { type Breadcrumb, PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RoleEditorFacade } from '../state/roles.facade';
import { PrivilegeMatrixComponent, sortKeys } from '../ui/privilege-matrix.component';
import { type BranchOption, ResultRightsGridComponent } from '../ui/result-rights-grid.component';
import { isDiscountCapValid, SpecialRightsFormComponent } from '../ui/special-rights-form.component';

export type RoleTab = 'general' | 'permissions' | 'special' | 'results';

function sameKeys(left: ReadonlySet<string>, right: readonly string[]): boolean {
  return left.size === right.length && right.every((key) => left.has(key));
}

function resultKey(right: ResultRight): string {
  return `${right.clinicalGroupId}|${right.branchId ?? ''}|${right.canSee}${right.canReview}${right.canVerify}${right.canEdit}${right.canPrint}`;
}

function sameResults(left: readonly ResultRight[], right: readonly ResultRight[]): boolean {
  const keys = new Set(left.map(resultKey));
  return left.length === right.length && right.every((item) => keys.has(resultKey(item)));
}

/**
 * The role editor (SRS CP-37; SA-06 under the platform tenant). General data, the privilege matrix, the special rights
 * and the result rights per clinical group and branch are separate tabs, each saved on its own (Ctrl+S saves the open
 * tab). A new role is created from the general tab with its first permissions (an active role needs one, ORG-005).
 * Changing a role changes every member at their next request (BR-17). The platform portal shows no special or result
 * rights (laboratory concepts).
 */
@Component({
  selector: 'app-role-editor-page',
  imports: [
    ReactiveFormsModule,
    SaveShortcutDirective,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    TabsComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    BadgeComponent,
    IconComponent,
    PrivilegeMatrixComponent,
    SpecialRightsFormComponent,
    ResultRightsGridComponent,
  ],
  templateUrl: './role-editor.page.html',
  styleUrl: './organization-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleEditorPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(RoleEditorFacade);
  private readonly portal = inject(PortalService);
  private readonly can = inject(PermissionsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  /** Route parameter (`withComponentInputBinding`); absent on the "new" route. */
  readonly roleId = input<string | undefined>(undefined);

  protected readonly permissions = ORGANIZATION_PERMISSIONS;
  protected readonly isNew = computed(() => this.roleId() === undefined);
  protected readonly isPlatform = this.portal.isPlatform;
  protected readonly canEdit = computed(() => this.can.can(this.isNew() ? ORGANIZATION_PERMISSIONS.create : ORGANIZATION_PERMISSIONS.edit));

  protected readonly activeTab = signal<RoleTab>('general');

  protected readonly tabs = computed<readonly TabItem[]>(() => {
    const items: TabItem[] = [
      { id: 'general', label: this.i18n.t('organization.roles.tab.general') },
      { id: 'permissions', label: this.i18n.t('organization.roles.tab.permissions'), badge: this.keys().size },
    ];
    if (!this.isPlatform()) {
      items.push(
        { id: 'special', label: this.i18n.t('organization.roles.tab.special'), disabled: this.isNew() },
        { id: 'results', label: this.i18n.t('organization.roles.tab.results'), badge: this.results().length, disabled: this.isNew() },
      );
    }

    return items;
  });

  protected readonly form = new FormGroup({
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  /** The editable copies of the role's privileges; they reset whenever the detail (re)loads. */
  protected readonly keys = linkedSignal<ReadonlySet<string>>(() => new Set(this.facade.role()?.permissionKeys ?? []));
  protected readonly special = linkedSignal<SpecialRights>(() => this.facade.role()?.specialRights ?? NO_SPECIAL_RIGHTS);
  protected readonly results = linkedSignal<readonly ResultRight[]>(() => this.facade.role()?.resultRights ?? []);

  protected readonly permissionsDirty = computed(() => !sameKeys(this.keys(), this.facade.role()?.permissionKeys ?? []));
  protected readonly specialDirty = computed(() => {
    const role = this.facade.role();
    return role !== null && JSON.stringify(role.specialRights) !== JSON.stringify(this.special());
  });
  protected readonly resultsDirty = computed(() => {
    const role = this.facade.role();
    return role !== null && !sameResults(role.resultRights, this.results());
  });

  protected readonly catalogue = computed(() => (this.facade.catalogue.hasValue() ? this.facade.catalogue.value() : { modules: [], actions: [] }));
  protected readonly groups = computed(() => (this.facade.clinicalGroups.hasValue() ? this.facade.clinicalGroups.value() : []));
  protected readonly branches = computed<readonly BranchOption[]>(() => (this.facade.branches.hasValue() ? this.facade.branches.value() : []));

  protected readonly title = computed(() => {
    const role = this.facade.role();
    return role === null ? this.i18n.t(this.isNew() ? 'organization.roles.newTitle' : 'organization.roles.title') : localizedPick({ en: role.nameEn, ar: role.nameAr }, this.i18n.lang());
  });

  protected readonly breadcrumb = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t('organization.roles.title'), path: this.isPlatform() ? '/admin/organization/roles' : '/organization/roles' },
    { label: this.title(), path: null },
  ]);

  /** The problem of the save that ran last, whichever tab it belongs to. */
  protected readonly problem = computed<ProblemDetails | null>(
    () => this.facade.create.problem() ?? this.facade.update.problem() ?? this.facade.savePermissions.problem() ?? this.facade.saveSpecialRights.problem() ?? this.facade.saveResultRights.problem() ?? this.facade.setActive.problem(),
  );

  constructor() {
    void this.i18n.load('organization');
    effect(() => {
      const id = this.roleId();
      untracked(() => this.facade.roleId.set(id ?? null));
    });
    effect(() => {
      const role = this.facade.role();
      untracked(() => {
        if (role !== null) {
          this.form.reset({ name: { en: role.nameEn, ar: role.nameAr }, description: role.description ?? '' });
        }

        if (!this.canEdit()) {
          this.form.disable();
        }
      });
    });
  }

  protected setKeys(keys: readonly string[]): void {
    this.keys.set(new Set(keys));
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'organization', problem);
  }

  /** Ctrl+S: saves the open tab. */
  protected async saveActive(): Promise<void> {
    switch (this.activeTab()) {
      case 'general':
        await this.saveGeneral();
        break;
      case 'permissions':
        await (this.isNew() ? this.saveGeneral() : this.savePermissions());
        break;
      case 'special':
        await this.saveSpecial();
        break;
      case 'results':
        await this.saveResults();
        break;
    }
  }

  protected async saveGeneral(): Promise<void> {
    if (!this.canEdit() || !markAllTouched(this.form)) {
      return;
    }

    const value = this.form.getRawValue();
    const general = { nameEn: value.name.en.trim(), nameAr: value.name.ar.trim(), description: value.description.trim() === '' ? null : value.description.trim() };
    if (this.isNew()) {
      if (this.keys().size === 0) {
        this.activeTab.set('permissions');
        this.toast.warn(this.i18n.t('organization.roles.needPermission'));
        return;
      }

      const created = await this.facade.create.execute({ ...general, permissionKeys: sortKeys(this.keys()) });
      if (created === null) {
        problemToFormErrors(this.facade.create.problem(), this.form);
        return;
      }

      this.toast.success(this.i18n.t('organization.roles.created'));
      void this.router.navigate([this.isPlatform() ? '/admin/organization/roles' : '/organization/roles', created.id]);
      return;
    }

    if ((await this.facade.update.execute(general)) === null) {
      problemToFormErrors(this.facade.update.problem(), this.form);
      return;
    }

    this.toast.success(this.i18n.t('organization.saved'));
  }

  protected async savePermissions(): Promise<void> {
    if (this.isNew() || !this.permissionsDirty()) {
      return;
    }

    const role = this.facade.role();
    if (role !== null && role.memberCount > 0) {
      const confirmed = await this.confirm.confirm({
        title: this.i18n.t('organization.roles.confirmPropagate.title'),
        message: this.i18n.tf('organization.roles.confirmPropagate.message', { count: role.memberCount }),
      });
      if (!confirmed) {
        return;
      }
    }

    if ((await this.facade.savePermissions.execute(sortKeys(this.keys()))) !== null) {
      this.toast.success(this.i18n.t('organization.roles.permissionsSaved'));
    }
  }

  protected async saveSpecial(): Promise<void> {
    if (this.isNew() || !this.specialDirty()) {
      return;
    }

    if (!isDiscountCapValid(this.special().discountCapPercent)) {
      this.toast.error(this.i18n.t('organization.error.ORG-006'));
      return;
    }

    if ((await this.facade.saveSpecialRights.execute(this.special())) !== null) {
      this.toast.success(this.i18n.t('organization.roles.specialSaved'));
    }
  }

  protected async saveResults(): Promise<void> {
    if (this.isNew() || !this.resultsDirty()) {
      return;
    }

    if ((await this.facade.saveResultRights.execute(this.results())) !== null) {
      this.toast.success(this.i18n.t('organization.roles.resultsSaved'));
    }
  }

  protected async toggleActive(): Promise<void> {
    const role = this.facade.role();
    if (role === null) {
      return;
    }

    const activate = !role.isActive;
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t(activate ? 'organization.roles.confirmActivate.title' : 'organization.roles.confirmDeactivate.title'),
      message: this.i18n.tf(activate ? 'organization.roles.confirmActivate.message' : 'organization.roles.confirmDeactivate.message', { name: this.title() }),
      danger: !activate,
    });
    if (confirmed && (await this.facade.setActive.execute(activate)) !== null) {
      this.toast.success(this.i18n.t(activate ? 'organization.activated' : 'organization.deactivated'));
    }
  }

  protected discard(tab: RoleTab): void {
    const role = this.facade.role();
    switch (tab) {
      case 'permissions':
        this.keys.set(new Set(role?.permissionKeys ?? []));
        break;
      case 'special':
        this.special.set(role?.specialRights ?? NO_SPECIAL_RIGHTS);
        break;
      case 'results':
        this.results.set(role?.resultRights ?? []);
        break;
      case 'general':
        if (role !== null) {
          this.form.reset({ name: { en: role.nameEn, ar: role.nameAr }, description: role.description ?? '' });
        }

        break;
    }
  }

  protected onTab(id: string | null): void {
    const tab = (['general', 'permissions', 'special', 'results'] as const).find((candidate) => candidate === id);
    if (tab) {
      this.activeTab.set(tab);
    }
  }
}
