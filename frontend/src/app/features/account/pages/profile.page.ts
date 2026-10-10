import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { AccountLanguage, MyAccountProfile } from '../../../core/api/account.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { formatDate, formatDateTime, pick } from '../data/account-format';
import { ProfileFacade } from '../state/profile.facade';

/**
 * The profile (SRS CP-65 for a laboratory user, SA-21 for platform staff): the laboratory's subscription record
 * (read-only, from the platform), the user's own details (read-only; the administrator edits them), the language
 * preference and the password change (current + new password, the length-only rule of 2026-10-09 shown live; the
 * other sessions end). `?mustChange=1` (the password-change guard) shows why the user landed here.
 */
@Component({
  selector: 'app-profile-page',
  imports: [ReactiveFormsModule, SaveShortcutDirective, PageHeaderComponent, SectionCardComponent, FormFieldComponent, BadgeComponent, IconComponent],
  templateUrl: './profile.page.html',
  styleUrl: './profile.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ProfileFacade);
  private readonly toast = inject(ToastService);

  readonly mustChange = input<string | undefined>();

  protected readonly language = new FormControl<AccountLanguage>('en', { nonNullable: true });

  protected readonly passwords = new FormGroup({
    current: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1024)] }),
    next: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    confirmation: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly showPasswords = signal(false);

  private readonly passwordValue = toSignal(this.passwords.valueChanges, { initialValue: this.passwords.getRawValue() });

  protected readonly profile = computed<MyAccountProfile | null>(() => (this.facade.profile.hasValue() ? this.facade.profile.value() : null));

  protected readonly policy = computed(() => this.profile()?.passwordPolicy ?? { minLength: 8, maxLength: 128 });

  protected readonly checks = computed(() => {
    const value = this.passwordValue();
    const next = value.next ?? '';
    const username = (this.profile()?.username ?? '').toLowerCase();
    const policy = this.policy();
    return {
      length: next.length >= policy.minLength && next.length <= policy.maxLength,
      notUsername: next.length > 0 && (username.length === 0 || !next.toLowerCase().includes(username)),
      differs: next.length > 0 && next !== (value.current ?? ''),
      matches: next.length > 0 && next === (value.confirmation ?? ''),
    };
  });

  protected readonly mustChangeNow = computed(() => this.profile()?.mustChangePassword === true || this.mustChange() !== undefined);

  constructor() {
    void this.i18n.load('account');
    effect(() => {
      const profile = this.profile();
      if (profile && (profile.language === 'en' || profile.language === 'ar') && !this.language.dirty) {
        this.language.setValue(profile.language);
      }
    });
  }

  protected text(en: string | null | undefined, ar: string | null | undefined): string {
    return pick(en, ar, this.i18n.lang()) || '—';
  }

  protected when(value: string | null | undefined): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected day(value: string | null | undefined): string {
    return formatDate(value, this.i18n.lang());
  }

  protected async saveLanguage(): Promise<void> {
    if (this.facade.saveLanguage.pending()) {
      return;
    }

    if ((await this.facade.saveLanguage.execute(this.language.value)) !== null) {
      this.language.markAsPristine();
      this.toast.success(this.i18n.t('account.profile.languageSaved'));
    }
  }

  protected async changePassword(): Promise<void> {
    const checks = this.checks();
    if (!markAllTouched(this.passwords) || !checks.length || !checks.notUsername || !checks.differs || !checks.matches || this.facade.changePassword.pending()) {
      return;
    }

    const { current, next } = this.passwords.getRawValue();
    const done = await this.facade.changePassword.execute({ currentPassword: current, newPassword: next });
    this.passwords.reset();
    if (done !== null) {
      this.toast.success(this.i18n.t('account.profile.passwordUpdated'));
    }
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'account', problem);
  }
}
