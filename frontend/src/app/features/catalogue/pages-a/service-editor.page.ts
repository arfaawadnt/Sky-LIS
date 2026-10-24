import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';

import {
  CATALOGUE_A_PERMISSIONS,
  GENDER_RESTRICTIONS,
  SERVICE_TYPES,
  type GenderRestriction,
  type SampleConditionView,
  type ServiceDetail,
  type ServiceGeneral,
  type ServiceMemberItem,
  type ServiceMemberKind,
  type ServiceType,
  serviceCarriesSample,
  serviceMemberKind,
} from '../../../core/api/catalogue-a.contracts';
import { type ProblemDetails, ProblemCodes } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { Breadcrumb } from '../../../shared/ui/page-header/page-header.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { numberOrNull, serviceGeneralOf, textOrNull } from '../data-a/catalogue-a.mapping';
import { CatalogueAOptions, type CatalogueOption, optionLabel } from '../state-a/catalogue-a-options';
import { type MemberCandidate, ServiceEditorFacade } from '../state-a/service-editor.facade';

/** The sample-type picker of a culture service: the culture specimens first, then the rest (LIS-CR-007, C-11). */
export interface SampleTypeGroups {
  readonly cultures: readonly CatalogueOption[];
  readonly others: readonly CatalogueOption[];
}

/** A member row of the editor; its order is its position. */
export interface MemberRow {
  readonly kind: ServiceMemberKind;
  readonly memberId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly memberType: string;
  readonly isActive: boolean;
  readonly inSum: boolean;
  readonly visible: boolean;
}

/**
 * The service editor (SRS CP-40): general data and members, one save (Ctrl+S). A new service is created with its
 * members in one call (a test service needs its test from the start); an existing one saves the general data and the
 * members separately, only what changed. The type is fixed once created (CAT-035).
 */
@Component({
  selector: 'app-service-editor-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    TabsComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    SearchBoxComponent,
    BadgeComponent,
    IconComponent,
    CanDirective,
    SaveShortcutDirective,
    UppercaseDirective,
  ],
  templateUrl: './service-editor.page.html',
  styleUrl: './catalogue-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceEditorPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ServiceEditorFacade);
  protected readonly options = inject(CatalogueAOptions);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly access = inject(PermissionsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;
  protected readonly types = SERVICE_TYPES;
  protected readonly genders = GENDER_RESTRICTIONS;

  /** The id in the address bar, followed reactively: the router reuses this page when only `:id` changes (UAT B-05). */
  protected readonly serviceId = toSignal(this.route.paramMap.pipe(map((params) => params.get('id'))), { initialValue: this.route.snapshot.paramMap.get('id') });
  protected readonly isNew = computed(() => this.serviceId() === null);

  /** Viewers (no create / edit right) see the service read-only (UAT B-12); the server decides regardless. */
  protected readonly canEdit = computed(() => this.access.can(this.isNew() ? CATALOGUE_A_PERMISSIONS.create : CATALOGUE_A_PERMISSIONS.edit));

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(16)] }),
    type: new FormControl<ServiceType>('Test', { nonNullable: true }),
    shortName: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(50)] }),
    shortcutNo: new FormControl<number | null>(null, { validators: [Validators.min(1), Validators.max(999999)] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    reportName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    loinc: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    cpt: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    clinicalGroupId: new FormControl<string | null>(null),
    sampleTypeId: new FormControl<string | null>(null),
    sampleConditionId: new FormControl<string | null>(null),
    header: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    aliases: new FormControl('', { nonNullable: true }),
    precaution: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
    gender: new FormControl<GenderRestriction>('Any', { nonNullable: true }),
    turnaroundHours: new FormControl<number | null>(24, { validators: [Validators.required, Validators.min(0), Validators.max(8760)] }),
    labelCount: new FormControl<number | null>(1, { validators: [Validators.required, Validators.min(0), Validators.max(20)] }),
    printAlone: new FormControl(false, { nonNullable: true }),
    isOrderable: new FormControl(true, { nonNullable: true }),
    showOnWeb: new FormControl(false, { nonNullable: true }),
    profileSum: new FormControl<number | null>(null, { validators: [Validators.min(0.0001)] }),
  });

  protected readonly type = toSignal(this.form.controls.type.valueChanges, { initialValue: this.form.controls.type.value });
  private readonly sampleTypeId = toSignal(this.form.controls.sampleTypeId.valueChanges, { initialValue: this.form.controls.sampleTypeId.value });

  protected readonly carriesSample = computed(() => serviceCarriesSample(this.type()));
  protected readonly memberKind = computed(() => serviceMemberKind(this.type()));
  protected readonly isProfile = computed(() => this.type() === 'Profile');

  protected readonly members = signal<readonly MemberRow[]>([]);
  protected readonly membersDirty = signal(false);
  protected readonly conditions = signal<readonly SampleConditionView[]>([]);
  protected readonly activeTab = signal<string | null>('general');
  protected readonly saving = signal(false);
  /** A save the form refused (validation v0.5.0 round 4): the page says so, like the drawer editors, until the form is valid. */
  protected readonly blocked = signal(false);
  protected readonly generalProblem = signal<ProblemDetails | null>(null);
  protected readonly membersProblem = signal<ProblemDetails | null>(null);

  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { id: 'general', label: this.i18n.t('catalogue-a.services.tab.general') },
    { id: 'members', label: this.i18n.t('catalogue-a.services.tab.members'), badge: this.members().length, disabled: this.memberKind() === null },
  ]);

  /** The loaded service, only while it is the one in the address bar: a previous record is never shown or acted on (UAT B-05). */
  protected readonly detail = computed<ServiceDetail | null>(() => {
    const detail = this.facade.detail.hasValue() ? this.facade.detail.value() : null;
    return detail !== null && detail.id === this.serviceId() ? detail : null;
  });

  /** The active conditions of the chosen sample type, plus the one the service already holds (BR-24, UAT B-08). */
  protected readonly conditionChoices = computed(() => {
    const current = this.detail()?.sampleConditionId ?? null;
    return this.conditions().filter((condition) => condition.isActive || condition.id === current);
  });

  protected readonly title = computed(() => {
    const detail = this.detail();
    return detail === null ? this.i18n.t('catalogue-a.services.new') : `${detail.code} · ${localizedPick({ en: detail.nameEn, ar: detail.nameAr }, this.i18n.lang())}`;
  });

  protected readonly crumbs = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t('catalogue-a.services.title'), path: this.router.createUrlTree(['..'], { relativeTo: this.route }).toString() },
    { label: this.detail()?.code ?? this.i18n.t('catalogue-a.services.new'), path: null },
  ]);

  protected readonly candidates = computed<readonly MemberCandidate[]>(() => {
    const chosen = new Set(this.members().map((member) => member.memberId));
    const own = this.serviceId();
    return (this.facade.candidates.hasValue() ? this.facade.candidates.value() : []).filter((candidate) => !chosen.has(candidate.id) && candidate.id !== own);
  });

  protected readonly groupOptions = computed(() => this.selectable(this.options.groups.hasValue() ? this.options.groups.value() : [], this.detail()?.clinicalGroupId ?? null));
  protected readonly sampleTypeOptions = computed(() => this.selectable(this.options.sampleTypes.hasValue() ? this.options.sampleTypes.value() : [], this.detail()?.sampleTypeId ?? null));

  /** A culture service lists the culture specimens first (two groups); every other type, or no specimen flagged, keeps one list. */
  protected readonly sampleTypeGroups = computed<SampleTypeGroups | null>(() => {
    if (this.type() !== 'Culture') {
      return null;
    }

    const options = this.sampleTypeOptions();
    const cultures = options.filter((option) => option.forCulture === true);
    return cultures.length === 0 ? null : { cultures, others: options.filter((option) => option.forCulture !== true) };
  });

  constructor() {
    void this.i18n.load('catalogue-a');
    this.options.refresh();   // the options outlive the page: re-read on open (UAT D-14)

    // every id in the address bar opens its own record, also when the router reuses the page (UAT B-05)
    effect(() => {
      const id = this.serviceId();
      untracked(() => this.open(id));
    });

    // read-only without the right to save (UAT B-12)
    effect(() => {
      this.canEdit();
      untracked(() => this.applyAccess());
    });

    // the loaded detail fills the form once per load
    effect(() => {
      const detail = this.detail();
      if (detail !== null) {
        untracked(() => this.fill(detail));
      }
    });

    // validators and members follow the type
    effect(() => {
      const type = this.type();
      untracked(() => this.applyType(type));
    });

    // the conditions of the chosen sample type
    effect(() => {
      const sampleTypeId = this.sampleTypeId();
      untracked(() => void this.loadConditions(sampleTypeId));
    });

    // the picker searches the kind the type holds
    effect(() => {
      const kind = this.memberKind();
      untracked(() => {
        if (kind !== null) {
          this.facade.search(kind, '');
        }
      });
    });
  }

  protected optionText(option: CatalogueOption): string {
    return optionLabel(option, this.i18n.lang()) + (option.isActive ? '' : ` (${this.i18n.t('catalogue-a.inactive')})`);
  }

  protected conditionText(condition: SampleConditionView): string {
    return `${condition.code} · ${localizedPick({ en: condition.nameEn, ar: condition.nameAr }, this.i18n.lang())}`;
  }

  protected typeLabel(type: string): string {
    return this.i18n.has(`catalogue-a.serviceType.${type}`) ? this.i18n.t(`catalogue-a.serviceType.${type}`) : this.i18n.t(`catalogue-a.testType.${type}`);
  }

  protected memberName(member: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: member.nameEn, ar: member.nameAr }, this.i18n.lang());
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problem === null ? this.i18n.t('shared.error.generic') : problemMessage(this.i18n, 'catalogue-a', problem);
  }

  protected onSearch(search: string): void {
    const kind = this.memberKind();
    if (kind !== null) {
      this.facade.search(kind, search);
    }
  }

  protected addMember(candidate: MemberCandidate): void {
    if (!this.canEdit()) {
      return;
    }

    this.members.update((rows) => [
      ...rows,
      { kind: candidate.kind, memberId: candidate.id, code: candidate.code, nameEn: candidate.nameEn, nameAr: candidate.nameAr, memberType: candidate.type, isActive: true, inSum: false, visible: true },
    ]);
    this.membersDirty.set(true);
  }

  protected removeMember(index: number): void {
    if (!this.canEdit()) {
      return;
    }

    this.members.update((rows) => rows.filter((_, position) => position !== index));
    this.membersDirty.set(true);
  }

  protected moveMember(index: number, offset: -1 | 1): void {
    if (!this.canEdit()) {
      return;
    }

    const target = index + offset;
    this.members.update((rows) => {
      const first = rows[index];
      const second = rows[target];
      if (first === undefined || second === undefined) {
        return rows;
      }

      const next = [...rows];
      next[index] = second;
      next[target] = first;
      return next;
    });
    this.membersDirty.set(true);
  }

  protected toggleMember(index: number, flag: 'inSum' | 'visible', event: Event): void {
    if (!this.canEdit()) {
      return;
    }

    const checked = (event.target as HTMLInputElement).checked;
    this.members.update((rows) => rows.map((row, position) => (position === index ? { ...row, [flag]: checked } : row)));
    this.membersDirty.set(true);
  }

  async save(): Promise<void> {
    const id = this.serviceId();
    if (this.saving() || !this.canEdit() || (id !== null && this.detail() === null)) {
      return;   // nothing to save for a viewer, nor before the record in the address bar is loaded
    }

    if (!markAllTouched(this.form)) {
      this.activeTab.set('general');
      this.blocked.set(true);
      afterNextRender({ read: () => this.focusFirstInvalid() }, { injector: this.injector });
      return;
    }

    this.blocked.set(false);
    this.saving.set(true);
    this.generalProblem.set(null);
    this.membersProblem.set(null);
    try {
      if (id === null) {
        await this.create();
      } else {
        await this.update(id);
      }
    } finally {
      this.saving.set(false);
    }
  }

  /** The first marked field of the general data, in document order. */
  private focusFirstInvalid(): void {
    const selector = ['input', 'select', 'textarea'].map((tag) => `form.editor ${tag}.ng-invalid:not([disabled])`).join(', ');
    this.host.querySelector<HTMLElement>(selector)?.focus();
  }

  protected async toggleActive(): Promise<void> {
    const detail = this.detail();
    if (detail === null) {
      return;
    }

    const key = detail.isActive ? 'confirmDeactivate' : 'confirmActivate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`catalogue-a.${key}.title`), message: this.i18n.tf(`catalogue-a.${key}.message`, { code: detail.code }), danger: detail.isActive });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.setActive.execute({ id: detail.id, active: !detail.isActive })) !== null) {
      this.toast.success(this.i18n.t(detail.isActive ? 'catalogue-a.deactivated' : 'catalogue-a.activated'));
    } else {
      this.toast.error(this.problemText(this.facade.setActive.problem()));
    }
  }

  protected back(): void {
    void this.router.navigate(['..'], { relativeTo: this.route });
  }

  private async create(): Promise<void> {
    const raw = this.form.getRawValue();
    const created = await this.facade.create.execute({ code: raw.code.trim(), type: raw.type, ...this.general(), members: this.memberItems() });
    if (created === null) {
      this.showProblem(this.facade.create.problem(), 'general');
      return;
    }

    this.toast.success(this.i18n.t('catalogue-a.services.created'));
    this.membersDirty.set(false);
    this.form.markAsPristine();
    await this.router.navigate(['..', created.id], { relativeTo: this.route, replaceUrl: true });
  }

  private async update(id: string): Promise<void> {
    let changed = false;
    if (this.form.dirty) {
      const done = await this.facade.update.execute({ id, request: { ...this.general(), type: this.form.getRawValue().type } });
      if (done === null) {
        this.showProblem(this.facade.update.problem(), 'general');
        return;
      }

      this.form.markAsPristine();
      changed = true;
    }

    if (this.membersDirty()) {
      const done = await this.facade.replaceMembers.execute({ id, members: this.memberItems() });
      if (done === null) {
        this.showProblem(this.facade.replaceMembers.problem(), 'members');
        return;
      }

      this.membersDirty.set(false);
      changed = true;
    }

    this.toast.success(this.i18n.t(changed ? 'catalogue-a.saved' : 'catalogue-a.nothingToSave'));
    if (changed) {
      this.facade.detail.reload();
    }
  }

  private showProblem(problem: ProblemDetails | null, tab: 'general' | 'members'): void {
    if (problem === null) {
      return;
    }

    if (problem.code === ProblemCodes.validationFailed) {
      problemToFormErrors(problem, this.form);
    }

    const membersCodes = ['CAT-021', 'CAT-022', 'CAT-023', 'CAT-024', 'CAT-025', 'CAT-026', 'CAT-027', 'CAT-036'];
    const onMembers = tab === 'members' || membersCodes.includes(problem.code);
    (onMembers ? this.membersProblem : this.generalProblem).set(problem);
    this.activeTab.set(onMembers ? 'members' : 'general');
  }

  private general(): ServiceGeneral {
    const raw = this.form.getRawValue();
    const carriesSample = serviceCarriesSample(raw.type);
    return {
      shortName: raw.shortName.trim(),
      shortcutNo: numberOrNull(raw.shortcutNo),
      nameEn: raw.name.en.trim(),
      nameAr: raw.name.ar.trim(),
      reportName: textOrNull(raw.reportName),
      loinc: textOrNull(raw.loinc),
      cpt: textOrNull(raw.cpt),
      clinicalGroupId: raw.clinicalGroupId,
      sampleTypeId: carriesSample ? raw.sampleTypeId : null,
      sampleConditionId: carriesSample ? raw.sampleConditionId : null,
      header: textOrNull(raw.header),
      aliases: raw.aliases.split(/[,\n،]/).map((alias) => alias.trim()).filter((alias) => alias !== ''),
      precaution: textOrNull(raw.precaution),
      gender: raw.gender,
      turnaroundHours: numberOrNull(raw.turnaroundHours) ?? 0,
      labelCount: numberOrNull(raw.labelCount) ?? 0,
      printAlone: raw.printAlone,
      isOrderable: raw.isOrderable,
      showOnWeb: raw.showOnWeb,
      profileSum: raw.type === 'Profile' ? numberOrNull(raw.profileSum) : null,
    };
  }

  private memberItems(): readonly ServiceMemberItem[] {
    return this.members().map((member, index) => ({ kind: member.kind, memberId: member.memberId, order: index + 1, inSum: member.kind === 'Test' && member.inSum, visible: member.visible }));
  }

  private fill(detail: ServiceDetail): void {
    const general = serviceGeneralOf(detail);
    this.form.reset({
      code: detail.code,
      type: detail.type,
      shortName: general.shortName,
      shortcutNo: general.shortcutNo,
      name: { en: general.nameEn, ar: general.nameAr },
      reportName: general.reportName ?? '',
      loinc: general.loinc ?? '',
      cpt: general.cpt ?? '',
      clinicalGroupId: general.clinicalGroupId,
      sampleTypeId: general.sampleTypeId,
      sampleConditionId: general.sampleConditionId,
      header: general.header ?? '',
      aliases: general.aliases.join(', '),
      precaution: general.precaution ?? '',
      gender: general.gender,
      turnaroundHours: general.turnaroundHours,
      labelCount: general.labelCount,
      printAlone: general.printAlone,
      isOrderable: general.isOrderable,
      showOnWeb: general.showOnWeb,
      profileSum: general.profileSum,
    });
    this.members.set(detail.members.map((member) => ({ ...member })));
    this.membersDirty.set(false);
    this.applyAccess();
  }

  /** Opens the record of the address bar: a new id clears the problems and refills the page from the server. */
  private open(id: string | null): void {
    this.generalProblem.set(null);
    this.membersProblem.set(null);
    this.activeTab.set('general');
    this.facade.load(id);
    this.applyAccess();
  }

  /** Every input follows the right to save; the code and the type never change once created (CAT-035). */
  private applyAccess(): void {
    if (!this.canEdit()) {
      this.form.disable({ emitEvent: false });
      return;
    }

    this.form.enable({ emitEvent: false });
    if (!this.isNew()) {
      this.form.controls.code.disable({ emitEvent: false });
      this.form.controls.type.disable({ emitEvent: false });
    }
  }

  private applyType(type: ServiceType): void {
    const group = this.form.controls.clinicalGroupId;
    group.setValidators(type === 'Additional' ? [] : [Validators.required]);
    group.updateValueAndValidity({ emitEvent: false });
    const sample = this.form.controls.sampleTypeId;
    sample.setValidators(serviceCarriesSample(type) ? [Validators.required] : []);
    sample.updateValueAndValidity({ emitEvent: false });

    // a new service that switches between test members and service members starts its member list again
    const kind = serviceMemberKind(type);
    if (this.isNew() && this.members().some((member) => member.kind !== kind)) {
      this.members.set([]);
    }
  }

  private async loadConditions(sampleTypeId: string | null): Promise<void> {
    const conditions = sampleTypeId === null ? [] : await this.options.conditionsOf(sampleTypeId);
    this.conditions.set(conditions);
    const current = this.form.controls.sampleConditionId.value;
    if (current !== null && !conditions.some((condition) => condition.id === current)) {
      this.form.controls.sampleConditionId.setValue(null);
    }
  }

  /** Active options plus the one currently referenced (it may have been deactivated since, BR-24). */
  private selectable(options: readonly CatalogueOption[], current: string | null): readonly CatalogueOption[] {
    return options.filter((option) => option.isActive || option.value === current);
  }
}
