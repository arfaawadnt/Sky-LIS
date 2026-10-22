import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import type { TrackerLayout } from '../state/tracker.facade';

/** The *Table | Board* segmented toggle (`aria-pressed` on each half). */
@Component({
  selector: 'app-tracker-layout-toggle',
  imports: [IconComponent],
  template: `
    <div class="seg" role="group" [attr.aria-label]="i18n.t('tracker.layout.label')">
      <button type="button" [attr.aria-pressed]="layout() === 'table'" (click)="layoutChange.emit('table')">
        <app-icon name="menu" [size]="14" />{{ i18n.t('tracker.layout.table') }}
      </button>
      <button type="button" [attr.aria-pressed]="layout() === 'board'" (click)="layoutChange.emit('board')">
        <app-icon name="grid" [size]="14" />{{ i18n.t('tracker.layout.board') }}
      </button>
    </div>
  `,
  styles: `
    .seg {
      display: inline-flex;
      border: 1px solid var(--line);
      border-radius: var(--r-sm);
      overflow: hidden;
      background: var(--surface);
    }

    button {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      border: 0;
      background: none;
      color: var(--ink-2);
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
    }

    button + button {
      border-inline-start: 1px solid var(--line);
    }

    button[aria-pressed='true'] {
      background: var(--navy);
      color: var(--paper);
    }

    button:focus-visible {
      outline: none;
      box-shadow: var(--focus-ring);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LayoutToggleComponent {
  protected readonly i18n = inject(I18nService);

  readonly layout = input.required<TrackerLayout>();
  readonly layoutChange = output<TrackerLayout>();
}
