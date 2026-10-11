import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { count } from '../data/registration-format';
import type { SaveBlocker } from '../state/registration.facade';

/** An open item of the save, as the bar shows it. */
export interface BarProblem {
  readonly blocker: SaveBlocker;
  readonly text: string;
}

/**
 * The sticky bottom bar of the registration page (UX option B): every open item as a chip that jumps to its field, the
 * notices the save will ask to confirm, the amount due and the Save button (Ctrl+S is handled by the page). Sits inside
 * the page's save-shortcut host so the shortcut also works while the focus is on the bar.
 */
@Component({
  selector: 'app-reg-action-bar',
  imports: [IconComponent],
  host: { role: 'region', '[attr.aria-label]': "i18n.t('registration.bar.label')" },
  template: `
    <div class="status">
      @if (problems().length > 0) {
        <span class="title bad">{{ i18n.tf('registration.bar.toFix', { count: num(problems().length) }) }}</span>
        @for (problem of problems(); track problem.blocker) {
          <button type="button" class="chip bad" (click)="jump.emit(problem.blocker)">{{ problem.text }}</button>
        }
      } @else {
        <span class="title ok"><app-icon name="check" [size]="15" /> {{ i18n.t('registration.bar.ready') }}</span>
      }
      @for (notice of notices(); track $index) {
        <span class="chip warn">{{ notice }}</span>
      }
    </div>
    <div class="actions">
      <span class="due"><span class="small muted">{{ dueLabel() }}</span> <b>{{ due() }}</b></span>
      <button type="button" class="btn primary save" [disabled]="saving()" (click)="save.emit()">
        <app-icon name="check" [size]="15" />{{ saveLabel() }}
        <kbd>Ctrl+S</kbd>
      </button>
    </div>
  `,
  styles: `
    :host {
      position: sticky;
      inset-block-end: 0;
      z-index: var(--z-sticky);
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      border: 1px solid var(--line);
      border-radius: var(--r-lg) var(--r-lg) 0 0;
      background: var(--surface);
      box-shadow: 0 -10px 24px -16px rgba(11, 42, 91, 0.35);
    }

    .status {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
      min-width: 0;
    }

    .title {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-1);
      font-weight: 700;
    }

    .title.bad {
      color: var(--bad);
    }

    .title.ok {
      color: var(--ok);
    }

    .chip {
      display: inline-flex;
      align-items: center;
      min-height: 30px;
      padding: 0 var(--sp-3);
      border: 0;
      border-radius: var(--r-pill);
      font: inherit;
      font-size: var(--fs-sm);
    }

    .chip.bad {
      background: var(--bad-soft);
      color: var(--bad);
      font-weight: 600;
      cursor: pointer;
    }

    .chip.bad:hover {
      text-decoration: underline;
    }

    .chip.warn {
      background: var(--warn-soft);
      color: var(--warn);
    }

    .actions {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      margin-inline-start: auto;
    }

    .due {
      display: inline-flex;
      align-items: baseline;
      gap: var(--sp-2);
      font-variant-numeric: tabular-nums;
    }

    .due b {
      font-size: var(--fs-lg);
    }

    .save {
      min-height: 44px;
      padding-inline: var(--sp-5);
    }

    kbd {
      margin-inline-start: var(--sp-2);
      padding: 0 var(--sp-1);
      border-radius: var(--r-sm);
      background: rgba(255, 255, 255, 0.2);
      font-family: var(--mono);
      font-size: var(--fs-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationActionBarComponent {
  protected readonly i18n = inject(I18nService);

  readonly problems = input.required<readonly BarProblem[]>();
  /** Notices that do not block the save but will be confirmed (frequency alerts). */
  readonly notices = input<readonly string[]>([]);
  readonly dueLabel = input.required<string>();
  /** The amount with its currency, already formatted. */
  readonly due = input.required<string>();
  readonly saveLabel = input.required<string>();
  readonly saving = input(false);

  readonly jump = output<SaveBlocker>();
  readonly save = output<void>();

  /** Counts in the digits of the language (UAT A-07). */
  protected num(value: number): string {
    return count(value, this.i18n.lang());
  }
}
