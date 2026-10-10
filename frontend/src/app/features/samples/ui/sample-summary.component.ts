import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SAMPLES_LINKS, type SampleDetail, type SampleStepView } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { formatDateTime, patientLabel, pickName, sampleTypeLabel, unitLabel } from './sample-format';

/** The key facts of one sample (CP-14 feedback, CP-16 lookup, CP-17 header): number, status, visit, patient, tube, steps, rejection, replacement links. */
@Component({
  selector: 'app-sample-summary',
  imports: [RouterLink, BadgeComponent],
  template: `
    @let sample = detail().summary;
    <div class="head">
      <a class="number mono" [routerLink]="links.track(sample.sampleNumber)">{{ sample.sampleNumber }}</a>
      <app-badge [status]="sample.status" />
      @if (detail().isResultEntryAllowed) {
        <span class="gate ok">{{ i18n.t('samples.summary.gateOpen') }}</span>
      } @else {
        <span class="gate">{{ i18n.t('samples.summary.gateClosed') }}</span>
      }
    </div>
    <dl class="kv">
      <dt>{{ i18n.t('samples.column.accession') }}</dt>
      <dd><a class="link mono" [routerLink]="'/samples/visits'" [queryParams]="{ accession: sample.accession }">{{ sample.accession }}</a></dd>
      <dt>{{ i18n.t('samples.column.patient') }}</dt>
      <dd>{{ patient() || '—' }}</dd>
      <dt>{{ i18n.t('samples.column.unit') }}</dt>
      <dd>{{ unit() }}</dd>
      <dt>{{ i18n.t('samples.column.sampleType') }}</dt>
      <dd>{{ type() }}</dd>
      <dt>{{ i18n.t('samples.column.services') }}</dt>
      <dd>{{ services() || '—' }}</dd>
      <dt>{{ i18n.t('samples.column.orderedAt') }}</dt>
      <dd>{{ when(sample.orderedAtUtc) }}</dd>
      @if (detail().collected; as step) {
        <dt>{{ i18n.t('samples.column.collectedAt') }}</dt>
        <dd>{{ stepText(step) }}</dd>
      }
      @if (detail().received; as step) {
        <dt>{{ i18n.t('samples.column.receivedAt') }}</dt>
        <dd>{{ stepText(step) }}</dd>
      }
      @if (detail().rejection; as rejection) {
        @if (sample.status === 'Rejected' || sample.status === 'Replaced') {
          <dt>{{ i18n.t('samples.column.reason') }}</dt>
          <dd>
            <b>{{ name(rejection.reasonNameEn, rejection.reasonNameAr) || i18n.t('samples.summary.reasonUnknown') }}</b>
            @if (rejection.note) {
              — {{ rejection.note }}
            }
            <span class="muted small"> ({{ stepText(rejection.step) }})</span>
          </dd>
        }
      }
      @if (detail().replacedBy; as link) {
        <dt>{{ i18n.t('samples.summary.replacedBy') }}</dt>
        <dd><a class="link mono" [routerLink]="links.track(link.sampleNumber)">{{ link.sampleNumber }}</a> <app-badge [status]="link.status" /></dd>
      }
      @if (detail().replaces; as link) {
        <dt>{{ i18n.t('samples.summary.replaces') }}</dt>
        <dd><a class="link mono" [routerLink]="links.track(link.sampleNumber)">{{ link.sampleNumber }}</a> <app-badge [status]="link.status" /></dd>
      }
      @if (detail().cancelledAtUtc) {
        <dt>{{ i18n.t('samples.summary.cancelled') }}</dt>
        <dd>{{ when(detail().cancelledAtUtc) }} — {{ detail().cancellationReason }}</dd>
      }
    </dl>
  `,
  styles: `
    :host {
      display: block;
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
      margin-block-end: var(--sp-3);
    }

    .number {
      font-size: var(--fs-lg);
      font-weight: 700;
      color: var(--ink);
      text-decoration: none;
    }

    .gate {
      font-size: var(--fs-xs);
      color: var(--muted);
    }

    .gate.ok {
      color: var(--ok);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SampleSummaryComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly links = SAMPLES_LINKS;

  readonly detail = input.required<SampleDetail>();

  protected readonly patient = computed(() => patientLabel(this.detail().summary, this.i18n.lang()));
  protected readonly unit = computed(() => unitLabel(this.detail().summary, this.i18n.lang()));
  protected readonly type = computed(() => sampleTypeLabel(this.detail().summary, this.i18n.lang()));
  protected readonly services = computed(() =>
    this.detail()
      .services.map((service) => service.code ?? pickName(service.nameEn, service.nameAr, this.i18n.lang()))
      .filter((text) => text.length > 0)
      .join(', '),
  );

  protected when(iso: string | null): string {
    return formatDateTime(iso, this.i18n.lang());
  }

  protected name(en: string | null, ar: string | null): string {
    return pickName(en, ar, this.i18n.lang());
  }

  protected stepText(step: SampleStepView): string {
    const branch = pickName(step.branchNameEn, step.branchNameAr, this.i18n.lang());
    return [this.when(step.atUtc), step.userName, branch].filter((part): part is string => !!part && part.length > 0).join(' · ');
  }
}
