import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';

/**
 * The Super Admin landing page (SRS SA-01 placeholder): a greeting and the KPI tiles the platform dashboard
 * (SA-03, module `plat`) will fill. Lives under `/admin`, reachable for the platform tenant only (`platformGuard`).
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

  constructor() {
    void this.i18n.load('adminHome');
  }
}
