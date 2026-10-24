import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { ReportUnitView } from '../../../core/api/results.contracts';
import { reportUnit, resultRow } from '../testing/results.fixtures';
import { ResultGridComponent } from './result-grid.component';

function render(unit: ReportUnitView): HTMLElement {
  const fixture = TestBed.createComponent(ResultGridComponent);
  fixture.componentRef.setInput('unit', unit);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Result grid — the expected range before entry (LIS-CR-007 C-08)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  it('greys a provisional range and explains it in the tooltip', () => {
    const host = render(reportUnit([resultRow({ status: 'Ordered', numericValue: null, textValue: null, rangeProvisional: true })]));

    const cell = host.querySelector<HTMLElement>('td.range');
    expect(cell?.textContent?.trim()).toBe('70 – 110');
    expect(cell?.classList.contains('provisional')).toBe(true);
    expect(cell?.getAttribute('title')).toBe('results.grid.rangeProvisional');
  });

  it('shows a stored range as before, also when the server sends no flag', () => {
    const host = render(reportUnit([resultRow(), resultRow({ id: 'res-2', rangeProvisional: false })]));

    const cells = Array.from(host.querySelectorAll<HTMLElement>('td.range'));
    expect(cells).toHaveLength(2);
    for (const cell of cells) {
      expect(cell.classList.contains('provisional')).toBe(false);
      expect(cell.hasAttribute('title')).toBe(false);
    }
  });
});
