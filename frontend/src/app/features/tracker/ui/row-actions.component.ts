import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BILLING_PERMISSIONS } from '../../../core/api/billing.contracts';
import { PATIENTS_PERMISSIONS } from '../../../core/api/patients.contracts';
import { REGISTRATION_PERMISSIONS } from '../../../core/api/registration.contracts';
import { RESULTS_PERMISSIONS } from '../../../core/api/results.contracts';
import { SAMPLES_PERMISSIONS } from '../../../core/api/samples.contracts';
import { type LabTrackerItem, TRACKER_LINKS } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { CanDirective } from '../../../shared/ui/can.directive';
import { RowMenuComponent } from '../../../shared/ui/data-table/row-menu.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { RETURN_TO_PARAM } from '../../../shared/ui/return-link/return-link.component';

/** The card of the accession page (CP-10b) a row action opens (`?tab=`): its documents, its samples, or the cancel-services dialog. */
export type AccessionTab = 'documents' | 'samples' | 'cancel';

/** One navigation of a row action: a path and its query (always with `returnTo` when the tracker gives one). */
export interface RowLink {
  readonly path: readonly string[];
  readonly queryParams: Readonly<Record<string, string>>;
}

/** The pages the row actions open (LIS-CR-014 T1: CP-10's actions as links; the tracker cannot import the registration feature). */
export function rowLinks(item: Pick<LabTrackerItem, 'visitId' | 'accession' | 'patientId'>, returnTo: string | null) {
  const back: Record<string, string> = returnTo === null ? {} : { [RETURN_TO_PARAM]: returnTo };
  const accession = (tab?: AccessionTab): RowLink => ({ path: [TRACKER_LINKS.accession], queryParams: { accession: item.accession, ...(tab ? { tab } : {}), ...back } });
  return {
    details: accession(),
    edit: { path: [TRACKER_LINKS.visits, item.visitId], queryParams: back } satisfies RowLink,
    payments: { path: [TRACKER_LINKS.payments], queryParams: { visitId: item.visitId, ...back } } satisfies RowLink,
    documents: accession('documents'),
    patient: { path: [TRACKER_LINKS.patients, item.patientId], queryParams: back } satisfies RowLink,
    samples: accession('samples'),
    results: { path: [TRACKER_LINKS.results], queryParams: { accession: item.accession, ...back } } satisfies RowLink,
    cancel: accession('cancel'),
  };
}

/**
 * The actions of a tracker row (LIS-CR-014 T1), CP-10's set (LIS-CR-007 C-06): *Details* and *Edit* labelled, *Payments* and
 * *Documents* as icons, then *More*: Patient, Samples, Results, Cancel services. Every action is a link to the page that
 * does it, carrying `returnTo` (the tracker with its filter) so the shell's "Back to Lab Tracker" chip brings the user back;
 * each shows only with its module's permission (`*appCan`, affordance only: the target page and the server decide).
 */
@Component({
  selector: 'app-tracker-row-actions',
  imports: [RouterLink, CanDirective, IconComponent, RowMenuComponent],
  template: `
    @let link = links();
    <a class="btn sm ghost" [routerLink]="link.details.path" [queryParams]="link.details.queryParams" [attr.aria-label]="aria('tracker.action.details')">
      <app-icon name="eye" [size]="14" />{{ i18n.t('tracker.action.details') }}
    </a>
    <a class="btn sm ghost" *appCan="permissions.edit" [routerLink]="link.edit.path" [queryParams]="link.edit.queryParams" [attr.aria-label]="aria('tracker.action.edit')">
      <app-icon name="edit" [size]="14" />{{ i18n.t('tracker.action.edit') }}
    </a>
    <a class="row-btn" *appCan="permissions.billing" [routerLink]="link.payments.path" [queryParams]="link.payments.queryParams" [attr.aria-label]="aria('tracker.action.payments')" [title]="i18n.t('tracker.action.payments')">
      <app-icon name="coins" [size]="15" />
    </a>
    <a class="row-btn" [routerLink]="link.documents.path" [queryParams]="link.documents.queryParams" [attr.aria-label]="aria('tracker.action.documents')" [title]="i18n.t('tracker.action.documents')">
      <app-icon name="printer" [size]="15" />
    </a>
    <app-row-menu [label]="aria('tracker.action.more')">
      <a *appCan="permissions.patient" [routerLink]="link.patient.path" [queryParams]="link.patient.queryParams"><app-icon name="user" [size]="14" />{{ i18n.t('tracker.action.patient') }}</a>
      <a *appCan="permissions.samples" [routerLink]="link.samples.path" [queryParams]="link.samples.queryParams"><app-icon name="barcode" [size]="14" />{{ i18n.t('tracker.action.samples') }}</a>
      <a *appCan="permissions.results" [routerLink]="link.results.path" [queryParams]="link.results.queryParams"><app-icon name="flask" [size]="14" />{{ i18n.t('tracker.action.results') }}</a>
      <a class="danger" *appCan="permissions.cancel" [routerLink]="link.cancel.path" [queryParams]="link.cancel.queryParams"><app-icon name="x" [size]="14" />{{ i18n.t('tracker.action.cancel') }}</a>
    </app-row-menu>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      white-space: nowrap;
    }

    a.btn,
    a.row-btn {
      text-decoration: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackerRowActionsComponent {
  protected readonly i18n = inject(I18nService);

  readonly item = input.required<LabTrackerItem>();

  /** The tracker's own URL with its filter (null = no way back is offered). */
  readonly returnTo = input<string | null>(null);

  protected readonly permissions = {
    edit: REGISTRATION_PERMISSIONS.edit,
    cancel: REGISTRATION_PERMISSIONS.cancel,
    billing: BILLING_PERMISSIONS.view,
    patient: PATIENTS_PERMISSIONS.view,
    samples: SAMPLES_PERMISSIONS.view,
    results: RESULTS_PERMISSIONS.view,
  } as const;

  protected readonly links = computed(() => rowLinks(this.item(), this.returnTo()));

  /** "Details — ACC-2026-00001": the action and the visit, for a screen reader. */
  protected aria(key: string): string {
    return this.i18n.tf('tracker.action.aria', { action: this.i18n.t(key), accession: this.item().accession });
  }
}
