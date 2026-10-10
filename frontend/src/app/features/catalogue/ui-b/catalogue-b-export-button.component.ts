import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';

import { CATALOGUE_B_PERMISSIONS } from '../../../core/api/catalogue-b.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';

/**
 * `<app-catalogue-b-export-button pageActions [export]="exportCsv" />`: the "Export CSV" action of a Catalogue B
 * register, shown only to holders of `catalogue.export` (the server decides), with a busy state and toasts.
 */
@Component({
  selector: 'app-catalogue-b-export-button',
  imports: [CanDirective, IconComponent],
  template: `
    <button type="button" class="btn" *appCan="permission" [disabled]="pending()" (click)="run()">
      <app-icon name="arrow-down" [size]="15" />{{ pending() ? i18n.t('catalogue-b.common.exporting') : i18n.t('catalogue-b.common.export') }}
    </button>
  `,
  styles: `:host { display: contents; }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogueBExportButtonComponent {
  protected readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);

  /** Downloads the register (the module's `exportCsv` call). */
  readonly export = input.required<() => Promise<void>>();

  protected readonly permission = CATALOGUE_B_PERMISSIONS.export;
  protected readonly pending = signal(false);

  protected async run(): Promise<void> {
    if (this.pending()) {
      return;
    }

    this.pending.set(true);
    try {
      await this.export()();
      this.toast.success(this.i18n.t('catalogue-b.common.exported'));
    } catch (error: unknown) {
      this.toast.error(error instanceof ProblemError ? problemMessage(this.i18n, 'catalogue-b', error.problem) : this.i18n.t('catalogue-b.common.exportError'));
    } finally {
      this.pending.set(false);
    }
  }
}
