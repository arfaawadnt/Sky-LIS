import { ChangeDetectionStrategy, Component, inject, input, model, output, signal } from '@angular/core';

import type { SiteAgentSecretResponse } from '../../../core/api/integration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/**
 * Shows a machine credential's client id and secret exactly once (02 §6.3): copy buttons, the expiry and a warning.
 * The secret lives only in this component's input while the dialog is open; nothing is stored in the browser.
 */
@Component({
  selector: 'app-integration-secret-dialog',
  imports: [DialogComponent, IconComponent],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('integration.secret.title')" size="md" [dismissible]="false" (closed)="dismissed.emit()">
      @if (secret(); as value) {
        <p class="warn-strip" role="alert">{{ i18n.t('integration.secret.warning') }}</p>
        <dl class="kv secret">
          <dt>{{ i18n.t('integration.secret.clientId') }}</dt>
          <dd>
            <code class="mono" data-testid="client-id">{{ value.clientId }}</code>
            <button type="button" class="btn sm ghost" (click)="copy(value.clientId)"><app-icon name="clipboard" [size]="14" />{{ i18n.t('integration.secret.copy') }}</button>
          </dd>
          <dt>{{ i18n.t('integration.secret.clientSecret') }}</dt>
          <dd>
            <code class="mono" data-testid="client-secret">{{ value.clientSecret }}</code>
            <button type="button" class="btn sm ghost" (click)="copy(value.clientSecret)"><app-icon name="clipboard" [size]="14" />{{ i18n.t('integration.secret.copy') }}</button>
          </dd>
          <dt>{{ i18n.t('integration.secret.expires') }}</dt>
          <dd>{{ value.expiresAtUtc.slice(0, 10) }}</dd>
        </dl>
        <p class="muted small">{{ i18n.t('integration.secret.hint') }}</p>
      }
      <label dialogFooter class="check">
        <input type="checkbox" [checked]="acknowledged()" (change)="acknowledged.set(!acknowledged())" />
        <span>{{ i18n.t('integration.secret.acknowledge') }}</span>
      </label>
      <button dialogFooter type="button" class="btn primary" [disabled]="!acknowledged()" (click)="close()">{{ i18n.t('integration.secret.done') }}</button>
    </app-dialog>
  `,
  styles: `
    .secret dd { display: flex; align-items: center; gap: var(--sp-2); flex-wrap: wrap; }
    .secret code { word-break: break-all; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SecretDialogComponent {
  protected readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);

  readonly open = model(false);
  readonly secret = input<SiteAgentSecretResponse | null>(null);
  readonly dismissed = output<void>();

  protected readonly acknowledged = signal(false);

  protected async copy(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toast.success(this.i18n.t('integration.secret.copied'));
    } catch {
      this.toast.warn(this.i18n.t('integration.secret.copyFailed'));
    }
  }

  protected close(): void {
    this.acknowledged.set(false);
    this.open.set(false);
    this.dismissed.emit();
  }
}
