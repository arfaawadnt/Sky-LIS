import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { IconComponent, type IconName } from '../icon/icon.component';

/**
 * `<app-empty-state [title]="…" [message]="…" icon="inbox"><button emptyAction>…</button></app-empty-state>`:
 * what a list or panel shows when there is nothing to show (prototype `.empty`). Texts are passed translated.
 */
@Component({
  selector: 'app-empty-state',
  imports: [IconComponent],
  template: `
    <div class="empty">
      <div class="glyph"><app-icon [name]="icon()" [size]="26" /></div>
      <p class="title">{{ title() }}</p>
      @if (message(); as message) {
        <p class="message">{{ message }}</p>
      }
      <div class="action"><ng-content select="[emptyAction]" /></div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .empty {
      padding: var(--sp-6) var(--sp-4);
      text-align: center;
      color: var(--muted);
      display: grid;
      justify-items: center;
      gap: var(--sp-1);
    }

    .glyph {
      width: 48px;
      height: 48px;
      border-radius: 14px;
      background: var(--grad-soft);
      color: var(--violet);
      display: grid;
      place-items: center;
      margin-bottom: var(--sp-2);
    }

    .title {
      margin: 0;
      color: var(--ink-2);
      font-weight: 600;
    }

    .message {
      margin: 0;
      font-size: var(--fs-sm);
      max-width: 48ch;
    }

    .action {
      margin-top: var(--sp-3);
    }

    .action:empty {
      display: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly message = input<string | null>(null);
  readonly icon = input<IconName>('inbox');
}
