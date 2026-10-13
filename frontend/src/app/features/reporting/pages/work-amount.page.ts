import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import type { SelectableBranch } from '../../../core/api/organization.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { REPORTING_PERMISSIONS, WORK_AMOUNT_GROUPINGS, type WorkAmountGrouping, type WorkAmountQuery, type WorkAmountReport, type WorkAmountRow } from '../../../core/api/reporting.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { PrintService } from '../../../shared/print/print.service';
import { CanDirective } from '../../../shared/ui/can.directive';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { saveFile } from '../data/save-file';
import { WorkAmountFacade } from '../state/work-amount.facade';
import { formatCount, formatDay, formatMoney, rowLabel } from '../ui/report-format';
import { WorkAmountDocumentComponent } from '../ui/work-amount-document.component';

/**
 * Work amount report (change request 2026-10-13 item 3): the income of the visits registered in a period of laboratory
 * days — cash (patient shares) and credit (payer shares), cancelled services excluded — grouped by day, payer, contract
 * tier, user, doctor, clinical group, service or branch, with a grand total. The period opens on today; a day before
 * the member's earliest allowed day (CP-37 report days limit) is refused by the server with REP-001 and hinted here.
 * Print (A4 landscape) and CSV export (`reporting.export`).
 */
@Component({
  selector: 'app-work-amount-page',
  imports: [ReactiveFormsModule, CanDirective, PageHeaderComponent, SectionCardComponent, FormFieldComponent, IconComponent, EmptyStateComponent],
  templateUrl: './work-amount.page.html',
  styleUrl: './work-amount.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkAmountPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(WorkAmountFacade);
  private readonly print = inject(PrintService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);

  protected readonly permissions = REPORTING_PERMISSIONS;
  protected readonly groupings = WORK_AMOUNT_GROUPINGS;

  protected readonly form = new FormGroup({
    from: new FormControl(this.facade.query().from, { nonNullable: true }),
    to: new FormControl(this.facade.query().to, { nonNullable: true }),
    groupBy: new FormControl<WorkAmountGrouping>(this.facade.query().groupBy, { nonNullable: true }),
    branchId: new FormControl(this.facade.query().branchId ?? '', { nonNullable: true }),
  });

  protected readonly report = this.facade.current;

  protected readonly rows = computed<readonly WorkAmountRow[]>(() => this.report()?.rows ?? []);

  /** The branch picker: offered when the member may select more than one branch (CP-04). */
  protected readonly branchChoice = computed<readonly SelectableBranch[]>(() => (this.facade.selectableBranches().length > 1 ? this.facade.selectableBranches() : []));

  protected readonly problem = computed(() => {
    const error = this.facade.report.error();
    if (!error) {
      return null;
    }

    return problemMessage(this.i18n, 'reporting', error instanceof ProblemError ? error.problem : { code: 'INTERNAL-500' });
  });

  /** The hint under the period: how far back the member may report (CP-37), once the privileges are read. */
  protected readonly limitHint = computed<string | null>(() => {
    const limit = this.facade.reportDaysLimit();
    if (limit === undefined || limit === null) {
      return null;
    }

    return limit === 0 ? this.i18n.t('reporting.workAmount.todayOnly') : this.i18n.tf('reporting.workAmount.earliestDay', { day: formatDay(this.facade.earliestDay() ?? '', this.i18n.lang()) });
  });

  constructor() {
    void this.i18n.load('reporting');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    effect(() => {
      // the server echoes the period it applied (defaults, laboratory calendar): keep the form on it
      const current = this.report();
      untracked(() => {
        if (current) {
          this.form.patchValue({ from: current.fromDay, to: current.toDay }, { emitEvent: false });
        }
      });
    });
  }

  protected run(): void {
    const value = this.form.getRawValue();
    const query: WorkAmountQuery = {
      from: value.from,
      to: value.to.length > 0 ? value.to : value.from,
      groupBy: this.groupings.find((grouping) => grouping === value.groupBy) ?? 'Day',
      branchId: value.branchId.length > 0 ? value.branchId : null,
    };
    this.facade.run(query);
  }

  /** The earliest day the date inputs accept (the member's days limit); the server is the judge (REP-001). */
  protected minDay(): string | null {
    return this.facade.earliestDay();
  }

  protected async printReport(): Promise<void> {
    const doc = this.report();
    if (!doc) {
      return;
    }

    await this.print.print({ title: this.i18n.t('reporting.workAmount.title'), size: 'A4', landscape: true, component: { type: WorkAmountDocumentComponent, inputs: { doc } } });
  }

  protected async exportCsv(): Promise<void> {
    const file = await this.facade.exportCsv.execute(this.facade.query());
    if (file === null) {
      this.toast.error(problemMessage(this.i18n, 'reporting', this.facade.exportCsv.problem()));
      return;
    }

    saveFile(this.document, file);
    this.toast.success(this.i18n.t('reporting.workAmount.exported'));
  }

  protected label(row: WorkAmountRow): string {
    return rowLabel(row, this.i18n.lang());
  }

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected count(value: number): string {
    return formatCount(value, this.i18n.lang());
  }

  protected day(value: string): string {
    return formatDay(value, this.i18n.lang());
  }

  protected branchName(branch: SelectableBranch): string {
    return this.i18n.lang() === 'ar' ? branch.nameAr : branch.nameEn;
  }

  protected periodText(report: WorkAmountReport): string {
    return report.fromDay === report.toDay ? this.day(report.fromDay) : `${this.day(report.fromDay)} – ${this.day(report.toDay)}`;
  }
}
