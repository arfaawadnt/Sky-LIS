import { DestroyRef, Directive, ElementRef, inject, output } from '@angular/core';

/** The hosts that currently carry the shortcut, in creation order; the last one is the topmost (an open drawer's form). */
const hosts: HTMLElement[] = [];

/**
 * `<form appSaveShortcut (appSaveShortcut)="save()">`: Ctrl+S (or Cmd+S) emits instead of opening the browser's
 * save dialog (SRS keyboard rules). The shortcut is handled once, by the innermost host that contains the focused
 * element; when focus is nowhere in particular (body), the most recently opened host (the open drawer) takes it.
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
    if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== 's') {
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
