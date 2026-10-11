import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { count } from '../data/registration-format';
import type { SaveBlocker } from '../state/registration.facade';

/** One line of the "Ready to save" checklist: done, still open (with the blocker it jumps to) or waiting for the dry run. */
export interface ChecklistItem {
  readonly key: string;
  readonly state: 'done' | 'open' | 'pending';
  readonly label: string;
  readonly blocker: SaveBlocker | null;
}

/**
 * The "Ready to save" checklist of the registration page (UX option C's readiness list): a count with a progress bar and
 * one line per requirement; an open line is a button that asks the page to bring its field into view.
 */
@Component({
  selector: 'app-reg-readiness',
  imports: [IconComponent],
  template: `
    <div class="head">
      <span class="count" [class.done]="done() === items().length">{{ i18n.tf('registration.ready.count', { done: num(done()), total: num(items().length) }) }}</span>
      <span class="progress" aria-hidden="true"><span class="bar" [style.width.%]="percent()"></span></span>
    </div>
    <ul class="list">
      @for (item of items(); track item.key) {
        <li [class]="'item ' + item.state">
          <span class="mark" aria-hidden="true">
            @switch (item.state) {
              @case ('done') { <app-icon name="check" [size]="13" /> }
              @case ('pending') { … }
              @default { ! }
            }
          </span>
          @if (item.blocker !== null && item.state === 'open') {
            <button type="button" class="link" (click)="jump.emit(item.blocker)">{{ item.label }}</button>
          } @else {
            <span>{{ item.label }}</span>
          }
        </li>
      }
    </ul>
  `,
  styles: `
    .head {
      display: grid;
      gap: var(--sp-2);
      margin-block-end: var(--sp-3);
    }

    .count {
      font-weight: 700;
      color: var(--blue-3);
    }

    .count.done {
      color: var(--ok);
    }

    .progress {
      display: block;
      height: 6px;
      border-radius: var(--r-pill);
      background: var(--line-soft);
      overflow: hidden;
    }

    .bar {
      display: block;
      height: 100%;
      background: var(--blue);
      transition: width var(--ease);
    }

    .list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: var(--sp-1);
    }

    .item {
      display: flex;
      gap: var(--sp-2);
      align-items: center;
      min-height: 28px;
      font-size: var(--fs-sm);
    }

    .mark {
      flex: none;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 20px;
      height: 20px;
      border-radius: 50%;
      font-size: var(--fs-xs);
      font-weight: 700;
      color: #fff;
    }

    .done .mark {
      background: var(--ok);
    }

    .open .mark {
      background: var(--bad);
    }

    .pending {
      color: var(--ink-2);
    }

    .pending .mark {
      background: var(--line);
      color: var(--ink-2);
    }

    .link {
      padding: 0;
      border: 0;
      background: none;
      color: var(--bad);
      font: inherit;
      font-weight: 600;
      text-align: start;
      text-decoration: underline;
      text-underline-offset: 2px;
      cursor: pointer;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationReadinessComponent {
  protected readonly i18n = inject(I18nService);

  readonly items = input.required<readonly ChecklistItem[]>();
  /** The clerk chose an open line: the page focuses its field. */
  readonly jump = output<SaveBlocker>();

  protected readonly done = computed(() => this.items().filter((item) => item.state === 'done').length);

  /** Counts in the digits of the language (UAT A-07). */
  protected num(value: number): string {
    return count(value, this.i18n.lang());
  }

  protected readonly percent = computed(() => {
    const total = this.items().length;
    return total === 0 ? 100 : Math.round((this.done() / total) * 100);
  });
}
