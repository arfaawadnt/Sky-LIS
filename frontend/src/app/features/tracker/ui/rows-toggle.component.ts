import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import type { TrackerRowMode } from '../state/tracker.facade';

/** The *Visits | Services* segmented toggle of the table (LIS-CR-014 T3; `aria-pressed` on each half). */
@Component({
  selector: 'app-tracker-rows-toggle',
  imports: [IconComponent],
  template: `
    <div class="seg" role="group" [attr.aria-label]="i18n.t('tracker.rows.label')">
      <button type="button" [attr.aria-pressed]="rows() === 'visits'" (click)="rowsChange.emit('visits')">
        <app-icon name="user" [size]="14" />{{ i18n.t('tracker.rows.visits') }}
      </button>
      <button type="button" [attr.aria-pressed]="rows() === 'services'" (click)="rowsChange.emit('services')">
        <app-icon name="flask" [size]="14" />{{ i18n.t('tracker.rows.services') }}
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
export class RowsToggleComponent {
  protected readonly i18n = inject(I18nService);

  readonly rows = input.required<TrackerRowMode>();
  readonly rowsChange = output<TrackerRowMode>();
}
