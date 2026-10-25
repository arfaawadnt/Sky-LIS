import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { AuthStore } from './core/auth/auth.store';
import { IdleWarningComponent } from './core/auth/idle-warning.component';
import { SubscriptionModalComponent } from './features/account/ui/subscription-notice';
import { I18nService } from './core/i18n/i18n.service';
import { DialogHostComponent } from './shared/ui/dialog/dialog-host.component';
import { ToastHostComponent } from './shared/ui/toast/toast-host.component';

/**
 * The application root (01 §6.2): language and direction on the host, the start-up gate (nothing routes until
 * the silent refresh settled), the two overlay hosts of the kit (toasts, confirm dialogs) and the idle warning of the
 * session (F-01, `IdleWatcher`). The signed-in
 * layout is the `ShellComponent` route; the sign-in page renders outside it.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHostComponent, DialogHostComponent, IdleWarningComponent, SubscriptionModalComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.dir]': 'i18n.dir()', '[attr.lang]': 'i18n.lang()' },
})
export class App {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthStore);

  constructor() {
    void this.i18n.load('shell');
    void this.i18n.load('shared');
  }
}
