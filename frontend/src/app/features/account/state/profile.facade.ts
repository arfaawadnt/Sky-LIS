import { Injectable, inject, resource } from '@angular/core';

import type { AccountLanguage, MyAccountProfile } from '../../../core/api/account.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { AccountApi } from '../data/account.api';
import { AccountSession } from './account-session.service';

/**
 * CP-65 / SA-21 state: the own profile (also cached for the guards), the language preference (saved on the
 * membership, applied to the interface at once, and picked up by the next session profile through a refresh) and
 * the password change (both values sealed in transit).
 */
@Injectable()
export class ProfileFacade {
  private readonly api = inject(AccountApi);
  private readonly auth = inject(AuthStore);
  private readonly i18n = inject(I18nService);
  private readonly session = inject(AccountSession);

  readonly profile = resource({
    loader: async (): Promise<MyAccountProfile> => {
      const profile = await this.api.profile();
      this.session.set(profile);
      return profile;
    },
  });

  readonly saveLanguage = mutation((language: AccountLanguage) => this.api.updateLanguage(language).then(() => language), {
    then: (language) => {
      this.i18n.setLanguage(language);
      this.session.invalidate();
      void this.auth.refresh();
      this.profile.reload();
    },
  });

  /** Re-reads the own profile when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01/C-13). A load in flight is left alone. */
  refresh(): void {
    this.profile.reload();
    this.saveLanguage.reset();
    this.changePassword.reset();
  }

  readonly changePassword = mutation((input: { currentPassword: string; newPassword: string }) => this.api.changePassword(input.currentPassword, input.newPassword), {
    then: () => {
      this.session.invalidate();
      this.profile.reload();
    },
  });
}
