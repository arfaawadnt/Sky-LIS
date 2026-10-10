import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { AUTH_API, type SessionResponse } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';

/** The signed-in landing page: the session as the server resolves it per request (02 §4.4), read through a resource (01 §6.3). */
@Component({
  selector: 'app-home-page',
  templateUrl: './home.page.html',
  styleUrl: './home.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthStore);

  protected readonly session = httpResource<SessionResponse>(() => AUTH_API.me);

  constructor() {
    void this.i18n.load('home');
  }
}
