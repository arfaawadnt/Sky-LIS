import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { SPECIAL_RIGHT_FLAGS, type SpecialRightFlag, type SpecialRights } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';

export type DiscountMode = 'default' | 'none' | 'custom';

/** The discount mode of a cap: null = the tenant parameter, 0 = no discount, otherwise a custom cap. */
export function discountModeOf(cap: number | null): DiscountMode {
  return cap === null ? 'default' : cap === 0 ? 'none' : 'custom';
}

/**
 * The special rights of a role, or a user's override of them (SRS CP-37; LDM manual §12.3 "Special Rights"): the
 * discount cap (tenant default, none, or a custom percentage up to 100) and the eight action flags. Emits a complete
 * value on every change.
 */
@Component({
  selector: 'app-special-rights-form',
  template: `
    <fieldset class="discount" [disabled]="disabled()">
      <legend>{{ i18n.t('organization.special.discount') }}</legend>
      <div class="discount-row">
        <select class="inp" [value]="mode()" (change)="onMode($event)" [attr.aria-label]="i18n.t('organization.special.discount')">
          <option value="default">{{ i18n.t('organization.special.discountDefault') }}</option>
          <option value="none">{{ i18n.t('organization.special.discountNone') }}</option>
          <option value="custom">{{ i18n.t('organization.special.discountCustom') }}</option>
        </select>
        @if (mode() === 'custom') {
          <label class="cap">
            <input class="inp" type="number" min="1" max="100" step="1" [value]="value().discountCapPercent" (change)="onCap($event)" [attr.aria-label]="i18n.t('organization.special.discountCap')" />
            <span>%</span>
          </label>
        }
      </div>
      <p class="muted small">{{ i18n.t('organization.special.discountHint') }}</p>
    </fieldset>

    <fieldset class="flags" [disabled]="disabled()">
      <legend>{{ i18n.t('organization.special.actions') }}</legend>
      <ul>
        @for (flag of flags; track flag) {
          <li>
            <label class="check">
              <input type="checkbox" [checked]="value()[flag]" (change)="onFlag(flag, $event)" />
              <span>
                <strong>{{ i18n.t('organization.special.' + flag) }}</strong>
                <span class="muted small">{{ i18n.t('organization.special.' + flag + 'Hint') }}</span>
              </span>
            </label>
          </li>
        }
      </ul>
    </fieldset>
  `,
  styles: `
    :host { display: grid; gap: var(--sp-4); }
    fieldset { border: 1px solid var(--line); border-radius: var(--r); padding: var(--sp-3) var(--sp-4); margin: 0; }
    legend { font-weight: 600; padding-inline: var(--sp-1); }
    .discount-row { display: flex; flex-wrap: wrap; gap: var(--sp-3); align-items: center; }
    .discount-row select { width: auto; min-width: 220px; }
    .cap { display: inline-flex; align-items: center; gap: var(--sp-1); }
    .cap input { width: 90px; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: var(--sp-3); }
    .check { align-items: flex-start; }
    .check > span { display: grid; gap: 2px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SpecialRightsFormComponent {
  protected readonly i18n = inject(I18nService);

  readonly value = input.required<SpecialRights>();
  readonly disabled = input(false);
  readonly valueChange = output<SpecialRights>();

  protected readonly flags = SPECIAL_RIGHT_FLAGS;
  protected readonly mode = computed(() => discountModeOf(this.value().discountCapPercent));

  protected onMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value as DiscountMode;
    const current = this.value().discountCapPercent;
    const cap = mode === 'default' ? null : mode === 'none' ? 0 : current !== null && current > 0 ? current : 10;
    this.valueChange.emit({ ...this.value(), discountCapPercent: cap });
  }

  protected onCap(event: Event): void {
    const raw = Number((event.target as HTMLInputElement).value);
    const cap = Number.isFinite(raw) ? Math.min(100, Math.max(1, Math.round(raw))) : 1;
    this.valueChange.emit({ ...this.value(), discountCapPercent: cap });
  }

  protected onFlag(flag: SpecialRightFlag, event: Event): void {
    this.valueChange.emit({ ...this.value(), [flag]: (event.target as HTMLInputElement).checked });
  }
}
