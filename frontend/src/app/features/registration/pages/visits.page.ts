import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import { type CancelLinesRequest, REGISTRATION_PERMISSIONS, VISIT_STATUSES, type VisitListItem } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvText, downloadCsv } from '../data/csv';
import { dateTime, isoDay, money, pick } from '../data/registration-format';
import { RegistrationPrinter } from '../state/registration-printer';
import { VisitsFacade } from '../state/visits.facade';
import { CancelServicesDialogComponent, VisitSamplesDialogComponent } from '../ui/visit-dialogs';

/**
 * Search registrations (SRS CP-10, Release 1): one search box (accession, patient number, mobile or name), the day
 * range, payer, status and "this branch only"; rows with the services styled by status (Reserved, Cancelled), the
 * patient share and the remaining balance (BR-14, Billing's definition). Row actions: edit, samples (dialog), payments,
 * documents (labels, job order), results, cancel services with a reason (BR-15). CSV export with `registration.export`.
 */
@Component({
  selector: 'app-registration-visits-page',
  imports: [
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    DialogComponent,
    CancelServicesDialogComponent,
    VisitSamplesDialogComponent,
  ],
  templateUrl: './visits.page.html',
  styleUrl: './visits.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisitsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(VisitsFacade);
  private readonly printer = inject(RegistrationPrinter);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly permissions = REGISTRATION_PERMISSIONS;
  protected readonly statuses = VISIT_STATUSES;
  protected readonly exporting = signal(false);

  protected readonly cancelOpen = signal(false);
  protected readonly samplesOpen = signal(false);
  protected readonly documentsOpen = signal(false);
  protected readonly current = signal<VisitListItem | null>(null);

  protected readonly columns: readonly TableColumn<VisitListItem>[] = [
    { key: 'accession', header: 'registration.column.accession', width: '150px' },
    { key: 'patient', header: 'registration.column.patient' },
    { key: 'branch', header: 'registration.column.branch', secondary: true, value: (row) => pick(row.branchNameEn, row.branchNameAr, this.i18n.lang()) },
    { key: 'registeredAtUtc', header: 'registration.column.date', value: (row) => dateTime(row.registeredAtUtc, this.i18n.lang()), secondary: true },
    { key: 'payer', header: 'registration.column.payer' },
    { key: 'services', header: 'registration.column.services' },
    { key: 'patientShareTotal', header: 'registration.column.patientShare', align: 'end', value: (row) => money(row.patientShareTotal, this.i18n.lang()) },
    { key: 'remaining', header: 'registration.column.remaining', align: 'end' },
    { key: 'status', header: 'registration.column.status', width: '110px' },
  ];

  protected readonly rows = computed<readonly VisitListItem[]>(() => (this.facade.visits.hasValue() ? this.facade.visits.value().items : []));
  protected readonly total = computed(() => (this.facade.visits.hasValue() ? this.facade.visits.value().totalCount : 0));

  constructor() {
    void this.i18n.load('registration');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
  }

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected amount(value: number | null): string {
    return money(value, this.i18n.lang());
  }

  protected patientName(row: VisitListItem): string {
    return row.patientMasked ? this.i18n.t('registration.vipMasked') : this.name(row.patientName, row.patientNameAr);
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected onStatus(event: Event): void {
    const value = this.value(event);
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected onPayer(event: Event): void {
    const value = this.value(event);
    this.facade.setPayer(value === '' ? null : value);
  }

  protected onBranchOnly(event: Event): void {
    this.facade.setBranchOnly((event.target as HTMLInputElement).checked);
  }

  protected onFrom(event: Event): void {
    this.facade.setDays({ from: this.value(event) });
  }

  protected onTo(event: Event): void {
    this.facade.setDays({ to: this.value(event) });
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected edit(row: VisitListItem): void {
    void this.router.navigate(['/registration/visits', row.id]);
  }

  /** CP-10 "payments": the Billing cash desk opened on this visit (BR-26). */
  protected payments(row: VisitListItem): void {
    void this.router.navigate(['/billing/payments'], { queryParams: { visitId: row.id } });
  }

  /** CP-10 "results": results by accession (the Results module's page; path agreed with the integrator). */
  protected results(row: VisitListItem): void {
    void this.router.navigate(['/results/by-accession'], { queryParams: { accession: row.accession } });
  }

  protected openSamples(row: VisitListItem): void {
    this.current.set(row);
    this.samplesOpen.set(true);
  }

  protected openDocuments(row: VisitListItem): void {
    this.current.set(row);
    this.documentsOpen.set(true);
  }

  protected openCancel(row: VisitListItem): void {
    this.facade.cancel.reset();
    this.current.set(row);
    this.cancelOpen.set(true);
  }

  protected async printLabels(): Promise<void> {
    const row = this.current();
    if (row === null) {
      return;
    }

    try {
      const count = await this.printer.labels(row.id);
      if (count === 0) {
        this.toast.info(this.i18n.t('registration.labels.none'));
      }
    } catch {
      this.toast.error(this.i18n.t('registration.printError'));
    }
  }

  protected async printJobOrder(): Promise<void> {
    const row = this.current();
    if (row !== null) {
      await this.printer.jobOrder(row.id).catch(() => this.toast.error(this.i18n.t('registration.printError')));
    }
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

  protected async export(): Promise<void> {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    try {
      const rows = await this.facade.exportRows();
      const t = (key: string): string => this.i18n.t(`registration.column.${key}`);
      const text = csvText(
        [t('accession'), t('date'), t('patientNumber'), t('patient'), t('mobile'), t('branch'), t('payer'), t('tier'), t('services'), t('total'), t('discount'), t('patientShare'), t('payerShare'), t('paid'), t('remaining'), t('status')],
        rows.map((row) => [
          row.accession,
          dateTime(row.registeredAtUtc, this.i18n.lang()),   // local date and time as in the grid, not raw UTC (UAT A-15)
          row.patientNumber,
          row.patientMasked ? this.i18n.t('registration.vipMasked') : row.patientName,
          row.mobile,
          this.name(row.branchNameEn, row.branchNameAr),
          this.name(row.payerNameEn, row.payerNameAr),
          this.name(row.tierNameEn, row.tierNameAr),
          row.lines.map((line) => `${line.code}${line.status === 'Ordered' ? '' : ` (${this.i18n.t('registration.lineStatus.' + line.status)})`}`).join('; '),
          row.totalListPrice,
          row.discountTotal,
          row.patientShareTotal,
          row.payerShareTotal,
          row.paid,
          row.remaining,
          this.i18n.t('registration.status.' + row.status),
        ]),
      );
      downloadCsv(`registrations-${isoDay(new Date())}.csv`, text);
      this.toast.success(this.i18n.t('registration.exported'));
    } catch {
      this.toast.error(this.i18n.t('registration.exportError'));
    } finally {
      this.exporting.set(false);
    }
  }
}
