import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, model, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthStore } from '../core/auth/auth.store';
import { I18nService } from '../core/i18n/i18n.service';
import { PortalService } from '../core/portal/portal.service';
import { IconComponent } from '../shared/ui/icon/icon.component';
import { SearchBoxComponent } from '../shared/ui/search-box/search-box.component';

/**
 * The top bar of the shell (prototypes' `.topbar`): menu button (under 960px), brand, the navigation search
 * (the Ctrl+K fallback when a page has no search box of its own), portal pill, language toggle, and the user
 * button with its menu (language, sign-out). The shell owns the state; this component renders it.
 */
@Component({
  selector: 'app-shell-topbar',
  imports: [RouterLink, IconComponent, SearchBoxComponent],
  templateUrl: './shell-topbar.component.html',
  styleUrl: './shell-topbar.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:click)': 'onDocumentClick($event)' },
})
export class ShellTopbarComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthStore);
  protected readonly portal = inject(PortalService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly portalLabel = input.required<string>();
  readonly mobileOpen = input(false);
  readonly navSearch = model('');
  readonly menuOpen = model(false);

  readonly menuToggled = output<void>();
  readonly searchSubmitted = output<void>();
  readonly languageToggled = output<void>();
  readonly signOutRequested = output<void>();

  protected readonly displayName = computed(() => this.auth.currentUser()?.displayName ?? '');

  protected readonly roleName = computed(() => this.auth.profile()?.roleName ?? null);

  protected readonly initials = computed(() =>
    this.displayName()
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join(''),
  );

  protected readonly otherLanguageLabel = computed(() => (this.i18n.lang() === 'en' ? 'العربية' : 'English'));

  protected toggleUserMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected onLanguage(): void {
    this.menuOpen.set(false);
    this.languageToggled.emit();
    this.rememberLanguage();
  }

  /** CP-65: the switch is also the user's preference, saved on the membership by the auth store (fire and forget; the switch itself never waits). */
  private rememberLanguage(): void {
    const language = this.i18n.lang();
    if (!this.auth.isAuthenticated() || (language !== 'en' && language !== 'ar')) {
      return;
    }

    void this.auth.updateLanguage(language).catch(() => undefined);
  }

  protected onSignOut(): void {
    this.menuOpen.set(false);
    this.signOutRequested.emit();
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (!this.menuOpen()) {
      return;
    }

    const wrap = this.host.querySelector('.profile-wrap');
    if (wrap && !wrap.contains(event.target as Node)) {
      this.menuOpen.set(false);
    }
  }
}
