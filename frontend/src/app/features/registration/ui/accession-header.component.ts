import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { PATIENTS_PERMISSIONS } from '../../../core/api/patients.contracts';
import { REGISTRATION_PERMISSIONS, type VisitDetail } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { dateTime, pick } from '../data/registration-format';

/** Whether a previous / next accession can be asked for (the page decides from the open accession). */
export interface AccessionNeighbours {
  readonly previous: boolean;
  readonly next: boolean;
}

/**
 * The sticky header of Accession details (CP-10b): accession, patient (a button opening the CP-10 patient drawer for
 * `patients.edit`), sex and age, VIP / urgent / biohazard flags, visit status, branch, registered at and by, payer and
 * tier, doctor; previous / next accession and *Edit registration* (`registration.edit`). Pure view: the page acts.
 */
@Component({
  selector: 'app-reg-accession-header',
  imports: [BadgeComponent, CanDirective, IconComponent],
  template: `
    @if (visit(); as v) {
      <div class="row">
        <div class="nav">
          <button type="button" class="btn sm ghost" [disabled]="!neighbours().previous" (click)="move.emit(-1)" [attr.aria-label]="i18n.t('registration.accession.previous')"><app-icon name="chevron-left" [size]="16" /></button>
          <b class="acc mono">{{ v.accession }}</b>
          <button type="button" class="btn sm ghost" [disabled]="!neighbours().next" (click)="move.emit(1)" [attr.aria-label]="i18n.t('registration.accession.next')"><app-icon name="chevron-right" [size]="16" /></button>
        </div>
        <div class="who">
          @if (v.patient.masked) {
            <span class="name">{{ i18n.t('registration.vipMasked') }}</span>
          } @else {
            @if (canEditPatient()) {
              <button type="button" class="name link" (click)="patient.emit()">{{ patientName(v) }}</button>
            } @else {
              <span class="name">{{ patientName(v) }}</span>
            }
            <span class="small muted mono">{{ v.patient.number ?? '' }}</span>
            <span class="small muted">{{ sexAge(v) }}</span>
          }
          <span class="flags">
            @if (v.patient.isVip) { <app-badge tone="warn">VIP</app-badge> }
            @if (v.urgent) { <app-badge status="Urgent">{{ i18n.t('registration.flag.urgent') }}</app-badge> }
            @if (v.biohazard) { <app-badge tone="bad">{{ i18n.t('registration.flag.biohazard') }}</app-badge> }
            @if (v.collectedOutside) { <app-badge tone="info">{{ i18n.t('registration.flag.collectedOutside') }}</app-badge> }
            <app-badge [status]="v.status">{{ i18n.t('registration.status.' + v.status) }}</app-badge>
          </span>
        </div>
        <div class="actions">
          <button type="button" class="btn sm" *appCan="permissions.edit" (click)="edit.emit()"><app-icon name="edit" [size]="14" />{{ i18n.t('registration.accession.edit') }}</button>
        </div>
      </div>
      <dl class="facts small">
        <div><dt>{{ i18n.t('registration.column.branch') }}</dt><dd>{{ name(v.branchNameEn, v.branchNameAr) }}</dd></div>
        <div><dt>{{ i18n.t('registration.accession.registeredAt') }}</dt><dd>{{ when(v.registeredAtUtc) }}<span class="muted"> · {{ v.registeredByName ?? '' }}</span></dd></div>
        <div><dt>{{ i18n.t('registration.column.payer') }}</dt><dd>{{ name(v.contract.payerNameEn, v.contract.payerNameAr) }}<span class="muted"> · {{ name(v.contract.tierNameEn, v.contract.tierNameAr) }}</span></dd></div>
        <div><dt>{{ i18n.t('registration.accession.doctor') }}</dt><dd>{{ v.doctorNameEn === null && v.doctorNameAr === null ? '—' : name(v.doctorNameEn, v.doctorNameAr) }}</dd></div>
        @if (v.treatmentNo) { <div><dt>{{ i18n.t('registration.accession.treatmentNo') }}</dt><dd class="mono">{{ v.treatmentNo }}</dd></div> }
        @if (v.batchNumber) { <div><dt>{{ i18n.t('registration.accession.batch') }}</dt><dd class="mono">{{ v.batchNumber }}</dd></div> }
      </dl>
    }
  `,
  styles: `
    :host { position: sticky; top: var(--topbar-h, 56px); z-index: var(--z-sticky, 3); display: block; padding: var(--sp-2) var(--sp-3); background: var(--surface); border: 1px solid var(--line-soft); border-radius: var(--r); }
    .row { display: flex; flex-wrap: wrap; gap: var(--sp-2) var(--sp-4); align-items: center; }
    .nav { display: inline-flex; align-items: center; gap: var(--sp-1); }
    .acc { font-size: var(--fs-lg); }
    .who { display: flex; flex-wrap: wrap; align-items: center; gap: var(--sp-1) var(--sp-2); flex: 1; min-width: 0; }
    .name { font-weight: 600; }
    .link { border: 0; background: transparent; padding: 0; font: inherit; font-weight: 600; color: var(--blue); cursor: pointer; }
    .link:hover { text-decoration: underline; }
    .flags { display: inline-flex; gap: var(--sp-1); flex-wrap: wrap; }
    .actions { display: inline-flex; gap: var(--sp-2); margin-inline-start: auto; }
    .facts { display: flex; flex-wrap: wrap; gap: var(--sp-1) var(--sp-4); margin: var(--sp-2) 0 0; color: var(--ink-2); }
    .facts div { display: inline-flex; gap: var(--sp-1); }
    .facts dt { color: var(--muted); }
    .facts dt::after { content: ':'; }
    .facts dd { margin: 0; }
    :host-context([dir='rtl']) .nav app-icon { transform: scaleX(-1); }
    @media (max-width: 720px) { .facts { display: none; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionHeaderComponent {
  protected readonly i18n = inject(I18nService);
  private readonly access = inject(PermissionsService);

  readonly visit = input<VisitDetail | null>(null);
  readonly neighbours = input<AccessionNeighbours>({ previous: false, next: false });
  readonly move = output<-1 | 1>();
  readonly patient = output<void>();
  readonly edit = output<void>();

  protected readonly permissions = REGISTRATION_PERMISSIONS;
  /** The name opens the patient file only for `patients.edit` (as CP-10); `can()` reads the auth signals, so this follows the session. */
  protected readonly canEditPatient = computed(() => this.access.can(PATIENTS_PERMISSIONS.edit));

  protected patientName(visit: VisitDetail): string {
    const title = visit.patient.title ? `${visit.patient.title} ` : '';
    return `${title}${pick(visit.patient.fullName, visit.patient.nameAr, this.i18n.lang())}`;
  }

  /** "Female · 36 y" — months under a year, days under a month (the laboratory reads age at a glance). */
  protected sexAge(visit: VisitDetail): string {
    const p = visit.patient;
    const sex = p.gender ? this.i18n.t('registration.gender.' + p.gender) : '';
    const age =
      p.ageYears > 0
        ? this.i18n.tf('registration.accession.ageYears', { n: p.ageYears })
        : p.ageMonths > 0
          ? this.i18n.tf('registration.accession.ageMonths', { n: p.ageMonths })
          : this.i18n.tf('registration.accession.ageDays', { n: p.ageDays });
    return [sex, age].filter((part) => part !== '').join(' · ');
  }

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected when(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }
}
