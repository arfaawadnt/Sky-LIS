import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/** `⌘+D` on Apple devices, `Ctrl+D` elsewhere: the browser's own "bookmark this page". */
export function bookmarkKeys(userAgent: string): string {
  return /Mac|iPhone|iPad|iPod/i.test(userAgent) ? '⌘+D' : 'Ctrl+D';
}

/**
 * "Keep this address" (change request LIS-CR-007, reviewer point C-01): every laboratory signs in at its own address, so the
 * sign-in pages tell how to keep it — the browser's bookmark keys and a *Copy address* button (`navigator.clipboard`, toast
 * "Copied"). `strip` (laboratory sign-in, under the card) offers *Hide*, which the page keeps in a signal for its own life;
 * `hint` (find page, under Continue) names the address that Continue opens. Nothing is stored on the device (no web storage,
 * no cookie): the "recent laboratories on this device" idea of LIS-RC-001 is deliberately not built.
 */
@Component({
  selector: 'app-keep-address',
  imports: [IconComponent],
  template: `
    <div class="keep" [class.strip]="variant() === 'strip'" role="note" [attr.aria-label]="i18n.t('auth.keepAddress.label')">
      <app-icon name="info" [size]="16" />
      <div class="body">
        <p class="text">{{ text() }}</p>
        <p class="row">
          <span class="address" dir="ltr">{{ address() }}</span>
          <button type="button" class="link-btn" (click)="copy()">
            <app-icon name="clipboard" [size]="14" />
            <span>{{ i18n.t('auth.keepAddress.copy') }}</span>
          </button>
          @if (variant() === 'strip') {
            <button type="button" class="link-btn quiet" (click)="hide.emit()">{{ i18n.t('auth.keepAddress.hide') }}</button>
          }
        </p>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .keep {
      display: flex;
      align-items: flex-start;
      gap: var(--sp-2);
      margin-block-start: var(--sp-4);
      padding: 10px 12px;
      border: 1px dashed var(--line);
      border-radius: var(--r);
      background: var(--surface-2);
      color: var(--ink-2);
      font-size: 13px;
    }

    .keep.strip {
      margin: 0;
      border-style: solid;
      border-color: var(--line-soft);
      background: var(--surface);
    }

    .body {
      min-width: 0;
      flex: 1;
    }

    .text,
    .row {
      margin: 0;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 12px;
      margin-block-start: 4px;
    }

    .address {
      min-width: 0;
      overflow-wrap: anywhere;
      font-family: var(--mono);
      color: var(--navy);
    }

    .link-btn {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 0;
      border: 0;
      background: none;
      color: var(--blue-2);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
    }

    .link-btn.quiet {
      color: var(--muted);
      font-weight: 400;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KeepAddressComponent {
  protected readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);
  private readonly window = inject(DOCUMENT).defaultView;

  /** The full address to keep (`https://host/lab/<slug>`). */
  readonly address = input.required<string>();
  readonly variant = input<'strip' | 'hint'>('strip');
  readonly hide = output();

  private readonly keys = bookmarkKeys(this.window?.navigator.userAgent ?? '');

  protected readonly text = computed(() => this.i18n.tf(this.variant() === 'strip' ? 'auth.keepAddress.strip' : 'auth.keepAddress.hint', { keys: this.keys }));

  protected async copy(): Promise<void> {
    const clipboard = this.window?.navigator.clipboard;
    try {
      if (!clipboard) {
        throw new Error('no clipboard');
      }

      await clipboard.writeText(this.address());
      this.toast.success(this.i18n.t('auth.keepAddress.copied'));
    } catch {
      this.toast.error(this.i18n.t('auth.keepAddress.copyFailed'));
    }
  }
}
