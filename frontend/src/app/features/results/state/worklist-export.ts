import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

import { MAX_PAGE_SIZE } from '../../../core/api/paging';
import type { WorklistItem, WorklistQuery } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { CSV_MAX_ROWS, type CsvColumn, downloadCsv, toCsv } from '../data/csv-export';
import { ResultsApi } from '../data/results.api';
import { type DurationUnits, flagSymbol, formatDuration, formatPrintDateTime, patientLabel, pickName, testLabel } from '../ui/result-format';

/** CSV export of the worklist (intake §14): every row of the current filter, up to {@link CSV_MAX_ROWS}, client side. */
@Injectable({ providedIn: 'root' })
export class WorklistExport {
  private readonly api = inject(ResultsApi);
  private readonly i18n = inject(I18nService);
  private readonly document = inject(DOCUMENT);

  /** Fetches every page of the query and saves the file; resolves with the number of rows written. */
  async exportQuery(query: WorklistQuery, fileName: string): Promise<number> {
    const rows: WorklistItem[] = [];
    for (let page = 1; rows.length < CSV_MAX_ROWS; page++) {
      const result = await this.api.worklist({ ...query, page, pageSize: MAX_PAGE_SIZE });
      rows.push(...result.items);
      if (result.items.length < MAX_PAGE_SIZE || page * MAX_PAGE_SIZE >= result.totalCount) {
        break;
      }
    }

    return this.exportRows(rows.slice(0, CSV_MAX_ROWS), fileName);
  }

  exportRows(rows: readonly WorklistItem[], fileName: string): number {
    downloadCsv(this.document, fileName, toCsv(rows, this.columns()));
    return rows.length;
  }

  private columns(): readonly CsvColumn<WorklistItem>[] {
    const lang = this.i18n.lang();
    const t = (key: string): string => this.i18n.t(`results.${key}`);
    const units: DurationUnits = { days: t('unit.days'), hours: t('unit.hours'), minutes: t('unit.minutes') };
    return [
      { header: t('worklist.column.patientNumber'), value: (row) => row.patientNumber },
      { header: t('worklist.column.accession'), value: (row) => row.accession },
      { header: t('worklist.column.sample'), value: (row) => row.sampleNumber },
      { header: t('worklist.column.patient'), value: (row) => patientLabel({ ...row, patientNumber: null }, lang, t('vipMasked')) },
      { header: t('worklist.column.age'), value: (row) => row.ageYears },
      { header: t('worklist.column.gender'), value: (row) => (row.gender ? t(`gender.${row.gender}`) : null) },
      { header: t('worklist.column.group'), value: (row) => pickName(row.groupNameEn, row.groupNameAr, lang) },
      { header: t('worklist.column.test'), value: (row) => testLabel(row, lang) },
      { header: t('worklist.column.status'), value: (row) => t(`status.${row.status}`) },
      { header: t('worklist.column.sampleStatus'), value: (row) => (row.sampleStatus ? t(`sampleStatus.${row.sampleStatus}`) : null) },
      { header: t('worklist.column.collected'), value: (row) => (row.collectedAtUtc ? formatPrintDateTime(row.collectedAtUtc, lang) : null) },
      { header: t('worklist.column.expected'), value: (row) => formatPrintDateTime(row.expectedAtUtc, lang) },
      { header: t('worklist.column.delay'), value: (row) => (row.delaySeconds > 0 ? formatDuration(row.delaySeconds, units) : null) },
      { header: t('worklist.column.consumed'), value: (row) => row.consumedPercent },
      { header: t('worklist.column.value'), value: (row) => row.textValue },
      { header: t('worklist.column.flag'), value: (row) => flagSymbol(row.flag) },
      { header: t('worklist.column.source'), value: (row) => row.source },
      { header: t('worklist.column.urgent'), value: (row) => row.urgent },
    ];
  }
}
