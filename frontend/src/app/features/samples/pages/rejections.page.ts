import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { SAMPLES_LINKS, SAMPLES_PERMISSIONS, type SampleListItem } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { RejectionsFacade } from '../state/rejections.facade';
import { type RejectChoice, RejectDialogComponent } from '../ui/reject-dialog.component';
import { SampleSummaryComponent } from '../ui/sample-summary.component';
import { ScanBoxComponent } from '../ui/scan-box.component';
import { formatDateTime, patientLabel, pickName, sampleTypeLabel } from '../ui/sample-format';

/** The sample a dialog or confirmation acts on. */
interface TargetSample {
  readonly id: string;
  readonly sampleNumber: string;
}

/**
 * CP-16 Reject / unreject (WF-05 steps 3–4, BR-18): look a tube up by barcode and reject it with a catalogue reason
 * (reception is notified through the rejected-today list), then reinstate it with the same barcode when the tenant
 * allows it, or replace it with a new sample and print the new label. Every action asks for confirmation.
 */
@Component({
  selector: 'app-samples-rejections-page',
  imports: [
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    IconComponent,
    ScanBoxComponent,
    SampleSummaryComponent,
    RejectDialogComponent,
  ],
  templateUrl: './rejections.page.html',
  styleUrl: './rejections.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RejectionsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(RejectionsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  /** `?sampleNumber=` from another page (CP-15 "reject"). */
  readonly sampleNumber = input<string>();

  protected readonly permissions = SAMPLES_PERMISSIONS;
  protected readonly links = SAMPLES_LINKS;

  protected readonly dialogOpen = signal(false);
  protected readonly dialogTarget = signal<TargetSample | null>(null);

  protected readonly columns: readonly TableColumn<SampleListItem>[] = [
    { key: 'sampleNumber', header: 'samples.column.sampleNumber', mono: true, width: '170px', sortable: true },
    { key: 'accession', header: 'samples.column.accession', mono: true, width: '150px', sortable: true },
    { key: 'patient', header: 'samples.column.patient', value: (row) => patientLabel(row, this.i18n.lang()) },
    { key: 'sampleType', header: 'samples.column.sampleType', value: (row) => sampleTypeLabel(row, this.i18n.lang()), secondary: true },
    { key: 'reason', header: 'samples.column.reason', value: (row) => pickName(row.rejectionReasonNameEn, row.rejectionReasonNameAr, this.i18n.lang(), '—') },
    { key: 'rejectionNote', header: 'samples.column.note', secondary: true },
    { key: 'rejectedAtUtc', header: 'samples.column.rejectedAt', value: (row) => formatDateTime(row.rejectedAtUtc, this.i18n.lang()), width: '150px', sortable: true },
  ];

  protected readonly rows = computed<readonly SampleListItem[]>(() => (this.facade.rejected.hasValue() ? this.facade.rejected.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.rejected.hasValue() ? this.facade.rejected.value().totalCount : 0));

  protected readonly lookupProblem = computed<ProblemDetails | null>(() => {
    const error = this.facade.lookup.error();
    return error instanceof ProblemError ? error.problem : error ? { type: 'about:blank', title: '', status: 0, code: 'SMP-404', traceId: null, correlationId: null, errors: null } : null;
  });

  protected readonly reasons = computed(() => (this.facade.reasons.hasValue() ? this.facade.reasons.value() : []));
  protected readonly reasonsUnavailable = computed(() => this.facade.reasons.error() !== undefined && this.facade.reasons.error() !== null);

  protected readonly dialogProblem = computed(() => {
    const problem = this.facade.reject.problem();
    return problem ? problemMessage(this.i18n, 'samples', problem) : '';
  });

  protected readonly actionProblems = computed(() =>
    [this.facade.unreject.problem(), this.facade.replace.problem(), this.facade.printLabel.problem(), this.facade.exporting.problem()].filter((problem): problem is ProblemDetails => problem !== null),
  );

  constructor() {
    void this.i18n.load('samples');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
    effect(() => {
      const number = this.sampleNumber();
      if (number) {
        untracked(() => this.facade.find(number));
      }
    });
  }

  protected find(value: string): void {
    this.facade.find(value);
  }

  protected openReject(target: TargetSample): void {
    this.facade.reject.reset();
    this.dialogTarget.set(target);
    this.dialogOpen.set(true);
  }

  protected async onRejectConfirmed(choice: RejectChoice): Promise<void> {
    const target = this.dialogTarget();
    if (!target) {
      return;
    }

    if ((await this.facade.reject.execute({ id: target.id, reasonId: choice.reasonId, note: choice.note })) !== null) {
      this.dialogOpen.set(false);
      this.toast.success(this.i18n.tf('samples.reject.done', { number: target.sampleNumber }));
    }
  }

  protected async unreject(target: TargetSample): Promise<void> {
    const note = await this.confirm.promptReason({
      title: this.i18n.t('samples.unreject.title'),
      message: this.i18n.tf('samples.unreject.message', { number: target.sampleNumber }),
      label: this.i18n.t('samples.reject.note'),
      required: false,
      maxLength: 500,
      confirmLabel: this.i18n.t('samples.unreject.confirm'),
    });
    if (note === null) {
      return;
    }

    if ((await this.facade.unreject.execute({ id: target.id, note: note === '' ? null : note })) !== null) {
      this.toast.success(this.i18n.tf('samples.unreject.done', { number: target.sampleNumber }));
    }
  }

  protected async replace(target: TargetSample): Promise<void> {
    const note = await this.confirm.promptReason({
      title: this.i18n.t('samples.replace.title'),
      message: this.i18n.tf('samples.replace.message', { number: target.sampleNumber }),
      label: this.i18n.t('samples.reject.note'),
      required: false,
      maxLength: 500,
      confirmLabel: this.i18n.t('samples.replace.confirm'),
    });
    if (note === null) {
      return;
    }

    const replacement = await this.facade.replace.execute({ id: target.id, note: note === '' ? null : note });
    if (replacement !== null) {
      this.toast.success(this.i18n.tf('samples.replace.done', { old: target.sampleNumber, number: replacement.summary.sampleNumber }));
    }
  }

  protected async printLabel(sampleId: string): Promise<void> {
    if ((await this.facade.printLabel.execute(sampleId)) !== null) {
      this.toast.success(this.i18n.tc('samples.labels.printed', 1));
    }
  }

  protected async exportCsv(): Promise<void> {
    const count = await this.facade.exporting.execute(`samples-rejected-${new Date().toISOString().slice(0, 10)}.csv`);
    if (count !== null) {
      this.toast.success(this.i18n.t('samples.exported'));
    }
  }

  protected onFrom(event: Event): void {
    this.facade.setDates((event.target as HTMLInputElement).value, this.facade.filter().toDate);
  }

  protected onTo(event: Event): void {
    this.facade.setDates(this.facade.filter().fromDate, (event.target as HTMLInputElement).value);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected rejectable(status: string): boolean {
    return status === 'Collected' || status === 'Received';
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'samples', problem);
  }
}
