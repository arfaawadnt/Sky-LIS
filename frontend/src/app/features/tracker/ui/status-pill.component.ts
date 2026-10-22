import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import type { StatusLook } from './tracker-format';

/**
 * `<app-tracker-pill [look]="…" />`: a status as icon + text + colour on the kit's badge. Without a look it shows "—":
 * `absent="rights"` (a section the API left out for lack of the module permission) adds the "Not in your rights" tooltip.
 */
@Component({
  selector: 'app-tracker-pill',
  imports: [BadgeComponent, IconComponent],
  template: `
    @if (look(); as look) {
      <app-badge [tone]="look.tone" [attr.title]="look.title ?? null"><app-icon [name]="look.icon" [size]="12" />{{ look.text }}</app-badge>
    } @else if (absent() === 'rights') {
      <span class="na" [title]="i18n.t('tracker.notInRights')"><span aria-hidden="true">—</span><span class="visually-hidden">{{ i18n.t('tracker.notInRights') }}</span></span>
    } @else {
      <span class="na">—</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .na {
      color: var(--muted);
      cursor: help;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPillComponent {
  protected readonly i18n = inject(I18nService);

  readonly look = input.required<StatusLook | null>();
  readonly absent = input<'rights' | 'none'>('rights');
}
