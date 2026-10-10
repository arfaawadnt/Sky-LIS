import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { AuthStore } from '../core/auth/auth.store';
import { I18nService } from '../core/i18n/i18n.service';
import { PermissionsService } from '../core/permissions/permissions.service';
import { PortalService } from '../core/portal/portal.service';
import { SearchFocusService } from '../shared/ui/search-box/search-focus.service';
import { type VisibleNavGroup, filterNav, firstEntry } from './nav.filter';
import { NAV_GROUPS, NAV_REGISTRY, inReleaseScope } from './nav.registry';
import { ShellSidebarComponent } from './shell-sidebar.component';
import { ShellTopbarComponent } from './shell-topbar.component';

/**
 * The signed-in layout (01 §6.2, prototypes' `.shell`): sidebar + top bar + routed page + footer. It owns the
 * layout state (collapsed, mobile drawer, user menu, navigation search) and the global keys: Ctrl+K focuses the
 * first search box of the page (else the navigation search), Esc closes the user menu, then the mobile drawer.
 * The navigation is the registry filtered by portal (`PortalService`: admin for the platform tenant) and
 * permission.
 */
/** The registry entries of the delivered release (RELEASE_SCOPE). */
const RELEASE_ENTRIES = NAV_REGISTRY.filter((entry) => inReleaseScope(entry.release));

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, ShellSidebarComponent, ShellTopbarComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onDocumentKeydown($event)' },
})
export class ShellComponent {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly portal = inject(PortalService);
  private readonly permissions = inject(PermissionsService);
  private readonly router = inject(Router);
  private readonly searchFocus = inject(SearchFocusService);

  protected readonly collapsed = signal(false);
  protected readonly mobileOpen = signal(false);
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
      RELEASE_ENTRIES,
      this.portal.portal(),
      (permission) => this.permissions.can(permission),
      (entry) => this.i18n.t(entry.i18nKey),
      this.navSearch(),
    ),
  );

  protected readonly portalLabel = computed(() => this.i18n.t(this.portal.portal() === 'admin' ? 'shell.portal.admin' : 'shell.portal.client'));

  protected readonly roleName = computed(() => this.auth.profile()?.roleName ?? null);

  protected toggleCollapsed(): void {
    this.collapsed.update((value) => !value);
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

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/login']);
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
