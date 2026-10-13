import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { DialogComponent } from '../../shared/ui/dialog/dialog.component';
import { I18nService } from '../i18n/i18n.service';
import { IdleWatcher } from './idle-watcher';

/**
 * The idle warning (validation F-01): "Your session will end in 60 s — Stay signed in / Sign out", with a live countdown,
 * on the kit's dialog. Placed once in the application root (outside the shell, so it also covers the Super Admin portal).
 * Closing it in any other way (Esc, the scrim, the close button) is the user's presence too: it counts as *Stay*.
 */
@Component({
  selector: 'app-idle-warning',
  imports: [DialogComponent],
  template: `
    <app-dialog [open]="watcher.warning()" (openChange)="onOpenChange($event)" [title]="i18n.t('shared.session.idleTitle')" size="sm">
      <p class="message">{{ i18n.tf('shared.session.idleMessage', { seconds: watcher.secondsLeft() }) }}</p>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="watcher.signOut()">{{ i18n.t('shared.session.signOut') }}</button>
        <button type="button" class="btn primary" data-autofocus="true" (click)="watcher.stay()">{{ i18n.t('shared.session.stay') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .message {
      margin: 0;
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IdleWarningComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly watcher = inject(IdleWatcher);

  protected onOpenChange(open: boolean): void {
    if (!open && this.watcher.warning()) {
      this.watcher.stay();
    }
  }
}
