import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { BILLING_PERMISSIONS } from '../../../core/api/billing.contracts';
import { type CancelLinesRequest, REGISTRATION_PERMISSIONS, type VisitLineItem, type VisitListItem } from '../../../core/api/registration.contracts';
import { RESULTS_PERMISSIONS } from '../../../core/api/results.contracts';
import { SAMPLES_PERMISSIONS, type SampleListItem } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { accessionNeighbour } from '../data/accession-neighbour';
import { ACCESSION_NOT_FOUND, AccessionFacade, type VisitKey } from '../state/accession.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { AccessionHeaderComponent } from '../ui/accession-header.component';
import { AccessionLinesComponent } from '../ui/accession-lines.component';
import { AccessionPaymentsComponent } from '../ui/accession-payments.component';
import { AccessionResultsComponent } from '../ui/accession-results.component';
import { AccessionSamplesComponent } from '../ui/accession-samples.component';
import { AccessionSearchComponent } from '../ui/accession-search.component';
import { CancelServicesDialogComponent } from '../ui/visit-dialogs';
import { type VisitPatientTarget, VisitPatientDrawerComponent } from '../ui/visit-patient-drawer.component';

/**
 * Accession details (SRS CP-10b, change request 2026-10-19 LIS-CR-003 P-C9): one page per accession — the visit, its
 * ordered tests, samples, results status and payments — with the actions reception needs in one place. Opened with
 * `?accession=` or `?visitId=` (the URL is the source of truth: the search strip, the match list and previous / next
 * navigate, so the browser's back button and a shared link work), or empty with the search strip focused. Cards show
 * only for the permission their module checks; buttons are affordance (`*appCan`), the server decides.
 */
@Component({
  selector: 'app-registration-accession-page',
  imports: [
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    EmptyStateComponent,
    IconComponent,
    AccessionSearchComponent,
    AccessionHeaderComponent,
    AccessionLinesComponent,
    AccessionSamplesComponent,
    AccessionResultsComponent,
    AccessionPaymentsComponent,
    CancelServicesDialogComponent,
    VisitPatientDrawerComponent,
  ],
  templateUrl: './accession-details.page.html',
  styleUrl: './accession-details.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionDetailsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(AccessionFacade);
  private readonly router = inject(Router);
  private readonly printer = inject(RegistrationPrinter);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly search = viewChild(AccessionSearchComponent);

  /** `?accession=` (links from other pages, previous / next) or `?visitId=` (the registration success panel). */
  readonly accession = input<string>();
  readonly visitId = input<string>();

  protected readonly permissions = REGISTRATION_PERMISSIONS;
  protected readonly samplesView = SAMPLES_PERMISSIONS.view;
  protected readonly resultsView = RESULTS_PERMISSIONS.view;
  protected readonly billingView = BILLING_PERMISSIONS.view;

  protected readonly visit = computed(() => (this.facade.detail.hasValue() ? this.facade.detail.value() : null));
  protected readonly notFound = computed(() => {
    const error = this.facade.detail.error();
    return error instanceof Error && error.message === ACCESSION_NOT_FOUND;
  });
  protected readonly samples = computed(() => (this.facade.samples.hasValue() ? this.facade.samples.value() : []));
  protected readonly account = computed(() => (this.facade.account.hasValue() ? this.facade.account.value() : null));
  protected readonly results = computed(() => (this.facade.results.hasValue() ? this.facade.results.value() : null));
  /** Previous / next by the accession number ± 1 (looked up before it is opened, as the results page does). */
  protected readonly neighbours = computed(() => {
    const visit = this.visit();
    return visit === null ? { previous: false, next: false } : { previous: accessionNeighbour(visit.accession, -1) !== null, next: accessionNeighbour(visit.accession, 1) !== null };
  });

  protected readonly cancelOpen = signal(false);
  protected readonly cancelLines = signal<readonly string[]>([]);
  protected readonly patientTarget = signal<VisitPatientTarget | null>(null);
  protected readonly collecting = signal<string | null>(null);

  constructor() {
    void this.i18n.load('registration');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
    effect(() => {
      const visitId = this.visitId();
      const accession = this.accession();
      const key: VisitKey | null = visitId ? { visitId } : accession ? { accession } : null;
      untracked(() => {
        this.facade.open(key);
        this.patientTarget.set(null);
        this.cancelOpen.set(false);
        if (key === null) {
          this.search()?.focus();
        }
      });
    });
  }

  // ---- search and navigation -----------------------------------------------------------------------------------------

  protected async onSearch(text: string): Promise<void> {
    const answer = await this.facade.search(text);
    switch (answer.kind) {
      case 'opened':
        await this.goTo(answer.visit.accession);
        break;
      case 'none':
        if (text.trim() !== '') {
          this.toast.info(this.i18n.t('registration.accession.noMatch'));
        }

        break;
      case 'failed':
        this.toast.error(this.i18n.t('registration.visits.loadError'));
        break;
      default:
        break;
    }
  }

  protected pickMatch(row: VisitListItem): void {
    this.facade.closeList();
    void this.goTo(row.accession);
  }

  protected async move(direction: -1 | 1): Promise<void> {
    const visit = this.visit();
    const accession = visit === null ? null : accessionNeighbour(visit.accession, direction);
    if (accession === null) {
      return;
    }

    const found = await this.facade.findByAccession(accession);
    if (found === null) {
      this.toast.info(this.i18n.tf('registration.accession.notFound', { accession }));
      return;
    }

    await this.goTo(found.accession);
  }

  protected edit(): void {
    const visit = this.visit();
    if (visit !== null) {
      void this.router.navigate(['/registration/visits', visit.id]);
    }
  }

  private goTo(accession: string): Promise<boolean> {
    return this.router.navigate(['/registration/accession'], { queryParams: { accession } });
  }

  // ---- patient -----------------------------------------------------------------------------------------------------------

  protected openPatient(): void {
    const visit = this.visit();
    if (visit !== null) {
      this.patientTarget.set({ patientId: visit.patient.id, visitId: visit.id });
    }
  }

  protected onPatientSaved(): void {
    this.facade.detail.reload();
  }

  // ---- cancel lines ------------------------------------------------------------------------------------------------------

  protected openCancel(line: VisitLineItem): void {
    this.facade.cancel.reset();
    this.cancelLines.set([line.id]);
    this.cancelOpen.set(true);
  }

  protected async cancelServices(input: { visitId: string; request: CancelLinesRequest }): Promise<void> {
    if ((await this.facade.cancel.execute(input)) !== null) {
      this.cancelOpen.set(false);
      this.toast.success(this.i18n.t('registration.cancel.done'));
    }
  }

  protected cancelProblem(): string | null {
    const problem = this.facade.cancel.problem();
    return problem === null ? null : problemMessage(this.i18n, 'registration', problem);
  }

  // ---- samples -------------------------------------------------------------------------------------------------------------

  /** The phlebotomy scan from the desk (`samples.create`): confirmed, because a collection time is evidence. */
  protected async collect(sample: SampleListItem): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('registration.accession.samples.collect'),
      message: this.i18n.tf('registration.accession.samples.collectConfirm', { sample: sample.sampleNumber }),
    });
    if (!confirmed) {
      return;
    }

    this.collecting.set(sample.sampleNumber);
    try {
      const collected = await this.facade.collect.execute(sample.sampleNumber);
      if (collected !== null) {
        this.toast.success(this.i18n.tf('registration.accession.samples.collected', { sample: sample.sampleNumber }));
      } else {
        const problem = this.facade.collect.problem();
        this.toast.error(problem === null ? this.i18n.t('registration.saveError') : problemMessage(this.i18n, 'registration', problem));
      }
    } finally {
      this.collecting.set(null);
    }
  }

  protected async label(sample: SampleListItem): Promise<void> {
    const visit = this.visit();
    if (visit !== null) {
      await this.printer.labels(visit.id, sample.tubeId).catch(() => this.toast.error(this.i18n.t('registration.printError')));
    }
  }

  // ---- documents -----------------------------------------------------------------------------------------------------------

  protected async printLabels(): Promise<void> {
    const visit = this.visit();
    if (visit === null) {
      return;
    }

    try {
      if ((await this.printer.labels(visit.id)) === 0) {
        this.toast.info(this.i18n.t('registration.labels.none'));
      }
    } catch {
      this.toast.error(this.i18n.t('registration.printError'));
    }
  }

  protected async printJobOrder(): Promise<void> {
    const visit = this.visit();
    if (visit !== null) {
      await this.printer.jobOrder(visit.id).catch(() => this.toast.error(this.i18n.t('registration.printError')));
    }
  }

  protected async receipt(paymentId: string): Promise<void> {
    await this.printer.receipt(paymentId).catch(() => this.toast.error(this.i18n.t('registration.printError')));
  }
}
