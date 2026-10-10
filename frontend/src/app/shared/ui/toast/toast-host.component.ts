import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent, type IconName } from '../icon/icon.component';
import { type Toast, ToastService } from './toast.service';

const ICON_BY_KIND: Readonly<Record<Toast['kind'], IconName>> = { success: 'check', error: 'alert', info: 'info', warn: 'alert' };

/** The toast stack at the bottom inline-end corner (prototype `.toasts`); placed once in the application root. */
@Component({
  selector: 'app-toast-host',
  imports: [IconComponent],
  template: `
    <div class="toasts">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [class]="'toast ' + toast.kind" [attr.role]="toast.kind === 'error' ? 'alert' : 'status'">
          <app-icon [name]="iconOf(toast)" [size]="16" />
          <div class="text">
            @if (toast.title; as title) {
              <b>{{ title }}</b>
            }
            <span>{{ toast.text }}</span>
          </div>
          <button type="button" class="dismiss" (click)="toasts.dismiss(toast.id)" [attr.aria-label]="i18n.t('shared.toast.dismiss')"><app-icon name="x" [size]="14" /></button>
        </div>
      }
    </div>
  `,
  styles: `
    .toasts {
      position: fixed;
      bottom: 18px;
      inset-inline-end: 18px;
      display: flex;
      flex-direction: column;
      gap: 8px;
      z-index: var(--z-toast);
      max-width: min(380px, calc(100vw - 36px));
    }

    .toast {
      background: var(--navy);
      color: #fff;
      border-radius: var(--r);
      padding: 10px 12px;
      display: flex;
      gap: 10px;
      align-items: flex-start;
      min-width: 260px;
      box-shadow: var(--sh-lg);
      font-size: 13px;
      animation: pop var(--ease);
    }

    .toast.success {
      background: var(--ok);
    }

    .toast.error {
      background: var(--bad);
    }

    .toast.warn {
      background: var(--warn);
    }

    .text {
      flex: 1;
      min-width: 0;
      display: grid;
      gap: 2px;
    }

    .text span {
      opacity: 0.92;
    }

    .dismiss {
      background: transparent;
      border: 0;
      color: inherit;
      opacity: 0.8;
      padding: 2px;
      border-radius: 6px;
      display: grid;
      place-items: center;
    }

    .dismiss:hover {
      opacity: 1;
      background: rgba(255, 255, 255, 0.15);
    }

    @keyframes pop {
      from {
        transform: translateY(8px);
        opacity: 0;
      }

      to {
        transform: none;
        opacity: 1;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToastHostComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly toasts = inject(ToastService);

  protected iconOf(toast: Toast): IconName {
    return ICON_BY_KIND[toast.kind];
  }
}
