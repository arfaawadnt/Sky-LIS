import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  RESULT_FILTER_STATUSES,
  RESULTS_LINKS,
  RESULTS_PERMISSIONS,
  SAMPLE_FILTER_STATUSES,
  type ResultStatus,
  type WorklistItem,
} from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { ResultsPrinter } from '../state/results-printer';
import { WorklistFacade } from '../state/worklist.facade';
import { type DurationUnits, delayClass, delayTone, formatDateTime, formatDuration, patientLabel, pickName, testLabel } from '../ui/result-format';

/**
 * CP-18 Worklist (WF-06 step 1; LDM §6): one row per ordered test of the collection window of the branch, filtered
 * by clinical group, test, analyser, sample status, payer and the status boxes, with the BR-13 delay colour
 * (warn / late thresholds from the tenant parameters), the counters, the bench-list document and a CSV of the
 * current filter. A row opens the visit on CP-19.
 */
@Component({
  selector: 'app-results-worklist-page',
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
    StatCardComponent,
  ],
  templateUrl: './worklist.page.html',
  styleUrl: './worklist.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorklistPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(WorklistFacade);
  private readonly printer = inject(ResultsPrinter);
  private readonly toast = inject(ToastService);

  protected readonly permissions = RESULTS_PERMISSIONS;
  protected readonly links = RESULTS_LINKS;
  protected readonly statuses = RESULT_FILTER_STATUSES;
  protected readonly sampleStatuses = SAMPLE_FILTER_STATUSES;

  protected readonly page = computed(() => (this.facade.list.hasValue() ? this.facade.list.value() : null));
  protected readonly totalCount = computed(() => this.page()?.totalCount ?? 0);
  protected readonly counters = computed(() => this.page()?.counters ?? { requests: 0, tests: 0, samples: 0, delayed: 0 });

  protected readonly units = computed<DurationUnits>(() => ({
    days: this.i18n.t('results.unit.days'),
    hours: this.i18n.t('results.unit.hours'),
    minutes: this.i18n.t('results.unit.minutes'),
  }));

  protected readonly columns = computed<readonly TableColumn<WorklistItem>[]>(() => {
    const lang = this.i18n.lang();
    return [
      { key: 'patientNumber', header: 'results.worklist.column.patientNumber', mono: true, width: '140px', secondary: true },
      { key: 'accession', header: 'results.worklist.column.accession', mono: true, width: '140px', sortable: true },
      { key: 'sampleNumber', header: 'results.worklist.column.sample', mono: true, width: '140px', secondary: true },
      { key: 'patient', header: 'results.worklist.column.patient', value: (row) => patientLabel({ patientNumber: null, patientName: row.patientName, patientNameAr: row.patientNameAr }, lang), sortable: true },
      { key: 'ageSex', header: 'results.worklist.column.ageSex', width: '90px', secondary: true, value: (row) => this.ageSex(row) },
      { key: 'test', header: 'results.worklist.column.test', value: (row) => testLabel(row, lang), sortable: true },
      { key: 'status', header: 'results.worklist.column.status', width: '110px', sortable: true },
      { key: 'sampleStatus', header: 'results.worklist.column.sampleStatus', width: '110px', secondary: true },
      { key: 'collectedAtUtc', header: 'results.worklist.column.collected', width: '140px', secondary: true, value: (row) => (row.collectedAtUtc ? formatDateTime(row.collectedAtUtc, lang) : null) },
      { key: 'expectedAtUtc', header: 'results.worklist.column.expected', width: '140px', sortable: true, value: (row) => formatDateTime(row.expectedAtUtc, lang) },
      { key: 'delay', header: 'results.worklist.column.delay', width: '110px', value: (row) => row.delaySeconds, sortable: true },
      { key: 'consumed', header: 'results.worklist.column.consumed', width: '120px', value: (row) => row.consumedPercent, sortable: true },
    ];
  });

  constructor() {
    void this.i18n.load('results');
  }

  protected readonly rowId = (row: WorklistItem): string => row.resultId;

  protected readonly rowClass = (row: WorklistItem): string | null => delayClass(row.delayState) || null;

  protected onDay(field: 'fromDay' | 'toDay', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.facade.update({ [field]: value.length === 0 ? null : value });
  }

  protected onSelect(field: 'clinicalGroupId' | 'testId' | 'analyserId' | 'payerId', event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const id = value.length === 0 ? null : value;
    if (field === 'clinicalGroupId') {
      this.facade.setGroup(id);
    } else {
      this.facade.update({ [field]: id });
    }
  }

  protected onSampleStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.update({ sampleStatus: this.sampleStatuses.find((status) => status === value) ?? null });
  }

  protected onToggle(field: 'delayedOnly' | 'branchOnly', event: Event): void {
    this.facade.update({ [field]: (event.target as HTMLInputElement).checked });
  }

  protected hasStatus(status: ResultStatus): boolean {
    return this.facade.filter().statuses.includes(status);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected async exportCsv(): Promise<void> {
    const count = await this.facade.exportCsv.execute(undefined);
    if (count !== null) {
      this.toast.success(this.i18n.tf('results.worklist.exported', { count }));
    } else {
      this.toast.error(problemMessage(this.i18n, 'results', this.facade.exportCsv.problem()));
    }
  }

  protected async printWorklist(): Promise<void> {
    const doc = await this.facade.document.execute(undefined);
    if (doc === null) {
      this.toast.error(problemMessage(this.i18n, 'results', this.facade.document.problem()));
      return;
    }

    await this.printer.worklist(doc);
  }

  protected ageSex(row: WorklistItem): string {
    const parts: string[] = [];
    if (row.ageYears !== null) {
      parts.push(`${row.ageYears}${this.i18n.t('results.age.y')}`);
    }

    if (row.gender) {
      parts.push(this.i18n.t(`results.gender.${row.gender}`));
    }

    return parts.join(' · ');
  }

  protected delay(row: WorklistItem): string {
    return formatDuration(row.delaySeconds, this.units());
  }

  protected delayBadgeTone(row: WorklistItem): ReturnType<typeof delayTone> {
    return delayTone(row.delayState);
  }

  protected name(en: string | null, ar: string | null): string {
    return pickName(en, ar, this.i18n.lang());
  }

  protected statusLabel(status: string): string {
    return this.i18n.t(`results.status.${status}`);
  }

  protected sampleStatusLabel(status: string): string {
    return this.i18n.t(`results.sampleStatus.${status}`);
  }

  protected testLabelOf(row: WorklistItem): string {
    return testLabel(row, this.i18n.lang());
  }
}
