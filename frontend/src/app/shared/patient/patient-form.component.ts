import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { type AbstractControl, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, type ValidatorFn, Validators } from '@angular/forms';

import {
  type ConditionOption,
  type CountryOption,
  type GovernorateOption,
  PATIENT_GENDERS,
  PATIENT_TITLES,
  PATIENTS_PERMISSIONS,
  type PatientAge,
  type PatientDetail,
  type PatientFormSettings,
  type PatientGender,
  type PatientTitle,
  type RegisterPatientRequest,
  type SimilarPatient,
} from '../../core/api/patients.contracts';
import { ProblemCodes, type ProblemDetails } from '../../core/api/problem-details';
import { I18nService } from '../../core/i18n/i18n.service';
import { PermissionsService } from '../../core/permissions/permissions.service';
import { markAllTouched } from '../forms/mark-all-touched';
import { problemMessage } from '../forms/problem-text';
import { SERVER_ERROR_KEY, clearServerErrors, problemToFormErrors } from '../forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../forms/save-shortcut.directive';
import { requiredText } from '../forms/validators';
import { BadgeComponent } from '../ui/badge/badge.component';
import { CanDirective } from '../ui/can.directive';
import { FormFieldComponent } from '../ui/form-field/form-field.component';
import { IconComponent } from '../ui/icon/icon.component';
import { type NationalIdFacts, readEgyptianNationalId } from './egyptian-national-id';
import { ageBetween, ageText, birthDateFromAge, defaultGender, isValidAge, isValidMobile, localDigits, mobileForStorage, parseIso, todayIso } from './patient-demographics';

/** What the user is typing in a new file, for the similar-patient lookup (BR-01). */
export interface TypedIdentity {
  readonly name: string;
  readonly mobile: string;
}

const EARLIEST_BIRTH = '1900-01-01';
const SIMILAR_DEBOUNCE_MS = 350;

/** Domain refusals that belong to one field (intake §7); the rest show above the form. */
const FIELD_OF_CODE: Readonly<Record<string, string>> = {
  'PAT-001': 'fullName',
  'PAT-002': 'nameAr',
  'PAT-005': 'dateOfBirth',
  'PAT-006': 'ageYears',
  'PAT-008': 'mobile',
  'PAT-009': 'mobile',
  'PAT-010': 'nationalId',
  'PAT-011': 'nationalId',
  'PAT-012': 'email',
};

/** ISO code of Egypt in the platform's country list (the only national id the form reads). */
const EGYPT = 'EG';

/** The fields an Egyptian national id can fill. */
type IdField = 'dob' | 'gender' | 'governorate';

/** The optional fields folded under "More details". */
const DETAIL_FIELDS = ['nationality', 'email', 'governorateId', 'city', 'address', 'comments'] as const;

/** The identity fields a result-status lock freezes (PAT-032; the server's list): everything else is contact data and stays editable. */
const IDENTITY_FIELDS = ['title', 'fullName', 'nameAr', 'gender', 'dateOfBirth', 'ageYears', 'ageMonths', 'ageDays', 'patientConditionId', 'nationalId', 'nationality'] as const;

/**
 * The patient create/edit form (CP-06 patient data, CP-11), in four groups — identity (title, both names, national id),
 * sex and age (gender switch, date of birth or age on one line, condition), contact (mobile with its country) and the
 * optional details folded until needed. An Egyptian national id fills the empty date of birth, gender and governorate of
 * birth (`egyptian-national-id.ts`) and lists what it contradicts; title proposes the gender, date of birth ↔ age sync in both
 * directions (the last one typed is what is saved), mobile digits checked against the chosen country's length when the
 * platform publishes it (8–15 otherwise), mandatory mobile / national id from the tenant parameters, VIP only for
 * `patients.viewConfidential`, and the similar-patient warning while a new file is typed. Presentational: inputs in,
 * `save` / `typed` / `useExisting` out; the host puts the buttons where it wants and calls `submit()`. Moves to
 * `shared/` with the patient picker.
 */
@Component({
  selector: 'app-patient-form',
  imports: [ReactiveFormsModule, SaveShortcutDirective, FormFieldComponent, CanDirective, BadgeComponent, IconComponent],
  templateUrl: './patient-form.component.html',
  styleUrl: './patient-form.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientFormComponent {
  protected readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);
  private typedTimer: ReturnType<typeof setTimeout> | null = null;

  /** The file being edited; `null` for a new one. */
  readonly patient = input<PatientDetail | null>(null);
  readonly settings = input<PatientFormSettings | null>(null);
  readonly countries = input<readonly CountryOption[]>([]);
  readonly governorates = input<readonly GovernorateOption[]>([]);
  readonly conditions = input<readonly ConditionOption[]>([]);
  /** Candidates to warn about (empty when the tenant disabled the warning). */
  readonly similar = input<readonly SimilarPatient[]>([]);
  readonly problem = input<ProblemDetails | null>(null);
  readonly pending = input(false);
  /** Prefills the name of a new file (the picker passes what the clerk searched). */
  readonly initialName = input('');
  /**
   * The identity fields are read-only (change request 2026-10-13 item 1): a result of the visit has passed the status the user's
   * `DemographicsEditUpTo` right allows (`PatientDemographicsLock.canEditIdentity` false). Contact fields stay editable; the
   * saved request still carries the unchanged identity, so the server sees no identity change.
   */
  readonly identityLocked = input(false);
  /** The sentence that says why (the status reached and the user's limit), shown above the identity group. */
  readonly lockHint = input<string | null>(null);

  readonly save = output<RegisterPatientRequest>();
  readonly typed = output<TypedIdentity>();
  readonly useExisting = output<SimilarPatient>();

  protected readonly titles = PATIENT_TITLES;
  protected readonly genders = PATIENT_GENDERS;
  protected readonly permissionKeys = PATIENTS_PERMISSIONS;
  protected readonly today = todayIso();
  protected readonly isEdit = computed(() => this.patient() !== null);

  /** Which of the two the user gave last: that one is saved, the other is derived. */
  protected readonly birthSource = signal<'dob' | 'age' | null>(null);
  /** The country the clerk picked for the mobile ('' = "Any"); null until they pick one. */
  private readonly chosenMobileCountry = signal<string | null>(null);
  /**
   * The mobile country: the clerk's choice, else the laboratory's country (`settings.countryCode`, the client record's
   * country; validation v0.5.0 F-08) when the platform publishes its mobile length, else "Any". So a 10-digit Egyptian
   * number is stored in its trunk form without the clerk choosing Egypt.
   */
  protected readonly mobileCountry = computed(() => {
    const chosen = this.chosenMobileCountry();
    if (chosen !== null) {
      return chosen;
    }

    const laboratory = this.settings()?.countryCode ?? null;
    return laboratory !== null && this.mobileCountries().some((country) => country.code === laboratory) ? laboratory : '';
  });
  /** The folded "More details" section: open while the clerk wants it, when it holds data or when a field in it is wrong. */
  protected readonly moreOpen = signal(false);
  /** A save was refused on the client: the notice above the form says so until the next attempt (validation round 4). */
  protected readonly blocked = signal(false);

  protected readonly mobileLength = computed(() => this.countries().find((country) => country.code === this.mobileCountry())?.mobileLength ?? null);
  protected readonly mobileCountries = computed(() => this.countries().filter((country) => country.mobileLength !== null));
  protected readonly generalProblem = computed(() => {
    const problem = this.problem();
    if (problem === null || problem.code === ProblemCodes.validationFailed || problem.code in FIELD_OF_CODE) {
      return null;
    }

    return problemMessage(this.i18n, 'patients', problem);
  });

  protected readonly form = new FormGroup(
    {
      title: new FormControl<PatientTitle>('Mr', { nonNullable: true }),
      fullName: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
      nameAr: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
      gender: new FormControl<PatientGender | ''>('Male', { nonNullable: true, validators: [Validators.required] }),
      dateOfBirth: new FormControl('', { nonNullable: true, validators: [(control) => this.validateBirth(control)] }),
      ageYears: new FormControl<number | null>(null, { validators: [Validators.min(0), Validators.max(150), integer] }),
      ageMonths: new FormControl<number | null>(null, { validators: [Validators.min(0), Validators.max(11), integer] }),
      ageDays: new FormControl<number | null>(null, { validators: [Validators.min(0), Validators.max(31), integer] }),
      patientConditionId: new FormControl('', { nonNullable: true }),
      mobile: new FormControl('', { nonNullable: true, validators: [(control) => this.validateMobile(control)] }),
      nationalId: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(32), Validators.pattern(/^[\sA-Za-z0-9\u0660-\u0669-]*$/)] }),
      nationality: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
      email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(254)] }),
      governorateId: new FormControl('', { nonNullable: true }),
      city: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
      address: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
      comments: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] }),
      isVip: new FormControl(false, { nonNullable: true }),
    },
    { validators: [(group) => this.validateBirthOrAge(group)] },
  );

  /** The form value as a signal, so the national-id checks follow every change. */
  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** Fields whose current value came from the national id: they follow a corrected id, a user edit releases them. */
  private readonly fromId = new Set<IdField>();
  protected readonly idFilled = signal<readonly IdField[]>([]);

  /** What the typed national id says, when it is an Egyptian one and the nationality is Egyptian or not given. */
  protected readonly nationalIdFacts = computed<NationalIdFacts | null>(() => {
    const value = this.formValue();
    const nationality = value.nationality ?? '';
    return nationality === '' || nationality === EGYPT ? readEgyptianNationalId(value.nationalId ?? '', this.today) : null;
  });

  protected readonly nationalIdHint = computed(() => {
    const fields = this.idFilled();
    if (fields.length === 0 || this.nationalIdFacts() === null) {
      return null;
    }

    const separator = this.i18n.lang() === 'ar' ? '، ' : ', ';
    return this.i18n.tf('patients.nationalId.filled', { fields: fields.map((field) => this.i18n.t(`patients.nationalId.field.${field}`)).join(separator) });
  });

  /** What the clerk entered that the national id contradicts (never overwritten: shown so it can be checked). */
  protected readonly nationalIdConflicts = computed<readonly string[]>(() => {
    const facts = this.nationalIdFacts();
    const value = this.formValue();
    const filled = new Set(this.idFilled());
    if (facts === null) {
      return [];
    }

    const conflicts: string[] = [];
    if (!filled.has('dob') && this.birthSource() === 'dob' && value.dateOfBirth && value.dateOfBirth !== facts.dateOfBirth) {
      conflicts.push(this.i18n.tf('patients.nationalId.birthDiffers', { date: facts.dateOfBirth }));
    }

    const gender = this.i18n.t(`patients.gender.${facts.gender}`);
    if (value.gender && value.gender !== facts.gender) {
      conflicts.push(this.i18n.tf('patients.nationalId.genderDiffers', { gender }));
    } else {
      const titleGender = value.title ? defaultGender(value.title) : null;
      if (this.form.controls.title.dirty && titleGender !== null && titleGender !== facts.gender) {
        conflicts.push(this.i18n.tf('patients.nationalId.titleDiffers', { gender }));
      }
    }

    return conflicts;
  });

  constructor() {
    void this.i18n.load('patients');

    effect(() => {
      const patient = this.patient();
      const name = this.initialName();
      untracked(() => (patient ? this.fill(patient) : this.reset(name)));
    });

    // Tenant parameters make the mobile / national id mandatory (CP-57).
    effect(() => {
      const settings = this.settings();
      untracked(() => {
        setRequired(this.form.controls.mobile, settings?.mandatoryMobile ?? false, (control) => this.validateMobile(control));
        setRequired(this.form.controls.nationalId, settings?.mandatoryNationalId ?? false, Validators.maxLength(32), Validators.pattern(/^[\sA-Za-z0-9\u0660-\u0669-]*$/));
      });
    });

    // The lock freezes the identity controls; `getRawValue()` keeps their values in the saved request.
    effect(() => {
      const locked = this.identityLocked();
      untracked(() => {
        for (const field of IDENTITY_FIELDS) {
          const control = this.form.controls[field];
          if (locked && control.enabled) {
            control.disable({ emitEvent: false });
          } else if (!locked && control.disabled) {
            control.enable({ emitEvent: false });
          }
        }

        this.form.updateValueAndValidity({ emitEvent: false });
      });
    });

    // The mobile country decides the mobile rule: re-check the number when the laboratory's default arrives (F-08).
    effect(() => {
      this.mobileCountry();
      untracked(() => this.form.controls.mobile.updateValueAndValidity());
    });

    // Server refusals land on their field when they name one (VALIDATION-400 or a field code).
    effect(() => {
      const problem = this.problem();
      untracked(() => {
        if (problem === null) {
          return;
        }

        if (problem.code === ProblemCodes.validationFailed) {
          problemToFormErrors(problem, this.form);
          this.revealInvalidDetails();
          return;
        }

        const field = FIELD_OF_CODE[problem.code];
        const control = field ? this.form.get(field) : null;
        if (control) {
          control.setErrors({ ...(control.errors ?? {}), [SERVER_ERROR_KEY]: problemMessage(this.i18n, 'patients', problem) });
          control.markAsTouched();
        }

        this.revealInvalidDetails();
      });
    });

    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  /** Validates and emits `save` with the request shape; returns false when the form is invalid. */
  submit(): boolean {
    for (const control of Object.values(this.form.controls)) {
      clearServerErrors(control);
    }

    if (this.pending()) {
      return false;
    }

    if (!markAllTouched(this.form)) {
      this.revealInvalidDetails();
      this.blocked.set(true);
      afterNextRender({ read: () => this.focusFirstInvalid() }, { injector: this.injector });
      return false;
    }

    this.blocked.set(false);

    const value = this.form.getRawValue();
    const source = this.birthSource();
    const age = this.typedAge();
    this.save.emit({
      title: value.title,
      fullName: value.fullName.trim(),
      nameAr: blankToNull(value.nameAr),
      gender: value.gender === '' ? 'Male' : value.gender,
      dateOfBirth: source === 'age' ? null : blankToNull(value.dateOfBirth),
      age: source === 'age' && age !== null ? age : null,
      patientConditionId: blankToNull(value.patientConditionId),
      mobile: blankToNull(mobileForStorage(value.mobile, this.mobileLength())),   // A-09: with or without the trunk 0, stored one way
      nationalId: blankToNull(value.nationalId),
      nationality: blankToNull(value.nationality),
      email: blankToNull(value.email),
      governorateId: blankToNull(value.governorateId),
      city: blankToNull(value.city),
      address: blankToNull(value.address),
      comments: blankToNull(value.comments),
      isVip: this.permissions.can(PATIENTS_PERMISSIONS.viewConfidential) ? value.isVip : null,
    });
    return true;
  }

  /**
   * An Egyptian national id fills the date of birth, the gender (a new file whose gender the clerk did not pick) and the
   * governorate of birth when they are empty, or when they came from the id before (a corrected id). Nothing the clerk
   * entered is overwritten; a contradiction is listed instead (`nationalIdConflicts`).
   */
  protected onNationalIdInput(event: Event): void {
    const nationality = this.form.controls.nationality.value;
    const raw = (event.target as HTMLInputElement).value;
    const facts = nationality === '' || nationality === EGYPT ? readEgyptianNationalId(raw, this.today) : null;
    if (facts === null) {
      return;   // values taken from an earlier valid id stay, and follow it once the id is valid again
    }

    const controls = this.form.controls;
    if (this.fromId.has('dob') || (controls.dateOfBirth.value === '' && this.typedAge() === null)) {
      controls.dateOfBirth.setValue(facts.dateOfBirth);
      this.onBirthInput();
      this.fromId.add('dob');
    }

    if (this.fromId.has('gender') || (!this.isEdit() && !controls.gender.dirty)) {
      controls.gender.setValue(facts.gender);
      this.fromId.add('gender');
      // the form's default title ("Mr") was never chosen: give it the id's gender (a title the clerk picked is kept and flagged)
      const titleGender = defaultGender(controls.title.value);
      if (!controls.title.dirty && titleGender !== null && titleGender !== facts.gender) {
        controls.title.setValue(facts.gender === 'Male' ? 'Mr' : 'Ms');
      }
    }

    const governorate = this.governorateOf(facts);
    if (governorate !== null && (this.fromId.has('governorate') || controls.governorateId.value === '')) {
      controls.governorateId.setValue(governorate.id);
      this.fromId.add('governorate');
    } else if (governorate === null && this.fromId.has('governorate')) {
      controls.governorateId.setValue('');
      this.fromId.delete('governorate');
    }

    this.idFilled.set([...this.fromId]);
  }

  /** The clerk changed one of the fields the id filled: it is theirs from now on. */
  protected birthEdited(): void {
    this.releaseFromId('dob');
  }

  protected genderEdited(): void {
    this.releaseFromId('gender');
  }

  protected governorateEdited(): void {
    this.releaseFromId('governorate');
  }

  private releaseFromId(field: IdField): void {
    if (this.fromId.delete(field)) {
      this.idFilled.set([...this.fromId]);
    }
  }

  /** The platform's Egyptian governorate named by the id (none when born abroad or not in the list). */
  private governorateOf(facts: NationalIdFacts): GovernorateOption | null {
    const name = facts.governorateNameEn?.toLowerCase();
    if (name === undefined) {
      return null;
    }

    const egypt = this.countries().find((country) => country.code === EGYPT)?.id ?? null;
    return this.governorates().find((option) => option.nameEn.toLowerCase() === name && (egypt === null || !option.countryId || option.countryId === egypt)) ?? null;
  }

  protected onTitleChange(): void {
    if (this.fromId.has('gender')) {
      return;   // the national id is surer than the title's guess
    }

    const gender = defaultGender(this.form.controls.title.value);
    if (gender !== null) {
      this.form.controls.gender.setValue(gender);
    }
  }

  protected onBirthInput(): void {
    const birth = this.form.controls.dateOfBirth.value;
    if (birth === '') {
      this.birthSource.set(this.typedAge() !== null ? 'age' : null);
      return;
    }

    this.birthSource.set('dob');
    if (parseIso(birth) !== null && birth <= this.today) {
      const age = ageBetween(birth, this.today);
      this.form.patchValue({ ageYears: age.years, ageMonths: age.months, ageDays: age.days }, { emitEvent: false });
    }

    this.form.updateValueAndValidity();
  }

  protected onAgeInput(): void {
    const age = this.typedAge();
    if (age === null) {
      this.birthSource.set(this.form.controls.dateOfBirth.value === '' ? null : 'dob');
      this.form.updateValueAndValidity();
      return;
    }

    this.birthSource.set('age');
    const birth = birthDateFromAge(age, this.today);
    if (birth !== null) {
      this.form.controls.dateOfBirth.setValue(birth, { emitEvent: false });
    }

    this.form.updateValueAndValidity();
  }

  protected onIdentityInput(): void {
    if (this.isEdit()) {
      return;
    }

    this.clearTimer();
    this.typedTimer = setTimeout(() => {
      this.typedTimer = null;
      this.typed.emit({ name: this.form.controls.fullName.value.trim(), mobile: mobileForStorage(this.form.controls.mobile.value, this.mobileLength()) });
    }, SIMILAR_DEBOUNCE_MS);
  }

  protected onMoreToggle(event: Event): void {
    this.moreOpen.set((event.target as HTMLDetailsElement).open);
  }

  /** The first marked field in document order; the date of birth when only "date of birth or age" is missing. */
  private focusFirstInvalid(): void {
    const selector = ['input', 'select', 'textarea'].map((tag) => `${tag}.ng-invalid:not([disabled])`).join(', ');
    const target = this.host.querySelector<HTMLElement>(selector) ?? (this.form.hasError('birthOrAge') ? this.host.querySelector<HTMLElement>('[formcontrolname="dateOfBirth"]') : null);
    target?.focus();
  }

  /** Opens "More details" when one of its fields holds an error, so the clerk sees why the save was refused. */
  private revealInvalidDetails(): void {
    if (DETAIL_FIELDS.some((field) => this.form.controls[field].invalid)) {
      this.moreOpen.set(true);
    }
  }

  protected onMobileCountryChange(event: Event): void {
    this.chosenMobileCountry.set((event.target as HTMLSelectElement).value);
    this.form.controls.mobile.updateValueAndValidity();
  }

  /** A number in the page language's digits (validation v0.5.0 F-09). */
  protected num(value: number): string {
    return localDigits(value, this.i18n.lang());
  }

  protected optionName(option: { readonly nameEn: string; readonly nameAr: string }): string {
    return this.i18n.lang() === 'ar' ? option.nameAr : option.nameEn;
  }

  protected ageText(age: PatientAge): string {
    return ageText(age, this.i18n.lang(), (key, params) => this.i18n.tf(key, params));
  }

  protected matchLabel(candidate: SimilarPatient): string {
    return this.i18n.t(`patients.match.${candidate.match}`);
  }

  /** The age the user typed, when complete enough (years given, the rest default to 0) and valid. */
  private typedAge(): PatientAge | null {
    const { ageYears, ageMonths, ageDays } = this.form.getRawValue();
    if (ageYears === null && ageMonths === null && ageDays === null) {
      return null;
    }

    const age = { years: ageYears ?? 0, months: ageMonths ?? 0, days: ageDays ?? 0 };
    return isValidAge(age) ? age : null;
  }

  private fill(patient: PatientDetail): void {
    const estimated = patient.dateOfBirthIsEstimated;
    this.chosenMobileCountry.set(null);
    this.form.reset({
      title: patient.title,
      fullName: patient.fullName,
      nameAr: patient.nameAr ?? '',
      gender: patient.gender,
      dateOfBirth: patient.dateOfBirth ?? (estimated ? (birthDateFromAge(patient.age, this.today) ?? '') : ''),
      ageYears: patient.age.years,
      ageMonths: patient.age.months,
      ageDays: patient.age.days,
      patientConditionId: patient.patientConditionId ?? '',
      mobile: patient.mobile ?? '',
      nationalId: patient.nationalId ?? '',
      nationality: patient.nationality ?? '',
      email: patient.email ?? '',
      governorateId: patient.governorateId ?? '',
      city: patient.city ?? '',
      address: patient.address ?? '',
      comments: patient.comments ?? '',
      isVip: patient.isVip ?? false,
    });
    this.birthSource.set(estimated ? 'age' : 'dob');
    this.moreOpen.set(DETAIL_FIELDS.some((field) => this.form.controls[field].value.trim() !== ''));
    this.blocked.set(false);
    this.fromId.clear();
    this.idFilled.set([]);
  }

  private reset(name: string): void {
    this.form.reset();
    this.chosenMobileCountry.set(null);
    this.form.controls.fullName.setValue(name);
    this.birthSource.set(null);
    this.moreOpen.set(false);
    this.blocked.set(false);
    this.fromId.clear();
    this.idFilled.set([]);
  }

  private validateBirth(control: AbstractControl): ValidationErrors | null {
    const value: unknown = control.value;
    if (typeof value !== 'string' || value === '') {
      return null;
    }

    if (parseIso(value) === null || value < EARLIEST_BIRTH || value > todayIso()) {
      return { [SERVER_ERROR_KEY]: this.i18n.t('patients.validation.dateOfBirth') };
    }

    return null;
  }

  private validateMobile(control: AbstractControl): ValidationErrors | null {
    const value: unknown = control.value;
    if (typeof value !== 'string') {
      return null;
    }

    return value.trim() === '' || isValidMobile(value, this.mobileLength()) ? null : { mobile: true };
  }

  private validateBirthOrAge(group: AbstractControl): ValidationErrors | null {
    const value = (group as FormGroup).getRawValue() as { dateOfBirth: string; ageYears: number | null };
    return value.dateOfBirth === '' && value.ageYears === null ? { birthOrAge: true } : null;
  }

  private clearTimer(): void {
    if (this.typedTimer !== null) {
      clearTimeout(this.typedTimer);
      this.typedTimer = null;
    }
  }
}

const integer: ValidatorFn = (control) => (control.value === null || Number.isInteger(control.value) ? null : { pattern: true });

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

function setRequired(control: AbstractControl, required: boolean, ...validators: ValidatorFn[]): void {
  control.setValidators(required ? [requiredText, ...validators] : validators);
  control.updateValueAndValidity({ emitEvent: false });
}
