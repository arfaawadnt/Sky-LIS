import { ChangeDetectionStrategy, Component, effect, inject, input, model, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { RejectionReasonOption } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { pickName } from './sample-format';

/** What the technician chose: a catalogue reason of kind Rejection and an optional note. */
export interface RejectChoice {
  readonly reasonId: string;
  readonly note: string | null;
}

/**
 * The reason dialog of CP-16 ("reject with reason"): a required catalogue reason (kind Rejection, active only) and an
 * optional note of at most 500 characters. Ctrl+S or the button confirms; Esc closes. The server still decides.
 */
@Component({
  selector: 'app-reject-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('samples.reject.title')" [danger]="true" size="md">
      <form [formGroup]="form" (ngSubmit)="confirm()" appSaveShortcut (appSaveShortcut)="confirm()" novalidate>
        <p class="lead">{{ i18n.tf('samples.reject.message', { number: sampleNumber() }) }}</p>
        <app-form-field [label]="i18n.t('samples.reject.reason')">
          <select formControlName="reasonId">
            <option value="">{{ i18n.t('samples.reject.chooseReason') }}</option>
            @for (reason of reasons(); track reason.id) {
              <option [value]="reason.id">{{ name(reason) }}</option>
            }
          </select>
        </app-form-field>
        @if (reasonsUnavailable()) {
          <p class="warn-strip" role="status">{{ i18n.t('samples.reject.reasonsUnavailable') }}</p>
        }
        <app-form-field [label]="i18n.t('samples.reject.note')" [hint]="i18n.t('samples.reject.noteHint')">
          <textarea formControlName="note" rows="3" maxlength="500"></textarea>
        </app-form-field>
        @if (problemText()) {
          <p class="problem" role="alert">{{ problemText() }}</p>
        }
      </form>
      <ng-container dialogFooter>
        <button type="button" class="btn ghost" (click)="open.set(false)">{{ i18n.t('samples.cancel') }}</button>
        <button type="button" class="btn danger" [disabled]="busy()" (click)="confirm()">{{ busy() ? i18n.t('samples.reject.rejecting') : i18n.t('samples.reject.confirm') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }

    .lead {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RejectDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly sampleNumber = input('');
  readonly reasons = input<readonly RejectionReasonOption[]>([]);
  readonly reasonsUnavailable = input(false);
  readonly busy = input(false);
  /** The translated refusal of the last attempt, or empty. */
  readonly problemText = input('');

  readonly confirmed = output<RejectChoice>();

  protected readonly form = new FormGroup({
    reasonId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  constructor() {
    // a fresh form every time the dialog opens
    effect(() => {
      if (this.open()) {
        this.form.reset({ reasonId: '', note: '' });
      }
    });
  }

  protected name(reason: RejectionReasonOption): string {
    return pickName(reason.nameEn, reason.nameAr, this.i18n.lang());
  }

  protected confirm(): void {
    if (!this.open() || this.busy() || !markAllTouched(this.form)) {
      return;
    }

    const { reasonId, note } = this.form.getRawValue();
    const trimmed = note.trim();
    this.confirmed.emit({ reasonId, note: trimmed.length === 0 ? null : trimmed });
  }
}
