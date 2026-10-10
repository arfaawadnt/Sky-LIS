import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info' | 'warn';

export interface Toast {
  readonly id: number;
  readonly kind: ToastKind;
  /** Already translated. */
  readonly text: string;
  readonly title: string | null;
}

export interface ToastOptions {
  readonly title?: string;
  /** Milliseconds before auto-dismiss; `0` keeps the toast until dismissed. */
  readonly duration?: number;
}

let nextToastId = 0;

/**
 * `toast.success(i18n.t('shared.toast.saved'))` after a mutation, `toast.error(problemMessage(…))` when one is
 * refused. Toasts are rendered by `ToastHostComponent` (in the application root) with `aria-live`; errors stay
 * longer than successes.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly state = signal<readonly Toast[]>([]);
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  readonly toasts = this.state.asReadonly();

  success(text: string, options?: ToastOptions): number {
    return this.show('success', text, options);
  }

  error(text: string, options?: ToastOptions): number {
    return this.show('error', text, { duration: 7000, ...options });
  }

  info(text: string, options?: ToastOptions): number {
    return this.show('info', text, options);
  }

  warn(text: string, options?: ToastOptions): number {
    return this.show('warn', text, { duration: 6000, ...options });
  }

  show(kind: ToastKind, text: string, options: ToastOptions = {}): number {
    const toast: Toast = { id: ++nextToastId, kind, text, title: options.title ?? null };
    this.state.update((toasts) => [...toasts.slice(-4), toast]);
    const duration = options.duration ?? 4000;
    if (duration > 0) {
      this.timers.set(
        toast.id,
        setTimeout(() => this.dismiss(toast.id), duration),
      );
    }

    return toast.id;
  }

  dismiss(id: number): void {
    const timer = this.timers.get(id);
    if (timer) {
      clearTimeout(timer);
      this.timers.delete(id);
    }

    this.state.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  clear(): void {
    for (const id of [...this.timers.keys()]) {
      this.dismiss(id);
    }

    this.state.set([]);
  }
}
