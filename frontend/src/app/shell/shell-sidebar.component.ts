import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService } from '../core/i18n/i18n.service';
import { PortalService } from '../core/portal/portal.service';
import { IconComponent } from '../shared/ui/icon/icon.component';
import { type VisibleNavGroup, isActivePath } from './nav.filter';
import type { NavEntry } from './nav.registry';

/**
 * The dark sidebar of the shell (prototypes' `.sidebar`): logo, portal and role, the navigation grouped by SRS
 * module (already filtered by the shell). Collapsed (the menu button of the top bar) it shows icons only; under 960px the
 * shell turns it into an off-canvas drawer (`mobileOpen`).
 */
@Component({
  selector: 'app-shell-sidebar',
  imports: [RouterLink, IconComponent],
  templateUrl: './shell-sidebar.component.html',
  styleUrl: './shell-sidebar.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.collapsed]': 'collapsed()', '[class.mobile-open]': 'mobileOpen()', id: 'app-sidebar' },
})
export class ShellSidebarComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly portal = inject(PortalService);

  readonly groups = input.required<readonly VisibleNavGroup[]>();
  readonly currentPath = input.required<string>();
  readonly portalLabel = input.required<string>();
  readonly roleName = input<string | null>(null);
  readonly collapsed = input(false);
  readonly mobileOpen = input(false);

  /** A link was followed (the shell closes the mobile drawer). */
  readonly navigated = output<void>();
  readonly closeRequested = output<void>();

  protected isActive(entry: NavEntry): boolean {
    return isActivePath(entry, this.currentPath());
  }
}
