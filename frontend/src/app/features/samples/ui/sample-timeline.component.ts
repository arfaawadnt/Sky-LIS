import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { SampleTimelineStep } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { type DurationUnits, formatDateTime, formatDuration, pickName } from './sample-format';

/** The CP-17 timeline: one entry per step (action, resulting status, time, user, location, reason or note) with the time since the previous step. */
@Component({
  selector: 'app-sample-timeline',
  imports: [BadgeComponent],
  template: `
    <ol class="timeline" [attr.aria-label]="i18n.t('samples.tracking.timeline')">
      @for (step of steps(); track step.id) {
        <li class="step" [class.bad]="step.action === 'Rejected' || step.action === 'Cancelled'">
          <span class="dot" aria-hidden="true"></span>
          <div class="body">
            <div class="line">
              <b>{{ i18n.t('samples.action.' + step.action) }}</b>
              <app-badge [status]="step.status" />
              @if (step.sincePreviousSeconds !== null) {
                <span class="since" [attr.title]="i18n.t('samples.tracking.sincePrevious')">+{{ duration(step.sincePreviousSeconds) }}</span>
              }
            </div>
            <div class="meta muted small">
              <span>{{ when(step.atUtc) }}</span>
              @if (step.userName) {
                <span>{{ step.userName }}</span>
              }
              @if (branch(step); as branchName) {
                <span>{{ branchName }}</span>
              }
            </div>
            @if (reason(step); as reasonName) {
              <div class="note"><span class="muted">{{ i18n.t('samples.column.reason') }}:</span> {{ reasonName }}</div>
            }
            @if (step.note) {
              <div class="note">
                @if (step.action === 'ReplacementOrdered') {
                  <span class="muted">{{ i18n.t('samples.summary.replaces') }}:</span>
                }
                {{ step.note }}
              </div>
            }
          </div>
        </li>
      } @empty {
        <li class="muted">{{ i18n.t('samples.tracking.noSteps') }}</li>
      }
    </ol>
  `,
  styles: `
    .timeline {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--sp-3);
    }

    .step {
      position: relative;
      display: grid;
      grid-template-columns: 14px minmax(0, 1fr);
      gap: var(--sp-3);
    }

    .step:not(:last-child)::after {
      content: '';
      position: absolute;
      inset-inline-start: 6px;
      inset-block: 18px calc(-1 * var(--sp-3));
      border-inline-start: 2px solid var(--line);
    }

    .dot {
      width: 14px;
      height: 14px;
      margin-block-start: 3px;
      border-radius: 50%;
      background: var(--blue);
      box-shadow: 0 0 0 3px var(--blue-soft);
    }

    .step.bad .dot {
      background: var(--bad);
      box-shadow: 0 0 0 3px var(--bad-soft);
    }

    .line {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
    }

    .since {
      font-size: var(--fs-xs);
      color: var(--ink-2);
      background: var(--surface-2);
      border-radius: var(--r-pill);
      padding: 1px var(--sp-2);
    }

    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-1) var(--sp-3);
    }

    .note {
      font-size: var(--fs-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SampleTimelineComponent {
  protected readonly i18n = inject(I18nService);

  readonly steps = input.required<readonly SampleTimelineStep[]>();

  protected readonly units = computed<DurationUnits>(() => ({
    days: this.i18n.t('samples.unit.days'),
    hours: this.i18n.t('samples.unit.hours'),
    minutes: this.i18n.t('samples.unit.minutes'),
    seconds: this.i18n.t('samples.unit.seconds'),
  }));

  protected duration(seconds: number | null): string {
    return formatDuration(seconds, this.units());
  }

  protected when(iso: string): string {
    return formatDateTime(iso, this.i18n.lang());
  }

  protected branch(step: SampleTimelineStep): string {
    return pickName(step.branchNameEn, step.branchNameAr, this.i18n.lang());
  }

  protected reason(step: SampleTimelineStep): string {
    if (step.reasonId === null) {
      return '';
    }

    return pickName(step.reasonNameEn, step.reasonNameAr, this.i18n.lang(), this.i18n.t('samples.summary.reasonUnknown'));
  }
}
