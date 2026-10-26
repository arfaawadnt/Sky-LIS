import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { type ExportCell, type ExportTexts, type TableExport, exportText } from '../../export/table-export';

/** The captions of a printed list, already translated. */
export interface TablePrintLabels {
  readonly printedAt: string;
  /** "12 rows" in the page's language. */
  readonly rows: string;
  /** The word before the page counter ("Page" → "Page 2 / 5"). */
  readonly page: string;
}

/**
 * LIS-CR-014 T4: a list table as an A4 landscape print document — laboratory name, list title, the filters in force
 * (`subtitle`), the print date and the row count above the visible columns of the chosen rows; the header row repeats on
 * every page (print.css) and the page counter sits in the bottom margin (`@page` margin box with CSS counters). The
 * browser's print dialog turns it into a PDF ("Save as PDF"). Rendered off-screen by `PrintService.render`; its rules come
 * from `tablePrintStyles` placed in the body beside it (component styles may be gone once the off-screen render is destroyed).
 */
@Component({
  selector: 'app-table-print',
  template: `
    <article class="tp-doc">
      <header class="tp-head">
        <b class="tp-lab">{{ laboratory() }}</b>
        <h1 class="tp-title">{{ table().title }}</h1>
        @if (table().subtitle; as subtitle) {
          <p class="tp-sub">{{ subtitle }}</p>
        }
        <p class="tp-meta">
          <span>{{ labels().printedAt }}: {{ printedAt() }}</span>
          <span>{{ labels().rows }}</span>
        </p>
      </header>
      <table class="tp-table">
        <thead>
          <tr>
            @for (column of table().columns; track column.key) {
              <th scope="col" [class]="'al-' + column.align">{{ column.header }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of table().rows; track $index) {
            <tr>
              @for (column of table().columns; track column.key; let i = $index) {
                <td [class]="'al-' + column.align">{{ text(row[i]) }}</td>
              }
            </tr>
          }
        </tbody>
      </table>
    </article>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TablePrintComponent {
  readonly table = input.required<TableExport>();
  readonly laboratory = input.required<string>();
  /** Already formatted for the reader. */
  readonly printedAt = input.required<string>();
  readonly labels = input.required<TablePrintLabels>();
  readonly texts = input.required<ExportTexts>();

  protected text(cell: ExportCell): string {
    return exportText(cell, this.texts());
  }
}

function cssString(text: string): string {
  return `"${text.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/[\r\n<>]/g, ' ')}"`;
}

/** The rules of the printed list (A4 landscape width, compact cells, the page counter "Page n / m" in the bottom margin). */
export function tablePrintStyles(pageWord: string): string {
  return [
    `@page { @bottom-center { content: ${cssString(`${pageWord} `)} counter(page) " / " counter(pages); font: 9px 'Segoe UI', Tahoma, Arial, sans-serif; color: #555; } }`,
    'body.print-root.size-A4 { width: auto; }',
    '.tp-doc { padding: 0; color: #111; font-size: 10.5px; }',
    '.tp-head { border-bottom: 2px solid #0b2a5b; padding-bottom: 6px; margin-bottom: 6px; }',
    '.tp-lab { display: block; font-size: 13px; color: #0b2a5b; }',
    '.tp-title { margin: 2px 0 0; font-size: 16px; color: #0b2a5b; }',
    '.tp-sub { margin: 2px 0 0; color: #333; }',
    '.tp-meta { margin: 4px 0 0; display: flex; justify-content: space-between; gap: 12px; color: #555; font-size: 9.5px; }',
    '.tp-table { font-size: 10px; }',
    '.tp-table th, .tp-table td { padding: 2px 5px; }',
    '.tp-table .al-end { text-align: end; font-variant-numeric: tabular-nums; }',
    '.tp-table .al-center { text-align: center; }',
  ].join('\n');
}
