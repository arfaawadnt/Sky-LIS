import { ChangeDetectionStrategy, Component, DOCUMENT, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { AuthStore } from '../core/auth/auth.store';
import { I18nService } from '../core/i18n/i18n.service';
import { PermissionsService } from '../core/permissions/permissions.service';
import { PortalService } from '../core/portal/portal.service';
import { followProfileChanges } from '../features/account/account.guards';
import { SearchFocusService } from '../shared/ui/search-box/search-focus.service';
import { type VisibleNavGroup, filterNav, firstEntry } from './nav.filter';
import { NAV_GROUPS, NAV_REGISTRY } from './nav.registry';
import { ShellSidebarComponent } from './shell-sidebar.component';
import { ShellTopbarComponent } from './shell-topbar.component';

/** The sidebar becomes a drawer below this width (px); keep in step with the 959px media queries of the shell CSS. */
const NARROW_BELOW = 960;

/**
 * The signed-in layout (01 §6.2, prototypes' `.shell`): sidebar + top bar + routed page + footer. It owns the
 * layout state (collapsed, mobile drawer, user menu, navigation search); the menu button at the start of the top bar
 * collapses the sidebar (or opens the drawer under 960px). The global keys: Ctrl+K focuses the
 * first search box of the page (else the navigation search), Esc closes the user menu, then the mobile drawer.
 * The navigation is the registry filtered by portal (`PortalService`: admin for the platform tenant) and
 * permission.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, ShellSidebarComponent, ShellTopbarComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onDocumentKeydown($event)', '(window:resize)': 'onResize()' },
})
export class ShellComponent {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly portal = inject(PortalService);
  private readonly permissions = inject(PermissionsService);
  private readonly router = inject(Router);
  private readonly searchFocus = inject(SearchFocusService);

  private readonly view = inject(DOCUMENT).defaultView;

  protected readonly collapsed = signal(false);
  protected readonly mobileOpen = signal(false);
  /** Under 960px the sidebar is an off-canvas drawer (same breakpoint as the sidebar and top bar CSS). */
  protected readonly narrow = signal(this.isNarrow());
  protected readonly userMenuOpen = signal(false);
  protected readonly navSearch = signal('');

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly currentPath = computed(() => this.currentUrl().split('?')[0]?.split('#')[0] ?? '/');

  protected readonly groups = computed<readonly VisibleNavGroup[]>(() =>
    filterNav(
      NAV_GROUPS,
      NAV_REGISTRY,
      this.portal.portal(),
      (permission) => this.permissions.can(permission),
      (entry) => this.i18n.t(entry.i18nKey),
      this.navSearch(),
    ),
  );

  protected readonly portalLabel = computed(() => this.i18n.t(this.portal.portal() === 'admin' ? 'shell.portal.admin' : 'shell.portal.client'));

  protected readonly roleName = computed(() => this.auth.profile()?.roleName ?? null);

  constructor() {
    followProfileChanges(); // R2-01: a password changed in another tab ends this tab's forced change
  }

  /** The menu button at the start of the top bar: collapses / expands the sidebar, or opens the drawer under 960px. */
  protected onMenuButton(): void {
    if (this.narrow()) {
      this.toggleMobile();
    } else {
      this.collapsed.update((value) => !value);
    }
  }

  protected onResize(): void {
    this.narrow.set(this.isNarrow());
  }

  private isNarrow(): boolean {
    return (this.view?.innerWidth ?? NARROW_BELOW) < NARROW_BELOW;
  }

  protected toggleMobile(): void {
    this.mobileOpen.update((value) => !value);
  }

  protected closeMobile(): void {
    this.mobileOpen.set(false);
  }

  /** The topbar saves the preference afterwards (CP-65). */
  protected toggleLanguage(): void {
    this.i18n.setLanguage(this.i18n.lang() === 'en' ? 'ar' : 'en');
  }

  /** The store ends with a full page load of the sign-in page (A-01): no module keeps this user's state. */
  protected async signOut(): Promise<void> {
    await this.auth.signOut();
  }

  /** Enter in the navigation search opens the first match. */
  protected async openFirstMatch(): Promise<void> {
    const entry = firstEntry(this.groups());
    if (entry && this.navSearch().trim() !== '') {
      this.navSearch.set('');
      this.closeMobile();
      await this.router.navigateByUrl(entry.path);
    }
  }

  protected onDocumentKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
      if (this.searchFocus.focusFirst()) {
        event.preventDefault();
      }

      return;
    }

    if (event.key === 'Escape') {
      if (this.userMenuOpen()) {
        this.userMenuOpen.set(false);
      } else if (this.mobileOpen()) {
        this.closeMobile();
      }
    }
  }
}
