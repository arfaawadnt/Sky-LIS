import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { SAMPLES_LINKS, SAMPLES_PERMISSIONS, type SampleListItem } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { VisitSamplesFacade } from '../state/visit-samples.facade';
import { ScanBoxComponent } from '../ui/scan-box.component';
import { formatDateTime, patientLabel, sampleTypeLabel, unitLabel } from '../ui/sample-format';
import { LABEL_SIZES, type LabelSize } from '../ui/sample-labels.document';

/**
 * CP-15 Samples by visit: look an accession up (or open it from another page with `?accession=` / `?visitId=`), see
 * every tube with unit, type, condition, status, status date and number, print one label or all labels (50×25 or
 * 38×25 mm, copies from the label document), open the job order and track a tube.
 */
@Component({
  selector: 'app-samples-visit-page',
  imports: [
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    BadgeComponent,
    IconComponent,
    EmptyStateComponent,
    ScanBoxComponent,
  ],
  templateUrl: './visit-samples.page.html',
  styleUrl: './visit-samples.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisitSamplesPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(VisitSamplesFacade);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  /** Query parameters (component input binding). */
  readonly accession = input<string>();
  readonly visitId = input<string>();

  protected readonly permissions = SAMPLES_PERMISSIONS;
  protected readonly links = SAMPLES_LINKS;
  protected readonly labelSizes = LABEL_SIZES;

  protected readonly columns: readonly TableColumn<SampleListItem>[] = [
    { key: 'unit', header: 'samples.column.unit', value: (row) => unitLabel(row, this.i18n.lang()), width: '160px' },
    { key: 'sampleType', header: 'samples.column.sampleType', value: (row) => sampleTypeLabel(row, this.i18n.lang()) },
    { key: 'status', header: 'samples.column.status', width: '120px' },
    { key: 'statusChangedAtUtc', header: 'samples.column.statusAt', value: (row) => formatDateTime(row.statusChangedAtUtc, this.i18n.lang()), width: '150px', secondary: true },
    { key: 'sampleNumber', header: 'samples.column.sampleNumber', mono: true, width: '170px' },
    { key: 'serviceCount', header: 'samples.column.services', align: 'end', width: '90px', secondary: true },
  ];

  protected readonly patient = computed(() => {
    const visit = this.facade.visit();
    return visit ? patientLabel(visit, this.i18n.lang()) : '';
  });

  protected readonly printableCount = computed(() => this.facade.rows().filter((row) => this.printable(row)).length);

  constructor() {
    void this.i18n.load('samples');
    effect(() => this.facade.show({ accession: this.accession() ?? null, visitId: this.visitId() ?? null }));
  }

  protected lookUp(accession: string): void {
    void this.router.navigate([], { queryParams: { accession: accession.trim(), visitId: null }, queryParamsHandling: 'merge' });
  }

  protected onLabelSize(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const size = this.labelSizes.find((candidate) => candidate === value);
    if (size) {
      this.facade.labels.size.set(size);
    }
  }

  protected labelSizeText(size: LabelSize): string {
    return this.i18n.t(`samples.labels.size.${size}`);
  }

  protected printable(row: SampleListItem): boolean {
    return row.status !== 'Replaced' && row.status !== 'Cancelled';
  }

  protected async printOne(row: SampleListItem): Promise<void> {
    const number = await this.facade.printOne.execute(row.id);
    if (number !== null) {
      this.toast.success(this.i18n.tf('samples.labels.printed', { count: 1 }));
    }
  }

  protected async printAll(): Promise<void> {
    const visit = this.facade.visit();
    if (!visit) {
      return;
    }

    const count = await this.facade.printAll.execute(visit.visitId);
    if (count !== null) {
      this.toast.success(this.i18n.tf('samples.labels.printed', { count }));
    }
  }

  protected async exportCsv(): Promise<void> {
    const visit = this.facade.visit();
    const count = await this.facade.exporting.execute(`samples-${visit?.accession ?? 'visit'}.csv`);
    if (count !== null) {
      this.toast.success(this.i18n.tf('samples.exported', { count }));
    }
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'samples', problem);
  }
}
