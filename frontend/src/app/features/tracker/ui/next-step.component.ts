import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { LabTrackerItem } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { RETURN_TO_PARAM } from '../../../shared/ui/return-link/return-link.component';
import { NEXT_STEP_ICONS, nextStepLink } from './tracker-format';

/**
 * The next step of a visit as a link to the page that does it (collect → accession details, enter / review / verify /
 * print → results by accession, take payment → payments); plain text when the user lacks that page's permission
 * (affordance only: the target page and the server decide). The link carries `returnTo` when given (LIS-CR-014 T5).
 */
@Component({
  selector: 'app-tracker-next-step',
  imports: [RouterLink, IconComponent],
  template: `
    @if (item().nextStep === 'None') {
      <span class="none"><app-icon name="check" [size]="14" />{{ label() }}</span>
    } @else if (target(); as target) {
      <a class="btn sm soft step" [routerLink]="target.path" [queryParams]="target.queryParams" [attr.aria-label]="ariaLabel()">
        <app-icon [name]="icon()" [size]="14" />{{ label() }}
      </a>
    } @else {
      <span class="text"><app-icon [name]="icon()" [size]="14" />{{ label() }}</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .step {
      white-space: nowrap;
      text-decoration: none;
    }

    .none,
    .text {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: var(--fs-sm);
      color: var(--muted);
    }

    .text {
      color: var(--ink-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NextStepComponent {
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionsService);

  readonly item = input.required<LabTrackerItem>();

  /** The tracker's URL with its filter, added to the link as `returnTo` (null = none). */
  readonly returnTo = input<string | null>(null);

  protected readonly label = computed(() => this.i18n.t(`tracker.next.${this.item().nextStep}`));
  protected readonly icon = computed(() => NEXT_STEP_ICONS[this.item().nextStep]);
  protected readonly ariaLabel = computed(() => this.i18n.tf('tracker.next.aria', { step: this.label(), accession: this.item().accession }));

  /** The link, or null when the step has no page or the user may not open it. */
  protected readonly target = computed(() => {
    const link = nextStepLink(this.item());
    if (link === null || !this.permissions.can(link.permission)) {
      return null;
    }

    const returnTo = this.returnTo();
    return returnTo === null ? link : { ...link, queryParams: { ...link.queryParams, [RETURN_TO_PARAM]: returnTo } };
  });
}
