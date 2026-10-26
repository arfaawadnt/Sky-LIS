import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { LabTrackerServiceItem, LabTrackerWorklistDocument } from '../../../core/api/tracker.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';
import { patientName, pickName } from './tracker-format';

/**
 * The worklist report of the tracker's service rows (change request of 2026-10-26), laid out as the worklist page's bench
 * list (A4 landscape; CP-18 "print worklist"): accession, sample, patient, service with its clinical group, test status,
 * collection and expected times and an empty result box per row. The API caps the rows (`truncated`); the sheet says so.
 * Dates and times print with Latin digits in both languages, as every printed list.
 */
@Component({
  selector: 'app-tracker-worklist-document',
  imports: [DocumentFrameComponent],
  template: `
    <div class="print-page">
      <app-document-frame
        [labNameEn]="doc().letterhead.labNameEn"
        [labNameAr]="doc().letterhead.labNameAr"
        [branch]="branch()"
        [branchEn]="doc().letterhead.branchNameEn"
        [branchAr]="doc().letterhead.branchNameAr"
        [title]="i18n.t('tracker.report.title')"
        [subtitle]="subtitle()"
        [printedBy]="doc().generatedBy"
        [printedAt]="at(doc().generatedAtUtc)"
      >
        <table>
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('tracker.service.column.accession') }}</th>
              <th scope="col">{{ i18n.t('tracker.service.column.sample') }}</th>
              <th scope="col">{{ i18n.t('tracker.service.column.patient') }}</th>
              <th scope="col">{{ i18n.t('tracker.service.column.service') }}</th>
              <th scope="col">{{ i18n.t('tracker.service.column.status') }}</th>
              <th scope="col">{{ i18n.t('tracker.service.column.collected') }}</th>
              <th scope="col">{{ i18n.t('tracker.service.column.expected') }}</th>
              <th scope="col" class="box">{{ i18n.t('tracker.report.result') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of doc().rows; track row.lineId) {
              <tr>
                <td class="mono">{{ row.accession }}</td>
                <td class="mono">{{ row.sampleNumber ?? '' }}</td>
                <td>{{ patient(row) }}</td>
                <td>{{ service(row) }}</td>
                <td>{{ row.resultStatus ? i18n.t('tracker.service.status.' + row.resultStatus) : '' }}</td>
                <td>{{ at(row.collectedAtUtc) }}</td>
                <td>{{ at(row.expectedAtUtc) }}</td>
                <td class="box"></td>
              </tr>
            }
          </tbody>
        </table>
        @if (doc().truncated) {
          <p class="muted">{{ i18n.t('tracker.report.truncated') }}</p>
        }
        <span documentFooter>{{ i18n.tc('tracker.report.count', doc().rows.length, { count: doc().rows.length }) }}</span>
      </app-document-frame>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .mono {
      font-family: 'Cascadia Mono', Consolas, monospace;
    }

    .muted {
      color: #555;
    }

    th.box,
    td.box {
      width: 28mm;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrackerWorklistDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<LabTrackerWorklistDocument>();

  protected readonly branch = computed(() => {
    const letterhead = this.doc().letterhead;
    return pickName(letterhead.branchNameEn, letterhead.branchNameAr, this.i18n.lang());
  });

  /** The period read: "26/10/2026 00:00 – 27/10/2026 00:00". */
  protected readonly subtitle = computed(() => `${this.at(this.doc().fromUtc)} – ${this.at(this.doc().toUtc)}`);

  protected patient(row: LabTrackerServiceItem): string {
    return patientName(row, this.i18n);
  }

  /** "GLU · Glucose · Chemistry". */
  protected service(row: LabTrackerServiceItem): string {
    const lang = this.i18n.lang();
    return [row.serviceCode, pickName(row.serviceName, row.serviceNameAr, lang) ?? row.serviceName, pickName(row.groupNameEn ?? null, row.groupNameAr ?? null, lang)].filter((part) => !!part).join(' · ');
  }

  protected at(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : new Intl.DateTimeFormat(this.i18n.lang() === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB', { dateStyle: 'short', timeStyle: 'short' }).format(date);
  }
}
