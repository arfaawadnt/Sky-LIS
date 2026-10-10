import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { PRICE_LIMITS } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';

/**
 * The alert condition of one contract price (CP-34, LDM "Alert"/"Conditions"): text shown to the reception clerk when the
 * service is ordered under this tier. Saving an empty text clears it. Dumb: emits the text (or null).
 */
@Component({
  selector: 'app-price-alert-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('pricing.alert.title')" size="sm">
      <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
        <p class="target">{{ label() }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        <app-form-field [label]="i18n.t('pricing.alert.text')" [hint]="i18n.t('pricing.alert.hint')">
          <textarea formControlName="text" rows="4" [attr.maxlength]="maxLength" [placeholder]="i18n.t('pricing.alert.placeholder')"></textarea>
        </app-form-field>
      </form>
      <ng-container dialogFooter>
        @if (current()) {
          <button type="button" class="btn danger" [disabled]="pending()" (click)="clear()">{{ i18n.t('pricing.alert.clear') }}</button>
        }
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('pricing.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="pending()" (click)="submit()">{{ i18n.t(pending() ? 'pricing.saving' : 'pricing.save') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }
    .target {
      margin: 0;
      font-weight: 600;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceAlertDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly label = input('');
  readonly current = input<string | null>(null);
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly save = output<string | null>();

  protected readonly maxLength = PRICE_LIMITS.maxAlertLength;
  protected readonly form = new FormGroup({
    text: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(PRICE_LIMITS.maxAlertLength)] }),
  });

  private readonly text = this.form.controls.text;

  constructor() {
    effect(() => {
      const current = this.current();
      if (this.open()) {
        untracked(() => this.text.reset(current ?? ''));
      }
    });
  }

  protected submit(): void {
    if (this.pending() || this.text.invalid) {
      this.text.markAsTouched();
      return;
    }

    const value = this.text.value.trim();
    this.save.emit(value === '' ? null : value);
  }

  protected clear(): void {
    if (!this.pending()) {
      this.save.emit(null);
    }
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'pricing', problem);
  }
}
