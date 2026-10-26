import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';

/** The query parameter that names the list to go back to (LIS-CR-014 T5). */
export const RETURN_TO_PARAM = 'returnTo';

/** The longest `returnTo` accepted (a list URL with its whole filter fits easily). */
export const RETURN_TO_MAX_LENGTH = 2000;

/**
 * The value of a `returnTo` parameter when it is an in-app path, else null: it starts with a single `/` (never `//` or
 * `/\`, which a browser reads as another host), carries no backslash, no control or white-space character and no scheme,
 * and is at most {@link RETURN_TO_MAX_LENGTH} long. Only such a value is ever navigated to (no open redirect).
 */
export function safeReturnTo(value: string | null | undefined): string | null {
  if (typeof value !== 'string' || value.length < 1 || value.length > RETURN_TO_MAX_LENGTH) {
    return null;
  }

  if (!value.startsWith('/') || value.startsWith('//') || value.includes('\\')) {
    return null;
  }

  // eslint-disable-next-line no-control-regex -- control characters are exactly what this refuses
  if (/[\u0000-\u001f\u007f\s]/.test(value) || /^\/*[a-z][a-z0-9+.-]*:/i.test(value)) {
    return null;
  }

  return value;
}

/** The path of a safe `returnTo` without its query or fragment (`/tracker?day=…` → `/tracker`): what a caller matches a nav entry on. */
export function returnToPath(value: string | null | undefined): string | null {
  const safe = safeReturnTo(value);
  return safe === null ? null : (safe.split(/[?#]/, 1)[0] ?? safe);
}

/**
 * `<app-return-link [label]="…" />` (LIS-CR-014 T5): a "Back to …" chip shown while the current URL carries a safe
 * `returnTo` (see {@link safeReturnTo}); a click navigates there, so a list opened with its filter in the URL comes back
 * exactly as it was left. Nothing renders without one (or when it names the page already shown). `label` is the name of
 * the page to go back to (for example the nav entry of {@link returnToPath}); without it the chip reads "Back to the list".
 * Mounted once in the shell's top bar, it serves every page without changes to them.
 */
@Component({
  selector: 'app-return-link',
  imports: [IconComponent],
  template: `
    @if (target(); as target) {
      <a class="return" [href]="target" [attr.aria-label]="text()" [title]="text()" (click)="go($event, target)">
        <app-icon name="chevron-left" [size]="14" />
        <span>{{ text() }}</span>
      </a>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      min-width: 0;
    }

    .return {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      max-width: 100%;
      padding: 3px 10px 3px 6px;
      border: 1px solid var(--line);
      border-radius: var(--r-pill);
      background: var(--surface);
      color: var(--ink-2);
      font-size: var(--fs-sm);
      font-weight: 600;
      text-decoration: none;
      white-space: nowrap;
    }

    .return span {
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .return:hover {
      background: var(--row-hover);
      color: var(--ink);
    }

    .return:focus-visible {
      outline: none;
      box-shadow: var(--focus-ring);
    }

    :host-context([dir='rtl']) app-icon {
      transform: scaleX(-1);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReturnLinkComponent {
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  /** The name of the page to go back to (translated by the caller); null = the generic "Back to the list". */
  readonly label = input<string | null>(null);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** The safe `returnTo` of the current URL, unless it is the page shown. */
  protected readonly target = computed(() => {
    const current = this.url();
    const target = safeReturnTo(this.router.parseUrl(current).queryParamMap.get(RETURN_TO_PARAM));
    return target === null || returnToPath(target) === current.split(/[?#]/, 1)[0] ? null : target;
  });

  protected readonly text = computed(() => {
    const label = this.label();
    return label === null || label.trim() === '' ? this.i18n.t('shared.returnLink.back') : this.i18n.tf('shared.returnLink.backTo', { page: label });
  });

  /** A plain click navigates in the app; a modified click (new tab or window) keeps the browser's own behaviour. */
  protected go(event: MouseEvent, target: string): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }

    event.preventDefault();
    void this.router.navigateByUrl(target);
  }
}
