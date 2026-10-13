import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { WorklistDocument, WorklistItem } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';
import { formatPrintDateTime, patientLabel, pickName, testLabel } from './result-format';

/**
 * The bench list of the worklist filter (A4 landscape; SRS CP-18 "print worklist", LDM §6): accession, sample,
 * patient, test, status, collection and expected times and an empty result box per row, so a bench can work from
 * paper. The API caps the rows (`truncated`); the sheet says so.
 */
@Component({
  selector: 'app-results-worklist-document',
  imports: [DocumentFrameComponent],
  template: `
    <div class="print-page">
      <app-document-frame
        [labNameEn]="doc().letterhead.labNameEn"
        [labNameAr]="doc().letterhead.labNameAr"
        [branch]="branch()"
        [branchEn]="doc().letterhead.branchNameEn"
        [branchAr]="doc().letterhead.branchNameAr"
        [title]="i18n.t('results.worklist.documentTitle')"
        [subtitle]="subtitle()"
        [printedBy]="doc().generatedBy"
        [printedAt]="at(doc().generatedAtUtc)"
      >
        <table>
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('results.worklist.column.accession') }}</th>
              <th scope="col">{{ i18n.t('results.worklist.column.sample') }}</th>
              <th scope="col">{{ i18n.t('results.worklist.column.patient') }}</th>
              <th scope="col">{{ i18n.t('results.worklist.column.test') }}</th>
              <th scope="col">{{ i18n.t('results.worklist.column.status') }}</th>
              <th scope="col">{{ i18n.t('results.worklist.column.collected') }}</th>
              <th scope="col">{{ i18n.t('results.worklist.column.expected') }}</th>
              <th scope="col" class="box">{{ i18n.t('results.worklist.column.result') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of doc().rows; track row.resultId) {
              <tr>
                <td class="mono">{{ row.accession }}</td>
                <td class="mono">{{ row.sampleNumber ?? '' }}</td>
                <td>{{ patient(row) }}</td>
                <td>{{ test(row) }}</td>
                <td>{{ i18n.t('results.status.' + row.status) }}</td>
                <td>{{ at(row.collectedAtUtc) }}</td>
                <td>{{ at(row.expectedAtUtc) }}</td>
                <td class="box"></td>
              </tr>
            }
          </tbody>
        </table>
        @if (doc().truncated) {
          <p class="muted">{{ i18n.t('results.worklist.documentTruncated') }}</p>
        }
        <span documentFooter>{{ i18n.tc('results.worklist.documentCount', doc().rows.length) }}</span>
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
export class WorklistDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<WorklistDocument>();

  protected readonly branch = computed(() => {
    const letterhead = this.doc().letterhead;
    const picked = pickName(letterhead.branchNameEn, letterhead.branchNameAr, this.i18n.lang());
    return picked === '—' ? null : picked;
  });

  protected readonly subtitle = computed(() => {
    const doc = this.doc();
    const parts: string[] = [];
    if (doc.fromUtc || doc.toUtc) {
      parts.push(`${this.at(doc.fromUtc)} – ${this.at(doc.toUtc)}`);
    }

    const group = pickName(doc.groupNameEn, doc.groupNameAr, this.i18n.lang());
    if (group !== '—') {
      parts.push(group);
    }

    return parts.length > 0 ? parts.join(' · ') : null;
  });

  protected patient(row: WorklistItem): string {
    return patientLabel(row, this.i18n.lang(), this.i18n.t('results.vipMasked'));
  }

  protected test(row: WorklistItem): string {
    return testLabel(row, this.i18n.lang());
  }

  protected at(value: string | null | undefined): string {
    return formatPrintDateTime(value, this.i18n.lang());
  }
}
