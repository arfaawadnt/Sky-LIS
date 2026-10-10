// Copied from features/patients (MODULES/brief: features never import each other) and bound to the registration dictionary
// (registration.patient.*); replace with the shared picker when the integrator moves it to shared/.
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
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
} from '../../../core/api/patients.contracts';
import { ProblemCodes, type ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SERVER_ERROR_KEY, clearServerErrors, problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ageBetween, ageParts, birthDateFromAge, defaultGender, isValidAge, isValidMobile, normalizeMobile, parseIso, todayIso } from './patient-demographics';

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

/**
 * The patient create/edit form (CP-06 patient data, CP-11): title proposes the gender, date of birth ↔ age sync in both
 * directions (the last one typed is what is saved), mobile digits checked against the chosen country's length when the
 * platform publishes it (8–15 otherwise), mandatory mobile / national id from the tenant parameters, VIP only for
 * `registration.patient.viewConfidential`, and the similar-patient warning while a new file is typed. Presentational: inputs in,
 * `save` / `typed` / `useExisting` out; the host puts the buttons where it wants and calls `submit()`. Moves to
 * `shared/` with the patient picker.
 */
@Component({
  selector: 'app-reg-patient-form',
  imports: [ReactiveFormsModule, SaveShortcutDirective, FormFieldComponent, CanDirective, BadgeComponent, IconComponent],
  templateUrl: './patient-form.component.html',
  styleUrl: './patient-form.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientFormComponent {
  protected readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionsService);
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
  private readonly mobileCountry = signal('');

  protected readonly mobileLength = computed(() => this.countries().find((country) => country.code === this.mobileCountry())?.mobileLength ?? null);
  protected readonly mobileCountries = computed(() => this.countries().filter((country) => country.mobileLength !== null));
  protected readonly generalProblem = computed(() => {
    const problem = this.problem();
    if (problem === null || problem.code === ProblemCodes.validationFailed || problem.code in FIELD_OF_CODE) {
      return null;
    }

    return problemMessage(this.i18n, 'registration', problem);
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

  constructor() {
    void this.i18n.load('registration');

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

    // Server refusals land on their field when they name one (VALIDATION-400 or a field code).
    effect(() => {
      const problem = this.problem();
      untracked(() => {
        if (problem === null) {
          return;
        }

        if (problem.code === ProblemCodes.validationFailed) {
          problemToFormErrors(problem, this.form);
          return;
        }

        const field = FIELD_OF_CODE[problem.code];
        const control = field ? this.form.get(field) : null;
        if (control) {
          control.setErrors({ ...(control.errors ?? {}), [SERVER_ERROR_KEY]: problemMessage(this.i18n, 'registration', problem) });
          control.markAsTouched();
        }
      });
    });

    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  /** Validates and emits `save` with the request shape; returns false when the form is invalid. */
  submit(): boolean {
    for (const control of Object.values(this.form.controls)) {
      clearServerErrors(control);
    }

    if (this.pending() || !markAllTouched(this.form)) {
      return false;
    }

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
      mobile: blankToNull(normalizeMobile(value.mobile)),
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

  protected onTitleChange(): void {
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
      this.typed.emit({ name: this.form.controls.fullName.value.trim(), mobile: normalizeMobile(this.form.controls.mobile.value) });
    }, SIMILAR_DEBOUNCE_MS);
  }

  protected onMobileCountryChange(event: Event): void {
    this.mobileCountry.set((event.target as HTMLSelectElement).value);
    this.form.controls.mobile.updateValueAndValidity();
  }

  protected optionName(option: { readonly nameEn: string; readonly nameAr: string }): string {
    return this.i18n.lang() === 'ar' ? option.nameAr : option.nameEn;
  }

  protected ageText(age: PatientAge): string {
    return ageParts(age)
      .map((part) => this.i18n.tf(`registration.patient.age.${part.unit}`, { value: part.value }))
      .join(' ');
  }

  protected matchLabel(candidate: SimilarPatient): string {
    return this.i18n.t(`registration.patient.match.${candidate.match}`);
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
  }

  private reset(name: string): void {
    this.form.reset();
    this.form.controls.fullName.setValue(name);
    this.birthSource.set(null);
  }

  private validateBirth(control: AbstractControl): ValidationErrors | null {
    const value: unknown = control.value;
    if (typeof value !== 'string' || value === '') {
      return null;
    }

    if (parseIso(value) === null || value < EARLIEST_BIRTH || value > todayIso()) {
      return { [SERVER_ERROR_KEY]: this.i18n.t('registration.patient.validation.dateOfBirth') };
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
