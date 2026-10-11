import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';

/**
 * The client home for a user whose menu is empty (R1-04): `homeRedirect` sends everyone else to the dashboard or their
 * first menu entry. A plain welcome with the way forward; no session internals (tenant id, raw expiry, permission keys).
 */
@Component({
  selector: 'app-home-page',
  imports: [RouterLink],
  templateUrl: './home.page.html',
  styleUrl: './home.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthStore);

  /** The profile is open to every client user (no permission), so it is always a way forward. */
  protected readonly profilePath = '/account/profile';

  constructor() {
    void this.i18n.load('home');
  }
}
