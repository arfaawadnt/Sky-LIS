import { Injectable, computed, signal } from '@angular/core';

export interface ConfirmOptions {
  /** Already translated. */
  readonly title: string;
  readonly message: string;
  /** Red confirm button for destructive actions. */
  readonly danger?: boolean;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
}

export interface PromptReasonOptions extends ConfirmOptions {
  /** Label of the reason field; defaults to `shared.dialog.reason`. */
  readonly label?: string;
  readonly placeholder?: string;
  /** A reason is mandatory by default (rejections, revocations, cancellations); `false` allows an empty one. */
  readonly required?: boolean;
  readonly maxLength?: number;
}

export type ConfirmKind = 'confirm' | 'prompt';

export interface PendingConfirm {
  readonly id: number;
  readonly kind: ConfirmKind;
  readonly options: PromptReasonOptions;
}

interface Pending extends PendingConfirm {
  readonly settle: (value: boolean | string | null) => void;
}

let nextConfirmId = 0;

/**
 * Promise-based confirmation: `if (!(await confirm.confirm({ title, message, danger: true }))) return;` and
 * `const reason = await confirm.promptReason({ title, message }); if (reason === null) return;`. Requests queue;
 * `DialogHostComponent` (in the shell) renders the first one and calls `resolve`. Without a host the promise
 * stays pending, so tests either render the host or call `resolve` themselves.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly queue = signal<readonly Pending[]>([]);

  /** The request currently shown, if any. */
  readonly current = computed<PendingConfirm | null>(() => this.queue()[0] ?? null);

  confirm(options: ConfirmOptions): Promise<boolean> {
    return this.enqueue('confirm', options).then((value) => value === true);
  }

  /** Resolves to the trimmed reason, or `null` when cancelled (or empty when not required). */
  promptReason(options: PromptReasonOptions): Promise<string | null> {
    return this.enqueue('prompt', options).then((value) => (typeof value === 'string' ? value : null));
  }

  /** Called by the host: `true`/the reason to accept, `false`/`null` to cancel. */
  resolve(value: boolean | string | null): void {
    const current = this.queue()[0];
    if (!current) {
      return;
    }

    this.queue.update((queue) => queue.slice(1));
    current.settle(value);
  }

  cancel(): void {
    this.resolve(null);
  }

  private enqueue(kind: ConfirmKind, options: PromptReasonOptions): Promise<boolean | string | null> {
    return new Promise((settle) => {
      this.queue.update((queue) => [...queue, { id: ++nextConfirmId, kind, options, settle }]);
    });
  }
}
