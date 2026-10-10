import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import type { AuthResponse, TenantChoice } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PortalService } from '../../../core/portal/portal.service';
import { mutation } from '../../../core/state/mutation';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

/** Where a client-portal session without a branch goes first (CP-04); the account module owns the page. */
export const SELECT_BRANCH_PATH = '/account/select-branch';

/**
 * Sign-in (02 §4.1, SRS CP-03). The form posts username and password; the envelope interceptor seals the password
 * before the request leaves the browser, so this page never touches cryptography. A user with several memberships
 * gets the tenant choice and re-posts with it. After sign-in the interface switches to the user's language and a
 * laboratory session without a branch goes to branch selection (CP-04). Links lead to password recovery, the
 * subscription wizard and the public site.
 */
@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './login.page.html',
  styleUrl: './login.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly portal = inject(PortalService);
  private readonly router = inject(Router);

  /** Bound from the query string by `withComponentInputBinding()`. */
  readonly returnUrl = input<string | undefined>();

  /** Prefilled after a subscription or a recovery (`/login?username=…`). */
  readonly username = input<string | undefined>();

  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1024)] }),
    tenant: new FormControl<string | null>(null),
  });

  protected readonly tenants = signal<readonly TenantChoice[]>([]);

  protected readonly showPassword = signal(false);

  protected readonly otherLanguage = computed(() => (this.i18n.lang() === 'en' ? 'العربية' : 'English'));

  protected readonly signIn = mutation(
    (input: { username: string; password: string; tenant: string | null }) => this.auth.signIn(input.username, input.password, input.tenant),
    { then: (response) => void this.afterSignIn(response) },
  );

  constructor() {
    void this.i18n.load('auth');
    effect(() => {
      const prefill = this.username();
      if (prefill && this.form.controls.username.value === '') {
        this.form.controls.username.setValue(prefill);
      }
    });
  }

  protected toggleLanguage(): void {
    this.i18n.setLanguage(this.i18n.lang() === 'en' ? 'ar' : 'en');
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.signIn.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    const { username, password, tenant } = this.form.getRawValue();
    await this.signIn.execute({ username, password, tenant });
    this.form.controls.password.reset();
  }

  protected problemText(code: string): string {
    const translated = this.i18n.t(`auth.error.${code}`);
    return translated === `auth.error.${code}` ? this.i18n.t('auth.error.generic') : translated;
  }

  private async afterSignIn(response: AuthResponse): Promise<void> {
    if (response.step === 'select-tenant') {
      this.tenants.set(response.tenants ?? []);
      const first = response.tenants?.[0];
      this.form.controls.tenant.setValue(first ? first.tenantId : null);
      return;
    }

    if (response.step !== 'signed-in') {
      return;
    }

    const language = response.profile?.language;
    if (language && language !== this.i18n.lang()) {
      this.i18n.setLanguage(language);
    }

    const requested = this.returnUrl();
    const target = requested && requested.startsWith('/') && !requested.startsWith('//') ? requested : this.portal.homePath();
    if (this.portal.portal() === 'client' && this.auth.currentBranch() === null) {
      await this.router.navigate([SELECT_BRANCH_PATH], { queryParams: { returnUrl: target } });
      return;
    }

    await this.router.navigateByUrl(target);
  }
}
