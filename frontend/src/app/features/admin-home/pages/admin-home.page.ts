import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';

import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { AdminHomeApi } from '../data/admin-home.api';

/** The permission of the platform dashboard (SA-03) whose figures the home shows. */
export const PLATFORM_VIEW = 'platform.view';

/**
 * The Super Admin landing page (SRS SA-01): a greeting and the Release-1 KPI tiles, Clients and Active subscriptions,
 * from the platform dashboard (SA-03) the dashboard page reads (UAT R2-05; the support-ticket and site-agent tiles
 * belong to Release 2). Lives under `/admin`, reachable for the platform tenant only (`platformGuard`).
 */
@Component({
  selector: 'app-admin-home-page',
  imports: [PageHeaderComponent, SectionCardComponent, StatCardComponent],
  templateUrl: './admin-home.page.html',
  styleUrl: './admin-home.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminHomePage {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthStore);
  private readonly api = inject(AdminHomeApi);

  /** Read only with the dashboard's permission; without it the tiles read "—" (the server decides regardless). */
  private readonly counts = resource({
    params: () => (this.auth.hasPermission(PLATFORM_VIEW) ? true : undefined),
    loader: ({ abortSignal }) => this.api.clientCounts(abortSignal),
  });

  protected readonly clients = computed(() => this.figure(this.counts.hasValue() ? this.counts.value().total : null));
  protected readonly activeSubscriptions = computed(() => this.figure(this.counts.hasValue() ? this.counts.value().active : null));

  constructor() {
    void this.i18n.load('adminHome');
  }

  private figure(value: number | null): string {
    return value === null ? '—' : new Intl.NumberFormat(this.i18n.lang() === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB').format(value);
  }
}
