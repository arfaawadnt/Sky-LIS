import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { type BadgeTone, toneOf } from './status-tones';

/**
 * `<app-badge tone="ok">Approved</app-badge>` or `<app-badge [status]="row.status" />`: a pill. With `status`
 * the tone comes from `STATUS_TONES` and, when nothing is projected, the label from `shared.status.<status>`;
 * `tone` overrides the mapped tone.
 */
@Component({
  selector: 'app-badge',
  template: `<span class="badge" [class]="'badge ' + effectiveTone()" [attr.data-status]="status()"><ng-content>{{ fallbackLabel() }}</ng-content></span>`,
  styles: `
    :host {
      display: inline-flex;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      border-radius: var(--r-pill);
      padding: 2px 9px;
      font-size: var(--fs-xs);
      font-weight: 600;
      white-space: nowrap;
      border: 1px solid transparent;
      line-height: 1.5;
    }

    .neutral {
      background: var(--line-soft);
      color: var(--ink-2);
    }

    .info {
      background: var(--info-soft);
      color: var(--info);
    }

    .ok {
      background: var(--ok-soft);
      color: var(--ok);
    }

    .warn {
      background: var(--warn-soft);
      color: var(--warn);
    }

    .bad {
      background: var(--bad-soft);
      color: var(--bad);
    }

    .violet {
      background: var(--violet-soft);
      color: var(--violet-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgeComponent {
  private readonly i18n = inject(I18nService);

  readonly tone = input<BadgeTone | null>(null);
  readonly status = input<string | null>(null);

  protected readonly effectiveTone = computed<BadgeTone>(() => this.tone() ?? toneOf(this.status()));

  protected readonly fallbackLabel = computed(() => {
    const status = this.status();
    if (status === null) {
      return '';
    }

    const key = `shared.status.${status}`;
    return this.i18n.has(key) ? this.i18n.t(key) : status;
  });
}
