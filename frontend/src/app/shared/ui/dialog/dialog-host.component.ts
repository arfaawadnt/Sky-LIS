import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { I18nService } from '../../../core/i18n/i18n.service';
import { ConfirmService } from './confirm.service';
import { DialogComponent } from './dialog.component';

/**
 * Renders the `ConfirmService` queue: one `<app-dialog>` at a time with the message, an optional reason field and
 * the confirm/cancel buttons. Placed once in the application root. Enter confirms a plain confirmation (the
 * confirm button has focus); Ctrl+Enter confirms a reason prompt.
 */
@Component({
  selector: 'app-dialog-host',
  imports: [DialogComponent, ReactiveFormsModule],
  templateUrl: './dialog-host.component.html',
  styleUrl: './dialog-host.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DialogHostComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly confirm = inject(ConfirmService);

  protected readonly reason = new FormControl('', { nonNullable: true });
  protected readonly reasonMissing = signal(false);
  protected readonly isOpen = computed(() => this.confirm.current() !== null);

  protected readonly reasonRequired = computed(() => this.confirm.current()?.options.required ?? true);

  constructor() {
    // every new request starts with an empty reason field
    effect(() => {
      this.confirm.current();
      untracked(() => {
        this.reason.reset('');
        this.reasonMissing.set(false);
      });
    });
  }

  protected accept(): void {
    const current = this.confirm.current();
    if (!current) {
      return;
    }

    if (current.kind === 'confirm') {
      this.confirm.resolve(true);
      return;
    }

    const reason = this.reason.getRawValue().trim();
    if (reason === '' && this.reasonRequired()) {
      this.reasonMissing.set(true);
      return;
    }

    this.confirm.resolve(reason);
  }

  protected cancel(): void {
    this.confirm.resolve(null);
  }

  /** The dialog closed through Esc or the scrim: that is a cancel. */
  protected onDialogOpenChange(open: boolean): void {
    if (!open && this.confirm.current() !== null) {
      this.confirm.resolve(null);
    }
  }

  protected onReasonKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.accept();
    }
  }
}
