import { ChangeDetectionStrategy, Component, inject, input, resource } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RegistrationApi } from '../data/registration.api';
import { RegistrationPrinter } from '../state/registration-printer';
import { JobOrderDocumentComponent } from '../ui/registration-documents';

/**
 * The job order of a visit on screen (LDM §4.5 "Sampling details"; linked from CP-06 edit, CP-10 documents and the
 * Samples pages): the document component rendered in the page with a print button; the data is
 * `GET …/visits/{id}/documents/job-order` (MODULES.md §1 "Documents").
 */
@Component({
  selector: 'app-registration-job-order-page',
  imports: [RouterLink, PageHeaderComponent, IconComponent, JobOrderDocumentComponent],
  template: `
    <app-page-header [title]="i18n.t('registration.jobOrder.title')" [subtitle]="document.hasValue() ? document.value().accession : ''">
      <a pageActions class="btn ghost" routerLink="/registration/visits">{{ i18n.t('registration.jobOrder.backToVisits') }}</a>
      @if (document.hasValue()) {
        <a pageActions class="btn" [routerLink]="['/registration/visits', visitId()]"><app-icon name="edit" [size]="15" />{{ i18n.t('registration.jobOrder.openVisit') }}</a>
      }
      <button pageActions type="button" class="btn primary" [disabled]="!document.hasValue()" (click)="print()"><app-icon name="printer" [size]="15" />{{ i18n.t('registration.jobOrder.print') }}</button>
    </app-page-header>

    @if (document.isLoading()) {
      <p class="muted small" role="status">{{ i18n.t('registration.loading') }}</p>
    } @else if (document.error()) {
      <p class="problem" role="alert">{{ i18n.t('registration.visitLoadError') }}</p>
    } @else if (document.hasValue()) {
      <div class="sheet">
        <app-reg-job-order-document [document]="document.value()" />
      </div>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--sp-4);
    }

    .sheet {
      max-width: 210mm;
      padding: var(--sp-4);
      background: #fff;
      color: #000;
      border: 1px solid var(--line);
      border-radius: var(--r-md, 8px);
      overflow-x: auto;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobOrderPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(RegistrationApi);
  private readonly printer = inject(RegistrationPrinter);
  private readonly toast = inject(ToastService);

  /** Route parameter. */
  readonly visitId = input.required<string>();

  protected readonly document = resource({
    params: () => this.visitId(),
    loader: ({ params, abortSignal }) => this.api.jobOrder(params, abortSignal),
  });

  constructor() {
    void this.i18n.load('registration');
  }

  protected async print(): Promise<void> {
    await this.printer.jobOrder(this.visitId()).catch(() => this.toast.error(this.i18n.t('registration.printError')));
  }
}
