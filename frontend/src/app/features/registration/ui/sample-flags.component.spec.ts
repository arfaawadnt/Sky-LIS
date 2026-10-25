import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import type { SampleFlagRow } from '../state/registration.facade';
import { type SampleCollectedAtChange, SampleFlagsComponent, type SampleOutsideChange } from './sample-flags.component';

function row(overrides: Partial<SampleFlagRow> = {}): SampleFlagRow {
  return {
    key: 'g-haem:st-blood:-',
    unitCode: 'H',
    sampleTypeNameEn: 'Whole blood',
    sampleTypeNameAr: 'دم كامل',
    sampleConditionNameEn: null,
    sampleConditionNameAr: null,
    sampleNumber: null,
    outside: false,
    collectedAt: '',
    ...overrides,
  };
}

describe('SampleFlagsComponent (CP-06 collected outside per sample, LIS-CR-008 C-02)', () => {
  let fixture: ComponentFixture<SampleFlagsComponent>;
  let host: HTMLElement;
  let switched: SampleOutsideChange[];
  let timed: SampleCollectedAtChange[];
  let all: boolean[];

  async function show(rows: readonly SampleFlagRow[], readOnly = false): Promise<void> {
    await TestBed.configureTestingModule({ imports: [SampleFlagsComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    fixture = TestBed.createComponent(SampleFlagsComponent);
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('readOnly', readOnly);
    switched = [];
    timed = [];
    all = [];
    fixture.componentInstance.outsideChanged.subscribe((change) => switched.push(change));
    fixture.componentInstance.collectedAtChanged.subscribe((change) => timed.push(change));
    fixture.componentInstance.allChanged.subscribe((value) => all.push(value));
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  it('lists one row per tube with a switch, and "All outside" switches every tube', async () => {
    await show([row(), row({ key: 'g-chem:st-serum:-', unitCode: 'C', sampleTypeNameEn: 'Serum', sampleConditionNameEn: 'Fasting', sampleConditionNameAr: 'صائم' })]);

    const rows = host.querySelectorAll('li.row');
    expect(rows.length).toBe(2);
    expect(rows[1]?.textContent).toContain('Serum');
    expect(rows[1]?.textContent).toContain('Fasting');
    expect(host.querySelectorAll('input[type="checkbox"][role="switch"]').length).toBe(2);
    expect(host.querySelector('input[type="datetime-local"]')).toBeNull();

    const toggle = host.querySelectorAll<HTMLInputElement>('input[role="switch"]')[1]!;
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    expect(switched).toEqual([{ key: 'g-chem:st-serum:-', outside: true }]);

    const allButton = host.querySelector<HTMLButtonElement>('.head button')!;
    expect(allButton.textContent).toContain('registration.sampleFlags.allOutside');
    allButton.click();
    expect(all).toEqual([true]);
  });

  it('offers the collection time once a tube is outside, and "None outside" when every tube is', async () => {
    await show([row({ outside: true }), row({ key: 'k2', outside: true, collectedAt: '2026-10-24T08:30' })]);

    const times = host.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]');
    expect(times.length).toBe(2);
    expect(times[1]?.value).toBe('2026-10-24T08:30');
    expect(times[0]?.max).not.toBe('');
    times[0]!.value = '2026-10-24T07:15';
    times[0]!.dispatchEvent(new Event('change'));
    expect(timed).toEqual([{ key: 'g-haem:st-blood:-', collectedAt: '2026-10-24T07:15' }]);
    expect(host.querySelector('li.row')?.classList).toContain('outside');

    host.querySelector<HTMLButtonElement>('.head button')!.click();
    expect(all).toEqual([false]);
  });

  it('has no "All outside" for a single tube', async () => {
    await show([row()]);

    expect(host.querySelector('.head button')).toBeNull();
  });

  it('shows a saved visit read-only: an "Outside" tag with the time, or collected in the laboratory', async () => {
    await show([row({ outside: true, collectedAt: '2026-10-24T06:30:00Z', sampleNumber: 'SMP-2026-00001' }), row({ key: 'k2', sampleNumber: 'SMP-2026-00002' })], true);

    expect(host.querySelector('input')).toBeNull();
    expect(host.querySelector('.head button')).toBeNull();
    const rows = host.querySelectorAll('li.row');
    expect(rows[0]?.textContent).toContain('registration.sampleFlags.outside');
    expect(rows[0]?.textContent).toContain('registration.sampleFlags.collectedAtValue');
    expect(rows[0]?.textContent).toContain('SMP-2026-00001');
    expect(rows[1]?.textContent).toContain('registration.sampleFlags.inside');
  });
});
