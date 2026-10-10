import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

/**
 * The top band of the public pages (prototype `.public-nav`): logo, language switch, sign-in and "subscribe now".
 * `compact` hides the anchors of the landing page (the wizard shows only the brand and the language).
 */
@Component({
  selector: 'app-public-header',
  imports: [RouterLink, IconComponent],
  template: `
    <header class="public-nav">
      <a class="brand" routerLink="/welcome" [attr.aria-label]="i18n.t('public.brand')">
        <img src="/brand/sky-lis-logo.png" alt="" height="40" />
        <span>{{ i18n.t('public.brand') }}</span>
      </a>
      @if (!compact()) {
        <nav class="anchors" [attr.aria-label]="i18n.t('public.nav.label')">
          <button type="button" class="anchor" (click)="scrollTo('features')">{{ i18n.t('public.nav.features') }}</button>
          <button type="button" class="anchor" (click)="scrollTo('portals')">{{ i18n.t('public.nav.portals') }}</button>
          <button type="button" class="anchor" (click)="scrollTo('release')">{{ i18n.t('public.nav.release') }}</button>
        </nav>
      }
      <div class="actions">
        <button type="button" class="lang-btn" (click)="toggleLanguage()" [attr.aria-label]="i18n.t('public.switchLanguage')">
          <app-icon name="globe" [size]="15" />
          <span>{{ otherLanguage() }}</span>
        </button>
        <a class="btn" routerLink="/login">{{ i18n.t('public.signIn') }}</a>
        @if (!compact()) {
          <a class="btn primary" routerLink="/welcome/subscribe">{{ i18n.t('public.subscribeNow') }}</a>
        }
      </div>
    </header>
  `,
  styles: `
    .public-nav {
      display: flex;
      align-items: center;
      gap: var(--sp-4);
      padding: var(--sp-3) var(--sp-5);
      background: var(--paper);
      border-block-end: 1px solid var(--line);
      position: sticky;
      inset-block-start: 0;
      z-index: var(--z-topbar);
    }

    .brand {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      color: var(--navy);
      font-weight: 700;
      font-size: var(--fs-lg);
      text-decoration: none;
    }

    .anchors {
      display: flex;
      gap: var(--sp-4);
      margin-inline-start: var(--sp-4);
    }

    .anchor {
      border: 0;
      background: none;
      padding: 4px 2px;
      color: var(--ink-2);
      font-weight: 600;
      font-size: var(--fs-sm);
      cursor: pointer;
    }

    .anchor:hover {
      color: var(--blue-2);
    }

    .actions {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      margin-inline-start: auto;
    }

    .lang-btn {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      border: 1px solid var(--line);
      background: var(--surface);
      color: var(--ink-2);
      border-radius: var(--r-pill);
      padding: 5px 12px;
      font-size: var(--fs-sm);
      cursor: pointer;
    }

    a.btn {
      text-decoration: none;
    }

    @media (max-width: 760px) {
      .anchors {
        display: none;
      }

      .public-nav {
        padding: var(--sp-2) var(--sp-3);
        gap: var(--sp-2);
      }

      .brand span {
        display: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicHeaderComponent {
  protected readonly i18n = inject(I18nService);

  readonly compact = input(false);

  protected readonly otherLanguage = computed(() => (this.i18n.lang() === 'en' ? 'العربية' : 'English'));

  private readonly document = inject(DOCUMENT);

  /** In-page sections of the landing page; a plain hash link would leave the routed page. */
  protected scrollTo(sectionId: string): void {
    this.document.getElementById(sectionId)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected toggleLanguage(): void {
    this.i18n.setLanguage(this.i18n.lang() === 'en' ? 'ar' : 'en');
  }
}
