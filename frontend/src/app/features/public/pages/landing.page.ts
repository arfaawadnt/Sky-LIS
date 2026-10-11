import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { type Release, inReleaseScope } from '../../../core/release';
import { IconComponent, type IconName } from '../../../shared/ui/icon/icon.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { PublicHeaderComponent } from '../ui/public-header.component';

interface Feature {
  readonly icon: IconName;
  readonly key: string;
  /** The release that delivers it: the page advertises only what the delivered scope contains (UAT R2-05). */
  readonly release?: Release;
}

/** The features of the delivered release, in their order. */
function scoped(features: readonly Feature[]): readonly Feature[] {
  return features.filter((feature) => inReleaseScope(feature.release ?? 'R1'));
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

  protected readonly clientFeatures: readonly Feature[] = scoped([
    { icon: 'clipboard', key: 'registration' },
    { icon: 'barcode', key: 'samples', release: 'R2' },
    { icon: 'flask', key: 'results' },
    { icon: 'layers', key: 'catalogue' },
    { icon: 'coins', key: 'finance' },
    { icon: 'shield', key: 'setup' },
    { icon: 'plug', key: 'analysers', release: 'R2' },
    { icon: 'zap', key: 'automation', release: 'R2' },
  ]);

  protected readonly adminFeatures: readonly Feature[] = scoped([
    { icon: 'building', key: 'clients' },
    { icon: 'database', key: 'masterData' },
    { icon: 'library', key: 'library', release: 'R2' },
    { icon: 'server', key: 'operations' },
  ]);

  protected readonly qualities: readonly Feature[] = [
    { icon: 'shield', key: 'secure' },
    { icon: 'globe', key: 'bilingual' },
    { icon: 'grid', key: 'responsive' },
  ];

  protected readonly releaseHighlights: readonly string[] = ['registration', 'results', 'catalogue', 'finance', 'setup', 'audit'];

  protected readonly steps: readonly string[] = ['details', 'verify', 'account', 'signIn'];

  constructor() {
    void this.i18n.load('public');
  }
}
