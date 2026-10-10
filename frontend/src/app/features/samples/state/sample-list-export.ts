import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

import { MAX_PAGE_SIZE } from '../../../core/api/paging';
import type { SampleListFilter, SampleListItem } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { CSV_MAX_ROWS, type CsvColumn, downloadCsv, toCsv } from '../data/csv-export';
import { SamplesApi } from '../data/samples.api';
import { formatDateTime, patientLabel, sampleTypeLabel, unitLabel } from '../ui/sample-format';

/** CSV export of the sample registers (intake §14): every row of the current filter, up to {@link CSV_MAX_ROWS}. */
@Injectable({ providedIn: 'root' })
export class SampleListExport {
  private readonly api = inject(SamplesApi);
  private readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);

  /** Fetches every page of the filter and saves the file; resolves with the number of rows written. */
  async exportFilter(filter: SampleListFilter, fileName: string): Promise<number> {
    const rows: SampleListItem[] = [];
    for (let page = 1; rows.length < CSV_MAX_ROWS; page++) {
      const result = await this.api.list({ ...filter, page, pageSize: MAX_PAGE_SIZE });
      rows.push(...result.items);
      if (result.items.length < MAX_PAGE_SIZE || page >= result.totalPages) {
        break;
      }
    }

    return this.exportRows(rows.slice(0, CSV_MAX_ROWS), fileName);
  }

  exportRows(rows: readonly SampleListItem[], fileName: string): number {
    downloadCsv(this.document, fileName, toCsv(rows, this.columns()));
    return rows.length;
  }

  private columns(): readonly CsvColumn<SampleListItem>[] {
    const lang = this.i18n.lang();
    const t = (key: string): string => this.i18n.t(`samples.${key}`);
    return [
      { header: t('column.sampleNumber'), value: (row) => row.sampleNumber },
      { header: t('column.accession'), value: (row) => row.accession },
      { header: t('column.patient'), value: (row) => patientLabel(row, lang) },
      { header: t('column.unit'), value: (row) => unitLabel(row, lang) },
      { header: t('column.sampleType'), value: (row) => sampleTypeLabel(row, lang) },
      { header: t('column.services'), value: (row) => row.serviceCount },
      { header: t('column.status'), value: (row) => this.i18n.t(`shared.status.${row.status}`) },
      { header: t('column.orderedAt'), value: (row) => formatDateTime(row.orderedAtUtc, lang) },
      { header: t('column.collectedAt'), value: (row) => formatDateTime(row.collectedAtUtc, lang) },
      { header: t('column.receivedAt'), value: (row) => formatDateTime(row.receivedAtUtc, lang) },
      { header: t('column.reason'), value: (row) => (lang === 'ar' ? (row.rejectionReasonNameAr ?? row.rejectionReasonNameEn) : (row.rejectionReasonNameEn ?? row.rejectionReasonNameAr)) },
      { header: t('column.note'), value: (row) => row.rejectionNote },
      { header: t('column.rejectedAt'), value: (row) => (row.rejectedAtUtc ? formatDateTime(row.rejectedAtUtc, lang) : null) },
    ];
  }
}
