import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { RowActionsTemplateDirective } from './cell-template.directive';
import { DataTableComponent } from './data-table.component';
import type { TableColumn } from './data-table.types';
import { RowMenuComponent } from './row-menu.component';

interface Row {
  readonly id: string;
  readonly name: string;
}

@Component({
  imports: [DataTableComponent, RowActionsTemplateDirective, RowMenuComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-data-table [columns]="columns" [rows]="rows" (rowActivate)="activated.push($event.id)">
      <ng-template appRowActions [appRowActionsOf]="rows" let-row>
        <button type="button" class="row-btn inline" (click)="ran.push('edit:' + row.id)">e</button>
        <app-row-menu>
          @if (withItems()) {
            <button type="button" class="one" (click)="ran.push('one:' + row.id)">One</button>
            <button type="button" class="off" disabled>Off</button>
            <button type="button" class="two" (click)="ran.push('two:' + row.id)">Two</button>
          }
        </app-row-menu>
      </ng-template>
    </app-data-table>
    <button type="button" class="outside">outside</button>
  `,
})
class HostComponent {
  readonly columns: readonly TableColumn<Row>[] = [{ key: 'name', header: 'x.name' }];
  readonly rows: readonly Row[] = [{ id: 'r1', name: 'First' }];
  readonly withItems = signal(true);
  readonly ran: string[] = [];
  readonly activated: string[] = [];
}

describe('RowMenuComponent (shared/ui, LIS-CR-007 C-06)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => (fixture.nativeElement as HTMLElement).remove());

  const trigger = (): HTMLButtonElement => host.querySelector<HTMLButtonElement>('app-row-menu > button')!;
  const panel = (): HTMLElement => host.querySelector<HTMLElement>('.row-menu-panel')!;
  const item = (name: string): HTMLButtonElement => host.querySelector<HTMLButtonElement>(`.row-menu-panel .${name}`)!;

  function key(target: HTMLElement, name: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
  }

  it('renders the actions in one line with a named More trigger, closed', () => {
    const actions = host.querySelector('td.actions .row-actions')!;
    expect([...actions.children].map((child) => child.tagName.toLowerCase())).toEqual(['button', 'app-row-menu']);
    expect(trigger().getAttribute('aria-label')).toBe('shared.table.moreActions');
    expect(trigger().getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(panel().hidden).toBe(true);
  });

  it('opens on a click with the focus on the first item, runs the item, closes and gives the focus back', async () => {
    trigger().click();
    await fixture.whenStable();
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(panel().hidden).toBe(false);
    expect(item('one').getAttribute('role')).toBe('menuitem');
    expect(document.activeElement).toBe(item('one'));

    item('two').click();
    await fixture.whenStable();
    expect(fixture.componentInstance.ran).toEqual(['two:r1']);
    expect(fixture.componentInstance.activated).toEqual([]);   // a menu item never activates the row
    expect(panel().hidden).toBe(true);
    expect(document.activeElement).toBe(trigger());
  });

  it('is driven by the keyboard: arrows skip the disabled item and wrap, Escape closes back to the trigger', async () => {
    trigger().focus();
    key(trigger(), 'ArrowDown');
    await fixture.whenStable();
    expect(document.activeElement).toBe(item('one'));

    key(item('one'), 'ArrowDown');
    expect(document.activeElement).toBe(item('two'));
    key(item('two'), 'ArrowDown');
    expect(document.activeElement).toBe(item('one'));
    key(item('one'), 'ArrowUp');
    expect(document.activeElement).toBe(item('two'));
    key(item('two'), 'Home');
    expect(document.activeElement).toBe(item('one'));

    const escape = key(item('one'), 'Escape');
    await fixture.whenStable();
    expect(escape.defaultPrevented).toBe(true);
    expect(panel().hidden).toBe(true);
    expect(document.activeElement).toBe(trigger());

    key(trigger(), 'ArrowUp');
    await fixture.whenStable();
    expect(document.activeElement).toBe(item('two'));
  });

  it('closes on a click outside without running anything', async () => {
    trigger().click();
    await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('.outside')!.click();
    await fixture.whenStable();
    expect(panel().hidden).toBe(true);
    expect(fixture.componentInstance.ran).toEqual([]);
  });

  it('hides its trigger when the page leaves it no item', async () => {
    expect(host.querySelector('app-row-menu')!.hasAttribute('hidden')).toBe(false);
    fixture.componentInstance.withItems.set(false);
    await fixture.whenStable();
    expect(host.querySelector('app-row-menu')!.hasAttribute('hidden')).toBe(true);
  });
});
