import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators, type AbstractControl, type ValidationErrors } from '@angular/forms';

import type { RangeOverrideDto } from '../../../core/api/integration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { parseNumber } from './integration-format';

/** Low ≤ high when both are numbers (INT-013 on the server). */
function orderedBounds(group: AbstractControl): ValidationErrors | null {
  const low = parseNumber(group.get('low')?.value as string | null);
  const high = parseNumber(group.get('high')?.value as string | null);
  return low !== null && high !== null && low > high ? { range: true } : null;
}

/** The analyser-specific reference range (CP-59; LDM manual §13.5): numeric bounds and/or a text; an empty form clears it. */
@Component({
  selector: 'app-integration-range-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('integration.range.title')" size="sm">
      <form [formGroup]="form" appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate class="form-grid">
        <app-form-field [label]="i18n.t('integration.range.low')"><input type="text" inputmode="decimal" formControlName="low" /></app-form-field>
        <app-form-field [label]="i18n.t('integration.range.high')"><input type="text" inputmode="decimal" formControlName="high" /></app-form-field>
        <app-form-field class="full" [label]="i18n.t('integration.range.text')" [hint]="i18n.t('integration.range.textHint')"><input type="text" formControlName="text" maxlength="200" /></app-form-field>
        @if (form.errors?.['range'] || invalidNumber()) {
          <p class="problem full" role="alert">{{ i18n.t(invalidNumber() ? 'integration.range.notNumber' : 'integration.range.order') }}</p>
        }
      </form>
      <button dialogFooter type="button" class="btn ghost" (click)="clear()">{{ i18n.t('integration.range.clear') }}</button>
      <button dialogFooter type="button" class="btn primary" [disabled]="pending()" (click)="submit()">{{ i18n.t('integration.save') }}</button>
    </app-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RangeOverrideDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly range = input<RangeOverrideDto | null>(null);
  readonly pending = input(false);
  readonly saved = output<RangeOverrideDto | null>();

  protected readonly form = new FormGroup(
    {
      low: new FormControl('', { nonNullable: true }),
      high: new FormControl('', { nonNullable: true }),
      text: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    },
    { validators: [orderedBounds] },
  );

  constructor() {
    effect(() => {
      if (this.open()) {
        const range = this.range();
        untracked(() => this.form.reset({ low: range?.low?.toString() ?? '', high: range?.high?.toString() ?? '', text: range?.text ?? '' }));
      }
    });
  }

  protected invalidNumber(): boolean {
    const { low, high } = this.form.getRawValue();
    return (low.trim() !== '' && parseNumber(low) === null) || (high.trim() !== '' && parseNumber(high) === null);
  }

  protected clear(): void {
    this.saved.emit(null);
  }

  protected submit(): void {
    if (!markAllTouched(this.form) || this.invalidNumber()) {
      return;
    }

    const { low, high, text } = this.form.getRawValue();
    const value: RangeOverrideDto = { low: parseNumber(low), high: parseNumber(high), text: text.trim() === '' ? null : text.trim() };
    this.saved.emit(value.low === null && value.high === null && value.text === null ? null : value);
  }
}
