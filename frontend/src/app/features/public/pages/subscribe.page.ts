import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { SUBSCRIPTION_PASSWORD_RULE, type PublicCountry } from '../../../core/api/public-subscription.contracts';
import { LABORATORY_PATH_PREFIX, SIGN_IN_PATH, laboratorySlugOf } from '../../../core/auth/session-navigation';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { MobileInputComponent } from '../../../shared/forms/mobile-input/mobile-input.component';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { requiredText } from '../../../shared/forms/validators';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { allPassed, countryMobileValidator, isUsernameShapeValid, normaliseUsername, passwordChecks } from '../data/subscription-rules';
import { SubscriptionFacade, type WizardStep } from '../state/subscription.facade';
import { PublicHeaderComponent } from '../ui/public-header.component';

const CODE_PATTERN = /^\d{4,8}$/;

/**
 * The subscription wizard (SRS CP-02, WF-01 step 1): laboratory details with country and governorate pickers and
 * mobiles checked by the country's length; a one-time code to the manager's mobile (resend after 60 s, valid for
 * five minutes); the administrator's credentials (availability while typing, the length-only password rule of
 * 2026-10-09 shown live); and the portal address with a sign-in button. Problem codes become messages.
 */
@Component({
  selector: 'app-subscribe-page',
  imports: [ReactiveFormsModule, RouterLink, FormFieldComponent, IconComponent, MobileInputComponent, PublicHeaderComponent],
  templateUrl: './subscribe.page.html',
  styleUrl: './subscribe.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscribePage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SubscriptionFacade);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly rule = SUBSCRIPTION_PASSWORD_RULE;
  protected readonly stepKeys: readonly { readonly step: WizardStep; readonly key: string }[] = [
    { step: 1, key: 'details' },
    { step: 2, key: 'verify' },
    { step: 3, key: 'account' },
    { step: 4, key: 'ready' },
  ];

  protected readonly details = new FormGroup({
    labNameEn: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
    labNameAr: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
    countryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    governorateId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    address: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(300)] }),
    mobile: new FormControl('', { nonNullable: true, validators: [requiredText, countryMobileValidator(() => this.facade.country(), (country) => this.mobileMessage('public.wizard.validation.mobileLength', country))] }),
    licenceNo: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    managerName: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
    managerMobile: new FormControl('', { nonNullable: true, validators: [requiredText, countryMobileValidator(() => this.facade.country(), (country) => this.mobileMessage('public.wizard.validation.managerMobileLength', country))] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(254)] }),
  });

  protected readonly code = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(CODE_PATTERN)] });

  protected readonly codeForm = new FormGroup({ code: this.code });

  protected readonly account = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(64)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(SUBSCRIPTION_PASSWORD_RULE.maxLength)] }),
    confirmation: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly showPassword = signal(false);

  private readonly accountValue = toSignal(this.account.valueChanges, { initialValue: this.account.getRawValue() });

  protected readonly checks = computed(() => {
    const value = this.accountValue();
    return passwordChecks(value.password ?? '', value.confirmation ?? '', value.username ?? '', this.rule.minLength, this.rule.maxLength);
  });

  protected readonly mobileHint = computed(() => {
    const country = this.facade.country();
    return country ? this.i18n.tf('public.wizard.mobileHint', { length: country.mobileLength, dial: country.dialCode }) : this.i18n.t('public.wizard.chooseCountryFirst');
  });

  protected readonly governorates = computed(() => (this.facade.governorates.hasValue() ? this.facade.governorates.value() : []));

  protected readonly countries = computed<readonly PublicCountry[]>(() => (this.facade.countries.hasValue() ? this.facade.countries.value() : []));

  constructor() {
    void this.i18n.load('public');
    // The digit-count messages are rendered text: re-validate the two mobiles when the language changes, so a shown
    // message follows the switch like every other message on the page (re-test R-02, 2026-10-12). Reading the two keys
    // makes the effect run again once the new language's dictionary has arrived; the status change is emitted (round 2
    // RA-02), because the form field redraws its message from the control's events and `emitEvent: false` left it stale.
    effect(() => {
      this.i18n.lang();
      this.i18n.t('public.wizard.validation.mobileLength');
      this.i18n.t('public.wizard.validation.managerMobileLength');
      untracked(() => {
        for (const control of [this.details.controls.mobile, this.details.controls.managerMobile]) {
          if (control.errors !== null) {
            control.updateValueAndValidity();
          }
        }
      });
    });
  }

  /** C-08: the same digit-count message as the Add client form of the Super Admin portal. */
  private mobileMessage(key: string, country: { readonly mobileLength: number; readonly dialCode: string }): string {
    return this.i18n.tf(key, { length: country.mobileLength, dial: country.dialCode });
  }

  protected name(item: { readonly nameEn: string; readonly nameAr: string }): string {
    return this.i18n.lang() === 'ar' ? item.nameAr : item.nameEn;
  }

  protected onCountryChange(): void {
    const countryId = this.details.controls.countryId.value;
    this.facade.selectCountry(countryId);
    this.details.controls.governorateId.setValue('');
    this.details.controls.mobile.updateValueAndValidity();
    this.details.controls.managerMobile.updateValueAndValidity();
  }

  protected async sendCode(): Promise<void> {
    if (!markAllTouched(this.details) || this.facade.start.pending()) {
      return;
    }

    const value = this.details.getRawValue();
    const started = await this.facade.start.execute({
      labNameEn: value.labNameEn.trim(),
      labNameAr: value.labNameAr.trim(),
      countryId: value.countryId,
      governorateId: value.governorateId,
      address: value.address.trim(),
      mobile: value.mobile.trim(),
      licenceNo: value.licenceNo.trim() === '' ? null : value.licenceNo.trim(),
      managerName: value.managerName.trim(),
      managerMobile: value.managerMobile.trim(),
      email: value.email.trim() === '' ? null : value.email.trim(),
      language: this.i18n.lang(),
    });
    if (started === null) {
      problemToFormErrors(this.facade.start.problem(), this.details);
    } else {
      this.code.reset();
    }
  }

  protected async verifyCode(): Promise<void> {
    const started = this.facade.started();
    this.code.setValue(this.code.value.trim());
    if (started === null || !markAllTouched(this.code) || this.facade.verify.pending()) {
      return;
    }

    await this.facade.verify.execute({ subscriptionId: started.subscriptionId, code: this.code.value });
  }

  protected async resendCode(): Promise<void> {
    const started = this.facade.started();
    if (started === null || this.facade.resendIn() > 0 || this.facade.resend.pending()) {
      return;
    }

    if ((await this.facade.resend.execute(started.subscriptionId)) !== null) {
      this.code.reset();
      this.toast.success(this.i18n.t('public.wizard.codeResent'));
    }
  }

  protected onUsernameInput(): void {
    this.facade.checkUsername(this.account.controls.username.value);
  }

  protected async createAccount(): Promise<void> {
    const started = this.facade.started();
    const username = normaliseUsername(this.account.controls.username.value);
    if (started === null || !markAllTouched(this.account) || !allPassed(this.checks()) || !isUsernameShapeValid(username) || this.facade.complete.pending()) {
      return;
    }

    if (this.facade.usernameState() === 'taken') {
      return;
    }

    const completed = await this.facade.complete.execute({ subscriptionId: started.subscriptionId, username, password: this.account.controls.password.value });
    this.account.controls.password.reset();
    this.account.controls.confirmation.reset();
    if (completed === null) {
      problemToFormErrors(this.facade.complete.problem(), this.account);
    }
  }

  /** LIS-CR-005: the new laboratory's own sign-in address (`/lab/<slug>`, the path of `portalUrl`), the username prefilled. */
  protected async signIn(): Promise<void> {
    const completed = this.facade.completed();
    const slug = laboratorySlugOf(completed?.portalUrl);
    await this.router.navigate(slug === null ? [SIGN_IN_PATH] : [LABORATORY_PATH_PREFIX, slug], { queryParams: completed ? { username: completed.username } : {} });
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'public', problem);
  }

  /** SUB-001 (the request expired) and SUB-404 can only be cured by starting again. */
  protected mustRestart(problem: ProblemDetails | null): boolean {
    return problem !== null && (problem.code === 'SUB-001' || problem.code === 'SUB-404' || problem.code === 'SUB-002');
  }

  protected restart(): void {
    this.facade.start.reset();
    this.facade.verify.reset();
    this.facade.complete.reset();
    this.facade.backToDetails();
  }
}
