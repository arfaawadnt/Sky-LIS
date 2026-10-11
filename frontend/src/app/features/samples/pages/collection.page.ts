import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { SAMPLES_LINKS, SAMPLES_PERMISSIONS, type SampleListItem, type SampleStatus } from '../../../core/api/samples.contracts';
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
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { type BulkAction, CollectionFacade, type ScanOutcome } from '../state/collection.facade';
import { ScanBoxComponent } from '../ui/scan-box.component';
import { formatDateTime, patientLabel, sampleTypeLabel, unitLabel } from '../ui/sample-format';

/**
 * CP-14 Collection & receipt (WF-05 steps 1–2): scan a tube in Collect or Receive mode with immediate visual
 * feedback, collect or receive every tube of an accession, and follow today's tubes of the branch by status.
 * Actions are gated by `*appCan`; the server decides (D-04) and its refusals are shown as their codes' texts.
 */
@Component({
  selector: 'app-samples-collection-page',
  imports: [
    ReactiveFormsModule,
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
    ScanBoxComponent,
  ],
  templateUrl: './collection.page.html',
  styleUrl: './collection.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(CollectionFacade);
  private readonly toast = inject(ToastService);

  protected readonly permissions = SAMPLES_PERMISSIONS;
  protected readonly links = SAMPLES_LINKS;
  protected readonly statusFilters: readonly (SampleStatus | null)[] = [null, 'Ordered', 'Collected', 'Received', 'Rejected'];

  protected readonly accession = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(40)] });

  protected readonly columns: readonly TableColumn<SampleListItem>[] = [
    { key: 'sampleNumber', header: 'samples.column.sampleNumber', mono: true, sortable: true, width: '170px' },
    { key: 'accession', header: 'samples.column.accession', mono: true, sortable: true, width: '150px' },
    { key: 'patient', header: 'samples.column.patient', value: (row) => patientLabel(row, this.i18n.lang()) },
    { key: 'unit', header: 'samples.column.unit', value: (row) => unitLabel(row, this.i18n.lang()), secondary: true },
    { key: 'sampleType', header: 'samples.column.sampleType', value: (row) => sampleTypeLabel(row, this.i18n.lang()) },
    { key: 'serviceCount', header: 'samples.column.services', align: 'end', width: '90px', secondary: true },
    { key: 'status', header: 'samples.column.status', width: '120px', sortable: true },
    { key: 'statusChangedAtUtc', header: 'samples.column.statusAt', value: (row) => formatDateTime(row.statusChangedAtUtc, this.i18n.lang()), secondary: true, width: '150px' },
  ];

  protected readonly rows = computed<readonly SampleListItem[]>(() => (this.facade.today.hasValue() ? this.facade.today.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.today.hasValue() ? this.facade.today.value().totalCount : 0));

  /** The hint under the scan title: what the current mode does under the tenant's parameters. */
  protected readonly modeHint = computed(() => {
    const settings = this.facade.settings.hasValue() ? this.facade.settings.value() : null;
    if (this.facade.mode() === 'Collect') {
      return this.i18n.t(settings?.autoReceive ? 'samples.collection.hint.collectAndReceive' : 'samples.collection.hint.collect');
    }

    return this.i18n.t('samples.collection.hint.receive');
  });

  constructor() {
    void this.i18n.load('samples');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
  }

  protected async onScan(value: string): Promise<void> {
    await this.facade.scanNumber(value);
  }

  /** Enter in the accession field runs the action of the current scan mode (collect all / receive all). */
  protected onBulkSubmit(event: Event): void {
    event.preventDefault();
    void this.runBulk(this.facade.mode() === 'Collect' ? 'collect' : 'receive');
  }

  protected async runBulk(action: BulkAction): Promise<void> {
    if (this.accession.invalid || this.facade.bulk.pending()) {
      this.accession.markAsTouched();
      return;
    }

    const result = await this.facade.bulk.execute({ accession: this.accession.value.trim(), action });
    if (result !== null) {
      this.toast.success(this.i18n.tf(`samples.collection.bulkDone.${action}`, { changed: result.changed, unchanged: result.unchanged, accession: result.accession }));
    }
  }

  protected async exportCsv(): Promise<void> {
    const count = await this.facade.exporting.execute(`samples-today-${new Date().toISOString().slice(0, 10)}.csv`);
    if (count !== null) {
      this.toast.success(this.i18n.t('samples.exported'));
    }
  }

  protected onStatus(status: SampleStatus | null): void {
    this.facade.setStatus(status);
  }

  protected onBranchOnly(event: Event): void {
    this.facade.setBranchOnly((event.target as HTMLInputElement).checked);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected statusLabel(status: SampleStatus | null): string {
    return status === null ? this.i18n.t('samples.allStatuses') : this.i18n.t(`shared.status.${status}`);
  }

  protected outcomeText(outcome: ScanOutcome): string {
    if (outcome.ok && outcome.detail) {
      return this.i18n.tf(`samples.scan.ok.${outcome.detail.summary.status}`, { number: outcome.sampleNumber });
    }

    return this.problemText(outcome.problem);
  }

  protected outcomeDetails(outcome: ScanOutcome): string {
    const summary = outcome.detail?.summary;
    if (!summary) {
      return '';
    }

    const lang = this.i18n.lang();
    return [summary.accession, patientLabel(summary, lang), sampleTypeLabel(summary, lang)].filter((part) => part.length > 0).join(' · ');
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'samples', problem);
  }

  protected when(iso: string): string {
    return formatDateTime(iso, this.i18n.lang());
  }
}
