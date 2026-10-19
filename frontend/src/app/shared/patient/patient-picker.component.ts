// Shared patient picker (Patients CP-11 and Registration CP-06 use the same file lookup and creation form). It reads the
// `patients` dictionary and the Patients contracts; it never imports a feature.
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, resource, signal, untracked, viewChild } from '@angular/core';

import { type CreatedPatient, PATIENTS_PERMISSIONS, type PatientListItem, type PatientSummary, type RegisterPatientRequest, type SimilarPatient } from '../../core/api/patients.contracts';
import { I18nService } from '../../core/i18n/i18n.service';
import { mutation } from '../../core/state/mutation';
import { BadgeComponent } from '../ui/badge/badge.component';
import { CanDirective } from '../ui/can.directive';
import { EmptyStateComponent } from '../ui/empty-state/empty-state.component';
import { IconComponent } from '../ui/icon/icon.component';
import { SearchBoxComponent } from '../ui/search-box/search-box.component';
import { PatientLookupApi } from './patient-lookup.api';
import { ageBetween, ageText, birthDateFromAge, toSummary, todayIso } from './patient-demographics';
import { PatientFormComponent, type TypedIdentity } from './patient-form.component';

const RESULT_LIMIT = 10;
const MIN_QUERY_LENGTH = 2;

/**
 * Patient search / create for registration screens (CP-06, WF-02 steps 1–2; REG-002): search by number, name, mobile
 * or national id; "New patient" opens the inline form with the similar-patient warning (BR-01) and "use this file";
 * saving registers the file (number assigned by the server) and emits it. Inputs/outputs only: it calls
 * {@link PatientLookupApi} and injects no facade, so the integrator can move it (with `patient-form`,
 * `patient-demographics` and `data/patient-lookup.api.ts`) to `shared/`.
 *
 * Usage: `<app-patient-picker [selected]="visit.patient()" (picked)="visit.setPatient($event)" (cleared)="visit.clearPatient()" />`
 */
@Component({
  selector: 'app-patient-picker',
  imports: [SearchBoxComponent, PatientFormComponent, BadgeComponent, CanDirective, EmptyStateComponent, IconComponent],
  templateUrl: './patient-picker.component.html',
  styleUrl: './patient-picker.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientPickerComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PatientLookupApi);
  private readonly createForm = viewChild(PatientFormComponent);

  /** The file currently chosen by the host, shown as a card with "Change". */
  readonly selected = input<PatientSummary | null>(null);
  /** Whether "New patient" is offered (registration modes that only reuse files pass false). */
  readonly allowCreate = input(true);

  /** A chosen existing file or a newly registered one. */
  readonly picked = output<PatientSummary>();
  /** The user asked to choose another file. */
  readonly cleared = output<void>();

  protected readonly permissions = PATIENTS_PERMISSIONS;
  protected readonly mode = signal<'search' | 'create'>('search');
  protected readonly query = signal('');
  private readonly typedIdentity = signal<TypedIdentity>({ name: '', mobile: '', secondPhone: '' });

  protected readonly results = resource({
    params: () => (this.query().length >= MIN_QUERY_LENGTH ? this.query() : undefined),
    loader: ({ params, abortSignal }) => this.api.search(params, RESULT_LIMIT, abortSignal),
  });

  protected readonly rows = computed<readonly PatientListItem[]>(() => (this.results.hasValue() ? this.results.value().items : []));

  /** What is in the box right now (every keystroke); `query` follows it after the debounce. */
  protected readonly typed = signal('');

  /**
   * The listed results belong to an older text: the box changed and the debounce or the search is still running. They
   * stay visible (no flicker) but cannot be picked (UAT A-09: a fast click chose the previous term's patient).
   */
  protected readonly stale = computed(() => this.typed().trim() !== this.query() || this.results.isLoading());

  protected readonly settings = resource({
    params: () => (this.mode() === 'create' ? true : undefined),
    loader: ({ abortSignal }) => this.api.formSettings(abortSignal),
  });

  protected readonly countries = resource({ params: () => (this.mode() === 'create' ? true : undefined), loader: () => this.api.countries() });
  protected readonly governorates = resource({ params: () => (this.mode() === 'create' ? true : undefined), loader: () => this.api.governorates() });
  protected readonly conditions = resource({ params: () => (this.mode() === 'create' ? true : undefined), loader: () => this.api.conditions() });

  protected readonly similar = resource({
    params: () => {
      const typed = this.typedIdentity();
      return this.mode() === 'create' && (typed.name.length >= MIN_QUERY_LENGTH || typed.mobile.length >= 8 || typed.secondPhone.length >= 8) ? typed : undefined;
    },
    loader: ({ params, abortSignal }) => this.api.similar(params, abortSignal),
  });

  protected readonly candidates = computed<readonly SimilarPatient[]>(() => {
    const response = this.similar.hasValue() ? this.similar.value() : null;
    return response?.warningEnabled ? response.candidates : [];
  });

  /** The name to prefill: what was searched, unless it was a number, a mobile or an id. */
  protected readonly initialName = computed(() => (/\d/.test(this.query()) ? '' : this.query()));

  protected readonly register = mutation((request: RegisterPatientRequest) => this.api.register(request));

  constructor() {
    void this.i18n.load('patients');

    // A chosen file ends the search. The search box is gone while the card shows, so when the host clears the choice
    // (a reset of the registration page after a save, "Change") the picker starts empty instead of listing the previous
    // result under an empty box, where one click re-selected that patient (validation R-01).
    effect(() => {
      if (this.selected() !== null) {
        untracked(() => this.resetSearch());
      }
    });
  }

  private resetSearch(): void {
    this.query.set('');
    this.typed.set('');
    this.mode.set('search');
  }

  protected onSearch(text: string): void {
    this.query.set(text.trim());
  }

  protected choose(item: PatientListItem): void {
    this.picked.emit(toSummary(item));
  }

  protected chooseSimilar(candidate: SimilarPatient): void {
    this.mode.set('search');
    this.picked.emit(toSummary(candidate, null));
  }

  protected startCreate(): void {
    this.register.reset();
    this.typedIdentity.set({ name: this.initialName(), mobile: '', secondPhone: '' });
    this.mode.set('create');
  }

  protected cancelCreate(): void {
    this.mode.set('search');
  }

  protected onTyped(identity: TypedIdentity): void {
    this.typedIdentity.set(identity);
  }

  protected saveNew(): void {
    this.createForm()?.submit();
  }

  /** Registers the file, then emits it as the server sees it; if that read fails the file exists anyway, so the summary is built from what was saved (never a second registration). */
  protected async onSave(request: RegisterPatientRequest): Promise<void> {
    const created = await this.register.execute(request);
    if (created === null) {
      return;
    }

    this.mode.set('search');
    const detail = await this.api.get(created.id).catch(() => null);
    this.picked.emit(detail === null ? summaryOf(created, request) : toSummary(detail));
  }

  protected ageText(item: { readonly age: { readonly years: number; readonly months: number; readonly days: number } }): string {
    return ageText(item.age, this.i18n.lang(), (key, params) => this.i18n.tf(key, params));
  }
}

/** The summary of a file just registered, from the request (the age computed like the server does). */
function summaryOf(created: CreatedPatient, request: RegisterPatientRequest): PatientSummary {
  const today = todayIso();
  const age = request.age ?? (request.dateOfBirth === null ? { years: 0, months: 0, days: 0 } : ageBetween(request.dateOfBirth, today));
  return {
    id: created.id,
    number: created.number,
    title: request.title,
    fullName: request.fullName,
    nameAr: request.nameAr,
    gender: request.gender,
    dateOfBirth: request.dateOfBirth ?? birthDateFromAge(age, today),
    dateOfBirthIsEstimated: request.dateOfBirth === null,
    age,
    mobile: request.mobile,
    nationalId: request.nationalId,
    isVip: request.isVip,
  };
}
