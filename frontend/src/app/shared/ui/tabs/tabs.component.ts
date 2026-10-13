import { ChangeDetectionStrategy, Component, ElementRef, booleanAttribute, computed, inject, input, model } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';

/** One tab; `label` is passed translated, `badge` is a small count or tag. */
export interface TabItem {
  readonly id: string;
  readonly label: string;
  readonly badge?: string | number | null;
  readonly disabled?: boolean;
}

let nextTabsId = 0;

/**
 * `<app-tabs [tabs]="tabs" [(active)]="active" />` then `@switch (active()) { … }` in the page: a WAI-ARIA tab
 * list with roving focus (Arrow keys move, Home/End jump, Enter/Space select); selection follows focus only on
 * Enter/Space so screen-reader users can browse. Panels are the page's own content: give them
 * `role="tabpanel"` and `[attr.aria-labelledby]="tabId(id)"`.
 */
@Component({
  selector: 'app-tabs',
  templateUrl: './tabs.component.html',
  styleUrl: './tabs.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.compact]': 'compact()' },
})
export class TabsComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly i18n = inject(I18nService);
  private readonly idPrefix = `app-tabs-${++nextTabsId}`;

  readonly tabs = input.required<readonly TabItem[]>();
  readonly active = model<string | null>(null);
  /** Inline in a toolbar row that draws its own rule (`<app-tabs compact />`): no bottom margin or rule. */
  readonly compact = input(false, { transform: booleanAttribute });

  protected readonly current = computed(() => this.active() ?? this.tabs().find((tab) => !tab.disabled)?.id ?? null);

  /** A count badge in the language's digits (round 2 R2-02: the Documents badge read "0" on Arabic pages); a tag as given. */
  protected readonly badgeFormat = computed(() => new Intl.NumberFormat(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { maximumFractionDigits: 0 }));

  protected badgeText(badge: string | number): string {
    return typeof badge === 'number' ? this.badgeFormat().format(badge) : badge;
  }

  tabId(id: string): string {
    return `${this.idPrefix}-${id}`;
  }

  protected select(tab: TabItem): void {
    if (!tab.disabled) {
      this.active.set(tab.id);
    }
  }

  protected onKeydown(event: KeyboardEvent, index: number): void {
    const enabled = this.tabs().filter((tab) => !tab.disabled);
    if (enabled.length === 0) {
      return;
    }

    const currentTab = this.tabs()[index];
    const position = currentTab ? enabled.indexOf(currentTab) : -1;
    let target: TabItem | undefined;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowLeft': {
        const forward = (event.key === 'ArrowRight') !== this.isRtl();
        target = enabled[(position + (forward ? 1 : enabled.length - 1)) % enabled.length];
        break;
      }
      case 'Home':
        target = enabled[0];
        break;
      case 'End':
        target = enabled[enabled.length - 1];
        break;
      case 'Enter':
      case ' ':
        if (currentTab) {
          this.select(currentTab);
        }

        event.preventDefault();
        return;
      default:
        return;
    }

    if (target) {
      event.preventDefault();
      this.host.querySelector<HTMLButtonElement>(`#${this.tabId(target.id)}`)?.focus();
    }
  }

  private isRtl(): boolean {
    return getComputedStyle(this.host).direction === 'rtl';
  }
}
