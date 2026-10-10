import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';

import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/**
 * The "Export CSV" toolbar button of every catalogue-A register: runs the page's exporter (server export under
 * `catalogue.export`, then the CSV download), shows the busy state and a toast on failure. Gate it with `*appCan`.
 */
@Component({
  selector: 'app-catalogue-export-button',
  imports: [IconComponent],
  template: `
    <button type="button" class="btn ghost" [disabled]="busy()" (click)="run()">
      <app-icon name="arrow-down" [size]="15" />{{ busy() ? i18n.t('catalogue-a.exporting') : i18n.t('catalogue-a.export') }}
    </button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogueExportButtonComponent {
  protected readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);

  readonly exporter = input.required<() => Promise<void>>();

  protected readonly busy = signal(false);

  protected async run(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    try {
      await this.exporter()();
      this.toast.success(this.i18n.t('catalogue-a.exported'));
    } catch (error: unknown) {
      this.toast.error(error instanceof ProblemError ? problemMessage(this.i18n, 'catalogue-a', error.problem) : this.i18n.t('shared.error.generic'));
    } finally {
      this.busy.set(false);
    }
  }
}
