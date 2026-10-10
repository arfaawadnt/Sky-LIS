import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

import { I18nService } from '../../../core/i18n/i18n.service';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';

/**
 * A one-field text dialog (internal note, delta acknowledgement comment): prefilled with the current text, Ctrl+S or
 * the button submits the trimmed text (empty → null when optional), Esc closes.
 */
@Component({
  selector: 'app-result-text-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="title()" size="md" (closed)="closed.emit()">
      <form appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
        @if (hint()) {
          <p class="info-strip">{{ hint() }}</p>
        }
        <app-form-field [label]="label()">
          <textarea class="inp" rows="4" [formControl]="text" [attr.maxlength]="maxLength()"></textarea>
        </app-form-field>
      </form>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('results.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="busy() || (required() && text.value.trim().length === 0)" (click)="submit()">{{ submitLabel() }}</button>
      </ng-container>
    </app-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResultTextDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly title = input.required<string>();
  readonly label = input.required<string>();
  readonly hint = input<string | null>(null);
  readonly initial = input<string | null>(null);
  readonly maxLength = input(2000);
  readonly required = input(false);
  readonly busy = input(false);
  readonly submitLabel = input.required<string>();

  readonly submitted = output<string | null>();
  readonly closed = output<void>();

  protected readonly text = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] });

  constructor() {
    effect(() => {
      if (this.open()) {
        const initial = this.initial();
        untracked(() => this.text.setValue(initial ?? ''));
      }
    });
  }

  protected submit(): void {
    const value = this.text.value.trim();
    if (this.required() && value.length === 0) {
      return;
    }

    this.submitted.emit(value.length === 0 ? null : value);
  }
}
