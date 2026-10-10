import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { PASSWORD_MAX_LENGTH } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';

export interface ResetPasswordInput {
  readonly temporaryPassword: string;
  readonly mustChangePassword: boolean;
}

/**
 * The administrator's password reset (SRS CP-38): a temporary password typed twice and "must change at next sign-in"
 * (on by default). The password policy is the server's (minimum from the tenant parameter, at most 128, not containing
 * the username); the minimum given here only spares a round trip. The password never leaves this dialog except in
 * the request, which the credential envelope seals (D-03).
 */
@Component({
  selector: 'app-reset-password-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.tf('organization.reset.title', { username: username() })" size="sm" (closed)="form.reset({ password: '', confirm: '', mustChange: true })">
      <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
        <p class="muted small">{{ i18n.tf('organization.reset.hint', { min: minLength() }) }}</p>
        <app-form-field [label]="i18n.t('organization.reset.password')">
          <input type="password" formControlName="password" autocomplete="new-password" [attr.maxlength]="maxLength" />
        </app-form-field>
        <app-form-field [label]="i18n.t('organization.reset.confirm')">
          <input type="password" formControlName="confirm" autocomplete="new-password" [attr.maxlength]="maxLength" />
        </app-form-field>
        @if (mismatch()) {
          <p class="problem" role="alert">{{ i18n.t('organization.reset.mismatch') }}</p>
        }
        <label class="check">
          <input type="checkbox" formControlName="mustChange" />
          <span>{{ i18n.t('organization.reset.mustChange') }}</span>
        </label>
        @if (problem()) {
          <p class="problem" role="alert">{{ problem() }}</p>
        }
      </form>
      <ng-container dialogFooter>
        <button type="button" class="btn ghost" (click)="open.set(false)">{{ i18n.t('organization.cancel') }}</button>
        <button type="button" class="btn danger" [disabled]="pending()" (click)="submit()">{{ i18n.t('organization.reset.submit') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form { display: grid; gap: var(--sp-3); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly username = input('');
  readonly minLength = input(8);
  readonly pending = input(false);
  /** A translated server refusal (ORG-020, ORG-021 …), or null. */
  readonly problem = input<string | null>(null);
  readonly submitted = output<ResetPasswordInput>();

  protected readonly maxLength = PASSWORD_MAX_LENGTH;
  protected readonly mismatch = signal(false);

  protected readonly form = new FormGroup({
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    mustChange: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      this.form.controls.password.setValidators([Validators.required, Validators.minLength(this.minLength()), Validators.maxLength(PASSWORD_MAX_LENGTH)]);
      this.form.controls.password.updateValueAndValidity({ emitEvent: false });
    });
  }

  protected submit(): void {
    if (this.pending() || !markAllTouched(this.form)) {
      return;
    }

    const { password, confirm, mustChange } = this.form.getRawValue();
    this.mismatch.set(password !== confirm);
    if (password !== confirm) {
      return;
    }

    this.submitted.emit({ temporaryPassword: password, mustChangePassword: mustChange });
  }
}
