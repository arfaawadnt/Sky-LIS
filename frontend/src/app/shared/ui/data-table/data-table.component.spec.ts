import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CellTemplateDirective, RowActionsTemplateDirective } from './cell-template.directive';
import { DataTableComponent } from './data-table.component';
import type { TableColumn, TableSort } from './data-table.types';

interface Row {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly qty: number;
}

@Component({
  imports: [DataTableComponent, CellTemplateDirective, RowActionsTemplateDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-data-table [columns]="columns" [rows]="rows()" [serverSort]="serverSort()" (sortChange)="sorts.push($event)" (rowActivate)="activated.push($event)" (rowClick)="clicked.push($event)">
      <ng-template appCell="name" [appCellOf]="rows()" let-row><b class="n">{{ row.name }}</b></ng-template>
      <ng-template appRowActions [appRowActionsOf]="rows()" let-row><button type="button" class="act" (click)="acted.push(row)">act</button></ng-template>
    </app-data-table>
  `,
})
class HostComponent {
  readonly columns: readonly TableColumn<Row>[] = [
    { key: 'code', header: 'x.code', sortable: true },
    { key: 'name', header: 'x.name', sortable: true },
    { key: 'qty', header: 'x.qty', sortable: true, align: 'end' },
  ];

  readonly rows = signal<readonly Row[]>([
    { id: '1', code: 'B', name: 'Beta', qty: 2 },
    { id: '2', code: 'A', name: 'Alpha', qty: 10 },
    { id: '3', code: 'C', name: 'Gamma', qty: 1 },
  ]);

  readonly serverSort = signal(false);
  readonly sorts: (TableSort | null)[] = [];
  readonly activated: Row[] = [];
  readonly clicked: Row[] = [];
  readonly acted: Row[] = [];
}

describe('DataTableComponent (shared/ui)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  function names(host: HTMLElement): string[] {
    return [...host.querySelectorAll('tbody tr[data-row] .n')].map((cell) => cell.textContent?.trim() ?? '');
  }

  function header(host: HTMLElement, text: string): HTMLButtonElement {
    const button = [...host.querySelectorAll<HTMLButtonElement>('thead .sort-btn')].find((candidate) => candidate.textContent?.includes(text));
    if (!button) {
      throw new Error(`no sortable header ${text}`);
    }

    return button;
  }

  it('renders the rows in the given order with the projected cell template', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(names(host)).toEqual(['Beta', 'Alpha', 'Gamma']);
    expect(host.querySelectorAll('thead th').length).toBe(4); // three columns + actions
    expect(host.querySelectorAll('tbody .act').length).toBe(3);
  });

  it('keeps the row actions on one line in a column as wide as its content (LIS-CR-007 C-06)', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    try {
      const cell = host.querySelector<HTMLElement>('tbody td.actions')!;
      const line = cell.querySelector<HTMLElement>('.row-actions')!;
      expect(line.querySelectorAll('.act').length).toBe(1);
      expect(getComputedStyle(line).flexWrap).toBe('nowrap');
      expect(getComputedStyle(cell).whiteSpace).toBe('nowrap');
    } finally {
      host.remove();
    }
  });

  it('sorts client-side on a header click: ascending, descending, then back to the server order', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    header(host, 'x.code').click();
    await fixture.whenStable();
    expect(names(host)).toEqual(['Alpha', 'Beta', 'Gamma']);
    expect(host.querySelector('thead th[aria-sort="ascending"]')).not.toBeNull();

    header(host, 'x.code').click();
    await fixture.whenStable();
    expect(names(host)).toEqual(['Gamma', 'Beta', 'Alpha']);

    header(host, 'x.code').click();
    await fixture.whenStable();
    expect(names(host)).toEqual(['Beta', 'Alpha', 'Gamma']);
    expect(fixture.componentInstance.sorts).toEqual([{ key: 'code', direction: 'asc' }, { key: 'code', direction: 'desc' }, null]);
  });

  it('sorts numbers numerically', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    header(host, 'x.qty').click();
    await fixture.whenStable();
    expect(names(host)).toEqual(['Gamma', 'Beta', 'Alpha']);
  });

  it('leaves the order to the server when serverSort is set, but still reports the sort', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.serverSort.set(true);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    header(host, 'x.code').click();
    await fixture.whenStable();
    expect(names(host)).toEqual(['Beta', 'Alpha', 'Gamma']);
    expect(fixture.componentInstance.sorts).toEqual([{ key: 'code', direction: 'asc' }]);
  });

  it('moves the focused row with the arrow keys and activates it with Enter', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const rows = [...host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]')];

    expect(rows[0]?.tabIndex).toBe(0);
    expect(rows[1]?.tabIndex).toBe(-1);

    rows[0]?.focus();
    rows[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    await fixture.whenStable();
    expect(document.activeElement).toBe(rows[1]);
    expect(rows[1]?.tabIndex).toBe(0);
    expect(rows[0]?.tabIndex).toBe(-1);

    rows[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(fixture.componentInstance.activated.map((row) => row.id)).toEqual(['2']);

    rows[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    await fixture.whenStable();
    expect(document.activeElement).toBe(rows[2]);

    rows[2]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    await fixture.whenStable();
    expect(document.activeElement).toBe(rows[0]);
  });

  it('activates a row on click, but not when the click lands on a row action', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const rows = [...host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]')];

    rows[2]?.click();
    expect(fixture.componentInstance.clicked.map((row) => row.id)).toEqual(['3']);
    expect(fixture.componentInstance.activated.map((row) => row.id)).toEqual(['3']);

    rows[0]?.querySelector<HTMLButtonElement>('.act')?.click();
    expect(fixture.componentInstance.acted.map((row) => row.id)).toEqual(['1']);
    expect(fixture.componentInstance.activated.map((row) => row.id)).toEqual(['3']);
  });

  it('shows the empty state when there are no rows', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.rows.set([]);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(0);
    expect(host.querySelector('app-empty-state')).not.toBeNull();
  });
});
