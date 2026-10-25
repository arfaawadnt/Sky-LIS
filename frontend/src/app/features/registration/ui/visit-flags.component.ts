import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';

/** A visit flag of CP-06 ("collected outside" moved to each sample, LIS-CR-008 C-02: `ui/sample-flags`). */
export type VisitFlag = 'urgent' | 'biohazard';

/**
 * The visit flags as toggle buttons (UX option B): one press turns a flag on or off, `aria-pressed` carries the state and
 * an urgent visit stands out in the alert colour. A disabled ancestor fieldset (read-only view) disables them.
 */
@Component({
  selector: 'app-reg-visit-flags',
  host: { role: 'group', '[attr.aria-label]': "i18n.t('registration.flags')" },
  template: `
    <button type="button" class="flag urgent" [attr.aria-pressed]="urgent()" (click)="toggled.emit('urgent')">{{ i18n.t('registration.flag.urgent') }}</button>
    <button type="button" class="flag" [attr.aria-pressed]="biohazard()" (click)="toggled.emit('biohazard')">{{ i18n.t('registration.flag.biohazard') }}</button>
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
    }

    .flag {
      min-height: 32px;
      padding: 0 var(--sp-3);
      border: 1px solid var(--line);
      border-radius: var(--r-pill);
      background: var(--surface);
      color: var(--ink-2);
      font: inherit;
      font-size: var(--fs-sm);
      cursor: pointer;
    }

    .flag:hover {
      border-color: var(--blue);
    }

    .flag[aria-pressed='true'] {
      border-color: var(--blue);
      background: var(--blue-soft);
      color: var(--blue-3);
      font-weight: 600;
    }

    .flag[aria-pressed='true']::before {
      content: '● ';
    }

    .urgent[aria-pressed='true'] {
      border-color: var(--bad);
      background: var(--bad-soft);
      color: var(--bad);
    }

    .flag:disabled {
      cursor: default;
      opacity: 0.7;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisitFlagsComponent {
  protected readonly i18n = inject(I18nService);

  readonly urgent = input(false);
  readonly biohazard = input(false);
  readonly toggled = output<VisitFlag>();
}
