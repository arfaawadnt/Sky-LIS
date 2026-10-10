import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { Router } from '@angular/router';

import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { SAMPLES_LINKS } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { TrackingFacade } from '../state/tracking.facade';
import { ScanBoxComponent } from '../ui/scan-box.component';
import { type DurationUnits, formatDuration, normalizeSampleNumber } from '../ui/sample-format';
import { SampleSummaryComponent } from '../ui/sample-summary.component';
import { SampleTimelineComponent } from '../ui/sample-timeline.component';

/**
 * CP-17 Sample tracking (WF-05 step 5): scan or type a barcode — or open `/samples/track/:sampleNumber` from the
 * worklist, results or another sample page — to see where the tube was and when: the timeline with user, location
 * and time of every step, the time between steps and the stage durations.
 */
@Component({
  selector: 'app-samples-tracking-page',
  imports: [PageHeaderComponent, SectionCardComponent, StatCardComponent, EmptyStateComponent, ScanBoxComponent, SampleSummaryComponent, SampleTimelineComponent],
  templateUrl: './tracking.page.html',
  styleUrl: './tracking.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackingPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TrackingFacade);
  private readonly router = inject(Router);

  /** The `:sampleNumber` route parameter (component input binding). */
  readonly sampleNumber = input<string>();

  protected readonly problem = computed<ProblemDetails | null>(() => {
    const error = this.facade.timeline.error();
    if (!error) {
      return null;
    }

    return error instanceof ProblemError ? error.problem : { type: 'about:blank', title: '', status: 0, code: 'INTERNAL-500', traceId: null, correlationId: null, errors: null };
  });

  protected readonly units = computed<DurationUnits>(() => ({
    days: this.i18n.t('samples.unit.days'),
    hours: this.i18n.t('samples.unit.hours'),
    minutes: this.i18n.t('samples.unit.minutes'),
    seconds: this.i18n.t('samples.unit.seconds'),
  }));

  constructor() {
    void this.i18n.load('samples');
    effect(() => {
      const number = this.sampleNumber();
      untracked(() => this.facade.track(number ?? null));
    });
  }

  protected track(value: string): void {
    const number = normalizeSampleNumber(value);
    if (number === this.facade.sampleNumber()) {
      this.facade.timeline.reload();
      return;
    }

    void this.router.navigateByUrl(SAMPLES_LINKS.track(number));
  }

  protected duration(seconds: number | null): string {
    return formatDuration(seconds, this.units());
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'samples', problem);
  }
}
