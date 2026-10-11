import { DOCUMENT, DestroyRef, Directive, ElementRef, type EnvironmentProviders, inject, output, provideEnvironmentInitializer } from '@angular/core';

/** The hosts that currently carry the shortcut, in creation order; the last one is the topmost (an open drawer's form). */
const hosts: HTMLElement[] = [];

/**
 * The save keys: **F10** (the save key of the legacy LDM forms the laboratories know, chosen by the business owner on
 * 2026-10-12) and Ctrl+S / Cmd+S (kept for those used to it). No other modifier.
 */
export function isSaveShortcut(event: KeyboardEvent): boolean {
  if (event.altKey || event.shiftKey) {
    return false;
  }

  if (event.key === 'F10') {
    return !event.ctrlKey && !event.metaKey;
  }

  return (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's';
}

/**
 * Keeps the browser's own action for the save keys out of the whole application: Ctrl+S opened "Save page as…" wherever
 * no form listened (the clerk thought the visit was saved), and F10 moves the focus to the browser menu. Registered once
 * in `app.config.ts`; the forms still get the keys through `appSaveShortcut`.
 */
export function provideSaveShortcutGuard(): EnvironmentProviders {
  return provideEnvironmentInitializer(() => {
    inject(DOCUMENT).addEventListener(
      'keydown',
      (event) => {
        if (isSaveShortcut(event)) {
          event.preventDefault();
        }
      },
      { capture: true },
    );
  });
}

/**
 * `<form appSaveShortcut (appSaveShortcut)="save()">`: F10 (or Ctrl+S / Cmd+S) emits the save (SRS keyboard rules). The
 * shortcut is handled once, by the innermost host that contains the focused element; when focus is nowhere in
 * particular (body), the most recently opened host (the open drawer) takes it.
 */
@Directive({ selector: '[appSaveShortcut]', host: { '(document:keydown)': 'onKeydown($event)' } })
export class SaveShortcutDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly appSaveShortcut = output<KeyboardEvent>();

  constructor() {
    hosts.push(this.host);
    inject(DestroyRef).onDestroy(() => {
      const index = hosts.indexOf(this.host);
      if (index >= 0) {
        hosts.splice(index, 1);
      }
    });
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (!isSaveShortcut(event)) {
      return;
    }

    const active = this.host.ownerDocument.activeElement;
    const owners = hosts.filter((host) => active !== null && host.contains(active));
    const owner = owners.length > 0 ? owners[owners.length - 1] : hosts[hosts.length - 1];
    if (owner !== this.host) {
      return;
    }

    event.preventDefault();
    this.appSaveShortcut.emit(event);
  }
}
