import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import { ProblemError } from '../../../core/api/problem-details';
import { type LabTrackerItem, type LabTrackerServiceItem, TRACKER_LINKS, isServiceRow } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { dateCell } from '../../../shared/export/table-export';
import { problemMessage } from '../../../shared/forms/problem-text';
import { PrintService } from '../../../shared/print/print.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { sameQuery, trackerQueryOf, trackerUrlQuery, trackerUrlState } from '../state/tracker-url';
import { TrackerFacade } from '../state/tracker.facade';
import { AttentionChipsComponent } from '../ui/attention-chips.component';
import { LayoutToggleComponent } from '../ui/layout-toggle.component';
import { NextStepComponent } from '../ui/next-step.component';
import { PeriodPickerComponent } from '../ui/period-picker.component';
import { PipelineStripComponent } from '../ui/pipeline-strip.component';
import { TrackerRowActionsComponent } from '../ui/row-actions.component';
import { RowsToggleComponent } from '../ui/rows-toggle.component';
import { TrackerServiceTableComponent } from '../ui/service-table.component';
import { TrackerWorklistDocumentComponent } from '../ui/tracker-worklist.document';
import { StatusPillComponent } from '../ui/status-pill.component';
import { type BoardLane, TrackerBoardComponent } from '../ui/tracker-board.component';
import { type StatusLook, clockTime, delayEdge, localToday, num, outsideLook, patientLine, payerLine, paymentLook, resultsLook, samplesLook, timeLeftLook } from '../ui/tracker-format';
import { ViewTabsComponent } from '../ui/view-tabs.component';

/**
 * Lab Tracker (LIS-CR-006, SRS CP-69): every visit of the laboratory day through registration → samples → results →
 * payment, as A · Pipeline board (table, server paged and sorted) or B · Stage lanes (board), over the same filters:
 * period (a day or a from / to range), search, desk view, stage (strip) and attention (chips). Live: re-read every 30 s while the tab is visible.
 * Sections the user may not see (samples, results, payment) arrive null from the server and read "—".
 * LIS-CR-014: an actions column like CP-10 (T1), *Visits | Services* rows (T3), and the whole filter in the URL query, read on
 * open and written on every change with replace state, so every action and next-step link carries `returnTo` = this URL (T5).
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
    RowActionsTemplateDirective,
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
    TrackerRowActionsComponent,
    RowsToggleComponent,
    TrackerServiceTableComponent,
  ],
  templateUrl: './tracker.page.html',
  styleUrl: './tracker.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackerPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TrackerFacade);
  private readonly print = inject(PrintService);
  private readonly toast = inject(ToastService);
  protected readonly accessionLink = TRACKER_LINKS.accession;

  protected readonly today = localToday();

  protected readonly columns: readonly TableColumn<LabTrackerItem>[] = [
    // LIS-CR-014: optional columns for the chooser (T2); exportValue = what Print / Excel write for a template cell (T4)
    { key: 'visit', header: 'tracker.column.visit', sortable: true, exportValue: (row) => [row.accession, this.patient(row), row.urgent ? this.i18n.t('tracker.urgent') : ''].filter((part) => part !== '').join(' · ') },
    {
      key: 'registration',
      header: 'tracker.column.registration',
      sortable: true,
      optional: true,
      exportValue: (row) => dateCell(row.registeredAtUtc, `${this.registeredAt(row)} · ${this.registrationLine(row)}`),
    },
    { key: 'samples', header: 'tracker.column.samples', optional: true, exportValue: (row) => lookText(this.samples(row), this.outside(row)) },
    { key: 'results', header: 'tracker.column.results', optional: true, exportValue: (row) => lookText(this.results(row)) },
    { key: 'payment', header: 'tracker.column.payment', optional: true, exportValue: (row) => lookText(this.payment(row)) },
    { key: 'nextStep', header: 'tracker.column.nextStep', sortable: true, exportValue: (row) => `${this.i18n.t('tracker.next.' + row.nextStep)} · ${this.i18n.t('tracker.stage.' + row.stage)}` },
    { key: 'timeLeft', header: 'tracker.column.timeLeft', sortable: true, width: '140px', optional: true, exportValue: (row) => lookText(this.timeLeft(row)) },
  ];

  /** LIS-CR-014 T3 and the change request of 2026-10-26: the service rows, shown with the worklist's columns. */
  protected readonly serviceRows = computed<readonly LabTrackerServiceItem[]>(() => this.facade.rows().filter(isServiceRow));

  /** The period and the branch under the title of a printed list. */
  protected readonly printSubtitle = computed(() => {
    const filter = this.facade.filter();
    const period = filter.from && filter.to ? `${filter.from} – ${filter.to}` : (this.facade.servedDay() ?? this.today);
    const branch = this.branchLabel();
    return branch ? `${period} · ${branch}` : period;
  });

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** This page's URL with its filter (LIS-CR-014 T5): the `returnTo` of every action and next-step link. */
  protected readonly returnTo = computed(() =>
    this.router.serializeUrl(this.router.createUrlTree([], { relativeTo: this.route, queryParams: trackerUrlQuery(this.facade.urlState()) })),
  );

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
    this.syncWithUrl();
    this.facade.refresh(); // the facade outlives the page (route provider): re-read on open
    this.facade.connect(inject(DestroyRef));
  }

  /**
   * LIS-CR-014 T5: a URL naming a tracker state wins on open (a return through `returnTo`, a shared link) and on the
   * browser's back / forward; a URL without one (the menu) keeps the facade's state. Every change of the state is written
   * back with replace state, so the history holds one entry per visit of the page and the URL is always the state.
   */
  private syncWithUrl(): void {
    const initial = trackerUrlState(this.route.snapshot.queryParamMap);
    if (initial !== null) {
      this.facade.restore(initial);
    }

    const params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
    let seen = params();
    effect(() => {
      const incoming = params();
      const state = this.facade.urlState();
      untracked(() => {
        if (incoming !== seen) {
          seen = incoming;
          const named = trackerUrlState(incoming);
          if (named !== null && !sameQuery(trackerUrlQuery(named), trackerUrlQuery(state))) {
            this.facade.restore(named);
            return;
          }
        }

        const wanted = trackerUrlQuery(state);
        if (!sameQuery(wanted, trackerQueryOf(incoming))) {
          void this.router.navigate([], { relativeTo: this.route, queryParams: wanted, replaceUrl: true });
        }
      });
    });
  }

  /** A visit row is keyed by its visit. */
  protected readonly rowId = (row: LabTrackerItem): string => row.visitId;

  protected readonly rowClass = (row: LabTrackerItem): string | null => delayEdge(row.delayState, row.minutesLeft);

  /** The worklist report of the service rows (change request of 2026-10-26), like the worklist page's *Print worklist*. */
  protected async printWorklist(): Promise<void> {
    const doc = await this.facade.worklistDocument.execute(undefined);
    if (doc === null) {
      this.toast.error(problemMessage(this.i18n, 'tracker', this.facade.worklistDocument.problem()));
      return;
    }

    await this.print.print({ title: this.i18n.t('tracker.report.title'), size: 'A4', landscape: true, component: { type: TrackerWorklistDocumentComponent, inputs: { doc } } });
  }

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

/** LIS-CR-014 T4: the printed / exported text of one or more status pills (absent sections read empty). */
function lookText(...looks: readonly (StatusLook | null)[]): string {
  return looks
    .filter((look): look is StatusLook => look !== null)
    .map((look) => look.text)
    .join(' · ');
}
