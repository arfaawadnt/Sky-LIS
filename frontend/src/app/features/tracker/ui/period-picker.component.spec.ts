import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { PeriodPickerComponent, type TrackerRange, shiftDay } from './period-picker.component';

describe('PeriodPickerComponent (tracker period: a day or a from / to range)', () => {
  let fixture: ComponentFixture<PeriodPickerComponent>;
  let host: HTMLElement;
  let days: (string | null)[];
  let ranges: TrackerRange[];

  async function show(inputs: { day?: string | null; from?: string | null; to?: string | null } = {}): Promise<void> {
    await TestBed.configureTestingModule({ imports: [PeriodPickerComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    fixture = TestBed.createComponent(PeriodPickerComponent);
    fixture.componentRef.setInput('today', '2026-10-22');
    days = [];
    ranges = [];
    fixture.componentInstance.dayChange.subscribe((day) => days.push(day));
    fixture.componentInstance.rangeChange.subscribe((range) => ranges.push(range));
    host = fixture.nativeElement as HTMLElement;
    filter(inputs);
  }

  /** The filter as the facade would hold it after a change. */
  function filter(inputs: { day?: string | null; from?: string | null; to?: string | null }): void {
    fixture.componentRef.setInput('day', inputs.day ?? null);
    fixture.componentRef.setInput('from', inputs.from ?? null);
    fixture.componentRef.setInput('to', inputs.to ?? null);
    fixture.detectChanges();
  }

  function pick(mode: string): void {
    const select = host.querySelector<HTMLSelectElement>('select')!;
    select.value = mode;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function type(selector: string, value: string, index = 0): void {
    const input = host.querySelectorAll<HTMLInputElement>(selector)[index]!;
    input.value = value;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('moves a day by calendar arithmetic', () => {
    expect([shiftDay('2026-10-22', -1), shiftDay('2026-03-01', -1), shiftDay('2026-12-31', 1)]).toEqual(['2026-10-21', '2026-02-28', '2027-01-01']);
  });

  it('derives the preset from the filter: today, yesterday, a day, or a range', async () => {
    await show();
    expect(host.querySelector<HTMLSelectElement>('select')?.value).toBe('today');
    expect(host.querySelectorAll('input').length).toBe(0);

    filter({ day: '2026-10-21' });
    expect(host.querySelector<HTMLSelectElement>('select')?.value).toBe('yesterday');

    filter({ day: '2026-10-01' });
    expect(host.querySelector<HTMLSelectElement>('select')?.value).toBe('day');
    expect(host.querySelector<HTMLInputElement>('input[type="date"]')?.value).toBe('2026-10-01');

    filter({ from: '2026-10-15T08:00', to: '2026-10-21T17:30' });
    expect(host.querySelector<HTMLSelectElement>('select')?.value).toBe('custom');
    expect([...host.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]')].map((input) => input.value)).toEqual(['2026-10-15T08:00', '2026-10-21T17:30']);
  });

  it('presets emit a day (null = today) or a range; a custom range is sent once both ends are filled and ordered', async () => {
    await show();
    pick('yesterday');
    pick('today');
    pick('last7');
    expect(days).toEqual(['2026-10-21', null]);
    expect(ranges).toEqual([{ from: '2026-10-16T00:00', to: '2026-10-22T23:59' }]);

    pick('custom');
    expect(ranges.at(-1)).toEqual({ from: '2026-10-22T00:00', to: '2026-10-22T23:59' });
    type('input[type="datetime-local"]', '2026-10-23T08:00'); // after "to": kept as a draft, nothing sent
    expect(ranges.length).toBe(2);
    type('input[type="datetime-local"]', '2026-10-24T18:00', 1);
    expect(ranges.at(-1)).toEqual({ from: '2026-10-23T08:00', to: '2026-10-24T18:00' });

    pick('day');
    expect(days.at(-1)).toBe('2026-10-22');
    type('input[type="date"]', '2026-10-22');
    expect(days.at(-1)).toBeNull(); // the laboratory's today is the default day, not a chosen one
  });
});
