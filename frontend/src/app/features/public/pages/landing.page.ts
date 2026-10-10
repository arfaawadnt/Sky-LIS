import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent, type IconName } from '../../../shared/ui/icon/icon.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { PublicHeaderComponent } from '../ui/public-header.component';

interface Feature {
  readonly icon: IconName;
  readonly key: string;
}

/**
 * The public website (SRS CP-01): what Sky LIS is, what each portal does, what release 1 delivers, demo figures,
 * and the two ways in (sign in, subscribe now). Static and anonymous; every text is in `public.{en,ar}.json`.
 */
@Component({
  selector: 'app-landing-page',
  imports: [RouterLink, IconComponent, StatCardComponent, PublicHeaderComponent],
  templateUrl: './landing.page.html',
  styleUrl: './landing.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingPage {
  protected readonly i18n = inject(I18nService);

  protected readonly clientFeatures: readonly Feature[] = [
    { icon: 'clipboard', key: 'registration' },
    { icon: 'barcode', key: 'samples' },
    { icon: 'flask', key: 'results' },
    { icon: 'coins', key: 'finance' },
    { icon: 'plug', key: 'analysers' },
    { icon: 'zap', key: 'automation' },
  ];

  protected readonly adminFeatures: readonly Feature[] = [
    { icon: 'building', key: 'clients' },
    { icon: 'database', key: 'masterData' },
    { icon: 'library', key: 'library' },
    { icon: 'server', key: 'operations' },
  ];

  protected readonly qualities: readonly Feature[] = [
    { icon: 'shield', key: 'secure' },
    { icon: 'globe', key: 'bilingual' },
    { icon: 'grid', key: 'responsive' },
  ];

  protected readonly releaseHighlights: readonly string[] = ['registration', 'samples', 'results', 'finance', 'setup', 'audit'];

  protected readonly steps: readonly string[] = ['details', 'verify', 'account', 'signIn'];

  constructor() {
    void this.i18n.load('public');
  }
}
