import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';

import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/**
 * The "Export CSV" action of the treasury register (UAT B-10): runs the page's exporter (the rows of the current
 * filter → CSV download), shows the busy state and a toast. The page gates it with `*appCan`.
 */
@Component({
  selector: 'app-treasury-export-button',
  imports: [IconComponent],
  template: `
    <button type="button" class="btn ghost" [disabled]="busy()" (click)="run()">
      <app-icon name="arrow-down" [size]="15" />{{ busy() ? i18n.t('treasury.exporting') : i18n.t('treasury.export') }}
    </button>
  `,
  styles: `:host { display: contents; }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TreasuryExportButtonComponent {
  protected readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);

  readonly exporter = input.required<() => Promise<void>>();

  protected readonly busy = signal(false);

  async run(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    try {
      await this.exporter()();
      this.toast.success(this.i18n.t('treasury.exported'));
    } catch (error: unknown) {
      this.toast.error(error instanceof ProblemError ? problemMessage(this.i18n, 'treasury', error.problem) : this.i18n.t('treasury.exportError'));
    } finally {
      this.busy.set(false);
    }
  }
}
