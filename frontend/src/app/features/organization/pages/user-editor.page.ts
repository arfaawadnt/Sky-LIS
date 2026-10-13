import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, linkedSignal, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import {
  NO_SPECIAL_RIGHTS,
  ORGANIZATION_PERMISSIONS,
  PASSWORD_MAX_LENGTH,
  STAFF_USERNAME_PATTERN,
  type SpecialRights,
  type UserDetail,
  type UserStatus,
} from '../../../core/api/organization.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PortalService } from '../../../core/portal/portal.service';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SERVER_ERROR_KEY, problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { mobileValidator } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { type Breadcrumb, PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { DEFAULT_PASSWORD_MIN_LENGTH, UserEditorFacade } from '../state/users.facade';
import { PrivilegeMatrixComponent, sortKeys } from '../ui/privilege-matrix.component';
import { catalogueInScope, permissionInScope, splitByScope } from './role-permission-scope';
import { type ResetPasswordInput, ResetPasswordDialogComponent } from '../ui/reset-password-dialog.component';
import { isDiscountCapValid, isReportDaysLimitValid, SpecialRightsFormComponent } from '../ui/special-rights-form.component';
import { USER_STATUS_TONES } from '../ui/user-status';

export type UserTab = 'profile' | 'access' | 'privileges' | 'security';

/** The profile field each uniqueness refusal is about (validation LIS-VR-002 O-6): shown under that field, not in a banner. */
export const USER_FIELD_OF_CODE: Readonly<Partial<Record<string, 'username' | 'email' | 'mobile' | 'employeeCode'>>> = {
  'ORG-102': 'username',
  'ORG-103': 'email',
  'ORG-104': 'mobile',
  'ORG-105': 'employeeCode',
};

const TABS: readonly UserTab[] = ['profile', 'access', 'privileges', 'security'];

/** `yyyy-MM-dd` of a stored instant, in local time (the date input's format). */
export function toDateInput(iso: string | null): string {
  if (iso === null) {
    return '';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** The end of the chosen local day as an ISO instant: access ends when that day is over. Empty → no expiry. */
export function fromDateInput(value: string): string | null {
  if (value.trim() === '') {
    return null;
  }

  const date = new Date(`${value}T23:59:59`);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** "Morning, Night" → ['Morning', 'Night']. */
export function parseShifts(value: string): string[] {
  return value
    .split(',')
    .map((shift) => shift.trim())
    .filter((shift) => shift.length > 0);
}

/**
 * The user editor (SRS CP-38; SA-07 under the platform tenant). Profile (with the initial password on creation),
 * access (role and branches), privileges (BR-17 overrides against the role, restore) and security (lock, password
 * reset, activation) are separate tabs saved on their own; Ctrl+S saves the open tab. The server enforces every rule
 * (SOD-ORG-001, ORG-030, ORG-031, TEN-012); the screen hides what it would refuse.
 */
@Component({
  selector: 'app-user-editor-page',
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
    ResetPasswordDialogComponent,
  ],
  templateUrl: './user-editor.page.html',
  styleUrl: './organization-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserEditorPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(UserEditorFacade);
  private readonly auth = inject(AuthStore);
  private readonly can = inject(PermissionsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly isPlatform = inject(PortalService).isPlatform;

  /** Route parameter (`withComponentInputBinding`); absent on the "new" route. */
  readonly userId = input<string | undefined>(undefined);

  protected readonly permissions = ORGANIZATION_PERMISSIONS;
  protected readonly maxPasswordLength = PASSWORD_MAX_LENGTH;
  protected readonly isNew = computed(() => this.userId() === undefined);
  protected readonly user = this.facade.user;
  /** SOD-ORG-001: an administrator does not administer their own access. */
  protected readonly isSelf = computed(() => this.user() !== null && this.user()?.userId === this.auth.currentUser()?.id);
  protected readonly canEditProfile = computed(() => this.can.can(this.isNew() ? ORGANIZATION_PERMISSIONS.usersCreate : ORGANIZATION_PERMISSIONS.usersEdit));
  protected readonly canManage = computed(() => this.can.can(ORGANIZATION_PERMISSIONS.usersManage) && !this.isSelf());
  protected readonly passwordMinLength = computed(() => (this.facade.passwordMinLength.hasValue() ? this.facade.passwordMinLength.value() : DEFAULT_PASSWORD_MIN_LENGTH));

  protected readonly activeTab = signal<UserTab>('profile');
  protected readonly resetOpen = signal(false);

  protected readonly tabs = computed<readonly TabItem[]>(() => {
    const items: TabItem[] = [
      { id: 'profile', label: this.i18n.t('organization.users.tab.profile') },
      { id: 'access', label: this.i18n.t('organization.users.tab.access'), disabled: this.isNew() },
    ];
    if (!this.isPlatform()) {
      items.push({ id: 'privileges', label: this.i18n.t('organization.users.tab.privileges'), disabled: this.isNew(), badge: this.user()?.isModified ? '!' : null });
    }

    items.push({ id: 'security', label: this.i18n.t('organization.users.tab.security'), disabled: this.isNew() });
    return items;
  });

  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(STAFF_USERNAME_PATTERN)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    mustChange: new FormControl(true, { nonNullable: true }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(254)] }),
    mobile: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
    designation: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    shifts: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(440)] }),
    language: new FormControl('en', { nonNullable: true }),
    expiry: new FormControl('', { nonNullable: true }),
    employeeCode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(32)] }),
    roleId: new FormControl('', { nonNullable: true }),
  });

  protected readonly passwordMismatch = signal(false);
  /** A save refused by the form itself: the profile says so, as the drawer editors do (LIS-VR-002 round 4). */
  protected readonly blocked = signal(false);

  /** Access tab: the role picked and the branch assignment (empty set + all = every active branch). */
  protected readonly roleChoice = linkedSignal(() => this.user()?.roleId ?? '');
  protected readonly allBranches = linkedSignal(() => (this.user()?.branchIds.length ?? 0) === 0);
  protected readonly branchChoice = linkedSignal<ReadonlySet<string>>(() => new Set(this.user()?.branchIds ?? []));

  /** Privileges tab: the selection is the effective key set; overrides are its difference from the role's keys. */
  protected readonly baseline = computed<ReadonlySet<string>>(() => new Set(this.user()?.rolePermissionKeys ?? []));
  protected readonly overrideKeys = linkedSignal<ReadonlySet<string>>(() => this.selectionOf(this.user()));
  protected readonly useRoleSpecialRights = linkedSignal(() => this.user()?.overrides.specialRights === null);
  protected readonly specialOverride = linkedSignal<SpecialRights>(() => this.user()?.overrides.specialRights ?? this.user()?.roleSpecialRights ?? NO_SPECIAL_RIGHTS);

  protected readonly added = computed(() => sortKeys([...this.overrideKeys()].filter((key) => !this.baseline().has(key))));
  protected readonly removed = computed(() => sortKeys([...this.baseline()].filter((key) => !this.overrideKeys().has(key))));

  /**
   * O-7: the matrix offers the modules of the delivered release only (as the role editor). The selection and the role's
   * keys it is compared with are cut to those modules; the user's overrides on hidden (Release 2) modules stay in
   * `overrideKeys` and so in every save, never dropped.
   */
  protected readonly catalogue = computed(() => catalogueInScope(this.facade.catalogue.hasValue() ? this.facade.catalogue.value() : { modules: [], actions: [] }));
  protected readonly shownOverrideKeys = computed(() => splitByScope(this.overrideKeys()).shown);
  protected readonly shownBaseline = computed(() => splitByScope(this.baseline()).shown);
  protected readonly shownAdded = computed(() => this.added().filter((key) => permissionInScope(key)));
  protected readonly shownRemoved = computed(() => this.removed().filter((key) => permissionInScope(key)));
  protected readonly keptOverrideCount = computed(() => this.added().length + this.removed().length - this.shownAdded().length - this.shownRemoved().length);
  protected readonly roles = computed(() => (this.facade.roles.hasValue() ? this.facade.roles.value() : []));
  protected readonly branches = computed(() => (this.facade.branches.hasValue() ? this.facade.branches.value() : []));

  protected readonly title = computed(() => {
    const user = this.user();
    return user === null ? this.i18n.t(this.isNew() ? 'organization.users.newTitle' : 'organization.users.title') : localizedPick({ en: user.fullNameEn, ar: user.fullNameAr }, this.i18n.lang());
  });

  protected readonly breadcrumb = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t(this.isPlatform() ? 'organization.users.adminTitle' : 'organization.users.title'), path: this.isPlatform() ? '/admin/organization/users' : '/organization/users' },
    { label: this.title(), path: null },
  ]);

  /** The banner: every refusal but a uniqueness one, which is shown under its field (O-6). */
  protected readonly problem = computed<ProblemDetails | null>(
    () =>
      this.bannerOf(this.facade.create.problem()) ??
      this.bannerOf(this.facade.update.problem()) ??
      this.facade.assignRole.problem() ??
      this.facade.assignBranches.problem() ??
      this.facade.setOverrides.problem() ??
      this.facade.restore.problem() ??
      this.facade.setLocked.problem() ??
      this.facade.setActive.problem(),
  );

  protected readonly resetProblem = computed(() => {
    const problem = this.facade.resetPassword.problem();
    return problem === null ? null : problemMessage(this.i18n, 'organization', problem);
  });

  constructor() {
    void this.i18n.load('organization');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    effect(() => {
      const id = this.userId();
      untracked(() => this.facade.userId.set(id ?? null));
    });
    effect(() => {
      const user = this.user();
      const isNew = this.isNew();
      untracked(() => this.resetForm(user, isNew));
    });
    effect(() => {
      const min = this.passwordMinLength();
      untracked(() => {
        this.form.controls.password.setValidators([Validators.required, Validators.minLength(min), Validators.maxLength(PASSWORD_MAX_LENGTH)]);
        this.form.controls.password.updateValueAndValidity({ emitEvent: false });
      });
    });
  }

  protected localized(en: string, ar: string): string {
    return localizedPick({ en, ar }, this.i18n.lang());
  }

  /** A stored instant in the reader's language (dates are shown, never edited, here). */
  protected formatInstant(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? iso : new Intl.DateTimeFormat(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
  }

  protected statusLabel(status: UserStatus): string {
    return this.i18n.t(`organization.userStatus.${status}`);
  }

  protected statusTone(status: UserStatus): BadgeTone {
    return USER_STATUS_TONES[status];
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'organization', problem);
  }

  protected onTab(id: string | null): void {
    const tab = TABS.find((candidate) => candidate === id);
    if (tab) {
      this.activeTab.set(tab);
    }
  }

  /** Ctrl+S: saves the open tab. */
  protected async saveActive(): Promise<void> {
    switch (this.activeTab()) {
      case 'profile':
        await this.saveProfile();
        break;
      case 'access':
        await this.saveBranches();
        break;
      case 'privileges':
        await this.saveOverrides();
        break;
      case 'security':
        break;
    }
  }

  protected async saveProfile(): Promise<void> {
    if (!this.canEditProfile()) {
      return;
    }

    if (!markAllTouched(this.form)) {
      this.blocked.set(true);
      this.focusFirstInvalid();
      return;
    }

    this.blocked.set(false);

    const value = this.form.getRawValue();
    const common = {
      fullNameEn: value.name.en.trim(),
      fullNameAr: value.name.ar.trim(),
      email: value.email.trim(),
      mobile: value.mobile.trim() === '' ? null : value.mobile.trim(),
      designation: value.designation.trim() === '' ? null : value.designation.trim(),
      shifts: parseShifts(value.shifts),
      passwordExpiresAtUtc: fromDateInput(value.expiry),
      mustChangePassword: value.mustChange,
    };

    if (this.isNew()) {
      this.passwordMismatch.set(value.password !== value.confirm);
      if (value.password !== value.confirm) {
        return;
      }

      // B-02: "not every branch" with no branch ticked would be stored as the empty list, i.e. every branch; refused as
      // the editor's branches tab refuses it (the server refuses it too, ORG-018).
      if (!this.allBranches() && this.branchChoice().size === 0) {
        this.toast.warn(this.i18n.t('organization.users.pickBranch'));
        return;
      }

      const created = await this.facade.create.execute({
        ...common,
        username: value.username.trim().toLowerCase(),
        password: value.password,
        roleId: value.roleId === '' ? null : value.roleId,
        branchIds: this.allBranches() ? [] : [...this.branchChoice()],
        allBranches: this.allBranches(),
        employeeCode: value.employeeCode.trim() === '' ? null : value.employeeCode.trim(),
        preferredLanguage: value.language,
      });
      if (created === null) {
        this.applyProfileProblem(this.facade.create.problem());
        return;
      }

      this.toast.success(this.i18n.t('organization.users.created'));
      void this.router.navigate([this.isPlatform() ? '/admin/organization/users' : '/organization/users', created.id]);
      return;
    }

    if ((await this.facade.update.execute({ ...common, preferredLanguage: value.language })) === null) {
      this.applyProfileProblem(this.facade.update.problem());
      return;
    }

    this.toast.success(this.i18n.t('organization.saved'));
  }

  protected toggleBranch(id: string, event: Event): void {
    const next = new Set(this.branchChoice());
    if ((event.target as HTMLInputElement).checked) {
      next.add(id);
    } else {
      next.delete(id);
    }

    this.branchChoice.set(next);
  }

  protected onAllBranches(event: Event): void {
    this.allBranches.set((event.target as HTMLInputElement).checked);
  }

  protected onRoleChoice(event: Event): void {
    this.roleChoice.set((event.target as HTMLSelectElement).value);
  }

  protected async saveRole(): Promise<void> {
    const roleId = this.pendingRole();
    if (roleId !== null) {
      await this.assignRole(roleId);
    }
  }

  /** A role picked on the access tab and not assigned yet, or null. */
  private pendingRole(): string | null {
    const user = this.user();
    const roleId = this.roleChoice();
    return user === null || roleId === '' || roleId === user.roleId ? null : roleId;
  }

  /** Assigns the role after the confirmation; true when it is stored. */
  private async assignRole(roleId: string): Promise<boolean> {
    const confirmed = await this.confirm.confirm({ title: this.i18n.t('organization.users.confirmRole.title'), message: this.i18n.t('organization.users.confirmRole.message') });
    if (confirmed && (await this.facade.assignRole.execute(roleId)) !== null) {
      this.toast.success(this.i18n.t('organization.users.roleSaved'));
      return true;
    }

    return false;
  }

  protected async saveBranches(): Promise<void> {
    if (this.isNew() || this.isSelf() || !this.can.can(ORGANIZATION_PERMISSIONS.usersEdit)) {
      return;
    }

    if (!this.allBranches() && this.branchChoice().size === 0) {
      this.toast.warn(this.i18n.t('organization.users.pickBranch'));
      return;
    }

    // One tab, one save: a role picked but not assigned yet is assigned too (after its confirmation), never silently
    // dropped by the re-read that follows the branches (UAT B-14). Declined or refused, the user is told it was not stored.
    const branchIds = this.allBranches() ? [] : [...this.branchChoice()];
    const roleId = this.pendingRole();
    if (roleId !== null && !(await this.assignRole(roleId))) {
      this.toast.warn(this.i18n.t('organization.users.roleNotAssigned'));
    }

    if ((await this.facade.assignBranches.execute({ branchIds, allBranches: this.allBranches() })) !== null) {
      this.toast.success(this.i18n.t('organization.users.branchesSaved'));
    }
  }

  /** A change of the matrix (the shown keys): the effective keys of hidden modules stay as they are. */
  protected setOverrideKeys(keys: readonly string[]): void {
    this.overrideKeys.set(new Set([...keys, ...splitByScope(this.overrideKeys()).kept]));
  }

  protected onUseRoleSpecialRights(event: Event): void {
    this.useRoleSpecialRights.set((event.target as HTMLInputElement).checked);
  }

  protected async saveOverrides(): Promise<void> {
    if (this.isNew() || !this.canManage()) {
      return;
    }

    if (!this.useRoleSpecialRights() && !isDiscountCapValid(this.specialOverride().discountCapPercent)) {
      this.toast.error(this.i18n.t('organization.error.ORG-006'));
      return;
    }

    if (!this.useRoleSpecialRights() && !isReportDaysLimitValid(this.specialOverride().reportDaysLimit)) {
      this.toast.error(this.i18n.t('organization.error.ORG-019'));
      return;
    }

    const request = { addedPermissionKeys: this.added(), removedPermissionKeys: this.removed(), specialRights: this.useRoleSpecialRights() ? null : this.specialOverride() };
    if ((await this.facade.setOverrides.execute(request)) !== null) {
      this.toast.success(this.i18n.t('organization.users.overridesSaved'));
    }
  }

  protected async restore(): Promise<void> {
    const confirmed = await this.confirm.confirm({ title: this.i18n.t('organization.users.confirmRestore.title'), message: this.i18n.t('organization.users.confirmRestore.message') });
    if (confirmed && (await this.facade.restore.execute(undefined)) !== null) {
      this.toast.success(this.i18n.t('organization.users.restored'));
    }
  }

  protected async setLocked(locked: boolean): Promise<void> {
    const key = locked ? 'confirmLock' : 'confirmUnlock';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`organization.users.${key}.title`), message: this.i18n.t(`organization.users.${key}.message`), danger: locked });
    if (confirmed && (await this.facade.setLocked.execute(locked)) !== null) {
      this.toast.success(this.i18n.t(locked ? 'organization.users.locked' : 'organization.users.unlocked'));
    }
  }

  protected async setActive(active: boolean): Promise<void> {
    const key = active ? 'confirmReactivate' : 'confirmDeactivate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`organization.users.${key}.title`), message: this.i18n.t(`organization.users.${key}.message`), danger: !active });
    if (confirmed && (await this.facade.setActive.execute(active)) !== null) {
      this.toast.success(this.i18n.t(active ? 'organization.activated' : 'organization.deactivated'));
    }
  }

  protected async resetPassword(input: ResetPasswordInput): Promise<void> {
    if ((await this.facade.resetPassword.execute(input)) !== null) {
      this.resetOpen.set(false);
      this.toast.success(this.i18n.t('organization.reset.done'));
    }
  }

  protected openReset(): void {
    this.facade.resetPassword.reset();
    this.resetOpen.set(true);
  }

  private bannerOf(problem: ProblemDetails | null): ProblemDetails | null {
    return problem !== null && USER_FIELD_OF_CODE[problem.code] !== undefined ? null : problem;
  }

  /** A refused profile save: a uniqueness refusal goes under its field (O-6), field errors under theirs; the first one takes the focus. */
  private applyProfileProblem(problem: ProblemDetails | null): void {
    const field = problem === null ? undefined : USER_FIELD_OF_CODE[problem.code];
    if (problem !== null && field !== undefined) {
      const control = this.form.controls[field];
      control.setErrors({ ...(control.errors ?? {}), [SERVER_ERROR_KEY]: this.problemText(problem) });
      control.markAsTouched();
    } else {
      problemToFormErrors(problem, this.form);
    }

    this.focusFirstInvalid(true);
  }

  /**
   * Focuses the first invalid field of the profile in document order. A field the form itself refuses is already rendered
   * invalid; a server error arrives with the problem signal, so its mark is focused after that render.
   */
  private focusFirstInvalid(afterRender = false): void {
    const selector = ['input', 'select', 'textarea'].map((tag) => `form ${tag}.ng-invalid:not([disabled])`).join(', ');
    const focus = (): void => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus();
    if (afterRender) {
      afterNextRender({ read: focus }, { injector: this.injector });
    } else {
      focus();
    }
  }

  private selectionOf(user: UserDetail | null): ReadonlySet<string> {
    if (user === null) {
      return new Set();
    }

    const removed = new Set(user.overrides.removedPermissionKeys);
    return new Set([...user.rolePermissionKeys.filter((key) => !removed.has(key)), ...user.overrides.addedPermissionKeys]);
  }

  private resetForm(user: UserDetail | null, isNew: boolean): void {
    const credentialControls = [this.form.controls.username, this.form.controls.password, this.form.controls.confirm, this.form.controls.employeeCode, this.form.controls.roleId];
    for (const control of credentialControls) {
      if (isNew) {
        control.enable({ emitEvent: false });
      } else {
        control.disable({ emitEvent: false });
      }
    }

    if (user !== null) {
      this.form.patchValue({
        username: user.username,
        mustChange: user.mustChangePassword,
        name: { en: user.fullNameEn, ar: user.fullNameAr },
        email: user.email,
        mobile: user.mobile ?? '',
        designation: user.designation ?? '',
        shifts: user.shifts.join(', '),
        language: user.preferredLanguage === 'ar' ? 'ar' : 'en',
        expiry: toDateInput(user.passwordExpiresAtUtc),
        employeeCode: user.employeeCode,
        roleId: user.roleId ?? '',
      });
      this.form.markAsPristine();

      // The global account data of a user who also works elsewhere changes only there (ORG-031).
      const accountControls = [this.form.controls.email, this.form.controls.mobile, this.form.controls.expiry, this.form.controls.mustChange];
      for (const control of accountControls) {
        if (user.belongsToOtherTenants) {
          control.disable({ emitEvent: false });
        } else {
          control.enable({ emitEvent: false });
        }
      }

      // Only the main administrator sets their own access end date (TEN-012).
      if (user.isPrimaryAdministrator && !this.isSelf()) {
        this.form.controls.expiry.disable({ emitEvent: false });
      }
    }

    if (!this.canEditProfile()) {
      this.form.disable({ emitEvent: false });
    }
  }
}
