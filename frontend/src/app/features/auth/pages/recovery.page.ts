import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { requiredText } from '../../../shared/forms/validators';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

type RecoveryStep = 1 | 2 | 3;

/** The policy of 2026-10-09 (02 §6.1): length only, never the username. The server's floor is the platform's minimum (default 8). */
export const RECOVERY_PASSWORD_RULE = { minLength: 8, maxLength: 128 } as const;

export const RESEND_AFTER_SECONDS = 60;

const MOBILE_LIKE = /^\+?[\d\s\-()]{8,}$/;

/** The foundation's single refusal of the last step (wrong, expired or consumed code, or an unknown account). */
export const OTP_REJECTED = 'AUTH-013';

/**
 * Password recovery (SRS CP-03): username or mobile → one-time code (six digits, sent to the registered mobile) →
 * new password and confirmation → back to the sign-in page with a success toast. The server never says whether the
 * account exists (always 202) and refuses every failure of the last step as `AUTH-013`, shown as "the code is invalid
 * or expired"; the new password is sealed by the credential envelope interceptor (D-03).
 */
@Component({
  selector: 'app-recovery-page',
  imports: [ReactiveFormsModule, RouterLink, FormFieldComponent, IconComponent],
  templateUrl: './recovery.page.html',
  styleUrl: './recovery.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecoveryPage {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private tick: ReturnType<typeof setInterval> | null = null;

  protected readonly rule = RECOVERY_PASSWORD_RULE;
  protected readonly step = signal<RecoveryStep>(1);
  protected readonly showPassword = signal(false);
  private readonly resendAt = signal(0);
  private readonly now = signal(Date.now());

  protected readonly identifier = new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(64)] });
  protected readonly code = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{6}$/)] });
  protected readonly identifyForm = new FormGroup({ identifier: this.identifier });
  protected readonly codeForm = new FormGroup({ code: this.code });

  protected readonly passwords = new FormGroup({
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(RECOVERY_PASSWORD_RULE.minLength), Validators.maxLength(RECOVERY_PASSWORD_RULE.maxLength)] }),
    confirmation: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  private readonly passwordValue = toSignal(this.passwords.valueChanges, { initialValue: this.passwords.getRawValue() });

  /** A username (not a mobile) is also checked locally against the "not containing the username" rule. */
  protected readonly checks = computed(() => {
    const value = this.passwordValue();
    const password = value.password ?? '';
    const confirmation = value.confirmation ?? '';
    const id = this.identifier.value.trim().toLowerCase();
    const isUsername = id.length > 0 && !MOBILE_LIKE.test(id);
    return {
      length: password.length >= this.rule.minLength && password.length <= this.rule.maxLength,
      notUsername: password.length > 0 && (!isUsername || !password.toLowerCase().includes(id)),
      matches: password.length > 0 && password === confirmation,
    };
  });

  protected readonly resendIn = computed(() => Math.max(0, Math.ceil((this.resendAt() - this.now()) / 1000)));

  protected readonly start = mutation((identifier: string) => this.auth.startPasswordRecovery(identifier, this.i18n.lang()), {
    then: () => {
      // C-06: a new request starts a clean code step; the previous attempt's refusal (AUTH-013) must not show on it
      this.complete.reset();
      this.armResend();
      this.step.set(2);
    },
  });

  protected readonly complete = mutation((input: { identifier: string; code: string; password: string }) => this.auth.completePasswordRecovery(input.identifier, input.code, input.password));

  constructor() {
    void this.i18n.load('auth');
    inject(DestroyRef).onDestroy(() => this.stopTicking());
  }

  protected async sendCode(): Promise<void> {
    if (!markAllTouched(this.identifier) || this.start.pending()) {
      return;
    }

    await this.start.execute(this.identifier.value.trim());
  }

  protected async resend(): Promise<void> {
    if (this.resendIn() > 0 || this.start.pending()) {
      return;
    }

    if ((await this.start.execute(this.identifier.value.trim())) !== null) {
      this.code.reset();
      this.toast.success(this.i18n.t('auth.recovery.codeResent'));
    }
  }

  protected toPasswords(): void {
    this.code.setValue(this.code.value.trim());
    if (!markAllTouched(this.code)) {
      return;
    }

    this.complete.reset();
    this.step.set(3);
  }

  protected async submit(): Promise<void> {
    const checks = this.checks();
    if (!markAllTouched(this.passwords) || !checks.length || !checks.notUsername || !checks.matches || this.complete.pending()) {
      return;
    }

    const identifier = this.identifier.value.trim();
    const done = await this.complete.execute({ identifier, code: this.code.value, password: this.passwords.controls.password.value });
    this.passwords.reset();
    if (done !== null) {
      this.stopTicking();
      this.toast.success(this.i18n.t('auth.recovery.done'));
      await this.router.navigate(['/login'], { queryParams: MOBILE_LIKE.test(identifier) ? {} : { username: identifier } });
      return;
    }

    if (this.complete.problem()?.code === OTP_REJECTED) {
      this.code.reset();
      this.step.set(2);
    }
  }

  protected back(): void {
    if (this.step() === 2) {
      // C-06: back to the identifier: the code step's refusal belonged to that attempt
      this.complete.reset();
      this.code.reset();
    }

    this.step.update((step) => (step === 3 ? 2 : 1));
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'auth.recovery', problem);
  }

  private armResend(): void {
    this.now.set(Date.now());
    this.resendAt.set(Date.now() + RESEND_AFTER_SECONDS * 1000);
    this.tick ??= setInterval(() => {
      this.now.set(Date.now());
      if (this.resendIn() === 0) {
        this.stopTicking();
      }
    }, 1000);
  }

  private stopTicking(): void {
    if (this.tick !== null) {
      clearInterval(this.tick);
      this.tick = null;
    }
  }
}
