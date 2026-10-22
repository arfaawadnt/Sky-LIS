import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { type LabTrackerAttention, type LabTrackerCounters, TRACKER_ATTENTION, attentionCount } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent, type IconName } from '../../../shared/ui/icon/icon.component';
import { ATTENTION_ICONS, num } from './tracker-format';

interface Chip {
  readonly attention: LabTrackerAttention;
  readonly label: string;
  readonly count: string;
  readonly icon: IconName;
  readonly hot: boolean;
  readonly pressed: boolean;
}

/** "Needs attention": Delayed, Panic, Rejected, Ready but unpaid — toggle buttons with the day's counts (`aria-pressed`). */
@Component({
  selector: 'app-tracker-attention-chips',
  imports: [IconComponent],
  template: `
    <div class="chips" role="group" [attr.aria-label]="i18n.t('tracker.attention.label')">
      <span class="title">{{ i18n.t('tracker.attention.label') }}</span>
      @for (chip of chips(); track chip.attention) {
        <button type="button" class="chip" [class.hot]="chip.hot" [attr.aria-pressed]="chip.pressed" (click)="attentionToggle.emit(chip.attention)">
          <app-icon [name]="chip.icon" [size]="14" />
          {{ chip.label }}
          <span class="count">{{ chip.count }}</span>
        </button>
      }
    </div>
  `,
  styles: `
    .chips {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
    }

    .title {
      font-size: var(--fs-sm);
      font-weight: 600;
      color: var(--ink-2);
      margin-inline-end: var(--sp-1);
    }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 5px 12px;
      border-radius: var(--r-pill);
      border: 1px solid var(--line);
      background: var(--surface);
      color: var(--ink-2);
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
    }

    .chip:focus-visible {
      outline: none;
      box-shadow: var(--focus-ring);
    }

    .chip.hot {
      border-color: var(--bad);
      color: var(--bad);
    }

    .chip[aria-pressed='true'] {
      background: var(--bad-soft);
      border-color: var(--bad);
      color: var(--bad);
    }

    .count {
      background: var(--line-soft);
      color: var(--ink-2);
      border-radius: var(--r-pill);
      padding: 0 7px;
      font-size: var(--fs-xs);
    }

    .hot .count {
      background: var(--bad);
      color: var(--paper);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttentionChipsComponent {
  protected readonly i18n = inject(I18nService);

  readonly counters = input.required<LabTrackerCounters>();
  readonly attention = input<LabTrackerAttention | null>(null);
  readonly attentionToggle = output<LabTrackerAttention>();

  protected readonly chips = computed<readonly Chip[]>(() => {
    const counters = this.counters();
    const lang = this.i18n.lang();
    return TRACKER_ATTENTION.map((attention) => {
      const count = attentionCount(counters, attention);
      return { attention, label: this.i18n.t(`tracker.attention.${attention}`), count: num(count, lang), icon: ATTENTION_ICONS[attention], hot: count > 0, pressed: this.attention() === attention };
    });
  });
}
