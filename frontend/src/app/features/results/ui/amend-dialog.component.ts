import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { ResultRowView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import type { AmendInput } from '../data/results.api';
import { initialDraft, toValueItem } from './result-format';

/** The amendment the dialog produces (T6: new value and the reason). */
export interface AmendRequest {
  readonly row: ResultRowView;
  readonly value: AmendInput;
  readonly reason: string;
}

/**
 * T6 Amend (modify a verified result, special right): the new value of a typed test and the mandatory reason. The
 * result returns to Complete and needs a new verification; the old value and signature stay in the result log.
 */
@Component({
  selector: 'app-amend-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('results.amend.title')" size="md" [danger]="true">
      @if (row(); as current) {
        <form [formGroup]="form" appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
          <p class="warn-strip">{{ i18n.t('results.amend.warning') }}</p>
          <dl class="kv">
            <dt>{{ i18n.t('results.grid.test') }}</dt>
            <dd>{{ current.reportName ?? current.testNameEn ?? current.testCode }}</dd>
            <dt>{{ i18n.t('results.amend.current') }}</dt>
            <dd>{{ current.textValue ?? '—' }} {{ current.unit ?? '' }}</dd>
          </dl>
          <app-form-field [label]="i18n.t('results.amend.value')">
            @if (current.testType === 'RestrictedList') {
              <select formControlName="value">
                <option value="">—</option>
                @for (option of options(); track option.value) {
                  <option [value]="option.value">{{ option.value }}</option>
                }
              </select>
            } @else {
              <input type="text" formControlName="value" maxlength="500" autocomplete="off" />
            }
          </app-form-field>
          <app-form-field [label]="i18n.t('results.amend.reason')">
            <textarea rows="3" formControlName="reason" maxlength="1000"></textarea>
          </app-form-field>
        </form>
      }
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('results.cancel') }}</button>
        <button type="button" class="btn danger" [disabled]="busy()" (click)="submit()">{{ i18n.t('results.amend.submit') }}</button>
      </ng-container>
    </app-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AmendDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly row = input<ResultRowView | null>(null);
  readonly busy = input(false);

  readonly submitted = output<AmendRequest>();

  protected readonly options = computed(() => (this.row()?.listOptions ?? []).filter((option) => !option.isPlaceholder));

  protected readonly form = new FormGroup({
    value: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(500)] }),
    reason: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(1000)] }),
  });

  constructor() {
    effect(() => {
      const row = this.row();
      if (this.open() && row) {
        untracked(() => this.form.reset({ value: initialDraft(row), reason: '' }));
      }
    });
  }

  protected submit(): void {
    const row = this.row();
    if (!row || !markAllTouched(this.form)) {
      return;
    }

    const item = toValueItem(row, this.form.controls.value.value);
    this.submitted.emit({ row, value: { numericValue: item.numericValue, textValue: item.textValue, paragraphs: null, culture: null }, reason: this.form.controls.reason.value.trim() });
  }
}
