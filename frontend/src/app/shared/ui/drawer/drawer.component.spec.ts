import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { OverlayStackService } from '../overlay/overlay-stack.service';
import { DrawerComponent } from './drawer.component';

@Component({
  imports: [DrawerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" id="opener" (click)="open.set(true)">open</button>
    <app-drawer [(open)]="open" [dismissible]="dismissible()" title="Edit" (closed)="closedCount = closedCount + 1">
      <input id="first" type="text" />
      <button type="button" id="ok" drawerFooter>ok</button>
    </app-drawer>
  `,
})
class HostComponent {
  readonly open = signal(false);
  readonly dismissible = signal(true);
  closedCount = 0;
}

describe('DrawerComponent (shared/ui)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  it('opens with the focus inside, closes on Escape and gives the focus back to the opener', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const opener = host.querySelector<HTMLButtonElement>('#opener');
    expect(host.querySelector('[role="dialog"]')).toBeNull();

    opener?.focus();
    opener?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(host.querySelector('#first')).toBe(document.activeElement);
    expect(host.querySelector('.foot #ok')).not.toBeNull();
    expect(document.body.classList.contains('app-scroll-lock')).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(fixture.componentInstance.open()).toBe(false);
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(fixture.componentInstance.closedCount).toBe(1);
    expect(document.activeElement).toBe(opener);
    expect(document.body.classList.contains('app-scroll-lock')).toBe(false);
    expect(TestBed.inject(OverlayStackService).count()).toBe(0);

    host.remove();
  });

  it('ignores Escape when it is not dismissible, and closes through the scrim otherwise', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.dismissible.set(false);
    fixture.componentInstance.open.set(true);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(fixture.componentInstance.open()).toBe(true);

    fixture.componentInstance.dismissible.set(true);
    await fixture.whenStable();
    host.querySelector<HTMLButtonElement>('.scrim')?.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.open()).toBe(false);
    expect(fixture.componentInstance.closedCount).toBe(1);
  });
});
