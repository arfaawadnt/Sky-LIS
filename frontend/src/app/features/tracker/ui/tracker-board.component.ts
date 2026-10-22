import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import type { LabTrackerItem, LabTrackerStage } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { STAGE_ICONS, num } from './tracker-format';
import { VisitCardComponent } from './visit-card.component';

/** One lane as the board shows it: the first cards of its stage and how many the stage holds. */
export interface BoardLane {
  readonly stage: LabTrackerStage;
  readonly items: readonly LabTrackerItem[];
  readonly totalCount: number;
  readonly loading: boolean;
  readonly failed: boolean;
}

/**
 * Layout B · Stage lanes: one column per stage with its cards; "+ n more" opens the table filtered by that stage. The
 * lanes scroll sideways on narrow screens.
 */
@Component({
  selector: 'app-tracker-board',
  imports: [IconComponent, VisitCardComponent],
  template: `
    <div class="lanes">
      @for (lane of lanes(); track lane.stage) {
        <section class="lane" [class]="'lane ' + lane.stage" [attr.aria-labelledby]="'lane-' + lane.stage" [attr.aria-busy]="lane.loading ? 'true' : null">
          <h2 class="head" [id]="'lane-' + lane.stage">
            <app-icon [name]="icons[lane.stage]" [size]="15" />
            <span>{{ i18n.t('tracker.stage.' + lane.stage) }}</span>
            <span class="count">{{ count(lane.totalCount) }}</span>
          </h2>
          @if (lane.failed) {
            <p class="problem" role="alert">{{ i18n.t('tracker.loadError') }}</p>
          } @else if (lane.items.length === 0) {
            <p class="empty">{{ lane.loading ? i18n.t('tracker.loading') : i18n.t('tracker.board.empty') }}</p>
          }
          <ul class="cards">
            @for (item of lane.items; track item.visitId) {
              <li><app-tracker-visit-card [item]="item" /></li>
            }
          </ul>
          @if (lane.totalCount > lane.items.length) {
            <button type="button" class="btn ghost sm more" (click)="more.emit(lane.stage)">{{ i18n.tf('tracker.board.more', { count: count(lane.totalCount - lane.items.length) }) }}</button>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .lanes {
      display: grid;
      grid-auto-flow: column;
      grid-auto-columns: minmax(250px, 1fr);
      gap: var(--sp-3);
      overflow-x: auto;
      padding-block-end: var(--sp-2);
    }

    .lane {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      min-width: 0;
      padding: var(--sp-2);
      background: var(--surface-2);
      border: 1px solid var(--line);
      border-block-start: 3px solid var(--line);
      border-radius: var(--r-lg);
    }

    .AwaitingSample {
      border-block-start-color: var(--warn);
    }

    .InLab {
      border-block-start-color: var(--info);
    }

    .AwaitingVerification {
      border-block-start-color: var(--violet);
    }

    .Ready {
      border-block-start-color: var(--ok);
    }

    .Delivered {
      border-block-start-color: var(--muted);
    }

    .head {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0;
      padding: 4px;
      font-size: var(--fs);
      color: var(--navy);
    }

    .count {
      margin-inline-start: auto;
      background: var(--line-soft);
      border-radius: var(--r-pill);
      padding: 0 8px;
      font-size: var(--fs-xs);
      color: var(--ink-2);
    }

    .cards {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--sp-2);
    }

    .empty {
      margin: 0;
      padding: var(--sp-3);
      text-align: center;
      font-size: var(--fs-sm);
      color: var(--muted);
    }

    .more {
      align-self: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackerBoardComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly icons = STAGE_ICONS;

  readonly lanes = input.required<readonly BoardLane[]>();
  readonly more = output<LabTrackerStage>();

  protected count(value: number): string {
    return num(value, this.i18n.lang());
  }
}
