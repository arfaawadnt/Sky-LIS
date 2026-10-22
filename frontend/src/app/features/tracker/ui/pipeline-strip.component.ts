import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { type LabTrackerCounters, type LabTrackerStage, TRACKER_LANES, stageCount } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent, type IconName } from '../../../shared/ui/icon/icon.component';
import { STAGE_ICONS, num } from './tracker-format';

interface Tile {
  readonly stage: LabTrackerStage | null;
  readonly label: string;
  readonly count: string;
  readonly icon: IconName;
  readonly pressed: boolean;
}

/**
 * Layout A's pipeline strip: Registered (the day's total) then the five stages, each a toggle button filtering by its
 * stage (`aria-pressed`); Registered shows every stage again. Counts are the whole day's.
 */
@Component({
  selector: 'app-tracker-pipeline-strip',
  imports: [IconComponent],
  template: `
    <div class="strip" role="group" [attr.aria-label]="i18n.t('tracker.strip.label')">
      @for (tile of tiles(); track tile.stage) {
        <button type="button" class="tile" [class]="'tile ' + (tile.stage ?? 'all')" [attr.aria-pressed]="tile.pressed" (click)="stageToggle.emit(tile.stage)">
          <span class="ico"><app-icon [name]="tile.icon" [size]="16" /></span>
          <span class="count">{{ tile.count }}</span>
          <span class="lbl">{{ tile.label }}</span>
        </button>
      }
    </div>
  `,
  styles: `
    .strip {
      display: grid;
      grid-template-columns: repeat(6, minmax(120px, 1fr));
      gap: var(--sp-2);
      overflow-x: auto;
    }

    .tile {
      display: grid;
      grid-template-columns: auto 1fr;
      align-items: center;
      gap: 2px var(--sp-2);
      padding: var(--sp-3);
      text-align: start;
      background: var(--surface);
      border: 1px solid var(--line);
      border-block-start: 3px solid var(--line);
      border-radius: var(--r);
      box-shadow: var(--sh);
      color: var(--ink);
      cursor: pointer;
    }

    .tile:hover {
      background: var(--row-hover);
    }

    .tile:focus-visible {
      outline: none;
      box-shadow: var(--focus-ring);
    }

    .tile[aria-pressed='true'] {
      background: var(--violet-soft);
      border-color: var(--violet);
    }

    .ico {
      grid-row: span 2;
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      border-radius: 9px;
      background: var(--surface-2);
      color: var(--ink-2);
    }

    .count {
      font-size: var(--fs-xl);
      font-weight: 800;
      color: var(--navy);
      line-height: 1.1;
    }

    .lbl {
      font-size: var(--fs-sm);
      color: var(--muted);
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

    .all {
      border-block-start-color: var(--navy);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PipelineStripComponent {
  protected readonly i18n = inject(I18nService);

  readonly counters = input.required<LabTrackerCounters>();
  readonly stage = input<LabTrackerStage | null>(null);
  readonly stageToggle = output<LabTrackerStage | null>();

  protected readonly tiles = computed<readonly Tile[]>(() => {
    const counters = this.counters();
    const lang = this.i18n.lang();
    const selected = this.stage();
    return [
      { stage: null, label: this.i18n.t('tracker.stage.Registered'), count: num(counters.total, lang), icon: 'clipboard', pressed: selected === null },
      ...TRACKER_LANES.map((stage) => ({ stage, label: this.i18n.t(`tracker.stage.${stage}`), count: num(stageCount(counters, stage), lang), icon: STAGE_ICONS[stage], pressed: selected === stage })),
    ];
  });
}
