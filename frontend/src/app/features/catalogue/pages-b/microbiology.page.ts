import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { I18nService } from '../../../core/i18n/i18n.service';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';

/** The lists of the microbiology masters, in the order of SA-14 (they are also the child route paths). */
export const MICROBIOLOGY_TABS = ['drug-types', 'commercial-names', 'antibiotics', 'organisms', 'sensitivity-values'] as const;

export type MicrobiologyTab = (typeof MICROBIOLOGY_TABS)[number];

/** The tab of a URL: the last path segment that names a microbiology list, else the first list. */
export function microbiologyTabOf(url: string): MicrobiologyTab {
  const segments = url.split(/[?#]/)[0]?.split('/') ?? [];
  return MICROBIOLOGY_TABS.find((tab) => segments.includes(tab)) ?? MICROBIOLOGY_TABS[0];
}

/**
 * Drug types · Commercial names · Antibiotics · Organisms · Sensitivity values (SRS CP-56, SA-14): one tab per list;
 * each tab is a child route holding the list's master-data page, so every list has its own URL.
 */
@Component({
  selector: 'app-catalogue-b-microbiology-page',
  imports: [TabsComponent, RouterOutlet],
  template: `
    <nav class="tabs" [attr.aria-label]="i18n.t('catalogue-b.microbiology.title')">
      <app-tabs [tabs]="tabs()" [active]="current()" (activeChange)="go($event)" />
    </nav>
    <div role="tabpanel" [id]="panelId()" [attr.aria-labelledby]="labelId()">
      <router-outlet />
    </div>
  `,
  styles: `
    :host { display: block; }
    .tabs { margin-block-end: var(--sp-4); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MicrobiologyPage {
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly tabsComponent = viewChild(TabsComponent);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly current = computed<MicrobiologyTab>(() => microbiologyTabOf(this.url()));

  protected readonly tabs = computed<readonly TabItem[]>(() => MICROBIOLOGY_TABS.map((id) => ({ id, label: this.i18n.t(`catalogue-b.microbiology.tab.${id}`) })));

  protected readonly labelId = computed(() => this.tabsComponent()?.tabId(this.current()) ?? null);

  protected readonly panelId = computed(() => {
    const label = this.labelId();
    return label === null ? null : `${label}-panel`;
  });

  constructor() {
    void this.i18n.load('catalogue-b');
  }

  protected go(tab: string | null): void {
    if (tab !== null && tab !== this.current()) {
      void this.router.navigate([tab], { relativeTo: this.route });
    }
  }
}
