import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { DEMOGRAPHICS_EDIT_LIMITS, MAX_REPORT_DAYS_LIMIT, SPECIAL_RIGHT_FLAGS, type DemographicsEditLimit, type SpecialRightFlag, type SpecialRights } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';

export type DiscountMode = 'default' | 'none' | 'custom';

/** The discount mode of a cap: null = the tenant parameter, 0 = no discount, otherwise a custom cap. */
export function discountModeOf(cap: number | null): DiscountMode {
  return cap === null ? 'default' : cap === 0 ? 'none' : 'custom';
}

export const MAX_DISCOUNT_CAP_PERCENT = 100;

/** The cap the server accepts (ORG-006): empty, or a whole percentage from 0 to 100. The page refuses anything else instead of clamping it. */
export function isDiscountCapValid(cap: number | null): boolean {
  return cap === null || (Number.isInteger(cap) && cap >= 0 && cap <= MAX_DISCOUNT_CAP_PERCENT);
}

/** The report days limit the server accepts (ORG-019): empty (no limit), or a whole number of days from 0 to 3650. */
export function isReportDaysLimitValid(days: number | null): boolean {
  return days === null || (Number.isInteger(days) && days >= 0 && days <= MAX_REPORT_DAYS_LIMIT);
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
            <input class="inp" type="number" min="1" [max]="maxCap" step="1" [value]="value().discountCapPercent" (change)="onCap($event)" [attr.aria-label]="i18n.t('organization.special.discountCap')" />
            <span>%</span>
          </label>
        }
      </div>
      @if (!capValid()) {
        <p class="problem small" role="alert">{{ i18n.t('organization.error.ORG-006') }}</p>
      }
      <p class="muted small">{{ i18n.t('organization.special.discountHint') }}</p>
    </fieldset>

    <fieldset class="limits" [disabled]="disabled()">
      <legend>{{ i18n.t('organization.special.limits') }}</legend>
      <div class="limits-grid">
        <label class="limit">
          <span>{{ i18n.t('organization.special.demographicsEditUpTo') }}</span>
          <select class="inp" [value]="value().demographicsEditUpTo" (change)="onEditLimit($event)">
            @for (limit of editLimits; track limit) {
              <option [value]="limit">{{ i18n.t('organization.special.editLimit.' + limit) }}</option>
            }
          </select>
          <span class="muted small">{{ i18n.t('organization.special.demographicsEditUpToHint') }}</span>
        </label>
        <label class="limit">
          <span>{{ i18n.t('organization.special.reportDaysLimit') }}</span>
          <input class="inp" type="number" min="0" [max]="maxDays" step="1" [value]="value().reportDaysLimit ?? ''" (change)="onDaysLimit($event)" [placeholder]="i18n.t('organization.special.reportDaysNoLimit')" />
          <span class="muted small">{{ i18n.t('organization.special.reportDaysLimitHint') }}</span>
        </label>
      </div>
      @if (!daysValid()) {
        <p class="problem small" role="alert">{{ i18n.t('organization.error.ORG-019') }}</p>
      }
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
    .problem { color: var(--danger, #b42318); margin: var(--sp-2) 0 0; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: var(--sp-3); }
    .check { align-items: flex-start; }
    .check > span { display: grid; gap: 2px; }
    .limits-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: var(--sp-3); }
    .limit { display: grid; gap: 4px; }
    .limit > span:first-child { font-weight: 600; }
    .limit input { width: 120px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SpecialRightsFormComponent {
  protected readonly i18n = inject(I18nService);

  readonly value = input.required<SpecialRights>();
  readonly disabled = input(false);
  readonly valueChange = output<SpecialRights>();

  protected readonly flags = SPECIAL_RIGHT_FLAGS;
  protected readonly maxCap = MAX_DISCOUNT_CAP_PERCENT;
  protected readonly maxDays = MAX_REPORT_DAYS_LIMIT;
  protected readonly editLimits = DEMOGRAPHICS_EDIT_LIMITS;
  protected readonly daysValid = computed(() => isReportDaysLimitValid(this.value().reportDaysLimit));
  protected readonly mode = computed(() => discountModeOf(this.value().discountCapPercent));
  protected readonly capValid = computed(() => isDiscountCapValid(this.value().discountCapPercent));

  protected onMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value as DiscountMode;
    const current = this.value().discountCapPercent;
    const cap = mode === 'default' ? null : mode === 'none' ? 0 : current !== null && current > 0 ? current : 10;
    this.valueChange.emit({ ...this.value(), discountCapPercent: cap });
  }

  protected onCap(event: Event): void {
    const raw = Number((event.target as HTMLInputElement).value);
    // The typed value travels as is (rounded to a whole percent): an out-of-range cap is shown as refused, never silently clamped (ORG-006).
    const cap = Number.isFinite(raw) ? Math.round(raw) : 1;
    this.valueChange.emit({ ...this.value(), discountCapPercent: cap });
  }

  protected onEditLimit(event: Event): void {
    const limit = (event.target as HTMLSelectElement).value as DemographicsEditLimit;
    this.valueChange.emit({ ...this.value(), demographicsEditUpTo: limit });
  }

  protected onDaysLimit(event: Event): void {
    const text = (event.target as HTMLInputElement).value.trim();
    // Empty = no limit; a typed value travels as is (rounded to whole days), refused by the page when out of range (ORG-019).
    const days = text === '' ? null : Number.isFinite(Number(text)) ? Math.round(Number(text)) : 0;
    this.valueChange.emit({ ...this.value(), reportDaysLimit: days });
  }

  protected onFlag(flag: SpecialRightFlag, event: Event): void {
    this.valueChange.emit({ ...this.value(), [flag]: (event.target as HTMLInputElement).checked });
  }
}
