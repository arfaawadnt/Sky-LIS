import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, computed, effect, inject, resource, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ACCOUNT_API, type LaboratoryLogoView, type LaboratoryProfile, type MyAccountProfile } from '../api/account.contracts';
import { AuthStore } from '../auth/auth.store';
import { I18nService } from '../i18n/i18n.service';
import { PortalService } from '../portal/portal.service';

/**
 * The client laboratory's identity for the shell (user request 2026-10-12): its name in the language of the page and
 * its logo, or its initials until the laboratory uploads one on the profile page. Read from the signed-in user's own
 * account (`GET /api/v1/account/profile`, `laboratory`) when the tenant changes, and again on `refresh()` (the profile
 * page calls it after an upload or a removal). The logo needs the bearer token, so it is fetched as a blob and shown
 * through an object URL (the site's CSP allows `img-src blob:`); the previous URL is revoked. The platform tenant (Super
 * Admin portal) has no laboratory: nothing is read there.
 */
@Injectable({ providedIn: 'root' })
export class LaboratoryBrandService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthStore);
  private readonly i18n = inject(I18nService);
  private readonly portal = inject(PortalService);

  private readonly profile = resource({
    params: () => (this.auth.isAuthenticated() && this.portal.portal() === 'client' ? this.auth.currentTenant() ?? undefined : undefined),
    loader: () => firstValueFrom(this.http.get<MyAccountProfile>(ACCOUNT_API.profile)),
  });

  /** The laboratory record of the session (kept while a refresh runs, so the top bar does not flicker). */
  private readonly laboratory = computed<LaboratoryProfile | null>(() => (this.profile.hasValue() ? this.profile.value().laboratory : null));

  /** The laboratory's name in the language of the page (the other language when one is missing); null when unknown. */
  readonly name = computed<string | null>(() => {
    const laboratory = this.laboratory();
    if (laboratory === null) {
      return null;
    }

    const preferred = this.i18n.lang() === 'ar' ? laboratory.nameAr : laboratory.nameEn;
    const name = (preferred ?? laboratory.nameEn ?? laboratory.nameAr ?? '').trim();
    return name === '' ? null : name;
  });

  readonly initials = computed(() => labInitials(this.name() ?? ''));

  /** Changes on every upload or removal; null when the laboratory has no logo. */
  readonly logoVersion = computed(() => this.laboratory()?.logoVersion ?? null);

  private readonly logo = resource({
    params: () => {
      const version = this.logoVersion();
      const tenant = this.auth.currentTenant();
      return version === null || tenant === null ? undefined : { tenant, version };
    },
    loader: () => firstValueFrom(this.http.get(ACCOUNT_API.laboratoryLogo, { responseType: 'blob' })),
  });

  private readonly url = signal<string | null>(null);
  private current: string | null = null;

  /** An object URL of the logo for an `<img>`, or null (show the initials). */
  readonly logoUrl = this.url.asReadonly();

  constructor() {
    effect(() => {
      const blob = this.logoVersion() !== null && this.logo.hasValue() ? this.logo.value() : null;
      this.replaceUrl(blob === null ? null : URL.createObjectURL(blob));
    });
    inject(DestroyRef).onDestroy(() => this.replaceUrl(null));
  }

  /** Reads the laboratory again (after the logo or the name changed). */
  refresh(): void {
    this.profile.reload();
  }

  /**
   * Uploads or replaces the laboratory's logo (`settings.manage`; PNG, JPEG or WebP up to 512 KB — the server decides
   * and refuses with ACC-040…042), then reads the laboratory again so every view shows the new image.
   */
  async upload(file: File): Promise<LaboratoryLogoView> {
    const form = new FormData();
    form.append('file', file, file.name);
    const view = await firstValueFrom(this.http.post<LaboratoryLogoView>(ACCOUNT_API.laboratoryLogo, form));
    this.refresh();
    return view;
  }

  /** Removes the laboratory's logo (ACC-045 when there is none); the initials come back. */
  async remove(): Promise<void> {
    await firstValueFrom(this.http.delete<void>(ACCOUNT_API.laboratoryLogo), { defaultValue: undefined });
    this.refresh();
  }

  /** Revokes the previous object URL; the previous one is kept in a field, not read from the signal, so the effect that calls this does not depend on what it writes. */
  private replaceUrl(next: string | null): void {
    if (this.current !== null) {
      URL.revokeObjectURL(this.current);
    }

    this.current = next;
    this.url.set(next);
  }
}

/** First letters of the first two words ("Demo Laboratory" → "DL"); an Arabic word's article "ال" is skipped ("العرض" → "ع"). */
export function labInitials(name: string): string {
  return name
    .split(/\s+/)
    .filter((word) => word.length > 0)
    .slice(0, 2)
    .map((word) => (word.startsWith('ال') && word.length > 2 ? word.charAt(2) : word.charAt(0)).toLocaleUpperCase())
    .join('');
}
