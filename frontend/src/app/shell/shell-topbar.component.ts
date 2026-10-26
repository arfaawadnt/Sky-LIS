import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, model, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { AuthStore } from '../core/auth/auth.store';
import { displayNameFor } from '../core/auth/display-name';
import { roleNameFor } from '../core/auth/role-name';
import { I18nService } from '../core/i18n/i18n.service';
import { changeBranchUrl } from '../features/account/account.guards';
import { SessionBranches } from '../features/account/state/session-branches.service';
import { IconComponent } from '../shared/ui/icon/icon.component';
import { SearchBoxComponent } from '../shared/ui/search-box/search-box.component';
import { ReturnLinkComponent, returnToPath } from '../shared/ui/return-link/return-link.component';
import { LabBrandComponent } from './lab-brand.component';
import { registryEntryFor } from './nav.guard';

/**
 * The top bar of the shell (prototypes' `.topbar`): menu button (collapses / expands the sidebar; under 960px opens it
 * as a drawer), the client laboratory (name and logo or initials, `LabBrandComponent`; the Sky LIS logo stays at the
 * top of the sidebar), the navigation search
 * (the Ctrl+K fallback when a page has no search box of its own), portal pill, language toggle, and the user
 * button with its menu (language, change of branch, sign-out). The shell owns the state; this component renders it.
 * Validation F-05 (CP-04): the client session's branch shows beside the portal pill and in the menu head; "Change branch"
 * opens the branch selection in change mode, and only when the user has more than one branch.
 */
@Component({
  selector: 'app-shell-topbar',
  imports: [IconComponent, SearchBoxComponent, LabBrandComponent, ReturnLinkComponent],
  templateUrl: './shell-topbar.component.html',
  styleUrl: './shell-topbar.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:click)': 'onDocumentClick($event)' },
})
export class ShellTopbarComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly router = inject(Router);
  protected readonly sessionBranches = inject(SessionBranches);

  readonly portalLabel = input.required<string>();
  readonly mobileOpen = input(false);
  readonly collapsed = input(false);
  /** Under 960px the menu button opens the drawer instead of collapsing the sidebar. */
  readonly narrow = input(false);
  readonly navSearch = model('');
  readonly menuOpen = model(false);

  readonly menuToggled = output<void>();
  readonly searchSubmitted = output<void>();
  readonly languageToggled = output<void>();
  readonly signOutRequested = output<void>();

  protected readonly menuExpanded = computed(() => (this.narrow() ? this.mobileOpen() : !this.collapsed()));

  private readonly currentUrl = toSignal(this.router.events.pipe(filter((event) => event instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });

  /** LIS-CR-014 T5: the name of the list a `returnTo` points at (its nav entry), for the Back chip; null = the generic label. */
  protected readonly returnLabel = computed(() => {
    const path = returnToPath(this.router.parseUrl(this.currentUrl()).queryParamMap.get('returnTo'));
    const entry = path === null ? null : registryEntryFor(path);
    return entry === null ? null : this.i18n.t(entry.i18nKey);
  });

  protected readonly menuLabel = computed(() =>
    this.i18n.t(this.narrow() ? 'shell.openMenu' : this.collapsed() ? 'shell.expandMenu' : 'shell.collapseMenu'));

  /** R-12: the user's own name in the language of the page (the member's full name in that language). */
  protected readonly displayName = computed(() => displayNameFor(this.auth.profile(), this.i18n.lang()));

  /** B-09: the role in the language of the page. */
  protected readonly roleName = computed(() => roleNameFor(this.auth.profile(), this.i18n.lang()));

  protected readonly initials = computed(() =>
    this.displayName()
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join(''),
  );

  /** F-05: the session's branch as "CODE · Name" in the language of the page, or null (no branch yet, or the platform portal). */
  protected readonly branchLabel = computed(() => {
    const branch = this.sessionBranches.current();
    return branch === null ? null : `${branch.code} · ${this.i18n.lang() === 'ar' ? branch.nameAr : branch.nameEn}`;
  });

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

  /** F-05 (CP-04): the branch selection in change mode; it comes back to the page the user was on. */
  protected onChangeBranch(): void {
    this.menuOpen.set(false);
    void this.router.navigateByUrl(changeBranchUrl(this.router, this.router.url));
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
