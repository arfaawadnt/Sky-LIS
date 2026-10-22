import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';

/** What the picker offers: the laboratory's today, yesterday, one chosen day, the last seven days or a from / to range. */
export type TrackerPeriodMode = 'today' | 'yesterday' | 'day' | 'last7' | 'custom';

export const TRACKER_PERIOD_MODES: readonly TrackerPeriodMode[] = ['today', 'yesterday', 'day', 'last7', 'custom'];

export interface TrackerRange {
  readonly from: string;
  readonly to: string;
}

/** `day` moved by `days` (calendar arithmetic on the date string, no clock involved). */
export function shiftDay(day: string, days: number): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/**
 * The period of the tracker in the page header: a preset (today · yesterday · a day · last 7 days · custom) and, by the
 * preset, a date or a from / to pair of `datetime-local` minutes. The values are the laboratory's wall-clock, which the
 * server reads in the laboratory's time zone; "today" is the day the server read by default, so the page rolls over at
 * midnight on its own. A range is sent once both ends are filled; `dayChange(null)` means today.
 */
@Component({
  selector: 'app-tracker-period',
  template: `
    <label class="part">
      <span class="visually-hidden">{{ i18n.t('tracker.period.label') }}</span>
      <select class="inp sm" [value]="mode()" (change)="onMode($event)">
        @for (option of modes; track option) {
          <option [value]="option">{{ i18n.t('tracker.period.' + option) }}</option>
        }
      </select>
    </label>
    @if (mode() === 'day') {
      <label class="part">
        <span class="visually-hidden">{{ i18n.t('tracker.day') }}</span>
        <input class="inp sm" type="date" [value]="day() ?? today()" (change)="onDay($event)" />
      </label>
    }
    @if (mode() === 'custom' || mode() === 'last7') {
      <label class="part">
        <span class="visually-hidden">{{ i18n.t('tracker.period.from') }}</span>
        <input class="inp sm" type="datetime-local" [value]="fromDraft()" [max]="toDraft()" (change)="onFrom($event)" />
      </label>
      <span class="dash" aria-hidden="true">–</span>
      <label class="part">
        <span class="visually-hidden">{{ i18n.t('tracker.period.to') }}</span>
        <input class="inp sm" type="datetime-local" [value]="toDraft()" [min]="fromDraft()" (change)="onTo($event)" />
      </label>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px;
    }

    .part {
      display: inline-flex;
    }

    .dash {
      color: var(--muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PeriodPickerComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly modes = TRACKER_PERIOD_MODES;

  /** The filter as the facade holds it (`day` null and no range = today). */
  readonly day = input<string | null>(null);
  readonly from = input<string | null>(null);
  readonly to = input<string | null>(null);

  /** The laboratory's today (`yyyy-MM-dd`), the server's default day. */
  readonly today = input.required<string>();

  readonly dayChange = output<string | null>();
  readonly rangeChange = output<TrackerRange>();

  /** The preset the user chose; absent, it is derived from the filter. */
  private readonly chosen = signal<TrackerPeriodMode | null>(null);

  /** Half-filled range inputs, kept until both ends are set. */
  private readonly draft = signal<Partial<TrackerRange>>({});

  protected readonly mode = computed<TrackerPeriodMode>(() => {
    const chosen = this.chosen();
    if (chosen !== null) {
      return chosen;
    }

    if (this.from() !== null) {
      return 'custom';
    }

    const day = this.day();
    return day === null ? 'today' : day === shiftDay(this.today(), -1) ? 'yesterday' : 'day';
  });

  protected readonly fromDraft = computed(() => this.draft().from ?? this.from() ?? `${this.today()}T00:00`);
  protected readonly toDraft = computed(() => this.draft().to ?? this.to() ?? `${this.today()}T23:59`);

  protected onMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value as TrackerPeriodMode;
    this.chosen.set(mode);
    this.draft.set({});
    switch (mode) {
      case 'today':
        this.dayChange.emit(null);
        return;
      case 'yesterday':
        this.dayChange.emit(shiftDay(this.today(), -1));
        return;
      case 'day':
        if (this.day() === null) {
          this.dayChange.emit(this.today());
        }
        return;
      case 'last7':
        this.rangeChange.emit({ from: `${shiftDay(this.today(), -6)}T00:00`, to: `${this.today()}T23:59` });
        return;
      case 'custom':
        this.rangeChange.emit({ from: this.fromDraft(), to: this.toDraft() });
        return;
    }
  }

  protected onDay(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (value.length > 0) {
      this.dayChange.emit(value === this.today() ? null : value);
    }
  }

  protected onFrom(event: Event): void {
    this.edit({ from: (event.target as HTMLInputElement).value });
  }

  protected onTo(event: Event): void {
    this.edit({ to: (event.target as HTMLInputElement).value });
  }

  private edit(change: Partial<TrackerRange>): void {
    this.chosen.set('custom');
    const draft = { ...this.draft(), ...change };
    this.draft.set(draft);
    const from = draft.from ?? this.from();
    const to = draft.to ?? this.to();
    if (from !== null && to !== null && from.length > 0 && to.length > 0 && from <= to) {
      this.draft.set({});
      this.rangeChange.emit({ from, to });
    }
  }
}
