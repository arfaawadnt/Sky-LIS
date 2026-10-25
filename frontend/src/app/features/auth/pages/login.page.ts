import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, resource, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import type { PublicAnnouncement } from '../../../core/api/announcement.contracts';
import type { SignInTarget } from '../../../core/api/auth.contracts';
import { statusOf } from '../../../core/api/problem-details';
import { PUBLIC_LABORATORY_API } from '../../../core/api/public-laboratory.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import {
  ADMIN_SIGN_IN_PATH,
  IDLE_REASON,
  LABORATORY_PATH_PREFIX,
  SIGN_IN_PATH,
  belongsToAdminPortal,
  isLaboratorySlug,
  meaningfulReturnUrl,
  normaliseLaboratorySlug,
} from '../../../core/auth/session-navigation';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PortalService } from '../../../core/portal/portal.service';
import { mutation } from '../../../core/state/mutation';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PublicAnnouncementsApi } from '../data/public-announcements.api';
import { PublicLaboratoryApi } from '../data/public-laboratory.api';
import { AuthFrameComponent } from '../ui/auth-frame.component';
import { KeepAddressComponent } from '../ui/keep-address.component';
import { SignInAnnouncementComponent } from '../ui/sign-in-announcement.component';

/** Where a client-portal session without a branch goes first (CP-04); the account module owns the page. */
export const SELECT_BRANCH_PATH = '/account/select-branch';

/** LIS-CR-005: the two sign-in addresses, told apart by the route's data (`mode`). */
export type SignInMode = 'laboratory' | 'admin';

/** What the laboratory address answered: its card, no such laboratory, or no answer (the form stays; the server decides). */
type AddressState = 'loading' | 'found' | 'missing' | 'unavailable';

/**
 * Sign-in (02 §4.1, SRS CP-03), design 5c of 2026-10-20. LIS-CR-005: one page, two addresses —
 * `/lab/<slug>` (mode `laboratory`) reads the laboratory's public card, shows its logo and name and posts
 * `{ laboratory: slug }`; an unknown or inactive address shows "not found" with the way to the find page and no form;
 * `/admin/login` (mode `admin`) is the Super Admin sign-in and posts `{ admin: true }`. The envelope interceptor seals the
 * password before the request leaves the browser. After sign-in the interface switches to the user's language and a
 * laboratory session without a branch goes to branch selection (CP-04). `?reason=idle` (F-01) says the session ended
 * after inactivity; `?username=` prefills the form (after a subscription or a recovery). LIS-CR-007 C-01: under the card of a
 * found laboratory a strip says how to keep the address (bookmark keys, *Copy address*); *Hide* lasts for the page only.
 */
@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink, IconComponent, AuthFrameComponent, KeepAddressComponent, SignInAnnouncementComponent],
  templateUrl: './login.page.html',
  styleUrl: './login.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly portal = inject(PortalService);
  private readonly router = inject(Router);
  private readonly laboratories = inject(PublicLaboratoryApi);
  private readonly announcementsApi = inject(PublicAnnouncementsApi);

  protected readonly findPath = SIGN_IN_PATH;

  /** Route data (`withComponentInputBinding()`). */
  readonly mode = input<SignInMode>('laboratory');

  /** The `:slug` of `/lab/:slug`. */
  readonly slug = input<string | undefined>();

  /** Bound from the query string by `withComponentInputBinding()`. */
  readonly returnUrl = input<string | undefined>();

  /** `?reason=idle`: the session ended after inactivity (validation F-01); the page says so. */
  readonly reason = input<string | undefined>();

  /** Prefilled after a subscription or a recovery (`?username=…`). */
  readonly username = input<string | undefined>();

  protected readonly endedIdle = computed(() => this.reason() === IDLE_REASON);

  protected readonly isAdmin = computed(() => this.mode() === 'admin');

  /** The laboratory address as it appears in the URL, lower-cased. */
  protected readonly address = computed(() => normaliseLaboratorySlug(this.slug() ?? ''));

  protected readonly laboratory = resource({
    params: () => (this.mode() === 'laboratory' && isLaboratorySlug(this.address()) ? this.address() : undefined),
    loader: ({ params }) => this.laboratories.card(params),
  });

  protected readonly state = computed<AddressState>(() => {
    if (this.isAdmin()) {
      return 'found';
    }

    if (!isLaboratorySlug(this.address())) {
      return 'missing';
    }

    if (this.laboratory.isLoading()) {
      return 'loading';
    }

    const error = this.laboratory.error();
    if (error !== undefined) {
      return statusOf(error) === 404 ? 'missing' : 'unavailable';
    }

    return this.laboratory.hasValue() ? 'found' : 'loading';
  });

  protected readonly card = computed(() => (this.laboratory.hasValue() ? this.laboratory.value() : null));

  /**
   * LIS-CR-013 (C-17): the laboratory's live SignIn announcements once its card is found (none on the Super Admin sign-in); the
   * first replaces the hero's slogan. A failed read is no announcement.
   */
  protected readonly announcements = resource({
    params: () => (!this.isAdmin() && this.state() === 'found' ? this.card()?.slug : undefined),
    loader: ({ params }) => this.announcementsApi.forLaboratory(params),
  });

  protected readonly heroAnnouncement = computed<PublicAnnouncement | null>(() => (this.announcements.hasValue() ? (this.announcements.value()[0] ?? null) : null));

  protected readonly laboratoryName = computed(() => {
    const card = this.card();
    if (card === null) {
      return null;
    }

    return this.i18n.lang() === 'ar' && card.nameAr.trim() !== '' ? card.nameAr : card.nameEn;
  });

  /** The laboratory's address line (area, district, city, governorate) of the public card, when a city is set (LIS-CR-008 C-04). */
  protected readonly laboratoryAddress = computed(() => {
    const card = this.card();
    if (card === null) {
      return null;
    }

    const line = this.i18n.lang() === 'ar' ? (card.addressAr ?? card.addressEn) : (card.addressEn ?? card.addressAr);
    return line !== undefined && line !== null && line.trim() !== '' ? line : null;
  });

  private readonly logoFailed = signal(false);

  protected readonly logoUrl = computed(() => {
    const card = this.card();
    return card !== null && card.logoVersion !== null && !this.logoFailed() ? PUBLIC_LABORATORY_API.logo(card.slug, card.logoVersion) : null;
  });

  protected readonly cardTitle = computed(() => {
    if (this.isAdmin()) {
      return this.i18n.t('auth.admin.cardTitle');
    }

    const name = this.laboratoryName();
    return name === null ? this.i18n.t('auth.title') : this.i18n.tf('auth.lab.title', { name });
  });

  /** *Hide* on the keep-address strip: for this page only, never stored (no device storage, LIS-CR-007 C-01). */
  protected readonly keepHidden = signal(false);

  private readonly origin = inject(DOCUMENT).defaultView?.location.origin ?? '';

  /** The laboratory's sign-in address without the query string, while the strip is shown. */
  protected readonly keepAddress = computed(() =>
    !this.isAdmin() && this.state() === 'found' && !this.keepHidden() ? `${this.origin}${LABORATORY_PATH_PREFIX}/${this.card()?.slug ?? this.address()}` : null,
  );

  protected readonly recoverLink = computed<readonly string[]>(() =>
    this.isAdmin() ? [ADMIN_SIGN_IN_PATH, 'recover'] : [LABORATORY_PATH_PREFIX, this.address(), 'recover'],
  );

  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1024)] }),
  });

  protected readonly showPassword = signal(false);

  protected readonly signIn = mutation(
    (input: { username: string; password: string; target: SignInTarget }) => this.auth.signIn(input.username, input.password, input.target),
    { then: () => void this.afterSignIn() },
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

  protected onLogoError(): void {
    this.logoFailed.set(true);
  }

  protected async submit(): Promise<void> {
    const state = this.state();
    if (this.form.invalid || this.signIn.pending() || state === 'missing' || state === 'loading') {
      this.form.markAllAsTouched();
      return;
    }

    const { username, password } = this.form.getRawValue();
    const target: SignInTarget = this.isAdmin() ? { admin: true } : { laboratory: this.card()?.slug ?? this.address() };
    await this.signIn.execute({ username, password, target });
    this.form.controls.password.reset();
  }

  protected problemText(code: string): string {
    const translated = this.i18n.t(`auth.error.${code}`);
    return translated === `auth.error.${code}` ? this.i18n.t('auth.error.generic') : translated;
  }

  private async afterSignIn(): Promise<void> {
    const profile = this.auth.profile();
    if (profile === null) {
      return;   // a step other than "signed-in" (MFA, not built yet)
    }

    if (profile.language && profile.language !== this.i18n.lang()) {
      this.i18n.setLanguage(profile.language);
    }

    const home = this.portal.homePath();
    // A return URL that points back at the sign-in routes (left by a 401 raced with the sign-out) would show the form again
    // to a signed-in user; the home page is the only sensible target then.
    const local = meaningfulReturnUrl(this.returnUrl()) ?? home;
    // A return URL left by the other portal (a laboratory user signed out, a platform administrator signs in) would land the
    // session on pages it cannot use; each portal returns only to its own routes.
    const target = this.portal.isPlatform() === belongsToAdminPortal(local) ? local : home;
    if (this.portal.portal() === 'client' && this.auth.currentBranch() === null) {
      await this.router.navigate([SELECT_BRANCH_PATH], { queryParams: { returnUrl: target } });
      return;
    }

    await this.router.navigateByUrl(target);
  }
}
