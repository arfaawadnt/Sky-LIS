import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { LaboratoryBrandService } from '../../../core/brand/laboratory-brand.service';
import { PrintService } from '../../print/print.service';
import { ToastService } from '../toast/toast.service';
import { CellTemplateDirective } from './cell-template.directive';
import { DataTableComponent } from './data-table.component';
import type { TableColumn } from './data-table.types';
import { TABLE_PREFERENCES, type TablePreferences } from './table-preferences';
import { TablePrintComponent } from './table-print.component';

interface Row {
  readonly id: string;
  readonly code: string;
  readonly branch: string;
  readonly amount: number;
}

@Component({
  imports: [DataTableComponent, CellTemplateDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-data-table
      [columns]="columns"
      [rows]="rows"
      [listKey]="listKey()"
      selection="multi"
      [(selected)]="selected"
      [exportable]="true"
      caption="Visits"
      printSubtitle="From 2026-10-01"
    >
      <ng-template appCell="branch" [appCellOf]="rows" let-row><i class="b">{{ row.branch }}</i></ng-template>
    </app-data-table>
  `,
})
class HostComponent {
  readonly table = viewChild.required(DataTableComponent<Row>);
  readonly listKey = signal<string | null>('visits');
  readonly selected = signal<readonly string[]>([]);
  readonly columns: readonly TableColumn<Row>[] = [
    { key: 'code', header: 'x.code', optional: true },
    { key: 'branch', header: 'x.branch', optional: true, exportValue: (row) => `Branch ${row.branch}` },
    { key: 'amount', header: 'x.amount', align: 'end', optional: true, exportValue: (row) => ({ value: row.amount, text: row.amount.toFixed(2), numFmt: '#,##0.00' }) },
  ];

  readonly rows: readonly Row[] = [
    { id: '1', code: 'V-1', branch: 'Main', amount: 10 },
    { id: '2', code: 'V-2', branch: 'North', amount: 25.5 },
    { id: '3', code: 'V-3', branch: 'South', amount: 7 },
  ];
}

/** A preference store kept in a signal, as the profile is. */
class FakePreferences implements TablePreferences {
  readonly lists = signal<Readonly<Record<string, readonly string[]>>>({});
  readonly saved: [string, readonly string[]][] = [];
  refuse = false;

  hidden(listKey: string): readonly string[] {
    return this.lists()[listKey] ?? [];
  }

  async save(listKey: string, hidden: readonly string[]): Promise<void> {
    this.saved.push([listKey, hidden]);
    if (this.refuse) {
      throw new Error('refused');
    }

    this.lists.update((lists) => ({ ...lists, [listKey]: hidden }));
    await Promise.resolve();
  }
}

describe('DataTableComponent column chooser and export (LIS-CR-014 T2 / T4)', () => {
  let preferences: FakePreferences;

  beforeEach(async () => {
    preferences = new FakePreferences();
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: TABLE_PREFERENCES, useValue: preferences },
        { provide: LaboratoryBrandService, useValue: { name: () => 'Demo Lab' } },
      ],
    }).compileComponents();
  });

  function headers(host: HTMLElement): string[] {
    return [...host.querySelectorAll('thead th:not(.sel):not(.chooser)')].map((cell) => cell.textContent?.trim() ?? '');
  }

  async function openChooser(host: HTMLElement, fixture: { whenStable(): Promise<unknown> }): Promise<HTMLElement> {
    host.querySelector<HTMLButtonElement>('app-column-chooser button')!.click();
    await fixture.whenStable();
    return host.querySelector<HTMLElement>('.column-chooser-panel')!;
  }

  it('offers the optional columns but the first, hides a column everywhere and saves the choice under the list key', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    try {
      expect(headers(host)).toEqual(['x.code', 'x.branch', 'x.amount']);
      const panel = await openChooser(host, fixture);
      expect(panel.hidden).toBe(false);
      const labels = [...panel.querySelectorAll('.cc-item')].map((item) => item.textContent?.trim());
      expect(labels).toEqual(['x.branch', 'x.amount']); // the first column always shows

      panel.querySelector<HTMLInputElement>('.cc-item input')!.click();
      await fixture.whenStable();

      expect(preferences.saved).toEqual([['visits', ['branch']]]);
      expect(headers(host)).toEqual(['x.code', 'x.amount']);
      expect(host.querySelectorAll('tbody .b').length).toBe(0); // cells are not rendered either
      expect(host.querySelector('tbody tr[data-row]')!.querySelectorAll('td').length).toBe(4); // select + code + amount + chooser gap

      panel.querySelector<HTMLButtonElement>('.cc-reset')!.click();
      await fixture.whenStable();
      expect(preferences.saved.at(-1)).toEqual(['visits', []]);
      expect(headers(host)).toEqual(['x.code', 'x.branch', 'x.amount']);
    } finally {
      (fixture.nativeElement as HTMLElement).remove();
    }
  });

  it('reads the saved choice of the user and keeps it per list key', async () => {
    preferences.lists.set({ visits: ['amount'], tracker: ['branch'] });
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(headers(host)).toEqual(['x.code', 'x.branch']);
    fixture.componentInstance.listKey.set('tracker');
    await fixture.whenStable();
    expect(headers(host)).toEqual(['x.code', 'x.amount']);
  });

  it('reports a refused save and keeps the columns of the saved view', async () => {
    preferences.refuse = true;
    const error = vi.spyOn(TestBed.inject(ToastService), 'error');
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    try {
      const panel = await openChooser(host, fixture);
      panel.querySelector<HTMLInputElement>('.cc-item input')!.click();
      await fixture.whenStable();
      await Promise.resolve();
      expect(preferences.saved).toEqual([['visits', ['branch']]]);
      expect(error).toHaveBeenCalledWith('shared.table.columnsSaveFailed');
      expect(headers(host)).toEqual(['x.code', 'x.branch', 'x.amount']);
    } finally {
      (fixture.nativeElement as HTMLElement).remove();
    }
  });

  it('keeps the choice on the page only without a list key', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.listKey.set(null);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    try {
      const panel = await openChooser(host, fixture);
      panel.querySelectorAll<HTMLInputElement>('.cc-item input')[1]!.click();
      await fixture.whenStable();
      expect(headers(host)).toEqual(['x.code', 'x.branch']);
      expect(preferences.saved).toEqual([]);
    } finally {
      (fixture.nativeElement as HTMLElement).remove();
    }
  });

  it('closes the chooser on Escape (back to the trigger) and on a click outside', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    try {
      const trigger = host.querySelector<HTMLButtonElement>('app-column-chooser button')!;
      let panel = await openChooser(host, fixture);
      expect(trigger.getAttribute('aria-expanded')).toBe('true');
      panel.querySelector('input')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await fixture.whenStable();
      expect(panel.hidden).toBe(true);
      expect(document.activeElement).toBe(trigger);

      panel = await openChooser(host, fixture);
      document.body.click();
      await fixture.whenStable();
      expect(panel.hidden).toBe(true);

      trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
      await fixture.whenStable();
      expect(panel.hidden).toBe(false);
    } finally {
      (fixture.nativeElement as HTMLElement).remove();
    }
  });

  it('exports the visible columns of every row shown, or of the selected rows only', async () => {
    preferences.lists.set({ visits: ['code'] }); // the first column cannot be hidden
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const table = fixture.componentInstance.table();

    const all = table.exportDocument();
    expect(all.title).toBe('Visits');
    expect(all.subtitle).toBe('From 2026-10-01');
    expect(all.columns.map((column) => column.header)).toEqual(['x.code', 'x.branch', 'x.amount']);
    expect(all.rows.length).toBe(3);
    expect(all.rows[1]).toEqual(['V-2', 'Branch North', { value: 25.5, text: '25.50', numFmt: '#,##0.00' }]);

    preferences.lists.set({ visits: ['branch'] });
    fixture.componentInstance.selected.set(['3', '1']);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.tools-note')?.textContent).toContain('shared.table.exportSelected');

    const chosen = table.exportDocument();
    expect(chosen.columns.map((column) => column.key)).toEqual(['code', 'amount']);
    expect(chosen.rows.map((row) => row[0])).toEqual(['V-1', 'V-3']);
  });

  it('prints an A4 landscape document of the chosen rows through the print service', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const printer = TestBed.inject(PrintService);
    const print = vi.spyOn(printer, 'print').mockResolvedValue(undefined);
    fixture.componentInstance.selected.set(['2']);
    await fixture.whenStable();

    await fixture.componentInstance.table().print();

    expect(print).toHaveBeenCalledTimes(1);
    const request = print.mock.calls[0]![0];
    expect(request).toEqual(expect.objectContaining({ title: 'Visits', size: 'A4', landscape: true }));
    expect(request.html).toContain('counter(pages)');
    expect(request.html).toContain('V-2');
    expect(request.html).toContain('Branch North');
    expect(request.html).not.toContain('V-1');
    expect(request.html).toContain('From 2026-10-01');
    expect(request.html).toContain('Demo Lab');
  });

  it('renders the print component with the headers, the cells and the row count', () => {
    const html = TestBed.inject(PrintService).render({
      type: TablePrintComponent,
      inputs: {
        table: { title: 'Visits', subtitle: null, columns: [{ key: 'a', header: 'Amount', align: 'end' }], rows: [[{ value: 3, text: '3.00' }], [null]] },
        laboratory: 'Demo Lab',
        printedAt: '25 Oct 2026, 10:00',
        labels: { printedAt: 'Printed at', rows: '2 rows', page: 'Page' },
        texts: { lang: 'en', yes: 'Yes', no: 'No' },
      },
    });

    expect(html).toContain('Demo Lab');
    expect(html).toContain('Amount');
    expect(html).toContain('3.00');
    expect(html).toContain('—');
    expect(html).toContain('2 rows');
    expect(html).toContain('al-end');
  });
});
