import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { dateTime, pick } from '../data/registration-format';
import type { SampleFlagRow } from '../state/registration.facade';

/** A switch of one tube: the tube key and the new state. */
export interface SampleOutsideChange {
  readonly key: string;
  readonly outside: boolean;
}

/** A collection time typed for one tube (`datetime-local` value, '' = now). */
export interface SampleCollectedAtChange {
  readonly key: string;
  readonly collectedAt: string;
}

/**
 * CP-06 "Sample collected outside the laboratory" per sample (change request LIS-CR-008, reviewer point C-02): one row per
 * tube of the order (unit letter, sample type and condition) with a switch and, once switched on, an optional collection
 * time (empty = the registration time); "All outside" keeps the one-click case. `readOnly` (a saved visit) lists the tubes
 * as recorded with an "Outside" tag and the time: the flag is set at registration only.
 */
@Component({
  selector: 'app-reg-sample-flags',
  imports: [BadgeComponent],
  host: { role: 'group', '[attr.aria-label]': "i18n.t('registration.sampleFlags.title')" },
  template: `
    <div class="head">
      <span class="title">{{ i18n.t('registration.sampleFlags.title') }}</span>
      @if (!readOnly() && rows().length > 1) {
        <button type="button" class="btn sm" (click)="allChanged.emit(!allOutside())">
          {{ i18n.t(allOutside() ? 'registration.sampleFlags.noneOutside' : 'registration.sampleFlags.allOutside') }}
        </button>
      }
    </div>
    <ul class="rows">
      @for (row of rows(); track row.key) {
        <li class="row" [class.outside]="row.outside">
          <span class="unit mono">{{ row.unitCode }}</span>
          <span class="type">
            {{ name(row.sampleTypeNameEn, row.sampleTypeNameAr) }}
            @if (row.sampleConditionNameEn) {
              <span class="muted"> · {{ name(row.sampleConditionNameEn, row.sampleConditionNameAr) }}</span>
            }
            @if (row.sampleNumber) {
              <span class="mono muted"> · {{ row.sampleNumber }}</span>
            }
          </span>
          @if (readOnly()) {
            @if (row.outside) {
              <app-badge tone="info">{{ i18n.t('registration.sampleFlags.outside') }}</app-badge>
              @if (row.collectedAt) {
                <span class="muted">{{ i18n.tf('registration.sampleFlags.collectedAtValue', { time: when(row.collectedAt) }) }}</span>
              }
            } @else {
              <span class="muted">{{ i18n.t('registration.sampleFlags.inside') }}</span>
            }
          } @else {
            <label class="switch">
              <input type="checkbox" role="switch" [checked]="row.outside" (change)="outsideChanged.emit({ key: row.key, outside: checked($event) })" />
              {{ i18n.t('registration.sampleFlags.collectedOutside') }}
            </label>
            @if (row.outside) {
              <label class="time">
                <span class="muted">{{ i18n.t('registration.sampleFlags.collectedAt') }}</span>
                <input class="inp sm" type="datetime-local" [max]="latest()" [value]="row.collectedAt" (change)="collectedAtChanged.emit({ key: row.key, collectedAt: value($event) })" />
              </label>
            }
          }
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: block;
      margin-block-start: var(--sp-3);
      font-size: var(--fs-sm);
    }

    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-2);
      margin-block-end: var(--sp-2);
    }

    .title {
      font-weight: 600;
    }

    .rows {
      display: grid;
      gap: var(--sp-1);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2) var(--sp-3);
      padding: var(--sp-1) var(--sp-2);
      border: 1px solid var(--line);
      border-radius: var(--r-sm);
    }

    .row.outside {
      border-color: var(--blue);
      background: var(--blue-soft);
    }

    .unit {
      min-inline-size: 1.5em;
      font-weight: 600;
      text-align: center;
    }

    .type {
      flex: 1 1 10rem;
    }

    .switch,
    .time {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-1);
      cursor: pointer;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SampleFlagsComponent {
  protected readonly i18n = inject(I18nService);

  readonly rows = input<readonly SampleFlagRow[]>([]);
  readonly readOnly = input(false);
  readonly outsideChanged = output<SampleOutsideChange>();
  readonly collectedAtChanged = output<SampleCollectedAtChange>();
  /** "All outside" (true) or "None outside" (false). */
  readonly allChanged = output<boolean>();

  protected readonly allOutside = computed(() => this.rows().length > 0 && this.rows().every((row) => row.outside));

  /** The latest time the picker offers: now, in the browser's local time (`datetime-local` format). */
  protected latest(): string {
    const now = new Date();
    const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
    return local.toISOString().slice(0, 16);
  }

  protected name(en: string | null | undefined, ar: string | null | undefined): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected when(utc: string): string {
    return dateTime(utc, this.i18n.lang());
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
