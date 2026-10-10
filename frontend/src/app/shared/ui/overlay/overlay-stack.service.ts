import { Injectable, signal } from '@angular/core';

/**
 * The open overlays (drawers, dialogs) in opening order, so that Esc closes only the topmost one and body scroll is
 * locked while anything is open. Each overlay registers a token when it opens and releases it when it closes.
 */
@Injectable({ providedIn: 'root' })
export class OverlayStackService {
  private readonly stack = signal<readonly symbol[]>([]);

  readonly count = () => this.stack().length;

  push(token: symbol): void {
    this.stack.update((current) => (current.includes(token) ? current : [...current, token]));
    this.syncScrollLock();
  }

  remove(token: symbol): void {
    this.stack.update((current) => current.filter((entry) => entry !== token));
    this.syncScrollLock();
  }

  isTop(token: symbol): boolean {
    const current = this.stack();
    return current.length > 0 && current[current.length - 1] === token;
  }

  private syncScrollLock(): void {
    if (typeof document === 'undefined') {
      return;
    }

    document.body.classList.toggle('app-scroll-lock', this.stack().length > 0);
  }
}
