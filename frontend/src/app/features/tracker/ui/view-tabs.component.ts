import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { type LabTrackerCounters, type LabTrackerView, TRACKER_LINKS } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { num } from './tracker-format';

interface ViewTab {
  readonly view: LabTrackerView | null;
  readonly label: string;
  readonly count: string | null;
}

/**
 * The desk views: All · Reception · Bench (a link to the Worklist, which stays the bench's page) · Verification · Front
 * desk. Presets are toggle buttons (`aria-pressed`); counts where the day's counters give them.
 */
@Component({
  selector: 'app-tracker-view-tabs',
  imports: [RouterLink, IconComponent],
  template: `
    <nav class="views" [attr.aria-label]="i18n.t('tracker.view.label')">
      @for (tab of tabs(); track tab.view) {
        <button type="button" class="view" [attr.aria-pressed]="tab.view === view()" (click)="viewChange.emit(tab.view)">
          {{ tab.label }}
          @if (tab.count !== null) {
            <span class="count">{{ tab.count }}</span>
          }
        </button>
        @if (tab.view === 'Reception') {
          <a class="view" [routerLink]="benchLink">{{ i18n.t('tracker.view.Bench') }}<app-icon name="chevron-right" [size]="13" class="flip" /></a>
        }
      }
    </nav>
  `,
  styles: `
    .views {
      display: flex;
      gap: 2px;
      border-block-end: 1px solid var(--line);
      overflow-x: auto;
      overflow-y: hidden;
    }

    .view {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 9px 14px;
      margin-block-end: -1px;
      background: none;
      border: 0;
      border-block-end: 2px solid transparent;
      color: var(--muted);
      font-weight: 600;
      white-space: nowrap;
      text-decoration: none;
      cursor: pointer;
    }

    .view:hover {
      color: var(--ink-2);
    }

    .view[aria-pressed='true'] {
      color: var(--navy);
      border-block-end-color: var(--violet);
      background: var(--violet-soft);
      border-radius: 8px 8px 0 0;
    }

    .count {
      background: var(--line-soft);
      color: var(--ink-2);
      border-radius: var(--r-pill);
      font-size: 10.5px;
      padding: 1px 7px;
    }

    .view[aria-pressed='true'] .count {
      background: var(--violet);
      color: var(--paper);
    }

    :host-context([dir='rtl']) .flip {
      transform: scaleX(-1);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ViewTabsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly benchLink = TRACKER_LINKS.worklist;

  readonly counters = input.required<LabTrackerCounters>();
  readonly view = input<LabTrackerView | null>(null);
  readonly viewChange = output<LabTrackerView | null>();

  protected readonly tabs = computed<readonly ViewTab[]>(() => {
    const counters = this.counters();
    const lang = this.i18n.lang();
    return [
      { view: null, label: this.i18n.t('tracker.view.All'), count: num(counters.total, lang) },
      { view: 'Reception', label: this.i18n.t('tracker.view.Reception'), count: null },
      { view: 'Verification', label: this.i18n.t('tracker.view.Verification'), count: num(counters.awaitingVerification, lang) },
      { view: 'FrontDesk', label: this.i18n.t('tracker.view.FrontDesk'), count: num(counters.ready, lang) },
    ];
  });
}
