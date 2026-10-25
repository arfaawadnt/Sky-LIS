import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import { ProblemError } from '../../../core/api/problem-details';
import { type LabTrackerItem, TRACKER_LINKS } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { TrackerFacade } from '../state/tracker.facade';
import { AttentionChipsComponent } from '../ui/attention-chips.component';
import { LayoutToggleComponent } from '../ui/layout-toggle.component';
import { NextStepComponent } from '../ui/next-step.component';
import { PeriodPickerComponent } from '../ui/period-picker.component';
import { PipelineStripComponent } from '../ui/pipeline-strip.component';
import { StatusPillComponent } from '../ui/status-pill.component';
import { type BoardLane, TrackerBoardComponent } from '../ui/tracker-board.component';
import { type StatusLook, clockTime, delayEdge, localToday, num, outsideLook, patientLine, payerLine, paymentLook, resultsLook, samplesLook, timeLeftLook } from '../ui/tracker-format';
import { ViewTabsComponent } from '../ui/view-tabs.component';

/**
 * Lab Tracker (LIS-CR-006, SRS CP-69): every visit of the laboratory day through registration → samples → results →
 * payment, as A · Pipeline board (table, server paged and sorted) or B · Stage lanes (board), over the same filters:
 * period (a day or a from / to range), search, desk view, stage (strip) and attention (chips). Live: re-read every 30 s while the tab is visible.
 * Sections the user may not see (samples, results, payment) arrive null from the server and read "—".
 */
@Component({
  selector: 'app-tracker-page',
  imports: [
    RouterLink,
    PageHeaderComponent,
    SectionCardComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    ViewTabsComponent,
    PeriodPickerComponent,
    PipelineStripComponent,
    AttentionChipsComponent,
    LayoutToggleComponent,
    StatusPillComponent,
    NextStepComponent,
    TrackerBoardComponent,
  ],
  templateUrl: './tracker.page.html',
  styleUrl: './tracker.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackerPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TrackerFacade);
  protected readonly accessionLink = TRACKER_LINKS.accession;

  protected readonly today = localToday();

  protected readonly columns: readonly TableColumn<LabTrackerItem>[] = [
    { key: 'visit', header: 'tracker.column.visit', sortable: true },
    { key: 'registration', header: 'tracker.column.registration', sortable: true },
    { key: 'samples', header: 'tracker.column.samples' },
    { key: 'results', header: 'tracker.column.results' },
    { key: 'payment', header: 'tracker.column.payment' },
    { key: 'nextStep', header: 'tracker.column.nextStep', sortable: true },
    { key: 'timeLeft', header: 'tracker.column.timeLeft', sortable: true, width: '140px' },
  ];

  protected readonly page = computed(() => (this.facade.list.hasValue() ? this.facade.list.value() : null));

  protected readonly branchLabel = computed(() => {
    const branch = this.facade.branch.hasValue() ? this.facade.branch.value() : null;
    return branch ? `${branch.code} · ${this.i18n.lang() === 'ar' ? branch.nameAr : branch.nameEn}` : null;
  });

  /** "Live · updated 12 s ago" / "Paused" while the tab is hidden. */
  protected readonly liveText = computed(() => {
    if (!this.facade.live()) {
      return this.i18n.t('tracker.live.paused');
    }

    const seconds = this.facade.secondsSinceUpdate();
    const lang = this.i18n.lang();
    if (seconds === null) {
      return this.i18n.t('tracker.live.waiting');
    }

    if (seconds < 5) {
      return this.i18n.t('tracker.live.justNow');
    }

    if (seconds < 60) {
      return this.i18n.tc('tracker.live.seconds', seconds, { count: num(seconds, lang) });
    }

    const minutes = Math.floor(seconds / 60);
    return this.i18n.tc('tracker.live.minutes', minutes, { count: num(minutes, lang) });
  });

  protected readonly boardLanes = computed<readonly BoardLane[]>(() =>
    this.facade.visibleLanes().map((lane) => {
      const page = lane.page.hasValue() ? lane.page.value() : null;
      return { stage: lane.stage, items: page?.items ?? [], totalCount: page?.totalCount ?? 0, loading: lane.page.isLoading(), failed: lane.page.error() !== undefined };
    }),
  );

  protected readonly problem = computed(() => {
    const error = this.facade.error();
    if (error === undefined || error === null || this.facade.layout() === 'board') {
      return null;
    }

    return problemMessage(this.i18n, 'tracker', error instanceof ProblemError ? error.problem : { code: 'INTERNAL-500' });
  });

  constructor() {
    void this.i18n.load('tracker');
    this.facade.refresh(); // the facade outlives the page (route provider): re-read on open
    this.facade.connect(inject(DestroyRef));
  }

  protected readonly rowId = (row: LabTrackerItem): string => row.visitId;

  protected readonly rowClass = (row: LabTrackerItem): string | null => delayEdge(row.delayState, row.minutesLeft);

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected patient(row: LabTrackerItem): string {
    return patientLine(row, this.i18n);
  }

  protected registeredAt(row: LabTrackerItem): string {
    return clockTime(row.registeredAtUtc, this.i18n.lang());
  }

  /** "3 services · Cash · Gold". */
  protected registrationLine(row: LabTrackerItem): string {
    const lang = this.i18n.lang();
    const live = row.registration.lines - row.registration.cancelledLines;
    const payer = payerLine(row, lang);
    const services = this.i18n.tc('tracker.services', live, { count: num(live, lang) });
    return payer === null ? services : `${services} · ${payer}`;
  }

  protected samples(row: LabTrackerItem): StatusLook | null {
    return samplesLook(row, this.i18n);
  }

  /** The "ext" tag of the samples cell (LIS-CR-008 C-02). */
  protected outside(row: LabTrackerItem): StatusLook | null {
    return outsideLook(row, this.i18n);
  }

  protected results(row: LabTrackerItem): StatusLook | null {
    return resultsLook(row, this.i18n);
  }

  protected payment(row: LabTrackerItem): StatusLook | null {
    return paymentLook(row, this.i18n);
  }

  protected timeLeft(row: LabTrackerItem): StatusLook | null {
    return timeLeftLook(row.minutesLeft, row.delayState, this.i18n);
  }

  protected count(value: number): string {
    return num(value, this.i18n.lang());
  }
}
