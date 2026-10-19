import { ChangeDetectionStrategy, Component, computed, inject, input, output, resource, viewChild } from '@angular/core';

import type { RegisterPatientRequest, UpdatePatientRequest } from '../../../core/api/patients.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { PatientFormComponent } from '../../../shared/patient/patient-form.component';
import { PatientLookupApi } from '../../../shared/patient/patient-lookup.api';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/** The patient file opened from a visit: the file and the visit the identity edit is judged on (PAT-032). */
export interface VisitPatientTarget {
  readonly patientId: string;
  readonly visitId: string;
}

/**
 * The patient drawer of CP-10 (change request 2026-10-13 item 1) as one component, so Accession details reuses it:
 * the file, its demographics lock for the visit, the form's lookups (`shared/patient`), and the save that emits
 * `saved` so the page re-reads what shows the name and mobile. Open while `target` is set; closing emits `closed`.
 */
@Component({
  selector: 'app-reg-patient-drawer',
  imports: [DrawerComponent, PatientFormComponent],
  template: `
    <app-drawer [open]="target() !== null" (openChange)="onOpenChange($event)" [title]="i18n.tf('registration.patient.title', { number: patient()?.number ?? '' })" size="lg">
      @if (target() !== null) {
        @if (detail.error()) {
          <p class="problem" role="alert">{{ i18n.t('registration.patient.loadError') }}</p>
        } @else if (ready()) {
          <app-patient-form
            [patient]="patient()"
            [settings]="settings.value() ?? null"
            [countries]="countries.value() ?? []"
            [governorates]="governorates.value() ?? []"
            [conditions]="conditions.value() ?? []"
            [identityLocked]="identityLocked()"
            [lockHint]="lockHint()"
            [problem]="update.problem()"
            [pending]="update.pending()"
            (save)="onSave($event)"
          />
        } @else {
          <p class="muted" role="status">{{ i18n.t('registration.patient.loading') }}</p>
        }
      }
      <ng-container drawerFooter>
        <button type="button" class="btn ghost" (click)="closed.emit()">{{ i18n.t('registration.close') }}</button>
        <button type="button" class="btn primary" [disabled]="!ready() || update.pending()" (click)="submit()">{{ update.pending() ? i18n.t('registration.patient.saving') : i18n.t('registration.patient.save') }}</button>
      </ng-container>
    </app-drawer>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisitPatientDrawerComponent {
  protected readonly i18n = inject(I18nService);
  private readonly patients = inject(PatientLookupApi);
  private readonly toast = inject(ToastService);
  private readonly form = viewChild(PatientFormComponent);

  readonly target = input<VisitPatientTarget | null>(null);
  readonly closed = output<void>();
  readonly saved = output<void>();

  protected readonly detail = resource({
    params: () => this.target() ?? undefined,
    loader: ({ params, abortSignal }) => this.patients.get(params.patientId, abortSignal),
  });

  /** Read before the form shows: what the server will decide on an identity change from this visit. */
  protected readonly lock = resource({
    params: () => this.target() ?? undefined,
    loader: ({ params, abortSignal }) => this.patients.demographicsLock(params.patientId, params.visitId, abortSignal),
  });

  protected readonly settings = resource({ params: () => (this.target() === null ? undefined : true), loader: ({ abortSignal }) => this.patients.formSettings(abortSignal) });
  protected readonly countries = resource({ params: () => (this.target() === null ? undefined : true), loader: () => this.patients.countries() });
  protected readonly governorates = resource({ params: () => (this.target() === null ? undefined : true), loader: () => this.patients.governorates() });
  protected readonly conditions = resource({ params: () => (this.target() === null ? undefined : true), loader: () => this.patients.conditions() });

  protected readonly update = mutation((input: { patientId: string; request: UpdatePatientRequest }) => this.patients.update(input.patientId, input.request));

  protected readonly patient = computed(() => (this.detail.hasValue() ? this.detail.value() : null));
  /** The form shows only once the file and its lock are both known, so the identity fields never flash editable. */
  protected readonly ready = computed(() => this.patient() !== null && (this.lock.hasValue() || this.lock.error() !== undefined));
  /** A lock that could not be read locks the identity (the server decides anyway). */
  protected readonly identityLocked = computed(() => (this.lock.hasValue() ? !this.lock.value().canEditIdentity : true));
  protected readonly lockHint = computed(() => {
    if (!this.lock.hasValue()) {
      return this.i18n.t('registration.patient.lockUnknown');
    }

    const lock = this.lock.value();
    return lock.canEditIdentity
      ? null
      : this.i18n.tf('registration.patient.locked', { status: this.i18n.t(`shared.status.${lock.highestStatus}`), limit: this.i18n.t(`shared.status.${lock.editableUpTo}`) });
  });

  /** The drawer's Save button; F10 inside the form reaches `onSave` through the form's own shortcut host. */
  protected submit(): void {
    this.form()?.submit();
  }

  protected onOpenChange(open: boolean): void {
    if (!open) {
      this.closed.emit();
    }
  }

  protected async onSave(request: RegisterPatientRequest): Promise<void> {
    const target = this.target();
    const patient = this.patient();
    if (target === null || patient === null) {
      return;
    }

    const saved = await this.update.execute({ patientId: target.patientId, request: { ...request, rowVersion: patient.rowVersion, visitId: target.visitId } });
    if (saved !== null) {
      this.toast.success(this.i18n.tf('registration.patient.saved', { number: patient.number }));
      this.saved.emit();
      this.closed.emit();
    }
  }
}
