import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { PRICE_LIMITS, type TierOption } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';

export interface CopyTierRequest {
  readonly sourceTierId: string;
  readonly factor: number;
}

/**
 * "Copy from another tier" of CP-34 (LDM "Client Copy"): the source tier's formula, prices, exclusions and alerts,
 * optionally scaled by a factor; replaces this tier's pricing. Dumb: emits the request.
 */
@Component({
  selector: 'app-copy-tier-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('pricing.copyTier.title')" size="sm">
      <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
        <p class="muted small">{{ i18n.t('pricing.copyTier.help') }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        @if (sources().length === 0) {
          <p class="warn-strip">{{ i18n.t('pricing.copyTier.none') }}</p>
        }
        <app-form-field [label]="i18n.t('pricing.copyTier.source')">
          <select formControlName="sourceTierId">
            <option value="">{{ i18n.t('pricing.choose') }}</option>
            @for (option of sources(); track option.id) {
              <option [value]="option.id">{{ optionLabel(option) }}</option>
            }
          </select>
        </app-form-field>
        <app-form-field [label]="i18n.t('pricing.copyTier.factor')" [hint]="i18n.t('pricing.copyTier.factorHint')">
          <input type="number" formControlName="factor" min="0.0001" max="10" step="0.01" />
        </app-form-field>
      </form>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('pricing.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="pending() || sources().length === 0" (click)="submit()">{{ i18n.t(pending() ? 'pricing.copy.copying' : 'pricing.copyTier.submit') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }
    .muted,
    .warn-strip {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CopyTierDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  /** Tiers that have a contract pricing, other than the current one. */
  readonly sources = input<readonly TierOption[]>([]);
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly confirmCopy = output<CopyTierRequest>();

  protected readonly form = new FormGroup({
    sourceTierId: new FormControl('', { nonNullable: true, validators: [requiredText] }),
    factor: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(0.0001), Validators.max(PRICE_LIMITS.maxFactor)] }),
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.form.reset({ sourceTierId: '', factor: 1 }));
      }
    });
  }

  protected optionLabel(option: TierOption): string {
    const lang = this.i18n.lang();
    return `${localizedPick({ en: option.payerNameEn, ar: option.payerNameAr }, lang)} / ${localizedPick({ en: option.nameEn, ar: option.nameAr }, lang)}`;
  }

  protected submit(): void {
    if (this.pending() || !markAllTouched(this.form)) {
      return;
    }

    const value = this.form.getRawValue();
    this.confirmCopy.emit({ sourceTierId: value.sourceTierId, factor: value.factor });
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'pricing', problem);
  }
}
