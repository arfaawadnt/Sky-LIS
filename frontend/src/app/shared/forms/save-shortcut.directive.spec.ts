import { ChangeDetectionStrategy, Component, DOCUMENT, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { SaveShortcutDirective, isSaveShortcut, provideSaveShortcutGuard } from './save-shortcut.directive';

@Component({
  imports: [SaveShortcutDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<form appSaveShortcut (appSaveShortcut)="saves = saves + 1"><input /></form>`,
})
class HostComponent {
  saves = 0;
}

function press(init: KeyboardEventInit): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
  document.dispatchEvent(event);
  return event;
}

describe('save shortcut (F10, Ctrl+S)', () => {
  it('recognises F10 and Ctrl/Cmd+S, nothing else', () => {
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 'F10' }))).toBe(true);
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 's', ctrlKey: true }))).toBe(true);
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 'S', metaKey: true }))).toBe(true);
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 'F10', shiftKey: true }))).toBe(false);   // Shift+F10 = context menu
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 'F10', ctrlKey: true }))).toBe(false);
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 's' }))).toBe(false);
    expect(isSaveShortcut(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, altKey: true }))).toBe(false);
  });

  it('saves the form on F10 and on Ctrl+S', async () => {
    TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] });
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();

    expect(press({ key: 'F10' }).defaultPrevented).toBe(true);
    expect(press({ key: 's', ctrlKey: true }).defaultPrevented).toBe(true);
    expect(fixture.componentInstance.saves).toBe(2);
    fixture.destroy();
  });

  it('keeps the browser action away even where no form listens', () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideSaveShortcutGuard()] });
    TestBed.inject(DOCUMENT);   // creates the injector, which runs the environment initializer

    expect(press({ key: 's', ctrlKey: true }).defaultPrevented).toBe(true);
    expect(press({ key: 'F10' }).defaultPrevented).toBe(true);
    expect(press({ key: 'F9' }).defaultPrevented).toBe(false);
  });
});
