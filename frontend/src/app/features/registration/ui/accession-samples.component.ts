import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { SAMPLES_PERMISSIONS, type SampleListItem } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { dateTime, pick } from '../data/registration-format';

/**
 * The *Samples* card of Accession details (CP-10b): one row per tube — number, unit, type, status, collected and
 * received times —, *Label* (the registration label of the tube) and *Collect* while the sample is still Ordered for
 * `samples.create` (the phlebotomy scan; with auto-collect on, Release 1's default, the button rarely shows).
 */
@Component({
  selector: 'app-reg-accession-samples',
  imports: [BadgeComponent, CanDirective, IconComponent, SectionCardComponent],
  template: `
    <app-section-card [title]="i18n.t('registration.accession.samples.title')" [padded]="false">
      <button cardActions type="button" class="btn sm ghost" (click)="labelAll.emit()" [disabled]="samples().length === 0"><app-icon name="printer" [size]="14" />{{ i18n.t('registration.samples.printAll') }}</button>
      @if (loading()) {
        <p class="muted small pad" role="status">{{ i18n.t('registration.loading') }}</p>
      } @else if (samples().length === 0) {
        <p class="muted small pad">{{ i18n.t('registration.samples.empty') }}</p>
      } @else {
        <div class="scroll">
          <table class="grid">
            <thead>
              <tr>
                <th>{{ i18n.t('registration.column.sample') }}</th>
                <th>{{ i18n.t('registration.column.unit') }}</th>
                <th>{{ i18n.t('registration.column.sampleType') }}</th>
                <th>{{ i18n.t('registration.column.status') }}</th>
                <th>{{ i18n.t('registration.column.collected') }}</th>
                <th>{{ i18n.t('registration.column.received') }}</th>
                <th><span class="visually-hidden">{{ i18n.t('registration.column.actions') }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (sample of samples(); track sample.id) {
                <tr [attr.data-sample]="sample.sampleNumber">
                  <td class="mono">{{ sample.sampleNumber }}</td>
                  <td>{{ sample.unitCode ?? '' }}</td>
                  <td>
                    {{ name(sample.sampleTypeNameEn, sample.sampleTypeNameAr) }}
                    @if (sample.collectedOutside) {
                      <app-badge tone="info">{{ i18n.t('registration.sampleFlags.outside') }}</app-badge>
                    }
                    <span class="small muted block">{{ name(sample.sampleConditionNameEn, sample.sampleConditionNameAr) }}</span>
                  </td>
                  <td>
                    <app-badge [status]="sample.status">{{ i18n.t('shared.status.' + sample.status) }}</app-badge>
                    @if (sample.status === 'Rejected') {
                      <span class="small muted block">{{ name(sample.rejectionReasonNameEn, sample.rejectionReasonNameAr) }}</span>
                    }
                  </td>
                  <td class="small">{{ when(sample.collectedAtUtc) }}</td>
                  <td class="small">{{ when(sample.receivedAtUtc) }}</td>
                  <td class="actions">
                    @if (sample.status === 'Ordered') {
                      <button type="button" class="btn sm primary" *appCan="permissions.create" [disabled]="collecting() === sample.sampleNumber" (click)="collect.emit(sample)">
                        <app-icon name="check" [size]="14" />{{ collecting() === sample.sampleNumber ? i18n.t('registration.accession.samples.collecting') : i18n.t('registration.accession.samples.collect') }}
                      </button>
                    }
                    <button type="button" class="btn sm" (click)="label.emit(sample)"><app-icon name="barcode" [size]="14" />{{ i18n.t('registration.samples.printLabel') }}</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </app-section-card>
  `,
  styles: `
    .pad { padding: var(--sp-3) var(--sp-4); }
    .scroll { overflow-x: auto; }
    .grid { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
    .grid th, .grid td { padding: var(--sp-2) var(--sp-3); border-block-end: 1px solid var(--line-soft); text-align: start; vertical-align: top; }
    .grid th { color: var(--ink-2); font-weight: 600; white-space: nowrap; }
    .block { display: block; }
    .actions { white-space: nowrap; text-align: end; }
    .actions .btn + .btn { margin-inline-start: var(--sp-1); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionSamplesComponent {
  protected readonly i18n = inject(I18nService);

  readonly samples = input<readonly SampleListItem[]>([]);
  readonly loading = input(false);
  /** The sample number whose collection is in flight (its button is disabled). */
  readonly collecting = input<string | null>(null);
  readonly collect = output<SampleListItem>();
  readonly label = output<SampleListItem>();
  readonly labelAll = output<void>();

  protected readonly permissions = SAMPLES_PERMISSIONS;

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected when(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }
}
